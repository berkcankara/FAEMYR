using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Ahenk;
public enum AlbumSortMode { Artist, Title, YearNewest, TrackCount }
public sealed record AlbumSortChoice(AlbumSortMode Mode,string Text){public string Label=>L.T(Text);}
public sealed class AlbumSortSettings
{
 public AlbumSortMode Mode {get;set;}=AlbumSortMode.Artist;
 public static AlbumSortSettings Load(string path){try{var value=JsonSerializer.Deserialize<AlbumSortSettings>(File.ReadAllText(path))??new();if(!Enum.IsDefined(value.Mode))value.Mode=AlbumSortMode.Artist;return value;}catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){return new();}}
 public void Save(string path){Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);string temp=path+".tmp";try{File.WriteAllText(temp,JsonSerializer.Serialize(this));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
}
public static class AlbumSorting
{
 public static int Year(IEnumerable<Track> tracks)
 {
  foreach(var t in tracks){var dates=t.Fields.Select(f=>(Key:f.Key[(f.Key.LastIndexOf('/')+1)..].Trim().ToUpperInvariant(),f.Value)).Where(f=>f.Key is "DATE" or "YEAR" or "ORIGINALDATE" or "ORIGINALYEAR").OrderBy(f=>f.Key=="DATE"?0:f.Key=="YEAR"?1:2);
   foreach(var date in dates){string value=date.Value.Trim();if(value.Length>=4&&int.TryParse(value[..4],out int year)&&year>=1000&&year<=9999)return year;}
  }return 0;
 }
 public static List<AlbumCard> Sort(IEnumerable<AlbumCard> albums,AlbumSortMode mode)
 {
  var comparer=StringComparer.CurrentCultureIgnoreCase;
  IOrderedEnumerable<AlbumCard> sorted=mode switch{
   AlbumSortMode.Title=>albums.OrderBy(a=>a.Title,comparer).ThenBy(a=>a.Artist,comparer),
   AlbumSortMode.YearNewest=>albums.OrderByDescending(a=>a.Year).ThenBy(a=>a.Artist,comparer).ThenBy(a=>a.Title,comparer),
   AlbumSortMode.TrackCount=>albums.OrderByDescending(a=>a.Count).ThenBy(a=>a.Artist,comparer).ThenBy(a=>a.Title,comparer),
   _=>albums.OrderBy(a=>a.Artist,comparer).ThenBy(a=>a.Title,comparer)};
  return sorted.ThenBy(a=>a.Key,StringComparer.Ordinal).ToList();
 }
}
public partial class MainWindow
{
 AlbumSortSettings albumSortSettings=new();bool albumSortReady;
 string AlbumSortSettingsPath=>System.IO.Path.Combine(Paths.Data,"album-sort.json");
 void InitializeAlbumSort()
 {
  albumSortSettings=AlbumSortSettings.Load(AlbumSortSettingsPath);
  AlbumSort.ItemsSource=new[]{new AlbumSortChoice(AlbumSortMode.Artist,"Sanatçı · A–Z"),new AlbumSortChoice(AlbumSortMode.Title,"Albüm adı · A–Z"),new AlbumSortChoice(AlbumSortMode.YearNewest,"Yıl · yeni → eski"),new AlbumSortChoice(AlbumSortMode.TrackCount,"Parça sayısı · çoktan aza")};
  AlbumSort.SelectedValue=albumSortSettings.Mode;albumSortReady=true;
 }
 void AlbumSort_Changed(object sender,SelectionChangedEventArgs e)
 {
  if(!albumSortReady||AlbumSort.SelectedValue is not AlbumSortMode mode)return;
  albumSortSettings.Mode=mode;RefreshView();if(Albums.Items.Count>0)Albums.ScrollIntoView(Albums.Items[0]);
  try{albumSortSettings.Save(AlbumSortSettingsPath);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Status.Text="Sıralama tercihi kaydedilemedi: "+ex.Message;}
 }
}
