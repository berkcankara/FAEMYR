using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;
namespace Ahenk;
public static class UpgradeUiTests
{
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
 static void Set(MainWindow window,string name,object value)=>typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,value);
 static object? Call(MainWindow window,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args);
 static async Task Wait(Func<bool> condition){for(int i=0;i<600;i++){if(condition())return;await Task.Delay(10);}throw new Exception("Upgrade UI timeout");}
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Themes.Load();MainWindow? window=null;
  try
  {
   string source=Path.Combine(directory,"silent-cue-source.wav");var format=new WaveFormat(44100,16,2);using(var writer=new WaveFileWriter(source,format))writer.Write(new byte[format.AverageBytesPerSecond*5],0,format.AverageBytesPerSecond*5);
   string cuePath=Path.Combine(directory,"ui-album.cue");await File.WriteAllTextAsync(cuePath,"TITLE \"UI CUE Album\"\nPERFORMER \"UI Artist\"\nFILE \"silent-cue-source.wav\" WAVE\nTRACK 01 AUDIO\nTITLE \"One\"\nINDEX 01 00:00:00\nTRACK 02 AUDIO\nTITLE \"Two\"\nINDEX 01 00:02:00\n");var cue=await CueSheet.Read(cuePath,default);
   string regularFile=Path.Combine(directory,"regular.wav");File.Copy(source,regularFile,true);var regular=await MetadataReader.Read(regularFile,default);regular.Title="Regular";regular.Artist=regular.AlbumArtist="UI Artist";regular.Album="Regular album";
   using(var db=new LibraryStore())db.SaveMany(cue.Concat([regular]));new AudioPreferences{Exclusive=false}.Save(Path.Combine(Paths.Data,"audio-settings.json"));PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{Volume=0});
   window=new MainWindow{Opacity=0,ShowInTaskbar=false};window.Show();await Wait(()=>Field<bool>(window,"libraryReady")&&Field<bool>(window,"sessionReady"));
   var tracks=Field<DataGrid>(window,"Tracks");Set(window,"view","tracks");Call(window,"RefreshView");Check(tracks.SelectionMode==DataGridSelectionMode.Extended&&tracks.Items.Count==3,"All-tracks view supports multi-selection and CUE virtual rows");
   var rows=tracks.Items.OfType<Track>().ToArray();tracks.SelectedItems.Add(rows[0]);tracks.SelectedItems.Add(rows[1]);Check(((List<Track>)Call(window,"SelectedTracks")!).Count==2,"Multi-selection retains both tracks for batch actions");
   Call(window,"EnqueueTracks",new Track[]{rows[0],rows[1]},false);var queue=Field<List<Track>>(window,"queue");Check(queue.Count==2,"Batch enqueue adds both selected tracks");Call(window,"EnqueueTracks",new Track[]{rows[2]},true);Check(queue.Count==3&&queue[0].Path==rows[2].Path,"Play-next inserts before the idle queue head");Call(window,"UndoQueue");Check(queue.Count==2&&queue.Select(t=>t.Path).SequenceEqual(rows.Take(2).Select(t=>t.Path)),"Undo restores prior queue order after play-next");
   var playlists=Field<PlaylistStore>(window,"playlists");var history=Field<ListeningStore>(window,"listening");var smart=new LocalPlaylist{Name="Unplayed",SmartQuery="plays:0"};playlists.Items.Add(smart);playlists.Save();Set(window,"playlistId",smart.Id);Set(window,"view","playlist");Call(window,"RefreshView");Check(tracks.Items.Count==3,"Smart playlist renders matching tracks in the real playlist view");history.Add(rows[0],31,true);Call(window,"RefreshView");Check(tracks.Items.Count==2,"Real smart playlist updates after play count changes");
   Set(window,"view","tracks");Field<TextBox>(window,"Search").Text="format:wav plays:0";Call(window,"RefreshView");Check(tracks.Items.Count==2,"Advanced search integrates with all-tracks view");Field<TextBox>(window,"Search").Text="";
   var favorites=Field<FavoriteStore>(window,"favorites");var liveRegular=Field<List<Track>>(window,"library").First(t=>t.Path==regular.Path);favorites.SetTrackFavorite(regular.Path,true);favorites.SetRating(regular.Path,4);history.Add(liveRegular,31,true);int previousCount=history.Get(liveRegular).PlayCount;var manual=new LocalPlaylist{Name="Relink test",Tracks=[regular.Path]};playlists.Items.Add(manual);playlists.Save();string replacement=Path.Combine(directory,"relocated.wav");File.Copy(regular.Path,replacement,true);byte[] before=await File.ReadAllBytesAsync(regular.Path);Call(window,"RelocateLibraryFile",regular.Path,replacement);
   Check(favorites.IsTrackFavorite(replacement)&&favorites.Rating(replacement)==4&&manual.Tracks.SequenceEqual(new[]{replacement})&&history.Get(liveRegular).PlayCount==previousCount,"Relinking preserves favorites, ratings, manual playlists and listening history");Check(before.SequenceEqual(await File.ReadAllBytesAsync(regular.Path)),"Relinking never rewrites or removes music data");
   var liveCue=Field<List<Track>>(window,"library").Where(t=>t.IsCue).OrderBy(t=>t.Number).ToArray();queue.Clear();queue.AddRange(liveCue);Set(window,"view","queue");Call(window,"RefreshView");Field<PlaybackOrder>(window,"order").Reset(2,-1);await (Task)Call(window,"StartFromView",liveCue[0])!;var audio=Field<AudioEngine>(window,"audio");await Wait(()=>audio.DeviceStarted);Check(Field<Track>(window,"now").Path==liveCue[0].Path&&Field<int>(window,"queueIndex")==0,"CUE playback begins on the selected virtual track");
   audio.Position=TimeSpan.FromSeconds(1.8);await Wait(()=>Field<Track>(window,"now").Path==liveCue[1].Path);Check(Field<int>(window,"queueIndex")==1&&audio.OutputStartCount==1&&audio.Position.TotalSeconds<1,"Audible gapless transition updates actual player, queue index and per-track position");
   audio.Toggle();await (Task)Call(window,"RebuildPlaybackPlan")!;Check(audio.IsPaused&&!audio.DeviceStarted,"Queue-plan rebuild preserves paused state without sending samples");
   Call(window,"SaveSession",true);var session=PlayerSession.Load(Path.Combine(Paths.Data,"session.json"));Check(session?.TrackPath==liveCue[1].Path&&session.QueueIndex==1&&session.Queue.Count==2,"Session checkpoint records the audible CUE track and queue");
   window.Close();window=null;window=new MainWindow{Opacity=0,ShowInTaskbar=false};window.Show();await Wait(()=>Field<bool>(window,"sessionReady"));audio=Field<AudioEngine>(window,"audio");Check(Field<Track>(window,"now").Path==liveCue[1].Path&&!audio.DeviceStarted,"CUE session restores silently using the physical source path");
   Set(window,"view","playlist");Set(window,"playlistId",smart.Id);Call(window,"RefreshView");Check(Field<DataGrid>(window,"Tracks").Items.Count==2,"Listening history and smart rules survive application restart");
   window.Close();window=null;
  }
  finally{window?.Close();File.WriteAllText(Path.Combine(directory,"upgrade-ui-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
