using System.IO;
using System.Text.Json;

namespace Ahenk;
public static class PlayerJson
{
 public static T? Read<T>(string path){try{return JsonSerializer.Deserialize<T>(File.ReadAllText(path));}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){return default;}}
 public static void Write<T>(string path,T value)
 {
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllText(temporary,JsonSerializer.Serialize(value));File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
}
public sealed class PlayerPreferences
{
 public double Volume{get;set;}=1;
 public bool Muted{get;set;}
 public bool AutoPlayOnStartup{get;set;}
 public static PlayerPreferences Load(string path){var p=PlayerJson.Read<PlayerPreferences>(path)??new();p.Volume=double.IsFinite(p.Volume)?Math.Clamp(p.Volume,0,1):1;return p;}
}
public sealed class PlayerSession
{
 public int Version{get;set;}=1;
 public List<string> Queue{get;set;}=[];
 public int QueueIndex{get;set;}=-1;
 public string? TrackPath{get;set;}
 public double Seconds{get;set;}
 public string View{get;set;}="albums";
 public string AlbumKey{get;set;}="";
 public string ArtistName{get;set;}="";
 public string PlaylistId{get;set;}="";
 public string Search{get;set;}="";
 public string FavoritesTab{get;set;}="tracks";
 public string? SelectedPath{get;set;}
 public double ScrollOffset{get;set;}
 public double HorizontalOffset{get;set;}
 public double PanelOffset{get;set;}
 public bool Shuffle{get;set;}
 public RepeatMode Repeat{get;set;}
 public PlaybackOrderState? Order{get;set;}public int SmartViewIndex{get;set;}
 public static PlayerSession? Load(string path){var s=PlayerJson.Read<PlayerSession>(path);return s?.Version==1&&s.Queue!=null?s:null;}
 public (List<Track> Queue,int Index,Track? Current,double Seconds) Resolve(IEnumerable<Track> library,Func<string,bool>? exists=null)
 {
  exists??=File.Exists;var lookup=library.GroupBy(t=>t.Path,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
  Track? Find(string? path)=>path!=null&&lookup.TryGetValue(path,out var t)&&exists(t.AudioPath)?t:null;
  var queue=new List<Track>();int index=-1;
  for(int i=0;i<Queue.Count;i++)if(Find(Queue[i]) is {} t){if(i==QueueIndex)index=queue.Count;queue.Add(t.Copy());}
  var current=Find(TrackPath);
  if(current==null||index<0||!string.Equals(queue[index].Path,current.Path,StringComparison.OrdinalIgnoreCase))index=current==null?-1:queue.FindIndex(t=>string.Equals(t.Path,current.Path,StringComparison.OrdinalIgnoreCase));
  double seconds=current!=null&&double.IsFinite(Seconds)?Math.Clamp(Seconds,0,Math.Max(0,current.Seconds-.05)):0;
  return(queue,index,current,seconds);
 }
}
