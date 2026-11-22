using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Faemyr;

public partial class MainWindow
{
 readonly record struct BackdropKey(object? Artwork,Color Canvas,bool Light,string? Tint);
 BackdropKey? backdropKey;
 bool backdropVisible;
 int backdropVisibilityVersion,backdropArtworkVersion;
 static readonly TimeSpan BackdropDuration=TimeSpan.FromMilliseconds(460);

 void UpdatePageBackdrop()
 {
  if(!libraryReady||closed)return;
  bool album=view=="album",gallery=view is "albums" or "artists";
  // The album being browsed takes priority over the current playback session.
  // The detail panel already decodes the current album art, so reuse that image.
  var source=album?AlbumArtwork.Source:gallery&&now!=null?(PanelCover.Background as ImageBrush)?.ImageSource:null;
  if(!InterfaceSettings.Current.AlbumArtworkBackground||(!album&&(!gallery||source==null)))
  {
   SetBackdropVisibility(false);return;
  }
  var canvas=((SolidColorBrush)FindResource("Canvas")).Color;
  Themes.Appearance.Colors.TryGetValue("AlbumTint",out string? customTint);
  object? artworkIdentity=source is BitmapImage {UriSource: {} uri}?uri:source;
  var key=new BackdropKey(artworkIdentity,canvas,Themes.Current=="light",customTint);
  if(backdropKey!=key)
  {
   bool crossfade=PaletteLayer.Visibility==Visibility.Visible&&PaletteLayer.Opacity>.01&&PaletteGradient.Background!=null&&SystemParameters.ClientAreaAnimation;
   if(crossfade)
   {
    PreviousPalette.BeginAnimation(OpacityProperty,null);
    PreviousPaletteGradient.Background=PaletteGradient.Background;PreviousAmbientCover.Source=AmbientCover.Source;
    PreviousPalette.Visibility=Visibility.Visible;PreviousPalette.Opacity=1;
   }
   else ClearPreviousBackdrop();
   PaletteGradient.Background=CreateBackdropGradient(source,canvas,key.Light,customTint);
   AmbientCover.Source=source;backdropKey=key;
   if(crossfade)
   {
    int version=++backdropArtworkVersion;
    AnimateBackdropOpacity(PreviousPalette,0,()=>{if(version==backdropArtworkVersion)ClearPreviousBackdrop();});
   }
  }
  SetBackdropVisibility(true);
 }

 void SetBackdropVisibility(bool visible)
 {
  if(visible==backdropVisible)return;
  backdropVisible=visible;int version=++backdropVisibilityVersion;
  if(visible)PaletteLayer.Visibility=Visibility.Visible;
  AnimateBackdropOpacity(PaletteLayer,visible?1:0,()=>
  {
   if(version!=backdropVisibilityVersion||visible)return;
   PaletteLayer.Visibility=Visibility.Collapsed;ClearPreviousBackdrop();
  });
 }

 void ClearPreviousBackdrop()
 {
  ++backdropArtworkVersion;PreviousPalette.BeginAnimation(OpacityProperty,null);PreviousPalette.Opacity=0;
  PreviousPalette.Visibility=Visibility.Collapsed;PreviousAmbientCover.Source=null;PreviousPaletteGradient.Background=null;
 }

 static void AnimateBackdropOpacity(UIElement target,double opacity,Action completed)
 {
  double from=target.Opacity;target.BeginAnimation(OpacityProperty,null);target.Opacity=opacity;
  if(!SystemParameters.ClientAreaAnimation||Math.Abs(from-opacity)<.001){completed();return;}
  var animation=new DoubleAnimation(from,opacity,BackdropDuration){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut},FillBehavior=FillBehavior.Stop};
  animation.Completed+=(_,_)=>completed();target.BeginAnimation(OpacityProperty,animation,HandoffBehavior.SnapshotAndReplace);
 }

 static LinearGradientBrush CreateBackdropGradient(ImageSource? source,Color canvas,bool light,string? customTint)
 {
  Color tint=Color.FromRgb(35,35,35);
  if(source is BitmapSource bitmap&&bitmap.PixelWidth>0&&bitmap.PixelHeight>0)
  {
   var small=new FormatConvertedBitmap(new TransformedBitmap(bitmap,new ScaleTransform(32.0/bitmap.PixelWidth,32.0/bitmap.PixelHeight)),PixelFormats.Bgra32,null,0);
   int stride=small.PixelWidth*4;var pixels=new byte[stride*small.PixelHeight];small.CopyPixels(pixels,stride,0);
   long r=0,g=0,b=0;int count=pixels.Length/4;
   for(int i=0;i<pixels.Length;i+=4){b+=pixels[i];g+=pixels[i+1];r+=pixels[i+2];}
   if(count>0)tint=Color.FromRgb((byte)Math.Clamp(r/count*.6,20,115),(byte)Math.Clamp(g/count*.6,20,115),(byte)Math.Clamp(b/count*.6,20,135));
  }
  if(light)tint=Color.FromRgb((byte)(185+tint.R*.35),(byte)(185+tint.G*.35),(byte)(185+tint.B*.35));
  if(customTint!=null)tint=(Color)ColorConverter.ConvertFromString(customTint);
  var middle=light?Color.FromRgb((byte)(210+tint.R*.15),(byte)(210+tint.G*.15),(byte)(210+tint.B*.15)):Color.FromRgb((byte)(tint.R*.55),(byte)(tint.G*.55),(byte)(tint.B*.65));
  var gradient=new LinearGradientBrush(new GradientStopCollection{new(tint,0),new(middle,.55),new(canvas,1)},new Point(0,0),new Point(1,1));gradient.Freeze();return gradient;
 }
}
