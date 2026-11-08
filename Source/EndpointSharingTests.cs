using System.IO;
using System.Text.Json;
using NAudio.Wave;
using NAudio.CoreAudioApi;
namespace Ahenk;
public static class EndpointSharingTests
{
 sealed class Silence(WaveFormat format):IWaveProvider{public WaveFormat WaveFormat=>format;public int Read(byte[] b,int o,int c){Array.Clear(b,o,c);return c;}}
 static bool Shared(MMDevice device,out string? error){try{using var client=device.AudioClient;using var output=new ExactWasapiOut(device,AudioClientShareMode.Shared);output.Init(new Silence(client.MixFormat));error=null;return true;}catch(Exception ex){error=$"0x{ex.HResult:X8}: {ex.Message}";return false;}}
 static MMDevice TestDevice(MMDeviceEnumerator endpoints)
 {
  using var current=endpoints.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
  foreach(var option in AudioEngine.Devices().OrderBy(d=>d.Id==current.ID?0:1))
  {
   var device=endpoints.GetDevice(option.Id);using var client=device.AudioClient;
   if(device.AudioMeterInformation.MasterPeakValue<.001&&AudioFormats.ExactCandidates(new WaveFormat(44100,16,2)).Any(f=>client.IsFormatSupported(AudioClientShareMode.Exclusive,f)))return device;
   device.Dispose();
  }
  throw new InvalidOperationException("No idle endpoint supporting Exclusive 16/44.1 is available.");
 }
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<object>();void Check(bool ok,string name,string? detail=null){checks.Add(new{name,passed=ok,detail});if(!ok)throw new Exception(name+": "+detail);}
  try
  {
   using var endpoints=new MMDeviceEnumerator();using var device=TestDevice(endpoints);Check(true,"Selected idle Exclusive-capable endpoint",device.FriendlyName);
   Check(Shared(device,out string? initial),"Other shared clients can initialize before Lyra playback",initial);
   Check(device.AudioMeterInformation.MasterPeakValue<.001,"Endpoint idle before zero-only exclusive test");
   string path=Path.Combine(directory,"zero.wav");var format=new WaveFormat(44100,16,2);using(var writer=new WaveFileWriter(path,format))writer.Write(new byte[format.AverageBytesPerSecond*4],0,format.AverageBytesPerSecond*4);
   using var player=new AudioEngine();string? failure=null;player.Failed+=text=>failure=text;player.Play(path,device.ID,true);
   async Task Wait(Func<bool> ready){for(int i=0;i<300&&!ready()&&failure==null;i++)await Task.Delay(10);if(failure!=null)throw new Exception(failure);}
   await Wait(()=>player.DeviceStarted);Check(player.DeviceStarted,"Exclusive zero PCM starts");Check(!Shared(device,out var blocked),"Active Exclusive reserves the device",blocked);
   player.Toggle();double paused=player.Position.TotalSeconds;await Task.Delay(100);Check(player.IsPaused&&!player.DeviceStarted&&Math.Abs(player.Position.TotalSeconds-paused)<.02,"Pause retains position without an active stream");
   Check(Shared(device,out var afterPause),"Other apps can initialize shared output while Lyra is paused",afterPause);
   player.Position=TimeSpan.FromSeconds(1);Check(player.IsPaused&&Math.Abs(player.Position.TotalSeconds-1)<.02&&Shared(device,out _),"Seeking while paused does not reacquire Exclusive");
   using(var otherClient=device.AudioClient)using(var otherOutput=new ExactWasapiOut(device,AudioClientShareMode.Shared))
   {
    otherOutput.Init(new Silence(otherClient.MixFormat));otherOutput.Play();await Task.Delay(80);bool rejected=false;try{player.Toggle();}catch(InvalidOperationException){rejected=true;}
    Check(rejected?player.IsPaused&&!player.DeviceStarted&&Math.Abs(player.Position.TotalSeconds-1)<.02:player.IsPlaying&&player.Position.TotalSeconds>=1,"Resume respects driver arbitration and preserves position",rejected?"Device rejected Exclusive; paused position retained":"Driver permitted user-requested Exclusive resume");
    if(!rejected)player.Toggle();
   }
   File.Move(path,path+".saved");bool missing=false;try{player.Toggle();}catch(FileNotFoundException){missing=true;}finally{File.Move(path+".saved",path);}
   Check(missing&&player.IsPaused&&!player.DeviceStarted&&player.Position.TotalSeconds>=1,"Failed resume retains paused entry and position");
   player.Toggle();await Wait(()=>player.DeviceStarted);Check(player.IsPlaying&&player.Position.TotalSeconds>=1,"Resume restores position and Exclusive output");
   player.Stop();Check(Shared(device,out var afterStop),"Explicit stop releases device",afterStop);
   var track=new Track{Path=path,Bits=16,SampleRate=44100,Channels=2,SampleFormat="s16",Seconds=4};var order=new PlaybackOrder();order.Reset(1,0);
   player.PlayStreaming(new(track,0,GainDecision.Off,order.Capture(),TimeSpan.FromSeconds(1)),new([track],0,order,_=>GainDecision.Off,false),device.ID,true,true);
   Check(player.IsPaused&&!player.DeviceStarted&&Math.Abs(player.Position.TotalSeconds-1)<.02&&Shared(device,out _),"Start-paused streaming never initializes an exclusive endpoint");player.Stop();
   bool ended=false;player.Ended+=()=>ended=true;player.Play(path,device.ID,true,startPosition:TimeSpan.FromSeconds(3.4));await Wait(()=>ended);string? afterEnd=null;bool released=ended&&Shared(device,out afterEnd);Check(released,"Natural track end releases Exclusive",afterEnd??(ended?null:"End not received"));
  }
  finally{File.WriteAllText(Path.Combine(directory,"endpoint-sharing-results.json"),JsonSerializer.Serialize(new{version=typeof(AudioEngine).Assembly.GetName().Version?.ToString(),checks,hardwareSignal="Generated zero PCM only; shared probes initialize and dispose without starting output."},new JsonSerializerOptions{WriteIndented=true}));}
 }
}
