using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Faemyr;

public enum VisualizerMode { Oscilloscope,Spectrum,VuMeter,Spectrogram }
public sealed class VisualizerPreferences
{
 static readonly JsonSerializerOptions options=new(){WriteIndented=true,Converters={new JsonStringEnumConverter()}};
 public VisualizerMode Mode{get;set;}
 public static VisualizerPreferences Load(string path)
 {
  try{var value=JsonSerializer.Deserialize<VisualizerPreferences>(File.ReadAllText(path),options)??new();if(!Enum.IsDefined(value.Mode))value.Mode=VisualizerMode.Oscilloscope;return value;}
  catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){return new();}
 }
 public void Save(string path)
 {
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllText(temp,JsonSerializer.Serialize(this,options));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
}

// Independent channel powers avoid cancellation when stereo channels have opposite phase.
public sealed class SpectrumAnalysis
{
 public const int BandCount=64;
 public const double FloorDb=-72;
 readonly double[] real,imaginary,window,power,levels;
 readonly double normalization;
 public int SampleRate{get;}
 public int Size=>real.Length;
 public double MaximumFrequency=>Math.Min(20000,SampleRate/2.0);
 public static int SizeForRate(int sampleRate){int size=512;while(size<sampleRate*.085&&size<32768)size*=2;return size;}
 public double BandFrequency(int index)=>20*Math.Pow(MaximumFrequency/20,(index+.5)/levels.Length);
 public SpectrumAnalysis(int sampleRate,int bands=BandCount)
 {
  SampleRate=sampleRate;levels=new double[Math.Clamp(bands,1,256)];int size=SizeForRate(sampleRate);real=new double[size];imaginary=new double[size];window=new double[size];power=new double[size/2+1];
  double sum=0;for(int i=0;i<size;i++){window[i]=.5-.5*Math.Cos(2*Math.PI*i/(size-1));sum+=window[i];}normalization=4/(sum*sum);
 }
 public ReadOnlySpan<double> Analyze(ReadOnlySpan<float> left,ReadOnlySpan<float> right,int channels)
 {
  Array.Clear(power);Transform(left);if(channels>1)Transform(right);
  double divisor=channels>1?2:1;
  for(int band=0;band<levels.Length;band++)
  {
   double lower=20*Math.Pow(MaximumFrequency/20,band/(double)levels.Length)*Size/SampleRate;
   double upper=20*Math.Pow(MaximumFrequency/20,(band+1.0)/levels.Length)*Size/SampleRate;
   int first=Math.Max(1,(int)Math.Floor(lower)),last=Math.Min(Size/2,(int)Math.Ceiling(upper));double peak=0;
   for(int bin=first;bin<=last;bin++)
   {
    double overlap=Math.Max(0,Math.Min(upper,bin+.5)-Math.Max(lower,bin-.5));peak=Math.Max(peak,power[bin]*Math.Min(1,overlap)/divisor);
   }
   levels[band]=peak>0?Math.Clamp(10*Math.Log10(peak),FloorDb,0):FloorDb;
  }
  return levels;
 }
 void Transform(ReadOnlySpan<float> samples)
 {
  Array.Clear(real);Array.Clear(imaginary);int count=Math.Min(Size,samples.Length),offset=Size-count;
  for(int i=0;i<count;i++){float sample=samples[samples.Length-count+i];real[offset+i]=(float.IsFinite(sample)?sample:0)*window[offset+i];}
  for(int i=1,j=0;i<Size;i++)
  {
   int bit=Size>>1;for(;(j&bit)!=0;bit>>=1)j^=bit;j^=bit;
   if(i<j)(real[i],real[j])=(real[j],real[i]);
  }
  for(int length=2;length<=Size;length*=2)
  {
   double angle=-2*Math.PI/length,stepReal=Math.Cos(angle),stepImaginary=Math.Sin(angle);int half=length/2;
   for(int start=0;start<Size;start+=length)
   {
    double wr=1,wi=0;
    for(int i=0;i<half;i++)
    {
     int a=start+i,b=a+half;double tr=wr*real[b]-wi*imaginary[b],ti=wr*imaginary[b]+wi*real[b];
     real[b]=real[a]-tr;imaginary[b]=imaginary[a]-ti;real[a]+=tr;imaginary[a]+=ti;
     double next=wr*stepReal-wi*stepImaginary;wi=wr*stepImaginary+wi*stepReal;wr=next;
    }
   }
  }
  for(int i=0;i<power.Length;i++)power[i]+=(real[i]*real[i]+imaginary[i]*imaginary[i])*normalization;
 }
}

