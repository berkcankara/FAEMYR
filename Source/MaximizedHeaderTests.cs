using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Ahenk;
public static class MaximizedHeaderTests
{
 [StructLayout(LayoutKind.Sequential)] struct RectI{public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)] struct MonitorI{public int Size;public RectI Monitor,Work;public uint Flags;}
 [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorI info);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out RectI bounds);
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
 static object? Call(MainWindow window,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,args);
 static async Task Wait(Func<bool> ready){for(int i=0;i<300;i++){if(ready())return;await Task.Delay(15);}throw new Exception("Maximized header timeout");}
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Themes.Load();Environment.SetEnvironmentVariable("LYRA_ARTIST_PHOTOS_OFFLINE","1");Environment.SetEnvironmentVariable("LYRA_ARTIST_BIOGRAPHY_OFFLINE","1");
  var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  MainWindow? window=null;
  try
  {
   Check(MainWindow.VisibleFrameInsets(new Rect(-8,-8,1936,1096),new Rect(0,0,1920,1080),1,1)==new Thickness(8),"100-percent DPI converts invisible native borders to an eight-DIP safe inset");
   Check(MainWindow.VisibleFrameInsets(new Rect(-1932,-12,2584,1464),new Rect(-1920,0,2560,1440),2d/3,2d/3)==new Thickness(8),"150-percent DPI and negative monitor coordinates preserve the same logical inset");
   Check(MainWindow.VisibleFrameInsets(new Rect(0,0,1920,1080),new Rect(0,0,1920,1080),1,1)==new Thickness(),"An already fitted fullscreen window gets no extra inset");
   PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{AutoPlayOnStartup=false});
   window=new MainWindow{Opacity=0,ShowActivated=false,ShowInTaskbar=false};window.Show();await Wait(()=>Field<bool>(window,"sessionReady"));
   var handle=new WindowInteropHelper(window).Handle;var monitor=new MonitorI{Size=Marshal.SizeOf<MonitorI>()};GetMonitorInfo(MonitorFromWindow(handle,2),ref monitor);
   var title=Field<Grid>(window,"TitleBar");var frame=Field<Border>(window,"WindowFrame");
   bool HeaderFits()
   {
    window.UpdateLayout();var top=title.PointToScreen(new Point());var bottom=title.PointToScreen(new Point(title.ActualWidth,title.ActualHeight));
    return top.X>=monitor.Work.Left-.5&&top.Y>=monitor.Work.Top-.5&&bottom.X<=monitor.Work.Right+.5&&bottom.Y<=monitor.Work.Bottom+.5&&Math.Abs(title.ActualHeight-44)<.1;
   }
   Check(frame.Padding==new Thickness(),"Normal windows have no maximized-frame inset");
   SystemCommands.MaximizeWindow(window);await Wait(()=>window.WindowState==WindowState.Maximized);await Task.Delay(100);window.UpdateLayout();
   GetWindowRect(handle,out var native);File.WriteAllText(Path.Combine(directory,"maximized-geometry.json"),JsonSerializer.Serialize(new{native,work=monitor.Work,headerTop=title.PointToScreen(new Point()),headerHeight=title.ActualHeight,padding=frame.Padding},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
   Check(HeaderFits(),"The full 44-DIP header is inside the monitor work area after native maximize");
   foreach(string name in new[]{"BackButton","Search","TopWindowControls"}){var element=Field<FrameworkElement>(window,name);var point=element.PointToScreen(new Point());Check(point.Y>=monitor.Work.Top-.5,"Maximized "+name+" is fully below the top screen edge");}
   Call(window,"SetTopSearchExpanded",true,false);window.UpdateLayout();Check(HeaderFits(),"Expanded search does not change the visible header height");
   Call(window,"EnterFullscreenPlayer");await Task.Delay(100);window.UpdateLayout();var full=Field<Grid>(window,"FullscreenRoot");var fullTop=full.PointToScreen(new Point());var fullBottom=full.PointToScreen(new Point(full.ActualWidth,full.ActualHeight));
   Check(Math.Abs(fullTop.X-monitor.Monitor.Left)<1&&Math.Abs(fullTop.Y-monitor.Monitor.Top)<1&&Math.Abs(fullBottom.X-monitor.Monitor.Right)<1&&Math.Abs(fullBottom.Y-monitor.Monitor.Bottom)<1,"Fullscreen content fills the visible monitor without overscan or an added border");
   Call(window,"ExitFullscreenPlayer");await Task.Delay(100);Check(window.WindowState==WindowState.Maximized&&HeaderFits(),"Exiting fullscreen restores the entire maximized header");
   SystemCommands.MinimizeWindow(window);await Wait(()=>window.WindowState==WindowState.Minimized);SystemCommands.RestoreWindow(window);await Wait(()=>window.WindowState==WindowState.Maximized);await Task.Delay(100);Check(HeaderFits(),"Taskbar minimize and restore retain the safe maximized header placement");
   SystemCommands.RestoreWindow(window);await Wait(()=>window.WindowState==WindowState.Normal);await Task.Delay(80);window.UpdateLayout();Check(frame.Padding==new Thickness()&&Math.Abs(title.ActualHeight-44)<.1,"Restoring normal size clears the maximized inset and keeps the header height");
   Check(!Field<AudioEngine>(window,"audio").DeviceStarted,"Frame verification never opens an audio endpoint");
  }
  finally{window?.Close();File.WriteAllText(Path.Combine(directory,"maximized-header-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
