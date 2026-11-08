using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Ahenk;
public static class LyricEmphasis
{
 public const double Enlargement=1.12;
 const double RestingScale=1/Enlargement;
 public static void Configure(TextBlock line,bool fullscreen,bool active)
 {
  if(line.RenderTransform is ScaleTransform previous){previous.BeginAnimation(ScaleTransform.ScaleXProperty,null);previous.BeginAnimation(ScaleTransform.ScaleYProperty,null);}
  // Reserve the largest size once. Animation only affects rendering, keeping
  // wrapping, neighboring rows and the smooth-scroll target stable throughout.
  line.LayoutTransform=fullscreen?new ScaleTransform(Enlargement,Enlargement):Transform.Identity;
  double scale=active?1:RestingScale;
  line.RenderTransform=fullscreen?new ScaleTransform(scale,scale):Transform.Identity;
  line.RenderTransformOrigin=fullscreen?new Point(0,.5):new Point();
 }
 public static void SetActive(TextBlock line,bool active)
 {
  if(line.RenderTransform is not ScaleTransform scale)return;
  double target=active?1:RestingScale;
  Animate(scale,ScaleTransform.ScaleXProperty,target);Animate(scale,ScaleTransform.ScaleYProperty,target);
 }
 static void Animate(ScaleTransform scale,DependencyProperty property,double target)
 {
  double current=(double)scale.GetValue(property);scale.BeginAnimation(property,null);scale.SetValue(property,target);
  if(!SystemParameters.ClientAreaAnimation||Math.Abs(current-target)<.0001)return;
  scale.BeginAnimation(property,new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(280)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut},FillBehavior=FillBehavior.Stop},HandoffBehavior.SnapshotAndReplace);
 }
}
