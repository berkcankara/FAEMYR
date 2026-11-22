using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Faemyr;
public sealed class RatingRequestedEventArgs(int rating):RoutedEventArgs(StarRating.RatingRequestedEvent){public int Rating{get;}=rating;}
public sealed class StarRating : FrameworkElement
{
 public static readonly RoutedEvent RatingRequestedEvent=EventManager.RegisterRoutedEvent("RatingRequested",RoutingStrategy.Bubble,typeof(RoutedEventHandler),typeof(StarRating));
 public static readonly DependencyProperty ValueProperty=DependencyProperty.Register(nameof(Value),typeof(int),typeof(StarRating),new FrameworkPropertyMetadata(0,FrameworkPropertyMetadataOptions.AffectsRender,ValueChanged),value=>(int)value is >=0 and <=5);
 public static readonly DependencyProperty BrushProperty=DependencyProperty.Register(nameof(Brush),typeof(Brush),typeof(StarRating),new FrameworkPropertyMetadata(Brushes.White,FrameworkPropertyMetadataOptions.AffectsRender));
 public int Value{get=>(int)GetValue(ValueProperty);set=>SetValue(ValueProperty,value);}
 public Brush Brush{get=>(Brush)GetValue(BrushProperty);set=>SetValue(BrushProperty,value);}
 static readonly Geometry star=Geometry.Parse("M12,2 L15.09,8.26 22,9.27 17,14.14 18.18,21.02 12,17.77 5.82,21.02 7,14.14 2,9.27 8.91,8.26 Z");
 int preview=-1;
 static StarRating(){star.Freeze();}
 public StarRating(){Focusable=true;Cursor=Cursors.Hand;Height=22;Width=92;UpdateDescription();MouseLeave+=(_,_)=>{preview=-1;InvalidateVisual();};GotKeyboardFocus+=(_,_)=>InvalidateVisual();LostKeyboardFocus+=(_,_)=>InvalidateVisual();}
 static void ValueChanged(DependencyObject sender,DependencyPropertyChangedEventArgs e)=>((StarRating)sender).UpdateDescription();
 void UpdateDescription(){ToolTip=Value==0?L.T("Yıldız ver · Aynı yıldıza tekrar tıklayarak kaldır"):L.T($"{Value}/5 yıldız · Aynı yıldıza tekrar tıklayarak kaldır");System.Windows.Automation.AutomationProperties.SetName(this,L.T($"Parça yıldızı: {Value}/5"));}
 public static int RatingAt(double x,double width)=>Math.Clamp((int)(x/Math.Max(1,width)*5)+1,1,5);
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);int value=RatingAt(e.GetPosition(this).X,ActualWidth);if(preview!=value){preview=value;InvalidateVisual();}}
 protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
 {
  base.OnMouseLeftButtonDown(e);Focus();int selected=RatingAt(e.GetPosition(this).X,ActualWidth);preview=-1;RaiseEvent(new RatingRequestedEventArgs(selected==Value?0:selected));InvalidateVisual();e.Handled=true;
 }
 protected override void OnKeyDown(KeyEventArgs e)
 {
  base.OnKeyDown(e);int? selected=e.Key switch{Key.Left=>Math.Max(0,Value-1),Key.Right=>Math.Min(5,Value+1),Key.Delete or Key.Back=>0,Key.Space=>(Value+1)%6,_=>null};
  if(e.Key>=Key.D1&&e.Key<=Key.D5)selected=e.Key-Key.D0;
  if(e.Key>=Key.NumPad1&&e.Key<=Key.NumPad5)selected=e.Key-Key.NumPad0;
  if(selected.HasValue){RaiseEvent(new RatingRequestedEventArgs(selected.Value));e.Handled=true;}
 }
 protected override Size MeasureOverride(Size available)=>new(Width,Height);
 protected override void OnRender(DrawingContext drawing)
 {
  base.OnRender(drawing);drawing.DrawRectangle(Brushes.Transparent,null,new Rect(RenderSize));int active=preview>=0?preview:Value;double step=ActualWidth/5,size=Math.Max(1,Math.Min(step-2,ActualHeight-4));
  var muted=Brush.CloneCurrentValue();muted.Opacity*=.26;muted.Freeze();
  for(int i=0;i<5;i++)
  {
   drawing.PushTransform(new TranslateTransform(i*step+(step-size)/2,(ActualHeight-size)/2));drawing.PushTransform(new ScaleTransform(size/24,size/24));
   drawing.DrawGeometry(i<active?Brush:null,new Pen(i<active?Brush:muted,1.5){LineJoin=PenLineJoin.Round},star);drawing.Pop();drawing.Pop();
  }
  if(IsKeyboardFocused)drawing.DrawRoundedRectangle(null,new Pen(muted,1),new Rect(.5,.5,Math.Max(0,ActualWidth-1),Math.Max(0,ActualHeight-1)),4,4);
 }
}
