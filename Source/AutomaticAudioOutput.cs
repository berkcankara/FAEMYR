using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace Ahenk;

public sealed record AutomaticOutputPlan(bool Exclusive,WaveFormat Format,string Reason);
public static class AutomaticAudioOutput
{
 public static MMDevice OpenDevice(MMDeviceEnumerator endpoints,string? id,bool automatic)
 {
  if(id==null)return endpoints.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
  try{var selected=endpoints.GetDevice(id);if(selected.State==DeviceState.Active)return selected;selected.Dispose();if(!automatic)throw new InvalidOperationException("Seçili ses aygıtı bağlı değil.");}
  catch(Exception error)when(automatic&&error is COMException or ArgumentException){}
  return endpoints.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
 }
 internal static void StartWithFallback(Action<bool> start)
 {
  try{start(false);}catch(AutomaticOutputRetryException){start(true);}
 }
 // This describes the Windows endpoint, not the receiver at the far end of
 // an optical cable. Never infer receiver support from the S/PDIF driver.
 public static int FormFactor(MMDevice device)
 {
  var key=PropertyKeys.PKEY_AudioEndpoint_FormFactor;
  return device.Properties.Contains(key)?Convert.ToInt32(device.Properties[key].Value):10;
 }
 public static AutomaticOutputPlan Shared(WaveFormat mix,string reason)=>new(false,mix,reason);
 public static bool HardwareVolume(MMDevice device)=>device.AudioEndpointVolume.HardwareSupport.HasFlag(EEndpointHardwareSupport.Volume);
 public static bool RequiresOpticalPcmGuard(int formFactor,WaveFormat format)=>formFactor is 7 or 8&&format.BitsPerSample>16;
 public static AutomaticOutputPlan Select(WaveFormat source,WaveFormat mix,int formFactor,Func<WaveFormat,bool> supported,bool hardwareVolume=true)
 {
  if(formFactor is 0 or 7 or 8 or 10)return Shared(mix,formFactor==8?"Optik alıcının sınırları okunamıyor; Windows uyumlu çıkışı":"Windows uyumlu çıkışı");
  if(!hardwareVolume)return Shared(mix,"Windows ses kontrolünü korumak için paylaşımlı çıkış");
  if(source.Channels>2)return Shared(mix,"Çok kanallı Windows çıkışı");
  WaveFormat? Find(WaveFormat format)
  {
   foreach(var candidate in AudioFormats.ExactCandidates(format))if(supported(candidate))return candidate;
   return null;
  }
  if(Find(source) is {} exact)return new(true,exact,"Kaynağın özgün biçimi destekleniyor");
  int[] widths=source.BitsPerSample<=16?[16,24,32]:source.BitsPerSample<=24?[24,32,16]:[32,24,16];
  // Preserve the source rate before considering a reduction. Higher rates than
  // the source are never selected merely to display a bigger kHz number.
  var rates=new[]{source.SampleRate,768000,705600,384000,352800,192000,176400,96000,88200,48000,44100,32000,24000,22050,16000,11025,8000}.Where(rate=>rate<=source.SampleRate).Distinct().OrderByDescending(rate=>rate);
  // Prefer preserving precision to discarding low bits merely to retain a
  // higher sample-rate number (e.g. 24/96 before 16/192 for a 24-bit source).
  foreach(bool preservePrecision in new[]{true,false})foreach(int rate in rates)foreach(int bits in widths.Where(bits=>(bits>=source.BitsPerSample)==preservePrecision))
   if(Find(new WaveFormat(rate,bits,source.Channels)) is {} format)return new(true,format,rate==source.SampleRate?"Özgün oranda aygıta uygun PCM çözünürlüğü":"Aygıtın desteklediği en yüksek uygun PCM oranı");
  return Shared(mix,"Özgün Exclusive biçimi desteklenmiyor; Windows uyumlu çıkışı");
 }
 public static bool CanRetryShared(Exception error)=>error is COMException or ArgumentException&&unchecked((uint)error.HResult) is 0x88890008 or 0x8889000A or 0x8889000E or 0x88890016 or 0x80070057;
}
internal sealed class AutomaticOutputRetryException(Exception inner):Exception("Exclusive açılamadı; Windows uyumlu çıkışı denenecek.",inner);
