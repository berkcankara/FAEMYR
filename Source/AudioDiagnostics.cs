using System.IO;
using System.Text.Json;
using NAudio.Wave;
using NAudio.CoreAudioApi;
namespace Faemyr;
public static class AudioDiagnostics
{
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);var results=new List<object>();using var enumerator=new MMDeviceEnumerator();using var device=enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
  foreach(var rate in new[]{44100,48000,96000})foreach(var bits in new[]{16,24,32})
  {
   string wav=System.IO.Path.Combine(directory,$"silence-{rate}-{bits}.wav");
   await ProcessTool.Run(Paths.Tool("ffmpeg"),["-v","error","-y","-f","lavfi","-i",$"anullsrc=r={rate}:cl=stereo","-t","2","-c:a",$"pcm_s{bits}le",wav]);
   string? failure=null;string signal="";bool advanced=false;int scopeFrames=0;bool scopeSilent=false;
   try{using var player=new AudioEngine();player.Failed+=message=>failure=message;player.Play(wav,null,false);await Task.Delay(400);advanced=player.IsPlaying&&player.Position.TotalMilliseconds>0;signal=player.Signal;
    if(player.Scope is {} capture){var left=new float[capture.BufferSize];var right=new float[capture.BufferSize];scopeFrames=player.CopyScope(capture,left,right);scopeSilent=left.Take(scopeFrames).Concat(right.Take(scopeFrames)).All(value=>value==0);}
   }
   catch(Exception ex){failure=ex.ToString();}
   results.Add(new{rate,bits,passed=failure==null&&advanced&&scopeFrames>0&&scopeSilent,advanced,scopeFrames,scopeSilent,failure,signal});
  }
  await File.WriteAllTextAsync(System.IO.Path.Combine(directory,"audio-results.json"),JsonSerializer.Serialize(new{device=device.FriendlyName,mix=device.AudioClient.MixFormat.ToString(),results},new JsonSerializerOptions{WriteIndented=true}));
 }
}
