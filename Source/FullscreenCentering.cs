using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Ahenk;
public partial class MainWindow
{
 bool fullscreenArtworkMoving;
 void ResetFullscreenArtworkMotion()
 {
  fullscreenArtworkMoving=false;
  FullscreenArtworkMotion.BeginAnimation(TranslateTransform.XProperty,null);FullscreenArtworkMotion.BeginAnimation(TranslateTransform.YProperty,null);
  FullscreenArtworkScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);FullscreenArtworkScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
  FullscreenArtworkMotion.X=FullscreenArtworkMotion.Y=0;FullscreenArtworkScale.ScaleX=FullscreenArtworkScale.ScaleY=1;
 }
 void AnimateFullscreenArtworkLayout(Rect previous)
 {
  if(!fullscreenPlayer||!SystemParameters.ClientAreaAnimation||previous.Width<1||FullscreenCover.ActualWidth<1)return;
  var destination=FullscreenCover.TransformToAncestor(FullscreenRoot).TransformBounds(new Rect(FullscreenCover.RenderSize));
  var origin=FullscreenCover.TranslatePoint(new Point(FullscreenCover.ActualWidth/2,FullscreenCover.ActualHeight/2),FullscreenArtwork);
  FullscreenArtworkScale.CenterX=origin.X;FullscreenArtworkScale.CenterY=origin.Y;
  double x=previous.Left+previous.Width/2-destination.Left-destination.Width/2,y=previous.Top+previous.Height/2-destination.Top-destination.Height/2,scale=previous.Width/destination.Width;
  fullscreenArtworkMoving=true;
  // Establish the first frame synchronously; animation clocks do not tick
  // until the dispatcher renders, which would otherwise expose the new layout.
  FullscreenArtworkMotion.X=x;FullscreenArtworkMotion.Y=y;FullscreenArtworkScale.ScaleX=FullscreenArtworkScale.ScaleY=scale;
  DoubleAnimation Motion(double from,double to)=>new(from,to,TimeSpan.FromMilliseconds(360)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut},FillBehavior=FillBehavior.HoldEnd};
  var horizontal=Motion(x,0);
  horizontal.Completed+=(_,_)=>{ResetFullscreenArtworkMotion();CenterFullscreenContent(false);};
  FullscreenArtworkMotion.BeginAnimation(TranslateTransform.XProperty,horizontal,HandoffBehavior.SnapshotAndReplace);
  FullscreenArtworkMotion.BeginAnimation(TranslateTransform.YProperty,Motion(y,0),HandoffBehavior.SnapshotAndReplace);
  FullscreenArtworkScale.BeginAnimation(ScaleTransform.ScaleXProperty,Motion(scale,1),HandoffBehavior.SnapshotAndReplace);
  FullscreenArtworkScale.BeginAnimation(ScaleTransform.ScaleYProperty,Motion(scale,1),HandoffBehavior.SnapshotAndReplace);
 }
 void InitializeFullscreenCentering()
 {
  foreach(var element in new FrameworkElement[]{FullscreenBody,FullscreenArtwork,FullscreenCover,LyricsCard,LyricsScroll,LyricsPlain})element.SizeChanged+=(_,_)=>CenterFullscreenContent(false);
 }
 void CenterFullscreenContent(bool animate)
 {
  if(fullscreenArtworkMoving)return;
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
