using System.IO;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using NAudio.Wave.SampleProviders;

namespace Ahenk;
public sealed class AudioEngine : IDisposable
{
 ExactWasapiOut? output;WaveStream? reader;MMDevice? device;bool failed;ScopeSource? scopeSource;MediaFoundationResampler? sharedResampler;
 BufferedAudioSource? buffered;PcmSequenceStream? sequence;PlaybackEntry? currentEntry;
 sealed record Request(string Path,string? Device,bool Exclusive,GainDecision Gain,bool Dop,AudioPreferences? Preferences,PlaybackPlan? Plan);
 Request? request;string streamingTransport="",activeDeviceName="";bool ownsExclusive,releasedPause;TimeSpan pausedPosition,pausedDuration;
 public bool Buffering=>buffered?.Buffering==true;
 public int OutputStartCount=>output?.StartCount??0;public string? ActiveDeviceId{get;private set;}public TimeSpan LastFailurePosition{get;private set;}
 public WaveFormat? OutputFormat=>output?.OutputWaveFormat;
 double ClockSeconds{get{try{return output==null?0:output.GetPosition()/(double)output.OutputWaveFormat.AverageBytesPerSecond;}catch(System.Runtime.InteropServices.COMException){return 0;}}}
 public PlaybackEntry? CurrentEntry{get{if(sequence!=null)currentEntry=sequence.Audible(ClockSeconds).Entry;return currentEntry;}}
 public ScopeCapture? Scope=>scopeSource?.Capture;
 public event Action? Ended; public event Action<string>? Failed; public bool IsPlaying=>!failed&&output?.PlaybackState==PlaybackState.Playing;
 public bool IsPaused=>releasedPause||!failed&&output?.PlaybackState==PlaybackState.Paused;
 public bool DeviceStarted=>output?.DeviceStarted==true;
 TimeSpan seekBase;long seekClock;
 public TimeSpan Position
 {
  get{if(releasedPause)return pausedPosition;if(sequence!=null)return sequence.Audible(ClockSeconds).Position;if(output!=null&&(IsPlaying||IsPaused))try{return TimeSpan.FromSeconds(Math.Clamp(seekBase.TotalSeconds+(output.GetPosition()-seekClock)/(double)output.OutputWaveFormat.AverageBytesPerSecond,0,Duration.TotalSeconds));}catch(System.Runtime.InteropServices.COMException){}return reader?.CurrentTime??TimeSpan.Zero;}
  set
  {
   if(releasedPause){pausedPosition=TimeSpan.FromSeconds(Math.Clamp(value.TotalSeconds,0,Math.Max(0,pausedDuration.TotalSeconds-.001)));return;}if(reader==null||request==null)return;var target=TimeSpan.FromSeconds(Math.Clamp(value.TotalSeconds,0,Math.Max(0,Duration.TotalSeconds-.001)));bool paused=IsPaused;var saved=request;
   if(saved.Plan!=null&&CurrentEntry is {} entry)PlayStreaming(entry with{Start=target},saved.Plan.Rebase(entry),saved.Device,saved.Exclusive,paused,saved.Preferences);
   else StartPlayback(saved.Path,saved.Device,saved.Exclusive,saved.Gain,saved.Dop,saved.Preferences,target,startPaused:paused);
  }
 }
 public TimeSpan Duration=>releasedPause?pausedDuration:sequence!=null&&CurrentEntry is {} entry?TimeSpan.FromSeconds(entry.Track.Seconds):reader?.TotalTime??TimeSpan.Zero;
 string signal="Henüz ses çıkışı yok";VolumeSource? volumeSource;double volume=1;bool muted,dopActive;
 public bool IsDop=>releasedPause?request?.Dop==true:dopActive&&output!=null;
 public double Volume{get=>volume;set{volume=VolumeSource.SafeLevel(value);if(volumeSource!=null)volumeSource.Level=muted?0:volume;}}
 public bool Muted{get=>muted;set{muted=value;if(volumeSource!=null)volumeSource.Level=muted?0:volume;}}
 public string Signal{get
 {
  string text=signal;if(sequence!=null&&CurrentEntry is {} entry){var f=DecoderWaveStream.Format(entry.Track);text=$"Kaynak: {f.BitsPerSample} bit / {f.SampleRate/1000d:0.###} kHz / {f.Channels} kanal → {activeDeviceName}\n{streamingTransport}\n{entry.Gain.Description}\nAkış decoder · uyumlu parçalarda kesintisiz";}
  return text+(Buffering?"\nSes tamponu hazırlanıyor…":"")+(output==null&&!releasedPause||IsDop?"":muted?"\nSessiz · dijital ses seviyesi işleniyor":volume<1?$"\nSes %{volume*100:0} · dijital zayıflatma · özgün örnek değerleri değişir":"\nSes %100 · ses seviyesi işlemi atlanıyor");
 }private set=>signal=value;}
 public static IWaveProvider OriginalSource(IWaveProvider source)
 {
  var format=source.WaveFormat;
  // WaveFileReader exposes extensible fmt chunks as generic extra-data objects.
  // Materialize the actual extensible layout, including subtype and channel mask.
  if(format.Encoding==WaveFormatEncoding.Extensible&&format is not WaveFormatExtensible)
  {
   if(format.ExtraSize<22)throw new InvalidDataException("Eksik genişletilmiş WAV biçimi.");
   var pointer=WaveFormat.MarshalToPtr(format);try{format=WaveFormat.MarshalFromPtr(pointer);}finally{System.Runtime.InteropServices.Marshal.FreeHGlobal(pointer);}
  }
  return new FormatSource(source,format);
 }
 public static IWaveProvider SharedSource(IWaveProvider source){var original=OriginalSource(source);return new FormatSource(original,original.WaveFormat.AsStandardWaveFormat()).ToSampleProvider().ToWaveProvider();}
 internal static MediaFoundationResampler CompatibilitySource(IWaveProvider source,WaveFormat destination)=>new(SharedSource(source),destination){ResamplerQuality=60};
 sealed class FormatSource(IWaveProvider source,WaveFormat format):IWaveProvider
 {
  public WaveFormat WaveFormat=>format;
  public int Read(byte[] buffer,int offset,int count)=>source.Read(buffer,offset,count);
 }
 public static List<(string Id,string Name)> Devices(){using var e=new MMDeviceEnumerator();var result=new List<(string,string)>();foreach(var d in e.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active)){using(d)result.Add((d.ID,d.FriendlyName));}return result;}
 public static async Task<string> Decode(Track t,string cache,CancellationToken ct)
 {
  if(t.IsDsd)throw new InvalidOperationException("DSD, PCM önbelleğine dönüştürülmez; DoP çıkış yolunu kullan.");
  ct.ThrowIfCancellationRequested();Directory.CreateDirectory(cache);
  var correction=System.IO.Path.ChangeExtension(t.AudioPath,".wvc");
  var source=new FileInfo(t.AudioPath);
  string key=Paths.Hash("pcm-v2|"+t.Path+"|"+(t.IsCue?t.CueStartFrame+":"+t.CueEndFrame+"|":"")+source.Length+"|"+source.LastWriteTimeUtc.Ticks+"|"+(File.Exists(correction)?new FileInfo(correction).Length+":"+File.GetLastWriteTimeUtc(correction).Ticks:"none"));
  string target=System.IO.Path.Combine(cache,key+".wav");if(File.Exists(target))return target;
  string temp=target+"."+Guid.NewGuid().ToString("N")+".partial.wav";try
  {
   if(t.IsCue){using var stream=new DecoderWaveStream(t);using var cancelled=ct.Register(stream.Abort);await Task.Run(()=>{using var writer=new WaveFileWriter(temp,stream.WaveFormat);byte[] bytes=new byte[stream.WaveFormat.BlockAlign*4096];int count;while((count=stream.Read(bytes,0,bytes.Length))>0){ct.ThrowIfCancellationRequested();writer.Write(bytes,0,count);}},ct);} else if(System.IO.Path.GetExtension(t.Path).Equals(".wv",StringComparison.OrdinalIgnoreCase))
    await ProcessTool.Run(Paths.Tool("wvunpack"),["-y","-w",t.Path,temp],ct);
   else
   {
    // FLAC integer samples remain integer PCM at the original width, rate and channel count.
    string codec=t.SampleFormat.StartsWith('d')?"pcm_f64le":t.SampleFormat.StartsWith('f')||t.Bits==0?"pcm_f32le":t.Bits switch {<=8=>"pcm_u8",<=16=>"pcm_s16le",<=24=>"pcm_s24le",_=>"pcm_s32le"};
    await ProcessTool.Run(Paths.Tool("ffmpeg"),["-v","error","-nostdin","-y","-i",t.Path,"-map","0:a:0","-vn","-c:a",codec,"-rf64","auto",temp],ct);
   }
   ct.ThrowIfCancellationRequested();
   try{File.Move(temp,target,false);}catch(IOException)when(File.Exists(target)){File.Delete(temp);}
   return target;
  }
  catch{try{File.Delete(temp);}catch{}throw;}
 }
 public void Play(string wav,string? deviceId,bool exclusive,GainDecision? gain=null,TimeSpan? startPosition=null)=>StartPlayback(wav,deviceId,exclusive,gain??GainDecision.Off,false,null,startPosition);
 public void PlayDop(string path,string? deviceId,AudioPreferences preferences,TimeSpan? startPosition=null)=>StartPlayback(path,deviceId,true,GainDecision.Off,true,preferences,startPosition);
 public void PlayStreaming(PlaybackEntry initial,PlaybackPlan plan,string? deviceId,bool exclusive,bool paused=false,AudioPreferences? preferences=null)
 {
  int gainBits=32;if(exclusive&&initial.Gain.ChangesSamples&&preferences?.AutomaticOutput!=true)
  {
   using var enumerator=new MMDeviceEnumerator();using var selected=deviceId==null?enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia):enumerator.GetDevice(deviceId);using var probe=selected.AudioClient;
   int gainRate=preferences?.PcmRate(selected.ID,initial.Track.SampleRate)??initial.Track.SampleRate;
   gainBits=new[]{32,24}.FirstOrDefault(bits=>AudioFormats.ExactCandidates(new WaveFormat(gainRate,bits,initial.Track.Channels)).Any(f=>probe.IsFormatSupported(AudioClientShareMode.Exclusive,f)));
   if(gainBits==0)throw new InvalidOperationException("Aygıt ReplayGain için özgün oranda 24/32 bit PCM kabul etmiyor.");
  }
  AutomaticAudioOutput.StartWithFallback(forceShared=>
  {
   var attemptPlan=plan.Rebase(initial);
   StartPlayback(initial.Track.AudioPath,deviceId,exclusive,GainDecision.Off,false,preferences,null,()=>new PcmSequenceStream(initial,attemptPlan,gainBits),attemptPlan,paused,forceShared);
  });
 }
 void StartPlayback(string path,string? deviceId,bool exclusive,GainDecision gain,bool dop,AudioPreferences? preferences,TimeSpan? startPosition,Func<WaveStream>? factory=null,PlaybackPlan? plan=null,bool startPaused=false,bool forceShared=false)
 {
  Stop();seekBase=TimeSpan.Zero;seekClock=0;string stage="Ses dosyasını açma",name="Varsayılan aygıt";try
  {
   if(dop){preferences!.RequireDop(deviceId);if(muted||volume<1)throw new InvalidOperationException("DoP taşıyıcısına dijital ses seviyesi uygulanamaz. DoP oynatmadan önce Lyra sesini %100 ve sesli duruma getir; dinleme seviyesini DAC üzerinden ayarla.");}dopActive=dop;
   reader=factory?.Invoke()??(dop?new DopWaveStream(path):new WaveFileReader(path));sequence=reader as PcmSequenceStream;request=new(path,deviceId,exclusive,gain,dop,preferences?.Copy(),plan);stage="Ses aygıtını açma";using var e=new MMDeviceEnumerator();device=AutomaticAudioOutput.OpenDevice(e,deviceId,preferences?.AutomaticOutput==true&&!dop);name=device.FriendlyName;activeDeviceName=name;ActiveDeviceId=device.ID;
   var original=OriginalSource(reader);
   using var probe=device.AudioClient;IWaveProvider payload=original;WaveFormat selected=original.WaveFormat;var mix=probe.MixFormat;
   bool automatic=preferences?.AutomaticOutput==true&&!dop;string automaticReason="";
   // Optical drivers can accept packed 24-bit PCM while the downstream path
   // emits noise. Never send that unverified direct stream, even in manual mode.
   bool opticalPcmGuard=!dop&&exclusive&&AutomaticAudioOutput.RequiresOpticalPcmGuard(AutomaticAudioOutput.FormFactor(device),original.WaveFormat);
   if(opticalPcmGuard)exclusive=false;
   int outputRate=exclusive&&!dop&&!automatic?preferences?.PcmRate(device.ID,selected.SampleRate)??selected.SampleRate:selected.SampleRate;
   bool resampled=outputRate!=selected.SampleRate;
   bool Supported(WaveFormat f)=>probe.IsFormatSupported(AudioClientShareMode.Exclusive,f);
   if(gain.ChangesSamples&&!dop)
   {
    int bits=32;
    if(exclusive&&!resampled&&!automatic)
    {
     WaveFormat? matched=null;foreach(int candidateBits in new[]{32,24})try{matched=AudioFormats.SelectExact(new WaveFormat(original.WaveFormat.SampleRate,candidateBits,original.WaveFormat.Channels),Supported);bits=candidateBits;break;}catch(InvalidOperationException){}
     selected=matched??throw new InvalidOperationException("ReplayGain için aygıt özgün örnekleme oranında 24/32 bit PCM kabul etmiyor. ReplayGain'i kapatabilir veya paylaşımlı modu seçebilirsin.");
    }
    payload=new GainSource(original,gain.Multiplier,bits);if(!exclusive)selected=payload.WaveFormat;
   }
   else if(exclusive&&!resampled&&!automatic)selected=AudioFormats.SelectExact(original.WaveFormat,Supported);
   if(automatic)
   {
    var choice=forceShared?AutomaticAudioOutput.Shared(mix,"Exclusive açılamadı; Windows uyumlu çıkışı"):AutomaticAudioOutput.Select(payload.WaveFormat,mix,AutomaticAudioOutput.FormFactor(device),Supported,AutomaticAudioOutput.HardwareVolume(device));
    exclusive=choice.Exclusive;automaticReason=choice.Reason;selected=exclusive?choice.Format:payload.WaveFormat;outputRate=selected.SampleRate;resampled=exclusive&&outputRate!=payload.WaveFormat.SampleRate;
   }
   bool converted=exclusive&&!AudioFormats.SameSignalFormat(payload.WaveFormat,selected);
   if(resampled||converted)
   {
    // An explicit, device-scoped PCM ceiling. Convert actual samples before
    // assigning the destination header; never reinterpret PCM or touch DoP.
    if(!automatic){int destinationBits=payload.WaveFormat.BitsPerSample>24?24:payload.WaveFormat.BitsPerSample;selected=AudioFormats.SelectExact(new WaveFormat(outputRate,destinationBits,payload.WaveFormat.Channels),Supported);}
    sharedResampler=CompatibilitySource(payload,selected);payload=sharedResampler;
   }
   payload=new FormatSource(payload,selected);
   if(!dop){volumeSource=new VolumeSource(payload,muted?0:volume);payload=volumeSource;}
   if(startPosition is {} position){reader.CurrentTime=TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds,0,Math.Max(0,reader.TotalTime.TotalSeconds-.05)));seekBase=reader.CurrentTime;}
   stage="WASAPI çıkışını hazırlama";
   // Windows shared AutoConvert can reject packed PCM WAVEFORMATEXTENSIBLE inputs.
   // Standard IEEE float avoids that boundary; exclusive playback still uses original PCM bytes.
   IWaveProvider source=payload;
   if(sequence==null)source=scopeSource=new ScopeSource(payload,reader,!dop);
   if(!exclusive)
   {
    var shared=SharedSource(source);
    // Hand WASAPI its exact mix header and matching samples. Windows' implicit
    // AutoConvert path is deliberately bypassed, including on Realtek outputs.
    if(AudioFormats.SameSignalFormat(shared.WaveFormat,mix))source=new FormatSource(shared,mix);
    else{sharedResampler=new MediaFoundationResampler(shared,mix){ResamplerQuality=60};source=sharedResampler;}
   }
   // Observe frames when consumed for output, after read-ahead and resampling.
   // A producer-side capture can overwrite audible samples before they play.
   if(sequence!=null){scopeSource=BufferAndObserve(source,sequence.Abort,out var buffer);buffered=buffer;source=scopeSource;}
   streamingTransport=exclusive?$"WASAPI Exclusive · {selected.SampleRate/1000d:0.###} kHz / {selected.BitsPerSample} bit · "+(resampled?"PCM uyumluluğu · yeniden örnekleme açık · bit-perfect değil":converted?"PCM çözünürlüğü dönüştürüldü · bit-perfect değil":"yeniden örnekleme yok"):$"WASAPI Paylaşımlı · {mix.BitsPerSample} bit / Windows: {mix.SampleRate/1000d:0.###} kHz · "+(AudioFormats.SameSignalFormat(original.WaveFormat,mix)?"Windows ses yolu":"Windows biçimine dönüşüm · bit-perfect değil");
   if(automatic)streamingTransport+="\nOtomatik çıkış · "+automaticReason;
   if(opticalPcmGuard)streamingTransport+=" · optik PCM koruması";
   var f=reader.WaveFormat;
   Signal=dop&&reader is DopWaveStream dsd?$"DSD: {dsd.Info.Rate/1000000.0:0.####} MHz / {dsd.Info.Channels} kanal → {device.FriendlyName}\nDoP 1.1 · {f.SampleRate/1000.0:0.###} kHz / 24 bit taşıyıcı · WASAPI Exclusive\nDSD bitleri korunur · PCM dönüşümü / ReplayGain yok · görselleştirme kapalı\nDAC üzerindeki DoP çözümü ayrıca doğrulanmalıdır.":$"Kaynak: {f.BitsPerSample} bit / {f.SampleRate/1000.0:0.###} kHz / {f.Channels} kanal → {device.FriendlyName}\n"+streamingTransport+"\n"+gain.Description+(exclusive&&!resampled&&!gain.ChangesSamples?"\nDAC çıkışında bit-perfect ölçümü yapılmadı.":"");
   if(startPaused&&exclusive){ReleasePausedOutput();return;}
   output=new ExactWasapiOut(device,exclusive?AudioClientShareMode.Exclusive:AudioClientShareMode.Shared);
   try{output.Init(source);}catch(Exception ex)when(automatic&&exclusive&&!forceShared&&AutomaticAudioOutput.CanRetryShared(ex)){throw new AutomaticOutputRetryException(ex);}
   output.PlaybackStopped+=OnStopped;
   // WasapiOut can otherwise choose a fallback/resampler internally. Never start such a stream.
   if(exclusive&&!AudioFormats.SameSignalFormat(selected,output.OutputWaveFormat))throw new InvalidOperationException("Sürücü istenen özgün biçim yerine başka biçim açtı; oynatma başlatılmadı.");
   ownsExclusive=exclusive;
   stage="Oynatmayı başlatma";
   if(startPaused)output.StartPaused();else output.Play();
  }catch(Exception ex){string format=reader?.WaveFormat.ToString()??"bilinmiyor";LogFailure(stage,ex,format,name);Stop();if(ex is ArgumentException or System.Runtime.InteropServices.COMException)throw new InvalidOperationException($"Ses çıkışı açılamadı ({stage}).\nAygıt: {name}\nKaynak: {format}\nMod: {(exclusive?"Exclusive":"Paylaşımlı")}\nHata kodu: 0x{ex.HResult:X8}\nAygıtları yenileyip tekrar deneyebilirsin. Teknik ayrıntı data/playback-errors.log dosyasına kaydedildi.",ex);throw;}
 }
 void OnStopped(object? sender,StoppedEventArgs e){if(!ReferenceEquals(sender,output))return;if(e.Exception!=null){LastFailurePosition=Position;failed=true;LogFailure("Oynatma sırasında",e.Exception,reader?.WaveFormat.ToString()??"","");string message=$"Ses çıkışı durdu (0x{e.Exception.HResult:X8}). Teknik ayrıntı data/playback-errors.log dosyasına kaydedildi.";Stop();Signal=message;Failed?.Invoke(message);return;}if(sequence?.AtEnd==true||sequence==null&&reader!=null&&reader.Position>=reader.Length){try{output?.Dispose();}catch(Exception ex){LogFailure("Parça sonunda aygıtı bırakma",ex,"","");}ownsExclusive=false;Ended?.Invoke();}}
 static void LogFailure(string stage,Exception error,string format,string deviceName){try{Directory.CreateDirectory(Paths.Data);string path=System.IO.Path.Combine(Paths.Data,"playback-errors.log");if(File.Exists(path)&&new FileInfo(path).Length>512*1024)File.WriteAllText(path,"");File.AppendAllText(path,$"{DateTimeOffset.Now:O} | {stage} | {deviceName} | {format}\n{error}\n\n");}catch(IOException){}catch(UnauthorizedAccessException){}}
 void ReleasePausedOutput()
 {
  var saved=request;if(saved==null)return;output?.Pause();var entry=CurrentEntry;var position=Position;var duration=Duration;string text=Signal;
  Stop();request=saved;currentEntry=entry;pausedPosition=position;pausedDuration=duration;releasedPause=true;
  signal=string.Join("\n",text.Split('\n').Where(line=>!line.StartsWith("Ses %")&&!line.StartsWith("Sessiz ·")));
 }
 public void Toggle()
 {
  if(releasedPause&&request is {} saved)
  {
   var entry=currentEntry;var position=pausedPosition;var duration=pausedDuration;string text=signal;
   try
   {
    if(saved.Plan!=null&&entry!=null)PlayStreaming(entry with{Start=position},saved.Plan.Rebase(entry),saved.Device,saved.Exclusive,false,saved.Preferences);
    else StartPlayback(saved.Path,saved.Device,saved.Exclusive,saved.Gain,saved.Dop,saved.Preferences,position);
   }
   catch{Stop();request=saved;currentEntry=entry;pausedPosition=position;pausedDuration=duration;releasedPause=true;signal=text;throw;}
  }
  else if(IsPlaying){if(ownsExclusive)ReleasePausedOutput();else output?.Pause();}
  else if(IsPaused)output?.Play();
 }
 internal static ScopeSource BufferAndObserve(IWaveProvider source,Action abort,out BufferedAudioSource buffer){buffer=new BufferedAudioSource(source,abort);return new ScopeSource(buffer);}
 public int CopyScope(ScopeCapture capture,Span<float> left,Span<float> right,int requestedFrames=0)
 {
  if(!ReferenceEquals(capture,Scope)||output==null)return 0;
  try
  {
   double seconds=output.GetPosition()/(double)output.OutputWaveFormat.AverageBytesPerSecond;
   return capture.CopyWindow((long)(seconds*capture.SampleRate),left,right,requestedFrames);
  }catch(System.Runtime.InteropServices.COMException){return 0;}catch(InvalidOperationException){return 0;}
 }
 public void Stop()
 {
  releasedPause=false;ownsExclusive=false;pausedPosition=pausedDuration=TimeSpan.Zero;
  Exception? error=null;void Release(Action action){try{action();}catch(Exception ex){error??=ex;}}
  if(output is {} old){output=null;old.PlaybackStopped-=OnStopped;Release(old.Dispose);}if(buffered!=null)Release(buffered.Dispose);buffered=null;
  if(sharedResampler!=null)Release(sharedResampler.Dispose);sharedResampler=null;scopeSource=null;volumeSource=null;dopActive=false;
  if(reader!=null)Release(reader.Dispose);reader=null;sequence=null;currentEntry=null;if(device!=null)Release(device.Dispose);device=null;activeDeviceName="";ActiveDeviceId=null;failed=false;request=null;
  if(error!=null)LogFailure("Ses çıkışını kapatma",error,"","");
 }
 public void Dispose()=>Stop();
 public static void TrimCache(string cache,string? keep=null,long maximumBytes=2L*1024*1024*1024)
 {
  try
  {
   if(!Directory.Exists(cache))return;long total=0;
   foreach(var f in new DirectoryInfo(cache).GetFiles("*.wav").Where(f=>!f.Name.EndsWith(".partial.wav",StringComparison.OrdinalIgnoreCase)).OrderByDescending(f=>f.LastWriteTimeUtc))
   {
    try{total+=f.Length;if(total>maximumBytes&&!string.Equals(f.FullName,keep,StringComparison.OrdinalIgnoreCase))f.Delete();}catch(IOException){}catch(UnauthorizedAccessException){}
   }
  }catch(IOException){}catch(UnauthorizedAccessException){}
 }
}
