using System.IO;
using System.Text.Json;
using NAudio.Wave;
using NAudio.CoreAudioApi;
namespace Ahenk;
public static class AudioSafetyTests
{
 sealed class BrokenProvider(bool nan):IWaveProvider
 {
  public WaveFormat WaveFormat=>WaveFormat.CreateIeeeFloatWaveFormat(96000,2);
  public int Read(byte[] buffer,int offset,int count){if(!nan)return count-1;BitConverter.GetBytes(float.NaN).CopyTo(buffer,offset);return count;}
 }
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);var results=new List<object>();var devices=new List<object>();
  foreach(var option in AudioEngine.Devices()){using var e=new MMDeviceEnumerator();using var d=e.GetDevice(option.Id);using var c=d.AudioClient;var f=c.MixFormat;devices.Add(new{option.Name,option.Id,format=f.ToString(),encoding=f.Encoding.ToString(),subtype=(f as WaveFormatExtensible)?.SubFormat.ToString(),f.SampleRate,f.BitsPerSample,f.Channels,f.BlockAlign});}
  foreach(var file in new DirectoryInfo(Path.Combine(Paths.Base,"data","cache")).Exists?new DirectoryInfo(Path.Combine(Paths.Base,"data","cache")).GetFiles("*.wav").OrderByDescending(f=>f.LastWriteTimeUtc).Take(2):new DirectoryInfo(Path.Combine(Paths.Data,"cache")).GetFiles("*.wav").OrderByDescending(f=>f.LastWriteTimeUtc).Take(2))
  {
   string expected=Path.Combine(directory,file.Name+".f32");await ProcessTool.Run(Paths.Tool("ffmpeg"),["-v","error","-nostdin","-y","-i",file.FullName,"-t","1","-c:a","pcm_f32le","-f","f32le",expected]);byte[] reference=await File.ReadAllBytesAsync(expected);
   foreach(int chunk in new[]{128,517,16384})
   {
    using var reader=new WaveFileReader(file.FullName);var original=AudioEngine.OriginalSource(reader);var scope=new ScopeSource(original,reader);var source=AudioEngine.SharedSource(scope);var data=new byte[reference.Length];int used=0;
    while(used<data.Length){int n=source.Read(data,used,Math.Min(chunk*source.WaveFormat.BlockAlign,data.Length-used));if(n==0)break;used+=n;}
    double maximumError=0,peak=0;int nonfinite=0;for(int p=0;p<used;p+=4){float sample=BitConverter.ToSingle(data,p),wanted=BitConverter.ToSingle(reference,p);if(!float.IsFinite(sample))nonfinite++;else{maximumError=Math.Max(maximumError,Math.Abs(sample-wanted));peak=Math.Max(peak,Math.Abs(sample));}}
    results.Add(new{file=file.Name,chunk,sourceFormat=reader.WaveFormat.ToString(),transport=source.WaveFormat.ToString(),used,expected=reference.Length,maximumError,peak,nonfinite,passed=used==reference.Length&&maximumError<1e-6&&nonfinite==0});
   }
  }
  foreach(bool nan in new[]{false,true}){bool refused=false;try{ExactWasapiOut.ValidateRead(new BrokenProvider(nan),new byte[80],80);}catch(InvalidDataException){refused=true;}results.Add(new{name=nan?"Nonfinite float rejected before endpoint buffer":"Partial frame rejected before endpoint buffer",passed=refused});if(!refused)throw new Exception("Output guard failed");}
  var soundbar=AudioEngine.Devices().SingleOrDefault(d=>d.Name.StartsWith("Soundbar",StringComparison.OrdinalIgnoreCase));
  if(soundbar.Id!=null)
  {
   using var enumerator=new MMDeviceEnumerator();using var device=enumerator.GetDevice(soundbar.Id);using var loopback=new WasapiLoopbackCapture(device);
   double peak=0;int nonfinite=0;long samples=0;object captureGate=new();
   loopback.DataAvailable+=(_,e)=>{lock(captureGate)for(int p=0;p+4<=e.BytesRecorded;p+=4){float value=BitConverter.ToSingle(e.Buffer,p);if(!float.IsFinite(value))nonfinite++;else peak=Math.Max(peak,Math.Abs(value));samples++;}};
   loopback.StartRecording();await Task.Delay(150);double baseline;lock(captureGate){baseline=peak;peak=0;}
   foreach(int rate in new[]{44100,96000,192000})
   {
    var f=new WaveFormat(rate,24,2);string path=Path.Combine(directory,$"silent-soundbar-{rate}.wav");using(var w=new WaveFileWriter(path,f)){var silence=new byte[f.AverageBytesPerSecond*2];w.Write(silence,0,silence.Length);}
    string? failure=null;using var player=new AudioEngine();player.Failed+=message=>failure=message;player.Play(path,soundbar.Id,false);await Task.Delay(250);
    bool playing=player.IsPlaying&&player.DeviceStarted;player.Toggle();bool stoppedOnPause=player.IsPaused&&!player.DeviceStarted;await Task.Delay(100);bool stablePause=!player.DeviceStarted;player.Toggle();await Task.Delay(100);bool resumed=player.IsPlaying&&player.DeviceStarted;player.Stop();bool released=!player.DeviceStarted;
    bool passed=playing&&stoppedOnPause&&stablePause&&resumed&&released&&failure==null;results.Add(new{name=$"Soundbar shared silent {rate}: endpoint start / pause / resume / release",playing,stoppedOnPause,stablePause,resumed,released,failure,passed});if(!passed)throw new Exception("Soundbar lifecycle failed: "+failure);
   }
   await Task.Delay(250);loopback.StopRecording();lock(captureGate){bool otherAudio=baseline>0.001;bool? passed=otherAudio?null:nonfinite==0&&peak<=0.001;results.Add(new{name="Soundbar loopback during exclusively silent tests",baseline,peak,nonfinite,samples,otherAudioPresent=otherAudio,passed,note=otherAudio?"Other apps already audible on endpoint; silence measurement inconclusive":"No unexpected output"});if(nonfinite>0||passed==false)throw new Exception("Soundbar silent output contains unexpected signal");}
  }
  await File.WriteAllTextAsync(Path.Combine(directory,"audio-safety-results.json"),JsonSerializer.Serialize(new{version="0.4.1",devices,results},new JsonSerializerOptions{WriteIndented=true}));
 }
}
