using System.Windows;
using System.Windows.Media;

namespace Faemyr;

// Keep every edge inside the drawing bounds. Tint the brush directly so this
// tiny caption glyph never needs a translucent intermediate rendering layer.
public sealed class CaptionPanelGlyph : FrameworkElement
{
 public static readonly DependencyProperty BrushProperty=DependencyProperty.Register(nameof(Brush),typeof(Brush),typeof(CaptionPanelGlyph),new FrameworkPropertyMetadata(Brushes.White,FrameworkPropertyMetadataOptions.AffectsRender));
 public static readonly DependencyProperty SurfaceProperty=DependencyProperty.Register(nameof(Surface),typeof(Brush),typeof(CaptionPanelGlyph),new FrameworkPropertyMetadata(Brushes.Black,FrameworkPropertyMetadataOptions.AffectsRender));
 public static readonly DependencyProperty PanelShownProperty=DependencyProperty.Register(nameof(PanelShown),typeof(bool),typeof(CaptionPanelGlyph),new FrameworkPropertyMetadata(true,FrameworkPropertyMetadataOptions.AffectsRender));
 public Brush Brush {get=>(Brush)GetValue(BrushProperty);set=>SetValue(BrushProperty,value);}
 public Brush Surface {get=>(Brush)GetValue(SurfaceProperty);set=>SetValue(SurfaceProperty,value);}
 public bool PanelShown {get=>(bool)GetValue(PanelShownProperty);set=>SetValue(PanelShownProperty,value);}

 protected override void OnRender(DrawingContext drawing)
 {
  base.OnRender(drawing);
  var ink=(Brush as SolidColorBrush)?.Color??Colors.White;
  var background=(Surface as SolidColorBrush)?.Color??Colors.Black;
  byte Tone(byte foreground,byte back)=>(byte)Math.Round(foreground*.75+back*.25);
  var color=new SolidColorBrush(Color.FromRgb(Tone(ink.R,background.R),Tone(ink.G,background.G),Tone(ink.B,background.B)));
  color.Freeze();
  var guides=new GuidelineSet(new[]{0d,1d,4d,5d,8d,9d,12d,13d},new[]{0d,1d,12d,13d});
  drawing.PushGuidelineSet(guides);
  drawing.DrawRectangle(color,null,new Rect(0,0,13,1));
  drawing.DrawRectangle(color,null,new Rect(0,12,13,1));
  drawing.DrawRectangle(color,null,new Rect(0,1,1,11));
  drawing.DrawRectangle(color,null,new Rect(12,1,1,11));
  drawing.DrawRectangle(color,null,new Rect(PanelShown?8:4,1,1,11));
  drawing.Pop();
 }
}
