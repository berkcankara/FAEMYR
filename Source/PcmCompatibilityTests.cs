using System.IO;
using System.Text.Json;
using NAudio.Wave;
using NAudio.CoreAudioApi;
namespace Ahenk;
public static class PcmCompatibilityTests
{
 sealed class Bytes(byte[] data,WaveFormat format):IWaveProvider
 {
  int position;public WaveFormat WaveFormat=>format;
  public int Read(byte[] buffer,int offset,int count){int n=Math.Min(count,data.Length-position);n-=n%format.BlockAlign;Array.Copy(data,position,buffer,offset,n);position+=n;return n;}
 }
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<object>();
  void Check(bool passed,string name){checks.Add(new{name,passed});if(!passed)throw new Exception(name);}
  try
  {
   var prefs=new AudioPreferences();Check(prefs.PcmRate("optical",192000)==192000,"No automatic resampling by default");prefs.PcmRateLimits["optical"]=96000;
   Check(prefs.PcmRate("optical",192000)==96000&&prefs.PcmRate("optical",44100)==44100&&prefs.PcmRate("dac",192000)==192000,"Ceiling never upsamples and affects only chosen physical device");
   var copied=prefs.Copy();copied.PcmRateLimits["optical"]=48000;Check(prefs.PcmRate("optical",192000)==96000,"Settings draft and playback request own independent dictionaries");
   string settings=Path.Combine(directory,"prefs.json");prefs.Save(settings);Check(AudioPreferences.Load(settings).PcmRate("optical",192000)==96000,"Device ceiling persists");
   File.WriteAllText(settings,"{\"PcmRateLimits\":{\"x\":0,\"y\":192000,\"z\":48000}}");var loaded=AudioPreferences.Load(settings);Check(loaded.PcmRateLimits.Count==1&&loaded.PcmRate("z",96000)==48000,"Unsupported and corrupt rate ceilings ignored");
   foreach(int destinationRate in new[]{48000,96000})foreach(int bits in new[]{16,24})
   {
    var inputFormat=new WaveFormat(192000,bits,2);byte[] input=new byte[inputFormat.AverageBytesPerSecond];int width=bits/8;
    for(int frame=0;frame<192000;frame++)for(int channel=0;channel<2;channel++){int value=channel==0?1<<(bits-4):-(1<<(bits-5));for(int b=0;b<width;b++)input[frame*inputFormat.BlockAlign+channel*width+b]=(byte)(value>>(8*b));}
    var original=input.ToArray();using var converted=AudioEngine.CompatibilitySource(new Bytes(input,inputFormat),new WaveFormat(destinationRate,bits,2));using var data=new MemoryStream();byte[] block=new byte[converted.WaveFormat.BlockAlign*2048];int read;
    while((read=ExactWasapiOut.ValidateRead(converted,block,block.Length))>0)data.Write(block,0,read);
    byte[] result=data.ToArray();int frames=result.Length/converted.WaveFormat.BlockAlign;Check(Math.Abs(frames-destinationRate)<256&&input.SequenceEqual(original),$"{bits}-bit 192 → {destinationRate}: real conversion, duration and original bytes preserved");
    bool values=true;for(int frame=256;frame<frames-256;frame++)for(int channel=0;channel<2;channel++){int p=frame*converted.WaveFormat.BlockAlign+channel*width;int sample=width==2?BitConverter.ToInt16(result,p):(result[p]|result[p+1]<<8|result[p+2]<<16)<<8>>8;double normalized=sample/Math.Pow(2,bits-1);values&=Math.Abs(normalized-(channel==0?.125:-.0625))<.002;}
    Check(values,$"{bits}-bit {destinationRate}: PCM sign, channel order and amplitude verified offline");
   }
   var soundbar=AudioEngine.Devices().SingleOrDefault(d=>d.Name.StartsWith("Soundbar",StringComparison.OrdinalIgnoreCase));
   if(soundbar.Id==null)throw new Exception("Soundbar not connected; hardware verification required");
   using(var enumerator=new MMDeviceEnumerator())using(var device=enumerator.GetDevice(soundbar.Id))Check(device.AudioMeterInformation.MasterPeakValue<.001,"Endpoint idle before zero-only hardware tests");
   foreach(var (sourceRate,limit) in new[]{(192000,96000),(192000,48000),(44100,96000),(192000,0)})
   {
    int bits=sourceRate==44100?16:24;string path=Path.Combine(directory,$"zero-{sourceRate}-{limit}.wav");var format=new WaveFormat(sourceRate,bits,2);using(var writer=new WaveFileWriter(path,format)){byte[] zero=new byte[format.AverageBytesPerSecond];for(int i=0;i<6;i++)writer.Write(zero,0,zero.Length);}
    var track=await MetadataReader.Read(path,default,directory);var order=new PlaybackOrder();order.Reset(1,0);var options=new AudioPreferences();if(limit>0)options.PcmRateLimits[soundbar.Id]=limit;
    using var engine=new AudioEngine();string? failure=null;engine.Failed+=text=>failure=text;engine.PlayStreaming(new(track,0,GainDecision.Off,order.Capture(),TimeSpan.Zero),new([track],0,order,_=>GainDecision.Off,false),soundbar.Id,true,preferences:options);
    async Task Started(){for(int i=0;i<150&&!engine.DeviceStarted&&failure==null;i++)await Task.Delay(20);if(failure!=null)throw new Exception(failure);}
    await Started();await Task.Delay(200);int expected=limit>0?Math.Min(sourceRate,limit):sourceRate;using var endpoints=new MMDeviceEnumerator();using var endpoint=endpoints.GetDevice(soundbar.Id);using var client=endpoint.AudioClient;bool guarded=AutomaticAudioOutput.RequiresOpticalPcmGuard(AutomaticAudioOutput.FormFactor(endpoint),format);if(guarded)expected=client.MixFormat.SampleRate;int expectedBits=guarded?client.MixFormat.BitsPerSample:bits;
    Check(engine.DeviceStarted&&engine.OutputFormat?.SampleRate==expected&&engine.OutputFormat.BitsPerSample==expectedBits,$"Zero-only Exclusive {sourceRate}/{bits}, cap {limit}: actual safe destination format");
    Check(engine.Signal.Contains("bit-perfect değil")== (guarded||expected!=sourceRate),"Signal truthfully reports resampling: "+sourceRate+"/"+limit);
    engine.Toggle();double paused=engine.Position.TotalSeconds;await Task.Delay(100);Check(engine.IsPaused&&!engine.DeviceStarted&&Math.Abs(engine.Position.TotalSeconds-paused)<.02,"Pause stops endpoint and freezes clock: "+sourceRate+"/"+limit);
    engine.Toggle();await Started();Check(engine.DeviceStarted,"Resume restarts endpoint: "+sourceRate+"/"+limit);
    engine.Position=TimeSpan.FromSeconds(2);await Started();Check(engine.OutputFormat?.SampleRate==expected&&engine.Position.TotalSeconds>=1.9,"Seek retains device ceiling: "+sourceRate+"/"+limit);
    engine.Stop();Check(!engine.DeviceStarted&&failure==null,"Stop releases endpoint: "+sourceRate+"/"+limit);
   }
  }
  finally{File.WriteAllText(Path.Combine(directory,"pcm-compatibility-results.json"),JsonSerializer.Serialize(new{version=typeof(AudioEngine).Assembly.GetName().Version?.ToString(),checks,hardwareSignal="Generated zero PCM only; no music, tones or DSD sent to hardware. Audible optical reception requires user confirmation."},new JsonSerializerOptions{WriteIndented=true}));}
 }
}
