using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Faemyr;

public static class AlbumDiscsTests
{
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
 static void Set(MainWindow window,string name,object value)=>typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,value);
 static void Refresh(MainWindow window)=>typeof(MainWindow).GetMethod("RefreshView",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
 static IEnumerable<DependencyObject> Visuals(DependencyObject root){yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var node in Visuals(VisualTreeHelper.GetChild(root,i)))yield return node;}
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  var results=new List<object>();void Check(bool pass,string name){results.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}MainWindow? window=null;
  try
  {
   L.Apply("tr");Themes.Load();
   Track Make(string album,string title,uint disc,uint number,string folder="")
   {
    string dir=Path.Combine(directory,album,folder);Directory.CreateDirectory(dir);string path=Path.Combine(dir,title+".flac");File.WriteAllText(path,"Disc test fixture; never played.");return new(){Path=path,Title=title,Album=album,Artist="Test artist",AlbumArtist="Test artist",Disc=disc,Number=number,Codec="flac",Seconds=60,Modified=File.GetLastWriteTimeUtc(path).Ticks};
   }
   var tracks=new[]{Make("Multi","Fourth",0,2,"CD 2"),Make("Multi","Second",1,2),Make("Multi","Third",2,1),Make("Multi","First",0,1),Make("Single","Only song",0,1),Make("Multi","Tenth disk",10,1)};
   var multi=tracks.Where(t=>t.Album=="Multi").ToArray();Check(AlbumDiscs.Order(multi).Select(t=>t.Title).SequenceEqual(["First","Second","Third","Fourth","Tenth disk"]),"Disc and track order is numeric and missing disc tags default to disc 1 or CD folder");
   Check(AlbumDiscs.Multiple(multi)&&!AlbumDiscs.Multiple(tracks.Where(t=>t.Album=="Single")),"Only multi-disc albums need headers");
   Check(new Track{Disc=3,Path=Path.Combine(directory,"Disc 2","song.flac")}.AlbumDisc==3,"Explicit disc metadata takes priority over folder name");
   using(var db=new LibraryStore())db.SaveMany(tracks);
   PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{AutoPlayOnStartup=false});new InterfacePreferences{ShowWaveforms=false}.Save(InterfaceSettings.Path);
   window=new MainWindow{Opacity=0,ShowActivated=false,ShowInTaskbar=false,Width=1440,Height=900};window.Show();for(int i=0;i<400&&!Field<bool>(window,"sessionReady");i++)await Task.Delay(15);
   Check(Field<bool>(window,"sessionReady"),"Library initialized");
   var grid=Field<DataGrid>(window,"Tracks");var search=Field<TextBox>(window,"Search");var lib=Field<List<Track>>(window,"library");
   void Open(string page,string album=""){Set(window,"view",page);if(album.Length>0)Set(window,"albumKey",lib.First(t=>t.Album==album).AlbumKey);Refresh(window);window.UpdateLayout();}
   uint[] Groups()=>grid.Items.Groups?.Cast<CollectionViewGroup>().Select(g=>(uint)g.Name).ToArray()??[];
   Open("album","Multi");Check(Groups().SequenceEqual([1u,2u,10u]),"Real album screen groups disk 1, 2, 10 numerically");
   string[] Headers()=>Visuals(grid).OfType<TextBlock>().Select(t=>t.Text).ToArray();
   var headers=Headers();File.WriteAllLines(Path.Combine(directory,"headers-first.txt"),headers);
   grid.ScrollIntoView(lib.Single(t=>t.Title=="Third"));window.UpdateLayout();await Task.Delay(80);window.UpdateLayout();var secondHeaders=Headers();File.WriteAllLines(Path.Combine(directory,"headers-second.txt"),secondHeaders);
   Check(headers.Contains("Disk 1")&&secondHeaders.Contains("Disk 2"),"Disk headers are rendered in the real track grid");
   Check(grid.Items.OfType<Track>().Select(t=>t.Title).SequenceEqual(["First","Second","Third","Fourth","Tenth disk"])&&grid.ItemsSource is List<Track>,"The actual playback collection contains all tracks in disc order and no fake rows");
   Check(VirtualizingPanel.GetIsVirtualizingWhenGrouping(grid)&&grid.EnableRowVirtualization,"Grouped track lists retain virtualization");
   grid.SelectedItem=lib.Single(t=>t.Title=="Third");Refresh(window);Check(grid.SelectedItem is Track{Title:"Third"},"Selected track is retained when rebuilding groups");
   search.Text="Fourth";Refresh(window);Check(Groups().SequenceEqual([2u])&&grid.Items.Count==1,"Filtering preserves disk identity without empty groups");search.Clear();
   L.Apply("en");Open("album","Multi");grid.ScrollIntoView(lib.Single(t=>t.Title=="First"));window.UpdateLayout();Check(Visuals(grid).OfType<TextBlock>().Any(t=>t.Text=="Disc 1"),"English translates the disk heading");
   Open("album","Single");Check(Groups().Length==0&&grid.Items.Count==1,"Single-disc album remains flat");
   Open("tracks");Check(Groups().Length==0&&grid.Items.Count==tracks.Length,"All tracks remains flat");
   Open("album","Multi");Check(Groups().SequenceEqual([1u,2u,10u]),"Returning to a multi-disc album restores grouping");
   Check(!Field<AudioEngine>(window,"audio").DeviceStarted,"UI tests never start audio playback");
  }
  finally{window?.Close();File.WriteAllText(Path.Combine(directory,"album-discs-results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
