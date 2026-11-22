using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;

namespace Faemyr;
public enum AlbumSortMode { Artist, Title, YearNewest, TrackCount }
public sealed record AlbumSortChoice(AlbumSortMode Mode,string Text){public string Label=>L.T(Text);}
public enum ArtistSortMode { NameAscending, NameDescending }
public sealed record ArtistSortChoice(ArtistSortMode Mode,string Text){public string Label=>L.T(Text);}
public sealed class AlbumSortSettings
{
 public AlbumSortMode Mode {get;set;}=AlbumSortMode.Artist;
 public ArtistSortMode ArtistMode {get;set;}=ArtistSortMode.NameAscending;
 public bool GroupByLetter {get;set;}=true;
 public static AlbumSortSettings Load(string path){try{var value=JsonSerializer.Deserialize<AlbumSortSettings>(File.ReadAllText(path))??new();if(!Enum.IsDefined(value.Mode))value.Mode=AlbumSortMode.Artist;if(!Enum.IsDefined(value.ArtistMode))value.ArtistMode=ArtistSortMode.NameAscending;return value;}catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){return new();}}
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
  ArtistSort.ItemsSource=new[]{new ArtistSortChoice(ArtistSortMode.NameAscending,"Sanatçı adı · A–Z"),new ArtistSortChoice(ArtistSortMode.NameDescending,"Sanatçı adı · Z–A")};
  AlbumSort.SelectedValue=albumSortSettings.Mode;ArtistSort.SelectedValue=albumSortSettings.ArtistMode;albumSortReady=true;UpdateGalleryGroupingButtons();
 }
 void AlbumSort_Changed(object sender,SelectionChangedEventArgs e)
 {
  if(!albumSortReady||AlbumSort.SelectedValue is not AlbumSortMode mode)return;
  albumSortSettings.Mode=mode;UpdateGalleryGroupingButtons();RefreshView();if(Albums.Items.Count>0)Albums.ScrollIntoView(Albums.Items[0]);
  SaveGallerySort();
 }
 void ArtistSort_Changed(object sender,SelectionChangedEventArgs e)
 {
  if(!albumSortReady||ArtistSort.SelectedValue is not ArtistSortMode mode)return;
  albumSortSettings.ArtistMode=mode;RefreshView();if(Artists.Items.Count>0)Artists.ScrollIntoView(Artists.Items[0]);SaveGallerySort();
 }
 void GalleryGrouping_Click(object sender,RoutedEventArgs e)
 {
  if(!albumSortReady||sender is not ToggleButton button||!button.IsEnabled)return;
  albumSortSettings.GroupByLetter=button.IsChecked==true;UpdateGalleryGroupingButtons();RefreshView();
  var gallery=view=="artists"?Artists:Albums;if(gallery.Items.Count>0)gallery.ScrollIntoView(gallery.Items[0]);SaveGallerySort();
 }
 void UpdateGalleryGroupingButtons()
 {
  bool alphabetical=albumSortSettings.Mode is AlbumSortMode.Artist or AlbumSortMode.Title;
  AlbumGroupingButton.IsEnabled=alphabetical;
  AlbumGroupingButton.IsChecked=alphabetical&&albumSortSettings.GroupByLetter;ArtistGroupingButton.IsChecked=albumSortSettings.GroupByLetter;
  foreach(var button in new[]{AlbumGroupingButton,ArtistGroupingButton})
  {
   AutomationProperties.SetName(button,L.T("Kategorili görünüm"));
   button.ToolTip=!button.IsEnabled?L.T("Kategorili görünüm yalnızca alfabetik sıralamada kullanılabilir."):button.IsChecked==true?L.T("Kategorili görünümü kapat"):L.T("Kategorili görünümü aç");
  }
 }
 void SaveGallerySort(){try{albumSortSettings.Save(AlbumSortSettingsPath);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Status.Text=L.T("Sıralama tercihi kaydedilemedi: ")+ex.Message;}}
}