public readonly record struct VuLevel(double RmsDb,double PeakDb);
public static class VuAnalysis
{
 public const double FloorDb=-60;
 public static VuLevel Measure(ReadOnlySpan<float> samples)
 {
  if(samples.Length==0)return new(FloorDb,FloorDb);double energy=0,peak=0;
  foreach(float sample in samples){double value=float.IsFinite(sample)?Math.Abs(sample):0;energy+=value*value;peak=Math.Max(peak,value);}
  double Db(double amplitude)=>amplitude>0?Math.Clamp(20*Math.Log10(amplitude),FloorDb,0):FloorDb;
  return new(Db(Math.Sqrt(energy/samples.Length)),Db(peak));
 }
 public static double Unit(double db,double floor)=>Math.Clamp((db-floor)/-floor,0,1);
 public static double Smooth(double current,double target,double seconds)=>current+(target-current)*(1-Math.Exp(-Math.Clamp(seconds,0,.1)/(target>current?.045:.24)));
}

public sealed partial class OscilloscopeView
{
 public static readonly DependencyProperty ModeProperty=DependencyProperty.Register(nameof(Mode),typeof(VisualizerMode),typeof(OscilloscopeView),new FrameworkPropertyMetadata(VisualizerMode.Oscilloscope,FrameworkPropertyMetadataOptions.AffectsRender,ModeChanged),value=>Enum.IsDefined((VisualizerMode)value));
 public VisualizerMode Mode{get=>(VisualizerMode)GetValue(ModeProperty);set=>SetValue(ModeProperty,value);}
 public event EventHandler? ModeSelected;
 readonly Dictionary<VisualizerMode,MenuItem> modeItems=[];
 readonly Dictionary<VisualizerMode,TextBlock> modeChecks=[];
 readonly Dictionary<string,FormattedText> labels=[];
 double labelDpi;
 SpectrumAnalysis? spectrum;
 readonly double[] spectrumBars=new double[SpectrumAnalysis.BandCount],spectrumPeaks=new double[SpectrumAnalysis.BandCount],spectrumHolds=new double[SpectrumAnalysis.BandCount];
 readonly double[] vuBars=new double[2],vuPeaks=new double[2],vuHolds=new double[2];
 readonly VuLevel[] vuLevels=[new(VuAnalysis.FloorDb,VuAnalysis.FloorDb),new(VuAnalysis.FloorDb,VuAnalysis.FloorDb)];
 long analysisStamp;
 void InitializeMenu()
 {
  ContextMenu=new ContextMenu();ToolTip=L.T("Sağ tıkla: Osiloskop, Spectrum, VU metre veya Spektrogram");
  foreach(var (mode,title) in new[]{(VisualizerMode.Oscilloscope,L.T("Osiloskop")),(VisualizerMode.Spectrum,"Spectrum"),(VisualizerMode.VuMeter,L.T("VU metre")),(VisualizerMode.Spectrogram,L.T("Spektrogram"))})
  {
   var header=new StackPanel{Orientation=Orientation.Horizontal};
   var check=new TextBlock{Text="✓",Width=20,Visibility=Visibility.Hidden};check.SetResourceReference(TextBlock.ForegroundProperty,"Accent");header.Children.Add(check);header.Children.Add(new TextBlock{Text=title});modeChecks[mode]=check;
   var item=new MenuItem{Header=header,IsCheckable=true};System.Windows.Automation.AutomationProperties.SetName(item,title);modeItems[mode]=item;ContextMenu.Items.Add(item);
   item.Click+=(_,_)=>{Mode=mode;SyncModeMenu();};
  }
  ContextMenuOpening+=(_,_)=>SyncModeMenu();SyncModeMenu();
 }
 void SyncModeMenu(){foreach(var item in modeItems){item.Value.IsChecked=item.Key==Mode;modeChecks[item.Key].Visibility=item.Value.IsChecked?Visibility.Visible:Visibility.Hidden;}}
 static void ModeChanged(DependencyObject sender,DependencyPropertyChangedEventArgs e)
 {
  var view=(OscilloscopeView)sender;view.ResetAnalysis();view.SyncModeMenu();view.ReadFrame(true);
  System.Windows.Automation.AutomationProperties.SetName(view,L.T(view.Mode switch{VisualizerMode.Spectrum=>"Canlı spectrum",VisualizerMode.VuMeter=>"Canlı VU metre",VisualizerMode.Spectrogram=>"Canlı spektrogram",_=>"Canlı osiloskop"}));
  view.ModeSelected?.Invoke(view,EventArgs.Empty);
 }
 void ResetAnalysis()
 {
  Array.Clear(spectrumBars);Array.Clear(spectrumPeaks);Array.Clear(spectrumHolds);Array.Clear(vuBars);Array.Clear(vuPeaks);Array.Clear(vuHolds);
  vuLevels[0]=vuLevels[1]=new(VuAnalysis.FloorDb,VuAnalysis.FloorDb);analysisStamp=0;
  ResetSpectrogram();
 }
 static void HoldPeak(ref double peak,ref double hold,double value,double seconds,bool immediate)
 {
  if(immediate||value>=peak){peak=value;hold=.65;}
  else if(hold>0)hold-=seconds;
  else peak=Math.Max(value,peak-seconds*.35);
 }
 void UpdateAnalysis(bool immediate)
 {
  long now=Stopwatch.GetTimestamp();double seconds=analysisStamp==0?1.0/30:Math.Clamp((now-analysisStamp)/(double)Stopwatch.Frequency,0,.1);analysisStamp=now;
  if(count<2||capture==null){ResetAnalysis();return;}
  if(Mode==VisualizerMode.Spectrogram)UpdateSpectrogram();
  else if(Mode==VisualizerMode.Spectrum)
  {
   if(spectrum?.SampleRate!=capture.SampleRate)spectrum=new SpectrumAnalysis(capture.SampleRate);
   var values=spectrum.Analyze(left.AsSpan(0,count),right.AsSpan(0,count),capture.Channels);
   for(int i=0;i<values.Length;i++)
   {
    double target=VuAnalysis.Unit(values[i],SpectrumAnalysis.FloorDb);spectrumBars[i]=immediate?target:VuAnalysis.Smooth(spectrumBars[i],target,seconds);
    HoldPeak(ref spectrumPeaks[i],ref spectrumHolds[i],target,seconds,immediate);
   }
  }
  else if(Mode==VisualizerMode.VuMeter)
  {
   vuLevels[0]=VuAnalysis.Measure(left.AsSpan(0,count));vuLevels[1]=capture.Channels>1?VuAnalysis.Measure(right.AsSpan(0,count)):vuLevels[0];
   for(int i=0;i<2;i++)
   {
    double target=VuAnalysis.Unit(vuLevels[i].RmsDb,VuAnalysis.FloorDb);vuBars[i]=immediate?target:VuAnalysis.Smooth(vuBars[i],target,seconds);
    HoldPeak(ref vuPeaks[i],ref vuHolds[i],VuAnalysis.Unit(vuLevels[i].PeakDb,VuAnalysis.FloorDb),seconds,immediate);
   }
  }
 }
 void Label(DrawingContext drawing,string text,double x,double y,double size=8,bool centered=false,bool cached=true)
 {
  double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;if(dpi!=labelDpi){labels.Clear();labelDpi=dpi;}
  string key=text+"|"+size.ToString(CultureInfo.InvariantCulture);
  if(!cached||!labels.TryGetValue(key,out var formatted))
  {
   formatted=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,secondary!.Brush,dpi);
   if(cached)labels[key]=formatted;
  }
  drawing.DrawText(formatted,new Point(centered?Math.Clamp(x-formatted.Width/2,0,Math.Max(0,ActualWidth-formatted.Width)):x,y));
 }
 void RenderSpectrum(DrawingContext drawing)
 {
  double height=Math.Max(2,ActualHeight-13),step=ActualWidth/SpectrumAnalysis.BandCount,width=Math.Max(1,step-2);
  drawing.PushOpacityMask(PlotEdgeMask(0,ActualWidth));
  drawing.DrawLine(axis,new Point(0,height),new Point(ActualWidth,height));
  for(int i=0;i<SpectrumAnalysis.BandCount;i++)
  {
   double x=i*step+1,h=spectrumBars[i]*(height-2);
   if(h>.1)drawing.DrawRoundedRectangle(primary!.Brush,null,new Rect(x,height-h,width,h),Math.Min(2,width/2),2);
   if(spectrumPeaks[i]>.002){double y=height-spectrumPeaks[i]*(height-2);drawing.DrawLine(secondary,new Point(x,y),new Point(x+width,y));}
  }
  drawing.Pop();
  double maximum=spectrum?.MaximumFrequency??20000;
  foreach(double hz in new double[]{20,100,1000,10000,20000})
  {
   if(hz>maximum)continue;double x=Math.Log(hz/20)/Math.Log(maximum/20)*ActualWidth;
   Label(drawing,hz>=1000?(hz/1000).ToString("0",CultureInfo.InvariantCulture)+"k":hz.ToString(CultureInfo.InvariantCulture),x,height+2,centered:true);
  }
 }
 void RenderVu(DrawingContext drawing)
 {
  double start=18,end=Math.Max(start+20,ActualWidth-53),width=end-start,plot=Math.Max(20,ActualHeight-13);
  bool mono=capture?.Channels==1;int rows=mono?1:2;
  for(int channel=0;channel<rows;channel++)
  {
   double y=(channel+.5)*plot/rows-5;Label(drawing,mono?"M":channel==0?"L":"R",0,y-1,9);
   drawing.PushOpacityMask(PlotEdgeMask(start,width));
   int segments=Math.Clamp((int)(width/9),24,96);double step=width/segments;
   for(int segment=0;segment<segments;segment++)
   {
    var rectangle=new Rect(start+segment*step,y,Math.Max(1,step-1.5),10);
    drawing.DrawRoundedRectangle(segment/(double)segments<vuBars[channel]?primary!.Brush:axis!.Brush,null,rectangle,1,1);
   }
   if(vuPeaks[channel]>.002){double peak=start+width*vuPeaks[channel];drawing.DrawLine(primary,new Point(peak,y-2),new Point(peak,y+12));}
   drawing.Pop();
   string db=vuLevels[channel].RmsDb<=VuAnalysis.FloorDb?"≤−60":vuLevels[channel].RmsDb.ToString("0.0",CultureInfo.InvariantCulture);
   Label(drawing,db,end+7,y-1,9,cached:false);
  }
  foreach(int db in new[]{-60,-36,-24,-12,-6,0})Label(drawing,db.ToString(CultureInfo.InvariantCulture),start+VuAnalysis.Unit(db,VuAnalysis.FloorDb)*width,plot+2,centered:true);
  Label(drawing,"dBFS",end+7,plot+2);
 }
}

public partial class MainWindow
{
 void InitializeVisualizer()
 {
  string path=Path.Combine(Paths.Data,"visualizer.json");Scope.Mode=InterfaceSettings.Current.VisualizerMode??VisualizerPreferences.Load(path).Mode;
  Scope.ModeSelected+=(_,_)=>
  {
   InterfaceSettings.Current.VisualizerMode=Scope.Mode;if(settingsWindow!=null)return;
   try{new VisualizerPreferences{Mode=Scope.Mode}.Save(path);InterfaceSettings.Current.Save(InterfaceSettings.Path);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Status.Text="Visualizer seçimi kaydedilemedi: "+ex.Message;}
  };
 }
}
