using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Ahenk;
public sealed record CuePart(string File,int Number,string Title,string Artist,long CdFrames);
public sealed record CueDocument(string Title,string Artist,string Date,List<CuePart> Parts);
public static class CueSheet
{
 static string Value(string text){text=text.Trim();return text.Length>=2&&text[0]=='"'&&text[^1]=='"'?text[1..^1]:text;}
 public static CueDocument Parse(string text)
 {
  string album="",artist="",date="",file="",title="",performer="";int number=0;long? index=null;var parts=new List<CuePart>();
  void Finish(){if(number>0&&file.Length>0&&index.HasValue)parts.Add(new(file,number,title.Length>0?title:$"Parça {number}",performer.Length>0?performer:artist,index.Value));index=null;}
  foreach(var line in text.Split('\n').Select(l=>l.Trim().TrimStart('\uFEFF')))
  {
   var m=Regex.Match(line,@"^(\w+)\s+(.*)$");if(!m.Success)continue;string key=m.Groups[1].Value.ToUpperInvariant(),value=m.Groups[2].Value;
   switch(key)
   {
    case "FILE":Finish();number=0;var f=Regex.Match(value,"^\"([^\"]+)\"\\s+\\S+$|^(\\S+)\\s+\\S+$");if(!f.Success)throw new InvalidDataException("CUE FILE satırı geçersiz.");file=f.Groups[1].Success?f.Groups[1].Value:f.Groups[2].Value;break;
    case "TRACK":Finish();var tr=Regex.Match(value,@"^(\d+)\s+AUDIO$",RegexOptions.IgnoreCase);number=tr.Success?int.Parse(tr.Groups[1].Value,CultureInfo.InvariantCulture):0;title=performer="";break;
    case "TITLE":if(number>0)title=Value(value);else album=Value(value);break;
    case "PERFORMER":if(number>0)performer=Value(value);else artist=Value(value);break;
    case "REM":if(value.StartsWith("DATE ",StringComparison.OrdinalIgnoreCase))date=Value(value[5..]);break;
    case "INDEX":var ix=Regex.Match(value,@"^01\s+(\d+):(\d{2}):(\d{2})$");if(ix.Success){int seconds=int.Parse(ix.Groups[2].Value),frames=int.Parse(ix.Groups[3].Value);if(seconds>=60||frames>=75)throw new InvalidDataException("CUE INDEX zamanı geçersiz.");index=(long.Parse(ix.Groups[1].Value)*60+seconds)*75+frames;}break;
   }
  }
  Finish();if(parts.Count==0)throw new InvalidDataException("CUE içinde AUDIO / INDEX 01 parçaları bulunamadı.");
  if(parts.Select(p=>p.Number).Distinct().Count()!=parts.Count)throw new InvalidDataException("CUE parça numaraları benzersiz olmalı.");
  foreach(var group in parts.GroupBy(p=>p.File,StringComparer.OrdinalIgnoreCase)){long previous=-1;foreach(var p in group){if(p.CdFrames<=previous)throw new InvalidDataException("CUE parça başlangıçları artan sırada olmalı.");previous=p.CdFrames;}}
  return new(album,artist,date,parts);
 }
 public static async Task<List<Track>> Read(string path,CancellationToken token)
 {
  if(new FileInfo(path).Length>2*1024*1024)throw new InvalidDataException("CUE dosyası çok büyük.");string text;
  byte[] bytes=await File.ReadAllBytesAsync(path,token);
  if(bytes.Length>=2&&bytes[0]==0xff&&bytes[1]==0xfe)text=Encoding.Unicode.GetString(bytes,2,bytes.Length-2);
  else if(bytes.Length>=2&&bytes[0]==0xfe&&bytes[1]==0xff)text=Encoding.BigEndianUnicode.GetString(bytes,2,bytes.Length-2);
  else try{text=new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);text=Encoding.GetEncoding(1254).GetString(bytes);}
  var cue=Parse(text);var sources=new Dictionary<string,Track>(StringComparer.OrdinalIgnoreCase);var result=new List<Track>();
  foreach(var part in cue.Parts)
  {
   token.ThrowIfCancellationRequested();string source=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!,part.File));
   if(!sources.TryGetValue(source,out var original)){original=await MetadataReader.Read(source,token);if(original.IsDsd)throw new NotSupportedException("CUE sanal parçaları PCM kaynaklarını destekler.");sources[source]=original;}
   long start=(long)Math.Round(part.CdFrames*original.SampleRate/75d);var next=cue.Parts.SkipWhile(p=>!ReferenceEquals(p,part)).Skip(1).FirstOrDefault(p=>p.File.Equals(part.File,StringComparison.OrdinalIgnoreCase));
   long total=(long)Math.Round(original.Seconds*original.SampleRate);long end=next==null?total:(long)Math.Round(next.CdFrames*original.SampleRate/75d);if(end<=start||end>total||start<0)throw new InvalidDataException("CUE aralığı kaynak süresi dışında.");
   var t=JsonSerializer.Deserialize<Track>(JsonSerializer.Serialize(original))!;t.Path=System.IO.Path.GetFullPath(path)+"#track="+part.Number;t.SourcePath=source;t.CueSheetPath=System.IO.Path.GetFullPath(path);t.CueStartFrame=start;t.CueEndFrame=end;t.Title=part.Title;t.Artist=part.Artist.Length>0?part.Artist:original.Artist;t.Album=cue.Title.Length>0?cue.Title:original.Album;t.AlbumArtist=cue.Artist.Length>0?cue.Artist:original.AlbumArtist;t.Number=(uint)part.Number;t.Seconds=(end-start)/(double)t.SampleRate;t.Modified=Math.Max(t.Modified,File.GetLastWriteTimeUtc(path).Ticks);if(cue.Date.Length>0)t.Fields.Add(new("CUE / DATE",cue.Date));t.Fields.Add(new("CUE / Aralık",$"{start}–{end} örnek"));result.Add(t);
  }
  return result;
 }
}
