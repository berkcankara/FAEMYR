using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using NAudio.Wave;
namespace Ahenk;
public static class DesktopPlaybackTests
{
 [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,int message,IntPtr wp,IntPtr lp);
 [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
 [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  async Task Until(Func<bool> condition){for(int n=0;n<200;n++){if(condition())return;await Task.Delay(25);}throw new Exception("Desktop control timeout");}
  Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Themes.Load();MainWindow? window=null;
  try
  {
   foreach(var (command,action) in new[]{(14,DesktopMediaAction.Toggle),(46,DesktopMediaAction.Play),(47,DesktopMediaAction.Pause),(11,DesktopMediaAction.Next),(12,DesktopMediaAction.Previous),(13,DesktopMediaAction.Stop)})Check(DesktopMediaKeys.AppCommand(((long)command|0x8000)<<16)==action,"APPCOMMAND mapping strips device bits: "+action);
   Check(DesktopMediaKeys.AppCommand(10L<<16)==DesktopMediaAction.None,"Volume keys stay with Windows");Check(DesktopMediaKeys.Hotkey(1)==DesktopMediaAction.None,"Foreign hotkey IDs ignored");
   foreach(var name in new[]{"previous","play","pause","next"}){var image=SvgIcon.ShellImage(name);Check(image.IsFrozen&&image.Width>=24&&image.Height>=24&&ReferenceEquals(image,SvgIcon.ShellImage(name)),"Native SVG shell icon cached and scalable: "+name);}
   var paths=new List<string>();using(var db=new LibraryStore())for(int i=1;i<=3;i++)
   {
    string path=Path.Combine(directory,$"silent-desktop-{i}.wav");paths.Add(path);var f=new WaveFormat(48000,16,2);using(var writer=new WaveFileWriter(path,f)){var bytes=new byte[f.AverageBytesPerSecond*20];writer.Write(bytes,0,bytes.Length);}db.Save(new Track{Path=path,Title="Sessiz deneme "+i,Artist="Lyra",AlbumArtist="Lyra",Album="Masaüstü kontrolleri",SampleRate=48000,Bits=16,Channels=2,SampleFormat="s16",Codec="pcm_s16le",Seconds=20,Number=(uint)i});
   }
   await File.WriteAllTextAsync(Path.Combine(Paths.Data,"queue.json"),JsonSerializer.Serialize(paths));new AudioPreferences{Exclusive=false}.Save(Path.Combine(Paths.Data,"audio-settings.json"));
   window=new MainWindow();window.Show();await Until(()=>Field<bool>(window,"libraryReady"));await Task.Delay(1000);
   var shell=typeof(Window).GetField("_taskbarList",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(window);Check(shell!=null,"Native Windows taskbar COM connection established");
   // Call the same native update used by WPF and inspect its HRESULT. Merely
   // constructing ThumbButtonInfos or invoking their managed events is not proof
   // that Explorer accepted a toolbar. In a restricted launch this must fail.
   var nativeResult=typeof(Window).GetMethod("UpdateTaskbarThumbButtons",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);Check(nativeResult?.ToString()=="S_OK","Explorer accepts thumbnail toolbar update (native S_OK)");
   var buttons=window.TaskbarItemInfo.ThumbButtonInfos;var audio=Field<AudioEngine>(window,"audio");var handle=new WindowInteropHelper(window).Handle;
   Check(buttons.Count==3&&buttons[0].Description=="Önceki parça"&&buttons[1].Description=="Oynat"&&buttons[2].Description=="Sonraki parça","Taskbar exposes previous / play / next in order");
   Check(buttons[1].IsEnabled&&!buttons[0].IsEnabled&&!buttons[2].IsEnabled,"Idle taskbar controls reflect saved queue");
   int registered=Field<List<int>>(window,"mediaHotkeys").Count,fallback=Field<HashSet<int>>(window,"fallbackMediaKeys").Count;bool fallbackActive=Field<IntPtr>(window,"mediaKeyboardHook")!=IntPtr.Zero;
   Check(registered+fallback==4&&(fallback==0||fallbackActive),"All four global multimedia keys covered by registration or transport-only fallback");
   // Use the real native message queue of this test window, never other apps or
   // synthetic keyboard input. This also exercises hook dispatch while minimized.
   window.WindowState=WindowState.Minimized;
   void Hotkey(int index)
   {
    if(Field<List<int>>(window,"mediaHotkeys").Contains(DesktopMediaKeys.BaseId+index)){if(!PostMessage(handle,0x312,new IntPtr(DesktopMediaKeys.BaseId+index),IntPtr.Zero))throw new Exception("Message queue rejected media event");return;}
    // Exercise this app's callback directly with a synthetic media-only record;
    // do not inject input into Windows or touch other applications.
    IntPtr data=Marshal.AllocHGlobal(24);try{Marshal.WriteInt32(data,DesktopMediaKeys.Bindings[index].Key);var callback=typeof(MainWindow).GetMethod("MediaKeyboardMessage",BindingFlags.Instance|BindingFlags.NonPublic)!;callback.Invoke(window,[0,new IntPtr(0x100),data]);callback.Invoke(window,[0,new IntPtr(0x101),data]);}finally{Marshal.FreeHGlobal(data);}
   }
   Hotkey(0);await Until(()=>audio.IsPlaying);await Task.Delay(200);Check(window.WindowState==WindowState.Minimized&&audio.DeviceStarted,"Global Play works with minimized main window");
   Check(buttons[1].Description=="Duraklat"&&ReferenceEquals(buttons[1].ImageSource,SvgIcon.ShellImage("pause")),"Taskbar play icon changes to pause");Check(window.TaskbarItemInfo.Description.Contains("Sessiz deneme 1"),"Taskbar description identifies actual playing track");
   Hotkey(0);await Until(()=>audio.IsPaused);await Task.Delay(150);Check(!audio.DeviceStarted&&buttons[1].Description=="Oynat","Media Pause stops endpoint and restores play icon");
   Hotkey(2);await Until(()=>Field<Track>(window,"now").Path==paths[1]&&audio.IsPlaying);Check(window.WindowState==WindowState.Minimized,"Next changes track without opening main window");
   Hotkey(1);await Until(()=>Field<Track>(window,"now").Path==paths[0]&&audio.IsPlaying);Check(true,"Media Previous returns to previous queue item");
   PostMessage(handle,0x319,handle,new IntPtr(47L<<16));await Until(()=>audio.IsPaused);Check(!audio.DeviceStarted,"Discrete PAUSE app command reaches endpoint stop");
   PostMessage(handle,0x319,handle,new IntPtr(46L<<16));await Until(()=>audio.IsPlaying);Check(true,"Discrete PLAY app command resumes");
   Hotkey(3);await Until(()=>!audio.IsPlaying&&!audio.IsPaused);Check(!audio.DeviceStarted,"Media Stop closes endpoint");
   void Click(int index){if(!PostMessage(handle,0x111,new IntPtr((0x1800<<16)|index),IntPtr.Zero))throw new Exception("Thumbnail click message rejected");}
   Click(1);await Until(()=>audio.IsPlaying);Check(true,"Taskbar Play click uses same playback flow");
   Click(2);await Until(()=>Field<Track>(window,"now").Path==paths[1]&&audio.IsPlaying);Check(true,"Taskbar Next click changes queue item");
   Click(0);await Until(()=>Field<Track>(window,"now").Path==paths[0]&&audio.IsPlaying);Check(true,"Taskbar Previous click changes queue item");
   Hotkey(3);await Until(()=>!audio.IsPlaying&&!audio.IsPaused);var registeredIds=Field<List<int>>(window,"mediaHotkeys").ToArray();var closedWindow=window;window.Close();window=null;
   Check(Field<IntPtr>(closedWindow,"mediaKeyboardHook")==IntPtr.Zero&&Field<HashSet<int>>(closedWindow,"pressedMediaKeys").Count==0,"Closing Lyra removes transport-only fallback hook");
   using var probe=new HwndSource(new HwndSourceParameters("Lyra registration lifecycle test"){Width=1,Height=1,WindowStyle=0});
   bool released=true;foreach(int oldId in registeredIds){int i=oldId-DesktopMediaKeys.BaseId,id=0x5c10+i;bool acquired=RegisterHotKey(probe.Handle,id,0x4000,(uint)DesktopMediaKeys.Bindings[i].Key);released&=acquired;if(acquired)UnregisterHotKey(probe.Handle,id);}Check(released&&Field<List<int>>(closedWindow,"mediaHotkeys").Count==0,"Closing Lyra releases its global media key registrations");
  }
  finally{window?.Close();await File.WriteAllTextAsync(Path.Combine(directory,"desktop-playback-results.json"),JsonSerializer.Serialize(new{version=typeof(MainWindow).Assembly.GetName().Version?.ToString(),checks},new JsonSerializerOptions{WriteIndented=true}));}
 }
}
