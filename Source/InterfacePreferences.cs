using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
namespace Ahenk;

public sealed class InterfacePreferences
{
 public string FontFamily{get;set;}="Segoe UI";
 public double FontScale{get;set;}=100;
 public double PageTitleSize{get;set;}=32;
 public double AlbumTitleSize{get;set;}=52;
 public double LyricsFontSize{get;set;}=17;
 public double PanelTextScale{get;set;}=100;
 public double SidebarWidth{get;set;}=200;
 public double NavigationHeight{get;set;}=42;
 public double NavigationGap{get;set;}=8;
 public double ContentPadding{get;set;}=30;
 public int AlbumColumns{get;set;}=6;
 public int ArtistColumns{get;set;}=6;
 public int PlaylistColumns{get;set;}=4;
 public double CardGap{get;set;}=8;
 public double CardRadius{get;set;}=12;
 public bool CircularArtists{get;set;}=true;
 public double ButtonRadius{get;set;}=8;
 public double PanelRadius{get;set;}=14;
 public double CoverMaximum{get;set;}=286;
 public double RowHeight{get;set;}=54;
 public List<string> ColumnOrder{get;set;}=[];
 public HashSet<string> HiddenNavigation{get;set;}=[];
 public double WaveformMaximum{get;set;}=176;
 public double ArtistColumnWidth{get;set;}=150;
 public bool AlternatingRows{get;set;}=true;
 public bool ShowRightPanel{get;set;}=true;
 public double? RightPanelWidth{get;set;}
 public bool ShowLibraryStats{get;set;}=true;
 public bool ShowCardDetails{get;set;}=true;
 public bool ShowAlbumQuality{get;set;}=true;
 public bool ShowNextTrack{get;set;}=true;
 public bool AlbumArtworkBackground{get;set;}=true;
 public double ArtworkOpacity{get;set;}=30;
 public double ArtworkBlur{get;set;}=90;
 public bool ShowTrackFavorites{get;set;}=true;
 public bool ShowTrackNumbers{get;set;}=true;
 public bool ShowRatings{get;set;}=true;
 public bool ShowArtists{get;set;}=true;
 public bool ArtistsInAlbums{get;set;}
 public bool ShowFormats{get;set;}=true;
 public bool ShowBitrate{get;set;}=true;
 public bool ShowWaveforms{get;set;}=true;
 public bool ShowDurations{get;set;}=true;
 public bool TechnicalColumnsEverywhere{get;set;}
 public double PlayerHeight{get;set;}=120;
 public double PlayerSideWidth{get;set;}=260;
 public double PlaySize{get;set;}=48;
 public double PlayRoundness{get;set;}=100;
 public double AlbumButtonHeight{get;set;}=52;
 public bool PlayerFrames{get;set;}=true;
 public bool ShowSignal{get;set;}=true;
 public bool ShowVisualizer{get;set;}=true;
 public bool ShowSeek{get;set;}=true;
 public bool ShowTimes{get;set;}=true;
 public bool ShowVolume{get;set;}=true;
 public bool ShowShuffle{get;set;}=true;
 public bool ShowRepeat{get;set;}=true;
 public double VisualizerFps{get;set;}=60;
 public double TraceWidth{get;set;}=1.4;
 public double TraceGlow{get;set;}=12;
 public double TraceOpacity{get;set;}=100;
 public VisualizerMode? VisualizerMode{get;set;}
 public bool LyricsGlow{get;set;}=true;
 public bool AutoHideScrollbars{get;set;}=true;
 public double ScrollbarWidth{get;set;}=12;
 public double ScrollbarDelay{get;set;}=1.5;
 public InterfacePreferences Copy()=>JsonSerializer.Deserialize<InterfacePreferences>(JsonSerializer.Serialize(this))!;
 static double Bound(double value,double min,double max,double fallback)=>double.IsFinite(value)?Math.Clamp(value,min,max):fallback;
 public void Validate()
 {
  FontFamily=string.IsNullOrWhiteSpace(FontFamily)||FontFamily.Length>160?"Segoe UI":FontFamily.Trim();
  if(FontFamily.IndexOfAny(['/', '\\', ':', '#'])>=0)FontFamily="Segoe UI";
  FontScale=Bound(FontScale,85,125,100);PageTitleSize=Bound(PageTitleSize,24,44,32);AlbumTitleSize=Bound(AlbumTitleSize,30,72,52);LyricsFontSize=Bound(LyricsFontSize,13,28,17);PanelTextScale=Bound(PanelTextScale,85,135,100);
  SidebarWidth=Bound(SidebarWidth,180,260,200);NavigationHeight=Bound(NavigationHeight,36,56,42);NavigationGap=Bound(NavigationGap,4,14,8);ContentPadding=Bound(ContentPadding,16,42,30);AlbumColumns=Math.Clamp(AlbumColumns,3,8);ArtistColumns=Math.Clamp(ArtistColumns,3,8);PlaylistColumns=Math.Clamp(PlaylistColumns,2,6);
  CardGap=Bound(CardGap,2,20,8);CardRadius=Bound(CardRadius,0,32,12);ButtonRadius=Bound(ButtonRadius,0,24,8);PanelRadius=Bound(PanelRadius,0,32,14);CoverMaximum=Bound(CoverMaximum,220,360,286);RowHeight=Bound(RowHeight,38,82,54);
  if(RightPanelWidth.HasValue)RightPanelWidth=double.IsFinite(RightPanelWidth.Value)?Math.Clamp(RightPanelWidth.Value,280,640):null;
  ColumnOrder=(ColumnOrder??[]).Where(k=>new[]{"favorite","number","title","artist","format","bitrate","waveform","duration"}.Contains(k)).Distinct().ToList();HiddenNavigation=new((HiddenNavigation??[]).Where(k=>new[]{"albums","artists","tracks","favorites","playlists","queue"}.Contains(k)));WaveformMaximum=Bound(WaveformMaximum,100,260,176);ArtistColumnWidth=Bound(ArtistColumnWidth,100,260,150);
  ArtworkOpacity=Bound(ArtworkOpacity,0,70,30);ArtworkBlur=Bound(ArtworkBlur,0,120,90);PlayerHeight=Bound(PlayerHeight,110,200,120);PlayerSideWidth=Bound(PlayerSideWidth,260,340,260);PlaySize=Bound(PlaySize,40,64,48);PlayRoundness=Bound(PlayRoundness,0,100,100);AlbumButtonHeight=Bound(AlbumButtonHeight,44,60,52);
  VisualizerFps=Bound(VisualizerFps,30,120,60);TraceWidth=Bound(TraceWidth,.8,3.5,1.4);TraceGlow=Bound(TraceGlow,0,45,12);TraceOpacity=Bound(TraceOpacity,20,100,100);ScrollbarWidth=Bound(ScrollbarWidth,6,16,12);ScrollbarDelay=Bound(ScrollbarDelay,.5,4,1.5);
  if(VisualizerMode.HasValue&&!Enum.IsDefined(VisualizerMode.Value))VisualizerMode=null;
 }
 public static InterfacePreferences Load(string path)
 {
  try{if(new FileInfo(path).Length>65536)return new();var p=JsonSerializer.Deserialize<InterfacePreferences>(File.ReadAllText(path))??new();p.Validate();return p;}catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){return new();}
 }
 public void Save(string path){Validate();PlayerJson.Write(path,this);}
}
public static class InterfaceSettings
{
 public static InterfacePreferences Current{get;set;}=new();
 public static string Path=>System.IO.Path.Combine(Paths.Data,"interface-settings.json");
 public static void Load(){Current=InterfacePreferences.Load(Path);ApplyResources();}
 public static void ApplyResources()
 {
  var p=Current;p.Validate();var r=Application.Current.Resources;
  r["UiFontFamily"]=new FontFamily(p.FontFamily);for(int size=8;size<=72;size++)r["UiFont"+size]=size*p.FontScale/100;
  r["UiPageTitleSize"]=p.PageTitleSize*p.FontScale/100;r["UiAlbumTitleSize"]=p.AlbumTitleSize*p.FontScale/100;r["UiLyricsFontSize"]=p.LyricsFontSize*p.FontScale/100;r["UiPlainLyricsFontSize"]=(p.LyricsFontSize-1)*p.FontScale/100;
  r["UiButtonRadius"]=new CornerRadius(p.ButtonRadius);r["UiCardRadius"]=new CornerRadius(p.CardRadius);r["UiCardRadiusValue"]=p.CardRadius;r["UiPanelRadius"]=new CornerRadius(p.PanelRadius);r["UiPlayRadius"]=new CornerRadius(p.PlaySize/2*p.PlayRoundness/100);
  r["UiArtistRadius"]=new CornerRadius(p.CircularArtists?10000:p.CardRadius);r["UiArtistRadiusValue"]=p.CircularArtists?10000d:p.CardRadius;r["UiNavigationHeight"]=p.NavigationHeight;r["UiNavigationMargin"]=new Thickness(0,0,0,p.NavigationGap);
  r["UiPlaySize"]=p.PlaySize;r["UiAlbumButtonHeight"]=p.AlbumButtonHeight;r["UiAlbumColumns"]=p.AlbumColumns;r["UiArtistColumns"]=p.ArtistColumns;r["UiPlaylistColumns"]=p.PlaylistColumns;
  r["UiCardMargin"]=new Thickness(p.CardGap,0,p.CardGap,p.CardGap+14);r["UiCardDetails"]=p.ShowCardDetails?Visibility.Visible:Visibility.Collapsed;r["UiScrollbarWidth"]=p.ScrollbarWidth;
  r["UiShowRatings"]=p.ShowRatings?Visibility.Visible:Visibility.Collapsed;
 }
}
