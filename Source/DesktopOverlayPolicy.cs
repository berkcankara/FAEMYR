using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
namespace Faemyr;
public static class DesktopOverlayPolicy
{
 [StructLayout(LayoutKind.Sequential)] public struct Bounds{public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)] struct MonitorInfo{public int Size;public Bounds Monitor,Work;public uint Flags;}
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
 [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out Bounds rect);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Bounds rect);
 [StructLayout(LayoutKind.Sequential)] struct Point{public int X,Y;}
 [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref Point point);
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr window);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder name,int max);
 [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
 [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int w,int h,uint flags);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
 [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
 public static bool CoversMonitor(Bounds client,Bounds monitor)=>client.Left<=monitor.Left+2&&client.Top<=monitor.Top+2&&client.Right>=monitor.Right-2&&client.Bottom>=monitor.Bottom-2;
 public static bool ShellBlocks(int state)=>state is 1 or 4 or 6;
 public static bool ShouldSuppress(int state,bool ownWindow,bool geometryAvailable,bool coversMonitor,bool maximized,bool caption)
 {
  if(ShellBlocks(state))return true;
  // Our fullscreen player is checked by its caller. Shell BUSY can also be
  // caused by a maximized borderless desktop window or a background game.
  if(ownWindow)return false;
  if(geometryAvailable)return coversMonitor&&!(maximized&&caption);
  return state is 2 or 3;
 }
 public static bool Suppressed()
 {
  int state=5;if(SHQueryUserNotificationState(out int shell)==0)state=shell;
  if(ShellBlocks(state))return true;
  var foreground=GetForegroundWindow();if(foreground==IntPtr.Zero||IsIconic(foreground))return ShouldSuppress(state,false,false,false,false,false);
  GetWindowThreadProcessId(foreground,out uint process);if(process==Environment.ProcessId)return ShouldSuppress(state,true,false,false,false,false);
  var name=new StringBuilder(256);GetClassName(foreground,name,name.Capacity);if(name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")return false;
  var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};if(!GetMonitorInfo(MonitorFromWindow(foreground,2),ref info)||!GetClientRect(foreground,out var client))return ShouldSuppress(state,false,false,false,false,false);
  var origin=new Point();if(!ClientToScreen(foreground,ref origin))return ShouldSuppress(state,false,false,false,false,false);
  bool full=CoversMonitor(new Bounds{Left=origin.X,Top=origin.Y,Right=origin.X+client.Right,Bottom=origin.Y+client.Bottom},info.Monitor);
  return ShouldSuppress(state,false,true,full,IsZoomed(foreground),(GetWindowLongPtr(foreground,-16).ToInt64()&0x00c00000)!=0);
 }
 public static void NonActivating(Window window)
 {
  var handle=new WindowInteropHelper(window).Handle;long style=GetWindowLongPtr(handle,-20).ToInt64();SetWindowLongPtr(handle,-20,new IntPtr(style|0x08000000|0x80|0x20));
 }
 public static void TopRight(Window window,bool onlyIfOffscreen=false)
 {
  var handle=new WindowInteropHelper(window).Handle;if(!GetWindowRect(handle,out var rect))return;
  var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};var reference=onlyIfOffscreen?handle:GetForegroundWindow();if(!GetMonitorInfo(MonitorFromWindow(reference,2),ref info))return;
  if(onlyIfOffscreen&&Math.Min(rect.Right,info.Work.Right)-Math.Max(rect.Left,info.Work.Left)>40&&Math.Min(rect.Bottom,info.Work.Bottom)-Math.Max(rect.Top,info.Work.Top)>40)return;
  int margin=(int)Math.Round(16*Math.Max(1,GetDpiForWindow(handle)/96d));SetWindowPos(handle,IntPtr.Zero,Math.Max(info.Work.Left,info.Work.Right-(rect.Right-rect.Left)-margin),info.Work.Top+margin,0,0,0x1|0x4|0x10);
 }
 public static void RaiseWithoutActivation(Window window)=>SetWindowPos(new WindowInteropHelper(window).Handle,new IntPtr(-1),0,0,0,0,0x1|0x2|0x10);
}
