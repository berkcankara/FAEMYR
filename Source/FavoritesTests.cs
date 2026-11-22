using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Faemyr;
public static class FavoritesTests
{
 public static void Run(string directory,Action<bool,string> check)
 {
  string root=Path.Combine(directory,"favorites-tests");Directory.CreateDirectory(root);
  var store=new FavoriteStore(root);string music=Path.Combine(root,"untouched.flac");File.WriteAllBytes(music,[1,2,3,4,5]);var original=File.ReadAllBytes(music);
  var a=new Track{Path=music,Title="A",Album="Album",AlbumArtist="Artist"};var b=new Track{Path=Path.Combine(root,"b.flac"),Title="B",Album=a.Album,AlbumArtist=a.AlbumArtist};
  check(!store.IsTrackFavorite(a.Path)&&!store.IsAlbumFavorite(a.AlbumKey)&&store.Rating(a.Path)==0,"A new collection has no favorites or ratings");
  store.SetAlbumFavorite(a.AlbumKey,true);check(store.IsAlbumFavorite(a.AlbumKey)&&!store.IsTrackFavorite(a.Path)&&!store.IsTrackFavorite(b.Path),"Album favorites are independent of track favorites");
  store.SetTrackFavorite(a.Path,true);store.SetRating(a.Path,4);
  var reloaded=new FavoriteStore(root);check(reloaded.IsTrackFavorite(a.Path.ToUpperInvariant())&&reloaded.IsAlbumFavorite(a.AlbumKey)&&reloaded.Rating(a.Path.ToUpperInvariant())==4,"Track favorites, album favorites and ratings persist with case-insensitive Windows paths");
  reloaded.Apply(a);reloaded.Apply(b);check(a.IsFavorite&&a.Rating==4&&!b.IsFavorite&&b.Rating==0,"Collection preferences attach to tracks without favoriting an entire album's songs");
  using(var serialized=JsonDocument.Parse(JsonSerializer.Serialize(a)))check(!serialized.RootElement.TryGetProperty("IsFavorite",out _)&&!serialized.RootElement.TryGetProperty("Rating",out _),"Favorites and visual ratings are excluded from music metadata serialization");
  check(File.ReadAllBytes(music).SequenceEqual(original),"Favoriting and rating never edit the underlying music file");
  reloaded.SetTrackFavorite(a.Path.ToUpperInvariant(),false);check(!reloaded.IsTrackFavorite(a.Path)&&reloaded.Rating(a.Path)==4,"Removing a track favorite preserves its independent star rating");
  reloaded.SetAlbumFavorite(a.AlbumKey,false);check(!reloaded.IsAlbumFavorite(a.AlbumKey)&&reloaded.Rating(a.Path)==4,"Removing an album favorite preserves track ratings");
  var replaced=new Track{Path=a.Path.ToUpperInvariant()};reloaded.Apply(replaced);check(replaced.Rating==4,"A rescanned or replaced track instance restores its saved stars");
  var c=new Track{Path=Path.Combine(root,"c.flac"),Rating=5};a.Rating=4;b.Rating=0;Track[] tracks=[a,b,c];
  check(FavoriteStore.FilterRating(tracks,4).SequenceEqual(new[]{a})&&FavoriteStore.FilterRating(tracks,5).SequenceEqual(new[]{c}),"Exact star filtering selects the requested score without sorting the library");
  check(FavoriteStore.FilterRating(tracks,0).SequenceEqual(new[]{b})&&FavoriteStore.FilterRating(tracks,null).SequenceEqual(tracks),"Unrated and all-star filters remain distinct and preserve track order");
  reloaded.SetRating(a.Path,0);check(new FavoriteStore(root).Rating(a.Path)==0,"Clearing stars persists as an unrated track");
  bool rejected=false;try{reloaded.SetRating(a.Path,6);}catch(ArgumentOutOfRangeException){rejected=true;}check(rejected&&reloaded.Rating(a.Path)==0,"Ratings outside zero through five cannot overwrite saved values");
  var host=new StackPanel();var indicator=new StarRating{Value=2};host.Children.Add(indicator);int requested=-1;host.AddHandler(StarRating.RatingRequestedEvent,new RoutedEventHandler((_,e)=>requested=((RatingRequestedEventArgs)e).Rating));indicator.RaiseEvent(new RatingRequestedEventArgs(5));
  check(requested==5&&indicator.Value==2,"Star input requests a value through its owner without breaking the track binding");
  check(StarRating.RatingAt(0,100)==1&&StarRating.RatingAt(45,100)==3&&StarRating.RatingAt(99,100)==5,"Five visual star positions map to the requested score");
  string brokenRoot=Path.Combine(root,"corrupt");Directory.CreateDirectory(brokenRoot);string broken=Path.Combine(brokenRoot,"favorites.json");File.WriteAllText(broken,"broken");var corrupt=new FavoriteStore(brokenRoot);bool protectedFile=false;try{corrupt.SetTrackFavorite(a.Path,true);}catch(InvalidDataException){protectedFile=true;}
  check(protectedFile&&File.ReadAllText(broken)=="broken","Corrupt favorites are preserved instead of silently overwritten");
  string failedRoot=Path.Combine(root,"failed-save");Directory.CreateDirectory(Path.Combine(failedRoot,"favorites.json"));var failed=new FavoriteStore(failedRoot);bool failedSave=false;try{failed.SetRating(a.Path,5);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){failedSave=true;}
  check(failedSave&&failed.Rating(a.Path)==0&&!Directory.EnumerateFiles(failedRoot,"*.tmp").Any(),"A failed atomic save leaves in-memory ratings unchanged and cleans its temporary file");
 }
}
