using System.Buffers.Binary;
using System.Windows;
using System.Windows.Media;
using NAudio.Wave;

namespace Faemyr;

// Observes PCM returned to WASAPI. Audio bytes are never edited or replaced.
public sealed class ScopeCapture
{
 readonly object gate=new();
 readonly float[] left,right;
 readonly int bytesPerSample,blockAlign;
 readonly bool floating;
 long written,validFrom;int generation;
 public int Generation{get{lock(gate)return generation;}}
 public int SampleRate{get;}
 public int Channels{get;}
 public int WindowFrames=>Math.Max(2,SampleRate/40); // 25 ms across the plot
 public int TriggerFrames=>Math.Max(1,SampleRate/100);
 public int BufferSize=>WindowFrames+TriggerFrames;
 public long FramesWritten{get{lock(gate)return written;}}
 public static ScopeCapture? Create(WaveFormat format)
 {
  var standard=format.AsStandardWaveFormat();
  bool supported=standard.Encoding==WaveFormatEncoding.Pcm&&standard.BitsPerSample is 8 or 16 or 24 or 32
   ||standard.Encoding==WaveFormatEncoding.IeeeFloat&&standard.BitsPerSample is 32 or 64;
  return supported&&standard.Channels>0&&standard.SampleRate>0&&standard.BlockAlign==standard.Channels*(standard.BitsPerSample/8)?new ScopeCapture(standard):null;
 }
 ScopeCapture(WaveFormat format)
 {
  SampleRate=format.SampleRate;Channels=format.Channels;blockAlign=format.BlockAlign;bytesPerSample=format.BitsPerSample/8;floating=format.Encoding==WaveFormatEncoding.IeeeFloat;
  int capacity=Math.Clamp(SampleRate*2,8192,1536000);left=new float[capacity];right=new float[capacity];
 }
 float Sample(ReadOnlySpan<byte> bytes)
 {
  float value;
  if(floating)value=bytesPerSample==4?BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes)):(float)BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes));
  else value=bytesPerSample switch
  {
   1=>(bytes[0]-128)/128f,
   2=>BinaryPrimitives.ReadInt16LittleEndian(bytes)/32768f,
   3=>((bytes[0]|bytes[1]<<8|bytes[2]<<16)<<8>>8)/8388608f,
   _=>(float)(BinaryPrimitives.ReadInt32LittleEndian(bytes)/2147483648.0)
  };
  return float.IsFinite(value)?Math.Clamp(value,-1,1):0;
 }
 public void Append(ReadOnlySpan<byte> bytes)
 {
  int frames=bytes.Length/blockAlign;
  lock(gate)
  {
   int slot=(int)(written%left.Length);
   for(int i=0;i<frames;i++)
   {
    int offset=i*blockAlign;
    left[slot]=Sample(bytes.Slice(offset,bytesPerSample));
    right[slot]=Channels>1?Sample(bytes.Slice(offset+bytesPerSample,bytesPerSample)):left[slot];if(++slot==left.Length)slot=0;
   }
   written+=frames;
  }
 }
 // A seek discards old visualization history but keeps the output device's monotonic timeline.
 public void Clear(){lock(gate){validFrom=written;generation++;}}
 public int CopyWindow(long audibleFrame,Span<float> destinationLeft,Span<float> destinationRight,int requestedFrames=0)
  =>CopyWindow(audibleFrame,destinationLeft,destinationRight,out _,requestedFrames);
 public int CopyWindow(long audibleFrame,Span<float> destinationLeft,Span<float> destinationRight,out long endFrame,int requestedFrames=0)
 {
  lock(gate)
  {
   long start=Math.Max(validFrom,written-left.Length),end=Math.Min(audibleFrame,written);
   endFrame=end;
   if(end<=start)return 0;
   int count=(int)Math.Min(Math.Min(end-start,requestedFrames>0?requestedFrames:BufferSize),Math.Min(destinationLeft.Length,destinationRight.Length));
   int slot=(int)((end-count)%left.Length),first=Math.Min(count,left.Length-slot);
   left.AsSpan(slot,first).CopyTo(destinationLeft);right.AsSpan(slot,first).CopyTo(destinationRight);
   left.AsSpan(0,count-first).CopyTo(destinationLeft[first..]);right.AsSpan(0,count-first).CopyTo(destinationRight[first..]);
   return count;
  }
 }
 public static int TriggerStart(ReadOnlySpan<float> left,ReadOnlySpan<float> right,int windowFrames)
 {
  int search=left.Length-windowFrames;if(search<=0)return 0;
  double l=0,r=0;for(int i=0;i<left.Length;i++){l+=left[i]*left[i];r+=right[i]*right[i];}
  var channel=r>l?right:left;int start=search;
  for(int i=search;i>0;i--)if(channel[i-1]<=0&&channel[i]>0){start=i;break;}
  return start;
 }
}

