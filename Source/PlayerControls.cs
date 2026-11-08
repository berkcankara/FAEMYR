using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ahenk;
public partial class MainWindow
{
 PlayerPreferences playerPreferences=new();bool playerInitialized,sessionReady,resumePrepared;
 double resumeSeconds;Task sessionWrite=Task.CompletedTask;
 readonly DispatcherTimer sessionTimer=new(){Interval=TimeSpan.FromSeconds(5)};
 readonly DispatcherTimer volumeSaveTimer=new(){Interval=TimeSpan.FromMilliseconds(350)};
 string PlayerSettingsPath=>Path.Combine(Paths.Data,"player-settings.json");
 string SessionPath=>Path.Combine(Paths.Data,"session.json");
 TimeSpan PlayerPosition=>audio.IsPlaying||audio.IsPaused?audio.Position:TimeSpan.FromSeconds(resumePrepared?resumeSeconds:0);
 TimeSpan PlayerDuration=>audio.IsPlaying||audio.IsPaused?audio.Duration:TimeSpan.FromSeconds(resumePrepared?Math.Max(0,now?.Seconds??0):0);
 void InitializePlayerControls()
 {
  InitializeSignalPresentation();
  playerPreferences=PlayerPreferences.Load(PlayerSettingsPath);audio.Volume=playerPreferences.Volume;audio.Muted=playerPreferences.Muted;
  VolumeSlider.Value=playerPreferences.Volume*100;playerInitialized=true;UpdateVolumeControls();
  volumeSaveTimer.Tick+=(_,_)=>{volumeSaveTimer.Stop();SavePlayerPreferences();};
  sessionTimer.Tick+=(_,_)=>SaveSession(false);sessionTimer.Start();
 }
 void Volume_Changed(object sender,RoutedPropertyChangedEventArgs<double> e)
 {
  if(!playerInitialized||audio.IsDop)return;playerPreferences.Volume=e.NewValue/100;playerPreferences.Muted=false;ApplyVolume();
 }
 void Mute_Click(object sender,RoutedEventArgs e){if(audio.IsDop)return;playerPreferences.Muted=!playerPreferences.Muted;ApplyVolume();}
 void ApplyVolume(){audio.Volume=playerPreferences.Volume;audio.Muted=playerPreferences.Muted;UpdateVolumeControls();volumeSaveTimer.Stop();volumeSaveTimer.Start();}
 void UpdateVolumeControls()
 {
  if(!playerInitialized)return;bool silent=playerPreferences.Muted||playerPreferences.Volume==0;
  VolumeIcon.Icon=silent?"muted":"volume";VolumeLabel.Text=playerPreferences.Muted?L.T("Sessiz"):$"{playerPreferences.Volume*100:0}%";
  string hint=audio.IsDop?L.T("DoP: ses seviyesi DAC üzerinden ayarlanır."):playerPreferences.Muted?L.T("Sesi aç"):L.T($"Ses seviyesi: %{playerPreferences.Volume*100:0}");
  MuteButton.IsEnabled=VolumeSlider.IsEnabled=!audio.IsDop;MuteButton.ToolTip=hint;VolumeSlider.ToolTip=hint;
  System.Windows.Automation.AutomationProperties.SetName(MuteButton,playerPreferences.Muted?L.T("Sesi aç"):L.T("Sessize al"));
  if(audio.IsPlaying||audio.IsPaused)SetSignal(audio.Signal+(now?.WavPackMode.Length>0?"\nWavPack: "+now.WavPackMode:""));
 }
 void SavePlayerPreferences(){try{PlayerJson.Write(PlayerSettingsPath,playerPreferences);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Status.Text=L.T("Oynatıcı ayarları kaydedilemedi: ")+ex.Message;}}
 static ScrollViewer? FindScroll(DependencyObject? node)
 {
  if(node is ScrollViewer viewer)return viewer;if(node==null)return null;
  for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(FindScroll(VisualTreeHelper.GetChild(node,i)) is {} child)return child;return null;
 }
 ScrollViewer? CurrentScroll()=>FindScroll(view switch{"albums" or "artist"=>Albums,"artists"=>Artists,"playlists"=>PlaylistCards,"favorites" when favoritesTab=="albums"=>Albums,_=>Tracks});
 PlayerSession CaptureSession()
 {
  var scroll=CurrentScroll();return new(){Queue=queue.Select(t=>t.Path).ToList(),QueueIndex=queueIndex,TrackPath=now?.Path,Seconds=PlayerPosition.TotalSeconds,View=view,AlbumKey=albumKey,ArtistName=artistName,PlaylistId=playlistId,Search=Search.Text,FavoritesTab=favoritesTab,SelectedPath=(Tracks.SelectedItem as Track)?.Path,ScrollOffset=scroll?.VerticalOffset??0,HorizontalOffset=scroll?.HorizontalOffset??0,PanelOffset=DetailScroll.VerticalOffset,Shuffle=order.Shuffle,Repeat=order.Repeat,Order=order.Capture(),SmartViewIndex=SmartView.SelectedIndex};
 }
 void SaveSession(bool closing)
 {
  if(!libraryReady||!sessionReady)return;
  if(!closing&&!sessionWrite.IsCompleted)return;var snapshot=CaptureSession();
  void Save(){try{PlayerJson.Write(SessionPath,snapshot);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){/* A read-only directory must not interrupt playback. */}}
  if(closing){sessionWrite.GetAwaiter().GetResult();Save();}else sessionWrite=Task.Run(Save);
 }
 void RestoreSession(PlayerSession? saved)
 {
  if(saved==null){sessionReady=true;return;}var resolved=saved.Resolve(library);queue.Clear();queue.AddRange(resolved.Queue);queueIndex=resolved.Index;
  now=resolved.Current;resumePrepared=now!=null;resumeSeconds=resolved.Seconds;
  order.Repeat=Enum.IsDefined(saved.Repeat)?saved.Repeat:RepeatMode.Off;order.SetShuffle(saved.Shuffle,queue.Count,queueIndex);
  if(saved.Order is {} state&&resolved.Queue.Count==saved.Queue.Count&&state.History!=null&&state.Remaining!=null&&state.Priority!=null&&state.History.Concat(state.Remaining).Concat(state.Priority).All(i=>i>=0&&i<queue.Count)&&Enum.IsDefined(state.Repeat))order.Restore(state);
  SmartView.SelectedIndex=Math.Clamp(saved.SmartViewIndex,0,SmartView.Items.Count-1);
  RepeatOne.Visibility=Visible(order.Repeat==RepeatMode.One);RepeatButton.ToolTip="Tekrar: "+(order.Repeat==RepeatMode.Off?L.T("kapalı"):order.Repeat==RepeatMode.All?L.T("tüm kuyruk"):L.T("tek parça"));ShuffleButton.ToolTip=L.T("Karışık çalma: ")+(order.Shuffle?L.T("açık"):L.T("kapalı"));UpdateAccentControls();
  albumKey=saved.AlbumKey??"";artistName=saved.ArtistName??"";playlistId=saved.PlaylistId??"";
  view=saved.View is "albums" or "artists" or "artist" or "tracks" or "favorites" or "playlists" or "playlist" or "queue" or "album"?saved.View:"albums";
  if(view=="album"&&!library.Any(t=>t.AlbumKey==albumKey))view="albums";
  if(view=="artist"&&!ArtistCatalog.ProfileNames(library).Contains(artistName))view="artists";
  if(view=="playlist"&&CurrentPlaylist==null)view="playlists";
  favoritesTab=saved.FavoritesTab=="albums"?"albums":"tracks";Search.Text=saved.Search??"";searchDelay.Stop();
  if(now!=null){ShowTrackDetails(now);SetSignal(L.T("Oturum geri yüklendi · oynatılmayı bekliyor"));}RefreshView();
  Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,()=>
  {
   if(closed)return;if(Tracks.ItemsSource is IEnumerable<Track> rows)Tracks.SelectedItem=rows.FirstOrDefault(t=>t.Path==saved.SelectedPath);
   var scroll=CurrentScroll();if(scroll!=null){scroll.ScrollToVerticalOffset(FiniteOffset(saved.ScrollOffset));scroll.ScrollToHorizontalOffset(FiniteOffset(saved.HorizontalOffset));}DetailScroll.ScrollToVerticalOffset(FiniteOffset(saved.PanelOffset));sessionReady=true;
  });
 }
 static double FiniteOffset(double value)=>double.IsFinite(value)?Math.Max(0,value):0;
 void ClosePlayerControls(){sessionTimer.Stop();volumeSaveTimer.Stop();SaveSession(true);SavePlayerPreferences();}
}
