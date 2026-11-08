using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Ahenk;
public static class NativeAppearance
{
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 public static void Apply(Window window)
 {
  var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;
  int dark=Themes.Current=="light"?0:1;if(DwmSetWindowAttribute(handle,20,ref dark,4)!=0)DwmSetWindowAttribute(handle,19,ref dark,4);
  int NativeColor(string key){var c=((SolidColorBrush)Application.Current.FindResource(key)).Color;return c.R|(c.G<<8)|(c.B<<16);}
  int black=NativeColor("CaptionSurface"),text=NativeColor("Ink"),border=NativeColor("Line"),round=2;
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
 void InitializeWindowFrame()
 {
  WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=44,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(8),UseAeroCaptionButtons=false});
  SourceInitialized+=(_,_)=>{NativeAppearance.Apply(this);HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(FrameMessage);};
  StateChanged+=(_,_)=>{if(fullscreenPlayer){WindowFrame.BorderThickness=new Thickness(0);return;}bool maximized=WindowState==WindowState.Maximized;WindowFrame.BorderThickness=new Thickness(maximized?0:1);MaximizeGlyph.Data=Geometry.Parse(maximized?"M3,0 L11,0 11,8 M0,3 L8,3 8,11 0,11 Z":"M0,0 L10,0 10,10 0,10 Z");MaximizeButton.ToolTip=maximized?L.T("Geri yükle"):L.T("Büyüt");};
 }
 IntPtr FrameMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
 {
  if(message!=0x0024)return IntPtr.Zero; // WM_GETMINMAXINFO: keep maximized content inside the current work area.
  var monitor=MonitorFromWindow(hwnd,2);var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};if(!GetMonitorInfo(monitor,ref info))return IntPtr.Zero;
  var sizing=Marshal.PtrToStructure<MinMaxInfo>(lParam);sizing.MaxPosition.X=info.Work.Left-info.Monitor.Left;sizing.MaxPosition.Y=info.Work.Top-info.Monitor.Top;sizing.MaxSize.X=info.Work.Right-info.Work.Left;sizing.MaxSize.Y=info.Work.Bottom-info.Work.Top;
  var scale=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice??Matrix.Identity;
  sizing.MinTrackSize.X=Math.Max(sizing.MinTrackSize.X,(int)Math.Ceiling(MinWidth*scale.M11));sizing.MinTrackSize.Y=Math.Max(sizing.MinTrackSize.Y,(int)Math.Ceiling(MinHeight*scale.M22));Marshal.StructureToPtr(sizing,lParam,false);handled=true;return IntPtr.Zero;
 }
 void Minimize_Click(object sender,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
 void Maximize_Click(object sender,RoutedEventArgs e){if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);}
 void Close_Click(object sender,RoutedEventArgs e)=>SystemCommands.CloseWindow(this);
}
