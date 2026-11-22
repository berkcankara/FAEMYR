using System.Windows;
using System.Windows.Media;

namespace Faemyr;
// Visual analysis only: neither the output samples nor the playback volume change.
public sealed class FullscreenAmbience
{
 public double Opacity{get;private set;}
 public static double Energy(ReadOnlySpan<float> left,ReadOnlySpan<float> right,int channels)
 {
  int count=channels>1?Math.Min(left.Length,right.Length):left.Length;if(count==0)return 0;
  double power=0;double Safe(float sample)=>float.IsFinite(sample)?Math.Clamp((double)sample,-1,1):0;
  for(int i=0;i<count;i++){double l=Safe(left[i]);power+=l*l;if(channels>1){double r=Safe(right[i]);power+=r*r;}}
  power/=count*(channels>1?2d:1);if(power<=0)return 0;
  return Math.Pow(Math.Clamp((10*Math.Log10(power)+50)/42,0,1),1.2);
 }
 public double Step(double energy,bool playing,bool light,double seconds)
 {
  energy=double.IsFinite(energy)?Math.Clamp(energy,0,1):0;
  // The original album palette is the brightest point, preserving text contrast.
  double target=playing?(light?.07:.22)*(1-energy):0;
  double dt=double.IsFinite(seconds)?Math.Clamp(seconds,0,.1):0;
  Opacity+=(target-Opacity)*(1-Math.Exp(-dt/(target<Opacity?.16:.50)));
  if(Math.Abs(Opacity-target)<.0002)Opacity=target;return Opacity;
 }
 public void Reset()=>Opacity=0;
}

public partial class MainWindow
{
 readonly FullscreenAmbience fullscreenAmbience=new();
 readonly VisualizerFramePacer fullscreenAmbiencePacer=new(){TargetFps=30};
 bool fullscreenAmbienceSubscribed;
 TimeSpan fullscreenAmbienceFrame=TimeSpan.MinValue;
 ScopeCapture? fullscreenAmbienceCapture;
 float[] fullscreenAmbienceLeft=[],fullscreenAmbienceRight=[];
 bool CanAnimateFullscreen=>fullscreenPlayer&&IsLoaded&&IsVisible&&WindowState!=WindowState.Minimized&&SystemParameters.ClientAreaAnimation;
 void UpdateFullscreenAmbience()
 {
  if(!CanAnimateFullscreen){StopFullscreenAmbience();return;}
  bool playing=audio.IsPlaying&&audio.Scope!=null;
  if(!playing&&fullscreenAmbience.Opacity==0){StopFullscreenAmbience();return;}
  if(fullscreenAmbienceSubscribed)return;
  fullscreenAmbienceSubscribed=true;fullscreenAmbienceFrame=TimeSpan.MinValue;fullscreenAmbiencePacer.Reset();CompositionTarget.Rendering+=FullscreenAmbienceFrame;
 }
 void FullscreenAmbienceFrame(object? sender,EventArgs args)
 {
  if(!CanAnimateFullscreen){StopFullscreenAmbience();return;}
  if(args is not RenderingEventArgs frame||!fullscreenAmbiencePacer.Accept(frame.RenderingTime))return;
  double seconds=fullscreenAmbienceFrame==TimeSpan.MinValue?1d/30:(frame.RenderingTime-fullscreenAmbienceFrame).TotalSeconds;fullscreenAmbienceFrame=frame.RenderingTime;
  var capture=audio.Scope;bool playing=audio.IsPlaying&&capture!=null;double energy=0;
  if(playing&&!audio.Buffering)
  {
   if(!ReferenceEquals(capture,fullscreenAmbienceCapture))
   {
    fullscreenAmbienceCapture=capture;int length=Math.Clamp((int)Math.Ceiling(capture!.SampleRate*.08),128,16384);
    if(fullscreenAmbienceLeft.Length!=length){fullscreenAmbienceLeft=new float[length];fullscreenAmbienceRight=new float[length];}
   }
   int count=audio.CopyScope(capture!,fullscreenAmbienceLeft,fullscreenAmbienceRight,fullscreenAmbienceLeft.Length);
   energy=FullscreenAmbience.Energy(fullscreenAmbienceLeft.AsSpan(0,count),fullscreenAmbienceRight.AsSpan(0,count),capture!.Channels);
  }
  double opacity=fullscreenAmbience.Step(energy,playing,Themes.Current=="light",seconds);
  if(Math.Abs(FullscreenAmbientShade.Opacity-opacity)>.0001)FullscreenAmbientShade.Opacity=opacity;
  if(!playing&&opacity==0)StopFullscreenAmbience();
 }
 void StopFullscreenAmbience()
 {
  if(fullscreenAmbienceSubscribed)CompositionTarget.Rendering-=FullscreenAmbienceFrame;
  fullscreenAmbienceSubscribed=false;fullscreenAmbience.Reset();FullscreenAmbientShade.Opacity=0;fullscreenAmbienceCapture=null;
 }
}
