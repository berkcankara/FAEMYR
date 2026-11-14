using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;
namespace Ahenk;
public static class LibrarySyncTests
{
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
 static void Set(MainWindow window,string name,object value)=>typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,value);
 static object? Call(MainWindow window,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args);
 static async Task Wait(Func<bool> ready){for(int i=0;i<1500;i++){if(ready())return;await Task.Delay(10);}throw new Exception("Library sync timeout");}
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<object>();void Check(bool value,string name){checks.Add(new{name,passed=value});if(!value)throw new Exception(name);}
  Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Themes.Load();MainWindow? window=null;
  try
  {
   string root=Path.Combine(directory,"music");Directory.CreateDirectory(root);
   Track Make(string path){Directory.CreateDirectory(Path.GetDirectoryName(path)!);var format=new WaveFormat(44100,16,2);using(var writer=new WaveFileWriter(path,format))writer.Write(new byte[format.AverageBytesPerSecond],0,format.AverageBytesPerSecond);return new(){Path=path,Title=Path.GetFileNameWithoutExtension(path),Artist="Artist",AlbumArtist="Artist",Album=Path.GetFileNameWithoutExtension(path),Seconds=1,Codec="pcm_s16le",Bits=16,SampleRate=44100,Channels=2,Modified=File.GetLastWriteTimeUtc(path).Ticks};}
   var live=Make(Path.Combine(root,"live.wav"));var deleted=Make(Path.Combine(root,"deleted.wav"));File.Delete(deleted.Path);
   var sub=Make(Path.Combine(root,"album","sub.wav"));File.Delete(sub.Path);Directory.Delete(Path.GetDirectoryName(sub.Path)!);
   var offline=live.Copy();offline.Path=Path.Combine(directory,"disconnected","keep.wav");
   var other=live.Copy();other.Path=Path.Combine(root+"-other","keep.wav");
   string cueFile=Path.Combine(root,"album.cue");File.WriteAllText(cueFile,"CUE fixture");var cue=live.Copy();cue.Path=cueFile+"#track=1";cue.CueSheetPath=cueFile;cue.SourcePath=live.Path;
   Check(LibraryPresence.FindMissing([live,deleted,sub,offline,other,cue],[root,Path.GetDirectoryName(offline.Path)!]).Select(t=>t.Path).ToHashSet().SetEquals([deleted.Path,sub.Path]),"Detect missing files and deleted folders, retain CUE virtual rows, disconnected roots and unrelated prefix paths");
   int reads=0;var repeated=Enumerable.Range(0,100).Select(i=>{var t=deleted.Copy();t.Path=Path.Combine(root,"missing-"+i+".wav");return t;});LibraryPresence.FindMissing(repeated,[root],listDirectory:p=>{reads++;return Directory.GetFileSystemEntries(p);});Check(reads==1,"One directory listing serves 100 files without decoding music");
   string blocked=Path.Combine(root,"blocked");Directory.CreateDirectory(blocked);var inaccessible=live.Copy();inaccessible.Path=Path.Combine(blocked,"keep.wav");
   Check(LibraryPresence.FindMissing([inaccessible],[root],listDirectory:p=>p==blocked?throw new UnauthorizedAccessException():Directory.GetFileSystemEntries(p)).Count==0,"Unreadable subdirectories retain their records");
   Check(LibraryPresence.FindMissing([deleted],[root],listDirectory:_=>throw new IOException("Drive offline")).Count==0,"A failing root never removes tracks");
   using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool cancelled=false;try{LibraryPresence.FindMissing([deleted],[root],cancel.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"Cancellation aborts missing-file checks");}
   File.Delete(cueFile);Check(LibraryPresence.FindMissing([cue],[root]).Count==1,"Deleted CUE sheet removes virtual tracks even when its audio remains");File.WriteAllText(cueFile,"CUE fixture");
   var cueMissing=cue.Copy();cueMissing.SourcePath=deleted.Path;Check(LibraryPresence.FindMissing([cueMissing],[root]).Count==1,"Deleted CUE audio removes virtual tracks even when its sheet remains");
   var wv=Make(Path.Combine(root,"hybrid.wv"));Check(LibraryPresence.FindMissing([wv],[root]).Count==0,"Absent optional WavPack correction sidecar does not remove an existing track");
   var watched=Make(Path.Combine(root,"watched.wav"));var nested=Make(Path.Combine(root,"nested","nested.wav"));var scanned=Make(Path.Combine(root,"scanned.wav"));
   using(var db=new LibraryStore()){db.AddRoot(root);db.AddRoot(Path.GetDirectoryName(offline.Path)!);db.SaveMany([live,deleted,sub,offline,watched,nested,scanned,cue]);}
   Environment.SetEnvironmentVariable("LYRA_ARTIST_PHOTOS_OFFLINE","1");window=new MainWindow{Opacity=0,ShowInTaskbar=false};window.Show();
   await Wait(()=>Field<bool>(window,"libraryReady")&&!Field<List<Track>>(window,"library").Any(t=>t.Path==deleted.Path));
   var library=Field<List<Track>>(window,"library");Check(!library.Any(t=>t.Path==sub.Path)&&library.Any(t=>t.Path==offline.Path)&&library.Any(t=>t.Path==cue.Path),"Startup reconciles stale DB records and preserves unavailable drives and valid CUE tracks");
   var favorites=Field<FavoriteStore>(window,"favorites");favorites.SetTrackFavorite(watched.Path,true);favorites.SetRating(watched.Path,5);var playlists=Field<PlaylistStore>(window,"playlists");var playlist=new LocalPlaylist{Name="Keep references",Tracks=[watched.Path]};playlists.Items.Add(playlist);playlists.Save();
   Call(window,"EnqueueTracks",new Track[]{library.Single(t=>t.Path==watched.Path),library.Single(t=>t.Path==live.Path)},false);
   File.Delete(watched.Path);await Wait(()=>!library.Any(t=>t.Path==watched.Path));Check(Field<List<Track>>(window,"queue").Select(t=>t.Path).SequenceEqual([live.Path]),"Live file deletion removes its queue rows while preserving remaining order");
   Check(favorites.IsTrackFavorite(watched.Path)&&favorites.Rating(watched.Path)==5&&playlist.Tracks.Contains(watched.Path),"Favorites, ratings and saved playlist references survive deletion for a later reimport");
   File.Delete(nested.Path);Directory.Delete(Path.GetDirectoryName(nested.Path)!);await Wait(()=>!library.Any(t=>t.Path==nested.Path));Check(!library.Any(t=>t.Path==nested.Path),"Deleting an entire album folder removes its rows automatically");
   File.Delete(cueFile);await Wait(()=>!library.Any(t=>t.Path==cue.Path));Check(library.Any(t=>t.Path==live.Path),"Deleting a CUE sheet keeps the independent physical audio row");
   Call(window,"StopFolderWatching");File.Delete(scanned.Path);await (Task)Call(window,"Scan",(object)new string[]{root})!;Check(!library.Any(t=>t.Path==scanned.Path),"Manual rescan removes a deletion missed by the watcher");
   Set(window,"view","tracks");Call(window,"RefreshView");Check(!Field<DataGrid>(window,"Tracks").Items.OfType<Track>().Any(t=>new[]{watched.Path,scanned.Path,deleted.Path,sub.Path,nested.Path,cue.Path}.Contains(t.Path)),"The actual collection view no longer renders removed records");
   using(var db=new LibraryStore())Check(!db.Load().Any(t=>new[]{watched.Path,scanned.Path,deleted.Path,sub.Path,nested.Path,cue.Path}.Contains(t.Path)),"Removed records stay removed when the database is reopened");
   Call(window,"EnqueueTracks",new Track[]{live},false);Set(window,"now",live.Copy());Set(window,"resumePrepared",true);Call(window,"ShowTrackDetails",live);File.Delete(live.Path);
   await (Task)Call(window,"ReconcileLibraryAsync",new object[]{null!,CancellationToken.None})!;
   Check(Field<Track?>(window,"now")==null&&!Field<bool>(window,"resumePrepared")&&Field<List<Track>>(window,"queue").Count==0,"Deleting the restored current track clears its pending playback and queue safely");
   Check(Field<TextBlock>(window,"DetailTitle").Text==L.T("Henüz çalan parça yok")&&Field<ItemsControl>(window,"Metadata").Items.Count==0,"The player panel clears stale title and metadata when its file is removed");
   Check(!Field<AudioEngine>(window,"audio").DeviceStarted,"Library synchronization never starts an audio device");
   window.Close();window=null;
  }
  finally{window?.Close();File.WriteAllText(Path.Combine(directory,"library-sync-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
