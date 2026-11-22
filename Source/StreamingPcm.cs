using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using NAudio.Wave;
namespace Faemyr;
public interface IAbortableAudio{void Abort();}
public interface IRealtimeAudio{bool EndOfStream{get;}bool Buffering{get;}}
// Pipe I/O happens only on the bounded buffer's producer, never on the WASAPI thread.
public sealed class DecoderWaveStream : WaveStream,IAbortableAudio
{
 readonly Track track;Process? process;Task<string>? stderr;long position,remaining,skipBytes;volatile bool aborted;bool eof,headerRead;readonly WaveFormat format;
 public override WaveFormat WaveFormat=>format;public override long Length=>(long)Math.Round(track.Seconds*format.SampleRate)*format.BlockAlign;
 public override long Position{get=>position;set=>Restart(Math.Clamp(value/format.BlockAlign,0,Length/format.BlockAlign));}
 public static WaveFormat Format(Track track)
 {
  if(track.IsDsd)throw new NotSupportedException("DSD, PCM decoder yoluna gönderilmez.");
  if(track.SampleRate is <1 or >1536000||track.Channels is <1 or >32)throw new InvalidDataException("Kaynak örnekleme oranı veya kanal sayısı geçersiz.");
  bool floating=track.SampleFormat.StartsWith('f')||track.SampleFormat.StartsWith('d')||track.Bits==0;int bits=floating?(track.SampleFormat.StartsWith('d')?64:32):track.Bits switch{<=8=>8,<=16=>16,<=24=>24,_=>32};
  return floating?WaveFormat.CreateCustomFormat(WaveFormatEncoding.IeeeFloat,track.SampleRate,track.Channels,checked(track.SampleRate*track.Channels*(bits/8)),track.Channels*(bits/8),bits):new WaveFormat(track.SampleRate,bits,track.Channels);
 }
 public DecoderWaveStream(Track track,TimeSpan? start=null)
 {
  this.track=track;format=Format(track);var info=new FileInfo(track.AudioPath);long modified=track.SourceModified>0?track.SourceModified:track.IsCue?0:track.Modified;
  if(!info.Exists)throw new FileNotFoundException("Ses dosyası bulunamadı.",track.AudioPath);
  if(track.FileSize>0&&track.FileSize!=info.Length||modified>0&&modified!=info.LastWriteTimeUtc.Ticks)throw new InvalidDataException("Ses dosyası arşiv taramasından sonra değişti. Arşivi yenileyip tekrar dene.");
  Restart((long)Math.Round((start??TimeSpan.Zero).TotalSeconds*format.SampleRate));
 }
 void Restart(long frame)
 {
  Kill();process?.Dispose();aborted=false;eof=false;headerRead=false;position=frame*format.BlockAlign;long absolute=checked(track.CueStartFrame+frame);remaining=track.CueEndFrame.HasValue?Math.Max(0,track.CueEndFrame.Value-absolute)*format.BlockAlign:long.MaxValue;
  bool wv=System.IO.Path.GetExtension(track.AudioPath).Equals(".wv",StringComparison.OrdinalIgnoreCase);skipBytes=wv?0:absolute%format.SampleRate*format.BlockAlign;var start=new ProcessStartInfo(Paths.Tool(wv?"wvunpack":"ffmpeg")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
  var args=new List<string>();if(wv){args.AddRange(["-q","-w","--skip="+absolute]);if(track.CueEndFrame.HasValue)args.Add("--until="+track.CueEndFrame.Value);args.AddRange([track.AudioPath,"-"]);}
  else
  {
   // Seek to an exact whole second, then discard a counted number of PCM frames.
   // Fractional timestamp seeking can round a FLAC boundary by one sample.
   args.AddRange(["-v","error","-nostdin"]);if(absolute>=format.SampleRate)args.AddRange(["-ss",(absolute/format.SampleRate).ToString(CultureInfo.InvariantCulture)]);
   args.AddRange(["-i",track.AudioPath,"-map","0:a:0","-vn","-sn","-dn"]);
   string raw=format.Encoding==WaveFormatEncoding.IeeeFloat?(format.BitsPerSample==64?"f64le":"f32le"):format.BitsPerSample==8?"u8":$"s{format.BitsPerSample}le";
   args.AddRange(["-c:a","pcm_"+raw,"-f","wav","pipe:1"]);
  }
  foreach(string arg in args)start.ArgumentList.Add(arg);process=Process.Start(start)??throw new IOException("Akış decoder'ı başlatılamadı.");stderr=DrainError(process.StandardError);
 }
 static async Task<string> DrainError(StreamReader reader){var result=new StringBuilder();char[] chars=new char[2048];int count;while((count=await reader.ReadAsync(chars))>0)if(result.Length<16384)result.Append(chars,0,Math.Min(count,16384-result.Length));return result.ToString();}
 void ReadHeader()
 {
  using var reader=new BinaryReader(process!.StandardOutput.BaseStream,Encoding.UTF8,true);string Four()=>Encoding.ASCII.GetString(reader.ReadBytes(4));string container=Four();if(container is not ("RIFF" or "RF64"))throw new InvalidDataException("Decoder geçerli PCM/WAV başlığı döndürmedi; ses çıkışı engellendi.");reader.ReadUInt32();if(Four()!="WAVE")throw new InvalidDataException("Decoder WAVE türü geçersiz.");bool matched=false;
  while(true)
  {
   string key=Four();uint length=reader.ReadUInt32();if(key=="data"){if(!matched)throw new InvalidDataException("PCM biçimi doğrulanmadan ses verisi geldi.");headerRead=true;return;}
   if(length>16*1024*1024)throw new InvalidDataException("Decoder başlığı desteklenen sınırı aşıyor.");byte[] data=reader.ReadBytes((int)length);if(data.Length!=length)throw new EndOfStreamException("Decoder WAV başlığı eksik.");if((length&1)!=0)reader.ReadByte();
   if(key!="fmt ")continue;if(length<16)throw new InvalidDataException("Decoder PCM başlığı eksik.");int encoding=BitConverter.ToUInt16(data),channels=BitConverter.ToUInt16(data,2),rate=BitConverter.ToInt32(data,4),average=BitConverter.ToInt32(data,8),align=BitConverter.ToUInt16(data,12),bits=BitConverter.ToUInt16(data,14);
   if(encoding==0xfffe){if(length<40)throw new InvalidDataException("Genişletilmiş PCM başlığı eksik.");var subtype=new Guid(data.AsSpan(24,16));encoding=subtype==new Guid("00000001-0000-0010-8000-00aa00389b71")?1:subtype==new Guid("00000003-0000-0010-8000-00aa00389b71")?3:0;}
   var actual=WaveFormat.CreateCustomFormat((WaveFormatEncoding)encoding,rate,channels,average,align,bits);
   if(encoding is not (1 or 3)||align!=channels*bits/8||average!=rate*align||!AudioFormats.SameSignalFormat(actual,format))throw new InvalidDataException($"Decoder'ın gerçek PCM biçimi arşiv bilgisiyle uyuşmuyor ({actual}); çıkış engellendi. Arşivi yenile.");matched=true;
  }
 }
 public override int Read(byte[] buffer,int offset,int count)
 {
  if(aborted)throw new OperationCanceledException();if(eof||remaining==0)return 0;if(!headerRead)ReadHeader();if(skipBytes>0){byte[] discard=new byte[8192];while(skipBytes>0){int n=process!.StandardOutput.BaseStream.Read(discard,0,(int)Math.Min(skipBytes,discard.Length));if(n==0)throw new EndOfStreamException("İleri sarma noktası ses verisi dışında.");skipBytes-=n;}}
  count=(int)Math.Min(count-count%format.BlockAlign,remaining);int read=0;
  while(read<count){int n=process!.StandardOutput.BaseStream.Read(buffer,offset+read,count-read);if(n==0){eof=true;break;}read+=n;}
  if(aborted)throw new OperationCanceledException();if(read%format.BlockAlign!=0)throw new InvalidDataException("Decoder eksik PCM çerçevesi döndürdü.");
  position+=read;remaining-=read;
  if(eof){process!.WaitForExit();if(process.ExitCode!=0)throw new IOException("Ses decoder'ı durdu: "+stderr!.GetAwaiter().GetResult());if(track.CueEndFrame.HasValue&&remaining>format.BlockAlign)throw new InvalidDataException("CUE aralığı tamamlanmadan ses dosyası sona erdi.");}
  return read;
 }
 void Kill(){try{if(process is {} p&&!p.HasExited)p.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
 public void Abort(){aborted=true;Kill();}
 protected override void Dispose(bool disposing){if(disposing){Abort();process?.Dispose();process=null;}base.Dispose(disposing);}
}
public sealed class BufferedAudioSource : IWaveProvider,IRealtimeAudio,IDisposable
{
 readonly IWaveProvider source;readonly Action abort;readonly object gate=new();readonly byte[] ring;readonly Thread producer;int head,used;bool ended,disposed,primed;Exception? error;
 public WaveFormat WaveFormat=>source.WaveFormat;
 public bool EndOfStream{get{lock(gate)return ended&&used==0;}}
 public bool Buffering{get{lock(gate)return !ended&&used==0;}}
 public int BufferedBytes{get{lock(gate)return used;}}
 public BufferedAudioSource(IWaveProvider source,Action abort)
 {
  this.source=source;this.abort=abort;ring=new byte[Math.Max(source.WaveFormat.BlockAlign*4096,Math.Min(32*1024*1024,checked(source.WaveFormat.AverageBytesPerSecond*2)))];producer=new Thread(Produce){IsBackground=true,Name="Faemyr PCM buffer"};producer.Start();
 }
 void Produce()
 {
  byte[] block=new byte[source.WaveFormat.BlockAlign*2048];try
  {
   while(true)
   {
    lock(gate){while(!disposed&&ring.Length-used<block.Length)Monitor.Wait(gate);if(disposed)return;}
    int n=ExactWasapiOut.ValidateRead(source,block,block.Length);if(n==0){lock(gate){ended=true;Monitor.PulseAll(gate);}return;}
    lock(gate){if(disposed)return;int tail=(head+used)%ring.Length,first=Math.Min(n,ring.Length-tail);block.AsSpan(0,first).CopyTo(ring.AsSpan(tail));block.AsSpan(first,n-first).CopyTo(ring);used+=n;Monitor.PulseAll(gate);}
   }
  }catch(Exception ex){lock(gate){if(!disposed)error=ex;ended=true;Monitor.PulseAll(gate);}}
 }
 public int Read(byte[] buffer,int offset,int count)
 {
  lock(gate){if(error!=null)throw new IOException("PCM akışı güvenli biçimde durduruldu.",error);if(!primed&&!ended&&used<Math.Min(ring.Length/2,WaveFormat.AverageBytesPerSecond/10))return 0;primed=true;int n=Math.Min(count-count%WaveFormat.BlockAlign,used);int first=Math.Min(n,ring.Length-head);ring.AsSpan(head,first).CopyTo(buffer.AsSpan(offset));ring.AsSpan(0,n-first).CopyTo(buffer.AsSpan(offset+first));head=(head+n)%ring.Length;used-=n;if(n==0&&!ended)primed=false;Monitor.PulseAll(gate);return n;}
 }
 public void Dispose(){lock(gate){if(disposed)return;disposed=true;Monitor.PulseAll(gate);}abort();if(producer!=Thread.CurrentThread&&!producer.Join(5000))throw new IOException("Decoder zamanında kapanmadı.");}
}
