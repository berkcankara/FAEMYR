using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Faemyr;
public sealed class FullscreenControlsIdleState
{
 public const double DelaySeconds=5;
 public bool Hidden{get;private set;}
 public bool CursorHidden{get;private set;}
 double lastMotion;Point pointer;
 public void Reset(double seconds,Point position){Hidden=CursorHidden=false;lastMotion=seconds;pointer=position;}
 public bool Move(Point position,Rect hotzone,double seconds)
 {
  // WPF can synthesize mouse events during animation without physical motion.
  if((position-pointer).LengthSquared<.25)return false;
  pointer=position;lastMotion=seconds;CursorHidden=false;
  if(!Hidden||!hotzone.Contains(position))return false;
  Hidden=false;return true;
 }
 public bool Tick(double seconds,bool interacting)
 {
  if(Hidden&&CursorHidden)return false;
  if(interacting){lastMotion=seconds;return false;}
  if(seconds-lastMotion<DelaySeconds)return false;
  Hidden=CursorHidden=true;return true;
 }
}

public partial class MainWindow
{
 readonly FullscreenControlsIdleState fullscreenControlsIdle=new();
 readonly DispatcherTimer fullscreenControlsIdleTimer=new(){Interval=TimeSpan.FromMilliseconds(150)};
 object fullscreenCursorRestore=DependencyProperty.UnsetValue,fullscreenForceCursorRestore=DependencyProperty.UnsetValue;
 bool fullscreenCursorCaptured;
 HwndSource? fullscreenControlsInputSource;
 static double FullscreenControlsClock=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
 void InitializeFullscreenControlsAutoHide()
 {
  SourceInitialized+=(_,_)=>{fullscreenControlsInputSource=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);fullscreenControlsInputSource?.AddHook(FullscreenControlsNativeMouseMove);};
  Closed+=(_,_)=>{try{fullscreenControlsInputSource?.RemoveHook(FullscreenControlsNativeMouseMove);}catch(ObjectDisposedException){}fullscreenControlsInputSource=null;};
  fullscreenControlsIdleTimer.Tick+=(_,_)=>FullscreenControlsIdleTick();
  FullscreenControlsHotzone.SizeChanged+=(_,_)=>{if(fullscreenPlayer&&fullscreenControlsIdle.Hidden)SlideFullscreenControls(false);};
 }
 void StartFullscreenControlsAutoHide()
 {
  if(!fullscreenCursorCaptured){fullscreenCursorRestore=FullscreenRoot.ReadLocalValue(CursorProperty);fullscreenForceCursorRestore=FullscreenRoot.ReadLocalValue(ForceCursorProperty);fullscreenCursorCaptured=true;}
  fullscreenControlsIdle.Reset(FullscreenControlsClock,Mouse.GetPosition(FullscreenRoot));ApplyFullscreenCursor();SlideFullscreenControls(false);fullscreenControlsIdleTimer.Start();
 }
 void StopFullscreenControlsAutoHide()
 {
  fullscreenControlsIdleTimer.Stop();fullscreenControlsIdle.Reset(FullscreenControlsClock,new Point());ApplyFullscreenCursor();fullscreenCursorCaptured=false;SlideFullscreenControls(false);
 }
 Rect FullscreenControlsHotspot()=>new(FullscreenControlsHotzone.TranslatePoint(new Point(),FullscreenRoot),FullscreenControlsHotzone.RenderSize);
 IntPtr FullscreenControlsNativeMouseMove(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
 {
  // Rendering transforms can synthesize WPF MouseMove events. Only this
  // window's native client input resets inactivity; other windows are untouched.
  if(message==0x0200&&fullscreenPlayer&&FullscreenRoot.IsLoaded)
  {
   long packed=lParam.ToInt64();var point=new Point(unchecked((short)(packed&0xffff)),unchecked((short)((packed>>16)&0xffff)));
   point=(fullscreenControlsInputSource?.CompositionTarget?.TransformFromDevice??Matrix.Identity).Transform(point);
   FullscreenControlsPointerMoved(TranslatePoint(point,FullscreenRoot));
  }
  return IntPtr.Zero;
 }
 void FullscreenControlsPointerMoved(Point position)
 {
  if(!fullscreenPlayer)return;
  bool cursorWasHidden=fullscreenControlsIdle.CursorHidden;
  if(fullscreenControlsIdle.Move(position,FullscreenControlsHotspot(),FullscreenControlsClock)){SlideFullscreenControls(true);fullscreenControlsIdleTimer.Start();}
  if(cursorWasHidden&&!fullscreenControlsIdle.CursorHidden){ApplyFullscreenCursor();fullscreenControlsIdleTimer.Start();}
 }
 void FullscreenControlsIdleTick()
 {
  if(!fullscreenPlayer){StopFullscreenControlsAutoHide();return;}
  bool interacting=dragging||FullscreenRoot.IsMouseCaptureWithin||Scope.ContextMenu?.IsOpen==true||SignalBox.ContextMenu?.IsOpen==true||FullscreenRoot.IsMouseOver&&Mouse.LeftButton==MouseButtonState.Pressed;
  if(!fullscreenControlsIdle.Tick(FullscreenControlsClock,interacting))return;
  fullscreenControlsIdleTimer.Stop();ApplyFullscreenCursor();SlideFullscreenControls(true);
 }
 void ApplyFullscreenCursor()
 {
  if(fullscreenPlayer&&fullscreenControlsIdle.CursorHidden){FullscreenRoot.Cursor=Cursors.None;FullscreenRoot.ForceCursor=true;return;}
  if(!fullscreenCursorCaptured)return;
  // Scope the invisible cursor to this view. Other windows and the desktop
  // retain their normal cursor, and children recover their own hand/I-beam.
  if(fullscreenCursorRestore==DependencyProperty.UnsetValue)FullscreenRoot.ClearValue(CursorProperty);else FullscreenRoot.SetValue(CursorProperty,fullscreenCursorRestore);
  if(fullscreenForceCursorRestore==DependencyProperty.UnsetValue)FullscreenRoot.ClearValue(ForceCursorProperty);else FullscreenRoot.SetValue(ForceCursorProperty,fullscreenForceCursorRestore);
 }
 void SlideFullscreenControls(bool animate)
 {
  CenterFullscreenContent(animate);
  bool hidden=fullscreenControlsIdle.Hidden;
  if(hidden&&FullscreenControls.IsKeyboardFocusWithin)FullscreenRoot.Focus();
  FullscreenControls.IsEnabled=FullscreenControls.IsHitTestVisible=!hidden;
  ToolTipService.SetIsEnabled(Scope,!hidden);
  double current=FullscreenControlsSlide.Y,target=hidden?Math.Max(FullscreenControls.MinHeight,FullscreenControls.ActualHeight):0;
  FullscreenControlsSlide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,null);FullscreenControlsSlide.Y=target;
  if(!animate||!SystemParameters.ClientAreaAnimation||Math.Abs(current-target)<.1)return;
  FullscreenControlsSlide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(320)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut},FillBehavior=FillBehavior.Stop},HandoffBehavior.SnapshotAndReplace);
 }
}
