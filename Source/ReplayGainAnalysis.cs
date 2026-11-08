using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ahenk;
public sealed class GainCacheEntry
{
 public int Version{get;set;}=1;public string Key{get;set;}="";public double GainDb{get;set;}public double Peak{get;set;}
}
public sealed class ReplayGainStore(string directory)
{
 public static string TrackKey(Track t)
 {
  var file=new FileInfo(t.AudioPath);var correction=new FileInfo(Path.ChangeExtension(t.AudioPath,".wvc"));
  return Paths.Hash("rg-48k-v1|"+t.Path+"|"+(t.IsCue?t.CueStartFrame+"|"+t.CueEndFrame+"|":"")+file.Length+"|"+file.LastWriteTimeUtc.Ticks+"|"+(correction.Exists?correction.Length+":"+correction.LastWriteTimeUtc.Ticks:"none"));
 }
 static List<Track> Ordered(IEnumerable<Track> tracks)=>tracks.Where(t=>!t.IsDsd).DistinctBy(t=>t.Path,StringComparer.OrdinalIgnoreCase).OrderBy(t=>t.Disc).ThenBy(t=>t.Number).ThenBy(t=>t.Path,StringComparer.OrdinalIgnoreCase).ToList();
 public static string AlbumKey(IEnumerable<Track> tracks)=>Paths.Hash("album-v1|"+string.Join('|',Ordered(tracks).Select(TrackKey)));
 GainMeasurement? Read(string key)
 {
  try{var entry=JsonSerializer.Deserialize<GainCacheEntry>(File.ReadAllText(Path.Combine(directory,key+".json")));return entry is {Version:1}&&entry.Key==key&&double.IsFinite(entry.GainDb)&&entry.GainDb is >=-60 and <=60&&double.IsFinite(entry.Peak)&&entry.Peak is >=0 and <=128?new(entry.GainDb,entry.Peak):null;}
  catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){return null;}
 }
 public GainMeasurement? Get(Track track,GainMode mode,IEnumerable<Track> album)
 {
  if(mode==GainMode.Off||track.IsDsd)return null;
  try{return Read(mode==GainMode.Album?AlbumKey(album):TrackKey(track));}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){return null;}
 }
 void Save(string key,GainMeasurement value)
 {
  Directory.CreateDirectory(directory);string path=Path.Combine(directory,key+".json"),temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllText(temp,JsonSerializer.Serialize(new GainCacheEntry{Key=key,GainDb=value.GainDb,Peak=value.Peak}));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
 static Process Start(IEnumerable<string> args,bool input,bool output,string tool="ffmpeg")
 {
  var start=new ProcessStartInfo(Paths.Tool(tool)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=input,RedirectStandardOutput=output,RedirectStandardError=true};foreach(string arg in args)start.ArgumentList.Add(arg);return Process.Start(start)??throw new IOException("ReplayGain analizi başlatılamadı.");
 }
 static void Kill(Process p){try{if(!p.HasExited)p.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
 public static GainMeasurement Parse(string log)
 {
  double Value(string key){var match=Regex.Matches(log,key+@"\s*=\s*([+-]?[0-9]+(?:\.[0-9]+)?)",RegexOptions.CultureInvariant).LastOrDefault();return match!=null&&double.TryParse(match.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out double v)&&double.IsFinite(v)?v:throw new IOException("ReplayGain ölçüm sonucu okunamadı.");}
  double gain=Value("track_gain"),peak=Value("track_peak");
  var original=Regex.Matches(log,@"Peak level dB:\s*([+-]?[0-9]+(?:\.[0-9]+)?)").LastOrDefault();if(original!=null&&double.TryParse(original.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var db))peak=Math.Max(peak,Math.Pow(10,db/20));
  if(peak==0)return new(0,0);if(gain is <-60 or >60||peak is <0 or >128)throw new IOException("ReplayGain ölçümü desteklenen aralık dışında.");return new(gain,peak);
 }
 public async Task AnalyzeAsync(IEnumerable<Track> tracks,bool album,IProgress<string>? progress,CancellationToken ct)
 {
  var songs=Ordered(tracks);if(songs.Count==0)throw new InvalidOperationException("Analiz için PCM parça gerekli.");
  var keys=songs.Select(TrackKey).ToArray();string albumKey=AlbumKey(songs);var measurements=new List<GainMeasurement>();
  using var combined=album?Start(["-hide_banner","-nostats","-nostdin","-filter_threads","1","-f","f32le","-ar","48000","-ac","2","-i","pipe:0","-af","replaygain","-f","null","-"],true,false):null;
  using var combinedCancel=ct.Register(()=>{if(combined!=null)Kill(combined);});var albumLog=combined?.StandardError.ReadToEndAsync(ct);
  try
  {
   for(int i=0;i<songs.Count;i++)
   {
    ct.ThrowIfCancellationRequested();var song=songs[i];progress?.Report($"{i+1}/{songs.Count} · {song.Title}");
    var inputSong=song;if(song.IsCue){string wav=await AudioEngine.Decode(song,Path.Combine(Paths.Data,"cache"),ct);inputSong=song.Copy();inputSong.Path=wav;inputSong.SourcePath="";inputSong.CueSheetPath="";} bool wv=Path.GetExtension(inputSong.Path).Equals(".wv",StringComparison.OrdinalIgnoreCase);
    using var decoder=Start(["-hide_banner","-nostats","-nostdin","-threads","1","-filter_threads","1","-i",wv?"pipe:0":inputSong.Path,"-map","0:a:0","-vn","-ar","48000","-ac","2","-af","astats=measure_perchannel=none:measure_overall=Peak_level:reset=0,aformat=sample_rates=48000:channel_layouts=stereo:sample_fmts=flt,replaygain","-c:a","pcm_f32le","-f","f32le","pipe:1"],wv,true);
    using var unpack=wv?Start(["-q","-w",inputSong.Path,"-"],false,true,"wvunpack"):null;
    using var cancel=ct.Register(()=>{Kill(decoder);if(unpack!=null)Kill(unpack);});var log=decoder.StandardError.ReadToEndAsync(ct);var unpackLog=unpack?.StandardError.ReadToEndAsync(ct);
    async Task Pump(){if(unpack==null)return;try{await unpack.StandardOutput.BaseStream.CopyToAsync(decoder.StandardInput.BaseStream,ct);}finally{decoder.StandardInput.Close();}}
    var pumping=Pump();
    try{await decoder.StandardOutput.BaseStream.CopyToAsync(combined?.StandardInput.BaseStream??Stream.Null,ct);await pumping;await decoder.WaitForExitAsync(ct);if(unpack!=null)await unpack.WaitForExitAsync(ct);string report=await log;if(unpackLog!=null)await unpackLog;if(decoder.ExitCode!=0||unpack?.ExitCode is not (null or 0))throw new IOException("Parça analiz edilemedi: "+song.Title);measurements.Add(Parse(report));}
    finally{Kill(decoder);if(unpack!=null)Kill(unpack);try{await pumping;}catch{}}
   }
   GainMeasurement? albumValue=null;
   if(combined!=null){combined.StandardInput.Close();await combined.WaitForExitAsync(ct);string report=await albumLog!;if(combined.ExitCode!=0)throw new IOException("Albüm analizi tamamlanamadı.");var value=Parse(report);albumValue=value with{Peak=Math.Max(value.Peak,measurements.Max(m=>m.Peak))};}
   ct.ThrowIfCancellationRequested();if(!songs.Select(TrackKey).SequenceEqual(keys))throw new IOException("Analiz sırasında dosyalar değişti; sonuç kaydedilmedi.");
   // Publish only after the full scan succeeds; never rewrite the music or its tags.
   for(int i=0;i<songs.Count;i++)Save(keys[i],measurements[i]);if(albumValue!=null)Save(albumKey,albumValue);
  }catch(Exception)when(ct.IsCancellationRequested){throw new OperationCanceledException(ct);}
  finally{if(combined!=null)Kill(combined);}
 }
}
