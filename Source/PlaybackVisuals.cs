using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace Faemyr;
public static class PlaybackVisuals
{
 public static void Update(IEnumerable<Track> tracks,Track? current,bool playing)
 {
  foreach(var t in tracks){bool active=current!=null&&t.Path.Equals(current.Path,StringComparison.OrdinalIgnoreCase);t.IsCurrent=active;t.IsAnimating=active&&playing;}
 }
}
public sealed class PlayingIndicator : FrameworkElement
{
 public static readonly DependencyProperty RunningProperty=DependencyProperty.Register(nameof(Running),typeof(bool),typeof(PlayingIndicator),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender,Changed));
 public static readonly DependencyProperty BrushProperty=DependencyProperty.Register(nameof(Brush),typeof(Brush),typeof(PlayingIndicator),new FrameworkPropertyMetadata(Brushes.YellowGreen,FrameworkPropertyMetadataOptions.AffectsRender));
 public bool Running{get=>(bool)GetValue(RunningProperty);set=>SetValue(RunningProperty,value);}
 public Brush Brush{get=>(Brush)GetValue(BrushProperty);set=>SetValue(BrushProperty,value);}
 bool subscribed;
 public PlayingIndicator(){Loaded+=(_,_)=>Subscribe();Unloaded+=(_,_)=>Subscribe();IsVisibleChanged+=(_,_)=>Subscribe();}
 static void Changed(DependencyObject o,DependencyPropertyChangedEventArgs e)=>((PlayingIndicator)o).Subscribe();
 void Subscribe(){bool run=Running&&IsLoaded&&IsVisible;if(run==subscribed)return;if(run)CompositionTarget.Rendering+=Frame;else CompositionTarget.Rendering-=Frame;subscribed=run;InvalidateVisual();}
 void Frame(object? s,EventArgs e)=>InvalidateVisual();
 protected override void OnRender(DrawingContext dc){base.OnRender(dc);double time=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;for(int i=0;i<4;i++){double fraction=Running ? .25+.75*(.5+.5*Math.Sin(time*(5+i*.9)+i*1.8)):.3+i*.15;double h=Math.Max(2,ActualHeight*fraction);dc.DrawRoundedRectangle(Brush,null,new Rect(i*ActualWidth/4,ActualHeight-h,Math.Max(1,ActualWidth/4-1.5),h),.7,.7);}}
}
