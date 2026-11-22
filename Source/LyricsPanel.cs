using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Faemyr;
public partial class MainWindow
{
 LyricsService lyrics=null!;CancellationTokenSource? lyricsCts;long lyricsSelection;
 LyricsDocument? lyricsDocument;List<LyricLine> lyricTimeline=[];readonly List<TextBlock> lyricBlocks=[];
 int lyricActive=-1;
 bool? lyricEmphasisFullscreen;
 bool lyricsStatusInPanel=true;
 string lyricsTrackPath="";
 void ApplyLyricsPresentation()
 {
  bool missing=lyricsDocument?.HasLyrics!=true;
  LyricsHeader.Visibility=Visible(!fullscreenPlayer);LyricsStatus.Visibility=Visible(lyricsStatusInPanel&&(!fullscreenPlayer||missing));
  LyricsFetch.Visibility=Visible(fullscreenPlayer?missing:lyricsDocument==null||LyricsService.IsOnline(lyricsDocument));
  LyricsFetch.Content=fullscreenPlayer&&missing?L.T("Şarkı sözlerini bul"):LyricsService.IsOnline(lyricsDocument)?L.T("Yeniden ara"):L.T("İnternette bul");
  LyricsFetch.HorizontalAlignment=fullscreenPlayer?HorizontalAlignment.Left:HorizontalAlignment.Stretch;
  LyricsPlain.Visibility=Visible(lyricTimeline.Count==0&&lyricsDocument!=null);
  if(lyricEmphasisFullscreen!=fullscreenPlayer)
  {
   lyricEmphasisFullscreen=fullscreenPlayer;
   for(int i=0;i<lyricBlocks.Count;i++)LyricEmphasis.Configure(lyricBlocks[i],fullscreenPlayer,i==lyricActive);
  }
 }
 void InitializeLyrics()
 {
  lyrics=new(Path.Combine(Paths.Data,"lyrics"));
  lyricsPreferences=PlayerJson.Read<LyricsPreferences>(LyricsSettingsPath)??new();
 }
 void UpdateLyricsVisibility()=>LyricsCard.Visibility=Visible((fullscreenPlayer?fullscreenLyrics:panelPreferences.ShowLyrics)&&now!=null);
 void CloseLyrics(){SmoothScroll.Cancel(LyricsScroll);lyricsCts?.Cancel();lyrics?.Dispose();}
 async void SelectLyricsTrack(Track track)
 {
  lyricsTrackPath=track.Path;
  lyricsCts?.Cancel();lyricsCts?.Dispose();lyricsCts=new();var ct=lyricsCts.Token;long selection=++lyricsSelection;
  lyricsDocument=null;RenderLyrics(null);SetLyricsStatus(L.T("Yerel sözler kontrol ediliyor…"));LyricsFetch.IsEnabled=false;
  try
  {
   var doc=await lyrics.LocalAsync(track,ct);if(closed||ct.IsCancellationRequested||selection!=lyricsSelection)return;
   lyricsDocument=doc;RenderLyrics(doc);SetLyricsStatus(doc==null?L.T("Dosyada söz yok. İnternette arayabilirsin."):LyricsDescription(doc),doc==null);
  }
  catch(OperationCanceledException){}
  catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){if(selection==lyricsSelection&&!closed)SetLyricsStatus(L.T("Yerel sözler okunamadı. İnternette arayabilirsin."));}
  finally{if(selection==lyricsSelection&&!closed)LyricsFetch.IsEnabled=true;}
 }
 static string LyricsDescription(LyricsDocument doc)=>doc.Source+(doc.Instrumental?L.T(" · Enstrümantal"):doc.Synced.Length>0?L.T(" · Zamanlı sözler"):L.T(" · Düz sözler"));
 async void LyricsFetch_Click(object sender,RoutedEventArgs e)
 {
  if(now is not {} track||lyricsCts==null)return;long selection=lyricsSelection;var ct=lyricsCts.Token;
  LyricsFetch.IsEnabled=false;SetLyricsStatus(L.T("Şarkı sözleri aranıyor…"));
  try
  {
   var doc=await lyrics.FetchAsync(track,ct);if(closed||ct.IsCancellationRequested||selection!=lyricsSelection)return;
   if(doc==null){SetLyricsStatus(L.T("Bu kayıtla eşleşen söz bulunamadı."));return;}
   lyricsDocument=doc;RenderLyrics(doc);SetLyricsStatus(LyricsDescription(doc),false);
   try{await lyrics.SaveAsync(track,doc,ct);}
   catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){if(selection==lyricsSelection&&!closed)SetLyricsStatus(LyricsDescription(doc)+L.T(" · Çevrimdışı kaydedilemedi."));}
  }
  catch(OperationCanceledException){if(!closed&&selection==lyricsSelection&&!ct.IsCancellationRequested)SetLyricsStatus(L.T("Söz servisi zamanında yanıt vermedi. Yeniden deneyebilirsin."));}
  catch(Exception ex)when(ex is IOException or HttpRequestException or JsonException)
  {if(!closed&&selection==lyricsSelection)SetLyricsStatus(ex is HttpRequestException?L.T("İnternet bağlantısı veya söz servisine erişim yok. Yeniden deneyebilirsin."):ex.Message);}
  finally{if(!closed&&selection==lyricsSelection)LyricsFetch.IsEnabled=true;}
 }
 void RenderLyrics(LyricsDocument? doc)
 {
  SmoothScroll.Cancel(LyricsScroll);LyricsLines.Children.Clear();lyricBlocks.Clear();lyricTimeline=Lrc.Parse(doc?.Synced);lyricActive=-2;lyricEmphasisFullscreen=null;
  LyricsPlain.Text=doc?.Instrumental==true?L.T("Bu kayıt enstrümantal olarak işaretlenmiş."):doc?.Plain??"";
  LyricsPlain.Visibility=Visible(lyricTimeline.Count==0&&doc!=null);
  LyricsScroll.Visibility=Visible(lyricTimeline.Count>0);
  LyricsSyncBadge.Visibility=Visible(lyricTimeline.Count>0);
  UpdateLyricsSettingsInfo();
  foreach(var line in lyricTimeline)
  {
   var text=new TextBlock{Text=line.Text.Length>0?line.Text:"♪",TextWrapping=TextWrapping.Wrap,FontSize=17,Margin=new Thickness(0,7,0,7),Tag=line,Cursor=Cursors.Hand,Background=Brushes.Transparent,ToolTip=FormatTime(TimeSpan.FromSeconds(line.Seconds))};
   text.MouseLeftButtonUp+=LyricLine_Click;
   text.SetResourceReference(TextBlock.FontSizeProperty,"UiLyricsFontSize");text.SetResourceReference(TextBlock.ForegroundProperty,"Muted");LyricsLines.Children.Add(text);lyricBlocks.Add(text);
  }
  ApplyLyricsPresentation();
  LyricsScroll.ScrollToTop();LyricsPlain.ScrollToVerticalOffset(0);LyricsPlain.ScrollToHorizontalOffset(0);UpdateLyricsVisibility();
  if(fullscreenPlayer)UpdateFullscreenLayout();
  UpdateLyricsPlayback();
  // The new document is measured after the old viewport has been cleared.
  // Reset again after layout so a shorter/plain document cannot retain the
  // previous track's scroll position or fullscreen centering offset.
  long selection=lyricsSelection;
  Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,()=>
  {
   if(closed||selection!=lyricsSelection)return;
   LyricsScroll.ScrollToTop();LyricsPlain.ScrollToVerticalOffset(0);LyricsPlain.ScrollToHorizontalOffset(0);
   if(fullscreenPlayer){UpdateFullscreenLayout();CenterFullscreenContent(false);}
   lyricActive=-2;UpdateLyricsPlayback();
  });
 }
 void LyricLine_Click(object sender,MouseButtonEventArgs e)
 {
  if(closed||dragging||sender is not TextBlock line)return;
  // A gapless transition may have advanced before the next UI timer tick.
  // Refresh first so a detached row can never seek the following track.
  UpdateStreamTrack();
  if(now==null||now.Path!=lyricsTrackPath||!lyricBlocks.Contains(line)||line.Tag is not LyricLine stamp||!double.IsFinite(stamp.Seconds)||stamp.Seconds<0||!(audio.IsPlaying||audio.IsPaused||resumePrepared))return;
  double duration=PlayerDuration.TotalSeconds;if(!double.IsFinite(duration)||duration<=0)return;
  Seek.Maximum=Math.Max(1,duration);Seek.Value=Math.Clamp(stamp.Seconds,0,duration);
  CommitSeek();UpdateLyricsPlayback();Elapsed.Text=FormatTime(PlayerPosition);UpdateDesktopPlayback();UpdateMiniPlayer();
  e.Handled=true;
 }
 void UpdateLyricsPlayback()
 {
  if(LyricsCard.Visibility!=Visibility.Visible||lyricTimeline.Count==0)return;
  int active=audio.IsPlaying||audio.IsPaused?Lrc.Active(lyricTimeline,audio.Position.TotalSeconds):-1;if(active==lyricActive)return;
  // Reset every previous emphasis, including forced refreshes after a view or
  // follow-setting change, where lyricActive is deliberately invalidated.
  for(int i=0;i<lyricBlocks.Count;i++)if(i!=active&&lyricBlocks[i].FontWeight!=FontWeights.Normal){lyricBlocks[i].SetResourceReference(TextBlock.ForegroundProperty,"Muted");lyricBlocks[i].FontWeight=FontWeights.Normal;LyricEmphasis.SetActive(lyricBlocks[i],false);}
  lyricActive=active;if(active<0)return;var line=lyricBlocks[active];line.SetResourceReference(TextBlock.ForegroundProperty,"LyricsAccent");line.FontWeight=FontWeights.SemiBold;LyricEmphasis.SetActive(line,true);
  if(lyricsPreferences.FollowCurrentLine)
  {
   // Scroll only inside the lyrics viewport, leaving the rest of the player panel still.
   Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,()=>
   {
    if(closed||!lyricsPreferences.FollowCurrentLine||active!=lyricActive||!LyricsLines.Children.Contains(line))return;
    var point=line.TranslatePoint(new Point(),LyricsLines);SmoothScroll.To(LyricsScroll,Math.Max(0,point.Y-LyricsScroll.ViewportHeight*.35));
   });
  }
 }
}

