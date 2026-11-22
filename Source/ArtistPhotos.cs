using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
namespace Faemyr;

public sealed class ArtistPhotoPreferences
{
 public bool Enabled{get;set;}=true;
 public bool OnCards{get;set;}=true;
 public HashSet<string> AlbumCoverArtists{get;set;}=new(StringComparer.OrdinalIgnoreCase);
 public static ArtistPhotoPreferences Load(string path){var p=PlayerJson.Read<ArtistPhotoPreferences>(path)??new();p.AlbumCoverArtists=new(p.AlbumCoverArtists??[],StringComparer.OrdinalIgnoreCase);return p;}
}
public sealed class ArtistPhoto
{
 public string Artist{get;set;}="";
 public string File{get;set;}="";
 public string SourceUrl{get;set;}="";
 public string ArticleUrl{get;set;}="";
 public string Author{get;set;}="";
 public string License{get;set;}="";
 public DateTimeOffset CheckedAt{get;set;}
 public DateTimeOffset RetryAfter{get;set;}
 public bool Local{get;set;}
 [System.Text.Json.Serialization.JsonIgnore] public string Credit=>Local?"Yerel fotoğraf":string.Join(" · ",new[]{Author,License,"Wikimedia Commons"}.Where(s=>s.Length>0));
}
public sealed class ArtistPhotoStore : IDisposable
{
 readonly string directory;readonly HttpClient client;readonly CancellationTokenSource life=new();readonly object gate=new();readonly SemaphoreSlim available=new(0);
 readonly PriorityQueue<Request,int> queue=new();readonly Dictionary<string,Request> pending=new(StringComparer.OrdinalIgnoreCase);readonly ConcurrentDictionary<string,ArtistPhoto> records=new(StringComparer.OrdinalIgnoreCase);
 DateTimeOffset nextRequest,networkBackoff;public bool Disposed{get;private set;}
 sealed class Request(string name,bool force,int priority){public string Name=name;public bool Force=force,Started;public int Priority=priority;public TaskCompletionSource<ArtistPhoto?> Completion=new(TaskCreationOptions.RunContinuationsAsynchronously);}
 public ArtistPhotoStore(string directory,HttpMessageHandler? handler=null)
 {
  this.directory=directory;client=handler==null?new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.All}):new HttpClient(handler);client.Timeout=TimeSpan.FromSeconds(18);client.DefaultRequestHeaders.UserAgent.ParseAdd($"Faemyr/{AppBrand.Version} (Windows desktop music library)");_ = Task.Run(Worker);
 }
 public static string Identity(string name)=>ArtistCatalog.Credits(name).FirstOrDefault()?.Profile.Trim()??"";
 string Key(string name)=>Paths.Hash(Identity(name).ToUpperInvariant());
 string RecordPath(string name)=>Path.Combine(directory,Key(name)+".json");
 public ArtistPhoto? Cached(string name)
 {
  name=Identity(name);if(name.Length==0)return null;if(records.TryGetValue(name,out var cached))return cached;
  try{string path=RecordPath(name);if(!File.Exists(path)||new FileInfo(path).Length>16384)return null;var record=PlayerJson.Read<ArtistPhoto>(path);if(record==null||!string.Equals(record.Artist,name,StringComparison.OrdinalIgnoreCase))return null;record.File=Path.GetFileName(record.File??"");record.Author??="";record.License??="";record.SourceUrl??="";record.ArticleUrl??="";records[name]=record;return record;}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){return null;}
 }
 public string? ImagePath(ArtistPhoto? record)=>record is {File.Length:>0}&&File.Exists(Path.Combine(directory,Path.GetFileName(record.File)))?Path.Combine(directory,Path.GetFileName(record.File)):null;
 public Task<ArtistPhoto?> Get(string name,bool priority=false,bool force=false)
 {
  name=Identity(name);if(name.Length==0||name.Length>180||Disposed)return Task.FromResult<ArtistPhoto?>(null);
  var cached=Cached(name);if(!force&&cached!=null&&cached.RetryAfter>DateTimeOffset.UtcNow&&(cached.File.Length==0||ImagePath(cached)!=null))return Task.FromResult<ArtistPhoto?>(cached);
  if(AppBrand.Offline("ARTIST_PHOTOS"))return Task.FromResult(cached);
  lock(gate)
  {
   if(pending.TryGetValue(name,out var request)){if(priority&&!request.Started&&request.Priority>0){request.Priority=0;queue.Enqueue(request,0);available.Release();}return request.Completion.Task;}
   request=new(name,force,priority?0:10);pending[name]=request;queue.Enqueue(request,request.Priority);available.Release();return request.Completion.Task;
  }
 }
 async Task Worker()
 {
  try{while(true){await available.WaitAsync(life.Token);Request? request=null;lock(gate){while(queue.TryDequeue(out var candidate,out _))if(!candidate.Started){candidate.Started=true;request=candidate;break;}}if(request==null)continue;ArtistPhoto? result=null;try{result=await Fetch(request.Name,life.Token);}catch(Exception ex)when(ex is IOException or InvalidDataException or FormatException or UnauthorizedAccessException or HttpRequestException or JsonException or OperationCanceledException or NotSupportedException or ArgumentException or OverflowException or InvalidOperationException or KeyNotFoundException){result=Cached(request.Name);if(!life.IsCancellationRequested){result??=new(){Artist=request.Name};result.RetryAfter=DateTimeOffset.UtcNow.AddMinutes(15);try{Save(result);}catch(Exception saveError)when(saveError is IOException or UnauthorizedAccessException){}}}finally{lock(gate)pending.Remove(request.Name);request.Completion.TrySetResult(result);}}}catch(OperationCanceledException){}
 }
 async Task<byte[]> Read(Uri uri,int maximum,CancellationToken ct,int redirects=0)
 {
  if(uri.Scheme!="https"||uri.UserInfo.Length>0||!new[]{"en.wikipedia.org","commons.wikimedia.org","upload.wikimedia.org","thumb.wikimedia.org"}.Contains(uri.Host)||!uri.IsDefaultPort)throw new InvalidDataException("Desteklenmeyen görsel kaynağı.");
  if(networkBackoff>DateTimeOffset.UtcNow)throw new HttpRequestException("Görsel servisi beklemede.");
  var delay=nextRequest-DateTimeOffset.UtcNow;if(delay>TimeSpan.Zero)await Task.Delay(delay,ct);nextRequest=DateTimeOffset.UtcNow.AddMilliseconds(600);
  using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);
  if((int)response.StatusCode is 301 or 302 or 303 or 307 or 308&&response.Headers.Location is {} location&&redirects<3)return await Read(new Uri(uri,location),maximum,ct,redirects+1);
  if((int)response.StatusCode is 429 or 503){networkBackoff=DateTimeOffset.UtcNow.AddMinutes(5);throw new HttpRequestException("Görsel servisi yoğun.");}if((int)response.StatusCode==403)networkBackoff=DateTimeOffset.UtcNow.AddMinutes(1);
  response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>maximum)throw new InvalidDataException("Görsel yanıtı çok büyük.");
  await using var input=await response.Content.ReadAsStreamAsync(ct);using var output=new MemoryStream();byte[] buffer=new byte[16384];int read;while((read=await input.ReadAsync(buffer,ct))>0){if(output.Length+read>maximum)throw new InvalidDataException("Görsel yanıtı çok büyük.");output.Write(buffer,0,read);}return output.ToArray();
 }
 public static Uri QueryUri(string name)
 {
  string[] titles=[name,name+" (band)",name+" (musician)",name+" (singer)",name+" (composer)",name+" (rapper)",name+" (DJ)"];
  return new("https://en.wikipedia.org/w/api.php?action=query&format=json&formatversion=2&redirects=1&prop=pageimages%7Cpageprops%7Ccategories&piprop=name%7Cthumbnail&pithumbsize=1024&pilicense=free&cllimit=100&titles="+Uri.EscapeDataString(string.Join('|',titles)));
 }
 static string Text(JsonElement e,string property)=>e.TryGetProperty(property,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
 static string Normalize(string name)=>Regex.Replace(name.Normalize(NormalizationForm.FormKC),@"[^\p{L}\p{N}]","").ToUpperInvariant();
 public static JsonElement? SelectPage(JsonElement root,string name)
 {
  if(!root.TryGetProperty("query",out var query)||!query.TryGetProperty("pages",out var pages)||pages.ValueKind!=JsonValueKind.Array)return null;
  var candidates=new List<JsonElement>();foreach(var page in pages.EnumerateArray())
  {
   if(page.TryGetProperty("missing",out _)||page.TryGetProperty("pageprops",out var props)&&props.TryGetProperty("disambiguation",out _))continue;
   string title=Text(page,"title"),baseTitle=Regex.Replace(title,@"\s+\((?:band|musician|singer|composer|rapper|DJ)\)$","",RegexOptions.IgnoreCase);
   if(Normalize(baseTitle)!=Normalize(name)||!page.TryGetProperty("thumbnail",out var thumb)||Text(thumb,"source").Length==0||Text(page,"pageimage").Length==0)continue;
   if(!page.TryGetProperty("categories",out var categories)||!categories.EnumerateArray().Any(c=>Regex.IsMatch(Text(c,"title"),@"musician|singer|composer|musical group|music group|musical duo|rock group|rock band|pop group|rapper|disc jockey|record producer|electronic music duo",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)))continue;
   candidates.Add(page);
  }
  var exact=candidates.Where(p=>Text(p,"title").Equals(name,StringComparison.OrdinalIgnoreCase)).ToList();if(exact.Count==1)return exact[0];return candidates.Count==1?candidates[0]:null;
 }
 static string Clean(string text){string plain=WebUtility.HtmlDecode(Regex.Replace(text,"<[^>]+>"," ")).Trim();return plain[..Math.Min(plain.Length,500)];}
 async Task<ArtistPhoto> Fetch(string name,CancellationToken ct)
 {
  using var result=JsonDocument.Parse(await Read(QueryUri(name),1048576,ct));var page=SelectPage(result.RootElement,name);
  if(page==null){var missing=new ArtistPhoto{Artist=name,CheckedAt=DateTimeOffset.UtcNow,RetryAfter=DateTimeOffset.UtcNow.AddDays(7)};Save(missing);return missing;}
  string filename=Text(page.Value,"pageimage");var infoUri=new Uri("https://commons.wikimedia.org/w/api.php?action=query&format=json&formatversion=2&prop=imageinfo&iiprop=url%7Cextmetadata&iiurlwidth=1024&titles="+Uri.EscapeDataString("File:"+filename));using var info=JsonDocument.Parse(await Read(infoUri,1048576,ct));
  var file=info.RootElement.GetProperty("query").GetProperty("pages").EnumerateArray().FirstOrDefault();if(!file.TryGetProperty("imageinfo",out var entries)||entries.GetArrayLength()==0)throw new InvalidDataException("Görsel kaynak bilgisi bulunamadı.");var item=entries[0];var metadata=item.GetProperty("extmetadata");string Meta(string key)=>metadata.TryGetProperty(key,out var v)?Clean(Text(v,"value")):"";
  string license=Meta("LicenseShortName"),author=Meta("Artist");if(license.Length==0)throw new InvalidDataException("Fotoğraf lisans bilgisi bulunamadı.");string source=Text(item,"descriptionurl");if(!Uri.TryCreate(source,UriKind.Absolute,out var sourceUri)||sourceUri.Scheme!="https"||sourceUri.Host!="commons.wikimedia.org")throw new InvalidDataException("Görsel kaynağı doğrulanamadı.");
  string imageUrl=Text(item,"thumburl");if(imageUrl.Length==0)imageUrl=Text(item,"url");var imageBytes=await Read(new Uri(imageUrl),8388608,ct);string localFile=Key(name)+"-"+Guid.NewGuid().ToString("N")+".png";WriteImage(imageBytes,Path.Combine(directory,localFile));
  var photo=new ArtistPhoto{Artist=name,File=localFile,SourceUrl=source,ArticleUrl="https://en.wikipedia.org/wiki/"+Uri.EscapeDataString(Text(page.Value,"title").Replace(' ','_')),Author=author,License=license,CheckedAt=DateTimeOffset.UtcNow,RetryAfter=DateTimeOffset.UtcNow.AddDays(90)};Save(photo);return photo;
 }
 public static void WriteImage(byte[] bytes,string path)
 {
  using var stream=new MemoryStream(bytes);var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);var frame=decoder.Frames[0];if(frame.PixelWidth<32||frame.PixelHeight<32||(long)frame.PixelWidth*frame.PixelHeight>16777216)throw new InvalidDataException("Fotoğraf boyutu uygun değil.");
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(frame));Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temporary=path+".tmp";try{using(var output=File.Create(temporary))encoder.Save(output);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 public ArtistPhoto Import(string name,byte[] bytes){name=Identity(name);string file=Key(name)+"-"+Guid.NewGuid().ToString("N")+".png";WriteImage(bytes,Path.Combine(directory,file));var photo=new ArtistPhoto{Artist=name,File=file,Local=true,CheckedAt=DateTimeOffset.UtcNow,RetryAfter=DateTimeOffset.MaxValue};Save(photo);return photo;}
 void Save(ArtistPhoto record){PlayerJson.Write(RecordPath(record.Artist),record);records[record.Artist]=record;}
 public void Dispose(){if(Disposed)return;Disposed=true;life.Cancel();lock(gate){foreach(var request in pending.Values)request.Completion.TrySetCanceled();pending.Clear();}client.Dispose();}
}
