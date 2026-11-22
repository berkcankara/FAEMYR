using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Faemyr;
public sealed class ArtistBiography
{
 public string Artist{get;set;}="";
 public string Language{get;set;}="tr";
 public string Title{get;set;}="";
 public string Text{get;set;}="";
 public string Url{get;set;}="";
 public DateTimeOffset CheckedAt{get;set;}
 public int ContentVersion{get;set;}
}
public sealed class ArtistBiographyService:IDisposable
{
 readonly string directory;readonly HttpClient client;readonly SemaphoreSlim gate=new(1);
 DateTimeOffset nextRequest,blockedUntil;
 public ArtistBiographyService(string directory,HttpMessageHandler? handler=null)
 {
  this.directory=directory;client=handler==null?new(new HttpClientHandler{AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.All}):new(handler);
  client.Timeout=TimeSpan.FromSeconds(12);client.DefaultRequestHeaders.UserAgent.ParseAdd($"Faemyr/{AppBrand.Version} (desktop music player; https://github.com/berkcankara/FAEMYR-Lossless-Music-Player)");
 }
 public static string Language(string language)=>language=="en"?"en":"tr";
 public static string Key(string artist,string language)=>Paths.Hash(ArtistPhotoStore.Identity(artist).ToUpperInvariant()+"|"+Language(language));
 static string Text(JsonElement root,string key)=>root.ValueKind==JsonValueKind.Object&&root.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
 static string Normalize(string value)=>Regex.Replace(value.Normalize(NormalizationForm.FormKC),@"[^\p{L}\p{N}]","").ToUpperInvariant();
 public static Uri Query(string artist,string language)
 {
  artist=ArtistPhotoStore.Identity(artist);string[] titles=[artist,artist+" (band)",artist+" (musician)",artist+" (singer)",artist+" (composer)",artist+" (rapper)",artist+" (DJ)"];
  return new("https://en.wikipedia.org/w/api.php?action=query&format=json&formatversion=2&redirects=1&prop=pageprops%7Ccategories%7Clanglinks&cllimit=100&lllimit=1&llprop=url&lllang="+Language(language)+"&titles="+Uri.EscapeDataString(string.Join('|',titles)));
 }
 public static JsonElement? SelectPage(JsonElement root,string artist)
 {
  if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("query",out var query)||!query.TryGetProperty("pages",out var pages)||pages.ValueKind!=JsonValueKind.Array)return null;
  var matches=pages.EnumerateArray().Where(page=>
  {
   if(page.TryGetProperty("missing",out _)||page.TryGetProperty("pageprops",out var props)&&props.TryGetProperty("disambiguation",out _))return false;
   string title=Regex.Replace(Text(page,"title"),@"\s+\((?:band|musician|singer|composer|rapper|DJ)\)$","",RegexOptions.IgnoreCase);
   return Normalize(title)==Normalize(ArtistPhotoStore.Identity(artist))&&page.TryGetProperty("categories",out var categories)&&categories.ValueKind==JsonValueKind.Array&&categories.EnumerateArray().Any(category=>Regex.IsMatch(Text(category,"title"),@"musician|singer|composer|musical group|music group|musical duo|rock group|rock band|pop group|rapper|disc jockey|record producer|electronic music duo",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant));
  }).ToList();
  var exact=matches.Where(page=>Text(page,"title").Equals(artist,StringComparison.OrdinalIgnoreCase)).ToList();return exact.Count==1?exact[0]:matches.Count==1?matches[0]:null;
 }
 public static bool ArticleUrl(string url,string language)=>Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host==Language(language)+".wikipedia.org"&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&uri.AbsolutePath.StartsWith("/wiki/",StringComparison.Ordinal);
 public static Uri DetailedQuery(string title,string language)=>new("https://"+Language(language)+".wikipedia.org/w/api.php?action=query&format=json&formatversion=2&redirects=1&prop=extracts&explaintext=1&exsectionformat=wiki&titles="+Uri.EscapeDataString(title));
 public static string BiographyText(string text)
 {
  text=text.Replace("\r","").Trim();
  var end=Regex.Match(text,@"(?mi)^={2,}\s*(?:Discography|Selected discography|Videography|Filmography|References|Notes|External links|Bibliography|Diskografi|Seçili diskografi|Videografi|Filmografi|Kaynakça|Notlar|Dış bağlantılar|Bibliyografya)\s*={2,}\s*$");
  if(end.Success&&end.Index>100)text=text[..end.Index].TrimEnd();
  text=Regex.Replace(text,@"(?m)^={2,}\s*(.*?)\s*={2,}\s*$","$1");text=Regex.Replace(text,@"\n{3,}","\n\n");
  if(text.Length<=5000)return text;
  int cut=text.LastIndexOf("\n\n",5000,StringComparison.Ordinal);
  if(cut<2500){cut=text.LastIndexOfAny(['.','!','?'],4999);if(cut<2500)cut=text.LastIndexOf(' ',4999);else cut++;}
  if(cut<2500)cut=5000;if(char.IsHighSurrogate(text[cut-1]))cut--;
  return text[..cut].TrimEnd()+"…";
 }
 static bool Valid(ArtistBiography doc,string artist,string language)=>string.Equals(doc.Artist,artist,StringComparison.OrdinalIgnoreCase)&&doc.Language==language&&doc.Text!=null&&doc.Text.Length<=12000&&(doc.Text.Length==0||ArticleUrl(doc.Url,language));
 public async Task<ArtistBiography?> ReadCache(string artist,string language,CancellationToken ct=default)
 {
  try{string path=Path.Combine(directory,Key(artist,language)+".json");if(!File.Exists(path)||new FileInfo(path).Length>65536)return null;var doc=JsonSerializer.Deserialize<ArtistBiography>(await File.ReadAllTextAsync(path,ct));return doc!=null&&Valid(doc,ArtistPhotoStore.Identity(artist),Language(language))?doc:null;}
  catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException){return null;}
 }
 async Task<JsonDocument?> Send(Uri uri,CancellationToken ct,int redirects=0)
 {
  if(uri.Scheme!="https"||uri.UserInfo.Length>0||!uri.IsDefaultPort||uri.Host is not ("en.wikipedia.org" or "tr.wikipedia.org"))throw new InvalidDataException("Unexpected Wikipedia endpoint.");
  if(blockedUntil>DateTimeOffset.UtcNow)throw new IOException("Wikipedia cooldown.");
  var delay=nextRequest-DateTimeOffset.UtcNow;if(delay>TimeSpan.Zero)await Task.Delay(delay,ct);nextRequest=DateTimeOffset.UtcNow.AddMilliseconds(350);
  using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);
  if((int)response.StatusCode is 301 or 302 or 307 or 308&&response.Headers.Location is {} location&&redirects<3)return await Send(new Uri(uri,location),ct,redirects+1);
  if(response.StatusCode==HttpStatusCode.NotFound)return null;
  if((int)response.StatusCode is 429 or 503){var retry=response.Headers.RetryAfter;blockedUntil=retry?.Date??DateTimeOffset.UtcNow.Add(retry?.Delta??TimeSpan.FromMinutes(1));throw new IOException("Wikipedia cooldown.");}
  response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>1048576)throw new InvalidDataException("Wikipedia response too large.");
  if(response.Content.Headers.ContentType?.MediaType?.Contains("json",StringComparison.OrdinalIgnoreCase)!=true)throw new InvalidDataException("Unexpected Wikipedia response.");
  await using var input=await response.Content.ReadAsStreamAsync(ct);using var output=new MemoryStream();byte[] buffer=new byte[8192];int count;
  while((count=await input.ReadAsync(buffer,ct))>0){if(output.Length+count>1048576)throw new InvalidDataException("Wikipedia response too large.");output.Write(buffer,0,count);}
  return JsonDocument.Parse(output.ToArray());
 }
 public async Task<ArtistBiography?> Get(string artist,string language,CancellationToken ct=default)
 {
  artist=ArtistPhotoStore.Identity(artist);language=Language(language);if(artist.Length==0||artist.Length>180)return null;
  var cached=await ReadCache(artist,language,ct);if(cached!=null&&cached.ContentVersion>=2&&cached.CheckedAt>DateTimeOffset.UtcNow.AddDays(cached.Text.Length>0?-30:-1))return cached;
  if(AppBrand.Offline("ARTIST_BIOGRAPHY"))return cached;
  await gate.WaitAsync(ct);
  try
  {
   var result=new ArtistBiography{Artist=artist,Language=language,CheckedAt=DateTimeOffset.UtcNow,ContentVersion=2};
   using var query=await Send(Query(artist,language),ct);var page=query==null?null:SelectPage(query.RootElement,artist);string title="";
   if(page is {} matched)
   {
    if(language=="en")title=Text(matched,"title");
    else if(matched.TryGetProperty("langlinks",out var links)&&links.ValueKind==JsonValueKind.Array)
     foreach(var link in links.EnumerateArray())if(Text(link,"lang")==language){title=Text(link,"title");break;}
   }
   if(title.Length>0)
   {
    using var summary=await Send(new Uri("https://"+language+".wikipedia.org/api/rest_v1/page/summary/"+Uri.EscapeDataString(title.Replace(' ','_'))),ct);
    if(summary!=null)
    {
     var root=summary.RootElement;string text=Text(root,"extract"),lang=Text(root,"lang");
     if(root.ValueKind==JsonValueKind.Object&&Text(root,"type")=="standard"&&lang==language&&text.Length is >0 and <=12000&&root.TryGetProperty("content_urls",out var urls)&&urls.TryGetProperty("desktop",out var desktop)&&ArticleUrl(Text(desktop,"page"),language))
     {
      result.Title=Text(root,"title");result.Text=text.Trim();result.Url=Text(desktop,"page");
      try
      {
       using var full=await Send(DetailedQuery(result.Title,language),ct);
       if(full?.RootElement is {ValueKind:JsonValueKind.Object} detail&&detail.TryGetProperty("query",out var articleQuery)&&articleQuery.TryGetProperty("pages",out var pages)&&pages.ValueKind==JsonValueKind.Array)
        foreach(var article in pages.EnumerateArray())if(string.Equals(Text(article,"title"),result.Title,StringComparison.OrdinalIgnoreCase))
        {string expanded=BiographyText(Text(article,"extract"));if(expanded.Length>result.Text.Length)result.Text=expanded;break;}
      }
      catch(Exception ex)when(ex is IOException or HttpRequestException or JsonException||ex is OperationCanceledException&&!ct.IsCancellationRequested){result.ContentVersion=1;}
     }
    }
   }
   ct.ThrowIfCancellationRequested();try{PlayerJson.Write(Path.Combine(directory,Key(artist,language)+".json"),result);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
   return result;
  }
  catch(Exception ex)when(cached?.Text.Length>0&&(ex is HttpRequestException or IOException or JsonException||ex is OperationCanceledException&&!ct.IsCancellationRequested)){return cached;}
  finally{gate.Release();}
 }
 public void Dispose()=>client.Dispose();
}
