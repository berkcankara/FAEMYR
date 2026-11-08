using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ahenk;
public sealed record LyricLine(double Seconds,string Text);
public sealed class LyricsDocument
{
 public int Version{get;set;}=1;
 public string Key{get;set;}="";
 public string Source{get;set;}="";
 public long RemoteId{get;set;}
 public string Plain{get;set;}="";
 public string Synced{get;set;}="";
 public bool Instrumental{get;set;}
 public bool HasLyrics=>!string.IsNullOrWhiteSpace(Plain)||Lrc.Parse(Synced).Count>0;
}
public static class Lrc
{
 static readonly Regex Stamp=new(@"\[([0-9]{1,4}):([0-9]{2})(?:[\.:]([0-9]{1,3}))?\]",RegexOptions.Compiled);
 static readonly Regex Offset=new(@"\[offset:\s*([+-]?\d+)\]",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);
 public static List<LyricLine> Parse(string? text)
 {
  if(string.IsNullOrEmpty(text)||text.Length>262144)return [];
  double offset=0;var offsetTag=Offset.Matches(text).LastOrDefault();
  if(offsetTag!=null&&int.TryParse(offsetTag.Groups[1].Value,out int millis))offset=millis/1000d;
  var result=new List<LyricLine>();
  foreach(string row in text.Replace("\r","").Split('\n'))
  {
   string input=row.TrimStart('\uFEFF');var stamps=Stamp.Matches(input);if(stamps.Count==0||stamps[0].Index!=0)continue;
   int end=0;foreach(Match stamp in stamps){if(stamp.Index!=end)break;end=stamp.Index+stamp.Length;}
   string lyric=input[end..].Trim();
   foreach(Match stamp in stamps)
   {
    if(stamp.Index>=end)break;
    int minutes=int.Parse(stamp.Groups[1].Value,CultureInfo.InvariantCulture),seconds=int.Parse(stamp.Groups[2].Value,CultureInfo.InvariantCulture);if(seconds>=60)continue;
    double fraction=stamp.Groups[3].Success?int.Parse(stamp.Groups[3].Value,CultureInfo.InvariantCulture)/Math.Pow(10,stamp.Groups[3].Length):0;
    result.Add(new(Math.Max(0,minutes*60+seconds+fraction+offset),lyric));
   }
  }
  return result.OrderBy(l=>l.Seconds).Distinct().ToList();
 }
 public static int Active(IReadOnlyList<LyricLine> lines,double seconds)
 {
  int low=0,high=lines.Count-1,result=-1;
  while(low<=high){int mid=(low+high)/2;if(lines[mid].Seconds<=seconds){result=mid;low=mid+1;}else high=mid-1;}return result;
 }
}
public sealed class LyricsRecord
{
 public long Id{get;set;}
 public string TrackName{get;set;}="";
 public string ArtistName{get;set;}="";
 public string AlbumName{get;set;}="";
 public double Duration{get;set;}
 public bool Instrumental{get;set;}
 public string? PlainLyrics{get;set;}
 public string? SyncedLyrics{get;set;}
}
public sealed record LyricsAttempt(string Provider,int? Status,string Result);
public sealed class LyricsService : IDisposable
{
 readonly string directory;readonly HttpClient client;readonly SemaphoreSlim gate=new(1,1);
 readonly Dictionary<string,DateTimeOffset> blockedUntil=new(StringComparer.OrdinalIgnoreCase);
 readonly List<LyricsAttempt> attempts=[];
 DateTimeOffset lastRequest;
 public IReadOnlyList<LyricsAttempt> LastAttempts=>attempts.AsReadOnly();
 static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true,WriteIndented=true};
 public LyricsService(string directory,HttpMessageHandler? handler=null)
 {
  this.directory=directory;client=handler==null?new HttpClient():new HttpClient(handler);
  client.Timeout=TimeSpan.FromSeconds(20);
  client.DefaultRequestHeaders.UserAgent.ParseAdd("Lyra/0.3.10 (Windows local music player)");
  client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
 }
 public static string Key(Track t)=>Paths.Hash(string.Join('\u001f',t.Path,t.Modified,t.Title,t.Artist,t.Album,t.Seconds.ToString("R",CultureInfo.InvariantCulture)));
 static string Tag(string key)=>key[(key.LastIndexOf('/')+1)..].Trim().Replace("_","").Replace("-","").Replace(" ","").ToUpperInvariant();
 public static LyricsDocument? FromText(string? text,string source)
 {
  if(string.IsNullOrWhiteSpace(text)||text.Length>262144)return null;
  text=text.Replace("\r\n","\n").Replace('\r','\n').Trim('\uFEFF',' ','\n','\t');
  var lines=Lrc.Parse(text);return lines.Count>0?new(){Source=source,Synced=text,Plain=string.Join('\n',lines.Select(l=>l.Text))}:new(){Source=source,Plain=text};
 }
 public static LyricsDocument? Embedded(Track t)
 {
  var fields=t.Fields.Where(f=>Tag(f.Key) is "LYRICS" or "UNSYNCEDLYRICS" or "UNSYNCHRONISEDLYRICS" or "SYNCHRONIZEDLYRICS" or "SYNCHRONISEDLYRICS" or "SYNCEDLYRICS" or "USLT"||Regex.IsMatch(Tag(f.Key),@"^LYRICS[A-Z]{3}$"));
  var docs=fields.Select(f=>FromText(f.Value,"Dosyadan")).OfType<LyricsDocument>().ToList();
  return docs.FirstOrDefault(d=>d.Synced.Length>0)??docs.FirstOrDefault();
 }
 public async Task<LyricsDocument?> LocalAsync(Track track,CancellationToken ct)
 {
  return await Task.Run(async ()=>
  {
   ct.ThrowIfCancellationRequested();var embedded=Embedded(track);if(embedded!=null)return CueLyrics(track,embedded);
   string sidecar=Path.ChangeExtension(track.AudioPath,".lrc");
   try{if(File.Exists(sidecar)&&new FileInfo(sidecar).Length<=262144){var doc=FromText(await File.ReadAllTextAsync(sidecar,ct),"Yerel LRC");if(doc!=null)return CueLyrics(track,doc);}}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
   ct.ThrowIfCancellationRequested();
   try{if(File.Exists(track.AudioPath)){using var file=TagLib.File.Create(track.AudioPath);var doc=FromText(file.Tag.Lyrics,"Dosyadan");if(doc!=null)return CueLyrics(track,doc);}}
   catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException){}
   ct.ThrowIfCancellationRequested();return await ReadCacheAsync(track,ct);
  },ct);
 }
 public static LyricsDocument? CueLyrics(Track track,LyricsDocument doc)
 {
  if(!track.IsCue)return doc;var lines=Lrc.Parse(doc.Synced);if(lines.Count==0)return null;
  double start=track.CueStartFrame/(double)track.SampleRate,end=start+track.Seconds;
  var selected=lines.Where(l=>l.Seconds>=start&&l.Seconds<end).Select(l=>new LyricLine(l.Seconds-start,l.Text)).ToList();
  var carried=lines.LastOrDefault(l=>l.Seconds<start);if(carried!=null&&carried.Text.Length>0&&(selected.Count==0||selected[0].Seconds>0))selected.Insert(0,new(0,carried.Text));
  if(selected.Count==0)return null;
  return new(){Source=doc.Source,Plain=string.Join('\n',selected.Select(l=>l.Text)),Synced=string.Join('\n',selected.Select(l=>"["+((int)(l.Seconds/60)).ToString("D2",CultureInfo.InvariantCulture)+":"+(l.Seconds%60).ToString("00.000",CultureInfo.InvariantCulture)+"]"+l.Text))};
 }
 public async Task<LyricsDocument?> ReadCacheAsync(Track t,CancellationToken ct=default)
 {
  try
  {
   string path=Path.Combine(directory,Key(t)+".json");if(!File.Exists(path)||new FileInfo(path).Length>1048576)return null;
   var doc=JsonSerializer.Deserialize<LyricsDocument>(await File.ReadAllTextAsync(path,ct),Json);
   return doc is {Version:1}&&doc.Key==Key(t)&&RemoteDocument(doc)&&Valid(doc)?doc:null;
  }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){return null;}
 }
 public async Task SaveAsync(Track track,LyricsDocument doc,CancellationToken ct=default)
 {
  if(!RemoteDocument(doc)||!Valid(doc))throw new InvalidDataException("Geçersiz şarkı sözü kaydı.");
  Directory.CreateDirectory(directory);doc.Key=Key(track);string path=Path.Combine(directory,doc.Key+".json"),temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(doc,Json),ct);ct.ThrowIfCancellationRequested();File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
 static bool Valid(LyricsDocument doc)=>doc.Plain!=null&&doc.Synced!=null&&doc.Plain.Length<=262144&&doc.Synced.Length<=262144&&(doc.Instrumental||doc.HasLyrics);
 public static bool IsOnline(LyricsDocument? doc)=>doc?.Source is "LRCLIB" or "lyrics.ovh";
 static bool RemoteDocument(LyricsDocument doc)=>doc.Source=="LRCLIB"&&doc.RemoteId>0||doc.Source=="lyrics.ovh"&&doc.RemoteId==0&&!doc.Instrumental&&string.IsNullOrEmpty(doc.Synced);
 static string Normalize(string? value)=>Regex.Replace((value??"").Normalize(NormalizationForm.FormKC).ToUpperInvariant(),@"[^\p{L}\p{N}]","");
 public static LyricsRecord? Match(Track track,IEnumerable<LyricsRecord> records)
 {
  string artist=ArtistCatalog.Credits(track.Artist).FirstOrDefault()?.Profile??track.Artist;
  return records.Where(r=>r!=null&&r.Id>0&&Normalize(r.TrackName)==Normalize(track.Title)&&Normalize(ArtistCatalog.Credits(r.ArtistName).FirstOrDefault()?.Profile??r.ArtistName)==Normalize(artist))
   .Where(r=>double.IsFinite(r.Duration)&&(track.Seconds<=0||Math.Abs(r.Duration-track.Seconds)<=2))
   .Where(r=>r.Instrumental||!string.IsNullOrWhiteSpace(r.PlainLyrics)||Lrc.Parse(r.SyncedLyrics).Count>0)
   .OrderBy(r=>Normalize(r.AlbumName)==Normalize(track.Album)?0:1).ThenBy(r=>Math.Abs(r.Duration-track.Seconds)).ThenByDescending(r=>!string.IsNullOrWhiteSpace(r.SyncedLyrics)).FirstOrDefault();
 }
 public static Uri Request(Track t,bool search)
 {
  string artist=ArtistCatalog.Credits(t.Artist).FirstOrDefault()?.Profile??t.Artist;
  var args=new List<KeyValuePair<string,string>>{new("track_name",t.Title),new("artist_name",artist)};
  if(!search){if(!string.IsNullOrWhiteSpace(t.Album))args.Add(new("album_name",t.Album));if(double.IsFinite(t.Seconds)&&t.Seconds is >=1 and <=3600)args.Add(new("duration",t.Seconds.ToString("0.###",CultureInfo.InvariantCulture)));}
  return new("https://lrclib.net/api/"+(search?"search":"get")+"?"+string.Join('&',args.Select(p=>Uri.EscapeDataString(p.Key)+"="+Uri.EscapeDataString(p.Value))));
 }
 public static Uri FallbackRequest(Track t)
 {
  string artist=ArtistCatalog.Credits(t.Artist).FirstOrDefault()?.Profile??t.Artist;
  return new("https://api.lyrics.ovh/v1/"+Uri.EscapeDataString(artist)+"/"+Uri.EscapeDataString(t.Title));
 }
 async Task<string?> SendAsync(Uri uri,CancellationToken ct)
 {
  var wait=lastRequest+TimeSpan.FromMilliseconds(350)-DateTimeOffset.UtcNow;if(wait>TimeSpan.Zero)await Task.Delay(wait,ct);
  string host=uri.Host;
  if(blockedUntil.TryGetValue(host,out var until)&&DateTimeOffset.UtcNow<until){attempts.Add(new(host,null,"Cooldown"));throw new IOException("Bu söz kaynağı için bekleme süresi devam ediyor.");}
  try
  {
   using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);
   attempts.Add(new(host,(int)response.StatusCode,"Response"));
   if(response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
   {
    var retry=response.Headers.RetryAfter;var retryUntil=retry?.Date??DateTimeOffset.UtcNow+(retry?.Delta??TimeSpan.FromSeconds(response.StatusCode==HttpStatusCode.TooManyRequests?60:5));
    blockedUntil[host]=retryUntil.AddMilliseconds(Random.Shared.Next(250,1000));throw new IOException("Söz servisi şu anda yoğun. Biraz sonra yeniden dene.");
   }
   if(response.StatusCode==HttpStatusCode.NotFound)return null;
   if(!response.IsSuccessStatusCode){if((int)response.StatusCode>=500)blockedUntil[host]=DateTimeOffset.UtcNow.AddSeconds(60);throw new IOException("Söz servisine şu anda ulaşılamıyor. Biraz sonra yeniden dene.");}
   if(response.Content.Headers.ContentType?.MediaType?.Contains("json",StringComparison.OrdinalIgnoreCase)!=true)throw new InvalidDataException("Söz servisi beklenmeyen bir yanıt gönderdi.");
   await using var stream=await response.Content.ReadAsStreamAsync(ct);using var output=new MemoryStream();byte[] buffer=new byte[8192];int n;
   while((n=await stream.ReadAsync(buffer,ct))>0){if(output.Length+n>1048576)throw new InvalidDataException("Şarkı sözü yanıtı çok büyük.");output.Write(buffer,0,n);}return Encoding.UTF8.GetString(output.ToArray());
  }
  catch(HttpRequestException){blockedUntil[host]=DateTimeOffset.UtcNow.AddSeconds(30);attempts.Add(new(host,null,"Network error"));throw;}
  catch(OperationCanceledException)when(!ct.IsCancellationRequested){blockedUntil[host]=DateTimeOffset.UtcNow.AddSeconds(30);attempts.Add(new(host,null,"Timeout"));throw;}
  finally{lastRequest=DateTimeOffset.UtcNow;}
 }
 async Task<LyricsDocument?> PrimaryAsync(Track track,CancellationToken ct)
 {
  string? json=await SendAsync(Request(track,false),ct);LyricsRecord? record=null;
  if(json!=null){var one=JsonSerializer.Deserialize<LyricsRecord>(json,Json);if(one!=null)record=Match(track,[one]);}
  if(record==null){json=await SendAsync(Request(track,true),ct);if(json!=null)record=Match(track,JsonSerializer.Deserialize<List<LyricsRecord>>(json,Json)??[]);}
  if(record==null)return null;
  var doc=new LyricsDocument{Source="LRCLIB",RemoteId=record.Id,Plain=record.PlainLyrics??"",Synced=record.SyncedLyrics??"",Instrumental=record.Instrumental};
  return Valid(doc)?doc:null;
 }
 async Task<LyricsDocument?> FallbackAsync(Track track,CancellationToken ct)
 {
  string? json=await SendAsync(FallbackRequest(track),ct);if(json==null)return null;
  using var response=JsonDocument.Parse(json);
  if(response.RootElement.ValueKind!=JsonValueKind.Object||!response.RootElement.TryGetProperty("lyrics",out var value)||value.ValueKind!=JsonValueKind.String)return null;
  string text=(value.GetString()??"").Trim();if(text.Length==0||text.Length>262144)return null;
  // This provider supplies plain text only, without a recording-duration or timing claim.
  return new(){Source="lyrics.ovh",Plain=text};
 }
 public async Task<LyricsDocument?> FetchAsync(Track track,CancellationToken ct)
 {
  if(string.IsNullOrWhiteSpace(track.Title)||string.IsNullOrWhiteSpace(track.Artist))throw new IOException("Aramak için parça adı ve sanatçı bilgisi gerekli.");
  await gate.WaitAsync(ct);
  try
  {
   attempts.Clear();Exception? failure=null;
   try{var doc=await PrimaryAsync(track,ct);if(doc!=null)return doc;}
   catch(OperationCanceledException)when(!ct.IsCancellationRequested){failure=new IOException("LRCLIB zaman aşımı.");}
   catch(Exception ex)when(ex is IOException or HttpRequestException or JsonException){failure=ex;}
   ct.ThrowIfCancellationRequested();
   try{var doc=await FallbackAsync(track,ct);if(doc!=null)return doc;}
   catch(OperationCanceledException)when(!ct.IsCancellationRequested){failure=new IOException("Alternatif söz kaynağı zaman aşımı.");}
   catch(Exception ex)when(ex is IOException or HttpRequestException or JsonException){failure=ex;}
   if(failure!=null)throw new IOException("Söz kaynaklarına şu anda ulaşılamıyor veya alternatif kaynakta eşleşme bulunamadı. Daha sonra yeniden dene.",failure);
   return null;
  }finally{gate.Release();}
 }
 public void Dispose(){client.Dispose();}
}
