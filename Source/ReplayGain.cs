using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using NAudio.Wave;

namespace Faemyr;
public enum GainMode{Off,Track,Album}
public sealed class AudioPreferences
{
 public bool Exclusive{get;set;}=true;
 public bool AutomaticOutput{get;set;}
 public string? DeviceId{get;set;}
 public GainMode ReplayGain{get;set;}=GainMode.Off;
 public double PreampDb{get;set;}
 public bool PreventClipping{get;set;}=true;
 public bool DopEnabled{get;set;}
 public HashSet<string> DopDevices{get;set;}=new(StringComparer.Ordinal);
 public Dictionary<string,int> PcmRateLimits{get;set;}=new(StringComparer.Ordinal);
 public int PcmRate(string device,int sourceRate)=>PcmRateLimits.TryGetValue(device,out int limit)&&limit is 48000 or 96000?Math.Min(sourceRate,limit):sourceRate;
 public AudioPreferences Copy()=>new(){Exclusive=Exclusive,AutomaticOutput=AutomaticOutput,DeviceId=DeviceId,ReplayGain=ReplayGain,PreampDb=PreampDb,PreventClipping=PreventClipping,DopEnabled=DopEnabled,DopDevices=new(DopDevices,StringComparer.Ordinal),PcmRateLimits=new(PcmRateLimits,StringComparer.Ordinal)};
 public static AudioPreferences Load(string path)
 {
  try{string json=File.ReadAllText(path);var p=JsonSerializer.Deserialize<AudioPreferences>(json)??new();using var document=JsonDocument.Parse(json);if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException("Ses ayarları nesne olmalı.");if(!document.RootElement.TryGetProperty(nameof(AutomaticOutput),out _))p.AutomaticOutput=true;if(!Enum.IsDefined(p.ReplayGain))p.ReplayGain=GainMode.Off;p.PreampDb=double.IsFinite(p.PreampDb)?Math.Clamp(p.PreampDb,-12,12):0;p.DopDevices=new((p.DopDevices??[]).Where(id=>!string.IsNullOrWhiteSpace(id)),StringComparer.Ordinal);p.PcmRateLimits=(p.PcmRateLimits??[]).Where(pair=>!string.IsNullOrWhiteSpace(pair.Key)&&pair.Value is 48000 or 96000).ToDictionary(pair=>pair.Key,pair=>pair.Value,StringComparer.Ordinal);return p;}
  catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){return new(){AutomaticOutput=true};}
 }
 public void Save(string path)
 {
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
 public void RequireDop(string? selectedDevice)
 {
  if(!DopEnabled||selectedDevice==null||!DopDevices.Contains(selectedDevice))throw new InvalidOperationException("DSD/DoP için Ayarlar → Ses bölümünden DoP destekli DAC'ını açıkça seçip bu aygıt için DoP desteğini doğrula. Windows varsayılan aygıtına otomatik DoP gönderilmez.");
 }
}
public sealed record GainMeasurement(double GainDb,double Peak);
public sealed record GainDecision(double Multiplier,string Description)
{
 public bool ChangesSamples=>Math.Abs(Multiplier-1)>1e-12;
 public static readonly GainDecision Off=new(1,"ReplayGain kapalı · özgün örnekler");
}
public static class ReplayGain
{
 public static string Tag(string value)=>value[(value.LastIndexOf('/')+1)..].Trim().Replace("_","").Replace(" ","").ToUpperInvariant();
 static double? Number(string? value,bool decibels)
 {
  if(value==null)return null;string text=decibels?Regex.Replace(value,@"\s*dB\s*$","",RegexOptions.IgnoreCase):value;
  if(double.TryParse(text.Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n))return n;return null;
 }
 public static GainMeasurement? Tags(Track track,GainMode mode)
 {
  string prefix=mode==GainMode.Album?"REPLAYGAINALBUM":"REPLAYGAINTRACK";
  double? gain=track.Fields.Where(f=>Tag(f.Key)==prefix+"GAIN").Select(f=>Number(f.Value,true)).FirstOrDefault(n=>n.HasValue);
  double? peak=track.Fields.Where(f=>Tag(f.Key)==prefix+"PEAK").Select(f=>Number(f.Value,false)).FirstOrDefault(n=>n is >0 and <=128);
  return gain is >=-60 and <=60?new(gain.Value,peak??1):null;
 }
 public static GainDecision Decide(Track track,AudioPreferences settings,GainMeasurement? measured=null)
 {
  if(settings.ReplayGain==GainMode.Off)return GainDecision.Off;
  var metadata=Tags(track,settings.ReplayGain);var value=metadata??measured;
  if(value==null||!double.IsFinite(value.GainDb)||!double.IsFinite(value.Peak)||value.Peak<0)return new(1,"ReplayGain bilgisi yok · özgün örnekler (yerel analiz yapılabilir)");
  double multiplier=Math.Pow(10,(Math.Clamp(value.GainDb,-60,60)+Math.Clamp(settings.PreampDb,-12,12))/20);
  bool limited=false;if(settings.PreventClipping){double peak=value.Peak>0?value.Peak:1;double cap=1/peak;if(multiplier>cap){multiplier=cap;limited=true;}}
  string mode=settings.ReplayGain==GainMode.Album?"Albüm":"Parça";
  return new(multiplier,$"ReplayGain {mode} · {20*Math.Log10(multiplier):+0.00;-0.00;0.00} dB · "+(metadata!=null?"etiket":"yerel analiz")+(limited?" · tepeye göre kazanç sınırlandı":"")+(Math.Abs(multiplier-1)>1e-12?" · sabit kazanç, bit-perfect değil":" · özgün örnekler"));
 }
}
// A single, constant gain factor, calculated once per track/album. No compressor or limiter.
// Double-precision arithmetic feeds a 24/32-bit integer output at the original sample rate.
public sealed class GainSource : IWaveProvider
{
 readonly IWaveProvider source;readonly WaveFormat input;readonly double multiplier;readonly int outputBytes,inputBytes;byte[] scratch=[];
 public WaveFormat WaveFormat{get;}
 public GainSource(IWaveProvider source,double multiplier,int outputBits=32)
 {
  this.source=source;input=source.WaveFormat.AsStandardWaveFormat();this.multiplier=multiplier;
  if(!double.IsFinite(multiplier)||multiplier<0||outputBits is not 24 and not 32)throw new ArgumentException("Geçersiz sabit kazanç.");
  if(!(input.Encoding==WaveFormatEncoding.Pcm&&input.BitsPerSample is 8 or 16 or 24 or 32||input.Encoding==WaveFormatEncoding.IeeeFloat&&input.BitsPerSample is 32 or 64))throw new NotSupportedException("Bu PCM türüne ReplayGain uygulanamıyor.");
  inputBytes=input.BitsPerSample/8;outputBytes=outputBits/8;WaveFormat=new(input.SampleRate,outputBits,input.Channels);
 }
 double Sample(ReadOnlySpan<byte> b)=>input.Encoding==WaveFormatEncoding.IeeeFloat?inputBytes==4?BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b)):BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(b)):inputBytes switch{1=>(b[0]-128)/128d,2=>BinaryPrimitives.ReadInt16LittleEndian(b)/32768d,3=>((b[0]|b[1]<<8|b[2]<<16)<<8>>8)/8388608d,_=>BinaryPrimitives.ReadInt32LittleEndian(b)/2147483648d};
 public int Read(byte[] buffer,int offset,int count)
 {
  int frames=count/WaveFormat.BlockAlign,required=frames*input.BlockAlign;if(required==0)return 0;if(scratch.Length<required)scratch=new byte[required];
  int read=source.Read(scratch,0,required);if(read%input.BlockAlign!=0)throw new InvalidDataException("Eksik PCM örnek çerçevesi.");
  int samples=read/inputBytes;double scale=outputBytes==4?2147483648d:8388608d;
  for(int i=0;i<samples;i++)
  {
   double value=Sample(scratch.AsSpan(i*inputBytes,inputBytes))*multiplier;if(!double.IsFinite(value))value=0;
   long n=(long)Math.Clamp(Math.Round(value*scale,MidpointRounding.ToEven),-scale,scale-1);int pos=offset+i*outputBytes;
   for(int b=0;b<outputBytes;b++)buffer[pos+b]=(byte)(n>>(b*8));
  }
  return samples*outputBytes;
 }
}
