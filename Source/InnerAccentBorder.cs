using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ahenk;
// Paint only the card background. A continuous distance field replaces stacked
// strokes; subpixel coverage smooths the corners without rasterizing the content.
public sealed class InnerAccentBorder : Border
{
 public static readonly DependencyProperty PlainPresentationProperty=DependencyProperty.Register(nameof(PlainPresentation),typeof(bool),typeof(InnerAccentBorder),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender));
 public bool PlainPresentation{get=>(bool)GetValue(PlainPresentationProperty);set=>SetValue(PlainPresentationProperty,value);}
 public static readonly DependencyProperty GlowBrushProperty=DependencyProperty.Register(nameof(GlowBrush),typeof(Brush),typeof(InnerAccentBorder),new FrameworkPropertyMetadata(Brushes.Transparent,FrameworkPropertyMetadataOptions.AffectsRender));
 public Brush GlowBrush{get=>(Brush)GetValue(GlowBrushProperty);set=>SetValue(GlowBrushProperty,value);}
 object? cachedKey;BitmapSource? cachedImage;
 public InnerAccentBorder(){RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);RenderOptions.SetEdgeMode(this,EdgeMode.Unspecified);}
 protected override void OnRender(DrawingContext drawing)
 {
  if(PlainPresentation)return;
  if(ActualWidth<4||ActualHeight<4||Background is not SolidColorBrush surface||GlowBrush is not SolidColorBrush glow||BorderBrush is not SolidColorBrush rim){base.OnRender(drawing);return;}
  var dpi=VisualTreeHelper.GetDpi(this);var key=(ActualWidth,ActualHeight,dpi.DpiScaleX,dpi.DpiScaleY,CornerRadius.TopLeft,surface.Color,surface.Opacity,glow.Color,glow.Opacity,rim.Color,rim.Opacity);
  if(cachedImage==null||!Equals(cachedKey,key))
  {
   int width=Math.Max(1,(int)Math.Ceiling(ActualWidth*dpi.DpiScaleX)),height=Math.Max(1,(int)Math.Ceiling(ActualHeight*dpi.DpiScaleY));double sx=width/ActualWidth,sy=height/ActualHeight;
   double radius=Math.Clamp(CornerRadius.TopLeft,0,Math.Min(ActualWidth,ActualHeight)/2),halfWidth=ActualWidth/2,halfHeight=ActualHeight/2;byte[] pixels=new byte[checked(width*height*4)];
   double Distance(double x,double y){double qx=Math.Abs(x-halfWidth)-(halfWidth-radius),qy=Math.Abs(y-halfHeight)-(halfHeight-radius);return radius-Math.Sqrt(Math.Max(qx,0)*Math.Max(qx,0)+Math.Max(qy,0)*Math.Max(qy,0))-Math.Min(Math.Max(qx,qy),0);}
   double bgAlpha=surface.Color.A/255d*surface.Opacity;
   for(int y=0;y<height;y++)for(int x=0;x<width;x++)
   {
    double distance=Distance((x+.5)/sx,(y+.5)/sy);if(distance < -1/Math.Min(sx,sy))continue;
    // Supersample only the edge and its fine rim. Interior pixels use the same
    // smooth exponential field, which has no visible cutoff or concentric bands.
    int samples=distance<2?8:1;double red=0,green=0,blue=0,alpha=0;
    for(int yy=0;yy<samples;yy++)for(int xx=0;xx<samples;xx++)
    {
     double inward=samples==1?distance:Distance((x+(xx+.5)/samples)/sx,(y+(yy+.5)/samples)/sy);if(inward<0)continue;
     double a=bgAlpha,r=surface.Color.R/255d*a,g=surface.Color.G/255d*a,b=surface.Color.B/255d*a;
     void Over(Color c,double opacity){double coverage=c.A/255d*opacity;r=c.R/255d*coverage+r*(1-coverage);g=c.G/255d*coverage+g*(1-coverage);b=c.B/255d*coverage+b*(1-coverage);a=coverage+a*(1-coverage);}
     Over(glow.Color,.12*Math.Exp(-inward/4.5)*glow.Opacity);
     Over(rim.Color,.30*Math.Exp(-.5*Math.Pow(inward/.55,2))*rim.Opacity);
     red+=r;green+=g;blue+=b;alpha+=a;
    }
    int offset=(y*width+x)*4;double scale=255d/(samples*samples);
    pixels[offset]=(byte)Math.Clamp(Math.Round(blue*scale),0,255);pixels[offset+1]=(byte)Math.Clamp(Math.Round(green*scale),0,255);pixels[offset+2]=(byte)Math.Clamp(Math.Round(red*scale),0,255);pixels[offset+3]=(byte)Math.Clamp(Math.Round(alpha*scale),0,255);
   }
   cachedImage=BitmapSource.Create(width,height,96*sx,96*sy,PixelFormats.Pbgra32,null,pixels,width*4);cachedImage.Freeze();cachedKey=key;
  }
  drawing.DrawImage(cachedImage,new Rect(RenderSize));
 }
}
