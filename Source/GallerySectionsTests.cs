using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse=System.Windows.Shapes.Ellipse;
using System.Windows.Threading;

namespace Faemyr;

public static class GallerySectionsTests
{
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
 static void Set(MainWindow window,string name,object value)=>typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,value);
 static void Refresh(MainWindow window)=>typeof(MainWindow).GetMethod("RefreshView",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
 static IEnumerable<DependencyObject> Visuals(DependencyObject root){yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var node in Visuals(VisualTreeHelper.GetChild(root,i)))yield return node;}
 static async Task Wait(Func<bool> ready){for(int i=0;i<400;i++){if(ready())return;await Task.Delay(15);}throw new Exception("Gallery timeout");}
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  Environment.SetEnvironmentVariable("LYRA_ARTIST_PHOTOS_OFFLINE","1");Environment.SetEnvironmentVariable("LYRA_ARTIST_BIOGRAPHY_OFFLINE","1");
  var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}MainWindow? window=null;
  try
  {
   L.Apply("tr");Themes.Load();var tr=CultureInfo.CurrentCulture;
   Check(GallerySections.Initial("...And Justice",tr)=="A"&&GallerySections.Initial("1992",tr)=="#"&&GallerySections.Initial("♪",tr)=="#","Punctuation prefixes and nonalphabetic entries have sensible section names");
   Check(GallerySections.Initial("çalı",tr)=="Ç"&&GallerySections.Initial("istanbul",tr)=="İ"&&GallerySections.Initial("ışık",tr)=="I"&&GallerySections.Initial("öykü",tr)=="Ö","Turkish characters keep their correct headings");
   var names=new[]{"1992","A1","A2","A3","A4","B1","B2","Ç1","D1","I1","İ1"};var rows=GallerySections.Rows(names,3,x=>x);
   Check(rows.Select(r=>r.Heading).SequenceEqual(["#","A","","B","Ç","D","I","İ"]),"Each letter starts a new row, even when preceding section is incomplete");
   Check(rows.SelectMany(r=>r.Cards).SequenceEqual(names)&&rows.All(r=>r.Cards.Length<=3),"Every card occurs exactly once and density is respected");
   Check(GallerySections.Rows(names.Reverse(),3,x=>x,true).Where(r=>r.Heading!="").Select(r=>r.Heading).SequenceEqual(["İ","I","D","Ç","B","A","#"]),"Descending sections use Turkish collation and numbers follow letters");
   Check(GallerySections.Rows(names,3).All(r=>r.Heading=="")&&GallerySections.Rows(Array.Empty<string>(),3,x=>x).Length==0,"Flat and empty collections have no unnecessary headings");
   Track Make(string title,string artist,int index){string path=Path.Combine(directory,index+".flac");File.WriteAllText(path,"Gallery fixture; never played.");return new(){Path=path,Title=title,Album=title,Artist=artist,AlbumArtist=artist,Codec="flac",Seconds=60,Number=1,Modified=File.GetLastWriteTimeUtc(path).Ticks};}
   using(var db=new LibraryStore())db.SaveMany([Make("Zebra","Alpha",0),Make("Apple","Beta",1),Make("Arrow","Alpha",2),Make("Banana","Charlie",3),Make("1992","Delta",4),Make("Cedar","Beta",5)]);
   PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{AutoPlayOnStartup=false});new InterfacePreferences{AlbumColumns=3,ArtistColumns=3,ShowWaveforms=false}.Save(InterfaceSettings.Path);
   window=new MainWindow{Opacity=0,ShowActivated=false,ShowInTaskbar=false,Width=1440,Height=900};window.Show();await Wait(()=>Field<bool>(window,"sessionReady"));
   var albums=Field<ListBox>(window,"Albums");var artists=Field<ListBox>(window,"Artists");var albumSort=Field<ComboBox>(window,"AlbumSort");var artistSort=Field<ComboBox>(window,"ArtistSort");var search=Field<TextBox>(window,"Search");
   void Open(string page){Set(window,"view",page);Refresh(window);window.UpdateLayout();}
   string[] Heads<T>(ListBox list)=>list.Items.Cast<GalleryRow<T>>().Where(r=>r.Heading!="").Select(r=>r.Heading).ToArray();
   Open("albums");albumSort.SelectedValue=AlbumSortMode.Title;window.UpdateLayout();Check(Heads<AlbumCard>(albums).SequenceEqual(["#","A","B","C","Z"]),"Album-title choice groups real gallery by title");
   Check(Visuals(albums).OfType<TextBlock>().Any(t=>t.Text=="A"&&t.IsVisible),"A letter heading is rendered in the actual album row template");
   Check(albums.Items.Cast<GalleryRow<AlbumCard>>().SelectMany(r=>r.Cards).Select(c=>c.Key).Distinct().Count()==6&&VirtualizingPanel.GetIsVirtualizing(albums),"Album cards retain unique navigation targets and virtualized rows");
   albumSort.SelectedValue=AlbumSortMode.Artist;Check(Heads<AlbumCard>(albums).SequenceEqual(["A","B","C","D"]),"Artist sort on album gallery uses artist names as headings");
   albumSort.SelectedValue=AlbumSortMode.YearNewest;Check(Heads<AlbumCard>(albums).Length==0,"Year sorting remains flat");albumSort.SelectedValue=AlbumSortMode.TrackCount;Check(Heads<AlbumCard>(albums).Length==0,"Track-count sorting remains flat");
   albumSort.SelectedValue=AlbumSortMode.Title;search.Text="Apple";Refresh(window);Check(Heads<AlbumCard>(albums).SequenceEqual(["A"])&&albums.Items.Cast<GalleryRow<AlbumCard>>().Sum(r=>r.Cards.Length)==1,"Search rebuilds sections without empty headers");search.Text="";
   Open("artists");Check(artistSort.IsVisible&&!albumSort.IsVisible&&Heads<ArtistCard>(artists).SequenceEqual(["A","B","C","D"]),"Artists exposes its own sorting control in the page header");
   artistSort.SelectedValue=ArtistSortMode.NameDescending;window.UpdateLayout();Check(Heads<ArtistCard>(artists).SequenceEqual(["D","C","B","A"])&&VirtualizingPanel.GetIsVirtualizing(artists),"Artist descending order updates real gallery without disabling virtualization");
   Check(Visuals(artists).OfType<TextBlock>().Any(t=>t.Text=="D"&&t.IsVisible),"The artist heading is rendered in the actual row template");
   var persisted=AlbumSortSettings.Load(Path.Combine(Paths.Data,"album-sort.json"));Check(persisted.Mode==AlbumSortMode.Title&&persisted.ArtistMode==ArtistSortMode.NameDescending,"Album and artist sorting choices persist independently");
   Open("albums");Check(albumSort.IsVisible&&!artistSort.IsVisible&&Heads<AlbumCard>(albums).SequenceEqual(["#","A","B","C","Z"]),"Returning to albums retains the previous selection");
   var albumToggle=Field<ToggleButton>(window,"AlbumGroupingButton");var artistToggle=Field<ToggleButton>(window,"ArtistGroupingButton");
   void Toggle(ToggleButton button)=>typeof(ToggleButton).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(button,null);
   Check(albumToggle.IsVisible&&albumToggle.IsChecked==true&&albumToggle.IsEnabled,"The album header exposes an active grouping toggle beside the sort menu");
   var sortBounds=albumSort.TransformToAncestor(window).TransformBounds(new Rect(albumSort.RenderSize));var toggleBounds=albumToggle.TransformToAncestor(window).TransformBounds(new Rect(albumToggle.RenderSize));Check(toggleBounds.Left>=sortBounds.Right&&Math.Abs(toggleBounds.Height-sortBounds.Height)<1,"Grouping toggle is aligned beside the sort control");
   Toggle(albumToggle);window.UpdateLayout();Check(albumToggle.IsChecked==false&&Heads<AlbumCard>(albums).Length==0&&albums.Items.Count==2&&albumSort.SelectedValue is AlbumSortMode.Title,"Turning grouping off restores the compact gallery without changing sort mode");
   Check(!AlbumSortSettings.Load(Path.Combine(Paths.Data,"album-sort.json")).GroupByLetter,"The disabled grouping preference is persisted");
   Open("artists");Check(artistToggle.IsVisible&&artistToggle.IsChecked==false&&Heads<ArtistCard>(artists).Length==0&&artistSort.SelectedValue is ArtistSortMode.NameDescending,"The artists page shares the disabled view preference and keeps its own sort direction");
   Toggle(artistToggle);Check(artistToggle.IsChecked==true&&Heads<ArtistCard>(artists).SequenceEqual(["D","C","B","A"]),"Artist toggle restores categorized rows immediately");
   Open("albums");Check(albumToggle.IsChecked==true&&Heads<AlbumCard>(albums).SequenceEqual(["#","A","B","C","Z"]),"Turning grouping on from artists also restores album groups");
   albumSort.SelectedValue=AlbumSortMode.TrackCount;Check(albumToggle.IsEnabled==false&&albumToggle.IsChecked==false&&Heads<AlbumCard>(albums).Length==0&&AlbumSortSettings.Load(Path.Combine(Paths.Data,"album-sort.json")).GroupByLetter,"Nonalphabetic sort disables the button without losing the preference");
   albumSort.SelectedValue=AlbumSortMode.Title;Check(albumToggle.IsEnabled&&albumToggle.IsChecked==true,"Returning to alphabetical sort restores the preference");
   Toggle(albumToggle);window.Close();window=null;window=new MainWindow{Opacity=0,ShowActivated=false,ShowInTaskbar=false,Width=1440,Height=900};window.Show();await Wait(()=>Field<bool>(window,"sessionReady"));Open("albums");Check(Field<ToggleButton>(window,"AlbumGroupingButton").IsChecked==false&&Heads<AlbumCard>(Field<ListBox>(window,"Albums")).Length==0,"The disabled grouping state survives restarting the player");
   var currentToggle=Field<ToggleButton>(window,"AlbumGroupingButton");var currentSort=Field<ComboBox>(window,"AlbumSort");
   Color Resource(string name)=>((SolidColorBrush)window.FindResource(name)).Color;
   Color BrushColor(Brush brush)=>((SolidColorBrush)brush).Color;
   Border ToggleBorder(){currentToggle.ApplyTemplate();return (Border)currentToggle.Template.FindName("Box",currentToggle);}
   foreach(string theme in new[]{"oled","dark","light","warm"})
   {
    Themes.Apply(theme);window.UpdateLayout();
    Check(BrushColor(ToggleBorder().BorderBrush)==BrushColor(currentSort.BorderBrush)&&BrushColor(ToggleBorder().BorderBrush)==Resource("Line"),"Inactive grouping border matches the adjacent sort menu in "+theme);
    Toggle(currentToggle);window.UpdateLayout();Check(BrushColor(ToggleBorder().BorderBrush)==Resource("Accent")&&BrushColor(ToggleBorder().Background)==Resource("Accent"),"Active grouping indicator uses the theme accent in "+theme);Toggle(currentToggle);window.UpdateLayout();
    var icon=Visuals(currentToggle).OfType<SvgIcon>().Single();Check(icon.Icon=="grouped"&&icon.ActualWidth>=22&&icon.ActualHeight>=22&&RenderOptions.GetEdgeMode(icon)!=EdgeMode.Aliased,"Grouping icon uses a larger antialiased vector in "+theme);
    var nav=Field<Button>(window,"NavArtists");Check(BrushColor(nav.Background)==Resource("NavigationSurface")&&Resource("NavigationSurface")!=Resource("SidebarSurface")&&Resource("NavigationSurface")!=Resource("Canvas"),"Inactive navigation buttons contrast with the panel in "+theme);
   }
   Exception? dialogFailure=null;var done=new TaskCompletionSource();
   _=window.Dispatcher.BeginInvoke(new Action(()=>
   {
    Window? dialog=null;
    try
    {
     dialog=Application.Current.Windows.OfType<Window>().Single(w=>w.Title=="Faemyr — Ayarlar");dialog.UpdateLayout();
     var links=Visuals(dialog).OfType<Button>().Where(b=>AutomationProperties.GetAutomationId(b).StartsWith("SettingsNav_",StringComparison.Ordinal)).ToArray();
     var appearance=links.Single(b=>AutomationProperties.GetAutomationId(b)=="SettingsNav_appearance");var layout=links.Single(b=>AutomationProperties.GetAutomationId(b)=="SettingsNav_layout");
     foreach(string theme in new[]{"oled","dark","light","warm"})
     {
      Themes.Apply(theme);dialog.UpdateLayout();Check(links.Length==11&&links.Where(b=>b!=appearance).All(b=>BrushColor(b.Background)==Resource("NavigationSurface"))&&BrushColor(appearance.Background)==Resource("Accent"),"Settings navigation has readable backgrounds in "+theme+" ("+links.Length+" buttons)");
     }
     layout.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(BrushColor(layout.Background)==Resource("Accent")&&BrushColor(appearance.Background)==Resource("NavigationSurface"),"Settings selection follows the active tab without losing inactive button backgrounds");
    }
    catch(Exception ex){dialogFailure=ex;}
    finally{dialog?.Close();done.SetResult();}
   }));
   typeof(MainWindow).GetMethod("OpenSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,["appearance"]);await done.Task;if(dialogFailure!=null)throw dialogFailure;
   // A gallery large enough to exceed the old 128-image cache, with
   // differently sized letter sections and a partially filled final row.
   var coverFile=Path.Combine(directory,"gallery-art.png");
   var bitmap=BitmapSource.Create(320,320,96,96,PixelFormats.Bgra32,null,new byte[320*320*4],320*4);
   var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(coverFile))encoder.Save(output);
   var covers=Enumerable.Range(0,277).Select(i=>Path.Combine(directory,"art-"+i+".png")).ToArray();
   foreach(var file in covers)File.Copy(coverFile,file,true);
   var first=await ThumbnailCache.Load(covers[0]);
   foreach(var file in covers.Skip(1))await ThumbnailCache.Load(file);
   Check(first!=null&&ReferenceEquals(first,await ThumbnailCache.Load(covers[0]))&&ThumbnailCache.CachedBytes<=ThumbnailCache.MemoryBudget,"277 album thumbnails remain warm within the byte-bounded cache after scrolling through the library");
   Check(SmoothScroll.WheelTarget(940,1000,-96,1000,true)==844&&SmoothScroll.WheelTarget(60,0,96,1000,true)==156,"A reverse wheel tick responds immediately rather than consuming pending momentum");
   var newAlbums=Field<ListBox>(window,"Albums");var newArtists=Field<ListBox>(window,"Artists");
   string Name(int index)=>((char)('A'+index/13%26)).ToString()+" "+index.ToString("D3");
   async Task VerifyScroll(ListBox list)
   {
    window.UpdateLayout();var scroll=Visuals(list).OfType<ScrollViewer>().First();var panel=Visuals(list).OfType<GalleryVirtualizingPanel>().First();
    await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
    scroll.ScrollToBottom();window.UpdateLayout();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);window.UpdateLayout();
    double bottom=scroll.VerticalOffset;Check(bottom>1000&&Math.Abs(bottom-scroll.ScrollableHeight)<3,"Large gallery reaches its genuine bottom");
    double previous=bottom;
    for(int i=0;i<12;i++)
    {
     scroll.ScrollToVerticalOffset(Math.Max(0,previous-96));window.UpdateLayout();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);window.UpdateLayout();
     Check(scroll.VerticalOffset<previous+.5,"Reverse scroll stays monotonic across categorized rows ("+previous+" to "+scroll.VerticalOffset+")");previous=scroll.VerticalOffset;
    }
    Check(bottom-previous>900&&panel.Children.Count<list.Items.Count,"Reverse scrolling spans multiple rows without realizing the full gallery");
    var squares=Visuals(list).OfType<SquareGalleryImage>().Where(c=>c.ActualWidth>0).ToArray();
    Check(squares.Length>0&&squares.All(c=>Math.Abs(c.ActualWidth-c.ActualHeight)<1.5),"Recycled card artwork is square without a second width-driven layout");
   }
   Open("albums");newAlbums.ItemsSource=GallerySections.Rows(covers.Select((cover,i)=>new AlbumCard{Key=i.ToString(),Title=Name(i),Artist="Artist",Cover=cover,Count=1}),InterfaceSettings.Current.AlbumColumns,c=>c.Title);
   await VerifyScroll(newAlbums);
   var albumPanel=Visuals(newAlbums).OfType<GalleryVirtualizingPanel>().First();
   double groupedHeight=albumPanel.ExtentHeight;
   newAlbums.ItemsSource=newAlbums.Items.Cast<GalleryRow<AlbumCard>>().Select(row=>row with{Heading=""}).ToArray();
   window.UpdateLayout();Check(albumPanel.ExtentHeight<groupedHeight,"Replacing the gallery with equally many rows refreshes its cached heading heights");
   Open("artists");newArtists.ItemsSource=GallerySections.Rows(covers.Select((cover,i)=>new ArtistCard{Name=Name(i),Cover=cover,Albums=1,Tracks=1}),InterfaceSettings.Current.ArtistColumns,c=>c.Name);
   await VerifyScroll(newArtists);
   var order=Field<PlaybackOrder>(window,"order");var shuffleDot=Field<Ellipse>(window,"ShuffleActiveDot");var repeatDot=Field<Ellipse>(window,"RepeatActiveDot");
   void Modes(bool shuffle,RepeatMode repeat){order.SetShuffle(shuffle,0,-1);order.Repeat=repeat;typeof(MainWindow).GetMethod("UpdateAccentControls",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);}
   Modes(false,RepeatMode.Off);Check(shuffleDot.Visibility==Visibility.Hidden&&repeatDot.Visibility==Visibility.Hidden,"Inactive playback modes show no marker");
   Modes(true,RepeatMode.All);Check(shuffleDot.Visibility==Visibility.Visible&&repeatDot.Visibility==Visibility.Visible,"Both active playback modes show accent markers in the main player");
   Modes(false,RepeatMode.One);Check(shuffleDot.Visibility==Visibility.Hidden&&repeatDot.Visibility==Visibility.Visible,"Repeat-one remains marked independently of shuffle");
   Check(!Field<AudioEngine>(window,"audio").DeviceStarted,"Gallery and appearance changes never start audio playback");
  }
  finally{window?.Close();File.WriteAllText(Path.Combine(directory,"gallery-sections-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
