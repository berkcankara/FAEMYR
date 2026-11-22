using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media;
using Windows.Media.Control;

namespace Faemyr;
public static class WindowsMediaSessionTests
{
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
 static object? Call(MainWindow window,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args);
 static async Task Wait(Func<bool> condition){for(int i=0;i<300;i++){if(condition())return;await Task.Delay(20);}throw new Exception("Windows media session timeout");}
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Themes.Load();
  Environment.SetEnvironmentVariable("LYRA_ARTIST_PHOTOS_OFFLINE","1");Environment.SetEnvironmentVariable("LYRA_ARTIST_BIOGRAPHY_OFFLINE","1");
  var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  Window? host=null;WindowsMediaSession? bridge=null;MainWindow? main=null;
  try
  {
   foreach(var (button,action) in new[]{(SystemMediaTransportControlsButton.Play,DesktopMediaAction.Play),(SystemMediaTransportControlsButton.Pause,DesktopMediaAction.Pause),(SystemMediaTransportControlsButton.Previous,DesktopMediaAction.Previous),(SystemMediaTransportControlsButton.Next,DesktopMediaAction.Next),(SystemMediaTransportControlsButton.Stop,DesktopMediaAction.Stop)})Check(WindowsMediaSession.ActionFor(button)==action,"System transport action maps correctly: "+action);
   Check(WindowsMediaSession.ActionFor(SystemMediaTransportControlsButton.Record)==DesktopMediaAction.None,"Unsupported Windows transport commands are ignored");
   string cover=Path.Combine(directory,"cover.png");var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[]{160,40,220,255,160,40,220,255,160,40,220,255,160,40,220,255},8)));using(var file=File.Create(cover))png.Save(file);
   var track=new Track{Path=Path.Combine(directory,"never-played.flac"),Title="Faemyr Windows media integration fixture",Artist="Faemyr test artist",AlbumArtist="Faemyr album artist",Album="Faemyr test album",Cover=cover,Seconds=90};
   var actions=new ConcurrentQueue<DesktopMediaAction>();var seeks=new ConcurrentQueue<TimeSpan>();
   host=new Window{Title="Faemyr media test",Width=320,Height=200,Opacity=0,ShowActivated=false,ShowInTaskbar=false};host.Show();bridge=new(new WindowInteropHelper(host).Handle,actions.Enqueue,seeks.Enqueue);
   bridge.Update(track,false,false,true,true,false,true,TimeSpan.FromSeconds(3),TimeSpan.FromSeconds(90));
   Check(!bridge.Published&&!bridge.Controls.IsEnabled,"Restoring an unplayed track does not create an active Windows card or steal another app's media keys");
   bridge.Update(track,true,false,true,true,true,true,TimeSpan.FromSeconds(12),TimeSpan.FromSeconds(90));
   Check(bridge.Published&&bridge.Controls.IsEnabled&&bridge.Controls.PlaybackStatus==MediaPlaybackStatus.Playing,"Manual session publishes playback state without an audio player");
   GlobalSystemMediaTransportControlsSessionManager? manager=null;
   try{manager=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();}
   catch(UnauthorizedAccessException ex){File.WriteAllText(Path.Combine(directory,"windows-session-readback.json"),JsonSerializer.Serialize(new{available=false,reason=ex.Message}));}
   if(manager!=null)
   {
   GlobalSystemMediaTransportControlsSession? session=null;
   for(int i=0;i<150&&session==null;i++)
   {
    foreach(var candidate in manager.GetSessions().Where(s=>s.SourceAppUserModelId.Contains("Faemyr",StringComparison.OrdinalIgnoreCase)))
    {var props=await candidate.TryGetMediaPropertiesAsync();if(props.Title==track.Title){session=candidate;break;}}
    if(session==null)await Task.Delay(40);
   }
   Check(session!=null,"Windows global media-session manager recognizes the actual Faemyr session");
   var metadata=await session!.TryGetMediaPropertiesAsync();
   Check(metadata.Title==track.Title&&metadata.Artist==track.Artist&&metadata.AlbumTitle==track.Album&&metadata.AlbumArtist==track.AlbumArtist,"Windows exposes the exact track title, artist and album metadata");
   for(int i=0;i<100&&metadata.Thumbnail==null;i++){await Task.Delay(30);metadata=await session.TryGetMediaPropertiesAsync();}
   Check(metadata.Thumbnail!=null,"Windows receives the local album artwork");
   using(var image=await metadata.Thumbnail!.OpenReadAsync())Check(image.Size>0,"Windows can read the album artwork stream");
   var timeline=session.GetTimelineProperties();Check(timeline.Position==TimeSpan.FromSeconds(12)&&timeline.EndTime==TimeSpan.FromSeconds(90),"Windows receives track position and full duration");
   await session.TryPauseAsync();await Wait(()=>actions.Contains(DesktopMediaAction.Pause));
   Check(actions.Count(a=>a==DesktopMediaAction.Pause)==1,"A real Windows Pause request is delivered exactly once");
   bridge.Update(track,false,true,true,true,true,true,TimeSpan.FromSeconds(13),TimeSpan.FromSeconds(90));
   await session.TryPlayAsync();await Wait(()=>actions.Contains(DesktopMediaAction.Play));await session.TrySkipNextAsync();await Wait(()=>actions.Contains(DesktopMediaAction.Next));await session.TrySkipPreviousAsync();await Wait(()=>actions.Contains(DesktopMediaAction.Previous));
   Check(actions.Count==4,"Windows Play, Next and Previous use the shared transport command path without duplicate keyboard events");
   await session.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(42).Ticks);await Wait(()=>seeks.Count==1);Check(seeks.TryPeek(out var seek)&&seek==TimeSpan.FromSeconds(42),"Windows timeline seeking preserves the requested timestamp");
   }
   else
   {
    var updater=bridge.Controls.DisplayUpdater;
    Check(updater.Type==MediaPlaybackType.Music&&updater.MusicProperties.Title==track.Title&&updater.MusicProperties.Artist==track.Artist&&updater.MusicProperties.AlbumTitle==track.Album,"The native Windows display updater contains music title, artist and album metadata");
    await Wait(()=>updater.Thumbnail!=null);using(var image=await updater.Thumbnail.OpenReadAsync())Check(image.Size>0,"The native Windows thumbnail reference exposes readable local artwork");
   }
   bridge.Update(track,false,true,true,true,true,false,TimeSpan.FromSeconds(42),TimeSpan.FromSeconds(90));
   Check(bridge.Controls.PlaybackStatus==MediaPlaybackStatus.Paused&&!bridge.Controls.IsNextEnabled&&bridge.Controls.IsPlayEnabled,"Paused status and queue boundary button availability are accurate");
   track.Title="Faemyr second fixture";track.Cover=Path.Combine(directory,"missing.png");bridge.Update(track,false,true,true,true,false,false,TimeSpan.Zero,TimeSpan.FromSeconds(60));
   Check(bridge.Controls.DisplayUpdater.MusicProperties.Title==track.Title&&bridge.Controls.DisplayUpdater.Thumbnail==null,"Track change clears previous artwork when the next cover is missing");
   bridge.Update(null,false,false,true,false,false,false,TimeSpan.Zero,TimeSpan.Zero);Check(!bridge.Controls.IsEnabled&&!bridge.Published,"Removing the current track removes its Windows media card");
   bridge.Dispose();Check(!bridge.Controls.IsEnabled&&bridge.Controls.PlaybackStatus==MediaPlaybackStatus.Closed,"Disposal disables the Windows session and clears its metadata");bridge=null;host.Close();host=null;
   File.WriteAllText(track.Path,"Metadata-only fixture, never decoded.");track.Fields=[new("Vorbis / LYRICS","A local lyric")];using(var store=new LibraryStore())store.Save(track);
   PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{AutoPlayOnStartup=false});PlayerJson.Write(Path.Combine(Paths.Data,"session.json"),new PlayerSession{Queue=[track.Path],QueueIndex=0,TrackPath=track.Path,Seconds=10,View="tracks"});
   main=new MainWindow{Opacity=0,ShowActivated=false,ShowInTaskbar=false};main.Show();await Wait(()=>Field<bool>(main,"sessionReady"));
   var integration=Field<WindowsMediaSession>(main,"windowsMedia");Check(integration!=null&&!integration.Published,"The real player initializes SMTC without activating an unplayed restored session");
   Check(Field<List<int>>(main,"mediaHotkeys").Count==0&&Field<IntPtr>(main,"mediaKeyboardHook")==IntPtr.Zero,"SMTC integration does not register global media hotkeys or a low-level keyboard hook");
   Call(main,"SystemMediaSeek",TimeSpan.FromSeconds(35));await Task.Delay(70);Check(Math.Abs(Field<double>(main,"resumeSeconds")-35)<.001,"Windows seek delegates to the real player's bounded seek path while remaining stopped");
   var audio=Field<AudioEngine>(main,"audio");Check(audio.OutputStartCount==0&&!audio.DeviceStarted&&!audio.IsPlaying,"The complete integration test never opens an audio output device");
   main.Close();main=null;Check(!integration!.Controls.IsEnabled,"Closing the real player disables and releases its Windows media session");
  }
  finally{bridge?.Dispose();host?.Close();main?.Close();File.WriteAllText(Path.Combine(directory,"windows-media-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
