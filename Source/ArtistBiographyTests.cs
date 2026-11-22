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

namespace Faemyr;
public static class ArtistBiographyTests
{
 sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> respond):HttpMessageHandler
 {
  public List<Uri> Requests=[];
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Requests.Add(request.RequestUri!);return respond(request,ct);}
 }
 static HttpResponseMessage Json(object value,HttpStatusCode status=HttpStatusCode.OK)=>new(status){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
 static object Page(bool translated=true)=>new{query=new{pages=new[]{new{title="Ada",categories=new[]{new{title="Category:Turkish singers"}},langlinks=translated?new[]{new{lang="tr",title="Ada (şarkıcı)"}}:[]}}}};
 static object Summary(string language,string? source=null)=>new{type="standard",lang=language,title="Ada",extract=language=="tr"?"Türkçe sanatçı özeti.":"English artist summary.",content_urls=new{desktop=new{page=source??"https://"+language+".wikipedia.org/wiki/Ada"}}};
 static string Expanded(string language)=>language=="tr"?"Türkçe sanatçı özeti.\n\nSanatçının kuruluşu, müzik kariyeri ve albümleri hakkında daha kapsamlı bir ikinci paragraf. Bu bilgiler aynı dildeki Wikipedia maddesinden gelir.":"English artist summary.\n\nA more detailed second paragraph about the artist's formation, music career and albums. This information comes from the Wikipedia article in the same language.";
 static object Details(string language)=>new{query=new{pages=new[]{new{title="Ada",extract=Expanded(language)+"\n\n== References ==\nReference list not part of the biography."}}}};
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
 static object? Call(MainWindow window,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,args);
 static async Task Wait(Func<bool> ready){for(int i=0;i<400;i++){if(ready())return;await Task.Delay(10);}throw new Exception("Artist biography timeout");}
 public static async Task Live(string directory)
 {
  Directory.CreateDirectory(directory);using var service=new ArtistBiographyService(Path.Combine(directory,"cache"));var results=new List<object>();
  foreach(string language in new[]{"tr","en"})
  {
   var doc=await service.Get("Duran Duran",language);if(doc?.Text.Length is not >=300||doc.ContentVersion!=2||doc.Language!=language||!ArtistBiographyService.ArticleUrl(doc.Url,language))throw new Exception("Live expanded Wikipedia biography failed: "+language);
   results.Add(new{Language=doc.Language,doc.Title,Characters=doc.Text.Length,Source=doc.Url});
  }
  File.WriteAllText(Path.Combine(directory,"artist-biography-live-results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
 }
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  MainWindow? window=null;
  try
  {
   var handler=new Handler((request,_)=>{string language=request.RequestUri!.Host.StartsWith("tr.")?"tr":"en";return Task.FromResult(Json(request.RequestUri.Query.Contains("prop=extracts")?Details(language):request.RequestUri.AbsolutePath=="/w/api.php"?Page():Summary(language)));});
   string cache=Path.Combine(directory,"cache");using(var service=new ArtistBiographyService(cache,handler))
   {
    var turkish=await service.Get("Ada","tr");var english=await service.Get("Ada","en");
    Check(turkish?.Text==Expanded("tr")&&english?.Text==Expanded("en")&&turkish.Language!=english.Language,"Expanded Wikipedia biographies follow the requested language without translating or mixing languages");
    Check(turkish?.ContentVersion==2&&handler.Requests.Count(uri=>uri.Query.Contains("prop=extracts"))==2,"The detailed article request supplies additional paragraphs instead of the first-paragraph summary");
    Check(handler.Requests.Any(uri=>uri.Host=="tr.wikipedia.org"&&Uri.UnescapeDataString(uri.AbsolutePath).Contains("Ada_(şarkıcı)")),"Turkish summary uses the verified Wikipedia interlanguage title");
    Check(ArtistBiographyService.Key("Ada","tr")!=ArtistBiographyService.Key("Ada","en"),"Local biography caches are separated by language");
    int count=handler.Requests.Count;await service.Get("Ada feat. Guest","tr");Check(handler.Requests.Count==count,"Repeated canonical artist access uses the local cache without another network request");
    Check(handler.Requests.All(uri=>!uri.AbsoluteUri.Contains(".flac")&&!uri.AbsoluteUri.Contains("C%3A")),"Wikipedia requests contain artist identity and no local music paths");
   }
   using(var reopened=new ArtistBiographyService(cache,new Handler((_,_)=>throw new Exception("Unexpected network request"))))Check((await reopened.Get("Ada","tr"))?.Text==Expanded("tr"),"Expanded biographies survive service restart");
   string legacyCache=Path.Combine(directory,"legacy");PlayerJson.Write(Path.Combine(legacyCache,ArtistBiographyService.Key("Ada","tr")+".json"),new ArtistBiography{Artist="Ada",Language="tr",Text="Legacy single sentence.",Url="https://tr.wikipedia.org/wiki/Ada",CheckedAt=DateTimeOffset.UtcNow});
   using(var migrated=new ArtistBiographyService(legacyCache,handler))Check((await migrated.Get("Ada","tr")) is {ContentVersion:2} updated&&updated.Text==Expanded("tr"),"Existing fresh single-sentence cache entries are automatically upgraded");
   var unavailableDetails=new Handler((request,_)=>Task.FromResult(request.RequestUri!.Query.Contains("prop=extracts")?Json(new{},HttpStatusCode.ServiceUnavailable):Json(request.RequestUri.AbsolutePath=="/w/api.php"?Page():Summary("tr"))));
   using(var service=new ArtistBiographyService(Path.Combine(directory,"detail-fallback"),unavailableDetails))Check((await service.Get("Ada","tr")) is {ContentVersion:1,Text:"Türkçe sanatçı özeti."},"An unavailable detailed article retains the working summary and remains eligible for refresh");
   Check(ArtistBiographyService.BiographyText(Expanded("tr")+"\n\n== Diskografi ==\nAlbum list")==Expanded("tr"),"Discography and reference lists do not fill the artist biography");
   string longBiography=ArtistBiographyService.BiographyText(string.Join("\n\n",Enumerable.Repeat(new string('x',700),12)));Check(longBiography.Length<=5001&&longBiography.EndsWith('…')&&longBiography.Length>2500,"Long articles stop at a readable paragraph boundary within the biography limit");
   var noTranslation=new Handler((_,_)=>Task.FromResult(Json(Page(false))));using(var service=new ArtistBiographyService(Path.Combine(directory,"missing-translation"),noTranslation)){var doc=await service.Get("Ada","tr");Check(doc?.Text==""&&noTranslation.Requests.Count==1,"Missing Turkish article does not display an English summary");}
   using(var wrong=JsonDocument.Parse("{\"query\":{\"pages\":[{\"title\":\"Ada\",\"categories\":[{\"title\":\"Category:Television series\"}]}]}}"))Check(ArtistBiographyService.SelectPage(wrong.RootElement,"Ada")==null,"Non-musical homonyms are rejected");
   using(var ambiguous=JsonDocument.Parse("{\"query\":{\"pages\":[{\"title\":\"Ada\",\"pageprops\":{\"disambiguation\":\"\"},\"categories\":[{\"title\":\"Category:Singers\"}]}]}}"))Check(ArtistBiographyService.SelectPage(ambiguous.RootElement,"Ada")==null,"Disambiguation articles are rejected");
   foreach(bool wrongLanguage in new[]{true,false})
   {
    var invalid=new Handler((request,_)=>Task.FromResult(Json(request.RequestUri!.AbsolutePath=="/w/api.php"?Page():Summary(wrongLanguage?"en":"tr",wrongLanguage?null:"https://example.com/other"))));
    using var service=new ArtistBiographyService(Path.Combine(directory,"invalid-"+wrongLanguage),invalid);Check((await service.Get("Ada","tr"))?.Text=="",wrongLanguage?"A response in the wrong language cannot populate the card":"Unexpected source domains cannot populate the card");
   }
   var limited=new Handler((_,_)=>{var response=Json(new{},HttpStatusCode.TooManyRequests);response.Headers.RetryAfter=new(TimeSpan.FromMinutes(1));return Task.FromResult(response);});using(var service=new ArtistBiographyService(Path.Combine(directory,"limited"),limited))
   {
    for(int i=0;i<2;i++)try{await service.Get("Ada","en");}catch(IOException){}
    Check(limited.Requests.Count==1,"Wikipedia Retry-After prevents repeated requests during cooldown");
   }
   var slow=new Handler(async(_,ct)=>{await Task.Delay(30000,ct);return Json(Page());});using(var service=new ArtistBiographyService(Path.Combine(directory,"cancelled"),slow)){using var stop=new CancellationTokenSource(30);bool cancelled=false;try{await service.Get("Ada","en",stop.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled&&slow.Requests.Count==1,"Changing artist can cancel an in-flight biography request");}
   string stalePath=Path.Combine(cache,ArtistBiographyService.Key("Ada","tr")+".json");var stale=JsonSerializer.Deserialize<ArtistBiography>(File.ReadAllText(stalePath))!;stale.CheckedAt=DateTimeOffset.UtcNow.AddDays(-60);PlayerJson.Write(stalePath,stale);
   using(var offline=new ArtistBiographyService(cache,new Handler((_,_)=>Task.FromResult(Json(new{},HttpStatusCode.ServiceUnavailable)))))Check((await offline.Get("Ada","tr"))?.Text==stale.Text,"A cached summary remains available when refreshing Wikipedia fails");
   File.WriteAllText(stalePath,"{invalid");using(var corrupt=new ArtistBiographyService(cache,handler))Check(await corrupt.ReadCache("Ada","tr")==null,"Corrupt cached biographies do not interrupt the application");

   Environment.SetEnvironmentVariable("LYRA_ARTIST_BIOGRAPHY_OFFLINE","1");Environment.SetEnvironmentVariable("LYRA_ARTIST_PHOTOS_OFFLINE","1");Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;L.Apply("tr");Themes.Load();
   string music=Path.Combine(directory,"fixture.flac");File.WriteAllText(music,"Metadata fixture, never decoded.");
   using(var db=new LibraryStore())for(int i=0;i<12;i++)db.Save(new Track{Path=music+"-"+i,Artist="Ada",AlbumArtist="Ada",Title="Test",Album="Album "+i,Seconds=60,BitRate=1000,Codec="flac"});
   var image=new PngBitmapEncoder();image.Frames.Add(BitmapFrame.Create(BitmapSource.Create(640,240,96,96,PixelFormats.Bgr24,null,Enumerable.Repeat((byte)80,640*240*3).ToArray(),640*3)));using(var bytes=new MemoryStream()){image.Save(bytes);ArtistPhotos.Store.Import("Ada",bytes.ToArray());}
   foreach(string language in new[]{"tr","en"})PlayerJson.Write(Path.Combine(Paths.Data,"artist-biographies",ArtistBiographyService.Key("Ada",language)+".json"),new ArtistBiography{Artist="Ada",Language=language,Title="Ada",Text=language=="tr"?"Türkçe sanatçı özeti.":"English artist summary.",Url="https://"+language+".wikipedia.org/wiki/Ada",CheckedAt=DateTimeOffset.UtcNow});
   PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{AutoPlayOnStartup=false});window=new MainWindow{Opacity=0,ShowActivated=false,ShowInTaskbar=false};window.Show();await Wait(()=>Field<bool>(window,"sessionReady"));int originalAlbumIndex=Field<Grid>(window,"TrackArea").Children.IndexOf(Field<Grid>(window,"AlbumScroll"));Call(window,"OpenArtist","Ada");await Wait(()=>Field<TextBlock>(window,"ArtistBiographyText").Text=="Türkçe sanatçı özeti."&&Field<System.Windows.Controls.Image>(window,"ArtistBackdrop").Effect==null);window.UpdateLayout();
   var scroll=Field<ScrollViewer>(window,"ArtistProfileScroll");var content=Field<StackPanel>(window,"ArtistProfileContent");var hero=Field<Border>(window,"ArtistHero");var albums=Field<Grid>(window,"AlbumScroll");var card=Field<Border>(window,"ArtistAboutCard");
   Check(hero.ActualHeight>=320&&hero.ActualHeight<=480,"Artist photo uses the reduced height so albums start higher");
   Check(Field<StackPanel>(window,"ArtistHeading").VerticalAlignment==VerticalAlignment.Bottom,"The artist name is anchored to the lower-left of rectangular artwork");
   Check(content.Children.IndexOf(hero)==0&&content.Children.IndexOf(albums)==2&&content.Children.IndexOf(card)==3,"Photo, album toolbar, album rows and biography share one scroll flow");
   Check(card.TranslatePoint(new Point(),content).Y>=albums.TranslatePoint(new Point(),content).Y+albums.ActualHeight+27,"About card appears below every album row with clear spacing");
   Check(scroll.ScrollableHeight>0&&ScrollViewer.GetVerticalScrollBarVisibility(Field<ListBox>(window,"Albums"))==ScrollBarVisibility.Disabled,"The outer artist page scrolls instead of trapping the biography below an inner album scroller");
   scroll.ScrollToBottom();await Task.Delay(80);window.UpdateLayout();Check(card.TransformToAncestor(scroll).TransformBounds(new Rect(card.RenderSize)).Bottom<=scroll.ActualHeight+.5,"Biography card is reachable above the bottom player");
   Check(Field<TextBlock>(window,"ArtistBiographyHeading").Text=="Sanatçı hakkında"&&Equals(Field<Button>(window,"ArtistBiographySource").Tag,"https://tr.wikipedia.org/wiki/Ada"),"Turkish card displays the localized heading and correct article link");
   L.Apply("en");Call(window,"RefreshView");await Wait(()=>Field<TextBlock>(window,"ArtistBiographyText").Text=="English artist summary.");Check(Field<TextBlock>(window,"ArtistBiographyHeading").Text=="About the artist"&&Equals(Field<Button>(window,"ArtistBiographySource").Tag,"https://en.wikipedia.org/wiki/Ada"),"Changing application language reloads the matching cached biography");
   Call(window,"Albums_Click",window,new RoutedEventArgs());window.UpdateLayout();Check(!scroll.IsVisible&&ReferenceEquals(albums.Parent,Field<Grid>(window,"TrackArea"))&&Field<Grid>(window,"TrackArea").Children.IndexOf(albums)==originalAlbumIndex&&ScrollViewer.GetVerticalScrollBarVisibility(Field<ListBox>(window,"Albums"))==ScrollBarVisibility.Auto,"Leaving an artist restores the original album gallery, child order and scrolling");
   Call(window,"OpenArtist","Ada");await Wait(()=>Field<TextBlock>(window,"ArtistBiographyText").Text=="English artist summary.");Check(content.Children.Count==4&&ReferenceEquals(card.Parent,content),"Reopening a profile does not duplicate or lose album and biography elements");
   Check(!Field<AudioEngine>(window,"audio").DeviceStarted,"Artist page and biography requests never open audio output");
  }
  finally{window?.Close();L.Apply("tr");File.WriteAllText(Path.Combine(directory,"artist-biography-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
