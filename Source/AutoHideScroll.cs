using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ahenk;
public static class AutoHideScroll
{
 public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(AutoHideScroll),new PropertyMetadata(false,Changed));
 public static readonly DependencyProperty VisibleProperty=DependencyProperty.RegisterAttached("Visible",typeof(bool),typeof(AutoHideScroll),new PropertyMetadata(false));
 public static bool GetEnabled(DependencyObject o)=>(bool)o.GetValue(EnabledProperty);
 public static void SetEnabled(DependencyObject o,bool v)=>o.SetValue(EnabledProperty,v);
 public static bool GetVisible(DependencyObject o)=>(bool)o.GetValue(VisibleProperty);
 public static void SetVisible(DependencyObject o,bool v)=>o.SetValue(VisibleProperty,v);
 public static void Refresh(DependencyObject node)
 {
  if(node is ScrollBar bar&&GetEnabled(bar))SetVisible(bar,!InterfaceSettings.Current.AutoHideScrollbars||bar.IsMouseOver);
  for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)Refresh(VisualTreeHelper.GetChild(node,i));
 }
 static void Changed(DependencyObject o,DependencyPropertyChangedEventArgs e)
 {
  if(o is ScrollBar bar && (bool)e.NewValue) new Activity(bar);
 }
 sealed class Activity
 {
  readonly ScrollBar bar;readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1.5)};ScrollViewer? owner;
  public Activity(ScrollBar b){bar=b;bar.Loaded+=Loaded;bar.Unloaded+=Unloaded;bar.MouseEnter+=(_,_)=>Reveal();bar.MouseMove+=(_,_)=>Reveal();bar.MouseLeave+=(_,_)=>Reveal();timer.Tick+=(_,_)=>{if(bar.IsMouseOver||bar.IsMouseCaptureWithin)return;timer.Stop();SetVisible(bar,!InterfaceSettings.Current.AutoHideScrollbars);};}
  void Loaded(object s,RoutedEventArgs e){DependencyObject? node=bar;while(node!=null&&node is not ScrollViewer)node=VisualTreeHelper.GetParent(node);owner=node as ScrollViewer;if(owner!=null){owner.ScrollChanged+=Scrolled;owner.PreviewMouseWheel+=Wheel;}SetVisible(bar,!InterfaceSettings.Current.AutoHideScrollbars);}
  void Unloaded(object s,RoutedEventArgs e){timer.Stop();if(owner!=null){owner.ScrollChanged-=Scrolled;owner.PreviewMouseWheel-=Wheel;owner=null;}}
  void Wheel(object s,System.Windows.Input.MouseWheelEventArgs e)=>Reveal();
  void Scrolled(object s,ScrollChangedEventArgs e){if(e.OriginalSource==owner&&(e.VerticalChange!=0||e.HorizontalChange!=0))Reveal();}
  void Reveal(){SetVisible(bar,true);timer.Stop();if(InterfaceSettings.Current.AutoHideScrollbars){timer.Interval=TimeSpan.FromSeconds(InterfaceSettings.Current.ScrollbarDelay);timer.Start();}}
 }
}
