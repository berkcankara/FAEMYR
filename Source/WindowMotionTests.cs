using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Faemyr;
public static class WindowMotionTests
{
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
 static object? Call(MainWindow window,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,args);
 static async Task Wait(Func<bool> condition){for(int i=0;i<400;i++){if(condition())return;await Task.Delay(15);}throw new Exception("Window motion timeout");}
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);Environment.SetEnvironmentVariable("LYRA_ARTIST_PHOTOS_OFFLINE","1");Environment.SetEnvironmentVariable("LYRA_ARTIST_BIOGRAPHY_OFFLINE","1");Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Themes.Load();
  var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  MainWindow? window=null;
  try
  {
   string path=Path.Combine(directory,"metadata-only.flac");File.WriteAllText(path,"Never decoded: window and layout checks only.");
   using(var store=new LibraryStore())store.Save(new Track{Path=path,Title="Motion fixture",Artist="Test",AlbumArtist="Test",Album="Test",Seconds=90,Bits=16,SampleRate=44100,Channels=2,Codec="flac",Fields=[new("Vorbis / SYNCED_LYRICS","[00:01] First line\n[00:10] Second line\n[00:20] Third line")]});
   PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{AutoPlayOnStartup=false});
   PlayerJson.Write(Path.Combine(Paths.Data,"session.json"),new PlayerSession{Queue=[path],QueueIndex=0,TrackPath=path,View="tracks"});
   window=new MainWindow{Opacity=0,ShowActivated=false,ShowInTaskbar=false};window.Show();
   await Wait(()=>Field<bool>(window,"sessionReady")&&Field<List<TextBlock>>(window,"lyricBlocks").Count==3);
   var audio=Field<AudioEngine>(window,"audio");var hwnd=new WindowInteropHelper(window).Handle;
   bool NativeStyles()=>((long)GetWindowLongPtr(hwnd,-16)&0x00cf0000)==0x00cf0000;
   Check(window.WindowStyle==WindowStyle.SingleBorderWindow&&NativeStyles(),"Custom frame retains native caption, system menu, resize, minimize and maximize styles");
   var original=new Rect(window.Left,window.Top,window.ActualWidth,window.ActualHeight);var chrome=WindowChrome.GetWindowChrome(window);
   Call(window,"Maximize_Click",window,new RoutedEventArgs());await Wait(()=>window.WindowState==WindowState.Maximized);
   Check(NativeStyles(),"Standard maximize preserves the Windows compositor window styles");
   Call(window,"Maximize_Click",window,new RoutedEventArgs());await Wait(()=>window.WindowState==WindowState.Normal);
   Call(window,"Minimize_Click",window,new RoutedEventArgs());await Wait(()=>window.WindowState==WindowState.Minimized);
   SystemCommands.RestoreWindow(window);await Wait(()=>window.WindowState==WindowState.Normal);
   Check(Math.Abs(window.ActualWidth-original.Width)<2&&Math.Abs(window.ActualHeight-original.Height)<2,"System minimize and restore preserve the normal window bounds");
   Call(window,"EnterFullscreenPlayer");await Wait(()=>window.WindowState==WindowState.Maximized);window.UpdateLayout();
   Check(NativeStyles()&&WindowChrome.GetWindowChrome(window)!=null,"Fullscreen uses a native maximize with a custom frame instead of manually teleporting window bounds");
   var root=Field<Grid>(window,"FullscreenRoot");var cover=Field<Border>(window,"FullscreenCover");
   Rect Cover()=>cover.TransformToAncestor(root).TransformBounds(new Rect(cover.RenderSize));
   var old=Cover();Call(window,"FullscreenLyrics_Click",window,new RoutedEventArgs());var start=Cover();
   Check(!SystemParameters.ClientAreaAnimation||Math.Abs(old.Left-start.Left)<3&&Math.Abs(old.Top-start.Top)<3&&Math.Abs(old.Width-start.Width)<3,"Hiding lyrics preserves the artwork's rendered position and size at the start of the transition");
   if(SystemParameters.ClientAreaAnimation)
   {
    await Task.Delay(130);var middle=Cover();await Task.Delay(300);var end=Cover();
    Check(middle.Left>old.Left+2&&middle.Left<end.Left-2,"Artwork passes through intermediate horizontal positions while lyrics close");
   }
   else await Task.Delay(400);
   Check(!Field<bool>(window,"fullscreenArtworkMoving")&&Math.Abs(Field<TranslateTransform>(window,"FullscreenArtworkMotion").X)<.1&&Math.Abs(Field<ScaleTransform>(window,"FullscreenArtworkScale").ScaleX-1)<.001,"Completed transition removes all temporary artwork offsets and scale");
   var centered=Cover();Call(window,"FullscreenLyrics_Click",window,new RoutedEventArgs());var reopen=Cover();
   Check(!SystemParameters.ClientAreaAnimation||Math.Abs(centered.Left-reopen.Left)<3&&Math.Abs(centered.Width-reopen.Width)<3,"Showing lyrics begins at the currently rendered cover position without a snap");
   await Task.Delay(110);var interrupted=Cover();Call(window,"FullscreenLyrics_Click",window,new RoutedEventArgs());var retargeted=Cover();
   Check(!SystemParameters.ClientAreaAnimation||Math.Abs(interrupted.Left-retargeted.Left)<3&&Math.Abs(interrupted.Top-retargeted.Top)<3&&Math.Abs(interrupted.Width-retargeted.Width)<3,"Rapid repeated lyric toggles retarget from the current animation rather than jumping");
   await Task.Delay(430);Call(window,"FullscreenLyrics_Click",window,new RoutedEventArgs());await Task.Delay(430);window.UpdateLayout();
   Check(Math.Abs(Cover().Left-old.Left)<2&&Math.Abs(Cover().Width-old.Width)<2,"Reopening lyrics returns exactly to the original layout");
   Field<System.Windows.Threading.DispatcherTimer>(window,"fullscreenControlsIdleTimer").Stop();
   root.Width=1120;root.Height=1080;window.UpdateLayout();await Task.Delay(80);window.UpdateLayout();var narrow=Cover();
   Call(window,"FullscreenLyrics_Click",window,new RoutedEventArgs());var scaledStart=Cover();
   Check(!SystemParameters.ClientAreaAnimation||Math.Abs(narrow.Width-scaledStart.Width)<2&&Math.Abs(narrow.Left-scaledStart.Left)<2&&Math.Abs(narrow.Top-scaledStart.Top)<2,"Narrow layouts animate cover size and position together without a first-frame jump");
   await Task.Delay(430);Check(Cover().Width>narrow.Width+20,"Hidden lyrics allow a larger cover after its smooth scale transition");
   Call(window,"FullscreenLyrics_Click",window,new RoutedEventArgs());await Task.Delay(430);root.ClearValue(FrameworkElement.WidthProperty);root.ClearValue(FrameworkElement.HeightProperty);window.UpdateLayout();
   var idle=Field<FullscreenControlsIdleState>(window,"fullscreenControlsIdle");double clock=System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency;
   idle.Reset(clock-6,new Point());Call(window,"FullscreenControlsIdleTick");await Task.Delay(400);window.UpdateLayout();var idleCover=Cover();
   Call(window,"FullscreenLyrics_Click",window,new RoutedEventArgs());var idleStart=Cover();
   Check(!SystemParameters.ClientAreaAnimation||Math.Abs(idleCover.Left-idleStart.Left)<2&&Math.Abs(idleCover.Top-idleStart.Top)<2,"Lyrics toggle preserves cover continuity while the bottom player is hidden");
   await Task.Delay(430);Check(Math.Abs(Cover().Top+Cover().Height/2-root.ActualHeight/2)<1,"Artwork remains vertically centered after the hidden-player lyric transition");
   Call(window,"FullscreenLyrics_Click",window,new RoutedEventArgs());await Task.Delay(430);
   Call(window,"FullscreenLyrics_Click",window,new RoutedEventArgs());Call(window,"ExitFullscreenPlayer");await Wait(()=>window.WindowState==WindowState.Normal);window.UpdateLayout();
   Check(!Field<bool>(window,"fullscreenArtworkMoving")&&Math.Abs(Field<TranslateTransform>(window,"FullscreenArtworkMotion").X)<.1,"Leaving fullscreen cancels a running artwork transition");
   Check(ReferenceEquals(WindowChrome.GetWindowChrome(window),chrome)&&NativeStyles()&&Math.Abs(window.Width-original.Width)<2&&Math.Abs(window.Height-original.Height)<2,"Fullscreen exit restores normal dimensions, original chrome and native animation styles");
   SystemCommands.MaximizeWindow(window);await Wait(()=>window.WindowState==WindowState.Maximized);Call(window,"EnterFullscreenPlayer");window.UpdateLayout();Call(window,"ExitFullscreenPlayer");window.UpdateLayout();
   Check(window.WindowState==WindowState.Maximized&&ReferenceEquals(WindowChrome.GetWindowChrome(window),chrome),"Entering from an already maximized window restores the maximized work-area layout");
   SystemCommands.RestoreWindow(window);await Wait(()=>window.WindowState==WindowState.Normal);window.Width=1120;window.Height=720;
   Call(window,"EnterFullscreenPlayer");await Wait(()=>window.WindowState==WindowState.Maximized);Call(window,"FullscreenLyrics_Click",window,new RoutedEventArgs());await Task.Delay(430);Call(window,"ExitFullscreenPlayer");await Wait(()=>window.WindowState==WindowState.Normal);
   Check(Math.Abs(window.Width-1120)<2&&Math.Abs(window.Height-720)<2,"Small normal window dimensions survive the fullscreen cycle");
   Check(audio.OutputStartCount==0&&!audio.DeviceStarted&&!audio.IsPlaying,"All native state and lyric-layout checks complete without opening audio hardware");
  }
  finally{window?.Close();File.WriteAllText(Path.Combine(directory,"window-motion-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}

