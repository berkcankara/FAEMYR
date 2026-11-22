using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;

namespace Faemyr;
public static class PerformanceRegressionTests
{
 public static async Task Run(string directory,Action<bool,string> check)
 {
  string root=Path.Combine(directory,"performance-regressions");Directory.CreateDirectory(root);
  string flac=Path.Combine(directory,"Türkçe çalışma 16.flac");
  var track=await MetadataReader.Read(flac,default,root);
  string cache=Path.Combine(root,"concurrent-pcm");
  var decoded=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>AudioEngine.Decode(track,cache,default)));
  check(decoded.Distinct().Count()==1&&new FileInfo(decoded[0]).Length>44,"Concurrent same-track decode publishes one complete PCM cache file");
  check(!Directory.EnumerateFiles(cache,"*.partial.wav").Any(),"Concurrent decoders remove only their own temporary files");
  using(var cancellation=new CancellationTokenSource())
  {
   string racingCache=Path.Combine(root,"cancel-race");
   var abandoned=AudioEngine.Decode(track,racingCache,cancellation.Token);
   var survivor=AudioEngine.Decode(track,racingCache,default);cancellation.Cancel();
   try{await abandoned;}catch(OperationCanceledException){}
   string complete=await survivor;
   check(File.Exists(complete)&&new FileInfo(complete).Length==new FileInfo(decoded[0]).Length,"Cancelling one decoder cannot delete another playback request's output");
   check(!Directory.EnumerateFiles(racingCache,"*.partial.wav").Any(),"Cancelled concurrent decode leaves no partial PCM output");
  }
  using(var cancellation=new CancellationTokenSource())
  {
   cancellation.Cancel();bool stopped=false;
   try{await AudioEngine.Decode(track,cache,cancellation.Token);}catch(OperationCanceledException){stopped=true;}
   check(stopped,"Cancelled playback cannot reuse an existing cache entry");
  }
  // A small limit exercises eviction without creating gigabytes of test files.
  string partial=Path.Combine(cache,"active.partial.wav");using(var f=File.Create(partial))f.SetLength(1024);
  AudioEngine.TrimCache(cache,decoded[0],1);
  check(File.Exists(partial)&&File.Exists(decoded[0]),"Cache trimming preserves active decoder output and currently playing PCM");File.Delete(partial);
  string dbRoot=Path.Combine(root,"library");
  using(var database=new LibraryStore(dbRoot))
  {
   var old=JsonSerializer.Deserialize<Track>(JsonSerializer.Serialize(track))!;old.BitRate=0;old.BitRateSource="";database.Save(old);
   var loaded=database.Load().Single();
   var notified=new List<string>();loaded.PropertyChanged+=(_,e)=>notified.Add(e.PropertyName??"");
   var ready=new TaskCompletionSource<(long Rate,string Source)>(TaskCreationOptions.RunContinuationsAsynchronously);
   using(var rates=new BitrateQueue((_,rate,source)=>ready.TrySetResult((rate,source))))
   {
    rates.Enqueue(loaded);var result=await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));loaded.BitRateSource=result.Source;loaded.BitRate=result.Rate;database.Save(loaded);
   }
   check(database.Load().Single().BitRate==track.BitRate&&loaded.BitRate>0,"Legacy bitrate is calculated on demand and persists across library reloads");
   check(notified.Contains(nameof(Track.BitRateLabel))&&notified.Contains(nameof(Track.BitRateHint)),"Bitrate cells refresh after background metadata completion");
  }
  using(var connection=new SqliteConnection("Data Source="+Path.Combine(dbRoot,"library.db")))
  {
   connection.Open();using var cmd=connection.CreateCommand();cmd.CommandText="INSERT INTO tracks VALUES($path,$json)";
   cmd.Parameters.AddWithValue("$path",track.Path.ToUpperInvariant());cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(new Track{Path=track.Path.ToUpperInvariant(),Modified=track.Modified+1,Title="Latest",Artist=null!,AlbumArtist=null!,Fields=null!,Seconds=-1}));cmd.ExecuteNonQuery();
  }
  using(var database=new LibraryStore(dbRoot))
  {
   var records=database.Load();check(records.Count==1&&records[0].Title=="Latest","Case-only duplicate library paths load once and preserve the latest record");
   check(records[0].Fields.Count==0&&records[0].Artist!=null&&records[0].Seconds==0,"Malformed optional legacy fields do not crash library views");
  }
  var oldTrack=new Track{Path="replacement.flac",Modified=1};var freshTrack=new Track{Path=oldTrack.Path,Modified=2};
  var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var finished=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  int analyses=0;var published=new List<Track>();
  using(var queue=new WaveformQueue(root,(item,_,_)=>{published.Add(item);if(ReferenceEquals(item,freshTrack))finished.TrySetResult();},async (item,ct)=>
  {
   Interlocked.Increment(ref analyses);if(ReferenceEquals(item,oldTrack)){started.TrySetResult();await release.Task.WaitAsync(ct);}return new WaveformData();
  }))
  {
   queue.Enqueue(oldTrack);await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
   for(int i=0;i<100;i++)queue.Enqueue(oldTrack,true);
   queue.Enqueue(freshTrack,true);for(int i=0;i<100;i++)queue.Enqueue(freshTrack,true);
   release.TrySetResult();await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
   check(analyses==2&&published.Count==1&&ReferenceEquals(published[0],freshTrack),"Waveform priority requests coalesce and stale track instances cannot publish results");
  }
  var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawRectangle(Brushes.MediumPurple,null,new Rect(0,0,2048,2048));
  var bitmap=new RenderTargetBitmap(2048,2048,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
  string cover=Path.Combine(root,"large-cover.png");var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(cover))encoder.Save(file);
  var thumbs=await Task.WhenAll(Enumerable.Range(0,20).Select(_=>ThumbnailCache.Load(cover)));
  check(thumbs[0] is {PixelWidth:<=320,IsFrozen:true}&&thumbs.All(t=>ReferenceEquals(t,thumbs[0])),"Large cover thumbnails decode once at bounded resolution and are safe to share");
  string corrupt=Path.Combine(root,"corrupt.img");await File.WriteAllTextAsync(corrupt,"invalid");
  check(await ThumbnailCache.Load(corrupt)==null&&await ThumbnailCache.Load(Path.Combine(root,"missing.png"))==null,"Corrupt and missing artwork use the fallback without throwing");
 }
}
