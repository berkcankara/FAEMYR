using System.IO;
using System.Text.RegularExpressions;
namespace Faemyr;
public static class AlbumGrouping
{
 static string Tag(Track t,params string[] names)=>t.Fields.FirstOrDefault(f=>names.Contains(ReplayGain.Tag(f.Key),StringComparer.OrdinalIgnoreCase))?.Value.Trim()??"";
 public static string Folder(Track track)
 {
  string folder=System.IO.Path.GetDirectoryName(track.AudioPath)??"";var leaf=System.IO.Path.GetFileName(folder);
  if(Regex.IsMatch(leaf,@"^(?:cd|disc|disk)[\s._-]*\d+(?:\s+of\s+\d+)?$",RegexOptions.IgnoreCase))folder=System.IO.Path.GetDirectoryName(folder)??folder;return folder;
 }
 public static List<Track> Apply(IEnumerable<Track> tracks)
 {
  var changed=new List<Track>();foreach(var album in tracks.GroupBy(t=>t.AlbumArtist+"\u001f"+t.Album))
  {
   var byFolder=album.GroupBy(Folder,StringComparer.OrdinalIgnoreCase).ToArray();var releases=byFolder.Select(folder=>new{Tracks=folder.ToArray(),Folder=folder.Key,Id=folder.Select(t=>Tag(t,"MUSICBRAINZALBUMID","MUSICBRAINZRELEASEID")).FirstOrDefault(s=>s.Length>0)??folder.Key}).GroupBy(g=>g.Id,StringComparer.OrdinalIgnoreCase).ToArray();
   if(releases.Length<=1)continue;
   foreach(var release in releases)
   {
    var songs=release.SelectMany(g=>g.Tracks).ToArray();string edition=songs.Select(t=>t.AlbumEditionKey).FirstOrDefault(s=>!string.IsNullOrEmpty(s))??Paths.Hash(release.Key)[..12];
    string label=Tag(songs[0],"EDITION","VERSION","CATALOGNUMBER");if(label.Length==0)label=System.IO.Path.GetFileName(release.First().Folder);int year=AlbumSorting.Year(songs);if(year>0&&!label.Contains(year.ToString()))label=year+" · "+label;
    foreach(var t in songs)if(t.AlbumEditionKey!=edition||t.EditionLabel!=label){t.AlbumEditionKey=edition;t.EditionLabel=label;changed.Add(t);}
   }
  }return changed;
 }
}
