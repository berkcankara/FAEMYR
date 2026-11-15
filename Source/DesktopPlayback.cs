using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Ahenk;
public enum DesktopMediaAction{None,Toggle,Play,Pause,Previous,Next,Stop,Shuffle,Repeat}
public static class DesktopMediaKeys
{
 public const int BaseId=0x4c10;
 public static readonly (int Key,DesktopMediaAction Action)[] Bindings=[(0xb3,DesktopMediaAction.Toggle),(0xb1,DesktopMediaAction.Previous),(0xb0,DesktopMediaAction.Next),(0xb2,DesktopMediaAction.Stop)];
 public static DesktopMediaAction Hotkey(int id)=>id>=BaseId&&id<BaseId+Bindings.Length?Bindings[id-BaseId].Action:DesktopMediaAction.None;
 public static DesktopMediaAction AppCommand(long value)=>((value>>16)&0x7ff) switch{14=>DesktopMediaAction.Toggle,46=>DesktopMediaAction.Play,47=>DesktopMediaAction.Pause,12=>DesktopMediaAction.Previous,11=>DesktopMediaAction.Next,13=>DesktopMediaAction.Stop,_=>DesktopMediaAction.None};
}
public partial class MainWindow
{
 [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
 [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
 delegate IntPtr MediaKeyboardCallback(int code,IntPtr message,IntPtr data);
 [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int hook,MediaKeyboardCallback callback,IntPtr module,uint thread);
 [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
 [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? module);
 MediaKeyboardCallback? mediaKeyboardCallback;IntPtr mediaKeyboardHook;readonly HashSet<int> fallbackMediaKeys=[],pressedMediaKeys=[];
 HwndSource? mediaSource;readonly List<int> mediaHotkeys=[];ThumbButtonInfo? thumbnailPrevious,thumbnailPlay,thumbnailNext;bool mediaActionBusy;string desktopState="";
 DesktopMediaAction pendingMediaAction;
 void InitializeDesktopPlayback()
 {
  if(StartupMetrics.Enabled)return;
  InitializeTaskbarDiagnostics();
  thumbnailPrevious=new(){Description=L.T("Önceki parça"),ImageSource=SvgIcon.ShellImage("previous"),DismissWhenClicked=false};
  thumbnailPlay=new(){Description=L.T("Oynat"),ImageSource=SvgIcon.ShellImage("play"),DismissWhenClicked=false};
  thumbnailNext=new(){Description=L.T("Sonraki parça"),ImageSource=SvgIcon.ShellImage("next"),DismissWhenClicked=false};
  TaskbarItemInfo=new(){Description="Lyra",ThumbButtonInfos=new(){thumbnailPrevious,thumbnailPlay,thumbnailNext}};
  thumbnailPrevious.Click+=(_,_)=>QueueDesktopAction(DesktopMediaAction.Previous);thumbnailPlay.Click+=(_,_)=>QueueDesktopAction(DesktopMediaAction.Toggle);thumbnailNext.Click+=(_,_)=>QueueDesktopAction(DesktopMediaAction.Next);
  SourceInitialized+=(_,_)=>
  {
   mediaSource=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);if(mediaSource==null)return;mediaSource.AddHook(DesktopMediaMessage);
   if(InitializeWindowsMedia())return;
   for(int i=0;i<DesktopMediaKeys.Bindings.Length;i++)if(RegisterHotKey(mediaSource.Handle,DesktopMediaKeys.BaseId+i,0x4000,(uint)DesktopMediaKeys.Bindings[i].Key))mediaHotkeys.Add(DesktopMediaKeys.BaseId+i);else fallbackMediaKeys.Add(DesktopMediaKeys.Bindings[i].Key);
   if(fallbackMediaKeys.Count>0){mediaKeyboardCallback=MediaKeyboardMessage;mediaKeyboardHook=SetWindowsHookEx(13,mediaKeyboardCallback,GetModuleHandle(null),0);if(mediaKeyboardHook==IntPtr.Zero)Status.Text=L.T("Bazı multimedya tuşları başka bir uygulama tarafından kullanılıyor; Lyra odaktayken de kullanılabilir.");}
  };
  Closing+=(_,_)=>CloseDesktopPlayback();UpdateDesktopPlayback();
 }
 void CloseDesktopPlayback()
 {
  windowsMedia?.Dispose();windowsMedia=null;
  if(mediaKeyboardHook!=IntPtr.Zero){UnhookWindowsHookEx(mediaKeyboardHook);mediaKeyboardHook=IntPtr.Zero;}mediaKeyboardCallback=null;pressedMediaKeys.Clear();fallbackMediaKeys.Clear();
  if(mediaSource==null)return;foreach(int id in mediaHotkeys)UnregisterHotKey(mediaSource.Handle,id);mediaHotkeys.Clear();mediaSource.RemoveHook(DesktopMediaMessage);mediaSource=null;
 }
 IntPtr MediaKeyboardMessage(int code,IntPtr message,IntPtr data)
 {
  // Windows can reserve media hotkeys for another session. In that case only
  // the four explicitly supported transport keys are handled here. No other
  // keys are retained, logged or blocked. Never do I/O in this callback.
  if(code>=0&&!closed&&libraryReady)
  {
   int key=Marshal.ReadInt32(data);if(fallbackMediaKeys.Contains(key))
   {
    int kind=message.ToInt32();if(kind is 0x100 or 0x104){if(pressedMediaKeys.Add(key))QueueDesktopAction(DesktopMediaKeys.Bindings.First(b=>b.Key==key).Action);return new IntPtr(1);}
    if(kind is 0x101 or 0x105){pressedMediaKeys.Remove(key);return new IntPtr(1);}
   }
  }
  return CallNextHookEx(mediaKeyboardHook,code,message,data);
 }
 IntPtr DesktopMediaMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
 {
  if(windowsMedia!=null)return IntPtr.Zero; // Let Windows route media keys to its selected SMTC session exactly once.
  var action=message==0x312&&mediaHotkeys.Contains(wParam.ToInt32())?DesktopMediaKeys.Hotkey(wParam.ToInt32()):message==0x319?DesktopMediaKeys.AppCommand(lParam.ToInt64()):DesktopMediaAction.None;
  if(action==DesktopMediaAction.None)return IntPtr.Zero;handled=true;QueueDesktopAction(action);return new IntPtr(1);
 }
 void QueueDesktopAction(DesktopMediaAction action)
 {
  if(closed)return;Dispatcher.BeginInvoke(DispatcherPriority.Normal,async()=>{try{await RunDesktopAction(action);}catch(Exception ex){if(!closed)Status.Text=L.T("Oynatma kontrolü uygulanamadı: ")+ex.Message;}});
 }
 async Task RunDesktopAction(DesktopMediaAction action)
 {
  if(closed||!libraryReady)return;
  if(action==DesktopMediaAction.Stop)
  {
   pendingMediaAction=DesktopMediaAction.None;playCts?.Cancel();resumePrepared=false;resumeSeconds=0;audio.Stop();Scope.Reset();SyncPlaybackVisuals(true);SetSignal(L.T("Ses çıkışı kapatıldı"));Status.Text=L.T("Oynatma durduruldu.");UpdateDesktopPlayback();return;
  }
  if(action==DesktopMediaAction.Pause){pendingMediaAction=DesktopMediaAction.None;playCts?.Cancel();if(audio.IsPlaying)audio.Toggle();SyncPlaybackVisuals();UpdateDesktopPlayback();return;}
  if(mediaActionBusy){pendingMediaAction=action;return;}mediaActionBusy=true;
  try
  {
   switch(action)
   {
    case DesktopMediaAction.Toggle:await TogglePlayback();break;
    case DesktopMediaAction.Play:if(!audio.IsPlaying)await TogglePlayback();break;
    case DesktopMediaAction.Previous:await PreviousTrack();break;
    case DesktopMediaAction.Next:if(queue.Count>0)await Advance(false);break;
    case DesktopMediaAction.Shuffle:await ToggleShuffle();break;
    case DesktopMediaAction.Repeat:await CycleRepeat();break;
   }
  }
  catch(Exception ex){if(!closed)Status.Text=L.T("Oynatma kontrolü uygulanamadı: ")+ex.Message;}
  finally{mediaActionBusy=false;if(!closed){SyncPlaybackVisuals();UpdateDesktopPlayback();if(pendingMediaAction!=DesktopMediaAction.None){var pending=pendingMediaAction;pendingMediaAction=DesktopMediaAction.None;QueueDesktopAction(pending);}}}
 }
 void UpdateDesktopPlayback()
 {
  UpdateTopBarState();
  if(thumbnailPlay==null)return;
  bool playing=audio.IsPlaying,ready=libraryReady&&!closed,busy=playCts!=null||mediaActionBusy;
  bool canPlay=ready&&!busy&&(playing||audio.IsPaused||now!=null||queue.Count>0||Tracks.Visibility==Visibility.Visible&&Tracks.Items.Count>0);
  bool canPrevious=ready&&!busy&&(playing||audio.IsPaused||queueIndex>=0);
  bool canNext=ready&&!busy&&queueIndex>=0&&order.Peek(queue.Count,queueIndex,false)>=0;
  string description=now==null?"Lyra":$"{now.Title} — {now.Artist}\n{(playing?L.T("Çalıyor"):audio.IsPaused?L.T("Duraklatıldı"):L.T("Durduruldu"))} · Lyra";
  windowsMedia?.Update(now,playing,audio.IsPaused,ready,canPlay,canPrevious,canNext,PlayerPosition,PlayerDuration);
  string state=$"{playing}|{canPlay}|{canPrevious}|{canNext}|{description}";if(desktopState==state)return;desktopState=state;
  thumbnailPlay.Description=playing?L.T("Duraklat"):L.T("Oynat");thumbnailPlay.ImageSource=SvgIcon.ShellImage(playing?"pause":"play");thumbnailPlay.IsEnabled=canPlay;thumbnailPrevious!.IsEnabled=canPrevious;thumbnailNext!.IsEnabled=canNext;TaskbarItemInfo.Description=description;
 }
}
