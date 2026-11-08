using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Ahenk;
public static class LyricsTests
{
 sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> reply):HttpMessageHandler
 {
  public List<Uri> Requests=[];
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Requests.Add(request.RequestUri!);return reply(request,ct);}
 }
 static HttpResponseMessage Response(object value,HttpStatusCode status=HttpStatusCode.OK)=>new(status){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
 public static async Task Run(string directory,Action<bool,string> check)
 {
  string root=Path.Combine(directory,"lyrics-tests");Directory.CreateDirectory(root);
  var lines=Lrc.Parse("[ar:Test]\n[00:01.2] Bir\n[00:02.34][00:04.567] İki\n[00:60.00] geçersiz\n[00:05] \n[offset:+100]");
  check(lines.Count==4&&Math.Abs(lines[0].Seconds-1.3)<.00001&&Math.Abs(lines[2].Seconds-4.667)<.00001,"LRC supports fractional timestamps, repeated lines, empty breaks, offsets and rejects invalid seconds");
  check(Lrc.Active(lines,0)==-1&&Lrc.Active(lines,1.3)==0&&Lrc.Active(lines,4.7)==2&&Lrc.Active(lines,100)==3&&Lrc.Active(lines,2)==0,"Lyrics follow uses the correct line before, at and after timestamps and after backward seeking");
  check(Lrc.Parse("[00:03] Üç\n[00:01] Bir\n[00:01] Bir").Select(l=>l.Text).SequenceEqual(new[]{"Bir","Üç"}),"LRC timestamps sort and exact duplicate lines collapse");
  check(Lrc.Parse("\uFEFF[00:01.001] Türkçe 日本語").Single().Text=="Türkçe 日本語"&&Lrc.Parse("[00:00.01] A\n[offset:-50]").Single().Seconds==0,"Unicode, BOM and negative offset boundaries are handled");
  check(Lrc.Parse(new string('x',262145)).Count==0&&Lrc.Parse("plain text").Count==0,"Oversized or plain text is not treated as timed lyrics");
  var track=new Track{Path=Path.Combine(root,"Music & test.flac"),Modified=12,Title="Bir & iki? 日本語",Artist="Ada feat. Deniz",Album="Albüm",Seconds=60,Fields=[new("Vorbis / LYRICIST","Author"),new("Vorbis / LYRICS","Düz test sözü"),new("APE / SYNCED_LYRICS","[00:01] Zamanlı test satırı")]};
  check(LyricsService.Embedded(track) is {Source:"Dosyadan",Synced.Length:>0},"Embedded timed lyrics take precedence over plain lyrics and lyricist credits");
  check(LyricsService.Embedded(new Track{Fields=[new("Vorbis / LYRICIST","Author")]})==null,"Lyricist credits are not mistaken for song lyrics");
  string get=LyricsService.Request(track,false).AbsoluteUri;check(get.Contains("artist_name=Ada")&&!get.Contains("Deniz")&&get.Contains("duration=60")&&get.Contains("%26")&&!get.Contains("Music"),"Online query encodes metadata, resolves main artist and never sends the local file path");
  check(!LyricsService.Request(new Track{Title="Long",Artist="Ada",Seconds=7200},false).Query.Contains("duration"),"Long tracks do not send a duration outside the provider's supported range");
  var record=new LyricsRecord{Id=37,TrackName=track.Title,ArtistName="Ada",AlbumName=track.Album,Duration=60,PlainLyrics="Test satırı",SyncedLyrics="[00:01] Birinci test\n[00:02] İkinci test"};
  var wrongArtist=new LyricsRecord{Id=1,TrackName=track.Title,ArtistName="Other",Duration=60,PlainLyrics="wrong"};
  var wrongVersion=new LyricsRecord{Id=2,TrackName=track.Title,ArtistName="Ada",Duration=90,PlainLyrics="wrong"};
  check(LyricsService.Match(track,new[]{wrongArtist,wrongVersion,record})==record&&LyricsService.Match(track,new[]{wrongArtist,wrongVersion})==null,"Matching rejects different artists and duration variants before selecting a record");
  var mock=new Handler((_,_)=>Task.FromResult(Response(record)));using var service=new LyricsService(Path.Combine(root,"cache"),mock);
  var doc=await service.FetchAsync(track,default);check(doc is {RemoteId:37,Source:"LRCLIB"}&&Lrc.Parse(doc.Synced).Count==2&&mock.Requests.Count==1,"Online success returns timed lyrics using one GET request");
  await service.SaveAsync(track,doc!);using var reopened=new LyricsService(Path.Combine(root,"cache"));
  check((await reopened.ReadCacheAsync(track)) is {RemoteId:37}&&mock.Requests.Count==1,"Saved online lyrics reload locally without an online request");
  var edited=new Track{Path=track.Path,Modified=13,Title=track.Title,Artist=track.Artist,Album=track.Album,Seconds=track.Seconds};
  check(await reopened.ReadCacheAsync(edited)==null&&LyricsService.Key(track)!=LyricsService.Key(edited),"Changed music metadata invalidates the previous lyrics cache key");
  string cached=Path.Combine(root,"cache",LyricsService.Key(track)+".json");await File.WriteAllTextAsync(cached,"{broken");check(await reopened.ReadCacheAsync(track)==null,"Invalid lyrics cache is ignored without failing playback");
  await File.WriteAllTextAsync(track.Path,"synthetic test content");byte[] original=await File.ReadAllBytesAsync(track.Path);
  check((await service.LocalAsync(track,default))?.Source=="Dosyadan","Embedded lyrics can load entirely offline");
  var sidecarTrack=new Track{Path=track.Path,Artist="Ada",Title="Local",Seconds=60};await File.WriteAllTextAsync(Path.ChangeExtension(track.Path,".lrc"),"[00:00] Yerel test satırı");
  check((await service.LocalAsync(sidecarTrack,default)) is {Source:"Yerel LRC"},"Same-name local LRC is supported without altering the music file");
  check(original.SequenceEqual(await File.ReadAllBytesAsync(track.Path)),"Lyrics extraction, lookup and caching preserve the original music bytes");
  int call=0;var fallbackMock=new Handler((_,_)=>Task.FromResult(++call==1?Response(new{},HttpStatusCode.NotFound):Response(new[]{wrongArtist,wrongVersion,record})));
  using var fallback=new LyricsService(Path.Combine(root,"fallback"),fallbackMock);var fallbackDoc=await fallback.FetchAsync(track,default);
  check(fallbackDoc?.RemoteId==37&&fallbackMock.Requests[1].AbsolutePath=="/api/search","Exact lookup miss falls back to a conservative artist/title/duration search");
  // An empty search response is reached after a proper 404 signature response.
  var absentMock=new Handler((request,_)=>Task.FromResult(request.RequestUri!.AbsolutePath=="/api/get"?Response(new{},HttpStatusCode.NotFound):Response(Array.Empty<LyricsRecord>())));
  using var absent=new LyricsService(Path.Combine(root,"absent"),absentMock);check(await absent.FetchAsync(track,default)==null,"Missing lyrics return a not-found result without claiming the track is instrumental");
  var instrumental=new LyricsRecord{Id=38,TrackName=track.Title,ArtistName="Ada",Duration=60,Instrumental=true};
  var instMock=new Handler((_,_)=>Task.FromResult(Response(instrumental)));using var inst=new LyricsService(Path.Combine(root,"inst"),instMock);
  check((await inst.FetchAsync(track,default)) is {Instrumental:true},"Instrumental state is accepted only from a matching provider record");
  var rateMock=new Handler((_,_)=>{var response=Response(new{},HttpStatusCode.TooManyRequests);response.Headers.RetryAfter=new(TimeSpan.FromSeconds(60));return Task.FromResult(response);});using var rate=new LyricsService(Path.Combine(root,"rate"),rateMock);
  try{await rate.FetchAsync(track,default);}catch(IOException){}try{await rate.FetchAsync(track,default);}catch(IOException){}
  check(rateMock.Requests.Count==2&&rateMock.Requests.Select(r=>r.Host).Distinct().Count()==2,"Retry-After prevents additional requests to each provider during its own cooldown");
  var cancelMock=new Handler(async (_,ct)=>{await Task.Delay(30000,ct);return Response(record);});using var cancellable=new LyricsService(Path.Combine(root,"cancel"),cancelMock);using var cancellation=new CancellationTokenSource(30);bool cancelled=false;
  try{await cancellable.FetchAsync(track,cancellation.Token);}catch(OperationCanceledException){cancelled=true;}check(cancelled&&cancelMock.Requests.Count==1,"Changing track or closing cancels an outstanding request without contacting the fallback");
  var prefs=new PanelPreferences{ShowLyrics=false};check(!prefs.Copy().ShowLyrics&&JsonSerializer.Deserialize<PanelPreferences>(JsonSerializer.Serialize(prefs))?.ShowLyrics==false&&JsonSerializer.Deserialize<PanelPreferences>("{}")!.ShowLyrics,"Lyrics visibility persists and older panel settings retain a sensible default");
  var backupMock=new Handler((request,_)=>Task.FromResult(request.RequestUri!.Host=="lrclib.net"?Response(new{},HttpStatusCode.BadGateway):Response(new{lyrics="Alternatif deneme sözü"})));
  using var backup=new LyricsService(Path.Combine(root,"backup"),backupMock);var backupDoc=await backup.FetchAsync(track,default);
  check(backupDoc is {Source:"lyrics.ovh",RemoteId:0,Synced:"",Instrumental:false}&&backupDoc.Plain=="Alternatif deneme sözü"&&backupMock.Requests.Count==2,"LRCLIB 502 falls back to real plain-lyrics response format without claiming timing or instrumental status");
  check(backup.LastAttempts.Select(a=>a.Status).SequenceEqual(new int?[]{502,200}),"Provider diagnostics identify the failed primary response and successful alternative without logging lyric text");
  await backup.SaveAsync(track,backupDoc!);using var backupReopened=new LyricsService(Path.Combine(root,"backup"));
  check((await backupReopened.ReadCacheAsync(track)) is {Source:"lyrics.ovh",Synced:""},"Alternative-source lyrics persist and reload offline with their original source");
  await backup.FetchAsync(track,default);
  check(backupMock.Requests.Count==3&&backupMock.Requests.Last().Host=="api.lyrics.ovh"&&backup.LastAttempts.First().Result=="Cooldown","Unavailable LRCLIB is temporarily skipped while the working alternative remains available");
  var backupUri=LyricsService.FallbackRequest(new Track{Artist="AC/DC",Title="Title / ? #",Path="C:\\private\\music.flac"});
  check(backupUri.Query.Length==0&&backupUri.AbsoluteUri.Contains("AC%2FDC")&&backupUri.AbsoluteUri.Contains("%3F")&&!backupUri.AbsoluteUri.Contains("private"),"Fallback URL encodes slash artist names and reserved title characters without sending paths");
  var missingPrimary=new Handler((request,_)=>Task.FromResult(request.RequestUri!.Host=="api.lyrics.ovh"?Response(new{lyrics="Bulunan deneme"}):request.RequestUri.AbsolutePath=="/api/get"?Response(new{},HttpStatusCode.NotFound):Response(Array.Empty<LyricsRecord>())));
  using var missingService=new LyricsService(Path.Combine(root,"missing-primary"),missingPrimary);
  check((await missingService.FetchAsync(track,default))?.Source=="lyrics.ovh"&&missingPrimary.Requests.Count==3,"A genuine LRCLIB not-found result can use the alternative after signature and search lookup");
  var mixedRateMock=new Handler((request,_)=>{if(request.RequestUri!.Host=="api.lyrics.ovh")return Task.FromResult(Response(new{lyrics="Başka kaynak"}));var response=Response(new{},HttpStatusCode.TooManyRequests);response.Headers.RetryAfter=new(TimeSpan.FromSeconds(90));return Task.FromResult(response);});
  using var mixedRate=new LyricsService(Path.Combine(root,"mixed-rate"),mixedRateMock);var mixed=await mixedRate.FetchAsync(track,default);await mixedRate.FetchAsync(track,default);
  check(mixed?.Source=="lyrics.ovh"&&mixedRateMock.Requests.Count(r=>r.Host=="lrclib.net")==1&&mixedRateMock.Requests.Count(r=>r.Host=="api.lyrics.ovh")==2,"One provider's Retry-After is respected without blocking another provider");
  var downMock=new Handler((_,_)=>Task.FromResult(Response(new{},HttpStatusCode.BadGateway)));using var down=new LyricsService(Path.Combine(root,"all-down"),downMock);bool failed=false;
  try{await down.FetchAsync(track,default);}catch(IOException){failed=true;}check(failed&&downMock.Requests.Count==2,"Two unavailable providers produce an error rather than a false no-lyrics or instrumental result");
  bool rejected=false;try{await backup.SaveAsync(track,new LyricsDocument{Source="lyrics.ovh",Plain="Test",Synced="[00:01] Test"});}catch(InvalidDataException){rejected=true;}
  check(rejected,"Plain-only alternative cache cannot claim unverified synchronized lyrics");
 }
 public static async Task LiveCheck(string directory)
 {
  Directory.CreateDirectory(directory);var track=new Track{Path=Path.Combine(directory,"network-probe.flac"),Title="Numb",Artist="Linkin Park",Album="Meteora",Seconds=187};
  using var service=new LyricsService(Path.Combine(directory,"cache"));var doc=await service.FetchAsync(track,default)??throw new IOException("Canlı söz bulunamadı.");
  await service.SaveAsync(track,doc);using var reopened=new LyricsService(Path.Combine(directory,"cache"));var cached=await reopened.ReadCacheAsync(track);
  await File.WriteAllTextAsync(Path.Combine(directory,"lyrics-live-results.json"),JsonSerializer.Serialize(new{source=doc.Source,plainCharacters=doc.Plain.Length,timedLines=Lrc.Parse(doc.Synced).Count,cacheReloaded=cached?.Plain==doc.Plain&&cached?.Source==doc.Source,attempts=service.LastAttempts},new JsonSerializerOptions{WriteIndented=true}));
 }
}
