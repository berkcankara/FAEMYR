using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
namespace Ahenk;
public partial class MainWindow
{
 readonly List<Action> interfaceEditorRefresh=[],interfaceEditorCleanup=[];bool applyingInterface;int lastAlbumColumns,lastArtistColumns,lastPlaylistColumns;
 IEnumerable<(string Key,DataGridColumn Column)> CustomizableColumns()=>new (string,DataGridColumn)[]{("favorite",TrackFavoriteColumn),("number",NumberColumn),("title",TrackTitleColumn),("artist",ArtistColumn),("format",FormatColumn),("bitrate",BitrateColumn),("waveform",WaveformColumn),("duration",DurationColumn)};
 void InitializeInterfaceCustomization()
 {
  Tracks.CanUserReorderColumns=true;Tracks.ColumnReordered+=(_,_)=>{if(applyingInterface)return;InterfaceSettings.Current.ColumnOrder=CustomizableColumns().OrderBy(x=>x.Column.DisplayIndex).Select(x=>x.Key).ToList();if(settingsWindow==null)try{InterfaceSettings.Current.Save(InterfaceSettings.Path);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Status.Text=ex.Message;}};
  AlbumArtwork.SizeChanged+=(_,_)=>UpdateArtworkClip();
  ApplyInterfacePreferences(false);
 }
 void ApplyInterfacePreferences(bool allowRefresh=true)
 {
  var p=InterfaceSettings.Current;p.Validate();InterfaceSettings.ApplyResources();
  if(SidebarBorder==null)return;
  WindowRoot.ColumnDefinitions[0].Width=new GridLength(p.SidebarWidth);PlayerRow.Height=GridLength.Auto;PlayerRow.MinHeight=Math.Max(p.PlayerHeight,p.PlaySize+44+(p.ShowVolume?24:0));
  TransportControlsHost.RowDefinitions[0].Height=new GridLength(Math.Max(52,p.PlaySize+4));TransportControlsHost.RowDefinitions[1].Height=new GridLength(p.ShowVolume?24:0);
  double sidePadding=Math.Clamp((p.SidebarWidth-156)/2,12,22);SidebarBorder.Padding=new Thickness(sidePadding,30,sidePadding,14);
  MainContent.Margin=new Thickness(p.ContentPadding,p.ContentPadding-2,p.ContentPadding-4,Math.Max(10,p.ContentPadding-12));
  AlbumHero.Margin=new Thickness(-p.ContentPadding,-(p.ContentPadding-2),-(p.ContentPadding-4),0);ArtistHero.Margin=new Thickness(-p.ContentPadding,-(p.ContentPadding-2),-(p.ContentPadding-4),22);
  AmbientCover.Opacity=p.ArtworkOpacity/100;if(AmbientCover.Effect is BlurEffect effect)effect.Radius=p.ArtworkBlur;
  DetailPanel.Visibility=DetailsSplitter.Visibility=Visible(p.ShowRightPanel);DetailsColumn.MinWidth=p.ShowRightPanel?280:0;DetailsColumn.Width=new GridLength(p.ShowRightPanel?Math.Clamp(p.RightPanelWidth??panelPreferences.Width,280,640):0);
  double sideWidth=Math.Max(p.PlayerSideWidth,p.PlaySize+212);TransportColumn.Width=new GridLength(sideWidth);SignalColumn.Width=new GridLength(p.ShowSignal?sideWidth:0);SignalBox.Visibility=Visible(p.ShowSignal);
  TransportBox.SetResourceReference(Border.BackgroundProperty,p.PlayerFrames?"Surface":"PlayerSurface");SignalBox.SetResourceReference(Border.BackgroundProperty,p.PlayerFrames?"Surface":"PlayerSurface");TransportBox.BorderThickness=SignalBox.BorderThickness=new Thickness(p.PlayerFrames?1:0);
  VolumeControls.Visibility=Visible(p.ShowVolume);ShuffleButton.Visibility=Visible(p.ShowShuffle);RepeatButton.Visibility=Visible(p.ShowRepeat);
  Scope.Visibility=Visible(p.ShowVisualizer);Scope.MaximumFramesPerSecond=p.VisualizerFps;Scope.TraceWidth=p.TraceWidth;Scope.GlowOpacity=p.TraceGlow/100;Scope.Opacity=p.TraceOpacity/100;
  if(p.LyricsGlow)LyricsCard.SetResourceReference(InnerAccentBorder.GlowBrushProperty,"LyricsAccent");else LyricsCard.GlowBrush=Brushes.Transparent;
  if(p.VisualizerMode.HasValue)Scope.Mode=p.VisualizerMode.Value;
  ScopeAndSeek.RowDefinitions[0].Height=p.ShowVisualizer?new GridLength(1,GridUnitType.Star):new GridLength(0);ScopeAndSeek.RowDefinitions[1].Height=p.ShowSeek?(p.ShowVisualizer?new GridLength(22):new GridLength(1,GridUnitType.Star)):new GridLength(0);
  Seek.Visibility=Visible(p.ShowSeek);Elapsed.Visibility=TotalTime.Visibility=Visible(p.ShowTimes&&p.ShowSeek);ScopeAndSeek.ColumnDefinitions[0].Width=ScopeAndSeek.ColumnDefinitions[2].Width=new GridLength(p.ShowTimes?36:0);
  Signal.MaxHeight=double.PositiveInfinity;Signal.FontSize=9.5*p.FontScale/100;Signal.LineHeight=11.5*p.FontScale/100;
  AlbumName.PreferredFontSize=p.AlbumTitleSize*p.FontScale/100;AlbumName.MaxHeight=170*p.FontScale/100;
  foreach(var (key,button) in new[]{("albums",NavAlbums),("artists",NavArtists),("tracks",NavTracks),("favorites",NavFavorites),("playlists",NavPlaylists),("queue",NavQueue)})button.Visibility=Visible(!p.HiddenNavigation.Contains(key));
  applyingInterface=true;try{var columns=CustomizableColumns().ToArray();int index=0;foreach(string key in p.ColumnOrder.Concat(columns.Select(x=>x.Key)).Distinct()){var pair=columns.FirstOrDefault(x=>x.Key==key);if(pair.Column!=null)pair.Column.DisplayIndex=index++;}}finally{applyingInterface=false;}
  ApplyInterfaceView();UpdatePanelScale();UpdateHeroLayout();UpdateArtworkClip();
  AutoHideScroll.Refresh(this);
  bool gridChanged=lastAlbumColumns!=p.AlbumColumns||lastArtistColumns!=p.ArtistColumns||lastPlaylistColumns!=p.PlaylistColumns;lastAlbumColumns=p.AlbumColumns;lastArtistColumns=p.ArtistColumns;lastPlaylistColumns=p.PlaylistColumns;
  if(libraryReady&&allowRefresh&&gridChanged)RefreshView();
 }
 void UpdateArtworkClip(){double radius=InterfaceSettings.Current.CardRadius;AlbumArtwork.Clip=new RectangleGeometry(new Rect(0,0,Math.Max(0,AlbumArtwork.ActualWidth),Math.Max(0,AlbumArtwork.ActualHeight)),radius,radius);}
 void ApplyInterfaceView()
 {
  var p=InterfaceSettings.Current;bool hero=view is "album" or "playlist",collection=view is "albums" or "artists" or "artist" or "playlists"||view=="favorites"&&favoritesTab=="albums";
  LibraryStats.Visibility=Visible(p.ShowLibraryStats&&!hero&&view!="artist");
  Tracks.RowHeight=Math.Max(p.RowHeight,25*p.FontScale/100+10)+(hero?6:0);
  Tracks.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty,p.AlternatingRows?"AlternateSurface":"Canvas");if(!p.AlternatingRows)Tracks.AlternatingRowBackground=Brushes.Transparent;
  TrackFavoriteColumn.Visibility=Visible(p.ShowTrackFavorites);NumberColumn.Visibility=Visible(p.ShowTrackNumbers);ArtistColumn.Visibility=Visible(p.ShowArtists&&(!hero||p.ArtistsInAlbums));FormatColumn.Visibility=Visible(p.ShowFormats);BitrateColumn.Visibility=Visible(p.ShowBitrate&&(hero||p.TechnicalColumnsEverywhere));WaveformColumn.Visibility=Visible(p.ShowWaveforms&&(hero||p.TechnicalColumnsEverywhere));DurationColumn.Visibility=Visible(p.ShowDurations);ArtistColumn.Width=p.ArtistColumnWidth;
  TrackArea.Margin=collection?new Thickness(0):new Thickness(-p.ContentPadding,0,-(p.ContentPadding-4),-Math.Max(10,p.ContentPadding-12));
  PaletteLayer.Visibility=Visible(hero&&p.AlbumArtworkBackground);TrackGlass.Visibility=Visible(hero);AlbumQualityBadge.Visibility=Visible(view=="album"&&p.ShowAlbumQuality);
  if(NextTrackCard!=null&&now!=null){nextVisual="";UpdateNextTrack();}
 }
}
