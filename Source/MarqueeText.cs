using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace Faemyr;
// Only the overflowing title moves. Animation runs in WPF's compositor, with
// a reading pause at both ends and no per-frame timer or layout invalidation.
public sealed class MarqueeText : Control
{
 readonly TextBlock text=new(){TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.None};
 readonly TranslateTransform slide=new();Window? owner;string motion="";
 public static readonly DependencyProperty TextProperty=DependencyProperty.Register(nameof(Text),typeof(string),typeof(MarqueeText),new FrameworkPropertyMetadata("",FrameworkPropertyMetadataOptions.AffectsMeasure));
 public static readonly DependencyProperty MotionEnabledProperty=DependencyProperty.Register(nameof(MotionEnabled),typeof(bool),typeof(MarqueeText),new PropertyMetadata(true,(view,_)=>((MarqueeText)view).Refresh()));
 public string Text{get=>(string)GetValue(TextProperty);set=>SetValue(TextProperty,value);}
 public bool MotionEnabled{get=>(bool)GetValue(MotionEnabledProperty);set=>SetValue(MotionEnabledProperty,value);}
 public bool IsOverflowing{get;private set;}public bool IsAnimating{get;private set;}
 public double ScrollOffset=>slide.X;
 public MarqueeText()
 {
  Focusable=false;ClipToBounds=true;AddVisualChild(text);AddLogicalChild(text);text.RenderTransform=slide;text.SetBinding(TextBlock.ForegroundProperty,new System.Windows.Data.Binding(nameof(Foreground)){Source=this});
  Loaded+=(_,_)=>{owner=Window.GetWindow(this);if(owner!=null)owner.StateChanged+=OwnerState;Refresh();};Unloaded+=(_,_)=>{if(owner!=null)owner.StateChanged-=OwnerState;owner=null;Stop();};IsVisibleChanged+=(_,_)=>Refresh();
 }
 void OwnerState(object? sender,EventArgs e)=>Refresh();
 protected override int VisualChildrenCount=>1;
 protected override Visual GetVisualChild(int index)=>index==0?text:throw new ArgumentOutOfRangeException(nameof(index));
 protected override System.Collections.IEnumerator LogicalChildren=>new[]{text}.GetEnumerator();
 protected override Size MeasureOverride(Size available)
 {
  text.Text=Text;text.FontSize=FontSize;text.FontFamily=FontFamily;text.FontWeight=FontWeight;text.FontStyle=FontStyle;text.FontStretch=FontStretch;text.FlowDirection=FlowDirection;
  text.Measure(new Size(double.PositiveInfinity,available.Height));return new Size(double.IsFinite(available.Width)?Math.Min(available.Width,text.DesiredSize.Width):text.DesiredSize.Width,text.DesiredSize.Height);
 }
 protected override Size ArrangeOverride(Size size){text.Arrange(new Rect(0,Math.Max(0,(size.Height-text.DesiredSize.Height)/2),text.DesiredSize.Width,text.DesiredSize.Height));Refresh(size.Width);return size;}
 void Refresh(double? width=null)
 {
  double overflow=Math.Max(0,text.DesiredSize.Width-(width??ActualWidth));IsOverflowing=overflow>1;
  if(!MotionEnabled||!IsLoaded||!IsVisible||owner?.WindowState==WindowState.Minimized||!IsOverflowing){Stop();return;}
  string key=Text+"|"+Math.Round(overflow,2)+"|"+FontSize;if(motion==key&&IsAnimating)return;Stop();motion=key;
  double travel=Math.Max(.6,overflow/32),hold=1.2;var animation=new DoubleAnimationUsingKeyFrames{RepeatBehavior=RepeatBehavior.Forever};
  animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.Zero)));
  animation.KeyFrames.Add(new LinearDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold))));
  animation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold+travel))));
  animation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold*2+travel))));
  animation.KeyFrames.Add(new LinearDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold*2+travel*2))));
  slide.BeginAnimation(TranslateTransform.XProperty,animation);IsAnimating=true;
 }
 void Stop(){slide.BeginAnimation(TranslateTransform.XProperty,null);slide.X=0;IsAnimating=false;motion="";}
}
