using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Faemyr;
public partial class MainWindow
{
 PlaylistStore playlists=null!;string playlistId="",artistName="",nextVisual="";
 LocalPlaylist? CurrentPlaylist=>playlists.Items.FirstOrDefault(p=>p.Id==playlistId);
 void InitializeCollections()
 {
  try{playlists=new PlaylistStore(Paths.Data);}catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){playlists=new PlaylistStore(Path.Combine(Paths.Data,"recovery"));Status.Text=L.T("Çalma listeleri okunamadı. Mevcut dosya korundu: ")+ex.Message;}
  Tracks.GroupStyle.Add((GroupStyle)FindResource("AlbumDiscGroupStyle"));VirtualizingPanel.SetIsVirtualizingWhenGrouping(Tracks,true);
  Tracks.ContextMenu=new ContextMenu();Tracks.ContextMenuOpening+=(_,e)=>{if(e.CursorLeft>=0&&e.OriginalSource is DependencyObject source&&ItemsControl.ContainerFromElement(Tracks,source) is not DataGridRow){e.Handled=true;return;}BuildTrackMenu();};
  Tracks.PreviewMouseRightButtonDown+=(_,e)=>{if(e.OriginalSource is DependencyObject source&&ItemsControl.ContainerFromElement(Tracks,source) is DataGridRow row&&!row.IsSelected)Tracks.SelectedItem=row.Item;};
  Seek.LostMouseCapture+=(_,_)=>{if(dragging){CommitSeek();dragging=false;}};
  Seek.PreviewMouseMove+=(_,e)=>{if(dragging&&e.LeftButton==System.Windows.Input.MouseButtonState.Pressed){MoveSeek(e.GetPosition(Seek).X);e.Handled=true;}};
  Seek.PreviewKeyUp+=(_,e)=>{if(e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right or System.Windows.Input.Key.Home or System.Windows.Input.Key.End)CommitSeek();};
 }
 List<AlbumCard> AlbumCards(IEnumerable<Track> tracks)
 {
  var names=ArtistCatalog.ProfileNames(library);
  return tracks.GroupBy(t=>t.AlbumKey).Select(g=>new AlbumCard{Key=g.Key,Title=g.First().Album+(g.First().EditionLabel.Length>0?" · "+g.First().EditionLabel:""),Artist=g.First().AlbumArtist,NavigableArtists=names,IsFavorite=favorites.IsAlbumFavorite(g.Key),Cover=g.Select(t=>t.Cover).FirstOrDefault(File.Exists),Count=g.Count(),Year=AlbumSorting.Year(g)}).ToList();
 }
 void Artists_Click(object s,RoutedEventArgs e){view="artists";RefreshView();}
 void Artist_Click(object s,RoutedEventArgs e)=>OpenArtist((string)((Button)s).Tag);
 void Playlists_Click(object s,RoutedEventArgs e){view="playlists";RefreshView();}
 void Playlist_Click(object s,RoutedEventArgs e){playlistId=(string)((Button)s).Tag;view="playlist";Search.Text="";RefreshView();}
 void NewPlaylist_Click(object s,RoutedEventArgs e)=>EditPlaylist(null);
 void EditPlaylist_Click(object s,RoutedEventArgs e){if(CurrentPlaylist is {} p){if(p.SmartQuery!=null)EditSmartPlaylist(p);else EditPlaylist(p);}}
 void DeletePlaylist_Click(object s,RoutedEventArgs e)
 {
  if(CurrentPlaylist is not {} p)return;
  if(MessageBox.Show(this,L.T($"“{p.Name}” çalma listesi silinsin mi? Müzik dosyaların korunur."),L.T("Çalma listesini sil"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
  playlists.Items.Remove(p);SavePlaylists();view="playlists";RefreshView();
 }
 void SavePlaylists(){try{playlists.Save();}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){MessageBox.Show(this,L.T("Çalma listesi kaydedilemedi: ")+ex.Message,"Faemyr");}}
 void BuildTrackMenu()
 {
  var menu=Tracks.ContextMenu;menu.Items.Clear();if(Tracks.SelectedItem is not Track t)return;
  var favorite=new MenuItem{Header=favorites.IsTrackFavorite(t.Path)?L.T("Favorilerden çıkar"):L.T("Favorilere ekle")};favorite.Click+=(_,_)=>ToggleTrackFavorite(t);menu.Items.Add(favorite);
  var ratingMenu=new MenuItem{Header=L.T("Yıldız ver")};menu.Items.Add(ratingMenu);
  for(int stars=1;stars<=5;stars++){int value=stars;var item=new MenuItem{Header=(favorites.Rating(t.Path)==stars?"✓  ":"    ")+new string('★',stars)+$" · {stars}/5"};item.Click+=(_,_)=>RateTrack(t,value);ratingMenu.Items.Add(item);}
  var noRating=new MenuItem{Header=L.T("Yıldızı kaldır"),IsEnabled=favorites.Rating(t.Path)>0};noRating.Click+=(_,_)=>RateTrack(t,0);ratingMenu.Items.Add(noRating);
  var favoriteAlbum=new MenuItem{Header=favorites.IsAlbumFavorite(t.AlbumKey)?L.T("Albümü favorilerden çıkar"):L.T("Albümü favorilere ekle")};favoriteAlbum.Click+=(_,_)=>{if(SaveFavoriteAction(()=>favorites.SetAlbumFavorite(t.AlbumKey,!favorites.IsAlbumFavorite(t.AlbumKey))))RefreshView();};menu.Items.Add(favoriteAlbum);menu.Items.Add(new Separator());
  var selected=SelectedTracks();var enqueue=new MenuItem{Header=selected.Count>1?L.T($"Seçili {selected.Count} parçayı sıraya ekle"):L.T("Sıraya ekle")};enqueue.Click+=(_,_)=>EnqueueTracks(selected,false);menu.Items.Add(enqueue);var playNext=new MenuItem{Header=L.T("Hemen sonra çal")};playNext.Click+=(_,_)=>EnqueueTracks(selected,true);menu.Items.Add(playNext);if(view=="queue"){var removeQueue=new MenuItem{Header=L.T("Seçilenleri kuyruktan çıkar")};removeQueue.Click+=(_,_)=>RemoveQueueSelection();menu.Items.Add(removeQueue);var undo=new MenuItem{Header=L.T("Kuyruk değişikliğini geri al · Ctrl+Z"),IsEnabled=queueUndo.Count>0};undo.Click+=(_,_)=>UndoQueue();menu.Items.Add(undo);}
  if(view is "album" or "playlist"){var all=new MenuItem{Header=view=="album"?L.T("Albümün tamamını sıraya ekle"):L.T("Listenin tamamını sıraya ekle")};all.Click+=(_,_)=>AlbumEnqueue_Click(this,new RoutedEventArgs());menu.Items.Add(all);}
  AddGainMenu(menu,t);var add=new MenuItem{Header=L.T("Çalma listesine ekle")};menu.Items.Add(add);
  foreach(var playlist in playlists.Items.Where(p=>p.SmartQuery==null).OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase)){var item=new MenuItem{Header=playlist.Name};item.Click+=(_,_)=>{try{playlists.Add(playlist,SelectedTracks());RefreshView();}catch(IOException ex){Status.Text=ex.Message;}};add.Items.Add(item);}
  var create=new MenuItem{Header=L.T("＋ Yeni çalma listesi…")};create.Click+=(_,_)=>EditPlaylist(null,t);add.Items.Add(create);
  if(view=="queue"){var clear=new MenuItem{Header=L.T("Kuyruğu temizle")};clear.Click+=(_,_)=>ClearQueue_Click(this,new RoutedEventArgs());menu.Items.Add(clear);}
  if(view=="playlist"&&CurrentPlaylist is {SmartQuery:null} current)
  {
   var up=new MenuItem{Header=L.T("Listede yukarı taşı"),IsEnabled=current.Tracks.IndexOf(t.Path)>0};up.Click+=(_,_)=>{PlaylistStore.Move(current,t.Path,-1);SavePlaylists();RefreshView();};menu.Items.Add(up);
   var down=new MenuItem{Header=L.T("Listede aşağı taşı"),IsEnabled=current.Tracks.IndexOf(t.Path)<current.Tracks.Count-1};down.Click+=(_,_)=>{PlaylistStore.Move(current,t.Path,1);SavePlaylists();RefreshView();};menu.Items.Add(down);
   var remove=new MenuItem{Header=L.T("Bu listeden çıkar")};remove.Click+=(_,_)=>{current.Tracks.RemoveAll(p=>p.Equals(t.Path,StringComparison.OrdinalIgnoreCase));SavePlaylists();RefreshView();};menu.Items.Add(remove);
  }
 }
 static BitmapImage? CoverImage(string? path,int width=720)
 {
  if(!File.Exists(path))return null;try{var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=width;image.UriSource=new Uri(path);image.EndInit();image.Freeze();return image;}catch(Exception ex)when(ex is IOException or NotSupportedException or ArgumentException or FormatException){return null;}
 }
 void EditPlaylist(LocalPlaylist? existing,Track? initial=null)
 {
  var draft=existing?.Copy()??new LocalPlaylist{Name=""};
  var dialog=new Window{Title=existing==null?L.T("Çalma listesi oluştur"):L.T("Çalma listesini düzenle"),Owner=this,Width=550,Height=650,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};dialog.SetResourceReference(BackgroundProperty,"Canvas");dialog.SetResourceReference(ForegroundProperty,"Ink");
  dialog.DataContext=draft;var root=new Grid{Margin=new Thickness(28)};root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});dialog.Content=root;
  root.Children.Add(new TextBlock{Text=dialog.Title,FontSize=25,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,20)});
  var content=new StackPanel();Grid.SetRow(content,1);root.Children.Add(content);
  content.Children.Add(BuildPlaylistCoverEditor(draft,dialog));
  content.Children.Add(new TextBlock{Text=L.T("Liste adı"),Margin=new Thickness(0,0,0,7)});var name=new TextBox{Text=draft.Name,MaxLength=100,Margin=new Thickness(0,0,0,18)};System.Windows.Automation.AutomationProperties.SetName(name,L.T("Liste adı"));content.Children.Add(name);
  content.Children.Add(new TextBlock{Text=L.T("Açıklama"),Margin=new Thickness(0,0,0,7)});var description=new TextBox{Text=draft.Description,MaxLength=1000,Height=95,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};content.Children.Add(description);
  var hint=new TextBlock{Text=L.T("Parçalara sağ tıklayıp “Çalma listesine ekle” menüsünü kullanabilirsin."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,18,0,0),FontSize=12};hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");content.Children.Add(hint);
  var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};Grid.SetRow(footer,2);root.Children.Add(footer);footer.Children.Add(new Button{Content=L.T("Vazgeç"),IsCancel=true});var save=new Button{Content=existing==null?L.T("Oluştur"):L.T("Kaydet"),IsDefault=true,Margin=new Thickness(0)};save.SetResourceReference(Control.BackgroundProperty,"Accent");save.SetResourceReference(Control.ForegroundProperty,"OnAccent");footer.Children.Add(save);
  save.Click+=(_,_)=>{
   if(string.IsNullOrWhiteSpace(name.Text)){name.Focus();return;}draft.Name=name.Text.Trim();draft.Description=description.Text.Trim();
   try{
    if(initial!=null&&!draft.Tracks.Contains(initial.Path,StringComparer.OrdinalIgnoreCase))draft.Tracks.Add(initial.Path);
    SavePlaylistDraft(draft,existing);
    playlistId=draft.Id;view="playlist";Search.Text="";RefreshView();dialog.DialogResult=true;
   }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or NotSupportedException){MessageBox.Show(dialog,L.T("Liste kaydedilemedi: ")+ex.Message,"Faemyr");}
  };
  dialog.Loaded+=(_,_)=>name.Focus();dialog.ShowDialog();
 }
 void RenderArtist(IEnumerable<Track> songs)
 {
  var tracks=songs.ToList();ArtistName.Text=artistName;ArtistSummary.Text=L.T($"{tracks.Select(t=>t.AlbumKey).Distinct().Count()} albüm · {tracks.Count} parça");
  var image=CoverImage(tracks.Select(t=>t.Cover).FirstOrDefault(File.Exists));ArtistBackdrop.Source=image;ArtistBackdrop.Opacity=.45;
  LoadArtistPortrait(artistName,tracks.Select(t=>t.Cover).FirstOrDefault(File.Exists));
  LoadArtistBiography(artistName);
 }
 void RenderPlaylist()
 {
  if(CurrentPlaylist is not {} p)return;var songs=PlaylistStore.Resolve(p,library,listening);HeroKind.Text=L.T("ÇALMA LİSTESİ");AlbumName.Text=p.Name;AlbumSummary.Names=AlbumSummary.Suffix="";AlbumSummary.Text=(p.Description.Length>0?p.Description+"\n":"")+L.T($"{songs.Count} parça · {(int)TimeSpan.FromSeconds(songs.Sum(t=>t.Seconds)).TotalMinutes} dk.")+(songs.Count<p.Tracks.Count?L.T($" · {p.Tracks.Count-songs.Count} parça arşivde bulunamadı"):"");
  AlbumArtwork.Source=CoverImage(p.Cover);HeroFallback.Visibility=AlbumArtwork.Source==null?Visibility.Visible:Visibility.Collapsed;AlbumCoverBox.Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(p.Color));
  renderedAlbum="";
 }
 void UpdateNextTrack()
 {
  int i=now!=null&&queueIndex>=0&&queueIndex<queue.Count&&queue[queueIndex].Path.Equals(now.Path,StringComparison.OrdinalIgnoreCase)?order.Peek(queue.Count,queueIndex,true):-1;
  var next=i>=0?queue[i]:null;string key=(next?.Path??"")+"|"+(order.Repeat==RepeatMode.One);
  if(nextVisual==key)return;nextVisual=key;NextTrackCard.Visibility=next!=null&&InterfaceSettings.Current.ShowNextTrack?Visibility.Visible:Visibility.Collapsed;
  if(next==null)return;NextTrackHeading.Text=order.Repeat==RepeatMode.One?L.T("TEKRAR ÇALACAK"):L.T("SIRADAKİ");NextTrackTitle.Text=next.Title;NextTrackArtist.Names=next.Artist;NextTrackCover.Source=CoverImage(next.Cover,160);
 }
 void CommitSeek(){if(now==null)return;if(audio.IsPlaying||audio.IsPaused)audio.Position=TimeSpan.FromSeconds(Math.Clamp(Seek.Value,0,audio.Duration.TotalSeconds));else if(resumePrepared)resumeSeconds=Math.Clamp(Seek.Value,0,PlayerDuration.TotalSeconds);}
 void MoveSeek(double x){Seek.Value=Seek.Minimum+Math.Clamp((x-5)/Math.Max(1,Seek.ActualWidth-10),0,1)*(Seek.Maximum-Seek.Minimum);}
}

