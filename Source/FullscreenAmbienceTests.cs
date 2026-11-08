namespace Ahenk;
public static class FullscreenAmbienceTests
{
 public static void Run(Action<bool,string> check)
 {
  float[] quiet=Enumerable.Repeat(.01f,256).ToArray(),loud=Enumerable.Repeat(.3f,256).ToArray(),inverted=loud.Select(sample=>-sample).ToArray(),silence=new float[256];
  double low=FullscreenAmbience.Energy(quiet,quiet,2),high=FullscreenAmbience.Energy(loud,loud,2);
  check(low>0&&low<high&&high<1,"Ambient brightness measures actual signal energy rather than a timed pulse");
  check(FullscreenAmbience.Energy(silence,silence,2)==0&&FullscreenAmbience.Energy([],[],1)==0,"Ambient silence and empty captures produce zero energy");
  check(Math.Abs(high-FullscreenAmbience.Energy(loud,inverted,2))<.000001,"Opposite-phase stereo keeps its full ambient energy");
  check(Math.Abs(high-FullscreenAmbience.Energy(loud,[],1))<.000001,"Mono captures use the same loudness scale as stereo");
  check(double.IsFinite(FullscreenAmbience.Energy([float.NaN,float.PositiveInfinity,1],[-1,0,float.NegativeInfinity],2)),"Nonfinite visual samples cannot corrupt ambient opacity");
  var effect=new FullscreenAmbience();for(int i=0;i<120;i++)effect.Step(0,true,false,1d/30);check(effect.Opacity>.21&&effect.Opacity<=.22,"Dark-theme ambient dimming is bounded and never exceeds twenty-two percent");
  double dark=effect.Opacity;effect.Step(1,true,false,1d/30);check(effect.Opacity>0&&effect.Opacity<dark,"Louder music brightens the background gradually without an immediate jump");
  for(int i=0;i<120;i++)effect.Step(0,false,false,1d/30);check(effect.Opacity==0,"Paused and stopped playback return to the unmodified album palette");
  for(int i=0;i<120;i++)effect.Step(0,true,true,1d/30);check(effect.Opacity<=.07,"Light-theme brightness variation remains subtle");
  var slow=new FullscreenAmbience();var fast=new FullscreenAmbience();for(int i=0;i<30;i++)slow.Step(low,true,false,1d/30);for(int i=0;i<60;i++)fast.Step(low,true,false,1d/60);check(Math.Abs(slow.Opacity-fast.Opacity)<.000001,"Ambient smoothing is independent of display frame rate");
  effect.Step(double.NaN,true,false,double.PositiveInfinity);check(double.IsFinite(effect.Opacity)&&effect.Opacity>=0&&effect.Opacity<=.22,"Invalid visual timing and energy remain bounded");
 }
}
