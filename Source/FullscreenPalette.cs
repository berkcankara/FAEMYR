using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Faemyr;
public static class FullscreenPalette
{
 public static Brush Create(BitmapSource? image,bool light,Color fallback)
 {
  if(image==null)return new SolidColorBrush(fallback);
  try
  {
   var small=new FormatConvertedBitmap(new TransformedBitmap(image,new ScaleTransform(48d/image.PixelWidth,48d/image.PixelHeight)),PixelFormats.Bgra32,null,0);int stride=small.PixelWidth*4;byte[] pixels=new byte[stride*small.PixelHeight];small.CopyPixels(pixels,stride,0);
   var bins=new Dictionary<int,(long R,long G,long B,int Count)>();for(int i=0;i<pixels.Length;i+=4){if(pixels[i+3]<128)continue;int r=pixels[i+2],g=pixels[i+1],b=pixels[i],key=(r>>5)*64+(g>>5)*8+(b>>5);var bin=bins.GetValueOrDefault(key);bins[key]=(bin.R+r,bin.G+g,bin.B+b,bin.Count+1);}
   if(bins.Count==0)return new SolidColorBrush(fallback);
   var colors=bins.Values.Select(bin=>{var color=Color.FromRgb((byte)(bin.R/bin.Count),(byte)(bin.G/bin.Count),(byte)(bin.B/bin.Count));double max=Math.Max(color.R,Math.Max(color.G,color.B)),min=Math.Min(color.R,Math.Min(color.G,color.B));return(Color:color,Score:bin.Count*(.15+(max-min)/Math.Max(1,max))*(max<30?.15:1));}).OrderByDescending(c=>c.Score).ToList();
   var first=colors[0].Color;double Distance(Color c)=>Math.Abs(c.R-first.R)+Math.Abs(c.G-first.G)+Math.Abs(c.B-first.B);var second=colors.FirstOrDefault(c=>Distance(c.Color)>100&&c.Score>=colors[0].Score*.1).Color;if(second.A==0)second=first;
   Color Shade(Color c,double amount)=>light?Color.FromRgb((byte)(205+c.R*.16),(byte)(205+c.G*.16),(byte)(205+c.B*.16)):Color.FromRgb((byte)(6+c.R*amount),(byte)(6+c.G*amount),(byte)(6+c.B*amount));
   var brush=new LinearGradientBrush(new GradientStopCollection{new(Shade(first,.50),0),new(Shade(second,.43),.58),new(Shade(first,.22),1)},new Point(0,0),new Point(1,1));brush.Freeze();return brush;
  }
  catch(Exception ex)when(ex is NotSupportedException or ArgumentException or InvalidOperationException){return new SolidColorBrush(fallback);}
 }
}
