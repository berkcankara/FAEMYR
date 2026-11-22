using System.IO;
using NAudio.Wave;
namespace Faemyr;
public sealed record PlaybackEntry(Track Track,int QueueIndex,GainDecision Gain,PlaybackOrderState Order,TimeSpan Start){public Guid Id{get;init;}=Guid.NewGuid();}
public sealed class PlaybackPlan
{
 readonly Track[] tracks;readonly PlaybackOrder order;readonly Func<Track,GainDecision> gain;int index;readonly bool gapless;
 public PlaybackPlan(IEnumerable<Track> tracks,int index,PlaybackOrder order,Func<Track,GainDecision> gain,bool gapless=true){this.tracks=tracks.ToArray();this.index=index;this.order=order.Clone();this.gain=gain;this.gapless=gapless;}
 public PlaybackEntry? Next()
 {
  if(!gapless)return null;index=order.Next(tracks.Length,index,true);if(index<0)return null;var track=tracks[index];return new(track,index,gain(track),order.Capture(),TimeSpan.Zero);
 }
 public PlaybackPlan Rebase(PlaybackEntry current)=>new(tracks,current.QueueIndex,From(current.Order),gain,gapless);
 static PlaybackOrder From(PlaybackOrderState state){var o=new PlaybackOrder();o.Restore(state);return o;}
}
public sealed class PcmSequenceStream : WaveStream,IAbortableAudio
{
 sealed class Clip(PlaybackEntry entry,int gainBits):IDisposable
 {
  public readonly PlaybackEntry Entry=entry;public readonly DecoderWaveStream Stream=new(entry.Track,entry.Start);
  public IWaveProvider Source=null!;
  public void Initialize(bool warm=false){Source=Entry.Gain.ChangesSamples?new GainSource(Stream,Entry.Gain.Multiplier,gainBits):Stream;if(warm){byte[] first=new byte[Source.WaveFormat.BlockAlign*2048];int count=ExactWasapiOut.ValidateRead(Source,first,first.Length);if(count==0)throw new InvalidDataException("Sıradaki ses kaydı boş.");Source=new PrefixSource(Source,first,count);}}
  public void Dispose()=>Stream.Dispose();
 }
 public sealed record Boundary(long Frame,PlaybackEntry Entry);
 readonly PlaybackPlan plan;readonly int gainBits;readonly CancellationTokenSource cancel=new();readonly object gate=new();readonly List<Boundary> boundaries=[];
 sealed class PrefixSource(IWaveProvider source,byte[] first,int length):IWaveProvider{int offset;public WaveFormat WaveFormat=>source.WaveFormat;public int Read(byte[] bytes,int at,int count){if(offset>=length)return source.Read(bytes,at,count);int n=Math.Min(count,length-offset);Array.Copy(first,offset,bytes,at,n);offset+=n;return n;}}
 Clip? current;Task<Clip?>? next;long position,clipStart;volatile bool atEnd;readonly WaveFormat format;bool disposed;
 public bool AtEnd=>atEnd;public IReadOnlyList<Boundary> Boundaries{get{lock(gate)return boundaries.ToArray();}}
 public override WaveFormat WaveFormat=>format;public override long Length=>atEnd?position:long.MaxValue/WaveFormat.BlockAlign*WaveFormat.BlockAlign;
 public override long Position{get=>position;set=>throw new NotSupportedException("Akış konumu ses motorundan değiştirilir.");}
 public PcmSequenceStream(PlaybackEntry first,PlaybackPlan plan,int gainBits=32)
 {
  this.plan=plan;this.gainBits=gainBits;current=new(first,gainBits);current.Initialize();format=current.Source.WaveFormat;boundaries.Add(new(0,first));ScheduleNext();
 }
 void ScheduleNext()
 {
  next=Task.Run(()=>
  {
   cancel.Token.ThrowIfCancellationRequested();var entry=plan.Next();if(entry==null||entry.Track.IsDsd||!File.Exists(entry.Track.AudioPath))return null;Clip? clip=null;
   try{clip=new(entry,gainBits);using var registration=cancel.Token.Register(clip.Stream.Abort);clip.Initialize(true);cancel.Token.ThrowIfCancellationRequested();if(!AudioFormats.SameSignalFormat(format,clip.Source.WaveFormat)){clip.Dispose();return null;}return clip;}catch(OperationCanceledException){clip?.Dispose();throw;}catch{clip?.Dispose();return null;}
  });
 }
 public (PlaybackEntry Entry,TimeSpan Position) Audible(double seconds)
 {
  long frame=Math.Max(0,(long)(seconds*format.SampleRate));lock(gate){var boundary=boundaries.Last(b=>b.Frame<=frame);return(boundary.Entry,TimeSpan.FromSeconds(Math.Clamp(boundary.Entry.Start.TotalSeconds+(frame-boundary.Frame)/(double)format.SampleRate,0,boundary.Entry.Track.Seconds)));}
 }
 public override int Read(byte[] buffer,int offset,int count)
 {
  cancel.Token.ThrowIfCancellationRequested();if(atEnd)return 0;count-=count%format.BlockAlign;int used=0;
  while(used<count)
  {
   cancel.Token.ThrowIfCancellationRequested();int n=current!.Source.Read(buffer,offset+used,count-used);if(n%format.BlockAlign!=0)throw new InvalidDataException("Gapless kaynağı eksik örnek döndürdü.");used+=n;position+=n;if(n>0)continue;
   if(position==clipStart)throw new InvalidDataException("Ses kaydı örnek içermiyor.");var ready=next!.GetAwaiter().GetResult();cancel.Token.ThrowIfCancellationRequested();if(ready==null){atEnd=true;break;}
   current.Dispose();current=ready;clipStart=position;lock(gate)boundaries.Add(new(position/format.BlockAlign,ready.Entry));ScheduleNext();
  }
  return used;
 }
 public void Abort(){cancel.Cancel();current?.Stream.Abort();var pending=next;if(pending?.IsCompletedSuccessfully==true)pending.Result?.Stream.Abort();}
 protected override void Dispose(bool disposing)
 {
  if(disposing&&!disposed){disposed=true;Abort();try{next?.Wait(5000);}catch(AggregateException){}if(next?.IsCompletedSuccessfully==true)next.Result?.Dispose();current?.Dispose();cancel.Dispose();}base.Dispose(disposing);
 }
}
