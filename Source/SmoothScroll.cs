using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Faemyr;
public static class SmoothScroll
{
 public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(SmoothScroll),new PropertyMetadata(false,Changed));
 public static bool GetEnabled(DependencyObject o)=>(bool)o.GetValue(EnabledProperty);
 public static void SetEnabled(DependencyObject o,bool value)=>o.SetValue(EnabledProperty,value);
 static readonly DependencyProperty MotionProperty=DependencyProperty.RegisterAttached("Motion",typeof(Motion),typeof(SmoothScroll));
 static Motion Controller(ScrollViewer viewer){if(viewer.GetValue(MotionProperty) is Motion saved)return saved;var motion=new Motion(viewer);viewer.SetValue(MotionProperty,motion);return motion;}
 static void Changed(DependencyObject o,DependencyPropertyChangedEventArgs e){if(o is ScrollViewer viewer){if((bool)e.NewValue)Controller(viewer);else Cancel(viewer);}}
 public static void To(ScrollViewer viewer,double offset)=>Controller(viewer).To(offset);
 public static void Cancel(ScrollViewer viewer){if(viewer.GetValue(MotionProperty) is Motion motion)motion.Stop();}
 public static double Step(double current,double target,double seconds)=>current+(target-current)*(1-Math.Exp(-Math.Clamp(seconds,0,.05)/.065));
 sealed class Motion
 {
  readonly ScrollViewer viewer;bool moving;double target,timeConstant=.065;long last;TimeSpan? lastFrame;
  public Motion(ScrollViewer v){viewer=v;v.PreviewMouseWheel+=Wheel;v.PreviewMouseDown+=(_,_)=>Stop();v.PreviewKeyDown+=(_,_)=>Stop();v.Unloaded+=(_,_)=>Stop();}
  void Wheel(object s,MouseWheelEventArgs e)
  {
   if(!GetEnabled(viewer)||Keyboard.Modifiers!=ModifierKeys.None)return;
   DependencyObject? node=e.OriginalSource as DependencyObject;
   while(node!=null){if(node is ScrollViewer inner&&inner.ScrollableHeight>0&&GetEnabled(inner)){if(inner!=viewer)return;break;}node=node is Visual||node is System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);}
   if(viewer.ScrollableHeight<=0)return;
   double delta=-e.Delta/120.0*96;
   // Drop unconsumed momentum on a direction reversal instead of requiring
   // several wheel ticks to cancel a scroll toward the opposite boundary.
   target=WheelTarget(viewer.VerticalOffset,target,delta,viewer.ScrollableHeight,moving);
   double next=target;
   if(Math.Abs(next-viewer.VerticalOffset)<.1&&!moving)return;
   timeConstant=.065;target=next;e.Handled=true;Start();
  }
  public void To(double offset)
  {
   if(!double.IsFinite(offset))return;target=Math.Clamp(offset,0,viewer.ScrollableHeight);timeConstant=.14;
   if(Math.Abs(target-viewer.VerticalOffset)<.35){Stop();return;}Start();
  }
  void Start(){if(moving)return;moving=true;last=Stopwatch.GetTimestamp();lastFrame=null;CompositionTarget.Rendering+=Frame;}
  void Frame(object? s,EventArgs e)
  {
   if(!viewer.IsVisible){Stop();return;}
   if(e is RenderingEventArgs frame){if(lastFrame==frame.RenderingTime)return;lastFrame=frame.RenderingTime;}
   target=Math.Clamp(target,0,viewer.ScrollableHeight);long stamp=Stopwatch.GetTimestamp();double dt=(stamp-last)/(double)Stopwatch.Frequency;last=stamp;
   double next=viewer.VerticalOffset+(target-viewer.VerticalOffset)*(1-Math.Exp(-Math.Clamp(dt,0,.1)/timeConstant));viewer.ScrollToVerticalOffset(next);
   if(Math.Abs(next-target)<.35){viewer.ScrollToVerticalOffset(target);Stop();}
  }
  public void Stop(){if(!moving)return;CompositionTarget.Rendering-=Frame;moving=false;}
 }
 internal static double WheelTarget(double offset,double pending,double delta,double limit,bool moving)
 {
  if(!moving||Math.Sign(pending-offset)!=Math.Sign(delta))pending=offset;
  return Math.Clamp(pending+delta,0,Math.Max(0,limit));
 }
}
