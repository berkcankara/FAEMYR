using System.IO;
using System.Windows.Media.Imaging;

namespace Faemyr;
public static class ArtistHeroArtwork
{
 public static bool IsRectangular(BitmapSource image)=>image.PixelHeight>0&&Math.Abs(image.PixelWidth/(double)image.PixelHeight-1)>.1;
 public static Task<BitmapSource?> LoadOriginal(string path)=>Task.Run<BitmapSource?>(()=>
 {
  try
  {
   using var stream=File.OpenRead(path);
   var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);var frame=decoder.Frames[0];
   if(frame.PixelWidth<32||frame.PixelHeight<32||(long)frame.PixelWidth*frame.PixelHeight>16777216)return null;
   stream.Position=0;var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();return image;
  }
  catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException){return null;}
 });
}
