using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.Windows;
using System.Windows.Media;

namespace Faemyr;
public sealed class WaveformData
{
 public int Version {get;set;}=1;
 public float[] Low {get;set;}=[];
 public float[] Mid {get;set;}=[];
 public float[] High {get;set;}=[];
 public bool Valid=>Version==1&&ValidBand(Low)&&ValidBand(Mid)&&ValidBand(High);
 static bool ValidBand(float[]? band)
 {
  if(band is not {Length:192})return false;
  foreach(float value in band)if(!(value>=0&&value<=1))return false;
  return true;
 }
}
public static class WaveformAnalyzer
{
 const int Rate=12000,Window=512,Bins=192;
 public static string CachePath(Track t,string root)
 {
  var info=new FileInfo(t.AudioPath);string correction=System.IO.Path.ChangeExtension(t.AudioPath,".wvc");var extra=new FileInfo(correction);
  return System.IO.Path.Combine(root,Paths.Hash("wave-v1|"+t.Path+"|"+(t.IsCue?t.CueStartFrame+"|"+t.CueEndFrame+"|":"")+info.Length+"|"+info.LastWriteTimeUtc.Ticks+"|"+(extra.Exists?extra.Length+"|"+extra.LastWriteTimeUtc.Ticks:"none"))+".json");
 }
 public static async Task<WaveformData> Get(Track t,string root,CancellationToken ct)
 {
  Directory.CreateDirectory(root);string path=CachePath(t,root);
  if(File.Exists(path))try{var saved=JsonSerializer.Deserialize<WaveformData>(await File.ReadAllTextAsync(path,ct));if(saved?.Valid==true)return saved;}catch(JsonException){}
  var data=await Analyze(t,ct);ct.ThrowIfCancellationRequested();if(CachePath(t,root)!=path)throw new IOException("Dosya analiz sırasında değişti.");string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(data),ct);ct.ThrowIfCancellationRequested();File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
  return data;
 }
 public static async Task<WaveformData> Analyze(Track t,CancellationToken ct)
 {
  if(t.IsCue){string wav=await AudioEngine.Decode(t,System.IO.Path.Combine(Paths.Data,"cache"),ct);var part=t.Copy();part.Path=wav;part.SourcePath="";part.CueSheetPath="";part.CueStartFrame=0;part.CueEndFrame=null;return await Analyze(part,ct);} bool wv=System.IO.Path.GetExtension(t.Path).Equals(".wv",StringComparison.OrdinalIgnoreCase);int channels=Math.Clamp(t.Channels,1,64),frameBytes=channels*4;
  using var ff=Start(Paths.Tool("ffmpeg"),["-v","error","-nostdin","-threads","1","-i",wv?"pipe:0":t.Path,"-map","0:a:0","-vn","-ac",channels.ToString(),"-ar",Rate.ToString(),"-c:a","pcm_f32le","-f","f32le","pipe:1"],wv);
  using var unpack=wv?Start(Paths.Tool("wvunpack"),["-q","-w",t.Path,"-"],false):null;
  using var cancel=ct.Register(()=>{Kill(ff);if(unpack!=null)Kill(unpack);});
  var ffErrors=ff.StandardError.ReadToEndAsync(ct);var unpackErrors=unpack?.StandardError.ReadToEndAsync(ct);
  async Task Pump(){if(unpack==null)return;try{await unpack.StandardOutput.BaseStream.CopyToAsync(ff.StandardInput.BaseStream,ct);}finally{ff.StandardInput.Close();}}
  var pumping=Pump();var windows=new List<(float low,float mid,float high)>();
  try
  {
   var bytes=new byte[32768];int carry=0,count=0;var lowState=new double[channels];var upperState=new double[channels];double lowEnergy=0,midEnergy=0,highEnergy=0;
   double lowAlpha=1-Math.Exp(-2*Math.PI*180/Rate),upperAlpha=1-Math.Exp(-2*Math.PI*2600/Rate);
   void Flush(){if(count==0)return;windows.Add(((float)Math.Sqrt(lowEnergy/(count*channels)),(float)Math.Sqrt(midEnergy/(count*channels)),(float)Math.Sqrt(highEnergy/(count*channels))));count=0;lowEnergy=midEnergy=highEnergy=0;}
   while(true)
   {
    int read=await ff.StandardOutput.BaseStream.ReadAsync(bytes.AsMemory(carry,bytes.Length-carry),ct);if(read==0)break;int length=carry+read,complete=length-length%frameBytes;
    for(int i=0;i<complete;i+=frameBytes){for(int c=0;c<channels;c++){float sample=BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i+c*4,4)));if(!float.IsFinite(sample))sample=0;lowState[c]+=lowAlpha*(sample-lowState[c]);upperState[c]+=upperAlpha*(sample-upperState[c]);double mid=upperState[c]-lowState[c],high=sample-upperState[c];lowEnergy+=lowState[c]*lowState[c];midEnergy+=mid*mid;highEnergy+=high*high;}if(++count==Window)Flush();}
    carry=length-complete;if(carry>0)Buffer.BlockCopy(bytes,complete,bytes,0,carry);
   }
   Flush();await pumping;await ff.WaitForExitAsync(ct);if(unpack!=null)await unpack.WaitForExitAsync(ct);
   string error=await ffErrors;string unpackError=unpackErrors==null?"":await unpackErrors;
   if(ff.ExitCode!=0||unpack?.ExitCode is not (null or 0))throw new IOException("Dalga formu çözümlenemedi: "+error+unpackError);
   if(windows.Count==0)throw new IOException("Ses örneği bulunamadı.");
   var result=new WaveformData{Low=new float[Bins],Mid=new float[Bins],High=new float[Bins]};
   for(int i=0;i<Bins;i++){int start=i*windows.Count/Bins,end=Math.Max(start+1,(i+1)*windows.Count/Bins);for(int j=start;j<Math.Min(end,windows.Count);j++){result.Low[i]=Math.Max(result.Low[i],windows[j].low);result.Mid[i]=Math.Max(result.Mid[i],windows[j].mid);result.High[i]=Math.Max(result.High[i],windows[j].high);}}
   float peak=Enumerable.Range(0,Bins).Max(i=>result.Low[i]+result.Mid[i]+result.High[i]);
   if(peak>0.000001f)for(int i=0;i<Bins;i++){result.Low[i]/=peak;result.Mid[i]/=peak;result.High[i]/=peak;}
   return result;
  }
  catch{Kill(ff);if(unpack!=null)Kill(unpack);try{await pumping;}catch{}try{await ffErrors;if(unpackErrors!=null)await unpackErrors;}catch{}throw;}
 }
 static Process Start(string path,IEnumerable<string> args,bool input)
 {
  var info=new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=input};foreach(var arg in args)info.ArgumentList.Add(arg);
  var p=Process.Start(info)??throw new IOException("Dalga formu aracı başlatılamadı.");try{p.PriorityClass=ProcessPriorityClass.BelowNormal;}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}return p;
 }
 static void Kill(Process p){try{if(!p.HasExited)p.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
}
public sealed class WaveformQueue : IDisposable
{
 sealed class WorkItem(Track track){public Track Track{get;}=track;public bool Urgent,Running,Done;}
 readonly ConcurrentQueue<WorkItem> normal=new(),priority=new();readonly Dictionary<string,WorkItem> queued=new(StringComparer.OrdinalIgnoreCase);
 readonly System.Runtime.CompilerServices.ConditionalWeakTable<Track,WorkItem> delivered=new();
 readonly SemaphoreSlim available=new(0);readonly CancellationTokenSource cancel=new();readonly string root;
 readonly Action<Track,WaveformData?,string> completed;
 readonly Func<Track,CancellationToken,Task<WaveformData>> analyze;
 public WaveformQueue(string root,Action<Track,WaveformData?,string> completed,Func<Track,CancellationToken,Task<WaveformData>>? analyze=null){this.root=root;this.completed=completed;this.analyze=analyze??((track,token)=>WaveformAnalyzer.Get(track,root,token));_ = Task.Run(Work);}
 public void Enqueue(Track t,bool urgent=false)
 {
  if(t.Waveform!=null||cancel.IsCancellationRequested)return;
  lock(queued)
  {
   if(delivered.TryGetValue(t,out _))return;
   if(queued.TryGetValue(t.Path,out var prior)&&ReferenceEquals(prior.Track,t))
   {
    if(!urgent||prior.Urgent||prior.Running||prior.Done)return;
    prior.Urgent=true;priority.Enqueue(prior);
   }
   else
   {
    var item=new WorkItem(t){Urgent=urgent};queued[t.Path]=item;
    if(urgent)priority.Enqueue(item);else normal.Enqueue(item);
   }
   available.Release();
  }
 }
 async Task Work()
 {
  try
  {
   while(true)
   {
    await available.WaitAsync(cancel.Token);
    if(!priority.TryDequeue(out var item)&&!normal.TryDequeue(out item))continue;
    lock(queued){if(item.Running||item.Done||!queued.TryGetValue(item.Track.Path,out var current)||!ReferenceEquals(item,current))continue;item.Running=true;}
    WaveformData? data=null;string error="";
    try{data=await analyze(item.Track,cancel.Token);cancel.Token.ThrowIfCancellationRequested();}
    catch(OperationCanceledException){throw;}catch(Exception ex){error="Oluşturulamadı: "+ex.Message;}
    lock(queued)
    {
     if(!queued.TryGetValue(item.Track.Path,out var latest)||!ReferenceEquals(item,latest))continue;
     item.Done=true;
     queued.Remove(item.Track.Path);
     if(data!=null)delivered.Add(item.Track,item);
     completed(item.Track,data,error);
    }
   }
  }catch(OperationCanceledException){}
 }
 public void Dispose()=>cancel.Cancel();
}
public sealed class WaveformView : FrameworkElement
{
 public static readonly DependencyProperty DataProperty=DependencyProperty.Register(nameof(Data),typeof(WaveformData),typeof(WaveformView),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
 public static readonly DependencyProperty HintProperty=DependencyProperty.Register(nameof(Hint),typeof(string),typeof(WaveformView),new FrameworkPropertyMetadata("Sırada",FrameworkPropertyMetadataOptions.AffectsRender));
 public WaveformData? Data{get=>(WaveformData?)GetValue(DataProperty);set=>SetValue(DataProperty,value);}
 public string Hint{get=>(string)GetValue(HintProperty);set=>SetValue(HintProperty,value);}
 static readonly Brush LowBrush=new SolidColorBrush(Color.FromRgb(242,232,207)),MidBrush=new SolidColorBrush(Color.FromRgb(191,123,53)),HighBrush=new SolidColorBrush(Color.FromRgb(37,68,181));
 protected override void OnRender(DrawingContext dc)
 {
  base.OnRender(dc);double width=ActualWidth,height=ActualHeight;if(width<=0||height<=0)return;
  if(Data is not { } data){var text=new FormattedText(Hint.StartsWith("Oluşturulamadı")?"Önizleme yok":"Hazırlanıyor…",System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),10,Brushes.SlateGray,VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(text,new Point(0,(height-text.Height)/2));return;}
  if(!data.Valid)return;
  void Layer(int layer,Brush brush){var shape=new StreamGeometry();using(var g=shape.Open()){g.BeginFigure(new Point(0,height),true,true);for(int i=0;i<data.Low.Length;i++){double energy=data.Low[i]+(layer>=1?data.Mid[i]:0)+(layer>=2?data.High[i]:0);g.LineTo(new Point(i*width/(data.Low.Length-1),height-Math.Max(1,energy*(height-2))),true,false);}g.LineTo(new Point(width,height),true,false);}shape.Freeze();dc.DrawGeometry(brush,null,shape);}
  Layer(2,HighBrush);Layer(1,MidBrush);Layer(0,LowBrush);
 }
}
