using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Ahenk;
public sealed class FavoritesState
{
 public HashSet<string> Tracks{get;set;}=new(StringComparer.OrdinalIgnoreCase);
 public HashSet<string> Albums{get;set;}=new(StringComparer.Ordinal);
 public Dictionary<string,int> Ratings{get;set;}=new(StringComparer.OrdinalIgnoreCase);
 public FavoritesState Copy()=>new(){Tracks=new(Tracks,StringComparer.OrdinalIgnoreCase),Albums=new(Albums,StringComparer.Ordinal),Ratings=new(Ratings,StringComparer.OrdinalIgnoreCase)};
}
public sealed class FavoriteStore
{
 readonly string path;FavoritesState state=new();
 public string LoadError{get;}="";
 public FavoriteStore(string directory)
 {
  path=Path.Combine(directory,"favorites.json");if(!File.Exists(path))return;
  try
  {
   var loaded=JsonSerializer.Deserialize<FavoritesState>(File.ReadAllText(path))??new();
   state.Tracks=new((loaded.Tracks??[]).Where(p=>!string.IsNullOrWhiteSpace(p)),StringComparer.OrdinalIgnoreCase);
   state.Albums=new((loaded.Albums??[]).Where(p=>!string.IsNullOrWhiteSpace(p)),StringComparer.Ordinal);
   foreach(var pair in loaded.Ratings??[])if(!string.IsNullOrWhiteSpace(pair.Key)&&pair.Value is >=1 and <=5)state.Ratings[pair.Key]=pair.Value;
  }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){LoadError=ex.Message;}
 }
 public bool IsTrackFavorite(string path)=>state.Tracks.Contains(path);
 public bool IsAlbumFavorite(string key)=>state.Albums.Contains(key);
 public int Rating(string path)=>state.Ratings.GetValueOrDefault(path);
 public void Apply(Track track){track.IsFavorite=IsTrackFavorite(track.Path);track.Rating=Rating(track.Path);}
 void Commit(FavoritesState next)
 {
  if(LoadError.Length>0)throw new InvalidDataException("Favoriler dosyası okunamadığı için üzerine yazılmadı. Dosya: "+path+"\n"+LoadError);
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllText(temp,JsonSerializer.Serialize(next,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,path,true);state=next;}
  finally{if(File.Exists(temp))File.Delete(temp);}
 }
 public void SetTrackFavorite(string path,bool value){var next=state.Copy();if(value)next.Tracks.Add(path);else next.Tracks.Remove(path);Commit(next);}
 public void Relocate(IReadOnlyDictionary<string,string> mapping){var next=state.Copy();foreach(var pair in mapping){if(next.Tracks.Remove(pair.Key))next.Tracks.Add(pair.Value);if(next.Ratings.Remove(pair.Key,out int stars))next.Ratings[pair.Value]=stars;}Commit(next);}
 public void MigrateAlbumEditions(IEnumerable<Track> tracks){var next=state.Copy();bool changed=false;var migrated=new HashSet<string>();foreach(var track in tracks){string old=track.AlbumArtist+"\u001f"+track.Album;if(track.AlbumEditionKey.Length>0&&next.Albums.Contains(old)){changed|=next.Albums.Add(track.AlbumKey);migrated.Add(old);}}foreach(string old in migrated)changed|=next.Albums.Remove(old);if(changed)Commit(next);}
 public void SetAlbumFavorite(string key,bool value){var next=state.Copy();if(value)next.Albums.Add(key);else next.Albums.Remove(key);Commit(next);}
 public void SetRating(string path,int rating)
 {
  if(rating is <0 or >5)throw new ArgumentOutOfRangeException(nameof(rating));
  var next=state.Copy();if(rating==0)next.Ratings.Remove(path);else next.Ratings[path]=rating;Commit(next);
 }
 public static IEnumerable<Track> FilterRating(IEnumerable<Track> tracks,int? stars)=>stars.HasValue?tracks.Where(t=>t.Rating==stars.Value):tracks;
}
public sealed record RatingFilterChoice(int? Stars,string Text){public string Label=>L.T(Text);}

