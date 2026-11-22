using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.Wave;
namespace Faemyr;
public static class ArtistPhotoTests
{
 const string Page="{\"query\":{\"pages\":[{\"title\":\"Ada\",\"pageimage\":\"Ada.jpg\",\"thumbnail\":{\"source\":\"https://upload.wikimedia.org/ada.png\"},\"categories\":[{\"title\":\"Category:Turkish singers\"}]}]}}";
 const string Info="{\"query\":{\"pages\":[{\"imageinfo\":[{\"thumburl\":\"https://upload.wikimedia.org/ada.png\",\"descriptionurl\":\"https://commons.wikimedia.org/wiki/File:Ada.jpg\",\"extmetadata\":{\"Artist\":{\"value\":\"<a>Photo Author</a>\"},\"LicenseShortName\":{\"value\":\"CC BY-SA 4.0\"}}}]}]}}";
 sealed class Handler(byte[] image):HttpMessageHandler
 {
  public int Calls;public bool Broken,BadImage,ThumbnailHost;public List<string> Requests=[];
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Calls++;Requests.Add(request.RequestUri!.AbsoluteUri);var response=new HttpResponseMessage(HttpStatusCode.OK);bool isImage=request.RequestUri.Host is "upload.wikimedia.org" or "thumb.wikimedia.org";response.Content=Broken?new StringContent("{"):isImage?new ByteArrayContent(BadImage?Encoding.UTF8.GetBytes("not an image"):image):new StringContent(request.RequestUri.Host=="commons.wikimedia.org"?(ThumbnailHost?Info.Replace("upload.wikimedia.org","thumb.wikimedia.org"):Info):Page,Encoding.UTF8,"application/json");return Task.FromResult(response);}
 }
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
 static object? Call(MainWindow window,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,args);
 static void Set(MainWindow window,string name,object value)=>typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,value);
 static IEnumerable<T> Visuals<T>(DependencyObject node)where T:DependencyObject{if(node is T typed)yield return typed;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Visuals<T>(VisualTreeHelper.GetChild(node,i)))yield return child;}
 static async Task Wait(Func<bool> condition){for(int i=0;i<600;i++){if(condition())return;await Task.Delay(10);}throw new Exception("Artist photo UI timeout");}
 public static async Task Run(string directory,bool live=false)
 {
  Directory.CreateDirectory(directory);var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  MainWindow? window=null;try
  {
   if(live)
   {
    using var actual=new ArtistPhotoStore(Path.Combine(directory,"artist-photos"));foreach(string name in new[]{"Röyksopp","Linkin Park"}){var record=await actual.Get(name,true,true);Check(actual.ImagePath(record)!=null&&record!.SourceUrl.StartsWith("https://commons.wikimedia.org/")&&record.License.Length>0,"Live artist photo downloads with source and license: "+name);var cached=await actual.Get(name);Check(cached?.File==record!.File,"Live photo is reused locally: "+name);}return;
   }
   Environment.SetEnvironmentVariable("LYRA_ARTIST_PHOTOS_OFFLINE",null);
   using var page=JsonDocument.Parse(Page);Check(ArtistPhotoStore.SelectPage(page.RootElement,"Ada")!=null,"Exact music page is accepted");
   using var wrong=JsonDocument.Parse(Page.Replace("Turkish singers","American television series"));Check(ArtistPhotoStore.SelectPage(wrong.RootElement,"Ada")==null,"Non-musical homonyms cannot supply artist photos");
   using var disambiguation=JsonDocument.Parse(Page.Replace("\"title\":\"Ada\"","\"pageprops\":{\"disambiguation\":\"\"},\"title\":\"Ada\""));Check(ArtistPhotoStore.SelectPage(disambiguation.RootElement,"Ada")==null,"Disambiguation page image is rejected");
   using var ambiguous=JsonDocument.Parse("{\"query\":{\"pages\":["+page.RootElement.GetProperty("query").GetProperty("pages")[0].GetRawText().Replace("\"Ada\"","\"Ada (singer)\"")+","+page.RootElement.GetProperty("query").GetProperty("pages")[0].GetRawText().Replace("\"Ada\"","\"Ada (band)\"")+"]}}");Check(ArtistPhotoStore.SelectPage(ambiguous.RootElement,"Ada")==null,"Ambiguous musicians fall back instead of guessing");
   Check(ArtistPhotoStore.QueryUri("A & B / ?").Query.Contains("titles=A%20%26%20B%20%2F%20%3F"),"Artist query is encoded as data");
   var bitmap=BitmapSource.Create(64,64,96,96,PixelFormats.Bgra32,null,Enumerable.Repeat((byte)120,64*64*4).ToArray(),64*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var buffer=new MemoryStream();encoder.Save(buffer);byte[] png=buffer.ToArray();
   var handler=new Handler(png);string cache=Path.Combine(directory,"cache");using(var store=new ArtistPhotoStore(cache,handler))
   {
    var one=store.Get("Ada");var two=store.Get("Ada & Guest",true);Check(ReferenceEquals(one,two),"Simultaneous canonical and guest credits share one request");var photo=await one;Check(store.ImagePath(photo)!=null&&photo!.Author=="Photo Author"&&photo.License=="CC BY-SA 4.0","Downloaded photo is validated and cached with clean attribution");Check(handler.Calls==3,"A photo uses one page lookup, one license lookup and one bounded image download");await store.Get("Ada");Check(handler.Calls==3,"Repeated photo access uses the cache without HTTP");
    handler.Broken=true;var preserved=await store.Get("Ada",true,true);Check(store.ImagePath(preserved)==store.ImagePath(photo),"Failed refresh preserves the last good photo");Check(handler.Requests.All(r=>!r.Contains(".flac")&&!r.Contains("C%3A")),"Photo requests contain artist names and no local music paths");
   }
   var badImageHandler=new Handler(png){BadImage=true,ThumbnailHost=true};using(var badImageStore=new ArtistPhotoStore(Path.Combine(directory,"bad-image"),badImageHandler)){var failed=await badImageStore.Get("Ada");Check(badImageStore.ImagePath(failed)==null,"Invalid downloaded images fall back without stopping the worker");badImageHandler.BadImage=false;var recovered=await badImageStore.Get("Ada",true,true);Check(badImageStore.ImagePath(recovered)!=null,"Image worker recovers and accepts the current Wikimedia thumbnail host");}
   Environment.SetEnvironmentVariable("LYRA_ARTIST_PHOTOS_OFFLINE","1");using(var restarted=new ArtistPhotoStore(cache)){var photo=await restarted.Get("Ada");Check(restarted.ImagePath(photo)!=null&&photo!.SourceUrl.Contains("File:Ada.jpg"),"Cached photo and attribution survive restart while offline");var local=restarted.Import("Ada",png);Check(local.Local&&restarted.ImagePath(local)!=null,"Local override is imported into managed storage");}
   Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Themes.Load();string audioPath=Path.Combine(directory,"silence.wav");using(var writer=new WaveFileWriter(audioPath,new WaveFormat(44100,16,2)))writer.Write(new byte[44100*4],0,44100*4);var track=await MetadataReader.Read(audioPath,default);track.Artist="Ada & Guest";track.AlbumArtist="Ada";track.Album="Photo test";string fallback=Path.Combine(directory,"album.png");File.WriteAllBytes(fallback,png);track.Cover=fallback;using(var db=new LibraryStore())db.Save(track);var savedPhoto=ArtistPhotos.Store.Import("Ada",png);string managed=ArtistPhotos.Store.ImagePath(savedPhoto)!;
   window=new MainWindow{Opacity=0,ShowInTaskbar=false};window.Show();await Wait(()=>Field<bool>(window,"libraryReady")&&Field<bool>(window,"sessionReady"));Call(window,"OpenArtist","Ada & Guest");await Wait(()=>Field<ThumbnailImage>(window,"ArtistPortrait").Path==managed);Check(Field<FittingTextBlock>(window,"ArtistName").Text=="Ada","Artist profile displays the photo under the normalized main identity");
   Set(window,"view","artists");Call(window,"RefreshView");await Wait(()=>Visuals<ArtistPhotoView>(window).Any(v=>v.Path==managed));Check(Visuals<ArtistPhotoView>(window).Single().Artist=="Ada","Visible artist card uses the same cached portrait");ArtistPhotos.Preferences.OnCards=false;ArtistPhotos.Notify("");await Wait(()=>Visuals<ArtistPhotoView>(window).Single().Path==fallback);Check(true,"Turning card photos off restores album artwork");Check(!Field<AudioEngine>(window,"audio").DeviceStarted,"Artist photo loading never starts audio output");
  }
  finally{window?.Close();File.WriteAllText(Path.Combine(directory,live?"artist-photo-live-results.json":"artist-photo-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
