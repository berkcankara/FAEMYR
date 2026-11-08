using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Ahenk;
public partial class MainWindow
{
 void InitializeFullscreenCentering()
 {
  foreach(var element in new FrameworkElement[]{FullscreenBody,FullscreenArtwork,FullscreenCover,LyricsCard,LyricsScroll,LyricsPlain})element.SizeChanged+=(_,_)=>CenterFullscreenContent(false);
 }
 void CenterFullscreenContent(bool animate)
 {
  bool centered=fullscreenPlayer&&fullscreenControlsIdle.Hidden;
  double ArtworkOffset()=>FullscreenCenterOffset(FullscreenArtwork,FullscreenCover,FullscreenArtworkShift);
  var lyricViewport=LyricsScroll.Visibility==Visibility.Visible?(FrameworkElement)LyricsScroll:LyricsPlain;
  double LyricsOffset()=>FullscreenCenterOffset(FullscreenLyricsHost,lyricViewport.ActualHeight>0?lyricViewport:FullscreenLyricsHost,FullscreenLyricsShift);
  AnimateFullscreenOffset(FullscreenArtworkShift,centered?ArtworkOffset():0,animate);
  AnimateFullscreenOffset(FullscreenLyricsShift,centered?LyricsOffset():0,animate);
 }
 double FullscreenCenterOffset(FrameworkElement content,FrameworkElement focus,TranslateTransform shift)
 {
  if(FullscreenRoot.ActualHeight<1||content.ActualHeight<1)return 0;
  // Subtract the current rendering offset, including a partially completed
  // animation, to keep the destination stable when playback or layout changes.
  double center=focus.TranslatePoint(new Point(0,focus.ActualHeight/2),FullscreenRoot).Y-shift.Y;
  double top=content.TranslatePoint(new Point(),FullscreenRoot).Y-shift.Y;
  double target=FullscreenRoot.ActualHeight/2-center;
  double minimum=24-top,maximum=FullscreenRoot.ActualHeight-24-top-content.ActualHeight;
  return minimum<=maximum?Math.Clamp(target,minimum,maximum):(minimum+maximum)/2;
 }
 static void AnimateFullscreenOffset(TranslateTransform shift,double target,bool animate)
 {
  double current=shift.Y;
  shift.BeginAnimation(TranslateTransform.YProperty,null);shift.Y=target;
  if(!animate||!SystemParameters.ClientAreaAnimation||Math.Abs(current-target)<.1)return;
  shift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(320)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut},FillBehavior=FillBehavior.Stop},HandoffBehavior.SnapshotAndReplace);
 }
}
