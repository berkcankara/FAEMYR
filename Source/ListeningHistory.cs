using System.IO;
namespace Faemyr;
public sealed class ListeningStats
{
 public DateTimeOffset AddedAt{get;set;}=DateTimeOffset.UtcNow;public int PlayCount{get;set;}public DateTimeOffset? LastPlayed{get;set;}public double Seconds{get;set;}
}
public sealed class ListeningStore
{
 readonly string path;readonly Dictionary<string,ListeningStats> entries;bool dirty;
 public ListeningStore(string directory)
 {
  path=Path.Combine(directory,"listening-history.json");entries=new(StringComparer.OrdinalIgnoreCase);
  var saved=PlayerJson.Read<Dictionary<string,ListeningStats>>(path);if(saved==null)return;
  foreach(var (key,value) in saved)
   if(value!=null&&(!entries.TryGetValue(key,out var other)||Nullable.Compare(value.LastPlayed,other.LastPlayed)>0))entries[key]=value;
 }
 public ListeningStats Get(Track track)
 {
  if(!entries.TryGetValue(track.Path,out var value)){entries[track.Path]=value=new(){AddedAt=track.AddedAt> DateTimeOffset.UnixEpoch?track.AddedAt:DateTimeOffset.UtcNow};dirty=true;}return value;
 }
 public void Ensure(IEnumerable<Track> tracks){foreach(var track in tracks)Get(track);}
 public void Add(Track track,double seconds,bool counted){var entry=Get(track);entry.Seconds+=Math.Max(0,seconds);if(counted){entry.PlayCount++;entry.LastPlayed=DateTimeOffset.UtcNow;}dirty=true;}
 public void Relocate(string oldPath,string newPath){if(entries.Remove(oldPath,out var value)){entries[newPath]=value;dirty=true;}}
 public void Save(){if(!dirty)return;PlayerJson.Write(path,entries);dirty=false;}
}
public partial class MainWindow
{
 ListeningStore listening=null!;PlaybackPreferences playbackPreferences=new();Track? listeningTrack;double listened,lastListenPosition;bool listenCounted;DateTimeOffset lastHistorySave=DateTimeOffset.UtcNow;
 void InitializeListening(){playbackPreferences=PlayerJson.Read<PlaybackPreferences>(Path.Combine(Paths.Data,"playback-settings.json"))??new();}
 void BeginListening(Track track){listeningTrack=track;listened=0;listenCounted=false;lastListenPosition=audio.Position.TotalSeconds;listening.Get(track);}
 void UpdateListening()
 {
  if(listeningTrack!=null&&audio.IsPlaying){double pos=audio.Position.TotalSeconds,delta=pos-lastListenPosition;lastListenPosition=pos;if(delta>0&&delta<1.5){listened+=delta;bool counted=!listenCounted&&listened>=Math.Min(30,Math.Max(.05,listeningTrack.Seconds*.5));listening.Add(listeningTrack,delta,counted);listenCounted|=counted;}}
  if((DateTimeOffset.UtcNow-lastHistorySave).TotalSeconds>=30){SaveListening();lastHistorySave=DateTimeOffset.UtcNow;}
 }
 void SaveListening(){try{listening?.Save();}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Status.Text="Dinleme geçmişi kaydedilemedi: "+ex.Message;}}
}