public partial class MainWindow
{
 FavoriteStore favorites=null!;string favoritesTab="tracks";
 void InitializeFavorites()
 {
  RatingFilter.ItemsSource=new[]{new RatingFilterChoice(null,"Tüm yıldızlar"),new(0,"Yıldızsız")}.Concat(Enumerable.Range(1,5).Select(n=>new RatingFilterChoice(n,new string('★',n)+$" · {n}/5"))).ToArray();RatingFilter.SelectedIndex=0;
  AddHandler(StarRating.RatingRequestedEvent,new RoutedEventHandler((sender,e)=>
  {
   if(e is RatingRequestedEventArgs rating&&e.OriginalSource is StarRating{Tag:Track track}){e.Handled=true;RateTrack(track,rating.Rating);}
  }));
 }
 void Favorites_Click(object sender,RoutedEventArgs e){view="favorites";RefreshView();}
 void FavoriteTracks_Click(object sender,RoutedEventArgs e){favoritesTab="tracks";RefreshView();}
 void FavoriteAlbums_Click(object sender,RoutedEventArgs e){favoritesTab="albums";RefreshView();}
 void RatingFilter_Changed(object sender,SelectionChangedEventArgs e){if(libraryReady)RefreshView();}
 void ApplyTrackPreferences(Track track)=>favorites?.Apply(track);
 bool SaveFavoriteAction(Action action)
 {
  try{action();return true;}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){MessageBox.Show(this,"Favori veya yıldız kaydedilemedi.\n"+ex.Message,"Lyra",MessageBoxButton.OK,MessageBoxImage.Information);return false;}
 }
 void SyncTrackPreferences(string path)
 {
  foreach(var track in library.Concat(queue).Concat(now==null?[]:new[]{now}).Distinct().Where(t=>t.Path.Equals(path,StringComparison.OrdinalIgnoreCase)))favorites.Apply(track);
 }
 void ToggleTrackFavorite(Track track)
 {
  if(!SaveFavoriteAction(()=>favorites.SetTrackFavorite(track.Path,!favorites.IsTrackFavorite(track.Path))))return;
  SyncTrackPreferences(track.Path);if(view=="favorites")RefreshView();
 }
 void FavoriteTrack_Click(object sender,RoutedEventArgs e){if(sender is Button{Tag:Track track}){e.Handled=true;ToggleTrackFavorite(track);}}
 void RateTrack(Track track,int rating)
 {
  if(!SaveFavoriteAction(()=>favorites.SetRating(track.Path,rating)))return;
  SyncTrackPreferences(track.Path);if(view=="tracks"&&(RatingFilter.SelectedItem as RatingFilterChoice)?.Stars!=null)RefreshView();
 }
 void AlbumCard_ContextMenuOpening(object sender,ContextMenuEventArgs e)
 {
  if(sender is not FrameworkElement{DataContext:AlbumCard card,ContextMenu:{} menu})return;
  menu.Items.Clear();var favorite=new MenuItem{Header=L.T(favorites.IsAlbumFavorite(card.Key)?"Favorilerden çıkar":"Favorile")};
  favorite.Click+=(_,_)=>{if(SaveFavoriteAction(()=>favorites.SetAlbumFavorite(card.Key,!favorites.IsAlbumFavorite(card.Key))))RefreshView();};menu.Items.Add(favorite);
  var enqueue=new MenuItem{Header="Sıraya ekle"};enqueue.Click+=(_,_)=>EnqueueTracks(library.Where(t=>t.AlbumKey==card.Key).OrderBy(t=>t.Disc).ThenBy(t=>t.Number).ThenBy(t=>t.Title,StringComparer.CurrentCultureIgnoreCase),false);menu.Items.Add(enqueue);
 }
 void FavoriteCurrentAlbum_Click(object sender,RoutedEventArgs e)
 {
  if(view!="album")return;e.Handled=true;
  if(SaveFavoriteAction(()=>favorites.SetAlbumFavorite(albumKey,!favorites.IsAlbumFavorite(albumKey))))RefreshView();
 }
 void UpdateFavoriteControls()
 {
  FavoriteTabs.Visibility=Visible(view=="favorites");SectionTitle.Visibility=Visible(view is not "album" and not "playlist" and not "favorites" and not "tracks");RatingFilterTools.Visibility=Visible(view=="tracks");
  foreach(var button in new[]{FavoriteTracksTab,FavoriteAlbumsTab})
  {
   bool active=button==(favoritesTab=="tracks"?FavoriteTracksTab:FavoriteAlbumsTab);
   button.SetResourceReference(Control.BackgroundProperty,active?"Accent":"ControlSurface");button.SetResourceReference(Control.ForegroundProperty,active?"OnAccent":"Ink");
  }
  FavoriteAlbumButton.Visibility=Visible(view=="album");
  AlbumQualityBadge.Visibility=Visible(view=="album"&&InterfaceSettings.Current.ShowAlbumQuality);
  AlbumFavoriteIcon.Icon=favorites.IsAlbumFavorite(albumKey)?"heart-filled":"heart";
  FavoriteAlbumButton.ToolTip=favorites.IsAlbumFavorite(albumKey)?"Albümü favorilerden çıkar":"Albümü favorilere ekle";
 }
 void ShowFavoritesEmpty(string title,string hint)
 {
  CollectionEmpty.Visibility=Visibility.Visible;EmptyPlaylistCreate.Visibility=Visibility.Collapsed;CollectionEmptyTitle.Text=title;CollectionEmptyHint.Text=hint;
 }
}

