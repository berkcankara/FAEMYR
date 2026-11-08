using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Ahenk;
public static class AudioFormats
{
 public static WaveFormat Extensible(WaveFormat format)
 {
  var basic=format.AsStandardWaveFormat();
  if(basic.Encoding is not WaveFormatEncoding.Pcm and not WaveFormatEncoding.IeeeFloat)throw new NotSupportedException("Desteklenmeyen PCM biçimi.");
  byte[] bytes=new byte[40];BinaryPrimitives.WriteUInt16LittleEndian(bytes,0xfffe);
  BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2),(short)basic.Channels);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),basic.SampleRate);
  BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8),basic.AverageBytesPerSecond);BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(12),(short)basic.BlockAlign);
  BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(14),(short)basic.BitsPerSample);BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(16),22);
  BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(18),(short)basic.BitsPerSample);
  int mask=basic.Channels switch{1=>4,2=>3,_=>0};BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20),mask);
  var subtype=new Guid(basic.Encoding==WaveFormatEncoding.Pcm?"00000001-0000-0010-8000-00aa00389b71":"00000003-0000-0010-8000-00aa00389b71");subtype.TryWriteBytes(bytes.AsSpan(24));
  IntPtr pointer=Marshal.AllocHGlobal(bytes.Length);try{Marshal.Copy(bytes,0,pointer,bytes.Length);return WaveFormat.MarshalFromPtr(pointer);}finally{Marshal.FreeHGlobal(pointer);}
 }
 public static IEnumerable<WaveFormat> ExactCandidates(WaveFormat format)
 {
  yield return format;
  // Windows drivers may accept only one of the two equivalent mono/stereo headers.
  if(format.Channels<=2){yield return format.AsStandardWaveFormat();yield return Extensible(format);}
 }
 public static WaveFormat SelectExact(WaveFormat format,Func<WaveFormat,bool> supported)
 {
  foreach(var candidate in ExactCandidates(format))if(supported(candidate))return candidate;
  throw new InvalidOperationException($"Aygıt {format.SampleRate:N0} Hz / {format.BitsPerSample} bit / {format.Channels} kanal biçimini Exclusive modda desteklemiyor. Örnekleme oranı değiştirilmedi. Başka aygıt seçebilir veya paylaşımlı modu kullanabilirsin.");
 }
 public static bool SameSignalFormat(WaveFormat a,WaveFormat b)
 {
  a=a.AsStandardWaveFormat();b=b.AsStandardWaveFormat();return a.SampleRate==b.SampleRate&&a.Channels==b.Channels&&a.BitsPerSample==b.BitsPerSample&&a.Encoding==b.Encoding&&a.BlockAlign==b.BlockAlign;
 }
}
