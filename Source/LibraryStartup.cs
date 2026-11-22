using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace Faemyr;
public partial class MainWindow
{
 bool libraryReady;
 BitrateQueue? bitrates;
 readonly DispatcherTimer searchDelay=new(){Interval=TimeSpan.FromMilliseconds(160)};
 async void LoadLibrary(object sender,RoutedEventArgs e)
 {
  Loaded-=LoadLibrary;
  searchDelay.Tick+=(_,_)=>{searchDelay.Stop();RefreshView();};
  try
  {
   var loaded=await Task.Run(()=>
   {
    var database=new LibraryStore();
    try
    {
     var tracks=database.Load();AlbumGrouping.Apply(tracks);
     var savedFavorites=new FavoriteStore(Paths.Data);savedFavorites.MigrateAlbumEditions(tracks);
     foreach(var track in tracks)savedFavorites.Apply(track);
     var history=new ListeningStore(Paths.Data);history.Ensure(tracks);
     var session=PlayerSession.Load(SessionPath);
     var resolved=session?.Resolve(tracks);
     return (Store:database,Tracks:tracks,Favorites:savedFavorites,History:history,Session:session,Resolved:resolved);
    }
    catch{database.Dispose();throw;}
   });
   if(closed){loaded.Store.Dispose();return;}
   store=loaded.Store;favorites=loaded.Favorites;listening=loaded.History;library.AddRange(loaded.Tracks);StartupMetrics.Mark("libraryLoaded");
   try
   {
    string path=Path.Combine(Paths.Data,"queue.json");
    if(loaded.Session==null&&File.Exists(path))
    {
     var saved=JsonSerializer.Deserialize<List<string>>(await File.ReadAllTextAsync(path))??[];
     if(closed)return;
     var lookup=library.ToDictionary(t=>t.Path,StringComparer.OrdinalIgnoreCase);
     queue.AddRange(saved.Where(p=>p!=null&&lookup.ContainsKey(p)).Select(p=>lookup[p].Copy()));
    }
   }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){Status.Text=L.T("Kayıtlı kuyruk okunamadı: ")+ex.Message;}
   if(closed)return;
   bitrates=new BitrateQueue((track,rate,source)=>Dispatcher.BeginInvoke(()=>
   {
    if(closed||!library.Contains(track))return;
    track.BitRateSource=source;track.BitRate=rate;
    try{store.Save(track);}catch(Exception ex)when(ex is IOException or Microsoft.Data.Sqlite.SqliteException){Status.Text=L.T("Bitrate kaydedilemedi: ")+ex.Message;}
    if(ReferenceEquals(inspectedTrack,track))ApplyPanelVisibility();
   }));
   order.Reset(queue.Count,queueIndex);libraryReady=true;WindowRoot.IsEnabled=true;
   if(loaded.Session!=null&&loaded.Resolved is {} resolved)RestoreSession(loaded.Session,resolved);
   else{sessionReady=true;RefreshView();}
   StartupMetrics.Mark("viewBound");
   await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
   if(closed)return;
   // First-page thumbnails are included in the diagnostic, not just the empty shell.
   if(StartupMetrics.Enabled){await ThumbnailCache.WhenIdle();StartupMetrics.Finish(this,library.Count);return;}
   InitializeFolderWatching();await RefreshDevicesAsync();StartDeviceMonitoring();
   if(!closed&&playerPreferences.AutoPlayOnStartup&&resumePrepared&&now!=null&&playCts==null&&!audio.IsPlaying&&!audio.IsPaused)await PlayTrack(now,TimeSpan.FromSeconds(resumeSeconds));
   // Existing waveforms load with their album/list; new imports still analyze automatically.
  }
  catch(Exception ex)
  {
   if(closed)return;
   StartupMetrics.Fail(ex);if(StartupMetrics.Enabled){Close();return;}
   LibraryStats.Text=L.T("Arşiv açılamadı.");Status.Text=ex.Message;
   MessageBox.Show(this,L.T("Arşiv okunamadı. Müzik dosyaların korunuyor.\n")+ex.Message,"Faemyr",MessageBoxButton.OK,MessageBoxImage.Information);
  }
 }
 async Task RefreshDevicesAsync()
 {
  var id=(DeviceChoice.SelectedItem as DeviceOption)?.Id;
  try
  {
   var devices=await Task.Run(AudioEngine.Devices);if(closed)return;id=audioPreferences.DeviceId;
   var options=new List<DeviceOption>{new(null,L.T("Windows varsayılan aygıtı"))};options.AddRange(devices.Select(x=>new DeviceOption(x.Id,x.Name)));
   if(id!=null&&!options.Any(x=>x.Id==id))options.Add(new(id,L.T("Kayıtlı aygıt · bağlı değil")));
   updatingAudioChoices=true;try{DeviceChoice.ItemsSource=options;DeviceChoice.SelectedItem=options.FirstOrDefault(x=>x.Id==id)??options[0];}finally{updatingAudioChoices=false;}
  }catch(Exception ex){if(!closed)Status.Text=ex.Message;}
 }
}

// Old libraries acquire bitrate on demand; no opening thousands of music files at launch.
public sealed class BitrateQueue : IDisposable
{
 readonly HashSet<Track> pending=[];
 readonly System.Collections.Concurrent.ConcurrentQueue<Track> items=new();
 readonly SemaphoreSlim available=new(0);
 readonly CancellationTokenSource cancel=new();
 readonly Action<Track,long,string> completed;
 public BitrateQueue(Action<Track,long,string> completed){this.completed=completed;_=Task.Run(Work);}
 public void Enqueue(Track track)
 {
  if(track.BitRate>0||cancel.IsCancellationRequested)return;
  lock(pending){if(!pending.Add(track))return;}
  items.Enqueue(track);available.Release();
 }
 async Task Work()
 {
  try
  {
   while(true)
   {
    await available.WaitAsync(cancel.Token);if(!items.TryDequeue(out var track))continue;
    try
    {
     var snapshot=new Track{Path=track.Path,Seconds=track.Seconds,ProbeJson=track.ProbeJson};
     MetadataReader.UpdateBitrate(snapshot);cancel.Token.ThrowIfCancellationRequested();
     if(snapshot.BitRate>0)completed(track,snapshot.BitRate,snapshot.BitRateSource);
    }
    catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException){}
    finally{lock(pending)pending.Remove(track);}
   }
  }catch(OperationCanceledException){}
 }
 public void Dispose()=>cancel.Cancel();
}
