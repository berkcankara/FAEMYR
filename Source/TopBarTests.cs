using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ahenk;
public static class TopBarTests
{
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
 static void Set(MainWindow window,string name,object value)=>typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,value);
 static object? Call(MainWindow window,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args);
 static async Task Wait(Func<bool> ready){for(int i=0;i<400;i++){if(ready())return;await Task.Delay(15);}throw new Exception("Top bar test timeout");}
 static void Click(MainWindow window,string name)=>Field<Button>(window,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
 static Rect Bounds(FrameworkElement element,Visual ancestor)=>element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Themes.Load();Environment.SetEnvironmentVariable("LYRA_ARTIST_PHOTOS_OFFLINE","1");Environment.SetEnvironmentVariable("LYRA_ARTIST_BIOGRAPHY_OFFLINE","1");
  var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  MainWindow? window=null;
  try
  {
   var tracks=new List<Track>();
   for(int i=0;i<70;i++){string path=Path.Combine(directory,"metadata-"+i+".flac");File.WriteAllText(path,"Metadata fixture, never decoded or played.");tracks.Add(new Track{Path=path,Title=i==0?"A long current track title for the centered header":$"Track {i:00}",Artist=i==0?"Alpha Artist":"Beta Artist",AlbumArtist="Alpha Artist",Album=i<35?"Alpha Album":"Beta Album",Number=(uint)(i+1),Seconds=90,SampleRate=44100,Bits=16,Channels=2,Codec="flac",Fields=[new("Vorbis / LYRICS","Local lyrics") ]});}
   using(var db=new LibraryStore())db.SaveMany(tracks);
   PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{AutoPlayOnStartup=false});PlayerJson.Write(Path.Combine(Paths.Data,"session.json"),new PlayerSession{Queue=tracks.Take(2).Select(t=>t.Path).ToList(),QueueIndex=0,TrackPath=tracks[0].Path,View="albums"});
   new InterfacePreferences{ShowRightPanel=true,RightPanelWidth=370,ShowWaveforms=false,ShowBitrate=false}.Save(InterfaceSettings.Path);
   window=new MainWindow{Opacity=0,ShowActivated=false,ShowInTaskbar=false};window.Show();await Wait(()=>Field<bool>(window,"sessionReady"));window.UpdateLayout();
   var search=Field<TextBox>(window,"Search");var titleBar=Field<Grid>(window,"TitleBar");var now=Field<StackPanel>(window,"TopNowPlaying");var column=Field<ColumnDefinition>(window,"DetailsColumn");var panel=Field<Border>(window,"DetailPanel");var content=Field<Grid>(window,"MainContent");
   Check(ReferenceEquals(search.Parent,Field<Grid>(window,"TopSearchHost"))&&!Field<Button>(window,"BackButton").IsEnabled,"Search lives beside the back button and the initial history is empty");
   double compactWidth=search.Width;
   Check(Math.Abs(Bounds(search,window).Right-Bounds(Field<Border>(window,"SidebarBorder"),window).Right)<1&&Equals(search.Tag,"Bul")&&!now.IsVisible&&Field<StackPanel>(window,"TopWindowControls").Children.Count==4,"Compact search aligns with the sidebar and shows Bul beside the icon");
   Check(Math.Abs(column.ActualWidth-370)<2,"Initial layout preserves the user's configured panel width before the first window measurement");
   Field<Grid>(window,"TopSearchHost").RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseEnterEvent});await Task.Delay(75);
   if(SystemParameters.ClientAreaAnimation)Check(search.Width>compactWidth&&search.Width<300,"Hover expansion passes through intermediate search widths");
   await Task.Delay(250);Check(search.Width>=220,"Hover expands the real search control");
   foreach(var (view,prompt) in new[]{("albums","hangi albümü arıyorsun?"),("artists","hangi sanatçıyı arıyorsun?"),("playlists","hangi listeyi arıyorsun?"),("tracks","hangi parçayı arıyorsun?"),("album","hangi parçayı arıyorsun?"),("artist","sanatçının hangi albümünü arıyorsun?")})
   {Set(window,"view",view);Set(window,"albumKey",tracks[0].AlbumKey);Set(window,"artistName","Alpha Artist");Call(window,"RefreshView");window.UpdateLayout();Check(Equals(search.Tag,prompt)&&search.IsVisible,"Top search remains available with the correct prompt on "+view);}
   Set(window,"view","albums");Call(window,"RefreshView");search.Text="Alpha";await Task.Delay(240);Click(window,"NavArtists");Click(window,"BackButton");await Task.Delay(80);
   Check(Field<string>(window,"view")=="albums"&&search.Text=="Alpha","Back restores the previous page and its query");
   search.Clear();await Task.Delay(240);Click(window,"NavTracks");window.UpdateLayout();var rows=Field<DataGrid>(window,"Tracks");rows.SelectedItem=rows.Items.Cast<Track>().Single(t=>t.Path==tracks[20].Path);var scroll=(ScrollViewer?)Call(window,"CurrentScroll");scroll!.ScrollToVerticalOffset(300);window.UpdateLayout();double offset=scroll.VerticalOffset;Click(window,"NavAlbums");Click(window,"BackButton");await Task.Delay(100);window.UpdateLayout();
   Check(Field<string>(window,"view")=="tracks"&&Math.Abs(((ScrollViewer?)Call(window,"CurrentScroll"))!.VerticalOffset-offset)<2&&rows.SelectedItem is Track selected&&selected.Path==tracks[20].Path,"Back preserves track selection and the actual list scroll position: expected="+offset+", actual="+((ScrollViewer?)Call(window,"CurrentScroll"))!.VerticalOffset+", selection="+(rows.SelectedItem as Track)?.Path);
   Call(window,"SetTopSearchExpanded",false,false);search.Text="Track";await Task.Delay(240);Call(window,"SetTopSearchExpanded",false,false);Check(Field<System.Windows.Shapes.Ellipse>(window,"SearchFilterDot").Visibility==Visibility.Visible,"A compact search shows an accent indicator while a filter is active");
   search.Clear();await Task.Delay(220);Call(window,"SetTopSearchExpanded",false,false);window.UpdateLayout();double before=content.ActualWidth,initialWidth=column.ActualWidth;double left=Bounds(panel,Field<Grid>(window,"WindowRoot")).Left;
   Click(window,"ToggleDetailsButton");await Task.Delay(100);window.UpdateLayout();
   if(SystemParameters.ClientAreaAnimation)Check(column.ActualWidth>0&&column.ActualWidth<initialWidth&&Bounds(panel,Field<Grid>(window,"WindowRoot")).Left>left,"The panel slides right while its reserved width shrinks smoothly");
   await Task.Delay(290);window.UpdateLayout();
   Check(column.ActualWidth<.1&&panel.Visibility==Visibility.Collapsed&&content.ActualWidth>before+initialWidth-2,"Hiding the panel frees all of its width for the collection");
   Check(now.IsVisible&&Field<TextBlock>(window,"TopTrackTitle").Text==tracks[0].Title&&Field<ArtistLinks>(window,"TopTrackArtist").Names==tracks[0].Artist,"The hidden-panel header shows the current track and artist");
   Check(!InterfacePreferences.Load(InterfaceSettings.Path).ShowRightPanel,"The panel visibility choice is saved");
   Click(window,"ToggleDetailsButton");await Task.Delay(100);Click(window,"ToggleDetailsButton");await Task.Delay(100);Click(window,"ToggleDetailsButton");await Task.Delay(400);window.UpdateLayout();
   Check(panel.Visibility==Visibility.Visible&&Math.Abs(column.ActualWidth-initialWidth)<2&&double.IsNaN(panel.Width)&&!now.IsVisible,"Rapid toggles settle at the original width and restore normal panel resizing: initial="+initialWidth+", width="+column.ActualWidth+", panelWidth="+panel.Width+", sliding="+Field<bool>(window,"detailsSliding")+", shown="+InterfaceSettings.Current.ShowRightPanel+", pref="+InterfaceSettings.Current.RightPanelWidth+", header="+now.Visibility);
   column.Width=new GridLength(410);Call(window,"SavePanelPreferences");InterfaceSettings.Current.RightPanelWidth=410;Click(window,"ToggleDetailsButton");await Task.Delay(370);Click(window,"ToggleDetailsButton");await Task.Delay(370);Check(Math.Abs(column.ActualWidth-410)<2,"A manually resized panel keeps its width across hide/show");
   Click(window,"ToggleDetailsButton");await Task.Delay(370);window.Width=1120;window.Height=720;Call(window,"SetTopSearchExpanded",true,false);window.UpdateLayout();
   var bounds=Bounds(now,titleBar);var navigation=Bounds(Field<StackPanel>(window,"TopNavigation"),titleBar);var controls=Bounds(Field<StackPanel>(window,"TopWindowControls"),titleBar);
   Check(Math.Abs(bounds.Left+bounds.Width/2-titleBar.ActualWidth/2)<1&&bounds.Left>=navigation.Right&&bounds.Right<=controls.Left,"Current-track text stays centered and cannot overlap expanded search or window controls at minimum width");
   Set(window,"now",tracks[1]);Call(window,"ShowTrackDetails",tracks[1]);Call(window,"UpdateDesktopPlayback");await Task.Delay(30);Check(Field<TextBlock>(window,"TopTrackTitle").Text==tracks[1].Title&&Field<ArtistLinks>(window,"TopTrackArtist").Names==tracks[1].Artist,"Current-track bindings update while the detail panel is hidden");
   L.Apply("en");Call(window,"RefreshView");Check((string)Field<Button>(window,"ToggleDetailsButton").ToolTip=="Show right panel"&&Equals(search.Tag,"Which track are you looking for?"),"Header tooltips and page prompts follow the application language");L.Apply("tr");
   Call(window,"EnterFullscreenPlayer");await Wait(()=>window.WindowState==WindowState.Maximized);Check(titleBar.Visibility==Visibility.Collapsed,"The new normal-window header does not appear over the fullscreen player");Call(window,"ExitFullscreenPlayer");await Wait(()=>window.WindowState==WindowState.Normal);window.UpdateLayout();Check(titleBar.IsVisible&&now.IsVisible&&column.ActualWidth<.1,"Leaving fullscreen restores the hidden-panel header layout");
   var audio=Field<AudioEngine>(window,"audio");Check(!audio.DeviceStarted&&audio.OutputStartCount==0,"All navigation, search and panel tests run without opening audio output");
   var root=(FrameworkElement)window.Content;var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(Path.Combine(directory,"top-bar-preview.png")))encoder.Save(output);
   window.Close();window=null;window=new MainWindow{Opacity=0,ShowActivated=false,ShowInTaskbar=false};window.Show();await Wait(()=>Field<bool>(window,"sessionReady"));Check(Field<Border>(window,"DetailPanel").Visibility==Visibility.Collapsed&&Field<StackPanel>(window,"TopNowPlaying").IsVisible,"Restart restores the panel preference and the current-track header");
  }
  finally{window?.Close();File.WriteAllText(Path.Combine(directory,"top-bar-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
