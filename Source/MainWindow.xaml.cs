using System.IO;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Ahenk;
public sealed record DeviceOption(string? Id,string Name);
public partial class MainWindow : Window
{
 LibraryStore store=null!;readonly List<Track> library=[];readonly List<Track> queue=[];readonly AudioEngine audio=new();
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
 WaveformQueue? waveforms;
 string? visualPath;bool visualPlaying;
 readonly PlaybackOrder order=new();string renderedAlbum="";
 CancellationTokenSource? scanCts,playCts;Track? now;int queueIndex=-1;bool dragging,closed;string view="albums",albumKey="";
 readonly string cache=System.IO.Path.Combine(Paths.Data,"cache");
 public MainWindow()
 {
  InterfaceSettings.Load();InitializeComponent();Scope.Engine=audio;StartupMetrics.Mark("xaml");AddHandler(ArtistLinks.ArtistRequestedEvent,new RoutedEventHandler(ArtistLinkRequested));InitializeWindowFrame();
  waveforms=new WaveformQueue(System.IO.Path.Combine(Paths.Data,"waveforms"),(t,data,error)=>{if(closed)return;Dispatcher.BeginInvoke(DispatcherPriority.Background,()=>{if(closed||!library.Contains(t))return;t.Waveform=data;t.WaveformStatus=data!=null?L.T("Yerel analiz · krem: bas / turuncu: orta / mavi: tiz"):error;});});
  InitializeDetailPanel();InitializeLyrics();InitializeAlbumSort();InitializeCollections();InitializeArtistPhotos();InitializeArtistProfile();InitializeThemes();InitializeVisualizer();InitializeFavorites();InitializeListening();InitializeQueueManagement();InitializeSmartLists();
  DeviceChoice.ItemsSource=new[]{new DeviceOption(null,L.T("Windows varsayılan aygıtı"))};DeviceChoice.SelectedIndex=0;InitializeAudioFeatures();InitializePlayerControls();InitializeDesktopPlayback();InitializeDesktopPlayers();InitializeFullscreenPlayer();InitializeInterfaceCustomization();
  InitializeTopBar();WindowRoot.IsEnabled=false;EmptyState.Visibility=Visibility.Collapsed;LibraryStats.Text=L.T("Arşivin hazırlanıyor…");Loaded+=LoadLibrary;
  audio.Ended+=()=>Dispatcher.BeginInvoke(async ()=>{UpdateStreamTrack();await Advance(true);});
  audio.Failed+=message=>Dispatcher.BeginInvoke(()=>{resumePrepared=now!=null;resumeSeconds=audio.LastFailurePosition.TotalSeconds;Scope.Reset();SetSignal(message);Status.Text=message;});
  timer.Tick+=(_,_)=>{UpdateStreamTrack();UpdateListening();SyncPlaybackVisuals();UpdateDesktopPlayback();UpdateNextTrack();UpdateLyricsPlayback();UpdateVolumeControls();UpdateFullscreenAmbience();UpdateMiniPlayer();NotifyTrackChange();Scope.Running=IsVisible&&(!fullscreenPlayer||!fullscreenControlsIdle.Hidden)&&InterfaceSettings.Current.ShowVisualizer&&audio.IsPlaying&&audio.Scope!=null&&WindowState!=WindowState.Minimized;if(!dragging){Seek.Maximum=Math.Max(1,PlayerDuration.TotalSeconds);Seek.Value=PlayerPosition.TotalSeconds;}Elapsed.Text=FormatTime(PlayerPosition);TotalTime.Text=FormatTime(PlayerDuration);PlayIcon.Icon=audio.IsPlaying?"pause":"play";};timer.Start();
  order.Reset(queue.Count,queueIndex);TrackArea.SizeChanged+=(_,_)=>{UpdateHeroLayout();UpdateGlassBackdrop();};TrackGlass.SizeChanged+=(_,_)=>UpdateGlassBackdrop();SizeChanged+=(_,_)=>{UpdateHeroLayout();UpdateGlassBackdrop();};
  Closing+=(_,_)=>{deviceMonitor.Stop();SaveListening();ClosePlayerControls();closed=true;gainScan?.Cancel();CloseLyrics();SavePanelPreferences();StopFolderWatching();waveforms?.Dispose();bitrates?.Dispose();searchDelay.Stop();scanCts?.Cancel();playCts?.Cancel();timer.Stop();Scope.Reset();audio.Dispose();if(libraryReady)try{File.WriteAllText(System.IO.Path.Combine(Paths.Data,"queue.json"),JsonSerializer.Serialize(queue.Select(t=>t.Path)));}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){} _=Task.Run(()=>AudioEngine.TrimCache(cache));store?.Dispose();};
  ContentRendered+=(_,_)=>StartupMetrics.Mark("firstRender");StartupMetrics.Mark("constructorComplete");
 }
 async void RefreshDevices_Click(object s,RoutedEventArgs e)=>await RefreshDevicesAsync();
 static string FormatTime(TimeSpan t)=>t.ToString(t.TotalHours>=1?@"h\:mm\:ss":@"m\:ss");
 void SyncPlaybackVisuals(bool force=false)
 {
  var current=audio.IsPlaying||audio.IsPaused||resumePrepared?now:null;bool playing=audio.IsPlaying;
  if(!force&&visualPath==current?.Path&&visualPlaying==playing)return;visualPath=current?.Path;visualPlaying=playing;
  PlaybackVisuals.Update(library.Concat(queue).Distinct(),current,playing);
 }
 void UpdateGlassBackdrop()
 {
  if((view!="album"&&view!="playlist")||TrackGlass.ActualWidth<=0||TrackGlass.ActualHeight<=0)return;
  var origin=TrackGlass.TranslatePoint(new Point(),PaletteLayer);GlassBackdrop.Fill=new VisualBrush(PaletteLayer){ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(origin,new Size(TrackGlass.ActualWidth,TrackGlass.ActualHeight)),Stretch=Stretch.Fill};
 }
 void RefreshView()
 {
  if(Tracks==null||playlists==null||!libraryReady)return;RecordNavigation();UpdateNavigation();UpdateSearchPrompt();string query=Search.Text.Trim();IEnumerable<Track> filtered=library;
  var queryMatch=TrackQuery.Compile(query,listening,out string queryError);filtered=filtered.Where(queryMatch);SmartFilterError.Text=queryError;SmartFilterError.Visibility=Visible(queryError.Length>0);
  var list=filtered.ToList();bool favoriteAlbums=view=="favorites"&&favoritesTab=="albums",favoriteTracks=view=="favorites"&&!favoriteAlbums;bool gallery=view is "albums" or "artist"||favoriteAlbums,collection=view is "albums" or "artists" or "artist" or "playlists"||favoriteAlbums,hero=view is "album" or "playlist",profile=view=="artist";
  LibraryStats.Text=L.T($"{library.Count:N0} parça · {library.Select(t=>t.AlbumKey).Distinct().Count():N0} albüm · {TimeSpan.FromSeconds(library.Sum(t=>t.Seconds)).TotalHours:0.#} saat müzik");
  EmptyState.Visibility=Visible(library.Count==0&&view!="playlists"&&view!="playlist"&&view!="favorites");
  AlbumScroll.Visibility=Visible(gallery);ArtistScroll.Visibility=Visible(view=="artists");PlaylistScroll.Visibility=Visible(view=="playlists");Tracks.Visibility=Visible(!collection);
  AlbumSortTools.Visibility=Visible(gallery);PlaylistTools.Visibility=Visible(view=="playlist");NewPlaylistTools.Visibility=Visible(view=="playlists");
  TrackArea.Margin=collection?new Thickness(0):new Thickness(-30,0,-26,-18);
  AlbumHero.Visibility=AlbumActions.Visibility=Visible(hero);CollectionToolbar.Visibility=Visible(!hero);ArtistHero.Visibility=Visible(profile);PageHeader.Visibility=LibraryStats.Visibility=Visible(!hero&&!profile);SectionTitle.Visibility=Visible(!hero);
  ArtistColumn.Visibility=Visible(!hero);Tracks.RowHeight=hero?60:54;BitrateColumn.Visibility=WaveformColumn.Visibility=Visible(hero);
  PaletteLayer.Visibility=TrackGlass.Visibility=Visible(hero);CollectionEmpty.Visibility=Visibility.Collapsed;
  if(view=="album")RenderAlbum();else if(view=="playlist")RenderPlaylist();
  if(hero){Dispatcher.BeginInvoke(DispatcherPriority.Loaded,()=>{UpdateHeroLayout();UpdateGlassBackdrop();});}
  SyncPlaybackVisuals(true);
  if(gallery){var artistAlbums=profile?ArtistCatalog.AlbumKeys(library,artistName):null;var source=profile?list.Where(t=>artistAlbums!.Contains(t.AlbumKey)):list;if(favoriteAlbums)source=source.Where(t=>favorites.IsAlbumFavorite(t.AlbumKey));var cards=AlbumSorting.Sort(AlbumCards(source),albumSortSettings.Mode);Albums.ItemsSource=cards.Chunk(InterfaceSettings.Current.AlbumColumns).ToArray();PageTitle.Text=favoriteAlbums?L.T("Favoriler"):L.T("Albümlerin");if(favoriteAlbums&&cards.Count==0)ShowFavoritesEmpty(query.Length>0?L.T("Albüm bulunamadı."):L.T("Henüz favori albüm yok."),L.T("Albüme sağ tıklayıp Favorile seçeneğini kullanarak veya albümü açıp kalp düğmesiyle buraya ekleyebilirsin."));SectionTitle.Text=profile?L.T("Albümler"):query.Length>0?L.T("Arama sonuçları"):L.T("Albümler");if(profile)RenderArtist(library.Where(t=>ArtistCatalog.Matches(t,artistName)));}
  else if(view=="artists")
  {
   PageTitle.Text=L.T("Sanatçılar");SectionTitle.Text=L.T("Koleksiyonundaki sanatçılar");
   Artists.ItemsSource=ArtistCatalog.Cards(list).Chunk(InterfaceSettings.Current.ArtistColumns).ToArray();
  }
  else if(view=="playlists")
  {
   foreach(var smart in playlists.Items.Where(p=>p.SmartQuery!=null))smart.SmartCount=PlaylistStore.Resolve(smart,library,listening).Count;PageTitle.Text=L.T("Çalma listelerin");SectionTitle.Text=L.T("Senin seçkin");var matches=playlists.Items.Where(p=>query.Length==0||(p.Name+" "+p.Description).Contains(query,StringComparison.CurrentCultureIgnoreCase)).OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase).ToList();PlaylistCards.ItemsSource=matches.Chunk(InterfaceSettings.Current.PlaylistColumns).ToArray();
   if(matches.Count==0){CollectionEmpty.Visibility=Visibility.Visible;EmptyPlaylistCreate.Visibility=Visibility.Visible;CollectionEmptyTitle.Text=query.Length>0?L.T("Liste bulunamadı."):L.T("Kendi seçkini oluştur.");CollectionEmptyHint.Text=L.T("Sevdiğin parçaları bir araya getir, listene bir kapak seç.");}
  }
  else
  {
   IEnumerable<Track> result=view=="queue"?queue:view=="playlist"&&CurrentPlaylist is {} playlist?PlaylistStore.Resolve(playlist,library,listening):view=="album"?library.Where(t=>t.AlbumKey==albumKey).OrderBy(t=>t.Disc).ThenBy(t=>t.Number).ThenBy(t=>t.Title):library.OrderBy(t=>t.Artist).ThenBy(t=>t.Album).ThenBy(t=>t.Disc).ThenBy(t=>t.Number);
   if(query.Length>0)result=result.Where(queryMatch);if(view=="tracks"&&SmartView.SelectedItem is SmartPreset preset){result=result.Where(TrackQuery.Compile(preset.Query,listening,out _));result=TrackQuery.Sort(result,preset.Sort,listening);}
   if(favoriteTracks)result=result.Where(t=>favorites.IsTrackFavorite(t.Path));if(view=="tracks")result=FavoriteStore.FilterRating(result,(RatingFilter.SelectedItem as RatingFilterChoice)?.Stars);
   var selected=(Tracks.SelectedItem as Track)?.Path;var rows=result.ToList();Tracks.ItemsSource=rows;if(selected!=null)Tracks.SelectedItem=rows.FirstOrDefault(t=>t.Path==selected);
   if(hero)foreach(var t in rows){if(InterfaceSettings.Current.ShowWaveforms)waveforms?.Enqueue(t,true);bitrates?.Enqueue(t);}
   PageTitle.Text=favoriteTracks?L.T("Favoriler"):view=="queue"?L.T("Çalma kuyruğu"):view=="album"?library.FirstOrDefault(t=>t.AlbumKey==albumKey)?.Album??L.T("Albüm"):view=="playlist"?CurrentPlaylist?.Name??L.T("Çalma listesi"):L.T("Tüm parçalar");SectionTitle.Text=view=="queue"?L.T($"{queue.Count} parça sırada"):"";
   if(favoriteTracks&&rows.Count==0)ShowFavoritesEmpty(query.Length>0?L.T("Parça bulunamadı."):L.T("Henüz favori parça yok."),L.T("Bir parçanın yanındaki kalbe tıklayarak veya sağ tık menüsünden favorilerine ekleyebilirsin."));
   if(view=="tracks"&&rows.Count==0&&library.Count>0)ShowFavoritesEmpty(L.T("Eşleşen parça yok."),L.T("Aramanı veya yıldız filtresini değiştirebilirsin."));
   if(view=="playlist"&&rows.Count==0){CollectionEmpty.Visibility=Visibility.Visible;EmptyPlaylistCreate.Visibility=Visibility.Collapsed;CollectionEmptyTitle.Text=L.T("Bu liste müziğini bekliyor.");CollectionEmptyHint.Text=L.T("Albümlerde veya tüm parçalarda bir parçaya sağ tıkla; “Çalma listesine ekle” menüsünden bu listeyi seç.");}
  }
  if(view=="favorites")LibraryStats.Text=L.T($"{library.Count(t=>favorites.IsTrackFavorite(t.Path))} favori parça · {library.Where(t=>favorites.IsAlbumFavorite(t.AlbumKey)).Select(t=>t.AlbumKey).Distinct().Count()} favori albüm");
  UpdateFavoriteControls();UpdateNextTrack();UpdateQueueTools();UpdateSmartTools();UpdateArtistProfileHost();ApplyInterfaceView();CompleteNavigation();
 }
 void UpdateHeroLayout()
 {
  var ui=InterfaceSettings.Current;double width=MainContent.ActualWidth+MainContent.Margin.Left+MainContent.Margin.Right,cover=Math.Clamp(width*.32,206,ui.CoverMaximum);AlbumCoverBox.Width=AlbumCoverBox.Height=cover;AlbumHero.MinHeight=cover+52;AlbumCoverColumn.Width=new GridLength(cover+12);
  // Keep album actions below the text when the extra favorite button would squeeze it.
  bool compact=view=="album"&&width<850;
  double reserve=compact?(ui.AlbumButtonHeight*(ui.ShowAlbumQuality?2:1)+(ui.ShowAlbumQuality?10:0)+16):0;double heroHeight=compact?Math.Max(cover,Math.Max(286,150+reserve)):cover;AlbumHero.MinHeight=heroHeight+52;
  AlbumCoverBox.VerticalAlignment=compact?VerticalAlignment.Center:VerticalAlignment.Bottom;
  Grid.SetColumn(HeroTools,compact?1:2);HeroTools.Margin=new Thickness(compact?22:20,0,0,0);
  HeroText.Margin=new Thickness(22,0,0,reserve);HeroText.Height=heroHeight-reserve;
  bool narrow=width<700;
  NumberColumn.Width=narrow?36:45;FormatColumn.Width=narrow?55:75;DurationColumn.Width=narrow?60:70;
  BitrateColumn.Width=narrow?80:92;WaveformColumn.Width=Math.Clamp(width*.17,85,ui.WaveformMaximum);
 } void Search_Changed(object s,TextChangedEventArgs e){if(IsInitialized){searchDelay.Stop();searchDelay.Start();}}
 void Albums_Click(object s,RoutedEventArgs e){view="albums";RefreshView();}
 void Tracks_Click(object s,RoutedEventArgs e){view="tracks";RefreshView();}
 void Queue_Click(object s,RoutedEventArgs e){view="queue";RefreshView();}
 void Album_Click(object s,RoutedEventArgs e){albumKey=(string)((Button)s).Tag;view="album";RefreshView();}
 async void AddFolder_Click(object s,RoutedEventArgs e){if(scanCts!=null)return;var dialog=new OpenFolderDialog{Title=L.T("Müzik arşivinin klasörünü seç"),Multiselect=false};if(dialog.ShowDialog(settingsWindow??this)==true){store.AddRoot(dialog.FolderName);RefreshLibraryRoots();RegisterFolderWatchers();await Scan([dialog.FolderName]);}}
 async void Rescan_Click(object s,RoutedEventArgs e){if(scanCts!=null)return;var roots=store.Roots();if(roots.Count==0){AddFolder_Click(s,e);return;}await Scan(roots);}
 void CancelScan_Click(object s,RoutedEventArgs e)=>scanCts?.Cancel();
 static readonly HashSet<string> Extensions=new(StringComparer.OrdinalIgnoreCase){".cue",".flac",".wv",".wav",".wave",".aiff",".aif",".aifc",".mp3",".m4a",".alac",".dsf",".dff",".ogg",".opus",".ape"};
 static IEnumerable<string> Walk(string root,Action<string> warning)
 {
  var stack=new Stack<string>();stack.Push(root);while(stack.Count>0){var dir=stack.Pop();string[] files,dirs;try{files=Directory.GetFiles(dir);dirs=Directory.GetDirectories(dir);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){warning(dir+": "+ex.Message);continue;}foreach(var f in files)if(Extensions.Contains(System.IO.Path.GetExtension(f)))yield return f;foreach(var d in dirs){try{if((File.GetAttributes(d)&FileAttributes.ReparsePoint)==0)stack.Push(d);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){warning(ex.Message);}}}
 }
 async Task Scan(IEnumerable<string> roots)
 {
  roots=roots.ToArray();
  scanCts=new();var token=scanCts.Token;UpdateLibraryScanState();var errors=new List<string>();int done=0,added=0;
  try
  {
   var files=await Task.Run(()=>roots.SelectMany(r=>Walk(r,errors.Add)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),token);
   var lookup=library.ToDictionary(t=>t.Path,StringComparer.OrdinalIgnoreCase);var covered=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var path in files.OrderBy(p=>p.EndsWith(".cue",StringComparison.OrdinalIgnoreCase)?0:1)){token.ThrowIfCancellationRequested();if(covered.Contains(path))continue;done++;Status.Text=L.T($"Taranıyor {done:N0}/{files.Count:N0} · {System.IO.Path.GetFileName(path)}");
    try{var modified=File.GetLastWriteTimeUtc(path).Ticks;if(!path.EndsWith(".cue",StringComparison.OrdinalIgnoreCase)&&lookup.TryGetValue(path,out var old)&&old.Modified==modified)continue;var tracks=await Task.Run(()=>ReadLibraryFile(path,token),token);if(closed)return;ApplyImportedTracks(path,tracks);foreach(var track in tracks){lookup[track.Path]=track;if(track.IsCue)covered.Add(track.AudioPath);}added+=tracks.Count;if(added%12==0)RefreshView();}
    catch(OperationCanceledException){throw;}catch(Exception ex){errors.Add(path+": "+ex.Message);}
   }
   await ReconcileLibraryAsync(roots,token);
   token.ThrowIfCancellationRequested();
   Status.Text=L.T($"Tarama tamamlandı · {added} yeni/güncel parça · {errors.Count} uyarı");
  }catch(OperationCanceledException){if(!closed)Status.Text=L.T($"Tarama durduruldu · {added} parça kaydedildi.");}catch(Exception ex){if(!closed)Status.Text=L.T("Tarama hatası: ")+ex.Message;}
  finally{scanCts.Dispose();scanCts=null;if(!closed){if(errors.Count>0)File.WriteAllLines(System.IO.Path.Combine(Paths.Data,"scan-errors.txt"),errors);UpdateLibraryScanState();RefreshView();}}
 }
 void Track_Selected(object s,SelectionChangedEventArgs e)
 {
  // Row selection is independent of the player panel; only successful playback changes it.
 }
 void Raw_Click(object s,RoutedEventArgs e)
 {
  var t=now??inspectedTrack;if(t==null){Status.Text=L.T("Önce bir parça oynat.");return;}
  var tabs=new TabControl();tabs.Items.Add(new TabItem{Header=L.T("FFprobe / tüm teknik veriler"),Content=new TextBox{Text=t.ProbeJson,IsReadOnly=true,AcceptsReturn=true,FontFamily=new("Consolas"),FontSize=12,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto}});
  var fields=new StackPanel{Margin=new Thickness(16)};fields.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"Ink");
  foreach(var field in t.Fields)
  {
   var label=new TextBlock{Text=field.Key,FontSize=11,Margin=new Thickness(0,0,0,5)};label.SetResourceReference(TextBlock.ForegroundProperty,"Muted");fields.Children.Add(label);
   if(field.IsArtist)fields.Children.Add(new ArtistLinks{Names=field.Value,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18)});
   else fields.Children.Add(new TextBox{Text=field.Value,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18)});
  }
  var tagScroll=new ScrollViewer{Content=fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};tagScroll.SetResourceReference(Control.BackgroundProperty,"Canvas");tabs.Items.Add(new TabItem{Header=L.T("Etiketler / tekrarlanan değerler"),Content=tagScroll});
  tabs.Items.Add(new TabItem{Header=L.T("Etiketler / ham metin"),Content=new TextBox{Text=string.Join("\r\n\r\n",t.Fields.Select(f=>f.Key+"\r\n"+f.Value)),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
  var window=new Window{Title=t.Title+" — metadata",Width=860,Height=650,Owner=this,Content=tabs,WindowStartupLocation=WindowStartupLocation.CenterOwner};window.AddHandler(ArtistLinks.ArtistRequestedEvent,new RoutedEventHandler(ArtistLinkRequested));window.Show();
 }
 async void Track_DoubleClick(object s,MouseButtonEventArgs e){if(IsArtistLinkSource(e.OriginalSource))return;if(Tracks.SelectedItem is Track t)await StartFromView(t);}
 async void RowPlay_Click(object sender,RoutedEventArgs e)
 {
  e.Handled=true;if(sender is not FrameworkElement{DataContext:Track track})return;Tracks.SelectedItem=track;
  if(now!=null&&string.Equals(now.Path,track.Path,StringComparison.OrdinalIgnoreCase)&&(audio.IsPlaying||audio.IsPaused||resumePrepared))await TogglePlayback();
  else await StartFromView(track);
  SyncPlaybackVisuals();UpdateDesktopPlayback();
 }
 async void Track_KeyDown(object s,KeyEventArgs e){if(IsArtistLinkSource(e.OriginalSource))return;if(e.Key==Key.Enter&&Tracks.SelectedItem is Track t){await StartFromView(t);e.Handled=true;}else if(e.Key==Key.Delete&&view=="queue"){RemoveQueueSelection();e.Handled=true;}}
 async Task StartFromView(Track t)
 {
  if(view!="queue"){RememberQueue();queue.Clear();queue.AddRange(Tracks.Items.OfType<Track>());}queueIndex=queue.IndexOf(t);order.Reset(queue.Count,queueIndex);await PlayTrack(t);
 }
 async Task PlayTrack(Track t,TimeSpan? startPosition=null,bool startPaused=false,bool continueListening=false)
 {
  playCts?.Cancel();var request=new CancellationTokenSource();playCts=request;if(audio.IsPlaying||audio.IsPaused){resumeSeconds=audio.Position.TotalSeconds;resumePrepared=now!=null;}audio.Stop();Scope.Reset();SyncPlaybackVisuals();var device=(DeviceChoice.SelectedItem as DeviceOption)?.Id;var preferences=audioPreferences.Copy();bool exclusive=ModeChoice.SelectedIndex==1;
  try{Status.Text=L.T("Ses çözümleniyor · ")+t.Title;SetSignal(L.T("Decoder çalışıyor…"));
   await Task.Yield();request.Token.ThrowIfCancellationRequested();
   string? wav=null;
   if(t.IsDsd){preferences.RequireDop(device);request.Token.ThrowIfCancellationRequested();audio.PlayDop(t.Path,device,preferences,startPosition);}
   else{request.Token.ThrowIfCancellationRequested();if(closed)return;var gain=ReplayGain.Decide(t,preferences,gainStore.Get(t,preferences.ReplayGain,GainAlbum(t)));var entry=new PlaybackEntry(t,queueIndex,gain,order.Capture(),startPosition??TimeSpan.Zero){Id=continueListening&&audibleEntry!=null?audibleEntry.Id:Guid.NewGuid()};audio.PlayStreaming(entry,MakePlaybackPlan(t,preferences),device,exclusive,startPaused,preferences);}
   now=t;audibleEntry=audio.CurrentEntry;if(!continueListening)BeginListening(t);resumePrepared=false;resumeSeconds=0;ShowTrackDetails(t);SyncPlaybackVisuals();SetSignal(audio.Signal+(t.WavPackMode.Length>0?"\nWavPack: "+t.WavPackMode:""));Status.Text=L.T("Çalıyor · ")+t.Quality;PlayIcon.Icon="pause";UpdateNextTrack();NotifyTrackChange();SaveSession(false);_=Task.Run(()=>AudioEngine.TrimCache(cache,wav));
  }catch(OperationCanceledException){}catch(Exception ex){if(!closed){SetSignal(L.T("Oynatma başlatılamadı"));Status.Text=ex.Message;MessageBox.Show(this,ex.Message,"Lyra — oynatma",MessageBoxButton.OK,MessageBoxImage.Information);}}finally{if(ReferenceEquals(playCts,request))playCts=null;request.Dispose();}
 }
 async void Play_Click(object s,RoutedEventArgs e)=>await TogglePlayback();
 async Task TogglePlayback(){if(audio.IsPlaying||audio.IsPaused){audio.Toggle();SyncPlaybackVisuals();}else if(resumePrepared&&now!=null)await PlayTrack(now,TimeSpan.FromSeconds(resumeSeconds));else if(Tracks.Visibility==Visibility.Visible&&Tracks.SelectedItem is Track t)await StartFromView(t);else if(now!=null)await PlayTrack(now);else if(queue.Count>0){queueIndex=0;order.Reset(queue.Count,0);await PlayTrack(queue[0]);}else if(Tracks.Visibility==Visibility.Visible&&Tracks.Items.OfType<Track>().FirstOrDefault() is {} first)await StartFromView(first);}
 async Task Advance(bool natural){UpdateStreamTrack();int next=order.Next(queue.Count,queueIndex,natural);if(next>=0){queueIndex=next;await PlayTrack(queue[next]);}else{audio.Stop();Scope.Reset();Status.Text=L.T("Çalma kuyruğu tamamlandı.");}}
 async void Next_Click(object s,RoutedEventArgs e)=>await Advance(false);
 async void Previous_Click(object s,RoutedEventArgs e)=>await PreviousTrack();
 async Task PreviousTrack(){UpdateStreamTrack();if(PlayerPosition.TotalSeconds>3){if(resumePrepared&&!audio.IsPlaying&&!audio.IsPaused)resumeSeconds=0;else audio.Position=TimeSpan.Zero;return;}int previous=order.Previous(queueIndex);if(previous>=0){queueIndex=previous;await PlayTrack(queue[previous]);}}
 async void Shuffle_Click(object s,RoutedEventArgs e)=>await ToggleShuffle();
 async Task ToggleShuffle(){order.SetShuffle(!order.Shuffle,queue.Count,queueIndex);UpdateAccentControls();ShuffleButton.ToolTip=L.T("Karışık çalma: ")+(order.Shuffle?L.T("açık"):L.T("kapalı"));await RebuildPlaybackPlan();SaveSession(false);}
 async void Repeat_Click(object s,RoutedEventArgs e)=>await CycleRepeat();
 async Task CycleRepeat(){order.Repeat=(RepeatMode)(((int)order.Repeat+1)%3);UpdateAccentControls();RepeatOne.Visibility=order.Repeat==RepeatMode.One?Visibility.Visible:Visibility.Collapsed;RepeatButton.ToolTip="Tekrar: "+(order.Repeat==RepeatMode.Off?L.T("kapalı"):order.Repeat==RepeatMode.All?L.T("tüm kuyruk"):L.T("tek parça"));await RebuildPlaybackPlan();SaveSession(false);}
 void RenderAlbum()
 {
  var songs=library.Where(t=>t.AlbumKey==albumKey).ToList();if(songs.Count==0)return;var first=songs[0];HeroKind.Text=L.T("ALBÜM");AlbumName.Text=first.Album;HeroFallback.Visibility=Visibility.Visible;AlbumCoverBox.SetResourceReference(Border.BackgroundProperty,"Surface");
  string year=first.Fields.FirstOrDefault(f=>f.Key.EndsWith("/ DATE",StringComparison.OrdinalIgnoreCase)||f.Key.EndsWith("/ YEAR",StringComparison.OrdinalIgnoreCase))?.Value??"";if(year.Length>4)year=year[..4];
  var duration=TimeSpan.FromSeconds(songs.Sum(t=>t.Seconds));AlbumSummary.AllowedNames=ArtistCatalog.ProfileNames(library);AlbumSummary.Names=first.AlbumArtist;AlbumSummary.Suffix=(year.Length>0?" · "+year:"")+L.T($" · {songs.Count} parça · ")+(duration.TotalHours>=1?L.T($"{(int)duration.TotalHours} sa. "):"")+L.T($"{duration.Minutes} dk.");
  var quality=AlbumAudioInfo.From(songs);AlbumQualityText.Text=quality.Text;AlbumQualityBadge.ToolTip=quality.Details;
  var cover=songs.Select(t=>t.Cover).FirstOrDefault(File.Exists);if(renderedAlbum==albumKey+cover){HeroFallback.Visibility=AlbumArtwork.Source!=null?Visibility.Collapsed:Visibility.Visible;return;}renderedAlbum=albumKey+cover;AlbumArtwork.Source=null;Color tint=Color.FromRgb(35,35,35);
   if(cover!=null)try{var bitmap=CoverImage(cover)??throw new NotSupportedException(L.T("Kapak okunamadı."));AlbumArtwork.Source=bitmap;
   var small=new FormatConvertedBitmap(new TransformedBitmap(bitmap,new ScaleTransform(32.0/bitmap.PixelWidth,32.0/bitmap.PixelHeight)),PixelFormats.Bgra32,null,0);int stride=small.PixelWidth*4;var pixels=new byte[stride*small.PixelHeight];small.CopyPixels(pixels,stride,0);long r=0,g=0,b=0;int count=pixels.Length/4;for(int i=0;i<pixels.Length;i+=4){b+=pixels[i];g+=pixels[i+1];r+=pixels[i+2];}tint=Color.FromRgb((byte)Math.Clamp(r/count*.6,20,115),(byte)Math.Clamp(g/count*.6,20,115),(byte)Math.Clamp(b/count*.6,20,135));
  }catch(IOException){}catch(NotSupportedException){}
  if(Themes.Current=="light")tint=Color.FromRgb((byte)(185+tint.R*.35),(byte)(185+tint.G*.35),(byte)(185+tint.B*.35));
  if(Themes.Appearance.Colors.TryGetValue("AlbumTint",out string? customTint))tint=(Color)ColorConverter.ConvertFromString(customTint);AlbumHero.Background=Brushes.Transparent;AmbientCover.Source=AlbumArtwork.Source;HeroFallback.Visibility=AlbumArtwork.Source!=null?Visibility.Collapsed:Visibility.Visible;
  PaletteGradient.Background=new LinearGradientBrush(new GradientStopCollection{new(tint,0),new(Themes.Current=="light"?Color.FromRgb((byte)(210+tint.R*.15),(byte)(210+tint.G*.15),(byte)(210+tint.B*.15)):Color.FromRgb((byte)(tint.R*.55),(byte)(tint.G*.55),(byte)(tint.B*.65)),.55),new(((SolidColorBrush)FindResource("Canvas")).Color,1)},new Point(0,0),new Point(1,1));
 }
 async void AlbumPlay_Click(object s,RoutedEventArgs e){if(Tracks.ItemsSource is IEnumerable<Track> tracks){var songs=tracks.ToList();if(songs.Count>0)await StartFromView(songs[order.Shuffle?Random.Shared.Next(songs.Count):0]);}}
 void AlbumEnqueue_Click(object s,RoutedEventArgs e){if(Tracks.ItemsSource is IEnumerable<Track> tracks){EnqueueTracks(tracks,false);}}
 void ClearQueue_Click(object s,RoutedEventArgs e){ChangeQueue(queue.Clear);Status.Text=L.T("Kuyruk temizlendi.");}
 void Seek_Down(object s,MouseButtonEventArgs e){if(now!=null&&(audio.IsPlaying||audio.IsPaused||resumePrepared)){dragging=true;Seek.CaptureMouse();MoveSeek(e.GetPosition(Seek).X);}e.Handled=true;}
 void Seek_Up(object s,MouseButtonEventArgs e){if(dragging){MoveSeek(e.GetPosition(Seek).X);CommitSeek();dragging=false;Seek.ReleaseMouseCapture();e.Handled=true;}}
}


