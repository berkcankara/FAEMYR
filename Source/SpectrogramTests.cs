using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.Wave;
namespace Faemyr;
public static class SpectrogramTests
{
 public static void Run(string directory,Action<bool,string> check)
 {
  var timings=new List<object>();
  foreach(int rate in new[]{44100,96000,192000})
  {
   var fft=new SpectrumAnalysis(rate,SpectrogramHistory.Bands);var signal=new float[fft.Size];var inverted=new float[fft.Size];
   foreach(int bin in new[]{8,96,500})
   {
    double frequency=bin*rate/(double)fft.Size;
    for(int i=0;i<signal.Length;i++){signal[i]=(float)(.5*Math.Sin(2*Math.PI*bin*i/fft.Size));inverted[i]=-signal[i];}
    var levels=fft.Analyze(signal,inverted,2).ToArray();int strongest=Array.IndexOf(levels,levels.Max());
    var mono=fft.Analyze(signal,inverted,1).ToArray();
    check(levels.Length==SpectrogramHistory.Bands&&Math.Abs(Math.Log(fft.BandFrequency(strongest)/frequency))<.1&&levels.Max()>-12&&levels.Zip(mono).All(pair=>Math.Abs(pair.First-pair.Second)<.00001),"Spectrogram resolves real tone position and opposite-phase stereo at "+rate+" Hz / "+frequency.ToString("0.0")+" Hz (peak "+levels.Max().ToString("0.0")+" dB)");
   }
   var watch=Stopwatch.StartNew();for(int i=0;i<30;i++)fft.Analyze(signal,inverted,2);watch.Stop();
   double average=watch.Elapsed.TotalMilliseconds/30;timings.Add(new{sampleRate=rate,fftSize=fft.Size,stereoFftMilliseconds=average});
   check(average<30,"Spectrogram FFT fits the 30 Hz analysis budget at "+rate+" Hz");
  }
  var bright=Enumerable.Repeat(0d,SpectrogramHistory.Bands).ToArray();var silence=Enumerable.Repeat(SpectrumAnalysis.FloorDb,SpectrogramHistory.Bands).ToArray();
  foreach(int fps in new[]{30,60,120})
  {
   var history=new SpectrogramHistory();for(int frame=0;frame<=fps*12;frame++)history.Update(bright,48000,frame*48000L/fps);
   check(history.FilledColumns==600&&history.Data.Length==600*128&&history.Data.ToArray().All(v=>v==255),"Spectrogram retains bounded ten-second history independently of "+fps+" fps redraw rate");
   var before=history.Data.ToArray();check(!history.Update(silence,48000,history.LastFrame)&&before.SequenceEqual(history.Data.ToArray()),"Paused or duplicate audio clocks cannot scroll spectrogram history at "+fps+" fps");
  }
  var gap=new SpectrogramHistory();gap.Update(bright,48000,0);gap.Update(bright,48000,48000);
  check(gap.Data[(SpectrogramHistory.Columns-2)*SpectrogramHistory.Bands]==0&&gap.Data[^1]==255,"Unobserved clock gaps stay empty instead of stretching stale spectral content");
  gap.Update(silence,48000,1);check(gap.FilledColumns==1&&gap.Data.ToArray().All(v=>v==0),"Backward seek discards the previous song or position history");
  gap.Update(bright,96000,1600);check(gap.FilledColumns==1&&gap.SampleRate==96000,"Sample-rate changes start a fresh correctly timed history");
  gap.Clear();check(gap.FilledColumns==0&&gap.LastFrame==-1&&gap.Data.ToArray().All(v=>v==0),"Stop or track change clears all spectrogram pixels");
  var fractional=new SpectrogramHistory();for(int frame=0;frame<=1200;frame++)fractional.Update(bright,11025,frame*11025L/120);
  check(fractional.LastFrame==110250&&fractional.FilledColumns==600,"Fractional samples per visual column do not accumulate clock drift at 11025 Hz");
  var nonfinite=Enumerable.Repeat(double.NaN,128).ToArray();gap.Update(nonfinite,48000,0);check(gap.Data.ToArray().All(v=>v==0),"Nonfinite spectral values remain silent and transparent");
  var capture=ScopeCapture.Create(new WaveFormat(44100,16,2))!;capture.Append(new byte[4410*4]);var l=new float[4096];var r=new float[4096];
  int count=capture.CopyWindow(2205,l,r,out long end,4096);check(count==2205&&end==2205,"Spectrogram clock is clamped to audible samples rather than decoder read-ahead");
  int generation=capture.Generation;capture.Clear();capture.CopyWindow(4410,l,r,out end);check(capture.Generation==generation+1&&capture.CopyWindow(4410,l,r)==0,"In-place seek invalidates visual history with a capture generation change");
  RenderFixture(directory,check);
  string language=L.Current;try
  {
   L.Apply("en");var localized=new OscilloscopeView{Mode=VisualizerMode.Spectrogram};check(AutomationProperties.GetName((MenuItem)localized.ContextMenu.Items[3])=="Spectrogram"&&AutomationProperties.GetName(localized)=="Live spectrogram","English mode name and accessibility label are localized");
  }finally{L.Apply(language);}
  File.WriteAllText(Path.Combine(directory,"spectrogram-timings.json"),JsonSerializer.Serialize(timings,new JsonSerializerOptions{WriteIndented=true}));
 }
 static void RenderFixture(string directory,Action<bool,string> check)
 {
  const int rate=44100;var capture=ScopeCapture.Create(new WaveFormat(rate,16,2))!;int size=SpectrumAnalysis.SizeForRate(rate);
  var left=new float[size];var right=new float[size];
  var view=new OscilloscopeView{Width=950,Height=90,TraceBrush=new SolidColorBrush(Color.FromRgb(166,143,255)),Mode=VisualizerMode.Spectrogram};
  view.Measure(new Size(950,90));view.Arrange(new Rect(0,0,950,90));
  void Set(string name,object value)=>typeof(OscilloscopeView).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(view,value);
  var update=typeof(OscilloscopeView).GetMethod("UpdateAnalysis",BindingFlags.NonPublic|BindingFlags.Instance)!;
  Set("capture",capture);Set("left",left);Set("right",right);Set("count",size);
  for(int frame=0;frame<=600;frame++)
  {
   if(frame%120==0)for(int i=0;i<size;i++){left[i]=(float)(.5*Math.Sin(2*Math.PI*(frame<240?8:frame<480?96:500)*i/size));right[i]=-left[i];}
   Set("scopeEndFrame",frame*rate/60L);update.Invoke(view,[false]);
  }
  RenderTargetBitmap Render(){view.InvalidateVisual();view.UpdateLayout();var image=new RenderTargetBitmap(950,90,96,96,PixelFormats.Pbgra32);image.Render(view);return image;}
  var image=Render();var pixels=new byte[950*90*4];image.CopyPixels(pixels,950*4,0);
  int opaque=0;for(int y=0;y<75;y++)for(int x=30;x<950;x++){int p=(y*950+x)*4;if(pixels[p+3]>100)opaque++;}
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(directory,"spectrogram-tones.png")))encoder.Save(file);
  double Center(int first,int last){double weighted=0,total=0;for(int y=0;y<75;y++)for(int x=first;x<last;x++){double a=pixels[(y*950+x)*4+3];weighted+=y*a;total+=a;}return total>0?weighted/total:-1;}
  double low=Center(80,300),middle=Center(430,650),high=Center(820,940);
  check(opaque>500&&low>middle+8&&middle>high+8,"Offline spectrogram places bass below mids and treble across its time axis (pixels "+opaque+", centers "+low.ToString("0.0")+" / "+middle.ToString("0.0")+" / "+high.ToString("0.0")+")");
  view.TraceBrush=new SolidColorBrush(Color.FromRgb(255,90,110));var recolored=Render();var changed=new byte[pixels.Length];recolored.CopyPixels(changed,950*4,0);
  check(!changed.SequenceEqual(pixels),"Changing the accent recolors existing history without losing its time data");
  view.Reset();var reset=Render();var cleared=new byte[pixels.Length];reset.CopyPixels(cleared,950*4,0);int energy=0;for(int y=0;y<70;y++)for(int x=30;x<950;x++)if(cleared[(y*950+x)*4+3]>100)energy++;
  check(energy==0,"Reset removes the old spectrogram image while retaining its axes");
  foreach(var (width,height) in new[]{(150d,26d),(950d,38d),(1600d,130d)}){view.Width=width;view.Height=height;view.Measure(new Size(width,height));view.Arrange(new Rect(0,0,width,height));Render();}
  check(true,"Spectrogram layout renders safely at compact and expanded player sizes");
 }
}
