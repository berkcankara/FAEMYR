using System.IO;
namespace Ahenk;
public partial class MainWindow
{
 static async Task<List<Track>> ReadLibraryFile(string path,CancellationToken ct)
 {
  if(System.IO.Path.GetExtension(path).Equals(".cue",StringComparison.OrdinalIgnoreCase))return await CueSheet.Read(path,ct);
  return [await MetadataReader.Read(path,ct)];
 }
 void ApplyImportedTracks(string input,List<Track> tracks)
 {
  if(tracks.Count==0)return;
  if(tracks[0].IsCue)
  {
   var ids=tracks.Select(t=>t.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);foreach(var old in library.Where(t=>t.CueSheetPath.Equals(input,StringComparison.OrdinalIgnoreCase)&&!ids.Contains(t.Path)).ToArray()){library.Remove(old);store.Delete(old.Path);}
   foreach(string source in tracks.Select(t=>t.AudioPath).Distinct(StringComparer.OrdinalIgnoreCase))
   {
    bool referenced=favorites.IsTrackFavorite(source)||playlists.Items.Any(p=>p.Tracks.Contains(source,StringComparer.OrdinalIgnoreCase))||queue.Any(t=>t.Path.Equals(source,StringComparison.OrdinalIgnoreCase))||now?.Path.Equals(source,StringComparison.OrdinalIgnoreCase)==true;
    if(!referenced)foreach(var old in library.Where(t=>!t.IsCue&&t.Path.Equals(source,StringComparison.OrdinalIgnoreCase)).ToArray()){library.Remove(old);store.Delete(old.Path);}
   }
  }
  foreach(var track in tracks)
  {
   var old=library.FirstOrDefault(t=>t.Path.Equals(track.Path,StringComparison.OrdinalIgnoreCase));if(old!=null){if(old.AddedAt>DateTimeOffset.UnixEpoch)track.AddedAt=old.AddedAt;library.Remove(old);}
   ApplyTrackPreferences(track);library.Add(track);store.Save(track);listening.Get(track);waveforms?.Enqueue(track,true);
  }
  var changed=AlbumGrouping.Apply(library);if(changed.Count>0){store.SaveMany(changed);favorites.MigrateAlbumEditions(library);}
 }
}
