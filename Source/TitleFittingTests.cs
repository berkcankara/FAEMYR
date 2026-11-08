using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ahenk;

public static class TitleFittingTests
{
 public static void Run(Action<bool,string> check)
 {
  const string title="10th Anniversary Bad Apple!! PHASE 3";
  var text=new FittingTextBlock{Text=title,FontFamily=new FontFamily("Segoe UI"),FontWeight=FontWeights.Bold,
   PreferredFontSize=52,FontSize=52,TextWrapping=TextWrapping.Wrap,MaxHeight=170};
  void Measure(double width){text.Measure(new Size(width,double.PositiveInfinity));text.Arrange(new Rect(0,0,width,text.DesiredSize.Height));}
  bool Complete(double width)
  {
   var probe=new TextBlock{Text=text.Text,FontFamily=text.FontFamily,FontWeight=text.FontWeight,
    FontSize=text.FontSize,TextWrapping=TextWrapping.Wrap};
   probe.Measure(new Size(width,double.PositiveInfinity));
   return probe.DesiredSize.Height<=170&&probe.DesiredSize.Width<=width;
  }
  Measure(850);check(text.FontSize==52&&Complete(850),"Long album title keeps preferred size when it fits");
  Measure(380);double smaller=text.FontSize;check(smaller<52&&Complete(380),"Narrow title fits completely without ellipsis");
  Measure(100);check(text.FontSize<smaller&&Complete(100),"Very narrow title respects both width and height");
  Measure(850);check(text.FontSize==52&&Complete(850),"Widening restores original title size");
  text.Text="Abyss";Measure(200);check(text.FontSize==52,"Short title does not shrink just because window narrows");
  text.Text="音楽の記憶 — Çok uzun bir albüm adı 🎵 第三楽章";Measure(150);check(Complete(150),"Unicode album title fits completely");
  text.Text=new string('W',100);Measure(150);check(Complete(150),"Unbroken title wraps and fits within its own column");
  text.Text=title;text.Measure(new Size(150,75));
  var constrained=new TextBlock{Text=title,FontFamily=text.FontFamily,FontWeight=text.FontWeight,FontSize=text.FontSize,TextWrapping=TextWrapping.Wrap};
  constrained.Measure(new Size(150,double.PositiveInfinity));check(constrained.DesiredSize.Height<=75,"Title also fits reduced height after artist summary wraps");
 }
}
