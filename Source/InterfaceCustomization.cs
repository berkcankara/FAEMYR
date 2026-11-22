using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
namespace Faemyr;
public partial class MainWindow
{
 readonly List<Action> interfaceEditorRefresh=[],interfaceEditorCleanup=[];bool applyingInterface;int lastAlbumColumns,lastArtistColumns,lastPlaylistColumns;
 IEnumerable<(string Key,DataGridColumn Column)> CustomizableColumns()=>new (string,DataGridColumn)[]{("favorite",TrackFavoriteColumn),("number",NumberColumn),("title",TrackTitleColumn),("artist",ArtistColumn),("format",FormatColumn),("bitrate",BitrateColumn),("waveform",WaveformColumn),("duration",DurationColumn)};
 void InitializeInterfaceCustomization()
 {
  Tracks.CanUserReorderColumns=true;Tracks.ColumnReordered+=(_,_)=>{if(applyingInterface)return;InterfaceSettings.Current.ColumnOrder=CustomizableColumns().OrderBy(x=>x.Column.DisplayIndex).Select(x=>x.Key).ToList();if(settingsWindow==null)try{InterfaceSettings.Current.Save(InterfaceSettings.Path);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Status.Text=ex.Message;}};
  AlbumArtwork.SizeChanged+=(_,_)=>UpdateArtworkClip();
  InitializePlayerBoxAlignment();ApplyInterfacePreferences(false);
 }
 void ApplyInterfacePreferences(bool allowRefresh=true)
 {
  var p=InterfaceSettings.Current;p.Validate();InterfaceSettings.ApplyResources();
  if(SidebarBorder==null)return;
  SidebarBorder.SetResourceReference(Border.BackgroundProperty,"ModuleSidebarSurface");DetailPanel.SetResourceReference(Border.BackgroundProperty,"ModuleDetailSurface");
  LyricsCard.SetResourceReference(Border.BackgroundProperty,"ModuleLyricsSurface");NextTrackCard.SetResourceReference(Border.BackgroundProperty,"ModuleUpNextSurface");
  WindowRoot.ColumnDefinitions[0].Width=new GridLength(p.SidebarWidth);PlayerRow.Height=GridLength.Auto;PlayerRow.MinHeight=Math.Max(p.PlayerHeight,p.PlaySize+44+(p.ShowVolume?24:0));
  TransportControlsHost.RowDefinitions[0].Height=new GridLength(Math.Max(52,p.PlaySize+4));TransportControlsHost.RowDefinitions[1].Height=new GridLength(p.ShowVolume?24:0);
  double sidePadding=Math.Clamp((p.SidebarWidth-156)/2,12,22);SidebarBorder.Padding=new Thickness(Math.Max(4,sidePadding-8),22,Math.Max(4,sidePadding-8),14);
  MainContent.Margin=new Thickness(p.ContentPadding,p.ContentPadding-2,p.ContentPadding-4,Math.Max(10,p.ContentPadding-12));
  AlbumHero.Margin=new Thickness(-p.ContentPadding,-(p.ContentPadding-2),-(p.ContentPadding-4),0);ArtistHero.Margin=new Thickness(-p.ContentPadding,-(p.ContentPadding-2),-(p.ContentPadding-4),22);
  foreach(var cover in new[]{AmbientCover,PreviousAmbientCover}){cover.Opacity=p.ArtworkOpacity/100;if(cover.Effect is BlurEffect effect)effect.Radius=p.ArtworkBlur;}
  ApplyDetailsPanelLayout(p.ShowRightPanel);
  SignalBox.Visibility=Visible(p.ShowSignal);UpdatePlayerBoxAlignment();
  foreach(var module in new[]{TransportBox,ScopeFrame,SignalBox})
  {
   module.SetResourceReference(Border.BackgroundProperty,p.PlayerFrames?"ModulePlayerSurface":"PlayerSurface");
   module.BorderThickness=new Thickness(p.PlayerFrames?.7:0);module.PlainPresentation=!p.PlayerFrames;
  }
  VolumeControls.Visibility=Visible(p.ShowVolume);ShuffleButton.Visibility=Visible(p.ShowShuffle);RepeatButton.Visibility=Visible(p.ShowRepeat);
  Scope.Visibility=Visible(p.ShowVisualizer);Scope.MaximumFramesPerSecond=p.VisualizerFps;Scope.TraceWidth=p.TraceWidth;Scope.GlowOpacity=p.TraceGlow/100;Scope.Opacity=p.TraceOpacity/100;
  if(p.LyricsGlow)LyricsCard.SetResourceReference(InnerAccentBorder.GlowBrushProperty,"LyricsAccent");else LyricsCard.GlowBrush=Brushes.Transparent;
  if(p.VisualizerMode.HasValue)Scope.Mode=p.VisualizerMode.Value;
  ScopeAndSeek.RowDefinitions[0].Height=new GridLength(1,GridUnitType.Star);ScopeAndSeek.RowDefinitions[1].Height=new GridLength(p.ShowSeek?22:0);
  Seek.Visibility=Visible(p.ShowSeek);Elapsed.Visibility=TotalTime.Visibility=Visible(p.ShowTimes&&p.ShowSeek);ScopeAndSeek.ColumnDefinitions[0].Width=new GridLength(p.ShowTimes?36:0);ScopeAndSeek.ColumnDefinitions[2].Width=GridLength.Auto;
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
  TrackArea.Margin=hero?new Thickness(8-p.ContentPadding,8,8-(p.ContentPadding-4),8-Math.Max(10,p.ContentPadding-12)):collection?new Thickness(0):new Thickness(-p.ContentPadding,0,-(p.ContentPadding-4),-Math.Max(10,p.ContentPadding-12));
  TrackListFrame.Visibility=Tracks.Visibility;TrackListFrame.PlainPresentation=!hero;
  TrackListFrame.BorderThickness=new Thickness(hero?.7:0);TrackListFrame.Padding=new Thickness(hero?8:0);
  RoundedClip.SetRadius(TrackListViewport,hero?p.PanelRadius:0);
  UpdatePageBackdrop();TrackGlass.Visibility=Visible(hero);AlbumQualityBadge.Visibility=Visible(view=="album"&&p.ShowAlbumQuality);
  if(NextTrackCard!=null&&now!=null){nextVisual="";UpdateNextTrack();}
  UpdateArtistProfileLayout();UpdateCollectionHeaderLayout();
 }
}
