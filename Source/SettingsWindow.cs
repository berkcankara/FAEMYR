using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Faemyr;
public sealed record SettingsSection(FrameworkElement View,Action Save,Action Cancel,Func<bool> IsValid);
public partial class MainWindow
{
 Window? settingsWindow;
 void Settings_Click(object sender,RoutedEventArgs e)=>OpenSettings();
 void OpenSettings(string section="appearance")
 {
  if(settingsWindow!=null){settingsWindow.Activate();return;}
  var dialog=new Window{Title=L.T("Faemyr — Ayarlar"),Owner=this,Width=960,Height=850,MinWidth=800,MinHeight=650,MaxHeight=SystemParameters.WorkArea.Height-24,WindowStartupLocation=WindowStartupLocation.CenterOwner};settingsWindow=dialog;
  var originalInterface=InterfaceSettings.Current.Copy();var originalVisualizer=Scope.Mode;interfaceEditorRefresh.Clear();interfaceEditorCleanup.Clear();
  dialog.SetResourceReference(BackgroundProperty,"Canvas");dialog.SetResourceReference(ForegroundProperty,"Ink");
  var root=new Grid{Margin=new Thickness(26)};root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});dialog.Content=root;
  root.Children.Add(new TextBlock{Text=L.T("Ayarlar"),FontSize=28,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,24)});
  var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(150)});body.ColumnDefinitions.Add(new(){Width=new GridLength(24)});body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});Grid.SetRow(body,1);root.Children.Add(body);
  var navigation=new StackPanel();body.Children.Add(new ScrollViewer{Content=navigation,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});var content=new Grid();Grid.SetColumn(content,2);body.Children.Add(content);
  var footer=new DockPanel{Margin=new Thickness(0,20,0,0)};Grid.SetRow(footer,2);root.Children.Add(footer);var error=new TextBlock{TextWrapping=TextWrapping.Wrap,MaxWidth=520,VerticalAlignment=VerticalAlignment.Center,FontSize=12};error.SetResourceReference(TextBlock.ForegroundProperty,"Muted");DockPanel.SetDock(error,Dock.Left);footer.Children.Add(error);
  var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(actions);actions.Children.Add(new Button{Content=L.T("Vazgeç"),IsCancel=true});var save=new Button{Content=L.T("Kaydet"),IsDefault=true,Margin=new Thickness(0)};save.SetResourceReference(Control.BackgroundProperty,"Accent");save.SetResourceReference(Control.ForegroundProperty,"OnAccent");actions.Children.Add(save);
  var appearance=BuildAppearanceSettings(dialog,valid=>save.IsEnabled=valid);var panel=BuildPanelSettings(dialog);var library=BuildLibrarySettings();UseLiveSettingsColors(panel.View);
  var photoSettings=BuildArtistPhotoSettings();var audioSettings=BuildAudioSettings(dialog);var lyricsSettings=BuildLyricsSettings();var layoutSettings=BuildInterfaceSettings("layout");var typeSettings=BuildInterfaceSettings("typography");var playerSettings=BuildInterfaceSettings("player");var desktopSettings=BuildDesktopPlayerSettings();var sections=new Dictionary<string,FrameworkElement>{{"appearance",appearance.View},{"layout",layoutSettings.View},{"typography",typeSettings.View},{"player",playerSettings.View},{"desktop",desktopSettings.View},{"profiles",BuildProfileSettings(dialog)},{"artistPhotos",photoSettings.View},{"panel",panel.View},{"lyrics",lyricsSettings.View},{"audio",audioSettings.View},{"library",library}};
  var buttons=new Dictionary<string,Button>();
  void Select(string id){foreach(var pair in sections){bool selected=pair.Key==id;pair.Value.Visibility=Visible(selected);buttons[pair.Key].SetResourceReference(Control.BackgroundProperty,selected?"Accent":"NavigationSurface");buttons[pair.Key].SetResourceReference(Control.ForegroundProperty,selected?"OnAccent":"Ink");}}
  foreach(var pair in sections)
  {
   content.Children.Add(pair.Value);var button=new Button{Content=pair.Key switch{"appearance"=>L.T("Renkler"),"layout"=>L.T("Düzen"),"typography"=>L.T("Yazı ve kartlar"),"player"=>L.T("Oynatıcı"),"desktop"=>L.T("Masaüstü oynatıcı"),"profiles"=>L.T("Profiller"),"artistPhotos"=>L.T("Sanatçı görselleri"),"panel"=>L.T("Sağ panel"),"lyrics"=>L.T("Şarkı sözleri"),"audio"=>L.T("Ses"),_=>L.T("Müzik arşivi")},HorizontalContentAlignment=HorizontalAlignment.Left,Height=44,Margin=new Thickness(0,0,0,8)};
   System.Windows.Automation.AutomationProperties.SetAutomationId(button,"SettingsNav_"+pair.Key);buttons[pair.Key]=button;navigation.Children.Add(button);string id=pair.Key;button.Click+=(_,_)=>Select(id);
  }
  Select(sections.ContainsKey(section)?section:"appearance");UpdateNavigation();
  bool committed=false;
  save.Click+=(_,_)=>{if(!appearance.IsValid()){Select("appearance");return;}if(!audioSettings.IsValid()){Select("audio");return;}try{layoutSettings.Save();typeSettings.Save();playerSettings.Save();desktopSettings.Save();photoSettings.Save();audioSettings.Save();panel.Save();lyricsSettings.Save();appearance.Save();InterfaceSettings.Current.VisualizerMode=Scope.Mode;InterfaceSettings.Current.Save(InterfaceSettings.Path);new VisualizerPreferences{Mode=Scope.Mode}.Save(Path.Combine(Paths.Data,"visualizer.json"));committed=true;dialog.DialogResult=true;}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){error.Text=L.T("Ayarlar kaydedilemedi: ")+ex.Message;}};
  dialog.Closed+=(_,_)=>{foreach(var stop in interfaceEditorCleanup)stop();if(!committed){panel.Cancel();appearance.Cancel();InterfaceSettings.Current=originalInterface.Copy();ApplyInterfacePreferences();Scope.Mode=originalVisualizer;InterfaceSettings.Current.VisualizerMode=originalInterface.VisualizerMode;renderedAlbum="";if(libraryReady)RefreshView();}appearanceEditorRefresh=null;interfaceEditorRefresh.Clear();interfaceEditorCleanup.Clear();settingsWindow=null;ReleaseLibrarySettings();ReleaseLyricsSettings();UpdateNavigation();};dialog.ShowDialog();
 }
 void UseLiveSettingsColors(DependencyObject node)
 {
  var property=node is TextBlock?TextBlock.ForegroundProperty:node is Control?Control.ForegroundProperty:null;
  if(property!=null&&node is FrameworkElement element&&node.ReadLocalValue(property) is Brush brush)
   foreach(string key in new[]{"Ink","Muted","Accent"})if(ReferenceEquals(brush,FindResource(key))){element.SetResourceReference(property,key);break;}
  foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())UseLiveSettingsColors(child);
 }
}
