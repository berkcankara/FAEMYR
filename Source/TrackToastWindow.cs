using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
namespace Faemyr;
public sealed class TrackToastWindow : Window
{
 public const double HoldSeconds=2,FadeSeconds=.7;
 readonly ThumbnailImage cover=new(){Width=56,Height=56,Radius=8,Stretch=Stretch.UniformToFill};
 readonly TextBlock title=new(){FontSize=16,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
 readonly TextBlock artist=new(){FontSize=13,Margin=new Thickness(0,5,0,0),TextTrimming=TextTrimming.CharacterEllipsis};
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(80)};readonly Func<bool> suppressed;long shown;bool fading;int generation;
 public TrackToastWindow(Func<bool>? suppressed=null)
 {
  this.suppressed=suppressed??DesktopOverlayPolicy.Suppressed;Title="Faemyr — Now playing";Width=380;Height=90;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;Focusable=false;
  var border=new Border{CornerRadius=new CornerRadius(14),Padding=new Thickness(16),BorderThickness=new Thickness(1)};border.SetResourceReference(Border.BackgroundProperty,"Surface");border.SetResourceReference(Border.BorderBrushProperty,"Line");Content=border;
  var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(56)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});border.Child=row;
  var art=new Grid();var fallback=new SvgIcon{Icon="music",Width=25,Height=25};fallback.SetResourceReference(SvgIcon.BrushProperty,"Muted");art.Children.Add(fallback);art.Children.Add(cover);row.Children.Add(art);
  var text=new StackPanel{Margin=new Thickness(14,4,0,0),VerticalAlignment=VerticalAlignment.Center};title.SetResourceReference(TextBlock.ForegroundProperty,"Ink");artist.SetResourceReference(TextBlock.ForegroundProperty,"Muted");text.Children.Add(title);text.Children.Add(artist);Grid.SetColumn(text,1);row.Children.Add(text);
  SourceInitialized+=(_,_)=>DesktopOverlayPolicy.NonActivating(this);DpiChanged+=(_,_)=>Dispatcher.BeginInvoke(()=>DesktopOverlayPolicy.TopRight(this));Closed+=(_,_)=>timer.Stop();timer.Tick+=(_,_)=>Tick();
 }
 public void Present(Track track)
 {
  if(suppressed()){Dismiss();return;}generation++;BeginAnimation(OpacityProperty,null);Opacity=1;fading=false;title.Text=track.Title;artist.Text=track.Artist;cover.Path=track.Cover;Show();DesktopOverlayPolicy.TopRight(this);DesktopOverlayPolicy.RaiseWithoutActivation(this);shown=Stopwatch.GetTimestamp();timer.Start();
 }
 void Tick()
 {
  if(suppressed()){Dismiss();return;}if(fading||Stopwatch.GetElapsedTime(shown).TotalSeconds<HoldSeconds)return;
  fading=true;int expected=generation;var animation=new DoubleAnimation(0,TimeSpan.FromSeconds(FadeSeconds)){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseInOut}};animation.Completed+=(_,_)=>{if(generation==expected)Dismiss();};BeginAnimation(OpacityProperty,animation);
 }
 public void Dismiss(){generation++;timer.Stop();BeginAnimation(OpacityProperty,null);Hide();Opacity=1;fading=false;}
}
