using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Ahenk;
public static class FullscreenLyricsTests
{
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
 static void Set(MainWindow window,string name,object? value)=>typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,value);
 static object? Call(MainWindow window,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args);
 static async Task Wait(Func<bool> ready){for(int i=0;i<400;i++){if(ready())return;await Task.Delay(15);}throw new Exception("Fullscreen lyrics test timeout");}
 sealed class Handler:HttpMessageHandler
 {
  public int Requests;
  public TaskCompletionSource<HttpResponseMessage>? Pending;
  public string Response="";
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
  {
   Requests++;
   if(Pending!=null)return await Pending.Task.WaitAsync(ct);
   return new(HttpStatusCode.OK){Content=new StringContent(Response,Encoding.UTF8,"application/json")};
  }
 }
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Themes.Load();
  Environment.SetEnvironmentVariable("LYRA_ARTIST_PHOTOS_OFFLINE","1");Environment.SetEnvironmentVariable("LYRA_ARTIST_BIOGRAPHY_OFFLINE","1");
  var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  MainWindow? window=null;
  try
  {
   var tracks=new List<Track>();
   for(int i=0;i<4;i++)
   {
    string path=Path.Combine(directory,$"lyrics-{i}.flac");File.WriteAllText(path,"Metadata fixture: never decoded or played.");
    tracks.Add(new Track{Path=path,Title=$"Lyrics {i}",Artist="Test Artist",AlbumArtist="Test Artist",Album="Test Album",Seconds=180,SampleRate=44100,Bits=16,Channels=2,Codec="flac"});
   }
   tracks[0].Fields.Add(new("Vorbis / SYNCED_LYRICS",string.Join('\n',Enumerable.Range(0,60).Select(i=>$"[00:{i:00}] First track line {i}"))));
   var cached=new LyricsDocument{Source="LRCLIB",RemoteId=42,Plain="Second track cached lyrics",Synced="[00:01] Second track cached lyrics\n[00:02] Next cached line"};
   using(var cache=new LyricsService(Path.Combine(Paths.Data,"lyrics")))await cache.SaveAsync(tracks[1],cached);
   tracks[3].Fields.Add(new("Vorbis / LYRICS",string.Join('\n',Enumerable.Range(0,80).Select(i=>"Long plain lyrics "+i))));
   using(var store=new LibraryStore())store.SaveMany(tracks);
   PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{AutoPlayOnStartup=false});
   PlayerJson.Write(Path.Combine(Paths.Data,"session.json"),new PlayerSession{Queue=tracks.Select(t=>t.Path).ToList(),QueueIndex=0,TrackPath=tracks[0].Path,View="tracks"});
   window=new MainWindow{Opacity=0,ShowActivated=false,ShowInTaskbar=false};window.Show();
   await Wait(()=>Field<bool>(window,"sessionReady")&&Field<List<TextBlock>>(window,"lyricBlocks").Count==60);
   var fetch=Field<Button>(window,"LyricsFetch");var card=Field<InnerAccentBorder>(window,"LyricsCard");var plain=Field<TextBox>(window,"LyricsPlain");
   async Task Select(Track track)
   {
    Set(window,"now",track);Call(window,"ShowTrackDetails",track);
    await Wait(()=>Field<string>(window,"lyricsTrackPath")==track.Path&&fetch.IsEnabled);window.UpdateLayout();await Task.Delay(60);window.UpdateLayout();
   }
   Call(window,"EnterFullscreenPlayer");window.UpdateLayout();
   Check(card.IsVisible&&!fetch.IsVisible&&Field<ScrollViewer>(window,"LyricsScroll").IsVisible,"Existing synchronized lyrics appear when fullscreen opens");
   Field<ScrollViewer>(window,"LyricsScroll").ScrollToEnd();await Select(tracks[1]);
   Check(Field<List<TextBlock>>(window,"lyricBlocks").First().Text=="Second track cached lyrics"&&card.IsVisible&&!fetch.IsVisible,"Changing tracks in fullscreen automatically replaces lyrics from the offline cache");
   Check(Field<ScrollViewer>(window,"LyricsScroll").VerticalOffset<1,"A new synchronized document starts at its own first line");
   await Select(tracks[3]);plain.ScrollToEnd();window.UpdateLayout();
   await Select(tracks[2]);
   Check(card.IsVisible&&fetch.IsVisible&&fetch.IsEnabled&&Equals(fetch.Content,"Şarkı sözlerini bul")&&!plain.IsVisible,"Missing lyrics show an enabled fullscreen search button instead of an empty region");
   Check(!Field<StackPanel>(window,"LyricsHeader").IsVisible&&card.PlainPresentation,"Fullscreen search retains the unframed lyrics presentation without a heading");
   var handler=new Handler{Response=JsonSerializer.Serialize(new LyricsRecord{Id=99,TrackName=tracks[2].Title,ArtistName=tracks[2].Artist,AlbumName=tracks[2].Album,Duration=180,PlainLyrics="Fetched lyrics",SyncedLyrics="[00:01] Fetched lyrics"})};
   Field<LyricsService>(window,"lyrics").Dispose();Set(window,"lyrics",new LyricsService(Path.Combine(Paths.Data,"lyrics"),handler));
   fetch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(()=>fetch.IsEnabled&&Field<LyricsDocument>(window,"lyricsDocument")?.RemoteId==99);window.UpdateLayout();
   Check(handler.Requests==1&&Field<List<TextBlock>>(window,"lyricBlocks").First().Text=="Fetched lyrics"&&!fetch.IsVisible,"Fullscreen search uses the shared fetch action and displays the returned lyrics immediately");
   await Select(tracks[3]);plain.ScrollToEnd();window.UpdateLayout();
   var shortPlain=tracks[3].Copy();shortPlain.Fields=[new("Vorbis / LYRICS","Short replacement lyrics")];await Select(shortPlain);
   Check(plain.IsVisible&&plain.Text=="Short replacement lyrics"&&plain.VerticalOffset<1,"Replacing a scrolled plain document resets its viewport and shows the new text");
   // Cancel a pending old-track request, then prove it cannot clear the new cache result.
   var missing=tracks[2].Copy();missing.Title="No cached match";await Select(missing);handler.Pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
   fetch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(()=>handler.Requests>=2);await Select(tracks[1]);
   handler.Pending.TrySetResult(new(HttpStatusCode.NotFound));await Task.Delay(100);
   Check(Field<List<TextBlock>>(window,"lyricBlocks").First().Text=="Second track cached lyrics"&&!fetch.IsVisible,"A canceled request from the previous track cannot replace or hide the new lyrics");
   await Select(missing);L.Apply("en");Call(window,"ApplyLyricsPresentation");Check(Equals(fetch.Content,"Find lyrics"),"Fullscreen search button follows the application language");L.Apply("tr");
   Call(window,"ExitFullscreenPlayer");window.UpdateLayout();Check(!card.PlainPresentation&&Field<StackPanel>(window,"LyricsHeader").IsVisible&&Equals(fetch.Content,"İnternette bul"),"Leaving fullscreen restores the regular lyrics panel and search label");
   var audio=Field<AudioEngine>(window,"audio");Check(!audio.DeviceStarted&&audio.OutputStartCount==0,"All lyric transitions and searches are verified without opening any audio output");
  }
  finally{window?.Close();File.WriteAllText(Path.Combine(directory,"fullscreen-lyrics-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
