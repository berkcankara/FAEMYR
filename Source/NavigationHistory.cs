using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Faemyr;
public partial class MainWindow
{
 sealed record NavigationPlace(string View,string Album,string Artist,string Playlist,string Tab,string Query)
 {
  public double Vertical,Horizontal;public string? Selection;
  public string Key=>View+"\n"+(View=="album"?Album:View=="artist"?Artist:View=="playlist"?Playlist:View=="favorites"?Tab:"");
 }
 readonly List<NavigationPlace> navigationHistory=[];
 NavigationPlace? navigationPlace;ScrollViewer? navigationScroll;bool navigatingBack;
 NavigationPlace CurrentNavigationPlace()=>new(view,albumKey,artistName,playlistId,favoritesTab,Search.Text);
 void RecordNavigation()
 {
  var next=CurrentNavigationPlace();
  if(navigationPlace!=null&&navigationPlace.Key!=next.Key&&!navigatingBack)
  {
   navigationPlace.Vertical=navigationScroll?.VerticalOffset??0;navigationPlace.Horizontal=navigationScroll?.HorizontalOffset??0;navigationPlace.Selection=(Tracks.SelectedItem as Track)?.Path;
   navigationHistory.Add(navigationPlace);if(navigationHistory.Count>100)navigationHistory.RemoveAt(0);
  }
  navigationPlace=next;UpdateTopBarState();
 }
 void CompleteNavigation()=>navigationScroll=CurrentScroll();
 void Back_Click(object sender,RoutedEventArgs e)=>NavigateBack();
 void NavigateBack()
 {
  if(!libraryReady||settingsWindow!=null||navigationHistory.Count==0)return;
  var previous=navigationHistory[^1];navigationHistory.RemoveAt(navigationHistory.Count-1);
  navigatingBack=true;
  try
  {
   view=previous.View;albumKey=previous.Album;artistName=previous.Artist;playlistId=previous.Playlist;favoritesTab=previous.Tab;
   if(view=="album"&&!library.Any(t=>t.AlbumKey==albumKey))view="albums";
   if(view=="artist"&&!ArtistCatalog.ProfileNames(library).Contains(artistName))view="artists";
   if(view=="playlist"&&CurrentPlaylist==null)view="playlists";
   Search.Text=previous.Query;searchDelay.Stop();RefreshView();
  }
  finally{navigatingBack=false;}
  string expected=navigationPlace?.Key??"";
  Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,()=>
  {
   if(closed||navigationPlace?.Key!=expected)return;
   var scroll=CurrentScroll();scroll?.ScrollToVerticalOffset(previous.Vertical);scroll?.ScrollToHorizontalOffset(previous.Horizontal);
   if(Tracks.ItemsSource is IEnumerable<Track> rows)Tracks.SelectedItem=rows.FirstOrDefault(t=>t.Path==previous.Selection);
  });
  UpdateTopBarState();
 }
}
