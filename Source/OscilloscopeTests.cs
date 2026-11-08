using System.Buffers.Binary;
using System.IO;
using NAudio.Wave;

namespace Ahenk;
public static class OscilloscopeTests
{
 public static void Run(Action<bool,string> check)
 {
  foreach(var (bits,floating) in new[]{(8,false),(16,false),(24,false),(32,false),(32,true),(64,true)})
  {
   int width=bits/8;var format=floating?WaveFormat.CreateCustomFormat(WaveFormatEncoding.IeeeFloat,44100,2,44100*2*width,2*width,bits):new WaveFormat(44100,bits,2);
   var payload=new byte[1024*2*width];float[] values=[-.75f,-.25f,0,.25f,.75f];
   for(int i=0;i<1024;i++){Encode(payload.AsSpan(i*2*width,width),values[i%values.Length],bits,floating);Encode(payload.AsSpan((i*2+1)*width,width),-values[i%values.Length],bits,floating);}
   using var raw=new RawSourceWaveStream(new MemoryStream(payload),format);var observer=new ScopeSource(raw,raw);
   var output=Enumerable.Repeat((byte)0xA5,payload.Length+19).ToArray();int read=observer.Read(output,7,payload.Length);
   check(read==payload.Length&&payload.AsSpan().SequenceEqual(output.AsSpan(7,read))&&output.Take(7).All(b=>b==0xA5)&&output.Skip(7+read).All(b=>b==0xA5),$"Oscilloscope leaves every audio byte and buffer offset unchanged: {bits} bit {(floating?"float":"PCM")}");
   var scope=observer.Capture!;var left=new float[scope.BufferSize];var right=new float[scope.BufferSize];int count=scope.CopyWindow(scope.FramesWritten,left,right);
   check(count==1024&&Enumerable.Range(0,count).All(i=>Math.Abs(left[i]-values[i%values.Length])<.000001&&Math.Abs(right[i]+values[i%values.Length])<.000001),$"Oscilloscope reads exact independent opposite-phase channels: {bits} bit {(floating?"float":"PCM")}");
   observer.Seek(TimeSpan.Zero);check(scope.CopyWindow(scope.FramesWritten,left,right)==0,$"Seek removes previous oscilloscope samples: {bits} bit {(floating?"float":"PCM")}");
  }
  var extended=new WaveFormatExtensible(96000,24,2);check(ScopeCapture.Create(extended)!=null,"Oscilloscope recognizes extensible original PCM output");
  var mono=ScopeCapture.Create(new WaveFormat(8000,16,1))!;var pulse=new byte[400];for(int i=0;i<200;i++)BinaryPrimitives.WriteInt16LittleEndian(pulse.AsSpan(i*2),i%2==0?(short)16384:(short)-16384);
  for(int i=0;i<100;i++)mono.Append(pulse);
  var monoLeft=new float[mono.BufferSize];var monoRight=new float[mono.BufferSize];int monoCount=mono.CopyWindow(mono.FramesWritten,monoLeft,monoRight);
  check(monoCount==mono.BufferSize&&monoLeft.Take(monoCount).SequenceEqual(monoRight.Take(monoCount))&&monoLeft.Take(monoCount).All(v=>Math.Abs(v)==.5),"Bounded ring buffer wraps correctly and mono retains its real amplitude");
  check(mono.CopyWindow(1,monoLeft,monoRight)==0,"An output clock older than retained audio cannot display stale samples");
  var invalid=ScopeCapture.Create(WaveFormat.CreateIeeeFloatWaveFormat(44100,1))!;var floats=new byte[12];BinaryPrimitives.WriteInt32LittleEndian(floats,BitConverter.SingleToInt32Bits(float.NaN));BinaryPrimitives.WriteInt32LittleEndian(floats.AsSpan(4),BitConverter.SingleToInt32Bits(float.PositiveInfinity));BinaryPrimitives.WriteInt32LittleEndian(floats.AsSpan(8),BitConverter.SingleToInt32Bits(.001f));invalid.Append(floats);var valuesLeft=new float[invalid.BufferSize];var valuesRight=new float[invalid.BufferSize];invalid.CopyWindow(3,valuesLeft,valuesRight);
  check(valuesLeft[0]==0&&valuesLeft[1]==0&&valuesLeft[2]==.001f,"Nonfinite visual samples are safe and quiet audio is not artificially amplified");
  float[] silent=new float[12],rightTrigger=[-1,-.5f,0,.5f,1,.5f,0,-.5f,-1,-.5f,0,.5f];
  int trigger=ScopeCapture.TriggerStart(silent,rightTrigger,8);check(trigger==3,"Trigger uses the audible right channel when the left channel is silent");
 }
 static void Encode(Span<byte> target,float value,int bits,bool floating)
 {
  if(floating){if(bits==32)BinaryPrimitives.WriteInt32LittleEndian(target,BitConverter.SingleToInt32Bits(value));else BinaryPrimitives.WriteInt64LittleEndian(target,BitConverter.DoubleToInt64Bits(value));return;}
  if(bits==8){target[0]=(byte)(value*128+128);return;}
  int scaled=(int)(value*Math.Pow(2,bits-1));
  if(bits==16)BinaryPrimitives.WriteInt16LittleEndian(target,(short)scaled);
  else if(bits==24){target[0]=(byte)scaled;target[1]=(byte)(scaled>>8);target[2]=(byte)(scaled>>16);}
  else BinaryPrimitives.WriteInt32LittleEndian(target,scaled);
 }
}
