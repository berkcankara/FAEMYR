using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace Ahenk;
public static class UpgradeTests
{
 static byte[] Read(IWaveProvider source,int frames=517){using var memory=new MemoryStream();byte[] bytes=new byte[source.WaveFormat.BlockAlign*frames+12];int n;while((n=source.Read(bytes,6,bytes.Length-12))>0)memory.Write(bytes,6,n);return memory.ToArray();}
 static byte[] Pcm(string path){using var stream=new WaveFileReader(path);return Read(stream);}
 static async Task Wait(Func<bool> condition,int milliseconds=10000){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<milliseconds){if(condition())return;await Task.Delay(10);}throw new Exception("Test timeout");}
 sealed class Blocked(WaveFormat format):IWaveProvider
 {
  public readonly ManualResetEventSlim Entered=new(),Release=new();public WaveFormat WaveFormat=>format;
  public int Read(byte[] bytes,int offset,int count){Entered.Set();Release.Wait();Array.Clear(bytes,offset,count);return 0;}
 }
 sealed class StallAfter(WaveFormat format):IWaveProvider
 {
  int remaining=(int)(format.SampleRate*.4)*format.BlockAlign;public readonly ManualResetEventSlim Entered=new(),Release=new();public WaveFormat WaveFormat=>format;
  public int Read(byte[] bytes,int offset,int count){if(remaining==0){Entered.Set();Release.Wait();return 0;}int n=Math.Min(remaining,count);Array.Clear(bytes,offset,n);remaining-=n;return n;}
 }
 sealed class FaultSource(WaveFormat format):IWaveProvider{public WaveFormat WaveFormat=>format;public int Read(byte[] buffer,int offset,int count)=>throw new System.Runtime.InteropServices.COMException("Simulated device invalidation",unchecked((int)0x88890004));}
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);Directory.CreateDirectory(Path.Combine(Paths.Data,"cache"));var checks=new List<object>();var hardware=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  try
  {
   var fixtures=new List<Track>();var originals=new List<byte[]>();
   foreach(int bits in new[]{8,16,24,32})
   {
    var format=new WaveFormat(44100,bits,2);string wav=Path.Combine(directory,$"native-{bits}.wav");byte[] data=new byte[11025*format.BlockAlign];new Random(bits+41).NextBytes(data);using(var writer=new WaveFileWriter(wav,format))writer.Write(data,0,data.Length);
    string path=wav;if(bits is 16 or 24){path=Path.ChangeExtension(wav,".flac");await ProcessTool.Run(Paths.Tool("ffmpeg"),["-v","error","-y","-i",wav,"-c:a","flac",path]);}
    var track=await MetadataReader.Read(path,default);fixtures.Add(track);originals.Add(data);using(var decoder=new DecoderWaveStream(track))Check(Read(decoder).SequenceEqual(data),$"Streaming PCM is byte-identical at {bits} bits");
    int start=1234;using(var decoder=new DecoderWaveStream(track,TimeSpan.FromSeconds(start/44100d)))Check(Read(decoder).SequenceEqual(data.Skip(start*format.BlockAlign)),$"Streaming sample-exact seek at {bits} bits");
   }
   foreach(int i in new[]{1,2})
   {
    string wv=Path.Combine(directory,$"native-{fixtures[i].Bits}.wv");string wav=Path.Combine(directory,$"native-{fixtures[i].Bits}.wav");await ProcessTool.Run(Paths.Tool("wavpack"),["-q","-y",wav,wv]);var track=await MetadataReader.Read(wv,default);using(var decoder=new DecoderWaveStream(track))Check(Read(decoder).SequenceEqual(originals[i]),$"Native WavPack streaming is lossless at {track.Bits} bits");
    string hybrid=Path.Combine(directory,$"hybrid-{track.Bits}.wv");await ProcessTool.Run(Paths.Tool("wavpack"),["-q","-y","-b3","-c",wav,hybrid]);var h=await MetadataReader.Read(hybrid,default);using(var decoder=new DecoderWaveStream(h))Check(Read(decoder).SequenceEqual(originals[i]),$"WavPack streaming includes WVC correction at {track.Bits} bits");
    using(var decoder=new DecoderWaveStream(h,TimeSpan.FromSeconds(1234/44100d)))Check(Read(decoder).SequenceEqual(originals[i].Skip(1234*track.Channels*(track.Bits/8))),$"Hybrid WavPack sample seek at {track.Bits} bits");
   }
   string longWav=Path.Combine(directory,"long-native.wav"),longFlac=Path.Combine(directory,"long-native.flac");var longFormat=new WaveFormat(44100,16,2);var longData=new byte[110250*longFormat.BlockAlign];new Random(14).NextBytes(longData);using(var w=new WaveFileWriter(longWav,longFormat))w.Write(longData,0,longData.Length);await ProcessTool.Run(Paths.Tool("ffmpeg"),["-v","error","-y","-i",longWav,"-c:a","flac",longFlac]);var longTrack=await MetadataReader.Read(longFlac,default);using(var decoder=new DecoderWaveStream(longTrack,TimeSpan.FromSeconds(50000/44100d)))Check(Read(decoder).SequenceEqual(longData.Skip(50000*4)),"FLAC seek remains sample-exact beyond whole-second demux boundaries");
   var wrong=fixtures[1].Copy();wrong.Channels=1;bool formatRefused=false;try{using var decoder=new DecoderWaveStream(wrong);Read(decoder);}catch(InvalidDataException){formatRefused=true;}Check(formatRefused,"Actual decoder WAV channel header is checked before any PCM is exposed");
   var a=fixtures[1];var b=a.Copy();b.Title="Second";var c=a.Copy();c.Title="Third";var order=new PlaybackOrder(1);order.Reset(3,0);var initial=new PlaybackEntry(a,0,GainDecision.Off,order.Capture(),TimeSpan.Zero);
   using(var sequence=new PcmSequenceStream(initial,new([a,b,c],0,order,_=>GainDecision.Off)))
   {
    byte[] actual=Read(sequence,613),expected=originals[1].Concat(originals[1]).Concat(originals[1]).ToArray();Check(actual.SequenceEqual(expected),"Gapless output has no missing, duplicated, faded or inserted samples");Check(sequence.Boundaries.Select(p=>p.Frame).SequenceEqual(new long[]{0,11025,22050}),"Gapless boundaries preserve exact frame positions");Check(sequence.Audible(.1).Entry.QueueIndex==0&&sequence.Audible(.25).Entry.QueueIndex==1&&sequence.Audible(.51).Entry.QueueIndex==2,"Player metadata follows audible clock rather than decoder read-ahead");
   }
   using(var sequence=new PcmSequenceStream(initial,new([a,fixtures[2]],0,order,_=>GainDecision.Off)))Check(Read(sequence).SequenceEqual(originals[1])&&sequence.Boundaries.Count==1,"Incompatible output sample widths stop at a clean boundary");
   var missing=a.Copy();missing.Path=Path.Combine(directory,"missing.flac");using(var sequence=new PcmSequenceStream(initial,new([a,missing],0,order,_=>GainDecision.Off)))Check(Read(sequence).SequenceEqual(originals[1]),"Missing next file does not truncate the valid current track");
   string cuePath=Path.Combine(directory,"album.cue");await File.WriteAllTextAsync(cuePath,"PERFORMER \"Sanatçı\"\nTITLE \"CUE Albüm\"\nREM DATE 1999\nFILE \"native-16.flac\" WAVE\n TRACK 01 AUDIO\n TITLE \"Bir\"\n INDEX 01 00:00:00\n TRACK 02 AUDIO\n TITLE \"İki\"\n INDEX 00 00:00:07\n INDEX 01 00:00:10\n");var cue=await CueSheet.Read(cuePath,default);
   Check(cue.Count==2&&cue[0].Title=="Bir"&&cue[1].Title=="İki"&&cue.All(t=>t.AlbumArtist=="Sanatçı"&&t.IsCue&&t.AudioPath==a.Path),"CUE imports distinct identities, Unicode metadata and source paths");Check(cue[0].CueEndFrame==5880&&cue[1].CueStartFrame==5880,"CUE 75-fps indices map to exact sample boundaries without dropping INDEX 00 pregap");
   byte[] first,second;using(var decoder=new DecoderWaveStream(cue[0]))first=Read(decoder);using(var decoder=new DecoderWaveStream(cue[1]))second=Read(decoder);Check(first.Concat(second).SequenceEqual(originals[1]),"Adjacent CUE segments reconstruct every original PCM byte");
   order.Reset(2,0);using(var sequence=new PcmSequenceStream(new(cue[0],0,GainDecision.Off,order.Capture(),TimeSpan.Zero),new(cue,0,order,_=>GainDecision.Off)))Check(Read(sequence).SequenceEqual(originals[1]),"CUE virtual tracks join gaplessly with no extra audio");
   bool invalid=false;try{CueSheet.Parse("FILE \"a.flac\" WAVE\nTRACK 1 AUDIO\nINDEX 01 00:00:90");}catch(InvalidDataException){invalid=true;}Check(invalid,"Malformed CUE timestamps are rejected");
   bool duplicateCue=false;try{CueSheet.Parse("FILE \"a.flac\" WAVE\nTRACK 1 AUDIO\nINDEX 01 00:00:00\nTRACK 1 AUDIO\nINDEX 01 00:01:00");}catch(InvalidDataException){duplicateCue=true;}Check(duplicateCue,"Duplicate CUE track identities are rejected");
   string unicodeCue=Path.Combine(directory,"unicode.cue");await File.WriteAllTextAsync(unicodeCue,await File.ReadAllTextAsync(cuePath),Encoding.Unicode);Check((await CueSheet.Read(unicodeCue,default))[1].Title=="İki","UTF-16 BOM CUE metadata is preserved");
   var timed=LyricsService.CueLyrics(cue[1],LyricsService.FromText("[00:00.000]Own line one\n[00:00.200]Own line two","Yerel LRC")!);var timedLines=Lrc.Parse(timed?.Synced);Check(timedLines.Count==2&&timedLines[0].Seconds==0&&Math.Abs(timedLines[1].Seconds-(.2-5880/44100d))<.001,"CUE local timed lyrics follow segment time instead of source time");
   var blocked=new Blocked(new WaveFormat(48000,16,2));using(var buffered=new BufferedAudioSource(blocked,blocked.Release.Set))
   {
    Check(blocked.Entered.Wait(2000),"Decoder-stall test reaches blocking producer");var watch=Stopwatch.StartNew();Check(buffered.Read(new byte[1024],0,1024)==0&&watch.ElapsedMilliseconds<100&&!buffered.EndOfStream,"Real-time consumer never blocks on stalled decoder and does not confuse underflow with EOF");watch.Restart();buffered.Dispose();Check(watch.ElapsedMilliseconds<500,"Cancellation releases a stalled decoder promptly");
   }
   var queue=new List<int>{0,1,2,3,4};QueueEditing.Move(queue,[1,3],5);Check(queue.SequenceEqual(new[]{0,2,4,1,3}),"Queue drag preserves selected order and adjusts drop index");QueueEditing.Move(queue,[3,4],0);Check(queue.SequenceEqual(new[]{1,3,0,2,4}),"Multi-row queue move supports upward drops");order.SetShuffle(true,5,0);order.Prioritize([3,4]);Check(order.Peek(5,0,true)==3&&order.Next(5,0,true)==3&&order.Next(5,3,true)==4,"Play-next overrides shuffle while preserving selected order");var clone=order.Clone();Check(order.Peek(5,4,true)==clone.Peek(5,4,true),"Prefetch uses an independent copy of playback order");
   var history=new ListeningStore(directory);a.AddedAt=DateTimeOffset.UtcNow;a.Rating=5;history.Get(a);var query=TrackQuery.Compile("format:flac bits:16 rate:>=44100 plays:0 rating:>=4",history,out string error);Check(error.Length==0&&query(a)&&!query(fixtures[2]),"Advanced filters combine format, bits, rate, play count and rating");Check(TrackQuery.Compile("year:1990..2000",history,out _)(cue[0]),"Year range matches CUE album date");Check(!TrackQuery.Compile("unknown:1",history,out error)(a)&&error.Length>0,"Invalid advanced filters produce a clear error");
   var smart=new LocalPlaylist{SmartQuery="plays:0",SmartSort="added"};Check(PlaylistStore.Resolve(smart,[a],history).Count==1,"Smart playlist initially includes an unplayed track");history.Add(a,31,true);history.Save();Check(PlaylistStore.Resolve(smart,[a],history).Count==0&&new ListeningStore(directory).Get(a).PlayCount==1,"Smart lists update from persisted listening history");
   var edition1=a.Copy();edition1.Path=Path.Combine(directory,"edition1","song.flac");edition1.Album="Shared album";edition1.AlbumArtist="Artist";var edition2=edition1.Copy();edition2.Path=Path.Combine(directory,"edition2","song.flac");AlbumGrouping.Apply([edition1,edition2]);Check(edition1.AlbumKey!=edition2.AlbumKey&&!string.IsNullOrWhiteSpace(edition1.EditionLabel),"Separate physical album editions do not merge");var disc1=edition1.Copy();disc1.AlbumEditionKey="";disc1.Path=Path.Combine(directory,"multi","CD1","song.flac");var disc2=disc1.Copy();disc2.Path=Path.Combine(directory,"multi","CD2","song.flac");AlbumGrouping.Apply([disc1,disc2]);Check(disc1.AlbumKey==disc2.AlbumKey,"Multi-disc folders remain one album");
   string duplicate=Path.Combine(directory,"copied.flac");File.Copy(a.Path,duplicate,true);var copied=a.Copy();copied.Path=duplicate;var gone=a.Copy();gone.Path=Path.Combine(directory,"not-here.flac");var report=await LibraryHealth.Inspect([a,copied,gone],true,null,default);Check(report.Items.Count(i=>i.Kind=="Aynı dosya içeriği")==2&&report.Items.Count(i=>i.Kind=="Eksik dosya")==1,"Library health verifies duplicates and missing files");gone.FileSignature=LibraryFingerprint.Read(a.Path);gone.FileSize=new FileInfo(a.Path).Length;var moved=await LibraryHealth.FindMoved([gone],[duplicate],null,default);Check(moved.Count==1&&moved[0].Candidate==duplicate,"Unique moved-file signature yields a reviewable relink candidate");
   await Hardware(directory,checks,hardware,Check);
  }
  finally{File.WriteAllText(Path.Combine(directory,"upgrade-results.json"),JsonSerializer.Serialize(new{version=typeof(AudioEngine).Assembly.GetName().Version?.ToString(),checks,hardware},new JsonSerializerOptions{WriteIndented=true}));}
 }
 static async Task Hardware(string directory,List<object> checks,List<object> hardware,Action<bool,string> check)
 {
  foreach(var selected in AudioEngine.Devices())
  {
   using var e=new MMDeviceEnumerator();using var device=e.GetDevice(selected.Id);using var client=device.AudioClient;hardware.Add(new{selected.Name,format=client.MixFormat.ToString()});
   foreach(var rate in new[]{44100,96000,192000})
   {
    string path=Path.Combine(directory,$"silent-{Paths.Hash(selected.Id)[..8]}-{rate}.wav");var format=new WaveFormat(rate,24,2);using(var w=new WaveFileWriter(path,format)){var zero=new byte[format.AverageBytesPerSecond*3];w.Write(zero,0,zero.Length);}var track=await MetadataReader.Read(path,default);var order=new PlaybackOrder();order.Reset(1,0);using var engine=new AudioEngine();string? failure=null;engine.Failed+=message=>failure=message;engine.PlayStreaming(new(track,0,GainDecision.Off,order.Capture(),TimeSpan.Zero),new([track],0,order,_=>GainDecision.Off),selected.Id,false);await Wait(()=>engine.DeviceStarted||failure!=null);
    check(engine.DeviceStarted&&failure==null,$"Silent streaming opens the exact shared mix format: {selected.Name} / {rate}");await Task.Delay(350);var capture=engine.Scope!;var visualLeft=new float[capture.BufferSize];var visualRight=new float[capture.BufferSize];int visualFrames=engine.CopyScope(capture,visualLeft,visualRight);check(visualFrames>0&&capture.SampleRate==client.MixFormat.SampleRate&&visualLeft.Take(visualFrames).All(v=>v==0)&&visualRight.Take(visualFrames).All(v=>v==0),$"Visualizer follows actual consumed silent output frames: {selected.Name} / {rate}");var watch=Stopwatch.StartNew();engine.Toggle();check(engine.IsPaused&&!engine.DeviceStarted&&watch.ElapsedMilliseconds<250,$"Pause immediately stops endpoint: {selected.Name} / {rate}");
    engine.Position=TimeSpan.FromSeconds(.6);check(engine.IsPaused&&!engine.DeviceStarted&&Math.Abs(engine.Position.TotalSeconds-.6)<.01,$"Paused seek discards old buffers without emitting sound: {selected.Name} / {rate}");engine.Toggle();await Wait(()=>engine.DeviceStarted||failure!=null);check(engine.IsPlaying&&failure==null,$"Streaming resumes after seek: {selected.Name} / {rate}");engine.Stop();check(!engine.DeviceStarted&&!engine.IsPlaying,$"Stop releases endpoint and decoder: {selected.Name} / {rate}");
   }
   var mix=client.MixFormat;var stalled=new StallAfter(mix);using(var buffer=new BufferedAudioSource(stalled,stalled.Release.Set))using(var output=new ExactWasapiOut(device,AudioClientShareMode.Shared))
   {
    output.Init(buffer);output.Play();await Wait(()=>output.DeviceStarted&&stalled.Entered.IsSet);var watch=Stopwatch.StartNew();output.Pause();check(!output.DeviceStarted&&watch.ElapsedMilliseconds<250,"Stalled producer cannot prevent hardware pause: "+selected.Name);watch.Restart();output.Stop();check(!output.DeviceStarted&&watch.ElapsedMilliseconds<250,"Stalled producer cannot prevent hardware stop: "+selected.Name);
   }
   using(var buffer=new BufferedAudioSource(new FaultSource(mix),()=>{}))using(var output=new ExactWasapiOut(device,AudioClientShareMode.Shared))
   {
    var failed=new TaskCompletionSource<StoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);output.PlaybackStopped+=(_,args)=>failed.TrySetResult(args);output.Init(buffer);output.Play();var error=await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));check(error.Exception!=null&&!output.DeviceStarted&&output.PlaybackState==PlaybackState.Stopped,"Injected invalidation fault stops and releases render loop: "+selected.Name);
   }
   string shortPath=Path.Combine(directory,"silent-gapless.wav");var shortFormat=new WaveFormat(44100,16,2);using(var writer=new WaveFileWriter(shortPath,shortFormat))writer.Write(new byte[shortFormat.AverageBytesPerSecond/2],0,shortFormat.AverageBytesPerSecond/2);var first=await MetadataReader.Read(shortPath,default);var second=first.Copy();second.Title="Second silent track";var gapOrder=new PlaybackOrder();gapOrder.Reset(2,0);using(var player=new AudioEngine())
   {
    bool ended=false;string? error=null;player.Ended+=()=>ended=true;player.Failed+=message=>error=message;player.PlayStreaming(new(first,0,GainDecision.Off,gapOrder.Capture(),TimeSpan.Zero),new([first,second],0,gapOrder,_=>GainDecision.Off),selected.Id,false);await Wait(()=>ended||error!=null);check(error==null&&ended&&player.CurrentEntry?.QueueIndex==1&&player.OutputStartCount==1,"Two compatible tracks cross hardware boundary with one endpoint start: "+selected.Name);
   }
  }
 }
}
