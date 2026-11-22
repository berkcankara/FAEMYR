using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace Faemyr;
public static class LibraryFingerprint
{
 public static string Read(string path)
 {
  using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);hash.AppendData(BitConverter.GetBytes(file.Length));byte[] bytes=new byte[65536];int n=file.Read(bytes);hash.AppendData(bytes,0,n);if(file.Length>bytes.Length){file.Seek(Math.Max(bytes.Length,file.Length-bytes.Length),SeekOrigin.Begin);n=file.Read(bytes);hash.AppendData(bytes,0,n);}return Convert.ToHexString(hash.GetHashAndReset());
 }
}
public sealed record HealthItem(string Kind,Track Track,string Detail,string? Candidate=null){public string Label=>Kind+" · "+Track.Artist+" — "+Track.Title+"\n"+Detail;}
public sealed record HealthReport(List<HealthItem> Items,List<Track> Baselines);
public static class LibraryHealth
{
 public static async Task<HealthReport> Inspect(IEnumerable<Track> tracks,bool duplicates,IProgress<string>? progress,CancellationToken ct)
 {
  var items=new List<HealthItem>();var baselines=new List<Track>();var files=new List<(Track Track,long Size)>();int i=0;
  foreach(var track in tracks.DistinctBy(t=>t.AudioPath,StringComparer.OrdinalIgnoreCase))
  {
   ct.ThrowIfCancellationRequested();progress?.Report(L.T("Dosyalar kontrol ediliyor · ")+ ++i);
   if(!File.Exists(track.AudioPath)){items.Add(new(L.T("Eksik dosya"),track,track.AudioPath));continue;}
   try{long size=new FileInfo(track.AudioPath).Length;files.Add((track,size));if(track.FileSize!=size||string.IsNullOrEmpty(track.FileSignature)){var copy=track.Copy();copy.FileSize=size;copy.FileSignature=LibraryFingerprint.Read(track.AudioPath);baselines.Add(copy);}}
   catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){items.Add(new(L.T("Okunamadı"),track,ex.Message));}
  }
  if(duplicates)foreach(var size in files.GroupBy(f=>f.Size).Where(g=>g.Count()>1))
  {
   var hashes=new Dictionary<string,List<Track>>();foreach(var file in size)
   {
    ct.ThrowIfCancellationRequested();progress?.Report(L.T("Yinelenenler doğrulanıyor · ")+System.IO.Path.GetFileName(file.Track.AudioPath));
    try{await using var stream=new FileStream(file.Track.AudioPath,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);string hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct));if(!hashes.TryGetValue(hash,out var group))hashes[hash]=group=[];group.Add(file.Track);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){items.Add(new(L.T("Okunamadı"),file.Track,ex.Message));}
   }
   foreach(var group in hashes.Values.Where(g=>g.Count>1))foreach(var t in group)items.Add(new(L.T("Aynı dosya içeriği"),t,t.AudioPath+"\n"+group.Count+L.T(" eş dosya · SHA-256 doğrulandı")));
  }
  return new(items,baselines);
 }
 public static async Task<List<HealthItem>> FindMoved(IEnumerable<Track> missing,IEnumerable<string> candidates,IProgress<string>? progress,CancellationToken ct)
 {
  var files=candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToArray();var signatures=new Dictionary<string,List<string>>();var items=new List<HealthItem>();
  foreach(string path in files)
  {
   ct.ThrowIfCancellationRequested();progress?.Report(L.T("Taşınan dosyalar aranıyor · ")+System.IO.Path.GetFileName(path));try{string signature=LibraryFingerprint.Read(path);if(!signatures.TryGetValue(signature,out var list))signatures[signature]=list=[];list.Add(path);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
  }
  foreach(var track in missing.DistinctBy(t=>t.AudioPath,StringComparer.OrdinalIgnoreCase))
  {
   ct.ThrowIfCancellationRequested();List<string> matches=[];
   if(!string.IsNullOrEmpty(track.FileSignature)&&signatures.TryGetValue(track.FileSignature,out var exact))matches=exact;
   else if(!track.IsCue)
   {
    foreach(string path in files.Where(p=>System.IO.Path.GetFileName(p).Equals(System.IO.Path.GetFileName(track.AudioPath),StringComparison.OrdinalIgnoreCase)))
    {
     if(track.FileSize>0&&new FileInfo(path).Length!=track.FileSize)continue;
     try{var candidate=await MetadataReader.Read(path,ct);if(candidate.Title.Equals(track.Title,StringComparison.OrdinalIgnoreCase)&&candidate.Artist.Equals(track.Artist,StringComparison.OrdinalIgnoreCase)&&candidate.Album.Equals(track.Album,StringComparison.OrdinalIgnoreCase)&&Math.Abs(candidate.Seconds-track.Seconds)<.02&&candidate.SampleRate==track.SampleRate&&candidate.Channels==track.Channels)matches.Add(path);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or NotSupportedException){}
    }
   }
   if(matches.Count==1)items.Add(new(L.T("Taşınmış dosya bulundu"),track,track.AudioPath+"\n→ "+matches[0],matches[0]));else if(matches.Count>1)items.Add(new(L.T("Birden çok aday"),track,string.Join("\n",matches)));
  }return items;
 }
}
