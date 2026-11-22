using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Diagnostics;
using NAudio.Wave;
namespace Faemyr;
public static class VisualizerUiTests
{
 static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(owner)!;
 static async Task Wait(Func<bool> ready){for(int i=0;i<500;i++){if(ready())return;await Task.Delay(20);}throw new Exception("Visualizer UI timeout");}
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<object>();var frameMetrics=new List<object>();var uiCosts=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Themes.Load();MainWindow? window=null;
  try
  {
   string path=Path.Combine(directory,"visualizer-silence.wav");var format=new WaveFormat(44100,16,2);using(var writer=new WaveFileWriter(path,format))writer.Write(new byte[format.AverageBytesPerSecond*30],0,format.AverageBytesPerSecond*30);var track=await MetadataReader.Read(path,default);track.Title="Visualizer sessiz test";track.Artist=track.AlbumArtist="Faemyr test";
   using(var db=new LibraryStore())db.Save(track);new AudioPreferences{Exclusive=false}.Save(Path.Combine(Paths.Data,"audio-settings.json"));PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{Volume=1,AutoPlayOnStartup=false});
   window=new MainWindow{ShowInTaskbar=false};window.Show();await Wait(()=>Field<bool>(window,"libraryReady")&&Field<bool>(window,"sessionReady"));typeof(MainWindow).GetField("view",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,"tracks");typeof(MainWindow).GetMethod("RefreshView",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,null);var liveTrack=Field<List<Track>>(window,"library").Single();await (Task)typeof(MainWindow).GetMethod("StartFromView",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,[liveTrack])!;
   var audio=Field<AudioEngine>(window,"audio");var view=Field<OscilloscopeView>(window,"Scope");await Wait(()=>audio.DeviceStarted&&view.Running&&Field<int>(view,"count")>0);
   foreach(string name in new[]{"UpdateStreamTrack","UpdateListening","SyncPlaybackVisuals","UpdateDesktopPlayback","UpdateNextTrack","UpdateLyricsPlayback","UpdateVolumeControls"})
   {
    var method=typeof(MainWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!;var clock=Stopwatch.StartNew();for(int run=0;run<5;run++)method.Invoke(window,name=="SyncPlaybackVisuals"?[false]:null);uiCosts.Add(new{name,averageMilliseconds=clock.Elapsed.TotalMilliseconds/5});
   }
   foreach(var (name,read) in new (string,Action)[]{("Audio.Position",()=>{_=audio.Position;}),("Audio.Duration",()=>{_=audio.Duration;}),("Audio.Signal",()=>{_=audio.Signal;})}){var clock=Stopwatch.StartNew();for(int run=0;run<5;run++)read();double average=clock.Elapsed.TotalMilliseconds/5;uiCosts.Add(new{name,averageMilliseconds=average});if(name=="Audio.Signal")Check(average<20,"Signal display avoids slow endpoint property queries on the UI thread");}
   typeof(MainWindow).GetMethod("EnterFullscreenPlayer",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,null);window.UpdateLayout();
   foreach(var mode in Enum.GetValues<VisualizerMode>())
   {
    typeof(MainWindow).GetMethod("StartFullscreenControlsAutoHide",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,null);view.Mode=mode;var frame=Field<TimeSpan>(view,"lastFrame");await Wait(()=>Field<TimeSpan>(view,"lastFrame")>frame&&Field<int>(view,"count")>0);await Task.Delay(120);
    Check(view.Running&&Field<int>(view,"count")>0,"Fullscreen with reactive background continuously renders consumed frames in "+mode);
    Check(await MeasureCadence(view,mode,frameMetrics),"Visualizer stays near display-limited 60 fps without long frame gaps: "+mode);
   }
   audio.Toggle();await Wait(()=>!view.Running);var pausedFrame=Field<TimeSpan>(view,"lastFrame");await Task.Delay(120);Check(!audio.DeviceStarted&&Field<TimeSpan>(view,"lastFrame")==pausedFrame,"Pausing stops both endpoint and visualizer refresh");
   audio.Position=TimeSpan.FromSeconds(5);audio.Toggle();await Wait(()=>view.Running&&Field<int>(view,"count")>0&&audio.Position.TotalSeconds>5.1);Check(ReferenceEquals(Field<ScopeCapture>(view,"capture"),audio.Scope),"Seek replaces visual history and resumes on new capture");
   window.WindowState=WindowState.Minimized;await Wait(()=>!view.Running);Check(!view.Running,"Minimizing suspends visualizer rendering");window.WindowState=WindowState.Normal;await Wait(()=>view.Running&&Field<int>(view,"count")>0);Check(view.Running,"Restoring window resumes live visualization");window.Close();window=null;
  }
  finally{window?.Close();File.WriteAllText(Path.Combine(directory,"visualizer-ui-results.json"),JsonSerializer.Serialize(new{version=typeof(AudioEngine).Assembly.GetName().Version?.ToString(),checks,frameMetrics,uiCosts,hardwareOutput="Synthetic silence only"},new JsonSerializerOptions{WriteIndented=true}));}
 }
 static async Task<bool> MeasureCadence(OscilloscopeView view,VisualizerMode mode,List<object> metrics)
 {
  var compositor=new List<double>();var accepted=new List<double>();TimeSpan last=TimeSpan.MinValue;
  void Observe(object? sender,EventArgs args){if(args is not RenderingEventArgs frame)return;double time=frame.RenderingTime.TotalMilliseconds;if(compositor.Count==0||time!=compositor[^1])compositor.Add(time);var current=Field<TimeSpan>(view,"lastFrame");if(current!=last){accepted.Add(current.TotalMilliseconds);last=current;}}
  CompositionTarget.Rendering+=Observe;try{await Task.Delay(1800);}finally{CompositionTarget.Rendering-=Observe;}
  double Rate(List<double> times)=>times.Count<2?0:1000*(times.Count-1)/(times[^1]-times[0]);var gaps=accepted.Zip(accepted.Skip(1),(a,b)=>b-a).Order().ToArray();
  metrics.Add(new{mode=mode.ToString(),compositorFps=Rate(compositor),visualizerFps=Rate(accepted),p95FrameMilliseconds=gaps.Length==0?0:gaps[(int)((gaps.Length-1)*.95)],maximumFrameMilliseconds=gaps.LastOrDefault(),frames=accepted.Count});
  return Rate(accepted)>=Math.Min(60,Rate(compositor))*.85&&gaps.Length>0&&gaps[(int)((gaps.Length-1)*.95)]<Math.Max(40,2000/Math.Max(1,Rate(compositor)));
 }
}
