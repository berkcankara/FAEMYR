using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Ahenk;
public partial class MainWindow
{
 WindowsMediaSession? windowsMedia;
 bool InitializeWindowsMedia()
 {
  try{windowsMedia=new(new WindowInteropHelper(this).Handle,QueueDesktopAction,SystemMediaSeek);return true;}
  catch(Exception){windowsMedia=null;return false;} // Legacy keyboard support remains available if Windows cannot create a session.
 }
 void SystemMediaSeek(TimeSpan position)
 {
  if(closed||Dispatcher.HasShutdownStarted)return;
  Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>
  {
   if(closed||now==null||!double.IsFinite(position.TotalSeconds))return;
   Seek.Value=Math.Clamp(position.TotalSeconds,0,PlayerDuration.TotalSeconds);CommitSeek();UpdateLyricsPlayback();UpdateDesktopPlayback();
  });
 }
}
