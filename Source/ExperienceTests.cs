using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace Ahenk;
public static class ExperienceTests
{
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  AppearanceTests.Run(directory,Check);
  TitleFittingTests.Run(Check);
  ArtistNavigationTests.Run(Check);
  ArtistNavigationTests.RunArchive(directory,Check);
  FavoritesTests.Run(directory,Check);
  await LyricsTests.Run(directory,Check);
  var a=new Track{Path="C:\\test\\a.flac",Title="Bir",Album="Beta",AlbumArtist="Zeta",Artist="Zeta"};var b=new Track{Path="C:\\test\\b.flac",Title="İki",Album="Alfa",AlbumArtist="Ada",Artist="Ada"};
  var store=new PlaylistStore(directory);var list=new LocalPlaylist{Name="Gece seçkisi",Description="Yerel liste",Color="#335E56"};store.Items.Add(list);store.Add(list,[a,b,a]);
  var loaded=new PlaylistStore(directory);Check(loaded.Items.Count==1&&loaded.Items[0].Tracks.SequenceEqual(new[]{a.Path,b.Path}),"Playlist saves and reloads with ordered unique tracks");Check(loaded.Items[0].Description=="Yerel liste"&&loaded.Items[0].Color=="#335E56","Playlist customization persists");
  PlaylistStore.Move(list,b.Path,-1);Check(list.Tracks.SequenceEqual(new[]{b.Path,a.Path}),"Playlist track order can be changed");PlaylistStore.Move(list,b.Path,-1);Check(list.Tracks[0]==b.Path,"Playlist reorder respects upper boundary");list.Tracks.Add("missing.flac");Check(PlaylistStore.Resolve(list,[a,b]).Select(t=>t.Path).SequenceEqual(new[]{b.Path,a.Path}),"Missing library tracks are skipped while preserving playlist order");store.Save();
  var order=new PlaybackOrder(77);order.SetShuffle(true,6,0);int current=0;var seen=new HashSet<int>{0};for(int n=0;n<5;n++){int expected=order.Peek(6,current,true);Check(expected==order.Peek(6,current,true),"Shuffle preview stays stable "+n);int next=order.Next(6,current,true);Check(expected==next&&seen.Add(next),"Shown next track matches actual shuffle transition "+n);current=next;}Check(order.Peek(6,current,true)==-1,"Up next is empty at queue end");order.Repeat=RepeatMode.All;int again=order.Peek(6,current,true);Check(again>=0&&again!=current&&again==order.Next(6,current,true),"Repeat-all preview matches the new shuffle cycle");order.Repeat=RepeatMode.One;Check(order.Peek(6,current,true)==current&&order.Next(6,current,true)==current,"Repeat-one preview identifies the current track");Check(order.Peek(0,-1,true)==-1,"Empty queue has no next track");
  var cards=new[]{new AlbumCard{Key="b",Title="Beta",Artist="Zeta",Count=5,Year=2020},new AlbumCard{Key="a",Title="Alfa",Artist="Ada",Count=9,Year=2025}};
  Check(AlbumSorting.Sort(cards,AlbumSortMode.Title)[0].Key=="a"&&AlbumSorting.Sort(cards,AlbumSortMode.Artist)[0].Key=="a","Album and artist alphabetic sorts");Check(AlbumSorting.Sort(cards,AlbumSortMode.YearNewest)[0].Key=="a"&&AlbumSorting.Sort(cards,AlbumSortMode.TrackCount)[0].Key=="a","Year and track-count sorts");
  foreach(var theme in Themes.Choices){Themes.Apply(theme.Id);var ink=(SolidColorBrush)System.Windows.Application.Current.Resources["Ink"];var canvas=(SolidColorBrush)System.Windows.Application.Current.Resources["Canvas"];Check(ink.Color!=canvas.Color,"Theme has distinct text and background: "+theme.Id);}Themes.Apply("dark");
  string state=Path.Combine(directory,"ui-state"),inbox=Path.Combine(directory,"music");Directory.CreateDirectory(state);Directory.CreateDirectory(inbox);
  string song=Path.Combine(inbox,"silence.flac");await ProcessTool.Run(Paths.Tool("ffmpeg"),["-v","error","-y","-f","lavfi","-i","anullsrc=r=44100:cl=stereo","-t","120","-c:a","flac",song]);
  var image=new DrawingVisual();using(var d=image.RenderOpen()){d.DrawRectangle(new LinearGradientBrush(Colors.Coral,Colors.DarkSlateBlue,45),null,new System.Windows.Rect(0,0,500,500));d.DrawEllipse(Brushes.Gold,null,new System.Windows.Point(170,220),120,120);}var bmp=new System.Windows.Media.Imaging.RenderTargetBitmap(500,500,96,96,PixelFormats.Pbgra32);bmp.Render(image);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));string cover=Path.Combine(state,"custom-cover.png");using(var f=File.Create(cover))encoder.Save(f);
  string imported=PlaylistArtwork.Import(cover,directory,list.Id);list.Cover=imported;store.Save();Check(File.Exists(new PlaylistStore(directory).Items[0].Cover)&&Path.GetDirectoryName(imported)==Path.Combine(directory,"playlist-covers"),"Custom cover is copied to managed local storage and survives reload");
  using(var db=new LibraryStore(state))for(int i=0;i<24;i++){string file=Path.Combine(inbox,$"{i:00}.flac");File.Copy(song,file,true);db.Save(new Track{Path=file,Title=$"Parça {i+1:00}",Artist=i<12?"Deniz":"Atlas",AlbumArtist=i<12?"Deniz":"Atlas",Album=$"Albüm {i/4+1}",Cover=cover,Codec="flac",SampleRate=44100,Bits=16,Channels=2,Seconds=120,Number=(uint)(i%4+1),Fields=[new("Vorbis / DATE",(2020+i/4).ToString())],Modified=File.GetLastWriteTimeUtc(file).Ticks});}
  await File.WriteAllTextAsync(Path.Combine(directory,"experience-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
 }
}
