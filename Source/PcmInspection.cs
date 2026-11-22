using System.IO;
using System.Text.Json;
namespace Faemyr;
public static class PcmInspection
{
 public static async Task Run(string directory,string path)
 {
  Directory.CreateDirectory(directory);var track=await MetadataReader.Read(path,default,directory);double level=.15333333333333338;
  string reference=Path.Combine(directory,"reference.f32");await ProcessTool.Run(Paths.Tool("ffmpeg"),["-v","error","-nostdin","-y","-i",path,"-t","3","-af","volume="+level.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+":precision=double","-c:a","pcm_f32le","-f","f32le",reference]);byte[] expected=File.ReadAllBytes(reference);
  using var decoder=new DecoderWaveStream(track);var source=AudioEngine.SharedSource(new VolumeSource(decoder,level));byte[] actual=new byte[expected.Length];int used=0;
  while(used<actual.Length){byte[] block=new byte[Math.Min(source.WaveFormat.BlockAlign*517,actual.Length-used)];int read=ExactWasapiOut.ValidateRead(source,block,block.Length);if(read==0)break;Array.Copy(block,0,actual,used,read);used+=read;}
  double maximumError=0,peak=0;int nonfinite=0;for(int p=0;p<used;p+=4){float value=BitConverter.ToSingle(actual,p),wanted=BitConverter.ToSingle(expected,p);if(!float.IsFinite(value))nonfinite++;else{peak=Math.Max(peak,Math.Abs(value));maximumError=Math.Max(maximumError,Math.Abs(value-wanted));}}
  bool passed=used==expected.Length&&nonfinite==0&&maximumError<2e-6;File.WriteAllText(Path.Combine(directory,"pcm-inspection.json"),JsonSerializer.Serialize(new{track.Title,track.Bits,track.SampleRate,track.Channels,level,used,expected=expected.Length,maximumError,peak,nonfinite,passed,outputStarted=false},new JsonSerializerOptions{WriteIndented=true}));if(!passed)throw new InvalidDataException("Offline PCM comparison failed.");
 }
}
