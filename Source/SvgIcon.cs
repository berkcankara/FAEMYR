using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace Faemyr;
// The bundled SVG paths render directly as native vectors at any Windows display scale.
public sealed class SvgIcon : FrameworkElement
{
 public static readonly DependencyProperty IconProperty=DependencyProperty.Register(nameof(Icon),typeof(string),typeof(SvgIcon),new FrameworkPropertyMetadata("play",FrameworkPropertyMetadataOptions.AffectsRender));
 public static readonly DependencyProperty BrushProperty=DependencyProperty.Register(nameof(Brush),typeof(Brush),typeof(SvgIcon),new FrameworkPropertyMetadata(Brushes.White,FrameworkPropertyMetadataOptions.AffectsRender));
 public string Icon{get=>(string)GetValue(IconProperty);set=>SetValue(IconProperty,value);}
 public Brush Brush{get=>(Brush)GetValue(BrushProperty);set=>SetValue(BrushProperty,value);}
 record Shape(Geometry Geometry,bool Fill,double Stroke);
 static readonly Dictionary<string,List<Shape>> cache=[];
 static List<Shape> Read(string name)
 {
  if(cache.TryGetValue(name,out var shapes))return shapes;
  var assembly=Assembly.GetExecutingAssembly();using var stream=assembly.GetManifestResourceStream("Faemyr.Assets.Icons."+name+".svg");shapes=[];
  if(stream!=null)foreach(var p in XDocument.Load(stream).Descendants().Where(e=>e.Name.LocalName=="path")){var g=Geometry.Parse((string?)p.Attribute("d")??"");g.Freeze();shapes.Add(new(g,(string?)p.Attribute("fill")!="none",double.TryParse((string?)p.Attribute("stroke-width"),CultureInfo.InvariantCulture,out var width)?width:1.8));}
  return cache[name]=shapes;
 }
 static readonly Dictionary<string,ImageSource> shellImages=[];
 public static ImageSource ShellImage(string name)
 {
  if(shellImages.TryGetValue(name,out var saved))return saved;
  var group=new DrawingGroup();group.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(0,0,24,24))));
  foreach(var shape in Read(name))group.Children.Add(new GeometryDrawing(shape.Fill?Brushes.White:null,new Pen(shape.Fill?Brushes.Black:Brushes.White,shape.Fill?.6:shape.Stroke){LineJoin=PenLineJoin.Round},shape.Geometry));
  group.Freeze();var image=new DrawingImage(group);image.Freeze();return shellImages[name]=image;
 }
 protected override Size MeasureOverride(Size available)=>new(double.IsNaN(Width)?24:Width,double.IsNaN(Height)?24:Height);
 protected override void OnRender(DrawingContext dc)
 {
  base.OnRender(dc);double scale=Math.Min(ActualWidth,ActualHeight)/24;dc.PushTransform(new TranslateTransform((ActualWidth-scale*24)/2,(ActualHeight-scale*24)/2));dc.PushTransform(new ScaleTransform(scale,scale));
  foreach(var shape in Read(Icon)){var pen=shape.Fill?null:new Pen(Brush,shape.Stroke){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};dc.DrawGeometry(shape.Fill?Brush:null,pen,shape.Geometry);}dc.Pop();dc.Pop();
 }
}
