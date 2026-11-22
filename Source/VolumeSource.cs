using System.Buffers.Binary;
using NAudio.Wave;

namespace Faemyr;
// The original format and bytes pass through at unity. Attenuation is deliberately
// reported as sample processing. Never put this provider in a DoP carrier path.
public sealed class VolumeSource : IWaveProvider
{
 readonly IWaveProvider source;readonly bool floating;readonly int bytes;double target,current;
 public WaveFormat WaveFormat=>source.WaveFormat;
 public double Level{get=>Volatile.Read(ref target);set=>Volatile.Write(ref target,SafeLevel(value));}
 public static double SafeLevel(double value)=>double.IsFinite(value)?Math.Clamp(value,0,1):0;
 public VolumeSource(IWaveProvider source,double level)
 {
  this.source=source;var f=AudioEngine.OriginalSource(source).WaveFormat;floating=f.Encoding==WaveFormatEncoding.IeeeFloat||(f is WaveFormatExtensible e&&e.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71"));bytes=f.BitsPerSample/8;
  if((floating&&f.BitsPerSample is not (32 or 64))||(!floating&&f.BitsPerSample is not (8 or 16 or 24 or 32)))throw new NotSupportedException("Ses seviyesi için desteklenmeyen PCM biçimi.");
  current=target=SafeLevel(level);
 }
 public int Read(byte[] buffer,int offset,int count)
 {
  int read=source.Read(buffer,offset,count);if(read%WaveFormat.BlockAlign!=0)throw new System.IO.InvalidDataException("Eksik PCM örnek çerçevesi.");
  double wanted=Level;if(wanted==1&&current==1)return read;
  // At most ten milliseconds to the new value, one gain value per sample frame.
  int frames=read/WaveFormat.BlockAlign,ramp=Math.Min(frames,Math.Max(1,WaveFormat.SampleRate/100));double step=(wanted-current)/ramp;
  for(int frame=0;frame<frames;frame++)
  {
   current=frame<ramp?current+step:wanted;if(frame==ramp-1)current=wanted;
   for(int ch=0;ch<WaveFormat.Channels;ch++)
   {
    var sample=buffer.AsSpan(offset+frame*WaveFormat.BlockAlign+ch*bytes,bytes);
    if(floating){if(bytes==4){float value=BitConverter.ToSingle(sample);BitConverter.TryWriteBytes(sample,(float)(value*current));}else{double value=BitConverter.ToDouble(sample);BitConverter.TryWriteBytes(sample,value*current);}continue;}
    long n=bytes switch{1=>sample[0]-128,2=>BinaryPrimitives.ReadInt16LittleEndian(sample),3=>(sample[0]|sample[1]<<8|sample[2]<<16)<<8>>8,_=>BinaryPrimitives.ReadInt32LittleEndian(sample)};
    long scaled=(long)Math.Round(n*current);switch(bytes){case 1:sample[0]=(byte)(scaled+128);break;case 2:BinaryPrimitives.WriteInt16LittleEndian(sample,(short)scaled);break;case 3:sample[0]=(byte)scaled;sample[1]=(byte)(scaled>>8);sample[2]=(byte)(scaled>>16);break;default:BinaryPrimitives.WriteInt32LittleEndian(sample,(int)scaled);break;}
   }
  }
  return read;
 }
}
