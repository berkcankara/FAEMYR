using System.Buffers.Binary;
using System.IO;
using System.Text;
using NAudio.Wave;

namespace Faemyr;
public sealed record DsdInfo(int Rate,int Channels,long Samples,long DataStart,long DataLength,int BlockSize,bool LeastBitFirst,bool Planar)
{
 public int CarrierRate=>Rate/16;
 public double Seconds=>Samples/(double)Rate;
}
public static class DsdContainer
{
 static string Id(BinaryReader reader)=>Encoding.ASCII.GetString(reader.ReadBytes(4));
 static long Size(ulong value){if(value>long.MaxValue)throw new InvalidDataException("DSD blok boyutu çok büyük.");return (long)value;}
 static long End(long start,long length,long limit){if(length<0||start<0||start>limit||length>limit-start)throw new InvalidDataException("Eksik veya geçersiz DSD bloğu.");return start+length;}
 static uint Be32(BinaryReader r){Span<byte> b=stackalloc byte[4];r.BaseStream.ReadExactly(b);return BinaryPrimitives.ReadUInt32BigEndian(b);}
 static ulong Be64(BinaryReader r){Span<byte> b=stackalloc byte[8];r.BaseStream.ReadExactly(b);return BinaryPrimitives.ReadUInt64BigEndian(b);}
 static ushort Be16(BinaryReader r){Span<byte> b=stackalloc byte[2];r.BaseStream.ReadExactly(b);return BinaryPrimitives.ReadUInt16BigEndian(b);}
 public static DsdInfo Read(string path){using var file=File.OpenRead(path);return Read(file);}
 public static DsdInfo Read(Stream file)
 {
  try
  {
   using var r=new BinaryReader(file,Encoding.ASCII,true);string id=Id(r);DsdInfo info=id switch{"DSD "=>Dsf(r),"FRM8"=>Dff(r),_=>throw new InvalidDataException("DSF veya DFF başlığı bulunamadı.")};
   if(info.Channels is <1 or >2)throw new NotSupportedException("DoP yolu mono/stereo DSF ve DFF içindir; çok kanallı DSD bu sürümde desteklenmiyor.");
   if(info.Rate<2822400||info.Rate>45158400||info.Rate%16!=0||info.Samples<=0)throw new InvalidDataException("Geçersiz DSD örnekleme oranı veya süresi.");
   return info;
  }catch(EndOfStreamException ex){throw new InvalidDataException("DSD dosyası eksik.",ex);}catch(OverflowException ex){throw new InvalidDataException("DSD dosyası boyutları geçersiz.",ex);}
 }
 static DsdInfo Dsf(BinaryReader r)
 {
  var f=r.BaseStream;if(r.ReadUInt64()!=28)throw new InvalidDataException("DSF DSD başlığı geçersiz.");
  long total=Size(r.ReadUInt64()),metadata=Size(r.ReadUInt64());if(total<92||total>f.Length||metadata>total)throw new InvalidDataException("DSF dosya boyutu geçersiz.");
  if(Id(r)!="fmt ")throw new InvalidDataException("DSF fmt bloğu yok.");long fmt=Size(r.ReadUInt64());if(fmt<52)throw new InvalidDataException("DSF fmt bloğu kısa.");long fmtEnd=End(28,fmt,total);
  uint version=r.ReadUInt32(),format=r.ReadUInt32(),type=r.ReadUInt32();int channels=checked((int)r.ReadUInt32()),rate=checked((int)r.ReadUInt32());uint bitOrder=r.ReadUInt32();long samples=Size(r.ReadUInt64());int block=checked((int)r.ReadUInt32());r.ReadUInt32();
  if(version!=1||format!=0||bitOrder is not 1 and not 8||block<=0||block>1048576||channels is <1 or >2||type!=(uint)channels)throw new NotSupportedException("Bu DSF kanal veya veri düzeni desteklenmiyor (mono/stereo ham DSD gerekli).");
  f.Position=fmtEnd;if(Id(r)!="data")throw new InvalidDataException("DSF data bloğu yok.");long chunk=Size(r.ReadUInt64());if(chunk<12)throw new InvalidDataException("DSF data boyutu geçersiz.");long start=f.Position,length=chunk-12;End(start,length,total);
  long channelBytes=checked((samples+7)/8),groups=checked((channelBytes+block-1)/block);if(checked(groups*block*channels)>length)throw new InvalidDataException("DSF örnek verisi eksik.");
  return new(rate,channels,samples,start,length,block,bitOrder==1,true);
 }
 static DsdInfo Dff(BinaryReader r)
 {
  var f=r.BaseStream;long root=End(12,Size(Be64(r)),f.Length);if(Id(r)!="DSD ")throw new InvalidDataException("DFF form türü geçersiz.");
  int rate=0,channels=0;string compression="";long dataStart=-1,dataLength=0;bool properties=false;
  while(f.Position+12<=root)
  {
   string id=Id(r);long length=Size(Be64(r)),start=f.Position,end=End(start,length,root);
   if(id=="PROP")
   {
    if(properties||length<4||Id(r)!="SND ")throw new InvalidDataException("DFF ses özellikleri geçersiz.");properties=true;
    while(f.Position+12<=end)
    {
     string property=Id(r);long size=Size(Be64(r)),p=f.Position,q=End(p,size,end);
     if(property=="FS  "){if(size!=4||rate!=0)throw new InvalidDataException("DFF oran alanı geçersiz.");rate=checked((int)Be32(r));}
     else if(property=="CHNL")
     {
      if(size<2||channels!=0)throw new InvalidDataException("DFF kanal alanı geçersiz.");channels=Be16(r);
      if(channels is <1 or >2||size<2+channels*4)throw new NotSupportedException("Bu sürümde mono/stereo DFF desteklenir.");
      string first=Id(r),second=channels==2?Id(r):"";
      if(channels==2&&(first!="SLFT"||second!="SRGT"))throw new NotSupportedException("DFF stereo kanal sırası SLFT/SRGT olmalı.");
     }
     else if(property=="CMPR"){if(size<4||compression.Length>0)throw new InvalidDataException("DFF sıkıştırma alanı geçersiz.");compression=Id(r);}
     f.Position=End(q,size%2,end);
    }
   }
   else if(id=="DSD "){if(dataStart>=0)throw new InvalidDataException("Birden çok DFF ses bloğu desteklenmiyor.");dataStart=start;dataLength=length;}
   else if(id=="DST ")throw new NotSupportedException("DST sıkıştırılmış DFF için DoP çözümü bu sürümde yok. Sıkıştırılmamış DFF veya DSF kullan.");
   f.Position=End(end,length%2,root);
  }
  if(compression!="DSD ")throw new NotSupportedException("DoP için sıkıştırılmamış DFF (DSD) gerekli; DST desteklenmiyor.");
  if(!properties||rate==0||channels==0||dataStart<0||dataLength==0||dataLength%channels!=0)throw new InvalidDataException("DFF ses bilgisi eksik.");
  return new(rate,channels,checked(dataLength/channels*8),dataStart,dataLength,0,false,false);
 }
}
// 24-bit little-endian DoP 1.1: chronological DSD bits occupy bits 15..0;
// bits 23..16 contain a shared 05/FA marker, alternating once per PCM frame.
public sealed class DopWaveStream : WaveStream
{
 readonly FileStream file;readonly DsdInfo info;readonly byte[] block;long cached=-1,frame;bool marker;
 public DsdInfo Info=>info;
 public override WaveFormat WaveFormat{get;}
 public override long Length{get;}
 public override long Position{get=>frame*WaveFormat.BlockAlign;set=>frame=Math.Clamp(value/WaveFormat.BlockAlign,0,Length/WaveFormat.BlockAlign);}
 static readonly byte[] Reversed=Enumerable.Range(0,256).Select(n=>{int v=n,r=0;for(int i=0;i<8;i++){r=r*2+(v&1);v>>=1;}return (byte)r;}).ToArray();
 public DopWaveStream(string path)
 {
  file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.RandomAccess);
  try{info=DsdContainer.Read(file);WaveFormat=new(info.CarrierRate,24,info.Channels);Length=checked((info.Samples+15)/16*WaveFormat.BlockAlign);block=new byte[info.Planar?info.BlockSize*info.Channels:8192];}
  catch{file.Dispose();throw;}
 }
 byte ChannelByte(int channel,long index)
 {
  long bytes=(info.Samples+7)/8;if(index>=bytes)return 0x69;
  long offset=info.Planar?index/info.BlockSize*block.Length:index*info.Channels;
  long group=info.Planar?offset:offset/block.Length*block.Length;
  if(cached!=group)
  {
   file.Position=info.DataStart+group;int length=(int)Math.Min(block.Length,info.DataLength-group);if(length<=0)throw new InvalidDataException("Eksik DSD ses verisi.");file.ReadExactly(block.AsSpan(0,length));cached=group;
  }
  int position=info.Planar?channel*info.BlockSize+(int)(index%info.BlockSize):(int)(offset-group)+channel;
  byte data=block[position];if(info.LeastBitFirst)data=Reversed[data];
  int valid=(int)(info.Samples%8);if(valid>0&&index==bytes-1){int mask=(0xff<<(8-valid))&0xff;data=(byte)((data&mask)|(0x69&~mask));}
  return data;
 }
 public override int Read(byte[] buffer,int offset,int count)
 {
  ArgumentOutOfRangeException.ThrowIfNegative(offset);ArgumentOutOfRangeException.ThrowIfNegative(count);if(count>buffer.Length-offset)throw new ArgumentException("DoP tamponu geçersiz.");
  int frames=(int)Math.Min(count/WaveFormat.BlockAlign,Length/WaveFormat.BlockAlign-frame),written=0;
  for(int i=0;i<frames;i++)
  {
   byte mark=marker?(byte)0xfa:(byte)0x05;
   for(int ch=0;ch<info.Channels;ch++){buffer[offset+written++]=ChannelByte(ch,frame*2+1);buffer[offset+written++]=ChannelByte(ch,frame*2);buffer[offset+written++]=mark;}
   frame++;marker=!marker;
  }
  return written;
 }
 protected override void Dispose(bool disposing){if(disposing)file.Dispose();base.Dispose(disposing);}
}
