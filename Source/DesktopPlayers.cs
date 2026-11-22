using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
namespace Faemyr;
public sealed class TrackChangeGate
{
 string? last;
 public bool Changed(string? path){if(string.IsNullOrEmpty(path)||string.Equals(last,path,StringComparison.OrdinalIgnoreCase))return false;last=path;return true;}
}
public sealed class DesktopPlayerPreferences
{
 public bool Notifications{get;set;}=true;
 public bool MiniTopmost{get;set;}=true;
 public double? Left{get;set;}public double? Top{get;set;}
 public double CompactWidth{get;set;}=430;public double CompactHeight{get;set;}=196;
 public double ExpandedWidth{get;set;}=380;public double ExpandedHeight{get;set;}=380;public bool CoverExpanded{get;set;}
 public void Validate(){if(Left is double left&&(!double.IsFinite(left)||Math.Abs(left)>100000))Left=null;if(Top is double top&&(!double.IsFinite(top)||Math.Abs(top)>100000))Top=null;CompactWidth=Bound(CompactWidth,350,1000,430);CompactHeight=Bound(CompactHeight,190,1000,196);ExpandedWidth=Bound(ExpandedWidth,260,1000,380);ExpandedHeight=ExpandedWidth;}
 static double Bound(double value,double min,double max,double fallback)=>double.IsFinite(value)?Math.Clamp(value,min,max):fallback;
}
public partial class MainWindow
{
 DesktopPlayerPreferences desktopPreferences=new();MiniPlayerWindow? miniPlayer;TrackToastWindow? trackToast;bool closingDesktopPlayers;readonly TrackChangeGate notificationTracks=new();string? miniArtistTrack;
 readonly DispatcherTimer desktopSaveTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
 string DesktopPlayersPath=>Path.Combine(Paths.Data,"desktop-player.json");
 void InitializeDesktopPlayers()
 {
  desktopPreferences=PlayerJson.Read<DesktopPlayerPreferences>(DesktopPlayersPath)??new();desktopPreferences.Validate();desktopSaveTimer.Tick+=(_,_)=>SaveDesktopPlayers();
  PreviewKeyDown+=(_,e)=>{if(e.Key==Key.M&&Keyboard.Modifiers==(ModifierKeys.Control|ModifierKeys.Shift)){e.Handled=true;ToggleMiniPlayer();}};
  Closing+=(_,_)=>{closingDesktopPlayers=true;if(miniPlayer!=null)SaveDesktopPlayers();desktopSaveTimer.Stop();miniPlayer?.Close();miniPlayer=null;trackToast?.Dismiss();trackToast?.Close();trackToast=null;};
 }
 void SaveDesktopPlayers(){desktopSaveTimer.Stop();try{if(miniPlayer!=null)miniPlayer.CapturePreferences(desktopPreferences);desktopPreferences.Validate();PlayerJson.Write(DesktopPlayersPath,desktopPreferences);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){if(!closed)Status.Text=L.T("Ayarlar kaydedilemedi: ")+ex.Message;}}
 void MiniPlayer_Click(object sender,RoutedEventArgs e)=>ToggleMiniPlayer();
 void ToggleMiniPlayer()
 {
  if(!libraryReady||settingsWindow!=null||closed)return;if(miniPlayer!=null){RestoreMainPlayer();return;}if(fullscreenPlayer)ExitFullscreenPlayer();
  var mini=new MiniPlayerWindow(RunDesktopAction,RestoreMainPlayer,pin=>{desktopPreferences.MiniTopmost=pin;SaveDesktopPlayers();},position=>{Seek.Value=position;CommitSeek();},level=>VolumeSlider.Value=level,()=>Mute_Click(this,new RoutedEventArgs())){Topmost=desktopPreferences.MiniTopmost,Opacity=Opacity,ShowInTaskbar=ShowInTaskbar,ShowActivated=ShowActivated,Icon=Icon};miniPlayer=mini;miniArtistTrack=null;mini.SyncPin();
  mini.RestorePreferences(desktopPreferences);
  mini.AddHandler(ArtistLinks.ArtistRequestedEvent,new RoutedEventHandler((_,e)=>{RestoreMainPlayer();ArtistLinkRequested(this,e);}));
  var context=new ContextMenu();var restore=new MenuItem{Header=L.T("Ana pencereye dön")};restore.Click+=(_,_)=>RestoreMainPlayer();context.Items.Add(restore);var quit=new MenuItem{Header=L.T("Faemyr’den çık")};quit.Click+=(_,_)=>Close();context.Items.Add(quit);mini.ContextMenu=context;
  bool placed=desktopPreferences.Left.HasValue&&desktopPreferences.Top.HasValue;if(placed){mini.Left=desktopPreferences.Left!.Value;mini.Top=desktopPreferences.Top!.Value;}
  mini.Closed+=(_,_)=>{desktopSaveTimer.Stop();mini.CapturePreferences(desktopPreferences);if(ReferenceEquals(miniPlayer,mini))miniPlayer=null;if(!closingDesktopPlayers){Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;if(Opacity>0)Activate();SaveDesktopPlayers();}};
  UpdateMiniPlayer();mini.Show();mini.UpdateLayout();DesktopOverlayPolicy.TopRight(mini,placed);
  mini.LocationChanged+=(_,_)=>{desktopPreferences.Left=mini.Left;desktopPreferences.Top=mini.Top;desktopSaveTimer.Stop();desktopSaveTimer.Start();};desktopPreferences.Left=mini.Left;desktopPreferences.Top=mini.Top;
  mini.SizeChanged+=(_,_)=>{desktopSaveTimer.Stop();desktopSaveTimer.Start();};
  Hide();if(Opacity>0)mini.Activate();SaveDesktopPlayers();
 }
 void RestoreMainPlayer(){if(closingDesktopPlayers)return;if(miniPlayer is {} mini){miniPlayer=null;mini.Close();}Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;if(Opacity>0)Activate();}
 void UpdateMiniPlayer()
 {
  if(miniPlayer==null)return;miniPlayer.Sync(now,audio.IsPlaying,PlayerPosition,PlayerDuration,audio.Volume,audio.Muted,audio.IsDop);
  miniPlayer.SyncModes(order.Shuffle,order.Repeat);
  if(miniArtistTrack!=now?.Path){miniArtistTrack=now?.Path;miniPlayer.Artist.AllowedNames=ArtistCatalog.ProfileNames(library);}
 }
 void NotifyTrackChange()
 {
  if(now==null||!audio.IsPlaying||!audio.DeviceStarted||Opacity<=0)return;if(!notificationTracks.Changed(now.Path))return;
  if(!desktopPreferences.Notifications||fullscreenPlayer||DesktopOverlayPolicy.Suppressed())return;
  trackToast??=new TrackToastWindow(()=>closed||fullscreenPlayer||!desktopPreferences.Notifications||DesktopOverlayPolicy.Suppressed());trackToast.Present(now);
 }
 SettingsSection BuildDesktopPlayerSettings()
 {
  var root=new StackPanel();var heading=new TextBlock{Text=L.T("Masaüstü oynatıcı"),FontSize=24,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,18)};root.Children.Add(heading);
  var open=new Button{Content=L.T("Mini oynatıcıyı aç · Ctrl+Shift+M"),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,18)};open.Click+=(_,_)=>{settingsWindow?.Close();Dispatcher.BeginInvoke(ToggleMiniPlayer);};root.Children.Add(open);
  var pinned=new CheckBox{Content=L.T("Mini oynatıcı her zaman üstte"),IsChecked=desktopPreferences.MiniTopmost,Margin=new Thickness(0,0,0,16)};pinned.SetResourceReference(Control.ForegroundProperty,"Ink");root.Children.Add(pinned);
  var notifications=new CheckBox{Content=L.T("Parça değişiminde masaüstü bildirimi göster"),IsChecked=desktopPreferences.Notifications,Margin=new Thickness(0,0,0,12)};notifications.SetResourceReference(Control.ForegroundProperty,"Ink");root.Children.Add(notifications);
  var hint=new TextBlock{Text=L.T("Bildirim iki saniye görünür, ardından yavaşça solar. Tam ekran uygulamalarda gösterilmez."),TextWrapping=TextWrapping.Wrap};hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");root.Children.Add(hint);
  return new(root,()=>{desktopPreferences.Notifications=notifications.IsChecked==true;desktopPreferences.MiniTopmost=pinned.IsChecked==true;if(miniPlayer!=null){miniPlayer.Topmost=desktopPreferences.MiniTopmost;miniPlayer.SyncPin();}if(!desktopPreferences.Notifications)trackToast?.Dismiss();SaveDesktopPlayers();},()=>{},()=>true);
 }
}
