using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
namespace Ahenk;
public partial class MainWindow
{
 bool fullscreenPlayer,fullscreenLyrics=true;
 Rect playerRestoreBounds;WindowState playerRestoreState;ResizeMode playerRestoreResize;
 double playerRestoreMinWidth,playerRestoreMinHeight;
 WindowChrome? playerRestoreChrome;IInputElement? playerRestoreFocus;
 Panel? lyricsRestoreParent;int lyricsRestoreIndex;Thickness lyricsRestoreMargin;
 Transform lyricsRestoreTransform=Transform.Identity;double lyricsRestoreScrollHeight,lyricsRestorePlainHeight,lyricsRestoreCardHeight;
 Thickness lyricsRestorePadding;
 BitmapSource? fullscreenPaletteSource;string fullscreenPaletteTheme="";Color fullscreenPaletteFallback;
 void InitializeFullscreenPlayer()
 {
  PreviewKeyDown+=FullscreenPlayerKey;FullscreenRoot.SizeChanged+=(_,_)=>UpdateFullscreenLayout();FullscreenTrack.SizeChanged+=(_,_)=>UpdateFullscreenLayout();FullscreenControls.SizeChanged+=(_,_)=>UpdateFullscreenLayout();
  InitializeFullscreenControlsAutoHide();InitializeFullscreenCentering();Closing+=(_,_)=>{if(fullscreenPlayer)ExitFullscreenPlayer();};
 }
 void Fullscreen_Click(object sender,RoutedEventArgs e){if(fullscreenPlayer)ExitFullscreenPlayer();else EnterFullscreenPlayer();}
 void EnterFullscreenPlayer()
 {
  if(fullscreenPlayer||!libraryReady||settingsWindow!=null)return;
  playerRestoreState=WindowState;playerRestoreBounds=WindowState==WindowState.Normal?new Rect(Left,Top,Width,Height):RestoreBounds;
  playerRestoreResize=ResizeMode;playerRestoreMinWidth=MinWidth;playerRestoreMinHeight=MinHeight;playerRestoreChrome=WindowChrome.GetWindowChrome(this);playerRestoreFocus=Keyboard.FocusedElement;
  // Keep the native caption/minimize/maximize styles. WindowChrome hides the
  // frame visually while Windows remains responsible for state transitions.
  fullscreenPlayer=true;MinWidth=640;MinHeight=480;
  WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(0),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
  WindowFrame.BorderThickness=new Thickness(0);
  WindowRoot.Children.Remove(PlayerPanel);FullscreenControls.Content=PlayerPanel;UpdatePlayerBoxAlignment();
  lyricsRestoreParent=LyricsCard.Parent as Panel;lyricsRestoreIndex=lyricsRestoreParent?.Children.IndexOf(LyricsCard)??0;lyricsRestoreMargin=LyricsCard.Margin;lyricsRestoreTransform=LyricsCard.LayoutTransform;lyricsRestoreScrollHeight=LyricsScroll.MaxHeight;lyricsRestorePlainHeight=LyricsPlain.MaxHeight;lyricsRestoreCardHeight=LyricsCard.MaxHeight;
  lyricsRestorePadding=LyricsCard.Padding;lyricsRestoreParent?.Children.Remove(LyricsCard);FullscreenLyricsHost.Content=LyricsCard;LyricsCard.Margin=new Thickness(0);LyricsCard.Padding=new Thickness(0);LyricsCard.PlainPresentation=true;ScrollEdgeFade.SetEnabled(LyricsScroll,true);ScrollEdgeFade.SetEnabled(LyricsPlain,true);ApplyLyricsPresentation();
  TitleBar.Visibility=WindowRoot.Visibility=Visibility.Collapsed;FullscreenRoot.Visibility=Visibility.Visible;UpdateFullscreenLayout();UpdateLyricsVisibility();lyricActive=-2;UpdateLyricsPlayback();FullscreenRoot.Focus();
  FullscreenIcon.Icon="fullscreen-exit";FullscreenButton.ToolTip=L.T("Tam ekrandan çık (Esc / F11)");UpdateFullscreenAmbience();StartFullscreenControlsAutoHide();
  if(WindowState==WindowState.Maximized)RefreshMaximizedFrame();else SystemCommands.MaximizeWindow(this);
 }
 void ExitFullscreenPlayer()
 {
  if(!fullscreenPlayer)return;ResetFullscreenArtworkMotion();fullscreenPlayer=false;StopFullscreenControlsAutoHide();StopFullscreenAmbience();SmoothScroll.Cancel(LyricsScroll);ScrollEdgeFade.SetEnabled(LyricsScroll,false);ScrollEdgeFade.SetEnabled(LyricsPlain,false);
  FullscreenControls.Content=null;WindowRoot.Children.Add(PlayerPanel);FullscreenLyricsHost.Content=null;UpdatePlayerBoxAlignment();
  if(lyricsRestoreParent!=null)lyricsRestoreParent.Children.Insert(Math.Clamp(lyricsRestoreIndex,0,lyricsRestoreParent.Children.Count),LyricsCard);
  LyricsCard.Margin=lyricsRestoreMargin;LyricsCard.LayoutTransform=lyricsRestoreTransform;LyricsScroll.MaxHeight=lyricsRestoreScrollHeight;LyricsPlain.MaxHeight=lyricsRestorePlainHeight;LyricsCard.MaxHeight=lyricsRestoreCardHeight;LyricsCard.Padding=lyricsRestorePadding;LyricsCard.PlainPresentation=false;ApplyLyricsPresentation();
  FullscreenRoot.Visibility=Visibility.Collapsed;TitleBar.Visibility=WindowRoot.Visibility=Visibility.Visible;
  MinWidth=playerRestoreMinWidth;MinHeight=playerRestoreMinHeight;ResizeMode=playerRestoreResize;WindowChrome.SetWindowChrome(this,playerRestoreChrome);
  if(playerRestoreState==WindowState.Maximized){if(WindowState==WindowState.Maximized)RefreshMaximizedFrame();else SystemCommands.MaximizeWindow(this);}
  else{SystemCommands.RestoreWindow(this);if(!playerRestoreBounds.IsEmpty){Left=playerRestoreBounds.Left;Top=playerRestoreBounds.Top;Width=playerRestoreBounds.Width;Height=playerRestoreBounds.Height;}}
  WindowFrame.BorderThickness=new Thickness(playerRestoreState==WindowState.Maximized?0:1);FullscreenIcon.Icon="fullscreen";FullscreenButton.ToolTip=L.T("Tam ekran oynatıcı (F11)");UpdateLyricsVisibility();
  if(playerRestoreFocus is DependencyObject node&&node is UIElement element&&element.IsVisible&&element.IsEnabled)Keyboard.Focus(playerRestoreFocus);else FullscreenButton.Focus();
 }
 void FullscreenLyrics_Click(object sender,RoutedEventArgs e)
 {
  var previous=FullscreenCover.TransformToAncestor(FullscreenRoot).TransformBounds(new Rect(FullscreenCover.RenderSize));
  ResetFullscreenArtworkMotion();fullscreenLyrics=!fullscreenLyrics;UpdateFullscreenLayout();UpdateLyricsVisibility();lyricActive=-2;UpdateLyricsPlayback();
  FullscreenRoot.UpdateLayout();CenterFullscreenContent(false);AnimateFullscreenArtworkLayout(previous);
 }
 void UpdateFullscreenLayout()
 {
  if(!fullscreenPlayer)return;double width=FullscreenRoot.ActualWidth>0?FullscreenRoot.ActualWidth:Width,height=FullscreenRoot.ActualHeight>0?FullscreenRoot.ActualHeight:Height;
  bool showLyrics=fullscreenLyrics&&now!=null;FullscreenLyricsHost.Visibility=Visible(showLyrics);FullscreenBody.ColumnDefinitions[1].Width=showLyrics?new GridLength(1,GridUnitType.Star):new GridLength(0);
  FullscreenControls.MinHeight=Math.Clamp(InterfaceSettings.Current.PlayerHeight+40,160,240);double playerHeight=Math.Max(FullscreenControls.MinHeight,FullscreenControls.ActualHeight);double bodyHeight=Math.Max(180,height-74-playerHeight-32);FullscreenTrack.Width=Math.Min(showLyrics?width*.43:width-100,760);
  double informationHeight=FullscreenTrack.ActualHeight>0?FullscreenTrack.ActualHeight+48:200*InterfaceSettings.Current.FontScale/100;
  double size=Math.Clamp(Math.Min(bodyHeight-informationHeight,(showLyrics?width*.39:width*.60)-80),140,580);FullscreenCover.Width=FullscreenCover.Height=size;
  double lyricScale=Math.Clamp(width/1500,1.1,1.45)*1.35;LyricsCard.LayoutTransform=new ScaleTransform(lyricScale,lyricScale);LyricsCard.MaxHeight=Math.Max(100,bodyHeight/lyricScale);LyricsScroll.MaxHeight=LyricsPlain.MaxHeight=Math.Max(80,bodyHeight/lyricScale-24);
  FullscreenLyricsToggle.SetResourceReference(Control.ForegroundProperty,showLyrics?"Accent":"Muted");
  UpdateFullscreenPalette();
 }
 void UpdateFullscreenPalette()
 {
  var image=(PanelCover.Background as ImageBrush)?.ImageSource as BitmapSource;var fallback=((SolidColorBrush)FindResource("Canvas")).Color;if(ReferenceEquals(image,fullscreenPaletteSource)&&fullscreenPaletteTheme==Themes.Current&&fullscreenPaletteFallback==fallback)return;fullscreenPaletteSource=image;fullscreenPaletteTheme=Themes.Current;fullscreenPaletteFallback=fallback;
  FullscreenColorBackdrop.Background=FullscreenPalette.Create(image,Themes.Current=="light",fallback);
 }
 void FullscreenPlayerKey(object sender,KeyEventArgs e)
 {
  if(e.Key==Key.F11&&Keyboard.Modifiers==ModifierKeys.None){Fullscreen_Click(this,new RoutedEventArgs());e.Handled=true;return;}
  if(!fullscreenPlayer)return;
  if(e.Key==Key.Escape){ExitFullscreenPlayer();e.Handled=true;return;}
  if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key is Key.Left or Key.Right){QueueDesktopAction(e.Key==Key.Left?DesktopMediaAction.Previous:DesktopMediaAction.Next);e.Handled=true;return;}
  if(Keyboard.FocusedElement is ButtonBase or Slider or TextBoxBase or Hyperlink)return;
  if(e.Key==Key.Space&&Keyboard.Modifiers==ModifierKeys.None){QueueDesktopAction(DesktopMediaAction.Toggle);e.Handled=true;}
 }
}
