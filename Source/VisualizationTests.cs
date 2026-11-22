using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Controls;

namespace Faemyr;
public static class VisualizationTests
{
 public static void Run(string directory,Action<bool,string> check)
 {
  foreach(int refresh in new[]{30,50,60,75,90,120,144,165,240})
  {
   var pacer=new VisualizerFramePacer();var frames=new List<double>();bool unique=true;
   for(int i=0;i<refresh*4;i++){var time=TimeSpan.FromSeconds(i/(double)refresh);if(pacer.Accept(time))frames.Add(time.TotalMilliseconds);unique&=!pacer.Accept(time);}
   check(Math.Abs(frames.Count/4d-Math.Min(60,refresh))<1,$"Visualizer keeps stable 60 fps target on a {refresh} Hz compositor");check(unique,$"Repeated compositor timestamps never redraw twice at {refresh} Hz");
   pacer.Reset();check(pacer.Accept(TimeSpan.Zero)&&pacer.Accept(TimeSpan.FromSeconds(10)),"Pause and long gaps restart frame scheduling without catch-up bursts: "+refresh);
  }
  var timings=new List<object>();
  foreach(int rate in new[]{44100,96000,192000})
  {
   var analyzer=new SpectrumAnalysis(rate);double frequency=rate*96.0/analyzer.Size;
   var signal=Enumerable.Range(0,analyzer.Size).Select(i=>(float)(.5*Math.Sin(2*Math.PI*96*i/analyzer.Size))).ToArray();
   var inverted=signal.Select(x=>-x).ToArray();var silence=new float[signal.Length];
   var mono=analyzer.Analyze(signal,silence,1).ToArray();int strongest=Array.IndexOf(mono,mono.Max());
   check(Math.Abs(Math.Log(analyzer.BandFrequency(strongest)/frequency))<.12&&Math.Abs(mono.Max()+6.0206)<.15,$"Spectrum detects the actual sine frequency and calibrated amplitude at {rate} Hz");
   var stereo=analyzer.Analyze(signal,inverted,2).ToArray();
   check(stereo.Zip(mono).All(pair=>Math.Abs(pair.First-pair.Second)<.00001),$"Opposite-phase stereo retains its full spectrum at {rate} Hz");
   var rightOnly=analyzer.Analyze(silence,signal,2).ToArray();
   check(Math.Abs(rightOnly.Max()+9.0309)<.15,$"Spectrum retains right-only audio with correct stereo power at {rate} Hz");
   check(analyzer.Analyze(silence,silence,2).ToArray().All(db=>db==SpectrumAnalysis.FloorDb),$"Spectrum silence produces no bars at {rate} Hz");
   var vu=VuAnalysis.Measure(signal);
   check(Math.Abs(vu.RmsDb+9.0309)<.01&&Math.Abs(vu.PeakDb+6.0206)<.01,$"VU separates true sine RMS and sample peak at {rate} Hz");
   var watch=Stopwatch.StartNew();for(int frame=0;frame<60;frame++)analyzer.Analyze(signal,inverted,2);watch.Stop();
   timings.Add(new{rate,fftSize=analyzer.Size,averageStereoFftMilliseconds=watch.Elapsed.TotalMilliseconds/60});
  }
  var transient=new float[256];transient[0]=1;var impulse=VuAnalysis.Measure(transient);
  check(impulse.PeakDb==0&&Math.Abs(impulse.RmsDb+24.0824)<.01,"VU peak identifies a full-scale transient while RMS reflects its actual energy");
  var zero=VuAnalysis.Measure(new float[100]);check(zero.RmsDb==VuAnalysis.FloorDb&&zero.PeakDb==VuAnalysis.FloorDb,"VU silence remains at the meter floor");
  check(double.IsFinite(VuAnalysis.Measure(new[]{float.NaN,float.PositiveInfinity,0f}).RmsDb),"VU safely ignores nonfinite visual samples");
  check(VuAnalysis.Smooth(0,1,.033)>1-VuAnalysis.Smooth(1,0,.033)&&VuAnalysis.Unit(-30,-60)==.5,"Meter mapping preserves the dB scale and attack/release stays bounded");
  var partial=new SpectrumAnalysis(44100);check(partial.Analyze(new[]{0f,float.NaN,0f},new[]{0f,float.PositiveInfinity,0f},2).ToArray().All(double.IsFinite),"A partial startup FFT with nonfinite samples remains safe");
  var view=new OscilloscopeView();view.Mode=VisualizerMode.Spectrum;
  check(view.ContextMenu.Items.Count==4&&view.ContextMenu.Items.Cast<MenuItem>().Count(item=>item.IsChecked)==1&&((MenuItem)view.ContextMenu.Items[1]).IsChecked,"Direct visualizer menu contains four modes and marks only the selected mode");
  view.Mode=VisualizerMode.VuMeter;check(((MenuItem)view.ContextMenu.Items[2]).IsChecked&&!((MenuItem)view.ContextMenu.Items[1]).IsChecked,"Changing mode while idle updates its menu without requiring playback");
  string path=Path.Combine(directory,"visualizer-preferences.json");
  foreach(var mode in Enum.GetValues<VisualizerMode>()){new VisualizerPreferences{Mode=mode}.Save(path);check(VisualizerPreferences.Load(path).Mode==mode,"Visualizer mode persists: "+mode);}
  File.WriteAllText(path,"{\"Mode\":\"Unsupported\"}");check(VisualizerPreferences.Load(path).Mode==VisualizerMode.Oscilloscope,"Unknown visualizer mode recovers the oscilloscope default");
  File.WriteAllText(path,"{\"Mode\":500}");check(VisualizerPreferences.Load(path).Mode==VisualizerMode.Oscilloscope,"Invalid numeric visualizer mode recovers safely");
  File.WriteAllText(path,"broken");check(VisualizerPreferences.Load(path).Mode==VisualizerMode.Oscilloscope,"Corrupt visualizer preferences do not prevent startup");
  File.WriteAllText(Path.Combine(directory,"visualizer-analysis-timings.json"),JsonSerializer.Serialize(timings,new JsonSerializerOptions{WriteIndented=true}));
 }
}
