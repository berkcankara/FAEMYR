using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ahenk;
public static class RoundedClip
{
 public static readonly DependencyProperty RadiusProperty=DependencyProperty.RegisterAttached("Radius",typeof(double),typeof(RoundedClip),new PropertyMetadata(0d,Changed));
 public static double GetRadius(DependencyObject node)=>(double)node.GetValue(RadiusProperty);
 public static void SetRadius(DependencyObject node,double value)=>node.SetValue(RadiusProperty,value);
 static void Changed(DependencyObject node,DependencyPropertyChangedEventArgs args){if(node is FrameworkElement element){element.SizeChanged-=Resize;element.SizeChanged+=Resize;Update(element);}}
 static void Resize(object sender,SizeChangedEventArgs args)=>Update((FrameworkElement)sender);
 static void Update(FrameworkElement element){double radius=GetRadius(element);element.Clip=new RectangleGeometry(new Rect(0,0,Math.Max(0,element.ActualWidth),Math.Max(0,element.ActualHeight)),radius,radius);}
}
public class ThumbnailImage : Image
{
 public static readonly DependencyProperty PathProperty=DependencyProperty.Register(nameof(Path),typeof(string),typeof(ThumbnailImage),new PropertyMetadata(null,Changed));
 public static readonly DependencyProperty DecodePixelWidthProperty=DependencyProperty.Register(nameof(DecodePixelWidth),typeof(int),typeof(ThumbnailImage),new PropertyMetadata(320,Changed),value=>value is int width&&width>=64&&width<=1536);
 public static readonly DependencyProperty RadiusProperty=DependencyProperty.Register(nameof(Radius),typeof(double),typeof(ThumbnailImage),new PropertyMetadata(0.0,(o,_)=>{var image=(ThumbnailImage)o;image.Clip=new RectangleGeometry(new Rect(image.RenderSize),image.Radius,image.Radius);}));
 public string? Path{get=>(string?)GetValue(PathProperty);set=>SetValue(PathProperty,value);}
 public double Radius{get=>(double)GetValue(RadiusProperty);set=>SetValue(RadiusProperty,value);}
 public int DecodePixelWidth{get=>(int)GetValue(DecodePixelWidthProperty);set=>SetValue(DecodePixelWidthProperty,value);}
 int generation;
 public ThumbnailImage(){Loaded+=(_,_)=>Reload();Unloaded+=(_,_)=>{generation++;Source=null;};SizeChanged+=(_,_)=>Clip=new RectangleGeometry(new Rect(RenderSize),Radius,Radius);}
 static void Changed(DependencyObject sender,DependencyPropertyChangedEventArgs e){var image=(ThumbnailImage)sender;if(image.IsLoaded)image.Reload();}
 async void Reload()
 {
  int request=++generation;string? path=Path;Source=null;
  var image=await ThumbnailCache.Load(path,DecodePixelWidth);
  if(IsLoaded&&generation==request&&Path==path)Source=image;
 }
}
public static class ThumbnailCache
{
 const int Capacity=128;
 static readonly object gate=new();
 static readonly SemaphoreSlim decoders=new(2);
 static readonly Dictionary<string,(BitmapSource Image,LinkedListNode<string> Node)> images=new(StringComparer.OrdinalIgnoreCase);
 static readonly LinkedList<string> recent=new();
 static readonly Dictionary<string,Task<BitmapSource?>> loading=new(StringComparer.OrdinalIgnoreCase);
 public static Task<BitmapSource?> Load(string? path,int decodeWidth=320)
 {
  if(string.IsNullOrEmpty(path))return Task.FromResult<BitmapSource?>(null);
  decodeWidth=Math.Clamp(decodeWidth,64,1536);string key=path+"\u001f"+decodeWidth;
  lock(gate)
  {
   if(images.TryGetValue(key,out var cached)){recent.Remove(cached.Node);recent.AddFirst(cached.Node);return Task.FromResult<BitmapSource?>(cached.Image);}
   if(loading.TryGetValue(key,out var pending))return pending;
   // Start on the pool so disk access and image decoding never execute on the UI thread.
   var task=Task.Run(()=>Decode(path,key,decodeWidth));loading[key]=task;return task;
  }
 }
 static async Task<BitmapSource?> Decode(string path,string key,int decodeWidth)
 {
  await decoders.WaitAsync();BitmapSource? result=null;
  try
  {
   if(File.Exists(path))
   {
    var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.CreateOptions=BitmapCreateOptions.IgnoreImageCache;image.DecodePixelWidth=decodeWidth;image.UriSource=new Uri(path,UriKind.Absolute);image.EndInit();image.Freeze();result=image;
   }
  }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException){}
  finally{decoders.Release();}
  lock(gate)
  {
   loading.Remove(key);
   if(result!=null)
   {
    var node=recent.AddFirst(key);images[key]=(result,node);
    while(images.Count>Capacity){string oldest=recent.Last!.Value;recent.RemoveLast();images.Remove(oldest);}
   }
  }
  return result;
 }
 public static async Task WhenIdle(){Task[] pending;lock(gate)pending=loading.Values.Cast<Task>().ToArray();await Task.WhenAll(pending);}
 public static int CachedCount{get{lock(gate)return images.Count;}}
}
