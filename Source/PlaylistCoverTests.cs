using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Faemyr;
public static class PlaylistCoverTests
{
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
 static void Set(MainWindow window,string name,object value)=>typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,value);
 static object? Call(MainWindow window,string name,params object?[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args);
 static IEnumerable<DependencyObject> Nodes(DependencyObject node){yield return node;foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())foreach(var descendant in Nodes(child))yield return descendant;}
 static Button Button(DependencyObject root,string label)=>Nodes(root).OfType<Button>().Single(b=>Equals(b.Content,L.T(label)));
 static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;L.Apply("tr");Themes.Load();
  var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  MainWindow? window=null;
  try
  {
   string Image(string name,byte red,byte green,byte blue)
   {
    string path=Path.Combine(directory,name+".png");var bmp=BitmapSource.Create(1,1,96,96,PixelFormats.Rgb24,null,new[]{red,green,blue},3);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var stream=File.Create(path);encoder.Save(stream);return path;
   }
   string red=Image("red",220,20,30),blue=Image("blue",20,40,220);
   PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{AutoPlayOnStartup=false});
   window=new MainWindow{Opacity=0,ShowActivated=false,ShowInTaskbar=false};window.Show();
   for(int i=0;i<200&&!Field<bool>(window,"sessionReady");i++)await Task.Delay(25);
   Check(Field<bool>(window,"sessionReady"),"Playlist editor test starts without playback");
   var store=Field<PlaylistStore>(window,"playlists");
   var draft=new LocalPlaylist();var editorOwner=new Window();string? selected=red;
   var coverEditor=(FrameworkElement)Call(window,"BuildPlaylistCoverEditor",draft,editorOwner,(Func<string?>)(()=>selected))!;
   Click(Button(coverEditor,"Kapak seç…"));Check(draft.Cover==red&&Nodes(coverEditor).OfType<Border>().Single().Background is ImageBrush,"Shared cover picker selects an image and immediately previews it");
   selected=null;Click(Button(coverEditor,"Kapak seç…"));Check(draft.Cover==red,"Cancelling the file picker preserves the chosen cover");
   Click(Button(coverEditor,"Renk kullan"));Check(draft.Cover==""&&Nodes(coverEditor).OfType<Border>().Single().Background is SolidColorBrush,"Shared color button clears the cover and updates the preview");
   void Dialog(Action<Window> edit,Action open)
   {
    Exception? failure=null;
    window!.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>
    {
     var dialog=window.OwnedWindows.Cast<Window>().Single(w=>w.IsVisible);
     try{edit(dialog);}catch(Exception ex){failure=ex;dialog.Close();}
    });
    open();if(failure!=null)throw failure;
   }
   foreach(bool smart in new[]{false,true})
   {
    string kind=smart?"smart":"manual";
    Dialog(dialog=>
    {
     Check(Button(dialog,"Kapak seç…").IsEnabled&&Button(dialog,"Renk kullan").IsEnabled,"Creation exposes enabled cover controls: "+kind);
     Nodes(dialog).OfType<TextBox>().First().Text="Cover test "+kind;
     ((LocalPlaylist)dialog.DataContext).Cover=red;Click(Button(dialog,"Oluştur"));
    },()=>{if(smart)Call(window,"EditSmartPlaylist",(object?)null);else Call(window,"EditPlaylist",null,null);});
    var item=store.Items.Last();string id=item.Id,oldCover=item.Cover;DateTime created=item.Created;string? rule=item.SmartQuery;string sort=item.SmartSort;
    Check(File.Exists(oldCover)&&oldCover!=red&&Path.GetDirectoryName(oldCover)==Path.Combine(Paths.Data,"playlist-covers"),"Creation imports a managed cover: "+kind);
    Check(new PlaylistStore(Paths.Data).Items.Single(p=>p.Id==id).Cover==oldCover,"Created cover survives store reload: "+kind);
    Check((Field<System.Windows.Controls.Image>(window,"AlbumArtwork").Source as BitmapImage)?.UriSource.LocalPath==oldCover,"Created cover immediately appears in the playlist header: "+kind);
    item.Description="Retained description";item.Tracks.Add("retained-track.flac");store.Save();
    Dialog(dialog=>
    {
     Check(Button(dialog,"Kapak seç…").IsEnabled&&Button(dialog,"Renk kullan").IsEnabled,"Top-right edit route exposes cover controls: "+kind);
     var pending=(LocalPlaylist)dialog.DataContext;Check(!ReferenceEquals(pending,item)&&!ReferenceEquals(pending.Tracks,item.Tracks),"Editor isolates pending changes from the saved playlist: "+kind);
     pending.Cover=blue;Click(Button(dialog,"Kaydet"));
    },()=>Call(window,"EditPlaylist_Click",window,new RoutedEventArgs()));
    item=store.Items.Single(p=>p.Id==id);
    Check(item.Cover!=oldCover&&File.Exists(item.Cover)&&File.Exists(oldCover),"Replacing a cover uses a fresh path and retains the previous file: "+kind);
    Check(item.Created==created&&item.Description=="Retained description"&&item.Tracks.SequenceEqual(new[]{"retained-track.flac"})&&item.SmartQuery==rule&&item.SmartSort==sort,"Changing a cover preserves identity, tracks, description and smart rules: "+kind);
    var displayed=(BitmapImage)Field<System.Windows.Controls.Image>(window,"AlbumArtwork").Source;byte[] pixels=new byte[4];new FormatConvertedBitmap(displayed,PixelFormats.Bgra32,null,0).CopyPixels(new Int32Rect(0,0,1,1),pixels,4,0);
    Check(displayed.UriSource.LocalPath==item.Cover&&pixels[0]>pixels[2],"Replacing a cover immediately displays the new blue artwork: "+kind);
    Check(new PlaylistStore(Paths.Data).Items.Single(p=>p.Id==id).Cover==item.Cover,"Replacement cover persists after reload: "+kind);
    string savedCover=item.Cover;int coverCount=Directory.GetFiles(Path.Combine(Paths.Data,"playlist-covers")).Length;
    Dialog(dialog=>{((LocalPlaylist)dialog.DataContext).Cover=red;dialog.DialogResult=false;},()=>Call(window,"EditPlaylist_Click",window,new RoutedEventArgs()));
    Check(store.Items.Single(p=>p.Id==id).Cover==savedCover&&new PlaylistStore(Paths.Data).Items.Single(p=>p.Id==id).Cover==savedCover&&Directory.GetFiles(Path.Combine(Paths.Data,"playlist-covers")).Length==coverCount,"Cancelling an edit preserves persisted artwork without importing a file: "+kind);
    Dialog(dialog=>{Click(Button(dialog,"Renk kullan"));Click(Button(dialog,"Kaydet"));},()=>Call(window,"EditPlaylist_Click",window,new RoutedEventArgs()));
    item=store.Items.Single(p=>p.Id==id);Check(item.Cover==""&&Field<System.Windows.Controls.Image>(window,"AlbumArtwork").Source==null&&Field<TextBlock>(window,"HeroFallback").Visibility==Visibility.Visible,"Saved color fallback immediately replaces the artwork: "+kind);
    Dialog(dialog=>{((LocalPlaylist)dialog.DataContext).Cover=red;Click(Button(dialog,"Kaydet"));},()=>Call(window,"EditPlaylist_Click",window,new RoutedEventArgs()));
    Check(File.Exists(store.Items.Single(p=>p.Id==id).Cover),"Cover remains changeable after removing a previous cover: "+kind);
   }
   Check(!Field<AudioEngine>(window,"audio").DeviceStarted,"All playlist cover changes complete without starting audio");
  }
  finally{window?.Close();File.WriteAllText(Path.Combine(directory,"playlist-cover-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
