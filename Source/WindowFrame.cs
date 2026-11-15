using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Ahenk;
public static class NativeAppearance
{
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 public static void Apply(Window window)
 {
  var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;
  int dark=Themes.Current=="light"?0:1;if(DwmSetWindowAttribute(handle,20,ref dark,4)!=0)DwmSetWindowAttribute(handle,19,ref dark,4);
  int NativeColor(string key){var c=((SolidColorBrush)Application.Current.FindResource(key)).Color;return c.R|(c.G<<8)|(c.B<<16);}
  int black=NativeColor(window is MainWindow?"WorkspaceSurface":"CaptionSurface"),text=NativeColor("Ink"),border=NativeColor("Line"),round=2;
  DwmSetWindowAttribute(handle,35,ref black,4);DwmSetWindowAttribute(handle,36,ref text,4);DwmSetWindowAttribute(handle,34,ref border,4);DwmSetWindowAttribute(handle,33,ref round,4);
 }
}
public partial class MainWindow
{
 [StructLayout(LayoutKind.Sequential)] struct NativePoint {public int X,Y;}
 [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)] struct MinMaxInfo {public NativePoint Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
 [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {public int Size;public NativeRect Monitor,Work;public uint Flags;}
 [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out NativeRect bounds);
 [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref NativePoint point);
 [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr window);
 bool frameInsetsQueued;
 internal static Thickness VisibleFrameInsets(Rect client,Rect viewport,double scaleX,double scaleY)=>new(
  Math.Max(0,viewport.Left-client.Left)*scaleX,Math.Max(0,viewport.Top-client.Top)*scaleY,
  Math.Max(0,client.Right-viewport.Right)*scaleX,Math.Max(0,client.Bottom-viewport.Bottom)*scaleY);
 void UpdateWindowFrameInsets()
 {
  if(WindowState==WindowState.Minimized)return;
  var handle=new WindowInteropHelper(this).Handle;
  if(handle==IntPtr.Zero||WindowState!=WindowState.Maximized&&!IsZoomed(handle)){WindowFrame.Padding=new Thickness();return;}
  var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};var origin=new NativePoint();
  if(!GetMonitorInfo(MonitorFromWindow(handle,2),ref info)||!GetClientRect(handle,out var client)||!ClientToScreen(handle,ref origin))return;
  var viewport=fullscreenPlayer?info.Monitor:info.Work;
  var scale=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice??Matrix.Identity;
  // A captioned maximized HWND keeps its invisible resize borders outside the
  // monitor. Custom chrome draws into that area, so inset the content by the
  // actual clipped pixels, converted using this window's current monitor DPI.
  WindowFrame.Padding=VisibleFrameInsets(new Rect(origin.X,origin.Y,client.Right-client.Left,client.Bottom-client.Top),new Rect(viewport.Left,viewport.Top,viewport.Right-viewport.Left,viewport.Bottom-viewport.Top),scale.M11,scale.M22);
 }
 void QueueWindowFrameInsets()
 {
  if(frameInsetsQueued||closed||Dispatcher.HasShutdownStarted)return;frameInsetsQueued=true;
  Dispatcher.BeginInvoke(DispatcherPriority.Loaded,()=>{frameInsetsQueued=false;if(!closed)UpdateWindowFrameInsets();});
 }
 void RefreshMaximizedFrame()
 {
  var handle=new WindowInteropHelper(this).Handle;
  var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
  if(handle==IntPtr.Zero||!GetMonitorInfo(MonitorFromWindow(handle,2),ref info))return;
  var bounds=fullscreenPlayer?info.Monitor:info.Work;
  SetWindowPos(handle,IntPtr.Zero,bounds.Left,bounds.Top,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top,0x0020|0x0004|0x0010);
  UpdateWindowFrameInsets();
 }
 void InitializeWindowFrame()
 {
  WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=44,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(8),UseAeroCaptionButtons=false});
  SourceInitialized+=(_,_)=>{NativeAppearance.Apply(this);HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(FrameMessage);};
  StateChanged+=(_,_)=>{if(fullscreenPlayer){WindowFrame.BorderThickness=new Thickness(0);UpdateWindowFrameInsets();return;}bool maximized=WindowState==WindowState.Maximized;WindowFrame.BorderThickness=new Thickness(maximized?0:1);MaximizeGlyph.Data=Geometry.Parse(maximized?"M3,0 L11,0 11,8 M0,3 L8,3 8,11 0,11 Z":"M0,0 L10,0 10,10 0,10 Z");MaximizeButton.ToolTip=maximized?L.T("Geri yükle"):L.T("Büyüt");UpdateWindowFrameInsets();};
  Loaded+=(_,_)=>UpdateWindowFrameInsets();SizeChanged+=(_,_)=>QueueWindowFrameInsets();LocationChanged+=(_,_)=>QueueWindowFrameInsets();
 }
 IntPtr FrameMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
 {
  if(message is 0x0047 or 0x02e0){QueueWindowFrameInsets();return IntPtr.Zero;} // Window placement or monitor DPI changed.
  if(message!=0x0024)return IntPtr.Zero; // WM_GETMINMAXINFO: keep maximized content inside the current work area.
  var monitor=MonitorFromWindow(hwnd,2);var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};if(!GetMonitorInfo(monitor,ref info))return IntPtr.Zero;
  var bounds=fullscreenPlayer?info.Monitor:info.Work;
  var sizing=Marshal.PtrToStructure<MinMaxInfo>(lParam);sizing.MaxPosition.X=bounds.Left-info.Monitor.Left;sizing.MaxPosition.Y=bounds.Top-info.Monitor.Top;sizing.MaxSize.X=bounds.Right-bounds.Left;sizing.MaxSize.Y=bounds.Bottom-bounds.Top;
  if(fullscreenPlayer){sizing.MaxTrackSize.X=Math.Max(sizing.MaxTrackSize.X,sizing.MaxSize.X);sizing.MaxTrackSize.Y=Math.Max(sizing.MaxTrackSize.Y,sizing.MaxSize.Y);}
  var scale=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice??Matrix.Identity;
  sizing.MinTrackSize.X=Math.Max(sizing.MinTrackSize.X,(int)Math.Ceiling(MinWidth*scale.M11));sizing.MinTrackSize.Y=Math.Max(sizing.MinTrackSize.Y,(int)Math.Ceiling(MinHeight*scale.M22));Marshal.StructureToPtr(sizing,lParam,false);handled=true;return IntPtr.Zero;
 }
 void Minimize_Click(object sender,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
 void Maximize_Click(object sender,RoutedEventArgs e){if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);}
 void Close_Click(object sender,RoutedEventArgs e)=>SystemCommands.CloseWindow(this);
}
