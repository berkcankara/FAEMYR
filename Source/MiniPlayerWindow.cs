using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Shell;
namespace Faemyr;
public sealed class MiniPlayerWindow : Window
{
 readonly ThumbnailImage cover=new(){Width=76,Height=76,Radius=10,Stretch=Stretch.UniformToFill};
 readonly ThumbnailImage largeCover=new(){Radius=15,Stretch=Stretch.UniformToFill,DecodePixelWidth=1024};
 readonly MarqueeText title=new(){FontWeight=FontWeights.SemiBold,FontSize=15};
 public ArtistLinks Artist{get;}=new(){FontSize=12,TextWrapping=TextWrapping.NoWrap,MaxHeight=24,Margin=new Thickness(0,5,0,0)};
 readonly Slider seek=new(){Minimum=0,Maximum=1,IsMoveToPointEnabled=true,Height=20};
 readonly Slider volume=new(){Minimum=0,Maximum=100,IsMoveToPointEnabled=true,Width=78};
 readonly SvgIcon playIcon=new(){Icon="play",Width=21,Height=21},volumeIcon=new(){Icon="volume",Width=16,Height=16},pinIcon=new(){Icon="pin",Width=14,Height=14},shuffleIcon=new(){Icon="shuffle",Width=16,Height=16},repeatIcon=new(){Icon="repeat",Width=16,Height=16};
 readonly TextBlock repeatOne=new(){Text="1",FontSize=8,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};
 readonly Ellipse shuffleActiveDot=ModeDot(),repeatActiveDot=ModeDot();
 readonly Button mute,shuffleButton,repeatButton,expandButton,shrinkButton;readonly Border footer;readonly Grid art,root;readonly ColumnDefinition coverColumn;readonly StackPanel tools,info;readonly ThumbButtonInfo taskPlay;
 readonly Grid frostedFooter=new(){VerticalAlignment=VerticalAlignment.Bottom,Height=280,ClipToBounds=true,IsHitTestVisible=false,Visibility=Visibility.Collapsed};
 readonly Rectangle frostBackdrop=new(){Effect=new BlurEffect{Radius=22,RenderingBias=RenderingBias.Performance}};
 readonly Action<double> seekTo,changeVolume;readonly Action toggleMute;bool updating,seeking,keyboardNavigation;string? artwork;double compactWidth=430,compactHeight=196,expandedWidth=380,expandedHeight=380;
 public bool ArtworkExpanded{get;private set;}
 public MiniPlayerWindow(Func<DesktopMediaAction,Task> action,Action restore,Action<bool> pin,Action<double> seekTo,Action<double> changeVolume,Action toggleMute)
 {
  this.seekTo=seekTo;this.changeVolume=changeVolume;this.toggleMute=toggleMute;
  SetResourceReference(FontFamilyProperty,"UiFontFamily");Title="Faemyr — "+L.T("Mini oynatıcı");Width=430;Height=196;MinWidth=350;MinHeight=190;MaxWidth=1000;MaxHeight=1000;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=true;
  var shell=new Border{CornerRadius=new CornerRadius(16),BorderThickness=new Thickness(1)};shell.SetResourceReference(Border.BackgroundProperty,"Surface");shell.SetResourceReference(Border.BorderBrushProperty,"Line");Content=shell;
  root=new Grid{ClipToBounds=true};RoundedClip.SetRadius(root,15);shell.Child=root;largeCover.Visibility=Visibility.Collapsed;root.Children.Add(largeCover);
  root.SizeChanged+=(_,_)=>UpdateFrostedBackdrop();
  var glassFade=new GradientStopCollection();for(int i=0;i<=24;i++){double t=i/24d;byte alpha=(byte)Math.Round(255*(1-Math.Cos(Math.PI*t))/2);glassFade.Add(new(Color.FromArgb(alpha,255,255,255),t));}
  frostedFooter.OpacityMask=new LinearGradientBrush(glassFade,new Point(0,0),new Point(0,110)){MappingMode=BrushMappingMode.Absolute};frostedFooter.Children.Add(frostBackdrop);frostedFooter.Children.Add(new Border{Background=new SolidColorBrush(Color.FromArgb(178,42,45,52))});root.Children.Add(frostedFooter);
  footer=new Border{Padding=new Thickness(14)};root.Children.Add(footer);var grid=new Grid();grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});footer.Child=grid;
  var header=new Grid{Margin=new Thickness(0,0,0,8)};coverColumn=new(){Width=new GridLength(76)};header.ColumnDefinitions.Add(coverColumn);header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.Children.Add(header);
  art=new Grid{VerticalAlignment=VerticalAlignment.Center};var fallback=new SvgIcon{Icon="music",Width=32,Height=32};fallback.SetResourceReference(SvgIcon.BrushProperty,"Muted");art.Children.Add(fallback);art.Children.Add(cover);header.Children.Add(art);
  expandButton=IconButton("fullscreen",L.T("Kapağı büyüt"),30);expandButton.Opacity=0;expandButton.SetResourceReference(Control.BackgroundProperty,"Surface");expandButton.Click+=(_,_)=>SetArtworkExpanded(true);art.Children.Add(expandButton);
  art.MouseEnter+=(_,_)=>Fade(expandButton,1);art.MouseLeave+=(_,_)=>{if(!expandButton.IsKeyboardFocused)Fade(expandButton,0);};expandButton.GotKeyboardFocus+=(_,_)=>Fade(expandButton,1);expandButton.LostKeyboardFocus+=(_,_)=>{if(!art.IsMouseOver)Fade(expandButton,0);};
  info=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,12,52,0)};title.SetResourceReference(Control.ForegroundProperty,"Ink");Artist.SetResourceReference(TextBlock.ForegroundProperty,"Muted");info.Children.Add(title);info.Children.Add(Artist);Grid.SetColumn(info,1);header.Children.Add(info);
  tools=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,12,12,0)};root.Children.Add(tools);
  var pinButton=IconButton("pin",L.T("Her zaman üstte"),24);pinButton.Content=pinIcon;pinIcon.SetResourceReference(SvgIcon.BrushProperty,"Muted");pinButton.Click+=(_,_)=>{Topmost=!Topmost;pin(Topmost);SyncPin();};tools.Children.Add(pinButton);
  var restoreButton=IconButton("fullscreen-exit",L.T("Ana pencereye dön"),24);restoreButton.Click+=(_,_)=>restore();tools.Children.Add(restoreButton);
  shrinkButton=IconButton("fullscreen-exit",L.T("Kapağı küçült"),30);shrinkButton.HorizontalAlignment=HorizontalAlignment.Left;shrinkButton.VerticalAlignment=VerticalAlignment.Top;shrinkButton.Margin=new Thickness(12);shrinkButton.Visibility=Visibility.Collapsed;shrinkButton.Click+=(_,_)=>SetArtworkExpanded(false);root.Children.Add(shrinkButton);
  var controls=new Grid();controls.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});controls.ColumnDefinitions.Add(new(){Width=GridLength.Auto});Grid.SetRow(controls,1);grid.Children.Add(controls);
  var transport=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};controls.Children.Add(transport);
  shuffleButton=IconButton("shuffle",L.T("Karışık çalma"),28);var shuffle=new Grid{Width=24,Height=26};shuffle.Children.Add(shuffleIcon);shuffle.Children.Add(shuffleActiveDot);shuffleButton.Content=shuffle;shuffleButton.Click+=async(_,_)=>await action(DesktopMediaAction.Shuffle);transport.Children.Add(shuffleButton);
  var previous=IconButton("previous",L.T("Önceki parça"),30);previous.Click+=async(_,_)=>await action(DesktopMediaAction.Previous);transport.Children.Add(previous);
  var play=IconButton("play",L.T("Oynat / duraklat"),42);play.Margin=new Thickness(5,0,5,0);play.SetResourceReference(Control.BackgroundProperty,"Accent");playIcon.SetResourceReference(SvgIcon.BrushProperty,"OnAccent");play.Content=playIcon;play.Click+=async(_,_)=>await action(DesktopMediaAction.Toggle);transport.Children.Add(play);
  var next=IconButton("next",L.T("Sonraki parça"),30);next.Click+=async(_,_)=>await action(DesktopMediaAction.Next);transport.Children.Add(next);
  repeatButton=IconButton("repeat",L.T("Tekrar"),28);var repeat=new Grid{Width=24,Height=26};repeat.Children.Add(repeatIcon);repeatOne.SetResourceReference(TextBlock.ForegroundProperty,"Accent");repeat.Children.Add(repeatOne);repeat.Children.Add(repeatActiveDot);repeatButton.Content=repeat;repeatButton.Click+=async(_,_)=>await action(DesktopMediaAction.Repeat);transport.Children.Add(repeatButton);
  var sound=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,0,0)};Grid.SetColumn(sound,1);controls.Children.Add(sound);mute=IconButton("volume",L.T("Sessize al"),28);mute.Content=volumeIcon;volumeIcon.SetResourceReference(SvgIcon.BrushProperty,"Muted");mute.Click+=(_,_)=>this.toggleMute();sound.Children.Add(mute);volume.SetResourceReference(StyleProperty,"ModernSeek");volume.ToolTip=L.T("Ses seviyesi");sound.Children.Add(volume);
  seek.SetResourceReference(StyleProperty,"ModernSeek");seek.Margin=new Thickness(0,6,0,0);seek.ToolTip=L.T("Akış çubuğu");Grid.SetRow(seek,2);grid.Children.Add(seek);
  volume.ValueChanged+=(_,_)=>{if(!updating)this.changeVolume(volume.Value);};seek.PreviewMouseLeftButtonDown+=(_,_)=>seeking=true;seek.LostMouseCapture+=(_,_)=>CommitSeek();seek.PreviewMouseLeftButtonUp+=(_,_)=>Dispatcher.BeginInvoke(CommitSeek);seek.PreviewKeyUp+=(_,e)=>{if(e.Key is Key.Left or Key.Right or Key.Home or Key.End)this.seekTo(seek.Value);};
  root.MouseLeftButtonDown+=(_,e)=>{if(Interactive(e.OriginalSource))return;if(e.ClickCount==2){if(ArtworkExpanded)SetArtworkExpanded(false);else restore();return;}if(e.ButtonState==MouseButtonState.Pressed)DragMove();};
  root.PreviewMouseDown+=(_,_)=>keyboardNavigation=false;root.MouseEnter+=(_,_)=>ShowArtworkControls(true);root.MouseLeave+=(_,_)=>{if(!keyboardNavigation)ShowArtworkControls(false);};GotKeyboardFocus+=(_,_)=>ShowArtworkControls(true);LostKeyboardFocus+=(_,_)=>{if(!root.IsMouseOver)ShowArtworkControls(false);};
  PreviewKeyDown+=async(_,e)=>{if(e.Key==Key.Tab){keyboardNavigation=true;ShowArtworkControls(true);}if(e.Key==Key.Escape||e.Key==Key.M&&Keyboard.Modifiers==(ModifierKeys.Control|ModifierKeys.Shift)){e.Handled=true;if(ArtworkExpanded&&e.Key==Key.Escape)SetArtworkExpanded(false);else restore();}else if(e.Key==Key.Space&&Keyboard.FocusedElement is not Slider){e.Handled=true;await action(DesktopMediaAction.Toggle);}};
  AddResizeGrips();SizeChanged+=(_,_)=>UpdateLayoutSizes();
  var taskbar=new TaskbarItemInfo();TaskbarItemInfo=taskbar;foreach(var command in new[]{DesktopMediaAction.Previous,DesktopMediaAction.Toggle,DesktopMediaAction.Next}){var button=new ThumbButtonInfo{Description=L.T(command==DesktopMediaAction.Previous?"Önceki parça":command==DesktopMediaAction.Next?"Sonraki parça":"Oynat / duraklat"),ImageSource=SvgIcon.ShellImage(command==DesktopMediaAction.Previous?"previous":command==DesktopMediaAction.Next?"next":"play"),DismissWhenClicked=false};button.Click+=async(_,_)=>await action(command);taskbar.ThumbButtonInfos.Add(button);}taskPlay=taskbar.ThumbButtonInfos[1];SyncModes(false,RepeatMode.Off);UpdateLayoutSizes();
 }
 static Button IconButton(string icon,string hint,double size){var image=new SvgIcon{Icon=icon,Width=size>30?18:14,Height=size>30?18:14};image.SetResourceReference(SvgIcon.BrushProperty,"Ink");var button=new Button{Content=image,ToolTip=hint,Width=size,Height=size,Margin=new Thickness(0),Padding=new Thickness(0),Style=(Style)Application.Current.FindResource("TransportButton"),FocusVisualStyle=null};System.Windows.Automation.AutomationProperties.SetName(button,hint);return button;}
 static Ellipse ModeDot(){var dot=new Ellipse{Width=4,Height=4,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Visibility=Visibility.Hidden,IsHitTestVisible=false};dot.SetResourceReference(Shape.FillProperty,"Accent");return dot;}
 static bool Interactive(object source){for(DependencyObject? node=source as DependencyObject;node!=null;node=node is FrameworkContentElement content?content.Parent:node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))if(node is ButtonBase or Slider or Thumb or System.Windows.Documents.Hyperlink or ArtistLinks)return true;return false;}
 static void Fade(UIElement view,double opacity){view.BeginAnimation(OpacityProperty,new DoubleAnimation(opacity,TimeSpan.FromMilliseconds(160)));}
 void ShowArtworkControls(bool visible){if(!ArtworkExpanded)return;title.MotionEnabled=visible;Fade(frostedFooter,visible?1:0);Fade(footer,visible?1:0);Fade(tools,visible?1:0);Fade(shrinkButton,visible?1:0);}
 void CommitSeek(){if(!seeking)return;seeking=false;seekTo(seek.Value);}
 void RememberSize(){if(ArtworkExpanded){expandedWidth=Width;expandedHeight=Height;}else{compactWidth=Width;compactHeight=Height;}}
 public void RestorePreferences(DesktopPlayerPreferences p){p.Validate();compactWidth=p.CompactWidth;compactHeight=p.CompactHeight;expandedWidth=p.ExpandedWidth;expandedHeight=p.ExpandedHeight;Width=compactWidth;Height=compactHeight;if(p.CoverExpanded)SetArtworkExpanded(true);}
 public void CapturePreferences(DesktopPlayerPreferences p){RememberSize();p.CompactWidth=compactWidth;p.CompactHeight=compactHeight;p.ExpandedWidth=expandedWidth;p.ExpandedHeight=expandedHeight;p.CoverExpanded=ArtworkExpanded;}
 public void SetArtworkExpanded(bool expanded)
 {
  if(ArtworkExpanded==expanded)return;RememberSize();ArtworkExpanded=expanded;
  if(expanded){Resources["Ink"]=Brushes.White;Resources["Muted"]=new SolidColorBrush(Color.FromRgb(236,238,244));MinWidth=260;MinHeight=260;Width=Height=expandedWidth;expandedHeight=expandedWidth;}
  else{Resources.Remove("Ink");Resources.Remove("Muted");MinWidth=350;MinHeight=190;Width=compactWidth;Height=compactHeight;}
  art.Visibility=expanded?Visibility.Collapsed:Visibility.Visible;largeCover.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;largeCover.Path=expanded?artwork:null;coverColumn.Width=new GridLength(expanded?0:76);
  info.Margin=expanded?new Thickness(0,0,0,0):new Thickness(12,12,52,0);shrinkButton.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;
  footer.VerticalAlignment=expanded?VerticalAlignment.Bottom:VerticalAlignment.Stretch;footer.Height=expanded?180:double.NaN;footer.Background=null;
  frostedFooter.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;info.Effect=expanded?new DropShadowEffect{Color=Colors.Black,BlurRadius=5,ShadowDepth=1,Opacity=.7}:null;
  foreach(var view in new UIElement[]{frostedFooter,footer,tools,shrinkButton}){view.BeginAnimation(OpacityProperty,null);view.Opacity=expanded&&!root.IsMouseOver?0:1;}
  title.MotionEnabled=!expanded||root.IsMouseOver;
  SyncPin();UpdateLayoutSizes();
 }
 void UpdateLayoutSizes()
 {
  title.FontSize=Math.Clamp(ActualWidth/28,15,22);Artist.FontSize=Math.Clamp(ActualWidth/35,12,16);
  volume.Visibility=ActualWidth>0&&ActualWidth<350?Visibility.Collapsed:Visibility.Visible;
  if(!ArtworkExpanded){double size=Math.Clamp(Math.Min(ActualWidth*.2,ActualHeight-110),76,128);cover.Width=cover.Height=size;coverColumn.Width=new GridLength(size);}
  UpdateFrostedBackdrop();
 }
 void UpdateFrostedBackdrop()
 {
  if(!ArtworkExpanded||root.ActualWidth<=0||root.ActualHeight<=0)return;
  frostBackdrop.Fill=new VisualBrush(largeCover){ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,root.ActualHeight-frostedFooter.Height,root.ActualWidth,frostedFooter.Height),Stretch=Stretch.Fill};
 }
 void ResizeCorner(int x,int y,double dx,double dy)
 {
  double width,height;
  if(ArtworkExpanded){double horizontal=x*dx,vertical=y*dy,delta=Math.Abs(horizontal)>=Math.Abs(vertical)?horizontal:vertical;double side=Math.Clamp(Width+delta,MinWidth,MaxWidth);width=height=side;}
  else{width=Math.Clamp(Width+x*dx,MinWidth,MaxWidth);height=Math.Clamp(Height+y*dy,MinHeight,MaxHeight);}
  if(x<0)Left-=width-Width;if(y<0)Top-=height-Height;Width=width;Height=height;
 }
 void AddResizeGrips()
 {
  foreach(var edge in new[]{(-1,-1),(1,-1),(-1,1),(1,1)})
  {
   int x=edge.Item1,y=edge.Item2;var fill=new FrameworkElementFactory(typeof(Border));fill.SetValue(Border.BackgroundProperty,Brushes.Transparent);var grip=new Thumb{Opacity=0,Template=new ControlTemplate(typeof(Thumb)){VisualTree=fill},Cursor=x==y?Cursors.SizeNWSE:Cursors.SizeNESW,HorizontalAlignment=x<0?HorizontalAlignment.Left:HorizontalAlignment.Right,VerticalAlignment=y<0?VerticalAlignment.Top:VerticalAlignment.Bottom,Width=14,Height=14};
   System.Windows.Automation.AutomationProperties.SetName(grip,L.T("Pencereyi boyutlandır"));grip.DragDelta+=(_,e)=>ResizeCorner(x,y,e.HorizontalChange,e.VerticalChange);Panel.SetZIndex(grip,100);root.Children.Add(grip);
  }
 }
 public void SyncPin()=>pinIcon.SetResourceReference(SvgIcon.BrushProperty,Topmost?"Accent":"Muted");
 (bool Shuffle,RepeatMode Repeat,string Language)? displayedModes;
 public void SyncModes(bool shuffle,RepeatMode repeat)
 {
  var modes=(shuffle,repeat,L.Current);if(displayedModes==modes)return;displayedModes=modes;
  shuffleIcon.SetResourceReference(SvgIcon.BrushProperty,shuffle?"Accent":"Muted");repeatIcon.SetResourceReference(SvgIcon.BrushProperty,repeat==RepeatMode.Off?"Muted":"Accent");repeatOne.Visibility=repeat==RepeatMode.One?Visibility.Visible:Visibility.Collapsed;
  shuffleActiveDot.Visibility=shuffle?Visibility.Visible:Visibility.Hidden;repeatActiveDot.Visibility=repeat!=RepeatMode.Off?Visibility.Visible:Visibility.Hidden;
  System.Windows.Automation.AutomationProperties.SetItemStatus(shuffleButton,L.T(shuffle?"açık":"kapalı"));System.Windows.Automation.AutomationProperties.SetItemStatus(repeatButton,L.T(repeat==RepeatMode.Off?"kapalı":repeat==RepeatMode.All?"tüm kuyruk":"tek parça"));
  shuffleButton.ToolTip=L.T("Karışık çalma: ")+(shuffle?L.T("açık"):L.T("kapalı"));repeatButton.ToolTip=L.T("Tekrar: ")+(repeat==RepeatMode.Off?L.T("kapalı"):repeat==RepeatMode.All?L.T("tüm kuyruk"):L.T("tek parça"));
 }
 public void Sync(Track? track,bool playing,TimeSpan position,TimeSpan duration,double level,bool muted,bool dop)
 {
  updating=true;try{title.Text=track?.Title??L.T("Henüz çalan parça yok");title.ToolTip=title.Text;Artist.Names=track?.Artist??"";cover.Path=artwork=track?.Cover;largeCover.Path=ArtworkExpanded?artwork:null;playIcon.Icon=playing?"pause":"play";taskPlay.ImageSource=SvgIcon.ShellImage(playIcon.Icon);Title="Faemyr — "+(track?.Title??L.T("Mini oynatıcı"));if(!seeking){seek.Maximum=Math.Max(1,duration.TotalSeconds);seek.Value=Math.Clamp(position.TotalSeconds,0,seek.Maximum);}seek.IsEnabled=track!=null;volume.IsEnabled=mute.IsEnabled=!dop;volume.Value=level*100;volumeIcon.Icon=muted||level==0?"muted":"volume";}finally{updating=false;}
 }
}
