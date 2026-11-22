using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
using Microsoft.Data.Sqlite;

namespace Faemyr;
public sealed record MetadataField(string Key,string Value)
{
 [System.Text.Json.Serialization.JsonIgnore] public bool IsArtist=>ArtistCatalog.IsArtistField(Key);
 [System.Text.Json.Serialization.JsonIgnore] public string Label=>L.T(Key);
}
public sealed class Track : System.ComponentModel.INotifyPropertyChanged
{
 public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
 bool isCurrent,isAnimating;
 bool isFavorite;int rating;
 [System.Text.Json.Serialization.JsonIgnore] public bool IsFavorite{get=>isFavorite;set{if(isFavorite==value)return;isFavorite=value;PropertyChanged?.Invoke(this,new(nameof(IsFavorite)));PropertyChanged?.Invoke(this,new(nameof(FavoriteAction)));PropertyChanged?.Invoke(this,new(nameof(FavoriteIcon)));}}
 [System.Text.Json.Serialization.JsonIgnore] public int Rating{get=>rating;set{value=Math.Clamp(value,0,5);if(rating==value)return;rating=value;PropertyChanged?.Invoke(this,new(nameof(Rating)));}}
 [System.Text.Json.Serialization.JsonIgnore] public string FavoriteAction=>L.T(IsFavorite?"Favorilerden çıkar":"Favorilere ekle");
 [System.Text.Json.Serialization.JsonIgnore] public string FavoriteIcon=>IsFavorite?"heart-filled":"heart";
 [System.Text.Json.Serialization.JsonIgnore] public bool IsCurrent{get=>isCurrent;set{if(isCurrent==value)return;isCurrent=value;PropertyChanged?.Invoke(this,new(nameof(IsCurrent)));}}
 [System.Text.Json.Serialization.JsonIgnore] public bool IsAnimating{get=>isAnimating;set{if(isAnimating==value)return;isAnimating=value;PropertyChanged?.Invoke(this,new(nameof(IsAnimating)));}}
 WaveformData? waveform;string waveformStatus="Sırada";
 [System.Text.Json.Serialization.JsonIgnore] public WaveformData? Waveform {get=>waveform;set{waveform=value;PropertyChanged?.Invoke(this,new(nameof(Waveform)));}}
 [System.Text.Json.Serialization.JsonIgnore] public string WaveformStatus {get=>waveformStatus;set{waveformStatus=value;PropertyChanged?.Invoke(this,new(nameof(WaveformStatus)));}}
 long bitRate;
 public long BitRate {get=>bitRate;set{if(bitRate==value)return;bitRate=value;PropertyChanged?.Invoke(this,new(nameof(BitRate)));PropertyChanged?.Invoke(this,new(nameof(BitRateLabel)));PropertyChanged?.Invoke(this,new(nameof(BitRateHint)));}} public string BitRateSource {get;set;}="";
 public string BitRateLabel=>BitRate>0?$"{BitRate/1000.0:N0} kbps":"—";
 public string BitRateHint=>"Ortalama bitrate · "+BitRateSource+(IsCue?" · CUE kaynak dosyasının ortalaması":"")+(System.IO.Path.GetExtension(AudioPath).Equals(".wv",StringComparison.OrdinalIgnoreCase)?" · .wvc düzeltme dosyası hariç":"");
 public string Path {get;set;}=""; public string Title {get;set;}=""; public string Artist {get;set;}="";
 public string SourcePath{get;set;}="";public string CueSheetPath{get;set;}="";public long CueStartFrame{get;set;}public long? CueEndFrame{get;set;}
 public long FileSize{get;set;}public DateTimeOffset AddedAt{get;set;}
 public Track Copy(){var copy=(Track)MemberwiseClone();copy.PropertyChanged=null;return copy;}
 public string AlbumEditionKey{get;set;}="";public string EditionLabel{get;set;}="";
 public string FileSignature{get;set;}="";
 public long SourceModified{get;set;}
 [System.Text.Json.Serialization.JsonIgnore] public string AudioPath=>string.IsNullOrEmpty(SourcePath)?Path:SourcePath;
 [System.Text.Json.Serialization.JsonIgnore] public bool IsCue=>!string.IsNullOrEmpty(CueSheetPath);
 public string Album {get;set;}=""; public string AlbumArtist {get;set;}=""; public string Cover {get;set;}="";
 public string Codec {get;set;}=""; public string SampleFormat {get;set;}=""; public string WavPackInfo {get;set;}="";
 string? parsedWavPackInfo,parsedWavPackMode;
 public string WavPackMode
 {
  get
  {
   if(string.IsNullOrEmpty(WavPackInfo))return "";
   if(parsedWavPackInfo!=WavPackInfo)
   {
    parsedWavPackMode=System.Text.RegularExpressions.Regex.Match(WavPackInfo,@"^modalities:\s*(.+)$",System.Text.RegularExpressions.RegexOptions.Multiline|System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value.Trim();
    parsedWavPackInfo=WavPackInfo;
   }
   return parsedWavPackMode??"";
  }
 }
 public int SampleRate {get;set;} public int Bits {get;set;} public int Channels {get;set;}
 public int DsdRate {get;set;}
 [System.Text.Json.Serialization.JsonIgnore] public bool IsDsd=>System.IO.Path.GetExtension(Path).ToLowerInvariant() is ".dsf" or ".dff"||Codec.StartsWith("dsd",StringComparison.OrdinalIgnoreCase)||Codec.Equals("dst",StringComparison.OrdinalIgnoreCase);
 public uint Number {get;set;} public uint Disc {get;set;} public double Seconds {get;set;} public long Modified {get;set;}
 [System.Text.Json.Serialization.JsonIgnore] public uint AlbumDisc=>AlbumDiscs.Number(this);
 public List<MetadataField> Fields {get;set;}=[]; public string ProbeJson {get;set;}="";
 public string Duration => TimeSpan.FromSeconds(Seconds).ToString(Seconds>=3600?@"h\:mm\:ss":@"m\:ss");
 public string Quality => IsDsd?L.T($"DSD{(DsdRate>0?DsdRate/44100:0)} · {DsdRate/1000000.0:0.####} MHz · {Channels} kanal"):L.T($"{Codec.ToUpperInvariant()} · {(Bits>0?Bits+" bit":L.T("kodlanmış ses"))} / {SampleRate/1000.0:0.###} kHz · {Channels} kanal");
 public string AlbumKey => AlbumArtist+"\u001f"+Album+(string.IsNullOrEmpty(AlbumEditionKey)?"":"\u001f"+AlbumEditionKey);
}
public sealed class AlbumCard
{
 public string Key {get;init;}=""; public string Title {get;init;}=""; public string Artist {get;init;}="";
 public string? Cover {get;init;} public int Count {get;init;} public int Year {get;init;}
 public IReadOnlySet<string>? NavigableArtists{get;init;}
 public bool IsFavorite{get;init;}
 public string FavoriteIcon=>IsFavorite?"heart-filled":"heart";
 public string FavoriteAction=>L.T(IsFavorite?"Albümü favorilerden çıkar":"Albümü favorilere ekle");
 public string Detail => L.T($"{Artist} · {Count} parça");
 public string CountLabel => L.T($" · {Count} parça");
 public string Tooltip=>$"{Title}\n{Artist} · "+(Year>0?$"{Year} · ":"")+$"{Count} parça";
}
public static class Paths
{
 public static string Base => AppContext.BaseDirectory;
 public static string Data => (Environment.GetEnvironmentVariable("FAEMYR_DATA_DIRECTORY")??Environment.GetEnvironmentVariable("LYRA_DATA_DIRECTORY")??Environment.GetEnvironmentVariable("AHENK_DATA_DIRECTORY")) is {Length:>0} isolated?System.IO.Path.GetFullPath(isolated):System.IO.Path.Combine(Base,"data");
 public static string Tool(string name) => System.IO.Path.Combine(Base,"tools",name+".exe");
 public static string Hash(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
public static class ProcessTool
{
 public static async Task<string> Run(string exe,IEnumerable<string> args,CancellationToken ct=default)
 {
  var info=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
  foreach(var arg in args)info.ArgumentList.Add(arg);
  using var p=Process.Start(info)??throw new IOException("Ses aracı başlatılamadı.");
  using var reg=ct.Register(()=> {try{if(!p.HasExited)p.Kill(true);}catch{}});
  var stdout=p.StandardOutput.ReadToEndAsync(ct); var stderr=p.StandardError.ReadToEndAsync(ct);
  await p.WaitForExitAsync(ct); var output=await stdout;var error=await stderr;
  if(p.ExitCode!=0)throw new IOException($"{System.IO.Path.GetFileName(exe)}: {error.Trim()}");
  return output+ (string.IsNullOrWhiteSpace(error)?"":"\n"+error);
 }
}
public sealed class LibraryStore : IDisposable
{
 readonly SqliteConnection db;readonly string directory;
 public LibraryStore(string? directory=null)
 {
  var dir=directory??Paths.Data;this.directory=dir;Directory.CreateDirectory(dir);
  db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=System.IO.Path.Combine(dir,"library.db")}.ToString());db.Open();
  using var cmd=db.CreateCommand();cmd.CommandText="PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS tracks(path TEXT PRIMARY KEY, json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS roots(path TEXT PRIMARY KEY);";cmd.ExecuteNonQuery();
 }
 public List<Track> Load()
 {
  var covers=System.IO.Path.Combine(directory,"covers");
  var localCovers=Directory.Exists(covers)?Directory.EnumerateFiles(covers).ToDictionary(p=>System.IO.Path.GetFileName(p)!,p=>p,StringComparer.OrdinalIgnoreCase):new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  using var cmd=db.CreateCommand();cmd.CommandText="PRAGMA encoding";
  bool utf8=string.Equals(cmd.ExecuteScalar() as string,"UTF-8",StringComparison.OrdinalIgnoreCase);
  cmd.CommandText=utf8?"SELECT CAST(json AS BLOB) FROM tracks":"SELECT json FROM tracks";using var r=cmd.ExecuteReader();
  var tracks=new Dictionary<string,Track>(StringComparer.OrdinalIgnoreCase);
  // Common immutable tags (artist, album, year, codec, etc.) can share one
  // instance across tracks. Keep the lookup bounded even with unique tags.
  var commonFields=new HashSet<MetadataField>();
  while(r.Read())try
  {
   var t=utf8?JsonSerializer.Deserialize<Track>(r.GetFieldValue<byte[]>(0)):JsonSerializer.Deserialize<Track>(r.GetString(0));
   if(t==null||string.IsNullOrWhiteSpace(t.Path))continue;
   t.Title??="";t.Artist??="";t.Album??="";t.AlbumArtist??=t.Artist;t.Cover??="";t.Codec??="";t.SampleFormat??="";t.WavPackInfo??="";t.ProbeJson??="";t.BitRateSource??="";t.SourcePath??="";t.CueSheetPath??="";t.AlbumEditionKey??="";t.EditionLabel??="";t.FileSignature??="";
   t.Fields??=[];
   t.Fields.RemoveAll(f=>f==null);
   for(int i=0;i<t.Fields.Count;i++)
   {
    var field=t.Fields[i];
    if(field.Key==null||field.Value==null)t.Fields[i]=field=new(field.Key??"",field.Value??"");
    if(field.Value.Length>96)continue;
    if(commonFields.TryGetValue(field,out var existing))t.Fields[i]=existing;
    else if(commonFields.Count<8192)commonFields.Add(field);
   }
   if(!double.IsFinite(t.Seconds)||t.Seconds<0||t.Seconds>1e9)t.Seconds=0;
   if(localCovers.TryGetValue(System.IO.Path.GetFileName(t.Cover),out var relocated))t.Cover=relocated;
   if(!tracks.TryGetValue(t.Path,out var prior)||t.Modified>=prior.Modified)tracks[t.Path]=t;
  }catch(JsonException){}catch(ArgumentException){}
  return tracks.Values.ToList();
 }
 public List<string> Roots(){using var cmd=db.CreateCommand();cmd.CommandText="SELECT path FROM roots";using var r=cmd.ExecuteReader();var a=new List<string>();while(r.Read())a.Add(r.GetString(0));return a;}
 public void AddRoot(string path){using var c=db.CreateCommand();c.CommandText="INSERT OR IGNORE INTO roots VALUES($p)";c.Parameters.AddWithValue("$p",path);c.ExecuteNonQuery();}
 public void Save(Track t){using var c=db.CreateCommand();c.CommandText="INSERT INTO tracks VALUES($p,$j) ON CONFLICT(path) DO UPDATE SET json=$j";c.Parameters.AddWithValue("$p",t.Path);c.Parameters.AddWithValue("$j",JsonSerializer.Serialize(t));c.ExecuteNonQuery();}
 public void SaveMany(IEnumerable<Track> tracks){using var tx=db.BeginTransaction();using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO tracks VALUES($p,$j) ON CONFLICT(path) DO UPDATE SET json=$j";var p=cmd.Parameters.Add("$p",SqliteType.Text);var j=cmd.Parameters.Add("$j",SqliteType.Text);foreach(var track in tracks){p.Value=track.Path;j.Value=JsonSerializer.Serialize(track);cmd.ExecuteNonQuery();}tx.Commit();}
 public void Delete(string path){using var c=db.CreateCommand();c.CommandText="DELETE FROM tracks WHERE path=$p";c.Parameters.AddWithValue("$p",path);c.ExecuteNonQuery();}
 public void DeleteMany(IEnumerable<string> paths){using var tx=db.BeginTransaction();using var c=db.CreateCommand();c.Transaction=tx;c.CommandText="DELETE FROM tracks WHERE path=$p";var p=c.Parameters.Add("$p",SqliteType.Text);foreach(var path in paths){p.Value=path;c.ExecuteNonQuery();}tx.Commit();}
 public void Dispose()=>db.Dispose();
}
public static class MetadataReader
{
 public static async Task<Track> Read(string path,CancellationToken ct=default,string? dataDirectory=null)
 {
  var t=new Track{Path=path,Title=System.IO.Path.GetFileNameWithoutExtension(path),Artist="Bilinmeyen sanatçı",Album="Adsız albüm",Modified=File.GetLastWriteTimeUtc(path).Ticks,FileSize=new FileInfo(path).Length,AddedAt=DateTimeOffset.UtcNow};
  var json=await ProcessTool.Run(Paths.Tool("ffprobe"),["-v","error","-show_format","-show_streams","-show_chapters","-of","json",path],ct);
  t.SourceModified=t.Modified;t.ProbeJson=json;using var doc=JsonDocument.Parse(json);
  var audio=doc.RootElement.GetProperty("streams").EnumerateArray().FirstOrDefault(s=>s.TryGetProperty("codec_type",out var type)&&type.GetString()=="audio");
  if(audio.ValueKind==JsonValueKind.Undefined)throw new IOException("Ses akışı bulunamadı.");
  t.Codec=audio.GetProperty("codec_name").GetString()??"";t.SampleFormat=audio.TryGetProperty("sample_fmt",out var sampleFormat)?sampleFormat.GetString()??"":"";
  int Int(string key)=>audio.TryGetProperty(key,out var v)&&int.TryParse(v.ToString(),out var i)?i:0;
  t.SampleRate=Int("sample_rate");t.Channels=Int("channels");t.Bits=Int("bits_per_raw_sample");if(t.Bits==0)t.Bits=Int("bits_per_sample");
  double seconds=0;if(doc.RootElement.TryGetProperty("format",out var fmt)&&fmt.TryGetProperty("duration",out var dur))double.TryParse(dur.ToString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out seconds);
  t.Seconds=seconds;
  if(t.IsDsd)try{var dsd=DsdContainer.Read(path);t.DsdRate=dsd.Rate;t.Channels=dsd.Channels;t.Seconds=dsd.Seconds;t.Bits=1;}catch(Exception ex)when(ex is IOException or NotSupportedException){t.DsdRate=t.SampleRate*8;t.Fields.Add(new("DSD / Biçim notu",ex.Message));}
  try
  {
   using var f=TagLib.File.Create(path);var tag=f.Tag;
   t.Title=string.IsNullOrEmpty(tag.Title)?t.Title:tag.Title;t.Artist=tag.Performers.Length>0?string.Join(" / ",tag.Performers):t.Artist;
   t.Album=string.IsNullOrEmpty(tag.Album)?t.Album:tag.Album;t.AlbumArtist=tag.AlbumArtists.Length>0?string.Join(" / ",tag.AlbumArtists):t.Artist;t.Number=tag.Track;t.Disc=tag.Disc;
   var pictures=tag.Pictures;var picture=pictures.FirstOrDefault(p=>p.Type==TagLib.PictureType.FrontCover)??pictures.FirstOrDefault();
   if(picture!=null&&picture.Data.Count<32*1024*1024){var covers=System.IO.Path.Combine(dataDirectory??Paths.Data,"covers");Directory.CreateDirectory(covers);t.Cover=System.IO.Path.Combine(covers,Paths.Hash(path+t.Modified)+".img");await File.WriteAllBytesAsync(t.Cover,picture.Data.Data,ct);}
  }catch(Exception ex) when(ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException){t.Fields.Add(new("Etiket okuyucu",ex.Message));}
  if(string.IsNullOrWhiteSpace(t.AlbumArtist))t.AlbumArtist=t.Artist;
  if(fmt.ValueKind!=JsonValueKind.Undefined&&fmt.TryGetProperty("tags",out var fallbackTags))
  {
   string? Tag(string key)=>fallbackTags.EnumerateObject().FirstOrDefault(p=>p.Name.Equals(key,StringComparison.OrdinalIgnoreCase)).Value is {ValueKind:JsonValueKind.String} value?value.GetString():null;
   if(t.Title==System.IO.Path.GetFileNameWithoutExtension(path)&&Tag("title") is {Length:>0} title)t.Title=title;
   if(t.Artist=="Bilinmeyen sanatçı"&&Tag("artist") is {Length:>0} artist)t.Artist=artist;
   if(t.Album=="Adsız albüm"&&Tag("album") is {Length:>0} album)t.Album=album;
   if(t.AlbumArtist=="Bilinmeyen sanatçı")t.AlbumArtist=Tag("album_artist")??t.Artist;
  }
  try {ReadNativeTags(path,t.Fields);}catch(Exception ex) when(ex is IOException or ArgumentException){t.Fields.Add(new("Ham etiket uyarısı",ex.Message));}
  void Collect(JsonElement obj,string source){if(obj.TryGetProperty("tags",out var tags))foreach(var kv in tags.EnumerateObject())t.Fields.Add(new(source+" / "+kv.Name,kv.Value.ToString()));}
  if(fmt.ValueKind!=JsonValueKind.Undefined)Collect(fmt,"Kapsayıcı");foreach(var s in doc.RootElement.GetProperty("streams").EnumerateArray())Collect(s,"Akış "+s.GetProperty("index"));
  if(System.IO.Path.GetExtension(path).Equals(".wv",StringComparison.OrdinalIgnoreCase))t.WavPackInfo=(await ProcessTool.Run(Paths.Tool("wvunpack"),["-ss",path],ct)).Trim();
  try{t.FileSignature=LibraryFingerprint.Read(path);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){} UpdateBitrate(t);return t;
 }
 public static void UpdateBitrate(Track t)
 {
  if(t.BitRate>0)return;
  if(t.Seconds>0&&System.IO.Path.GetExtension(t.Path).Equals(".flac",StringComparison.OrdinalIgnoreCase))try
  {
   using var file=File.OpenRead(t.Path);using var reader=new BinaryReader(file);if(Encoding.ASCII.GetString(reader.ReadBytes(4))=="fLaC"){bool last=false;while(!last){byte header=reader.ReadByte();last=(header&128)!=0;int length=(reader.ReadByte()<<16)|(reader.ReadByte()<<8)|reader.ReadByte();if(file.Position+length>file.Length)throw new IOException("Eksik FLAC metadata bloğu.");file.Seek(length,SeekOrigin.Current);}long audioBytes=file.Length-file.Position;if(audioBytes>0){t.BitRate=(long)Math.Round(audioBytes*8.0/t.Seconds);t.BitRateSource="FLAC ses verisi ortalaması; kapak ve etiketler hariç";return;}}
  }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
  try{using var doc=JsonDocument.Parse(t.ProbeJson);if(doc.RootElement.TryGetProperty("streams",out var streams))foreach(var s in streams.EnumerateArray())if(s.TryGetProperty("codec_type",out var type)&&type.GetString()=="audio"&&s.TryGetProperty("bit_rate",out var rate)&&long.TryParse(rate.ToString(),out long b)&&b>0){t.BitRate=b;t.BitRateSource="Ses akışı";return;}
   if(doc.RootElement.TryGetProperty("format",out var f)&&f.TryGetProperty("bit_rate",out var r)&&long.TryParse(r.ToString(),out long total)&&total>0){t.BitRate=total;t.BitRateSource="Dosya ortalaması; kapak ve etiketler dahil";}
  }catch(JsonException){}
 }
 // Preserve repeated and nonstandard native fields that a JSON object can collapse.
 static void ReadNativeTags(string path,List<MetadataField> fields)
 {
  using var f=File.OpenRead(path);using var r=new BinaryReader(f,Encoding.UTF8,true);
  if(f.Length>=4&&Encoding.ASCII.GetString(r.ReadBytes(4))=="fLaC")
  {
   bool last=false;while(!last){byte b=r.ReadByte();last=(b&128)!=0;int len=(r.ReadByte()<<16)|(r.ReadByte()<<8)|r.ReadByte();if(f.Position+len>f.Length)throw new IOException("Eksik FLAC metadata bloğu.");
    if((b&127)==4){var data=r.ReadBytes(len);int pos=0;uint ReadLength(){if(pos+4>data.Length)throw new IOException("Eksik Vorbis etiketi.");uint n=BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos,4));pos+=4;return n;}string ReadString(){uint n=ReadLength();if(n>data.Length-pos)throw new IOException("Geçersiz Vorbis etiket uzunluğu.");string s=Encoding.UTF8.GetString(data,pos,(int)n);pos+=(int)n;return s;}fields.Add(new("Vorbis / vendor",ReadString()));uint count=ReadLength();for(uint i=0;i<count;i++){string s=ReadString();int eq=s.IndexOf('=');fields.Add(new("Vorbis / "+(eq<0?s:s[..eq]),eq<0?"":s[(eq+1)..]));}}
    else if((b&127)==6){fields.Add(new("FLAC / PICTURE",$"Gömülü resim bloğu · {len:N0} bayt"));f.Seek(len,SeekOrigin.Current);}else f.Seek(len,SeekOrigin.Current);
   }
  }
  if(f.Length<32)return;long end=f.Length;
  if(end>=128){f.Position=end-128;if(Encoding.ASCII.GetString(r.ReadBytes(3))=="TAG")end-=128;}
  f.Position=end-32;byte[] footer=r.ReadBytes(32);if(Encoding.ASCII.GetString(footer,0,8)!="APETAGEX")return;
  uint size=BinaryPrimitives.ReadUInt32LittleEndian(footer.AsSpan(12,4)),items=BinaryPrimitives.ReadUInt32LittleEndian(footer.AsSpan(16,4));
  if(size<32||size>64*1024*1024||size>end||items>100000)throw new IOException("Geçersiz APEv2 etiketi.");
  f.Position=end-size;for(uint i=0;i<items;i++){if(f.Position+8>end-32)break;uint len=r.ReadUInt32(),flags=r.ReadUInt32();var key=new List<byte>();byte c;while((c=r.ReadByte())!=0){key.Add(c);if(key.Count>1024)throw new IOException("Geçersiz APE anahtarı.");}if(len>end-32-f.Position)throw new IOException("Eksik APE değeri.");var value=r.ReadBytes((int)len);string name="APE / "+Encoding.UTF8.GetString(key.ToArray());if((flags&6)==0)foreach(var part in Encoding.UTF8.GetString(value).Split('\0'))fields.Add(new(name,part));else fields.Add(new(name,$"İkili veri · {len:N0} bayt"));}
 }
}
