using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.Wave;
namespace Faemyr;

public static class VisualizerStreamingTests
{
 sealed class Waiting : IWaveProvider
 {
  public WaveFormat WaveFormat=>WaveFormat.CreateIeeeFloatWaveFormat(48000,2);
  public readonly ManualResetEventSlim Entered=new(),Release=new();
  public int Read(byte[] bytes,int offset,int count){Entered.Set();Release.Wait();return 0;}
 }
 static async Task Until(Func<bool> test){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<5000){if(test())return;await Task.Delay(5);}throw new Exception("Visualizer test timeout");}
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  try
  {
   OscilloscopeTests.Run(Check);VisualizationTests.Run(directory,Check);SpectrogramTests.Run(directory,Check);Themes.Apply("dark");
   // Nonzero fixtures remain offline. No AudioClient or output device is opened here.
   foreach(int rate in new[]{44100,96000,192000})
   {
    var format=WaveFormat.CreateIeeeFloatWaveFormat(rate,2);byte[] pcm=new byte[format.AverageBytesPerSecond*6];
    for(int frame=0;frame<rate*6;frame++){float value=(float)(.5*Math.Sin(2*Math.PI*997*frame/rate));BitConverter.TryWriteBytes(pcm.AsSpan(frame*8,4),value);BitConverter.TryWriteBytes(pcm.AsSpan(frame*8+4,4),-value);}
    using(var raw=new RawSourceWaveStream(new MemoryStream(pcm),format))
    {
     var legacy=new ScopeSource(raw);using var prior=new BufferedAudioSource(legacy,()=>{});await Until(()=>prior.BufferedBytes>=format.AverageBytesPerSecond*1.9);
     byte[] quarter=new byte[format.AverageBytesPerSecond/4];Check(prior.Read(quarter,0,quarter.Length)==quarter.Length,"Legacy read-ahead reproduction primes its buffer: "+rate);await Until(()=>prior.BufferedBytes>=format.AverageBytesPerSecond*1.9);
     var old=legacy.Capture!;Check(old.CopyWindow(rate/20,new float[old.BufferSize],new float[old.BufferSize])==0,"Reproduced 0.6.0 blank visualization: audible samples overwritten by producer at "+rate);
    }
    using var input=new RawSourceWaveStream(new MemoryStream(pcm),format);var observer=AudioEngine.BufferAndObserve(input,()=>{},out var buffer);using(buffer)
    {
     await Until(()=>buffer.BufferedBytes>=format.AverageBytesPerSecond*1.9);var capture=observer.Capture!;Check(capture.FramesWritten==0,"Read-ahead alone never advances visualization timeline: "+rate);
     byte[] chunk=new byte[format.AverageBytesPerSecond/4],actual=new byte[format.AverageBytesPerSecond*4];bool bytesMatch=true,framesAvailable=true;
     int request=SpectrumAnalysis.SizeForRate(rate);float[] left=new float[Math.Max(request,(int)(rate*.3))],right=new float[left.Length];
     for(int part=0;part<16;part++)
     {
      await Until(()=>buffer.BufferedBytes>=chunk.Length);int read=observer.Read(chunk,0,chunk.Length);bytesMatch&=read==chunk.Length;chunk.CopyTo(actual,part*chunk.Length);
      await Until(()=>buffer.BufferedBytes>=format.AverageBytesPerSecond*1.9||buffer.EndOfStream);
      long audible=(part+1L)*rate/4-rate/20;int count=capture.CopyWindow(audible,left,right,request);framesAvailable&=count==request&&left.Take(count).Any(v=>Math.Abs(v)>.4f);
     }
     Check(bytesMatch&&actual.AsSpan().SequenceEqual(pcm.AsSpan(0,actual.Length)),"Visualizer preserves every PCM byte across ring wraps: "+rate);
     Check(framesAvailable&&capture.FramesWritten==rate*4L,"Audible frames remain available throughout playback despite full producer buffer: "+rate);
     var analyzer=new SpectrumAnalysis(rate);var levels=analyzer.Analyze(left.AsSpan(0,request),right.AsSpan(0,request),2).ToArray();Check(levels.Max()>-10&&VuAnalysis.Measure(left.AsSpan(0,request)).RmsDb>-10,"Spectrum and VU receive real nonzero consumed samples: "+rate);
     if(rate==44100)RenderModes(directory,capture,left,right,request,Check);
    }
   }
   var stalled=new Waiting();var tap=AudioEngine.BufferAndObserve(stalled,stalled.Release.Set,out var pending);using(pending)
   {
    Check(stalled.Entered.Wait(2000),"Stalled producer fixture is active");var watch=Stopwatch.StartNew();int read=tap.Read(new byte[4096],0,4096);Check(read==0&&!tap.EndOfStream&&tap.Buffering&&watch.ElapsedMilliseconds<100,"Observation preserves nonblocking underflow semantics instead of reporting EOF");stalled.Release.Set();await Until(()=>tap.EndOfStream);Check(tap.EndOfStream,"True EOF still propagates through visualization observer");
   }
  }
  finally{File.WriteAllText(Path.Combine(directory,"visualizer-streaming-results.json"),JsonSerializer.Serialize(new{version=typeof(AudioEngine).Assembly.GetName().Version?.ToString(),checks,hardwareOutput="None: nonzero visual fixtures were processed and rendered offline."},new JsonSerializerOptions{WriteIndented=true}));}
 }
 static void RenderModes(string directory,ScopeCapture capture,float[] left,float[] right,int count,Action<bool,string> check)
 {
  var view=new OscilloscopeView{Width=950,Height=90,TraceBrush=Brushes.MediumPurple};view.Measure(new Size(950,90));view.Arrange(new Rect(0,0,950,90));
  void Set(string field,object value)=>typeof(OscilloscopeView).GetField(field,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(view,value);
  foreach(var mode in Enum.GetValues<VisualizerMode>())
  {
   view.Mode=mode;Set("capture",capture);Set("left",left);Set("right",right);Set("count",count);typeof(OscilloscopeView).GetMethod("UpdateAnalysis",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(view,[true]);view.InvalidateVisual();view.UpdateLayout();
   if(mode==VisualizerMode.Spectrogram)for(int frame=1;frame<=600;frame++){Set("scopeEndFrame",frame*capture.SampleRate/60L);typeof(OscilloscopeView).GetMethod("UpdateAnalysis",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(view,[false]);}
   view.InvalidateVisual();view.UpdateLayout();
   var bitmap=new RenderTargetBitmap(950,90,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(directory,mode+".png")))encoder.Save(file);
   var bytes=new byte[950*90*4];bitmap.CopyPixels(bytes,950*4,0);int colored=0;for(int p=0;p<bytes.Length;p+=4)if(bytes[p+3]>100)colored++;check(colored>500,"Offline nonzero signal renders visible geometry in "+mode);
  }
 }
}
