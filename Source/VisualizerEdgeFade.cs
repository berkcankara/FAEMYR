using System.Windows;
using System.Windows.Media;

namespace Ahenk;
public sealed partial class OscilloscopeView
{
 Brush? plotEdgeMask;double plotEdgeLeft,plotEdgeWidth;
 Brush PlotEdgeMask(double left,double width)
 {
  if(plotEdgeMask!=null&&plotEdgeLeft==left&&plotEdgeWidth==width)return plotEdgeMask;
  double edge=Math.Min(.25,28/Math.Max(1,width));var stops=new GradientStopCollection();
  for(int i=0;i<=8;i++)
  {
   double t=i/8d;byte alpha=(byte)Math.Round(255*(1-Math.Cos(Math.PI*t))/2);
   stops.Add(new GradientStop(Color.FromArgb(alpha,0,0,0),edge*t));
   stops.Add(new GradientStop(Color.FromArgb(alpha,0,0,0),1-edge*t));
  }
  var mask=new LinearGradientBrush(new GradientStopCollection(stops.OrderBy(stop=>stop.Offset)),new Point(left,0),new Point(left+Math.Max(1,width),0)){MappingMode=BrushMappingMode.Absolute};
  mask.Freeze();plotEdgeLeft=left;plotEdgeWidth=width;return plotEdgeMask=mask;
 }
}
