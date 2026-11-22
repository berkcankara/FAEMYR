using System.IO;
using System.Text.Json;

namespace Faemyr;
public sealed class LocalPlaylist
{
 public string Id {get;set;}=Guid.NewGuid().ToString("N");
 public string Name {get;set;}="Yeni çalma listesi";
 public string Description {get;set;}="";
 public string Cover {get;set;}="";
 public string Color {get;set;}="#53504B";
 public List<string> Tracks {get;set;}=[];
 public DateTime Created {get;set;}=DateTime.UtcNow;
 public string? SmartQuery{get;set;}public string SmartSort{get;set;}="library";
 public LocalPlaylist Copy()=>new(){Id=Id,Name=Name,Description=Description,Cover=Cover,Color=Color,Tracks=Tracks.ToList(),Created=Created,SmartQuery=SmartQuery,SmartSort=SmartSort,SmartCount=SmartCount};
 [System.Text.Json.Serialization.JsonIgnore]public int SmartCount{get;set;}
 public string Detail=>SmartQuery!=null?L.T($"Akıllı liste · {SmartCount} parça"):L.T($"{Tracks.Count} parça");
 public string Initial=>string.IsNullOrWhiteSpace(Name)?"♪":Name.Trim()[..1].ToUpperInvariant();
}
public sealed class PlaylistStore
{
 readonly string path;
 public List<LocalPlaylist> Items {get;private set;}=[];
 public PlaylistStore(string directory){path=Path.Combine(directory,"playlists.json");if(!File.Exists(path))return;Items=(JsonSerializer.Deserialize<List<LocalPlaylist>>(File.ReadAllText(path))??[]).Where(p=>p!=null).ToList();var ids=new HashSet<string>();foreach(var p in Items){if(!Guid.TryParse(p.Id,out _)||!ids.Add(p.Id)){p.Id=Guid.NewGuid().ToString("N");ids.Add(p.Id);}p.Name=string.IsNullOrWhiteSpace(p.Name)?"Adsız liste":p.Name.Trim();p.Description??="";p.Cover??="";if(p.Cover.Length>0){var relocated=Path.Combine(directory,"playlist-covers",Path.GetFileName(p.Cover));if(File.Exists(relocated))p.Cover=relocated;}if(!System.Text.RegularExpressions.Regex.IsMatch(p.Color??"",@"^#[0-9a-fA-F]{6}$"))p.Color="#53504B";p.Tracks=(p.Tracks??[]).Where(t=>!string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();}}
 public void Save(){Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+".tmp";try{File.WriteAllText(temp,JsonSerializer.Serialize(Items,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
 public void Add(LocalPlaylist p,IEnumerable<Track> tracks){var known=new HashSet<string>(p.Tracks,StringComparer.OrdinalIgnoreCase);foreach(var t in tracks)if(known.Add(t.Path))p.Tracks.Add(t.Path);Save();}
 public static List<Track> Resolve(LocalPlaylist p,IEnumerable<Track> library,ListeningStore? history=null){if(p.SmartQuery!=null){var match=TrackQuery.Compile(p.SmartQuery,history,out _);return TrackQuery.Sort(library.Where(match),p.SmartSort,history);}var lookup=library.ToDictionary(t=>t.Path,StringComparer.OrdinalIgnoreCase);return p.Tracks.Where(lookup.ContainsKey).Select(t=>lookup[t]).ToList();}
 public static void Move(LocalPlaylist p,string path,int delta){int i=p.Tracks.FindIndex(t=>t.Equals(path,StringComparison.OrdinalIgnoreCase)),j=i+delta;if(i<0||j<0||j>=p.Tracks.Count)return;(p.Tracks[i],p.Tracks[j])=(p.Tracks[j],p.Tracks[i]);}
}
public sealed class ArtistCard
{
 public string Name {get;init;}="";
 public string? Cover {get;init;}
 public int Albums {get;init;}
 public int Tracks {get;init;}
 public string Detail=>L.T($"{Albums} albüm · {Tracks} parça");
}
