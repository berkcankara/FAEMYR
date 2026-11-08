using System.Windows.Controls;

namespace Ahenk;
public partial class MainWindow
{
 void UpdateNavigation()
 {
  var active=settingsWindow!=null?NavSettings:view switch{
   "artists" or "artist"=>NavArtists,"tracks"=>NavTracks,"favorites"=>NavFavorites,"playlists" or "playlist"=>NavPlaylists,
   "queue"=>NavQueue,_=>NavAlbums};
  foreach(var button in new[]{NavAlbums,NavArtists,NavTracks,NavFavorites,NavPlaylists,NavQueue,NavSettings})
  {
   button.SetResourceReference(Control.BackgroundProperty,button==active?"Accent":"ControlSurface");
   button.SetResourceReference(Control.ForegroundProperty,button==active?"OnAccent":"Ink");
  }
 }
}
