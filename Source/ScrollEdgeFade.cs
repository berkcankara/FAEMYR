using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
  if((bool)args.NewValue&&(target is ScrollViewer or TextBox or ItemsControl))target.SetValue(ControllerProperty,new Controller(target));
 }
 sealed class Controller:IDisposable
 {
  readonly FrameworkElement target;FrameworkElement masked;Brush? original;ScrollViewer? scroll;bool updating; (double Height,bool Top,bool Bottom)? previous;
  LinearGradientBrush? activeMask;
  public Controller(FrameworkElement target){this.target=target;masked=target;original=target.OpacityMask;target.Loaded+=Loaded;target.SizeChanged+=Resized;Update();}
  void Loaded(object sender,RoutedEventArgs args)=>Update();
  void Resized(object sender,SizeChangedEventArgs args)=>Update();
  void Scrolled(object sender,ScrollChangedEventArgs args){if(args.OriginalSource==scroll)Update();}
  void Update()
  {
   if(updating)return;updating=true;
   try
   {
    var viewer=target as ScrollViewer;if(target is TextBox text){text.ApplyTemplate();viewer=text.Template?.FindName("PART_ContentHost",text) as ScrollViewer;}
    if(target is ItemsControl items){items.ApplyTemplate();viewer=FindViewer(items);}
    if(!ReferenceEquals(viewer,scroll)){if(scroll!=null)scroll.ScrollChanged-=Scrolled;scroll=viewer;if(scroll!=null)scroll.ScrollChanged+=Scrolled;previous=null;}
    FrameworkElement viewport=target;
    if(target is ItemsControl&&scroll!=null){scroll.ApplyTemplate();viewport=scroll.Template?.FindName("PART_ScrollContentPresenter",scroll) as FrameworkElement??target;}
    if(!ReferenceEquals(masked,viewport)){StopAnimations();activeMask=null;masked.OpacityMask=original;masked=viewport;original=masked.OpacityMask;previous=null;}
    double height=masked.ActualHeight;bool top=scroll!=null&&scroll.VerticalOffset>.5,bottom=scroll!=null&&scroll.ScrollableHeight-scroll.VerticalOffset>.5;var key=(Math.Round(height,1),top,bottom);if(previous==key)return;previous=key;
    if(height<1){StopAnimations();activeMask=null;masked.OpacityMask=original;return;}
    // Fade the actual viewport to transparency, so any album gradient remains visible.
    // Keep the first/last line opaque when it reaches its real scroll boundary.
    double edge=Math.Min(.25,56/height);
    if(activeMask==null)
    {
     var stops=new GradientStopCollection();
     for(int i=0;i<=8;i++)stops.Add(new(Colors.Black,edge*i/8d));
     for(int i=8;i>=0;i--)stops.Add(new(Colors.Black,1-edge*i/8d));
     activeMask=new LinearGradientBrush(stops,new Point(0,0),new Point(0,target is ItemsControl?height:1));
     masked.OpacityMask=activeMask;
    }
    var mask=activeMask;mask.EndPoint=new Point(0,target is ItemsControl?height:1);
    // A virtualizing presenter can have descendant bounds as tall as the whole
    // collection. Anchor its mask to viewport pixels, not those content bounds.
    if(target is ItemsControl)mask.MappingMode=BrushMappingMode.Absolute;
    for(int side=0;side<2;side++)for(int i=0;i<=8;i++)
    {
     double t=(side==0?i:8-i)/8d;var stop=mask.GradientStops[side*9+i];
     stop.Offset=side==0?edge*t:1-edge*t;
     byte alpha=(byte)Math.Round(255*(1-Math.Cos(Math.PI*t))/2);
     var color=Color.FromArgb((side==0?top:bottom)?alpha:(byte)255,0,0,0);
     var current=stop.Color;stop.BeginAnimation(GradientStop.ColorProperty,null);stop.Color=color;
     if(SystemParameters.ClientAreaAnimation&&current!=color)
      stop.BeginAnimation(GradientStop.ColorProperty,new ColorAnimation(current,color,TimeSpan.FromMilliseconds(320)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop},HandoffBehavior.SnapshotAndReplace);
    }
   }
   finally{updating=false;}
  }
  static ScrollViewer? FindViewer(DependencyObject parent)
  {
   for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
   {
    var child=VisualTreeHelper.GetChild(parent,i);if(child is ScrollViewer viewer)return viewer;
    if(FindViewer(child) is {} found)return found;
   }
   return null;
  }
  void StopAnimations(){if(activeMask!=null)foreach(var stop in activeMask.GradientStops)stop.BeginAnimation(GradientStop.ColorProperty,null);}
  public void Dispose(){target.Loaded-=Loaded;target.SizeChanged-=Resized;if(scroll!=null)scroll.ScrollChanged-=Scrolled;StopAnimations();masked.OpacityMask=original;}
 }
}
