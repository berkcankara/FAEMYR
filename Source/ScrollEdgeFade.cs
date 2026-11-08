using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Ahenk;
public static class ScrollEdgeFade
{
 public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(ScrollEdgeFade),new PropertyMetadata(false,Changed));
 static readonly DependencyProperty ControllerProperty=DependencyProperty.RegisterAttached("Controller",typeof(Controller),typeof(ScrollEdgeFade));
 public static bool GetEnabled(DependencyObject node)=>(bool)node.GetValue(EnabledProperty);
 public static void SetEnabled(DependencyObject node,bool value)=>node.SetValue(EnabledProperty,value);
 static void Changed(DependencyObject node,DependencyPropertyChangedEventArgs args)
 {
  if(node is not FrameworkElement target)return;
  if(target.GetValue(ControllerProperty) is Controller previous){previous.Dispose();target.ClearValue(ControllerProperty);}
  if((bool)args.NewValue&&(target is ScrollViewer or TextBox))target.SetValue(ControllerProperty,new Controller(target));
 }
 sealed class Controller:IDisposable
 {
  readonly FrameworkElement target;readonly Brush? original;ScrollViewer? scroll;bool updating; (double Height,bool Top,bool Bottom)? previous;
  public Controller(FrameworkElement target){this.target=target;original=target.OpacityMask;target.Loaded+=Loaded;target.SizeChanged+=Resized;Update();}
  void Loaded(object sender,RoutedEventArgs args)=>Update();
  void Resized(object sender,SizeChangedEventArgs args)=>Update();
  void Scrolled(object sender,ScrollChangedEventArgs args){if(args.OriginalSource==scroll)Update();}
  void Update()
  {
   if(updating)return;updating=true;
   try
   {
    var viewer=target as ScrollViewer;if(target is TextBox text){text.ApplyTemplate();viewer=text.Template?.FindName("PART_ContentHost",text) as ScrollViewer;}
    if(!ReferenceEquals(viewer,scroll)){if(scroll!=null)scroll.ScrollChanged-=Scrolled;scroll=viewer;if(scroll!=null)scroll.ScrollChanged+=Scrolled;previous=null;}
    double height=target.ActualHeight;bool top=scroll!=null&&scroll.VerticalOffset>.5,bottom=scroll!=null&&scroll.ScrollableHeight-scroll.VerticalOffset>.5;var key=(Math.Round(height,1),top,bottom);if(previous==key)return;previous=key;
    if(height<1||!top&&!bottom){target.OpacityMask=original;return;}
    // Fade the actual viewport to transparency, so any album gradient remains visible.
    // Keep the first/last line opaque when it reaches its real scroll boundary.
    double edge=Math.Min(.25,56/height);var stops=new List<GradientStop>();
    if(top)for(int i=0;i<=8;i++){double t=i/8d;byte alpha=(byte)Math.Round(255*(1-Math.Cos(Math.PI*t))/2);stops.Add(new(Color.FromArgb(alpha,0,0,0),edge*t));}else stops.Add(new(Colors.Black,0));
    if(bottom)for(int i=0;i<=8;i++){double t=i/8d;byte alpha=(byte)Math.Round(255*(1-Math.Cos(Math.PI*t))/2);stops.Add(new(Color.FromArgb(alpha,0,0,0),1-edge*t));}else stops.Add(new(Colors.Black,1));
    var mask=new LinearGradientBrush(new GradientStopCollection(stops.OrderBy(stop=>stop.Offset)),new Point(0,0),new Point(0,1));mask.Freeze();target.OpacityMask=mask;
   }
   finally{updating=false;}
  }
  public void Dispose(){target.Loaded-=Loaded;target.SizeChanged-=Resized;if(scroll!=null)scroll.ScrollChanged-=Scrolled;target.OpacityMask=original;}
 }
}