public sealed class ScopeSource(IWaveProvider source,WaveStream? stream=null,bool observe=true) : IWaveProvider,IRealtimeAudio
{
 readonly object ioGate=new();
 public WaveFormat WaveFormat=>source.WaveFormat;
 public ScopeCapture? Capture{get;}=observe?ScopeCapture.Create(source.WaveFormat):null;
 public bool EndOfStream=>source is not IRealtimeAudio live||live.EndOfStream;
 public bool Buffering=>source is IRealtimeAudio live&&live.Buffering;
 public int Read(byte[] buffer,int offset,int count)
 {
  lock(ioGate){int read=source.Read(buffer,offset,count);Capture?.Append(buffer.AsSpan(offset,read));return read;}
 }
 public void Seek(TimeSpan position){lock(ioGate){if(stream==null)return;stream.CurrentTime=position;Capture?.Clear();}}
}

internal sealed class VisualizerFramePacer
{
 public double TargetFps{get;set;}=60;
 TimeSpan observed=TimeSpan.MinValue,next=TimeSpan.MinValue;
 public void Reset(){observed=next=TimeSpan.MinValue;}
 public bool Accept(TimeSpan timestamp)
 {
  var interval=TimeSpan.FromSeconds(1/Math.Clamp(double.IsFinite(TargetFps)?TargetFps:60,30,120));
  if(timestamp==observed)return false;if(timestamp<observed)next=TimeSpan.MinValue;observed=timestamp;
  if(next!=TimeSpan.MinValue&&timestamp.Ticks+TimeSpan.TicksPerMillisecond/4<next.Ticks)return false;
  // Advance the deadline instead of measuring from the last accepted refresh.
  // This avoids falling to 48 fps on 144 Hz displays or skipping 60 Hz frames.
  next=next==TimeSpan.MinValue||timestamp-next>interval?timestamp+interval:next+interval;return true;
 }
}

