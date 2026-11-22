using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Faemyr;
// Explicit WASAPI lifecycle. Shared mode receives the actual Windows mix format;
// no AutoConvertPcm, implicit fallback or hidden resampler is enabled here.
public sealed class ExactWasapiOut : IDisposable
{
 readonly AudioClient client;readonly AudioClientShareMode mode;readonly object gate=new();readonly AutoResetEvent wake=new(false);readonly SynchronizationContext? context=SynchronizationContext.Current;
 IWaveProvider? source;AudioRenderClient? render;Thread? thread;byte[] bytes=[];bool disposed,initialized,started,draining;volatile PlaybackState state;long lastPosition;
 public event EventHandler<StoppedEventArgs>? PlaybackStopped;
 public PlaybackState PlaybackState=>state;
 public WaveFormat OutputWaveFormat{get;private set;}=null!;
 public bool DeviceStarted{get{lock(gate)return started;}}
 public int StartCount{get;private set;}
 public ExactWasapiOut(MMDevice device,AudioClientShareMode mode){client=device.AudioClient;this.mode=mode;}
 public void Init(IWaveProvider input)
 {
  if(initialized)throw new InvalidOperationException("Ses çıkışı zaten hazırlandı.");
  OutputWaveFormat=input.WaveFormat;var f=OutputWaveFormat;
  if(f.BlockAlign!=f.Channels*f.BitsPerSample/8||f.AverageBytesPerSecond!=f.SampleRate*f.BlockAlign)throw new InvalidDataException("PCM çerçeve boyutu ile başlık uyuşmuyor.");
  if(mode==AudioClientShareMode.Shared&&!AudioFormats.SameSignalFormat(f,client.MixFormat))throw new InvalidOperationException("Paylaşımlı veri Windows karıştırıcı biçimiyle uyuşmuyor; çıkış engellendi.");
  if(mode==AudioClientShareMode.Exclusive&&!client.IsFormatSupported(mode,f))throw new InvalidOperationException("Aygıt özgün biçimi kabul etmedi; çıkış engellendi.");
  client.Initialize(mode,AudioClientStreamFlags.None,2000000,0,f,Guid.Empty);
  source=input;render=client.AudioRenderClient;bytes=new byte[checked(client.BufferSize*f.BlockAlign)];initialized=true;
 }
 public void Play()
 {
  lock(gate)
  {
   ObjectDisposedException.ThrowIf(disposed,this);if(!initialized)throw new InvalidOperationException("Ses çıkışı hazırlanmadı.");
   if(state==PlaybackState.Playing)return;
   if(state==PlaybackState.Paused){state=PlaybackState.Playing;if(client.CurrentPadding>0){client.Start();StartCount++;started=true;}wake.Set();return;}
   state=PlaybackState.Playing;draining=false;thread=new Thread(Run){IsBackground=true,Name="Faemyr WASAPI"};thread.Start();
  }
 }
 public void StartPaused(){lock(gate){ObjectDisposedException.ThrowIf(disposed,this);if(!initialized)throw new InvalidOperationException("Ses çıkışı hazırlanmadı.");state=PlaybackState.Paused;thread=new Thread(Run){IsBackground=true,Name="Faemyr WASAPI"};thread.Start();}}
 // Stop the endpoint itself, not just the producer thread. The clock and queued
 // frames remain frozen until resume, so pause cannot leave an endpoint running.
 public void Pause(){lock(gate){if(state!=PlaybackState.Playing)return;if(started){client.Stop();started=false;}state=PlaybackState.Paused;}}
 public void Stop()
 {
  Exception? error=null;Thread? join;lock(gate){if(initialized&&state!=PlaybackState.Stopped)try{lastPosition=ReadClock();}catch(COMException){}state=PlaybackState.Stopped;if(started){try{client.Stop();}catch(Exception ex){error=ex;}finally{started=false;}}join=thread;wake.Set();}
  if(join!=null&&join!=Thread.CurrentThread)join.Join();
  lock(gate){try{if(initialized)client.Reset();}catch(Exception ex){error??=ex;}finally{thread=null;}}
  if(error!=null)throw new IOException("Ses aygıtı kapatılırken hata oluştu; çıkış serbest bırakılıyor.",error);
 }
 public long GetPosition()
 {
  lock(gate){if(!initialized)return 0;return state==PlaybackState.Stopped?lastPosition:lastPosition=ReadClock();}
 }
 long ReadClock(){client.AudioClockClient.GetPosition(out ulong frames,out _);return (long)(frames*(double)OutputWaveFormat.AverageBytesPerSecond/client.AudioClockClient.Frequency);}
 public static int ValidateRead(IWaveProvider source,byte[] buffer,int requested)
 {
  var f=source.WaveFormat;int read=source.Read(buffer,0,requested);
  if(read<0||read>requested||read%f.BlockAlign!=0)throw new InvalidDataException("PCM decoder eksik veya taşan çerçeve döndürdü; çıkış durduruldu.");
  var standard=f.AsStandardWaveFormat();if(standard.Encoding==WaveFormatEncoding.IeeeFloat)
   for(int p=0;p<read;p+=f.BitsPerSample/8){double sample=f.BitsPerSample==32?BitConverter.ToSingle(buffer,p):BitConverter.ToDouble(buffer,p);if(!double.IsFinite(sample))throw new InvalidDataException("Geçersiz kayan noktalı ses örneği; çıkış durduruldu.");}
  return read;
 }
 void Run()
 {
  Exception? failure=null;
  try
  {
   while(true)
   {
    lock(gate)
    {
     if(state==PlaybackState.Stopped)break;
     if(state==PlaybackState.Playing)
     {
      int padding=client.CurrentPadding,available=client.BufferSize-padding;
      if(draining){if(padding==0)break;}
      else if(available>0)
      {
       int read=ValidateRead(source!,bytes,available*OutputWaveFormat.BlockAlign);
       if(read==0){if(source is not IRealtimeAudio live||live.EndOfStream)draining=true;else if(padding==0&&started){client.Stop();started=false;}}
       else
       {
        int frames=read/OutputWaveFormat.BlockAlign;IntPtr pointer=render!.GetBuffer(frames);
        // Every submitted frame is initialized. A truly zero buffer uses WASAPI's
        // explicit SILENT flag, so no stale memory can be sent as PCM.
        bool silent=OutputWaveFormat.BitsPerSample!=8&&bytes.AsSpan(0,read).IndexOfAnyExcept((byte)0)<0;
        if(!silent)Marshal.Copy(bytes,0,pointer,read);
        render.ReleaseBuffer(frames,silent?AudioClientBufferFlags.Silent:AudioClientBufferFlags.None);
       }
      }
      if(!started&&!draining&&client.CurrentPadding>0){client.Start();StartCount++;started=true;}
     }
    }
    wake.WaitOne(5);
   }
  }
  catch(Exception ex){failure=ex;}
  finally
  {
   lock(gate){try{lastPosition=ReadClock();}catch(Exception ex){failure??=ex;}try{if(started)client.Stop();}catch(Exception ex){failure??=ex;}started=false;state=PlaybackState.Stopped;try{client.Reset();}catch(Exception ex){failure??=ex;}}
   var args=new StoppedEventArgs(failure);if(context!=null)context.Post(_=>PlaybackStopped?.Invoke(this,args),null);else PlaybackStopped?.Invoke(this,args);
  }
 }
 public void Dispose(){if(disposed)return;try{Stop();}finally{disposed=true;client.Dispose();wake.Dispose();}}
}
