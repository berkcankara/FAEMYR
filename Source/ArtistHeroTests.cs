using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Faemyr;
public static class ArtistHeroTests
{
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
 static object? Call(MainWindow window,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,args);
 static void Set(MainWindow window,string name,object value)=>typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,value);
 static async Task Wait(Func<bool> condition){for(int i=0;i<300;i++){if(condition())return;await Task.Delay(10);}throw new Exception("Artist hero timeout");}
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);Environment.SetEnvironmentVariable("LYRA_ARTIST_PHOTOS_OFFLINE","1");Environment.SetEnvironmentVariable("LYRA_ARTIST_BIOGRAPHY_OFFLINE","1");Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Themes.Load();
  var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  MainWindow? window=null;
  try
  {
   byte[] Image(int width,int height)
   {
    byte[] pixels=new byte[width*height*3];for(int y=0;y<height;y++)for(int x=0;x<width;x++){int i=(y*width+x)*3;pixels[i]=(byte)(x*255/width);pixels[i+1]=(byte)(y*180/height);pixels[i+2]=(byte)(255-x*160/width);}
    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width,height,96,96,PixelFormats.Rgb24,null,pixels,width*3)));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
   }
   var photos=new[]{("Wide Artist",2304,768),("Square Artist",512,512),("Tall Artist",512,1024)};
   string fallback=Path.Combine(directory,"album.png");File.WriteAllBytes(fallback,Image(128,128));
   foreach(var (name,width,height) in photos)
   {
    string music=Path.Combine(directory,name+".flac");File.WriteAllText(music,"Metadata fixture, never played.");
    using(var db=new LibraryStore())db.Save(new Track{Path=music,Title="Test",Artist=name,AlbumArtist=name,Album=name,Seconds=60,BitRate=1000,Codec="flac",Cover=fallback});
    ArtistPhotos.Store.Import(name,Image(width,height));
   }
   PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{AutoPlayOnStartup=false});
   window=new MainWindow{Opacity=0,ShowInTaskbar=false,ShowActivated=false};window.Show();await Wait(()=>Field<bool>(window,"libraryReady")&&Field<bool>(window,"sessionReady"));
   var backdrop=Field<System.Windows.Controls.Image>(window,"ArtistBackdrop");var portrait=Field<Border>(window,"ArtistPortraitFrame");var heading=Field<StackPanel>(window,"ArtistHeading");
   foreach(var (name,width,height) in photos)
   {
    Call(window,"OpenArtist",name);
    if(width==height)await Wait(()=>Field<ThumbnailImage>(window,"ArtistPortrait").Path==ArtistPhotos.Store.ImagePath(ArtistPhotos.Store.Cached(name)));
    else await Wait(()=>backdrop.Source is BitmapSource image&&image.PixelWidth==width&&image.PixelHeight==height);
    window.UpdateLayout();
    bool rectangular=width!=height;
    Check(portrait.Visibility==(rectangular?Visibility.Collapsed:Visibility.Visible),"Portrait visibility follows photo shape: "+name);
    Check(heading.Margin.Left==(rectangular?30:198),"Artist title uses the freed portrait space: "+name);
    Check(heading.VerticalAlignment==(rectangular?VerticalAlignment.Bottom:VerticalAlignment.Center),"Rectangular artist title is bottom-left while square portraits retain centered text: "+name);
    Check(Field<TextBlock>(window,"ArtistKind").Visibility==(rectangular?Visibility.Collapsed:Visibility.Visible)&&Field<TextBlock>(window,"ArtistSummary").Visibility==(rectangular?Visibility.Collapsed:Visibility.Visible),"Rectangular photo hero shows only the artist name: "+name);
    Check(backdrop.Opacity==(rectangular?1:.45)&&(rectangular?backdrop.Effect==null:backdrop.Effect is BlurEffect{Radius:12}),"Sharp rectangular background and legacy square-photo blur: "+name);
    Check(Field<FittingTextBlock>(window,"ArtistName").Text==name&&backdrop.Stretch==Stretch.UniformToFill&&RenderOptions.GetBitmapScalingMode(backdrop)==BitmapScalingMode.HighQuality,"Artist name and full-width high-quality artwork remain visible: "+name);
    if(rectangular)
    {
     var bitmap=(BitmapSource)backdrop.Source;Check(bitmap.IsFrozen&&bitmap.PixelWidth==width&&bitmap.PixelHeight==height,"Hero decodes the original photo instead of a 320-pixel thumbnail: "+name);
     Check(Field<ThumbnailImage>(window,"ArtistPortrait").Path==null,"Hidden portrait does not request another thumbnail: "+name);
     var hero=Field<Border>(window,"ArtistHero");var rendered=new RenderTargetBitmap((int)hero.ActualWidth,(int)hero.ActualHeight,96,96,PixelFormats.Pbgra32);rendered.Render(hero);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(rendered));using var file=File.Create(Path.Combine(directory,width>height?"landscape-preview.png":"portrait-preview.png"));png.Save(file);
    }
   }
   Call(window,"OpenArtist","Wide Artist");Call(window,"OpenArtist","Square Artist");await Wait(()=>Field<ThumbnailImage>(window,"ArtistPortrait").Path==ArtistPhotos.Store.ImagePath(ArtistPhotos.Store.Cached("Square Artist")));await Task.Delay(80);
   Check(Field<FittingTextBlock>(window,"ArtistName").Text=="Square Artist"&&portrait.IsVisible&&backdrop.Effect is BlurEffect,"Rapid navigation cannot apply an older rectangular photo to the square profile");
   Call(window,"OpenArtist","Wide Artist");await Wait(()=>backdrop.Effect==null);ArtistPhotos.Preferences.AlbumCoverArtists.Add("Wide Artist");ArtistPhotos.Notify("Wide Artist");await Wait(()=>Field<ThumbnailImage>(window,"ArtistPortrait").Path==fallback);
   Check(portrait.Visibility==Visibility.Visible&&heading.Margin.Left==198&&backdrop.Opacity==.45&&backdrop.Effect is BlurEffect{Radius:35},"Choosing album artwork restores the original portrait, spacing and fallback blur");
   ArtistPhotos.Preferences.AlbumCoverArtists.Remove("Wide Artist");ArtistPhotos.Notify("Wide Artist");await Wait(()=>backdrop.Source is BitmapSource image&&image.PixelWidth==2304);
   var photo=ArtistPhotos.Store.Cached("Wide Artist")!;photo.Local=false;photo.SourceUrl="https://commons.wikimedia.org/wiki/File:Test.png";photo.Author="Test Author";photo.License="CC BY-SA 4.0";ArtistPhotos.Notify("Wide Artist");await Wait(()=>Field<Button>(window,"ArtistPhotoCredit").Visibility==Visibility.Visible);
   Check(Equals(Field<Button>(window,"ArtistPhotoCredit").Tag,photo.SourceUrl)&&Field<Button>(window,"ArtistPhotoCredit").ToolTip.ToString()!.Contains(photo.License),"Wide backgrounds retain the source button and license attribution");
   Set(window,"view","artists");Call(window,"RefreshView");await ThumbnailCache.WhenIdle();
   Check(Field<System.Windows.Controls.ListBox>(window,"Artists").IsVisible,"Artist collection remains available with the existing card layout");
   string corrupt=Path.Combine(directory,"bad.png");File.WriteAllText(corrupt,"not an image");Check(await ArtistHeroArtwork.LoadOriginal(corrupt)==null&&await ArtistHeroArtwork.LoadOriginal(Path.Combine(directory,"missing.png"))==null,"Unreadable full-resolution artwork falls back without an unhandled error");
   Check(!Field<AudioEngine>(window,"audio").DeviceStarted,"Artist hero changes never start audio output");
  }
  finally{window?.Close();File.WriteAllText(Path.Combine(directory,"artist-hero-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