public sealed partial class OscilloscopeView : FrameworkElement
{
 public static readonly DependencyProperty RunningProperty=DependencyProperty.Register(nameof(Running),typeof(bool),typeof(OscilloscopeView),new FrameworkPropertyMetadata(false,Changed));
 public static readonly DependencyProperty TraceBrushProperty=DependencyProperty.Register(nameof(TraceBrush),typeof(Brush),typeof(OscilloscopeView),new FrameworkPropertyMetadata(Brushes.MediumPurple,FrameworkPropertyMetadataOptions.AffectsRender,BrushChanged));
 public bool Running{get=>(bool)GetValue(RunningProperty);set=>SetValue(RunningProperty,value);}
 public Brush TraceBrush{get=>(Brush)GetValue(TraceBrushProperty);set=>SetValue(TraceBrushProperty,value);}
 public AudioEngine? Engine{get;set;}
 ScopeCapture? capture;
 float[] left=[],right=[];
 int count;
 long scopeEndFrame;int captureGeneration;
 bool subscribed;
 TimeSpan lastFrame=TimeSpan.MinValue;
 readonly VisualizerFramePacer framePacer=new();
 double traceWidth=1.4,glowOpacity=.12;
 public double MaximumFramesPerSecond{get=>framePacer.TargetFps;set{if(framePacer.TargetFps==value)return;framePacer.TargetFps=value;framePacer.Reset();}}
 public double TraceWidth{get=>traceWidth;set{if(traceWidth==value)return;traceWidth=value;ClearPens();}}
 public double GlowOpacity{get=>glowOpacity;set{if(glowOpacity==value)return;glowOpacity=value;ClearPens();}}
 Pen? primary,secondary,glow,axis;
 public OscilloscopeView(){Loaded+=(_,_)=>Subscribe();Unloaded+=(_,_)=>Subscribe();IsVisibleChanged+=(_,_)=>Subscribe();ClipToBounds=true;InitializeMenu();}
 static void Changed(DependencyObject sender,DependencyPropertyChangedEventArgs e)=>((OscilloscopeView)sender).Subscribe();
 static void BrushChanged(DependencyObject sender,DependencyPropertyChangedEventArgs e){var view=(OscilloscopeView)sender;view.primary=view.secondary=view.glow=view.axis=null;view.labels.Clear();view.spectrogramColorDirty=true;}
 void ClearPens(){primary=secondary=glow=axis=null;labels.Clear();InvalidateVisual();}
 void Subscribe()
 {
  bool active=Running&&IsLoaded&&IsVisible;
  if(active==subscribed)return;
  if(active){lastFrame=TimeSpan.MinValue;framePacer.Reset();CompositionTarget.Rendering+=Frame;}else CompositionTarget.Rendering-=Frame;
  subscribed=active;
 }
 public void Reset(){Running=false;capture=null;count=0;ResetAnalysis();InvalidateVisual();}
 void Frame(object? sender,EventArgs e)
 {
  if(e is RenderingEventArgs rendering)
  {
   if(!framePacer.Accept(rendering.RenderingTime))return;
   lastFrame=rendering.RenderingTime;
  }
  ReadFrame(false);
 }
 void ReadFrame(bool immediate)
 {
  var current=Engine?.Scope;
  if(current==null){count=0;ResetAnalysis();InvalidateVisual();return;}
  if(!ReferenceEquals(capture,current))
  {
   capture=current;captureGeneration=current.Generation;ResetAnalysis();immediate=true;
   int capacity=Math.Max(current.BufferSize,Math.Max(SpectrumAnalysis.SizeForRate(current.SampleRate),(int)Math.Ceiling(current.SampleRate*.3)));
   if(left.Length!=capacity){left=new float[capacity];right=new float[capacity];}
  }
  if(captureGeneration!=current.Generation){captureGeneration=current.Generation;ResetAnalysis();immediate=true;}
  int requested=Mode switch{VisualizerMode.Spectrum or VisualizerMode.Spectrogram=>SpectrumAnalysis.SizeForRate(current.SampleRate),VisualizerMode.VuMeter=>(int)Math.Ceiling(current.SampleRate*.3),_=>current.BufferSize};
  count=Engine!.CopyScope(current,left,right,out scopeEndFrame,requested);UpdateAnalysis(immediate);InvalidateVisual();
 }
 void Pens()
 {
  if(primary!=null)return;
  Pen Make(double opacity,double width){var brush=TraceBrush.CloneCurrentValue();brush.Opacity*=opacity;brush.Freeze();var pen=new Pen(brush,width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};pen.Freeze();return pen;}
  primary=Make(1,traceWidth);secondary=Make(.44,Math.Max(.8,traceWidth*.7));glow=Make(glowOpacity,traceWidth+2.6);axis=Make(.13,.6);
 }
 protected override void OnRender(DrawingContext drawing)
 {
  base.OnRender(drawing);if(ActualWidth<2||ActualHeight<2)return;
  drawing.DrawRectangle(Brushes.Transparent,null,new Rect(RenderSize));Pens();
  if(Mode==VisualizerMode.Spectrum){RenderSpectrum(drawing);return;}
  if(Mode==VisualizerMode.VuMeter){RenderVu(drawing);return;}
  if(Mode==VisualizerMode.Spectrogram){RenderSpectrogram(drawing);return;}
  drawing.PushOpacityMask(PlotEdgeMask(0,ActualWidth));
  double middle=ActualHeight/2,amplitude=Math.Max(1,middle-4);
  drawing.DrawLine(axis,new Point(0,middle),new Point(ActualWidth,middle));
  if(count<2||capture==null){drawing.Pop();return;}
  int length=Math.Min(count,capture.WindowFrames),start=ScopeCapture.TriggerStart(left.AsSpan(0,count),right.AsSpan(0,count),length);
  StreamGeometry Trace(float[] samples)
  {
   var geometry=new StreamGeometry();using(var path=geometry.Open())
   {
    path.BeginFigure(new Point(0,middle-samples[start]*amplitude),false,false);
    int columns=Math.Clamp((int)ActualWidth,32,1024);
    if(length<=columns*2)
    {
     for(int i=1;i<length;i++)path.LineTo(new Point(i*ActualWidth/(length-1),middle-samples[start+i]*amplitude),true,false);
    }
    else
    {
     // Preserve both extrema in chronological order instead of aliasing high-frequency peaks away.
     for(int bucket=0;bucket<columns;bucket++)
     {
      int first=bucket*length/columns,end=(bucket+1)*length/columns,min=first,max=first;
      for(int i=first+1;i<end;i++){if(samples[start+i]<samples[start+min])min=i;if(samples[start+i]>samples[start+max])max=i;}
      void Add(int i)=>path.LineTo(new Point(i*ActualWidth/(length-1),middle-samples[start+i]*amplitude),true,false);
      if(min<max){Add(min);Add(max);}else{Add(max);Add(min);}
     }
    }
   }
   geometry.Freeze();return geometry;
  }
  var firstTrace=Trace(left);drawing.DrawGeometry(null,glow,firstTrace);
  if(capture.Channels>1)drawing.DrawGeometry(null,secondary,Trace(right));
  drawing.DrawGeometry(null,primary,firstTrace);drawing.Pop();
 }
}
