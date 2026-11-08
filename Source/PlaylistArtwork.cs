using System.IO;
using System.Windows.Media.Imaging;

namespace Ahenk;
public static class PlaylistArtwork
{
 public static string Import(string source,string directory,string id)
 {
  var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=1000;image.UriSource=new Uri(Path.GetFullPath(source));image.EndInit();image.Freeze();
  string covers=Path.Combine(directory,"playlist-covers");Directory.CreateDirectory(covers);string target=Path.Combine(covers,id+"-"+Guid.NewGuid().ToString("N")+".png");var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(target))encoder.Save(file);return target;
 }
}
