using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Ahenk;
public static class AudioFeatureTests
{
 static byte[] ReadAll(IWaveProvider source,int frames=517){using var data=new MemoryStream();byte[] b=new byte[source.WaveFormat.BlockAlign*frames];int n;while((n=source.Read(b,0,b.Length))>0)data.Write(b,0,n);return data.ToArray();}
 static byte[] Pcm(string path){using var r=new WaveFileReader(path);return ReadAll(AudioEngine.OriginalSource(r));}
 static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
 static bool Throws(Action action){try{action();return false;}catch(Exception ex)when(ex is InvalidOperationException or InvalidDataException or NotSupportedException){return true;}}
 sealed class Bytes(byte[] data,WaveFormat format):IWaveProvider{int p;public WaveFormat WaveFormat=>format;public int Read(byte[] buffer,int offset,int count){int n=Math.Min(count,data.Length-p);Array.Copy(data,p,buffer,offset,n);p+=n;return n;}}
 static void Dsf(string path,byte[][] channels,int rate=2822400,bool lsb=true,long? samples=null)
 {
  int block=4096,groups=(channels[0].Length+block-1)/block;long length=92L+groups*block*channels.Length;
  using var w=new BinaryWriter(File.Create(path));w.Write(Encoding.ASCII.GetBytes("DSD "));w.Write(28UL);w.Write((ulong)length);w.Write(0UL);w.Write(Encoding.ASCII.GetBytes("fmt "));w.Write(52UL);w.Write(1U);w.Write(0U);w.Write((uint)channels.Length);w.Write((uint)channels.Length);w.Write((uint)rate);w.Write(lsb?1U:8U);w.Write((ulong)(samples??channels[0].Length*8L));w.Write((uint)block);w.Write(0U);w.Write(Encoding.ASCII.GetBytes("data"));w.Write((ulong)(12+groups*block*channels.Length));
  for(int g=0;g<groups;g++)foreach(var ch in channels){var b=new byte[block];ch.AsSpan(g*block,Math.Min(block,ch.Length-g*block)).CopyTo(b);w.Write(b);}
 }
 static byte[] Be(long value,int bytes){byte[] b=new byte[bytes];for(int i=0;i<bytes;i++)b[bytes-i-1]=(byte)(value>>(8*i));return b;}
 static byte[] Chunk(string name,byte[] bytes){using var s=new MemoryStream();s.Write(Encoding.ASCII.GetBytes(name));s.Write(Be(bytes.Length,8));s.Write(bytes);if(bytes.Length%2!=0)s.WriteByte(0);return s.ToArray();}
 static void Dff(string path,byte[][] channels,int rate=2822400,string compression="DSD ")
 {
  byte[] ch=Be(channels.Length,2).Concat(Encoding.ASCII.GetBytes(channels.Length==1?"C   ":"SLFTSRGT")).ToArray();
  var props=Encoding.ASCII.GetBytes("SND ").Concat(Chunk("FS  ",Be(rate,4))).Concat(Chunk("CHNL",ch)).Concat(Chunk("CMPR",Encoding.ASCII.GetBytes(compression).Concat(new byte[]{14}).Concat(Encoding.ASCII.GetBytes("not compressed")).ToArray())).ToArray();
  var data=Enumerable.Range(0,channels[0].Length).SelectMany(i=>channels.Select(c=>c[i])).ToArray();
  var body=Encoding.ASCII.GetBytes("DSD ").Concat(Chunk("FVER",Be(0x01050000,4))).Concat(Chunk("PROP",props)).Concat(Chunk(compression=="DSD "?"DSD ":"DST ",data)).ToArray();File.WriteAllBytes(path,Encoding.ASCII.GetBytes("FRM8").Concat(Be(body.Length,8)).Concat(body).ToArray());
 }
 static byte Reverse(byte b){byte v=0;for(int i=0;i<8;i++)v=(byte)((v<<1)|((b>>i)&1));return v;}
 static byte[] Oracle(byte[][] canonical)
 {
  var result=new List<byte>();for(int frame=0;frame<(canonical[0].Length+1)/2;frame++)foreach(var ch in canonical){result.Add(frame*2+1<ch.Length?ch[frame*2+1]:(byte)0x69);result.Add(ch[frame*2]);result.Add(frame%2==0?(byte)5:(byte)250);}return result.ToArray();
 }
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);var results=new List<object>();var hardware=new List<object>();string cache=Path.Combine(directory,"decode");
  void Check(bool ok,string name){results.Add(new{name,passed=ok});if(!ok)throw new Exception("FAILED: "+name);}
  try
  {
   foreach(int rate in new[]{44100,48000,96000,192000})foreach(int bits in new[]{16,24})
   {
    var format=new WaveFormat(rate,bits,2);byte[] original=new byte[format.BlockAlign*4096];new Random(rate+bits).NextBytes(original);string wav=Path.Combine(directory,$"PCM-{rate}-{bits}.wav");using(var writer=new WaveFileWriter(wav,format))writer.Write(original,0,original.Length);
    foreach(string codec in new[]{"wav","flac","alac","aiff"})
    {
     string input=codec=="wav"?wav:Path.Combine(directory,$"Şarkı-{rate}-{bits}."+(codec=="alac"?"m4a":codec));
     if(codec!="wav")await ProcessTool.Run(Paths.Tool("ffmpeg"),["-v","error","-nostdin","-y","-i",wav,"-c:a",codec=="aiff"?$"pcm_s{bits}be":codec,"-metadata","title=Özgün — Şarkı",input]);
     string before=Hash(input);var track=await MetadataReader.Read(input,default,directory);string decoded=await AudioEngine.Decode(track,cache,default);
     using var reader=new WaveFileReader(decoded);var source=AudioEngine.OriginalSource(reader);
     Check(source.WaveFormat.SampleRate==rate&&source.WaveFormat.BitsPerSample==bits&&source.WaveFormat.Channels==2,$"{codec} {rate}/{bits}: native resolution");
     Check(SHA256.HashData(ReadAll(source)).SequenceEqual(SHA256.HashData(original)),$"{codec} {rate}/{bits}: every PCM byte identical");Check(Hash(input)==before,$"{codec} {rate}/{bits}: source unchanged");
    }
   }
   foreach(int bits in new[]{8,32})
   {
    var f=new WaveFormat(96000,bits,2);byte[] data=new byte[f.BlockAlign*101];new Random(33).NextBytes(data);string wav=Path.Combine(directory,$"wide-{bits}.wav");using(var w=new WaveFileWriter(wav,f))w.Write(data,0,data.Length);var t=await MetadataReader.Read(wav,default,directory);Check(Pcm(await AudioEngine.Decode(t,cache,default)).SequenceEqual(data),$"WAV {bits}-bit PCM lossless");
   }
   foreach(var f in new[]{new WaveFormat(192000,32,2),WaveFormat.CreateIeeeFloatWaveFormat(96000,2),new WaveFormat(44100,24,1)})
   {
    var ext=AudioFormats.Extensible(f);Check(AudioFormats.SameSignalFormat(f,ext),"Extensible header preserves PCM/float subtype: "+f);var chosen=AudioFormats.SelectExact(f,x=>x.Encoding==WaveFormatEncoding.Extensible);Check(chosen.Encoding==WaveFormatEncoding.Extensible&&AudioFormats.SameSignalFormat(chosen,f),"Exact equivalent header selection: "+f);
   }
   Check(Throws(()=>AudioFormats.SelectExact(new WaveFormat(192000,24,2),_=>false)),"Unsupported native format rejects instead of resampling");Check(!AudioFormats.SameSignalFormat(new WaveFormat(44100,24,2),new WaveFormat(48000,24,2)),"Post-init guard detects rate conversion");Check(!AudioFormats.SameSignalFormat(new WaveFormat(44100,32,2),WaveFormat.CreateIeeeFloatWaveFormat(44100,2)),"Post-init guard detects integer/float conversion");
   var trackGain=new Track{Fields=[new("Vorbis / replaygain_track_gain","-6.020599913 dB"),new("REPLAYGAIN_TRACK_PEAK","0.5"),new("REPLAYGAIN_ALBUM_GAIN","-12.041199827 dB"),new("REPLAYGAIN_ALBUM_PEAK","0.8")]};var prefs=new AudioPreferences();Check(ReplayGain.Decide(trackGain,prefs).Multiplier==1,"ReplayGain defaults off / exact bypass");prefs.ReplayGain=GainMode.Track;Check(Math.Abs(ReplayGain.Decide(trackGain,prefs).Multiplier-.5)<1e-9,"Track ReplayGain tag parsed case-insensitively");prefs.ReplayGain=GainMode.Album;Check(Math.Abs(ReplayGain.Decide(trackGain,prefs).Multiplier-.25)<1e-9,"Album gain distinct from track gain");
   var boosted=new Track{Fields=[new("REPLAYGAIN_TRACK_GAIN","12 dB")]};prefs.ReplayGain=GainMode.Track;Check(ReplayGain.Decide(boosted,prefs).Multiplier==1,"Missing peak caps boost at full scale");boosted.Fields.Add(new("REPLAYGAIN_TRACK_PEAK","0.25"));Check(ReplayGain.Decide(boosted,prefs).Multiplier>3.98,"Known peak permits safe amplification");prefs.PreampDb=12;Check(ReplayGain.Decide(boosted,prefs).Multiplier==4,"Anti-clipping adjusts constant gain, not dynamics");prefs.PreampDb=0;
   prefs.ReplayGain=GainMode.Album;Check(ReplayGain.Decide(boosted,prefs).Multiplier==1&&ReplayGain.Decide(boosted,prefs).Description.Contains("bilgisi yok"),"Album mode never substitutes track gain");
   foreach(int bits in new[]{16,24,32})
   {
    int width=bits/8;int[] values=[-32768,-16384,-1,0,1,16384,32767,2222];byte[] input=values.SelectMany(v=>Enumerable.Range(0,width).Select(b=>(byte)((((long)v)<<(bits-16))>>(b*8)))).ToArray();
    var source=new GainSource(new Bytes(input,new WaveFormat(96000,bits,2)),.5);byte[] output=ReadAll(source);Check(output.Length==values.Length*4&&source.WaveFormat.SampleRate==96000&&source.WaveFormat.Channels==2,$"Gain preserves frames / channels / rate {bits}");Check(Enumerable.Range(0,values.Length).All(i=>BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(i*4))==values[i]*32768),$"Constant gain precision and channel ordering {bits}");
   }
   byte[] floatSamples=new[]{-.5f,.25f,float.NaN,float.PositiveInfinity}.SelectMany(BitConverter.GetBytes).ToArray();var floatGain=ReadAll(new GainSource(new Bytes(floatSamples,WaveFormat.CreateIeeeFloatWaveFormat(44100,2)),.5));Check(BinaryPrimitives.ReadInt32LittleEndian(floatGain)==-536870912&&BinaryPrimitives.ReadInt32LittleEndian(floatGain.AsSpan(4))==268435456&&floatGain.Skip(8).All(b=>b==0),"Float ReplayGain is finite and constant");
   string settings=Path.Combine(directory,"audio-settings.json");prefs.DopEnabled=true;prefs.DopDevices.Add("confirmed-dac");prefs.Save(settings);var restored=AudioPreferences.Load(settings);Check(restored.ReplayGain==prefs.ReplayGain&&restored.DopDevices.Contains("confirmed-dac")&&restored.Exclusive,"Audio preferences persist");Check(Throws(()=>restored.RequireDop(null))&&Throws(()=>restored.RequireDop("different-dac")),"DoP refuses default and unconfirmed hardware");restored.RequireDop("confirmed-dac");restored.DopEnabled=false;Check(Throws(()=>restored.RequireDop("confirmed-dac")),"DoP disabled overrides past device confirmation");
   using(var engine=new AudioEngine())Check(Throws(()=>engine.PlayDop("does-not-exist.dsf",null,new AudioPreferences())),"DoP permission guard runs before file/device opening");
   foreach(int rate in new[]{2822400,5644800,11289600})foreach(bool lsb in new[]{true,false})
   {
    byte[][] canonical=[Enumerable.Range(0,8201).Select(i=>(byte)(i*37+1)).ToArray(),Enumerable.Range(0,8201).Select(i=>(byte)(i*17+4)).ToArray()];string dsf=Path.Combine(directory,$"dsd-{rate}-{lsb}.dsf"),dff=Path.ChangeExtension(dsf,"dff");Dsf(dsf,canonical.Select(c=>lsb?c.Select(Reverse).ToArray():c).ToArray(),rate,lsb);Dff(dff,canonical,rate);
    foreach(string input in new[]{dsf,dff})
    {
     string hash=Hash(input);using var stream=new DopWaveStream(input);Check(stream.WaveFormat.SampleRate==rate/16&&stream.WaveFormat.BitsPerSample==24&&stream.WaveFormat.Channels==2,$"DoP carrier {Path.GetFileName(input)}");Check(ReadAll(stream,31).SequenceEqual(Oracle(canonical)),"DoP golden payload / phase / block boundary / odd tail: "+Path.GetFileName(input));stream.Position=0;byte[] first=new byte[6];stream.Read(first,0,6);stream.Position=6*200;byte[] next=new byte[6];stream.Read(next,0,6);Check(first[2]!=next[2]&&next[2]==next[5]&&next[1]==canonical[0][400]&&next[0]==canonical[0][401],"DSD seek continues marker phase and channel byte order: "+Path.GetFileName(input));Check(Hash(input)==hash,"DSD source unchanged: "+Path.GetFileName(input));
    }
   }
   string mono=Path.Combine(directory,"mono.dsf");Dsf(mono,[[0xff]],samples:3,lsb:false);using(var stream=new DopWaveStream(mono)){var data=ReadAll(stream);Check(data.SequenceEqual(new byte[]{0x69,0xe9,5}),"Partial DSD byte fills only unused bits with DSD silence");}
   string dst=Path.Combine(directory,"compressed.dff");Dff(dst,[[0,1],[2,3]],compression:"DST ");Check(Throws(()=>DsdContainer.Read(dst)),"DST compressed DFF explicitly rejected");string bad=Path.Combine(directory,"truncated.dsf");File.WriteAllBytes(bad,File.ReadAllBytes(mono)[..88]);Check(Throws(()=>DsdContainer.Read(bad)),"Truncated DSD bounds checked");var corrupt=File.ReadAllBytes(mono);BinaryPrimitives.WriteUInt64LittleEndian(corrupt.AsSpan(4),ulong.MaxValue);File.WriteAllBytes(bad,corrupt);Check(Throws(()=>DsdContainer.Read(bad)),"Overflowed DSD length rejected");string multi=Path.Combine(directory,"multi.dsf");Dsf(multi,[[1],[2],[3]]);Check(Throws(()=>DsdContainer.Read(multi)),"Multichannel DSD rejected without PCM fallback");
   var dsdt=new Track{Path=mono};bool refused=false;try{await AudioEngine.Decode(dsdt,cache,default);}catch(InvalidOperationException){refused=true;}Check(refused,"DSD never silently enters PCM decode cache");
   foreach(string extension in new[]{"dsf","dff"}){string path=Path.Combine(directory,"dsd-2822400-True."+extension);var t=await MetadataReader.Read(path,default,directory);Check(t.IsDsd&&t.DsdRate==2822400&&t.Channels==2&&Math.Abs(t.Seconds-8201*8d/2822400)<1e-9,"DSD metadata uses bit rate and actual sample count: "+extension);}
   string tagged=Path.Combine(directory,"gain-tagged.flac");await ProcessTool.Run(Paths.Tool("ffmpeg"),["-v","error","-y","-i",Path.Combine(directory,"PCM-44100-24.wav"),"-c:a","flac","-metadata","REPLAYGAIN_TRACK_GAIN=-7.5 dB","-metadata","REPLAYGAIN_TRACK_PEAK=0.9",tagged]);var taggedTrack=await MetadataReader.Read(tagged,default,directory);Check(ReplayGain.Tags(taggedTrack,GainMode.Track) is {GainDb:-7.5,Peak:.9},"Real FLAC ReplayGain metadata reaches playback policy");
   var gainStore=new ReplayGainStore(Path.Combine(directory,"gain-cache"));var tones=new List<Track>();
   foreach(int amplitude in new[]{2000,8000})
   {
    string path=Path.Combine(directory,$"gain-tone-{amplitude}.wav");using(var writer=new WaveFileWriter(path,new WaveFormat(96000,16,2)))for(int i=0;i<192000;i++){short v=(short)Math.Round(Math.Sin(2*Math.PI*997*i/96000)*amplitude);writer.Write(BitConverter.GetBytes(v),0,2);writer.Write(BitConverter.GetBytes(v),0,2);}tones.Add(new(){Path=path,Title="Tone "+amplitude,Album="Analysis"});
   }
   var hashes=tones.Select(t=>Hash(t.Path)).ToArray();await gainStore.AnalyzeAsync(tones,true,null,default);var quiet=gainStore.Get(tones[0],GainMode.Track,tones);var loud=gainStore.Get(tones[1],GainMode.Track,tones);Check(quiet!=null&&loud!=null&&Math.Abs(quiet.GainDb-loud.GainDb-12.04)<.15,"Local ReplayGain measures expected 12 dB level difference");var album1=gainStore.Get(tones[0],GainMode.Album,tones);var album2=gainStore.Get(tones[1],GainMode.Album,tones);Check(album1!=null&&album1==album2&&album1.Peak>=loud!.Peak,"Album analysis shares one gain and album maximum peak");Check(tones.Select(t=>Hash(t.Path)).SequenceEqual(hashes),"Analysis never rewrites music or metadata");
   string hybrid=Path.Combine(directory,"analysis-hybrid.wv");await ProcessTool.Run(Paths.Tool("wavpack"),["-y","-b3","-c",tones[1].Path,hybrid]);var hybridTrack=new Track{Path=hybrid};await gainStore.AnalyzeAsync([hybridTrack],false,null,default);var hybridGain=gainStore.Get(hybridTrack,GainMode.Track,[]);Check(hybridGain!=null&&Math.Abs(hybridGain.GainDb-loud!.GainDb)<.01&&Math.Abs(hybridGain.Peak-loud.Peak)<1e-6,"ReplayGain WavPack hybrid analysis includes WVC correction");string hybridKey=ReplayGainStore.TrackKey(hybridTrack);File.Move(Path.ChangeExtension(hybrid,".wvc"),Path.ChangeExtension(hybrid,".wvc.saved"),true);Check(ReplayGainStore.TrackKey(hybridTrack)!=hybridKey&&gainStore.Get(hybridTrack,GainMode.Track,[])==null,"WVC change invalidates ReplayGain analysis");
   File.WriteAllText(Path.Combine(directory,"gain-cache",ReplayGainStore.TrackKey(tones[1])+".json"),"{broken");Check(gainStore.Get(tones[1],GainMode.Track,tones)==null,"Corrupt local analysis ignored without altering music");
   Check(ReplayGainAnalysisSilence(),"Silent analysis has finite neutral gain");string key=ReplayGainStore.TrackKey(tones[0]);File.SetLastWriteTimeUtc(tones[0].Path,DateTime.UtcNow.AddSeconds(1));Check(ReplayGainStore.TrackKey(tones[0])!=key&&gainStore.Get(tones[0],GainMode.Track,tones)==null&&gainStore.Get(tones[1],GainMode.Album,tones)==null,"File change invalidates track and album analysis");
   using(var cts=new CancellationTokenSource()){cts.Cancel();bool canceled=false;try{await gainStore.AnalyzeAsync(tones,false,null,cts.Token);}catch(OperationCanceledException){canceled=true;}Check(canceled,"Local analysis cancellation stops child tools");}
   var devices=AudioEngine.Devices();foreach(var option in devices)
   {
    using var enumerator=new MMDeviceEnumerator();using var device=enumerator.GetDevice(option.Id);using var client=device.AudioClient;
    if(device.AudioMeterInformation.MasterPeakValue>.001){hardware.Add(new{device=option.Name,skipped="Endpoint already playing audio; exclusive tests not attempted"});continue;}
    foreach(var (rate,bits) in new[]{(44100,16),(44100,24),(48000,24),(96000,24),(192000,24),(44100,16)})
    {
     var f=new WaveFormat(rate,bits,2);bool supported=AudioFormats.ExactCandidates(f).Any(x=>client.IsFormatSupported(AudioClientShareMode.Exclusive,x));if(!supported){hardware.Add(new{device=option.Name,rate,bits,supported,played=false});continue;}
     string path=Path.Combine(directory,$"silent-switch-{rate}-{bits}.wav");using(var w=new WaveFileWriter(path,f)){byte[] silence=new byte[f.AverageBytesPerSecond];w.Write(silence,0,silence.Length);}
     string? failure=null;bool played=false;string signal="";try{using var engine=new AudioEngine();engine.Failed+=message=>failure=message;engine.Play(path,option.Id,true);await Task.Delay(300);played=engine.IsPlaying&&engine.Position.TotalSeconds>0;signal=engine.Signal;}catch(Exception ex){failure=ex.Message;}hardware.Add(new{device=option.Name,rate,bits,supported,played,failure,signal});Check(played&&failure==null,$"Real WASAPI Exclusive native switching {option.Name}: {rate} Hz / {bits} bit");
    }
   }
  }
  finally{await File.WriteAllTextAsync(Path.Combine(directory,"audio-feature-results.json"),JsonSerializer.Serialize(new{version=typeof(AudioEngine).Assembly.GetName().Version?.ToString(),date=DateTimeOffset.Now,passed=results.Count(r=>JsonSerializer.Serialize(r).Contains("\"passed\":true")),results,hardware,dsdHardwareOutput="Not attempted: no user-confirmed DoP DAC; protocol bytes verified only."},new JsonSerializerOptions{WriteIndented=true}));}
 }
 static bool ReplayGainAnalysisSilence(){var v=ReplayGainStore.Parse("track_gain = +64.00 dB\ntrack_peak = 0.000000");return v.GainDb==0&&v.Peak==0;}
}
