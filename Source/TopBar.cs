using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Faemyr;
public partial class MainWindow
{
 readonly DispatcherTimer topSearchDelay=new(){Interval=TimeSpan.FromMilliseconds(240)};
 bool topBarReady,topSearchExpanded,topSearchAnimating;int topSearchGeneration,detailsAnimationGeneration;
 void InitializeTopBar()
 {
  topBarReady=true;
  TopSearchHost.MouseEnter+=(_,_)=>{topSearchDelay.Stop();SetTopSearchExpanded(true);};
  TopSearchHost.MouseLeave+=(_,_)=>{topSearchDelay.Stop();topSearchDelay.Start();};
  Search.GotKeyboardFocus+=(_,_)=>{topSearchDelay.Stop();SetTopSearchExpanded(true);};
  Search.LostKeyboardFocus+=(_,_)=>{topSearchDelay.Stop();topSearchDelay.Start();};
  Search.PreviewMouseLeftButtonDown+=(_,_)=>SetTopSearchExpanded(true);
  Search.TextChanged+=(_,_)=>UpdateTopBarState();
  topSearchDelay.Tick+=(_,_)=>{topSearchDelay.Stop();if(!TopSearchHost.IsMouseOver&&!Search.IsKeyboardFocusWithin)SetTopSearchExpanded(false);};
  TitleBar.SizeChanged+=(_,_)=>UpdateTopBarBounds();TopNavigation.SizeChanged+=(_,_)=>UpdateTopBarBounds();TopWindowControls.SizeChanged+=(_,_)=>UpdateTopBarBounds();
  SidebarBorder.SizeChanged+=(_,_)=>UpdateTopBarBounds();
  PreviewKeyDown+=(_,e)=>
  {
   if(e.Handled||fullscreenPlayer||settingsWindow!=null)return;
   var key=e.Key==Key.System?e.SystemKey:e.Key;
   if(key==Key.F&&Keyboard.Modifiers==ModifierKeys.Control){SetTopSearchExpanded(true);Search.Focus();Search.SelectAll();e.Handled=true;}
   else if(key==Key.Escape&&Keyboard.Modifiers==ModifierKeys.None&&Search.IsKeyboardFocusWithin){Search.Clear();Keyboard.ClearFocus();BackButton.Focus();SetTopSearchExpanded(false);e.Handled=true;}
   else if(key==Key.Left&&Keyboard.Modifiers==ModifierKeys.Alt){NavigateBack();e.Handled=true;}
  };
  PreviewMouseUp+=(_,e)=>{if(e.ChangedButton==MouseButton.XButton1&&settingsWindow==null&&!fullscreenPlayer){NavigateBack();e.Handled=true;}};
  Closed+=(_,_)=>{++topSearchGeneration;++detailsAnimationGeneration;topSearchDelay.Stop();Search.BeginAnimation(WidthProperty,null);BeginAnimation(DetailsRevealWidthProperty,null);};
  UpdateTopBarState();
 }
 void SetTopSearchExpanded(bool expanded,bool animate=true)
 {
  int generation=++topSearchGeneration;
  topSearchExpanded=expanded;UpdateSearchPrompt();
  double current=Search.Width,target=expanded?Math.Max(CompactSearchWidth(),Math.Clamp(TitleBar.ActualWidth*.25,220,300)):CompactSearchWidth();
  topSearchAnimating=true;
  Search.BeginAnimation(WidthProperty,null);Search.Width=current;
  if(!animate||!SystemParameters.ClientAreaAnimation||Math.Abs(current-target)<.2){Search.Width=target;topSearchAnimating=false;UpdateTopBarBounds();return;}
  var transition=new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(210)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.HoldEnd};
  transition.Completed+=(_,_)=>{if(generation!=topSearchGeneration)return;Search.BeginAnimation(WidthProperty,null);Search.Width=target;topSearchAnimating=false;UpdateTopBarBounds();};
  Search.BeginAnimation(WidthProperty,transition,HandoffBehavior.SnapshotAndReplace);
 }
 double CompactSearchWidth()
 {
  double sidebarWidth=WindowRoot.ColumnDefinitions[0].ActualWidth;
  if(sidebarWidth<=0)sidebarWidth=InterfaceSettings.Current.SidebarWidth;
  return Math.Max(80,sidebarWidth-SidebarBorder.Margin.Right-TopNavigation.Margin.Left-BackButton.Width-TopSearchHost.Margin.Left);
 }
 void UpdateTopBarBounds()
 {
  if(!topBarReady)return;
  if(!topSearchExpanded&&!topSearchAnimating)Search.Width=CompactSearchWidth();
  double reserve=Math.Max(TopNavigation.ActualWidth+TopNavigation.Margin.Left,TopWindowControls.ActualWidth);
  TopNowPlaying.Width=Math.Max(0,TitleBar.ActualWidth-2*reserve-32);
  UpdateSearchFilterIndicator();
 }
 void UpdateSearchFilterIndicator()=>SearchFilterDot.Visibility=Visible(Search.Text.Length>0&&!topSearchExpanded);
 void UpdateTopBarState()
 {
  if(!topBarReady)return;
  UpdateSearchPrompt();
  BackButton.ToolTip=L.T("Geri")+" · Alt+←";AutomationProperties.SetName(BackButton,L.T("Geri"));BackButton.IsEnabled=navigationHistory.Count>0&&settingsWindow==null;
  bool shown=InterfaceSettings.Current.ShowRightPanel;
  string panelLabel=L.T(shown?"Sağ paneli gizle":"Sağ paneli göster");ToggleDetailsButton.ToolTip=panelLabel;AutomationProperties.SetName(ToggleDetailsButton,panelLabel);ToggleDetailsGlyph.PanelShown=shown;
  TopNowPlaying.Visibility=Visible(!shown&&now!=null);TopNowPlaying.ToolTip=now==null?null:now.Title+"\n"+now.Artist;
  UpdateTopBarBounds();
 }

 public static readonly DependencyProperty DetailsRevealWidthProperty=DependencyProperty.Register("DetailsRevealWidth",typeof(double),typeof(MainWindow),new PropertyMetadata(340d,(d,e)=>((MainWindow)d).DetailsColumn.Width=new GridLength(Math.Max(0,(double)e.NewValue))));
 bool detailsSliding;
 void ToggleDetails_Click(object sender,RoutedEventArgs e)
 {
  var preferences=InterfaceSettings.Current;
  if(preferences.ShowRightPanel&&!detailsSliding){SavePanelPreferences();preferences.RightPanelWidth=panelPreferences.Width;}
  preferences.ShowRightPanel=!preferences.ShowRightPanel;
  ApplyDetailsPanelLayout(preferences.ShowRightPanel,true);
  try{preferences.Save(InterfaceSettings.Path);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Status.Text=ex.Message;}
 }
 void ApplyDetailsPanelLayout(bool show,bool animate=false)
 {
  if(DetailPanel==null)return;
  int generation=++detailsAnimationGeneration;
  double current=DetailsColumn.ActualWidth;
  BeginAnimation(DetailsRevealWidthProperty,null);detailsSliding=false;
  double maximum=Math.Clamp((ActualWidth>0?ActualWidth:Width)-InterfaceSettings.Current.SidebarWidth-500,280,640);DetailsColumn.MaxWidth=maximum;
  double target=show?Math.Clamp(InterfaceSettings.Current.RightPanelWidth??panelPreferences.Width,280,maximum):0;
  DetailsColumn.MinWidth=0;DetailsSplitter.Visibility=Visibility.Collapsed;
  if(!show&&DetailPanel.IsKeyboardFocusWithin)ToggleDetailsButton.Focus();
  UpdateTopBarState();
  void Complete()
  {
   if(generation!=detailsAnimationGeneration)return;
   detailsSliding=false;BeginAnimation(DetailsRevealWidthProperty,null);SetValue(DetailsRevealWidthProperty,target);
   DetailPanel.Width=double.NaN;DetailPanel.HorizontalAlignment=HorizontalAlignment.Stretch;
   DetailPanel.Visibility=DetailsSplitter.Visibility=Visible(show);DetailPanel.IsHitTestVisible=show;DetailsColumn.MinWidth=show?280:0;UpdatePanelScale();
  }
  if(!animate||!IsLoaded||!SystemParameters.ClientAreaAnimation||Math.Abs(current-target)<.2){Complete();return;}
  detailsSliding=true;DetailPanel.Visibility=Visibility.Visible;DetailPanel.IsHitTestVisible=show;
  // Fix the panel's own width during the transition so its lyrics and artwork
  // do not reflow. The shrinking column carries it smoothly beyond the edge.
  DetailPanel.Width=Math.Max(0,(show?target:Math.Max(280,InterfaceSettings.Current.RightPanelWidth??panelPreferences.Width))-DetailPanel.Margin.Left-DetailPanel.Margin.Right);DetailPanel.HorizontalAlignment=HorizontalAlignment.Left;
  SetValue(DetailsRevealWidthProperty,current);
  var transition=new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(300)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut},FillBehavior=FillBehavior.HoldEnd};
  transition.Completed+=(_,_)=>Complete();BeginAnimation(DetailsRevealWidthProperty,transition,HandoffBehavior.SnapshotAndReplace);
 }
}
