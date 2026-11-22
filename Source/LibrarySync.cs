using System.IO;
namespace Faemyr;

// An absent drive or unreadable directory is unknown, not evidence of deletion.
// Cache directory listings for this pass only; never open/decode music files.
public static class LibraryPresence
{
 public static List<Track> FindMissing(IEnumerable<Track> tracks,IEnumerable<string> roots,CancellationToken ct=default,Func<string,string[]>? listDirectory=null)
 {
  listDirectory??=Directory.GetFileSystemEntries;
  var listings=new Dictionary<string,HashSet<string>?>(StringComparer.OrdinalIgnoreCase);
  HashSet<string>? List(string directory)
  {
   ct.ThrowIfCancellationRequested();if(listings.TryGetValue(directory,out var saved))return saved;
   try{return listings[directory]=listDirectory(directory).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);}
   catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException){listings[directory]=null;return null;}
  }
  var available=roots.Select(Path.GetFullPath).Select(Path.TrimEndingDirectorySeparator).Distinct(StringComparer.OrdinalIgnoreCase).Where(root=>List(root)!=null).ToArray();
  bool Missing(string path)
  {
   if(string.IsNullOrWhiteSpace(path))return false;path=Path.GetFullPath(path);
   var root=available.Where(r=>path.StartsWith(Path.EndsInDirectorySeparator(r)?r:r+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).OrderByDescending(r=>r.Length).FirstOrDefault();
   if(root==null)return false;
   string parent=root;var parts=Path.GetRelativePath(root,path).Split(Path.DirectorySeparatorChar);
   foreach(string part in parts)
   {
    var entries=List(parent);if(entries==null)return false;string next=Path.Combine(parent,part);
    if(!entries.Contains(next))return true;parent=next;
   }
   return false;
  }
  return tracks.Where(t=>Missing(t.AudioPath)||(t.IsCue&&Missing(t.CueSheetPath))).ToList();
 }
}

public partial class MainWindow
{
 bool reconcilingLibrary,libraryReconcilePending;
 async Task ReconcileLibraryAsync(IEnumerable<string>? roots=null,CancellationToken ct=default)
 {
  if(closed||reconcilingLibrary)return;reconcilingLibrary=true;
  try
  {
   var snapshot=library.Select(t=>t.Copy()).ToArray();var folders=(roots??store.Roots()).ToArray();
   var missing=await Task.Run(()=>LibraryPresence.FindMissing(snapshot,folders,ct),ct);
   if(closed)return;ct.ThrowIfCancellationRequested();
   if(missing.Count==0)return;
   // A rename/import may have replaced a row while the background check ran.
   var currentRows=library.ToDictionary(t=>t.Path,StringComparer.OrdinalIgnoreCase);
   var keys=missing.Where(old=>currentRows.TryGetValue(old.Path,out var t)&&t.Modified==old.Modified&&t.AudioPath.Equals(old.AudioPath,StringComparison.OrdinalIgnoreCase)&&t.CueSheetPath.Equals(old.CueSheetPath,StringComparison.OrdinalIgnoreCase))
    .Select(t=>t.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
   if(keys.Count==0)return;
   store.DeleteMany(keys);library.RemoveAll(t=>keys.Contains(t.Path));
   if(now!=null&&keys.Contains(now.Path))
   {
    await RunDesktopAction(DesktopMediaAction.Stop);now=null;inspectedTrack=null;panelCoverPath="";
    DetailTitle.Text=L.T("Henüz çalan parça yok");DetailArtist.Names="";DetailQuality.Text="";Metadata.ItemsSource=null;DetailAlbumLink.IsEnabled=false;DetailAlbumLink.Tag=null;DetailAlbumLink.ToolTip=null;
    PanelCover.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty,"Surface");CoverFallback.Visibility=System.Windows.Visibility.Visible;
    lyricsCts?.Cancel();lyricsSelection++;lyricsDocument=null;RenderLyrics(null);UpdateLyricsVisibility();if(fullscreenPlayer)UpdateFullscreenLayout();
   }
   if(queue.Any(t=>keys.Contains(t.Path)))ChangeQueue(()=>queue.RemoveAll(t=>keys.Contains(t.Path)),remember:false);
   queueUndo.Clear();if(view=="album"&&!library.Any(t=>t.AlbumKey==albumKey))view="albums";
   if(view=="artist"&&!ArtistCatalog.ProfileNames(library).Contains(artistName))view="artists";
   // Retain favorites, ratings and saved playlist paths for a later reimport/relink.
   RefreshView();SaveSession(false);Status.Text=L.T($"{keys.Count} silinmiş parça arşivden kaldırıldı.");
  }
  catch(OperationCanceledException)when(ct.IsCancellationRequested){}
  catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException){if(!closed)Status.Text=L.T("Arşiv kontrol edilemedi: ")+ex.Message;}
  finally{reconcilingLibrary=false;}
 }
}
