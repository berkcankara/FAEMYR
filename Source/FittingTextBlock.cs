using System.Windows;
using System.Windows.Controls;

namespace Faemyr;

// Measure the complete wrapped title before choosing a font size; never trim it.
public sealed class FittingTextBlock : Control
{
 readonly TextBlock content=new(){TextTrimming=TextTrimming.None};
 public static readonly DependencyProperty TextProperty=DependencyProperty.Register(
  nameof(Text),typeof(string),typeof(FittingTextBlock),new FrameworkPropertyMetadata("",FrameworkPropertyMetadataOptions.AffectsMeasure));
 public string Text {get=>(string)GetValue(TextProperty);set=>SetValue(TextProperty,value);}
 public static readonly DependencyProperty TextWrappingProperty=DependencyProperty.Register(
  nameof(TextWrapping),typeof(TextWrapping),typeof(FittingTextBlock),new FrameworkPropertyMetadata(TextWrapping.Wrap,FrameworkPropertyMetadataOptions.AffectsMeasure));
 public TextWrapping TextWrapping {get=>(TextWrapping)GetValue(TextWrappingProperty);set=>SetValue(TextWrappingProperty,value);}
 public static readonly DependencyProperty PreferredFontSizeProperty=DependencyProperty.Register(
  nameof(PreferredFontSize),typeof(double),typeof(FittingTextBlock),
  new FrameworkPropertyMetadata(52d,FrameworkPropertyMetadataOptions.AffectsMeasure),
  value=>value is double size&&double.IsFinite(size)&&size>=1);
 public double PreferredFontSize {get=>(double)GetValue(PreferredFontSizeProperty);set=>SetValue(PreferredFontSizeProperty,value);}

 public FittingTextBlock(){Focusable=false;IsTabStop=false;AddVisualChild(content);AddLogicalChild(content);content.SetBinding(TextBlock.ForegroundProperty,new System.Windows.Data.Binding(nameof(Foreground)){Source=this});}
 protected override int VisualChildrenCount=>1;
 protected override System.Windows.Media.Visual GetVisualChild(int index)=>index==0?content:throw new ArgumentOutOfRangeException(nameof(index));
 protected override System.Collections.IEnumerator LogicalChildren=>new[]{content}.GetEnumerator();

 protected override Size MeasureOverride(Size availableSize)
 {
  content.Text=Text;content.FontFamily=FontFamily;content.FontWeight=FontWeight;content.FontStyle=FontStyle;
  content.FontStretch=FontStretch;content.TextWrapping=TextWrapping;content.FlowDirection=FlowDirection;
  content.Language=Language;
  double width=availableSize.Width,height=Math.Min(availableSize.Height,MaxHeight);
  if(width>0&&height>0&&double.IsFinite(width)&&!string.IsNullOrEmpty(Text))
  {
   var probe=new TextBlock{Text=Text,FontFamily=FontFamily,FontWeight=FontWeight,FontStyle=FontStyle,
    FontStretch=FontStretch,TextWrapping=TextWrapping,TextTrimming=TextTrimming.None,
    FlowDirection=FlowDirection,Language=Language};
   bool Fits(double size)
   {
    probe.FontSize=size;probe.Measure(new Size(width,double.PositiveInfinity));
    return probe.DesiredSize.Width<=width&&probe.DesiredSize.Height<=height;
   }
   double size=PreferredFontSize;
   if(!Fits(size))
   {
    double low=1,high=size;
    for(int i=0;i<16;i++){double mid=(low+high)/2;if(Fits(mid))low=mid;else high=mid;}
    size=low;
   }
   if(FontSize!=size)SetCurrentValue(FontSizeProperty,size);
  }
  else if(FontSize!=PreferredFontSize)SetCurrentValue(FontSizeProperty,PreferredFontSize);
  content.FontSize=FontSize;content.Measure(availableSize);return content.DesiredSize;
 }
 protected override Size ArrangeOverride(Size finalSize){content.Arrange(new Rect(finalSize));return finalSize;}
}
