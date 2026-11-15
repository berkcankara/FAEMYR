using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ahenk;
public static class LyricsSeekTests
{
 static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target)!;
 static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(target,value);
 static object? Call(MainWindow window,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args);
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Themes.Load();
  var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  MainWindow? window=null;
  try
  {
   string path=Path.Combine(directory,"lyrics-only.flac");File.WriteAllText(path,"Metadata fixture; never decoded or played.");
   var doc=new LyricsDocument{Source="Dosyadan",Synced="[00:01.250] First\n[00:09.500][00:21.000] Repeated\n[00:40] Beyond duration\n[offset:+250]"};
   var track=new Track{Path=path,Title="Lyrics seek",Artist="Test",AlbumArtist="Test",Album="Test",Seconds=30,SampleRate=44100,Bits=16,Channels=2,BitRate=1000,Codec="flac",Fields=[new("Vorbis / SYNCED_LYRICS",doc.Synced)]};
   using(var store=new LibraryStore())store.Save(track);
   PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{AutoPlayOnStartup=false});
   PlayerJson.Write(Path.Combine(Paths.Data,"session.json"),new PlayerSession{Queue=[path],QueueIndex=0,TrackPath=path,Seconds=0,View="tracks"});
   window=new MainWindow{Opacity=0,ShowActivated=false,ShowInTaskbar=false};window.Show();
   for(int i=0;i<200&&(!Field<bool>(window,"sessionReady")||Field<List<TextBlock>>(window,"lyricBlocks").Count!=4);i++)await Task.Delay(25);
   Check(Field<bool>(window,"sessionReady")&&Field<List<TextBlock>>(window,"lyricBlocks").Count==4,"Real player restores synchronized lyrics with offset and repeated timestamps");
   var audio=Field<AudioEngine>(window,"audio");var slider=Field<Slider>(window,"Seek");
   List<TextBlock> Blocks()=>Field<List<TextBlock>>(window!,"lyricBlocks");
   bool Click(TextBlock block,MouseButton button=MouseButton.Left)
   {
    var args=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,button){RoutedEvent=button==MouseButton.Left?UIElement.MouseLeftButtonUpEvent:UIElement.MouseRightButtonUpEvent};block.RaiseEvent(args);return args.Handled;
   }
   Check(Blocks().All(b=>b.Cursor==Cursors.Hand&&b.Tag is LyricLine),"Every timed lyric row exposes a hand cursor and its parsed timestamp");
   Check(Click(Blocks()[1])&&Math.Abs(Field<double>(window,"resumeSeconds")-9.75)<.000001&&slider.Value==9.75,"One left click seeks forward to the exact fractional timestamp including LRC offset");
   Check(Click(Blocks()[0])&&Field<double>(window,"resumeSeconds")==1.5,"An earlier lyric seeks backward");
   Check(!Click(Blocks()[2],MouseButton.Right)&&Field<double>(window,"resumeSeconds")==1.5,"Right click does not seek");
   Check(Click(Blocks()[2])&&Field<double>(window,"resumeSeconds")==21.25,"Repeated lyric text uses the clicked occurrence's own timestamp");
   Check(!audio.DeviceStarted&&!audio.IsPlaying&&!audio.IsPaused,"Seeking a restored track never opens an endpoint or starts playback");
   var previous=Blocks()[0];Call(window,"RenderLyrics",doc);Check(!Click(previous)&&Field<double>(window,"resumeSeconds")==21.25,"Rows detached by a lyric refresh cannot seek");
   Set(window,"lyricsTrackPath","different-track.flac");Check(!Click(Blocks()[0])&&Field<double>(window,"resumeSeconds")==21.25,"Lyrics belonging to another track cannot seek the current track");Set(window,"lyricsTrackPath",path);
   var row=Blocks()[0];var stamp=row.Tag;row.Tag=new LyricLine(double.NaN,"Invalid");Check(!Click(row)&&Field<double>(window,"resumeSeconds")==21.25,"Invalid timestamps are rejected before reaching the audio engine");row.Tag=stamp;
   Check(Click(Blocks()[3])&&slider.Value==30&&Field<double>(window,"resumeSeconds")==30,"A lyric beyond the recording is bounded by its duration");
   Call(window,"EnterFullscreenPlayer");Check(Field<bool>(window,"fullscreenPlayer")&&Click(Blocks()[1])&&Field<double>(window,"resumeSeconds")==9.75,"The same clickable lyric rows seek in fullscreen");
   // Model an endpoint-released pause; no PCM or hardware output is created.
   Set(audio,"releasedPause",true);Set(audio,"pausedDuration",TimeSpan.FromSeconds(30));Set(audio,"pausedPosition",TimeSpan.Zero);
   Check(Click(Blocks()[2])&&audio.Position==TimeSpan.FromSeconds(21.25)&&audio.IsPaused&&!audio.DeviceStarted,"A paused lyric seek updates the engine position while preserving pause and releasing the device");
   Check(Field<int>(window,"lyricActive")==2,"Current lyric emphasis follows the seek immediately");
   audio.Stop();Call(window,"ExitFullscreenPlayer");
   var stale=Blocks()[0];var plain=new LyricsDocument{Source="Dosyadan",Plain="No timestamps"};Set(window,"lyricsDocument",plain);Call(window,"RenderLyrics",plain);
   Check(Blocks().Count==0&&!Click(stale),"Plain lyrics do not expose seekable rows or stale timing");
   Check(audio.OutputStartCount==0&&!audio.DeviceStarted,"All lyric interactions complete without opening audio output");
  }
  finally{window?.Close();File.WriteAllText(Path.Combine(directory,"lyrics-seek-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
