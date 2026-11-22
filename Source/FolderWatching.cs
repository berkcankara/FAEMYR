using System.IO;
using System.Windows.Threading;

namespace Faemyr;
public partial class MainWindow
{
 readonly List<FileSystemWatcher> folderWatchers=[];
 readonly Dictionary<string,int> changedPaths=new(StringComparer.OrdinalIgnoreCase);
 readonly DispatcherTimer importTimer=new(){Interval=TimeSpan.FromSeconds(3)};
 readonly CancellationTokenSource importsCancelled=new();bool importing;int watcherGeneration;
 void InitializeFolderWatching(){RegisterFolderWatchers();libraryReconcilePending=true;importTimer.Tick+=async (_,_)=>await ImportChangedFiles();importTimer.Start();}
 async void RegisterFolderWatchers()
 {
  int generation=++watcherGeneration;var roots=store.Roots();
  foreach(var w in folderWatchers)w.Dispose();folderWatchers.Clear();
  var result=await Task.Run(()=>
  {
   var watchers=new List<FileSystemWatcher>();var errors=new List<string>();
   foreach(string root in roots)try
   {
    if(!Directory.Exists(root))continue;
    var w=new FileSystemWatcher(root){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.DirectoryName|NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size,InternalBufferSize=32768};
    try
    {
     w.Created+=FileChanged;w.Changed+=FileChanged;w.Deleted+=FileChanged;w.Renamed+=FileRenamed;
     w.Error+=(_,_)=>{if(!closed)Dispatcher.BeginInvoke(()=>{if(!closed){libraryReconcilePending=true;Status.Text=L.T("Klasör izleyicisi yoğunluk nedeniyle değişiklik kaçırmış olabilir; Arşivi yenile ile tamamlayabilirsin.");}});};
     w.EnableRaisingEvents=true;watchers.Add(w);
    }catch{w.Dispose();throw;}
   }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){errors.Add(ex.Message);}
   return (Watchers:watchers,Errors:errors);
  });
  if(closed||generation!=watcherGeneration){foreach(var w in result.Watchers)w.Dispose();return;}
  folderWatchers.AddRange(result.Watchers);if(result.Errors.Count>0)Status.Text=L.T("Klasör izlenemedi: ")+string.Join(" · ",result.Errors);
 }
 void FileRenamed(object sender,RenamedEventArgs e)
 {
  if(closed)return;Dispatcher.BeginInvoke(()=>
  {
   if(closed)return;
   try
   {
    if(Directory.Exists(e.FullPath))foreach(string source in library.Select(t=>t.AudioPath).Distinct(StringComparer.OrdinalIgnoreCase).Where(p=>p.StartsWith(e.OldFullPath+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).ToArray())RelocateLibraryFile(source,System.IO.Path.Combine(e.FullPath,System.IO.Path.GetRelativePath(e.OldFullPath,source)));
    else if(File.Exists(e.FullPath)&&library.Any(t=>t.AudioPath.Equals(e.OldFullPath,StringComparison.OrdinalIgnoreCase)))RelocateLibraryFile(e.OldFullPath,e.FullPath);
    libraryReconcilePending=true;RefreshView();FileChanged(sender,e);
   }catch(Exception ex){Status.Text=L.T("Taşınan kayıt eşleştirilemedi: ")+ex.Message;}
  });
 }
 void FileChanged(object s,FileSystemEventArgs e)
 {
  if(e.ChangeType==WatcherChangeTypes.Deleted&&!closed)Dispatcher.BeginInvoke(()=>{if(!closed)libraryReconcilePending=true;});
  string path=e.FullPath;if(path.StartsWith(Paths.Data+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return;var ext=System.IO.Path.GetExtension(path);if(ext.Equals(".wvc",StringComparison.OrdinalIgnoreCase))path=System.IO.Path.ChangeExtension(path,".wv");else if(!Extensions.Contains(ext))return;
  if(closed)return;string pending=path;Dispatcher.BeginInvoke(()=>{if(!closed)changedPaths[pending]=0;});
 }
 async Task ImportChangedFiles()
 {
  if(closed||importing||reconcilingLibrary||scanCts!=null||(!libraryReconcilePending&&changedPaths.Count==0))return;importing=true;
  try
  {
   bool imported=false;
   var batch=changedPaths.Take(12).ToList();foreach(var item in batch){changedPaths.Remove(item.Key);if(!File.Exists(item.Key))continue;
    try{var cuePaths=library.Where(t=>t.IsCue&&t.AudioPath.Equals(item.Key,StringComparison.OrdinalIgnoreCase)).Select(t=>t.CueSheetPath).Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToArray();if(cuePaths.Length>0){foreach(var cue in cuePaths){var tracks=await Task.Run(()=>ReadLibraryFile(cue,importsCancelled.Token),importsCancelled.Token);if(closed)return;ApplyImportedTracks(cue,tracks);}}else{var tracks=await Task.Run(()=>ReadLibraryFile(item.Key,importsCancelled.Token),importsCancelled.Token);if(closed)return;ApplyImportedTracks(item.Key,tracks);}}
    catch(OperationCanceledException){return;}catch(Exception ex){if(!closed){if(item.Value<4&&!changedPaths.ContainsKey(item.Key))changedPaths[item.Key]=item.Value+1;else Status.Text=L.T("Otomatik eklenemedi: ")+System.IO.Path.GetFileName(item.Key)+" · "+ex.Message;}continue;}
    imported=true;
   }
   if(!closed&&libraryReconcilePending){libraryReconcilePending=false;await ReconcileLibraryAsync(ct:importsCancelled.Token);}
   if(imported&&!closed)RefreshView();
  }finally{importing=false;}
 }
 void StopFolderWatching(){watcherGeneration++;importTimer.Stop();importsCancelled.Cancel();foreach(var w in folderWatchers)w.Dispose();folderWatchers.Clear();}
}
