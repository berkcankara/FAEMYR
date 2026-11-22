using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NAudio.Wave;

namespace Faemyr;
public static class PlayerStateTests
{
 sealed class Bytes(byte[] data,WaveFormat format):IWaveProvider{int position;public WaveFormat WaveFormat=>format;public int Read(byte[] buffer,int offset,int count){int n=Math.Min(count,data.Length-position);data.AsSpan(position,n).CopyTo(buffer.AsSpan(offset));position+=n;return n;}}
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
 static object? Call(MainWindow window,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args);
 static T Property<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetProperty(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
 static void Set(MainWindow window,string name,object value)=>typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,value);
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  async Task Until(Func<bool> condition){for(int i=0;i<400;i++){if(condition())return;await Task.Delay(25);}throw new Exception("Player state timeout");}
  foreach(var format in new[]{new WaveFormat(48000,8,2),new WaveFormat(48000,16,2),new WaveFormat(48000,24,2),new WaveFormat(48000,32,2),WaveFormat.CreateIeeeFloatWaveFormat(48000,2),WaveFormat.CreateCustomFormat(WaveFormatEncoding.IeeeFloat,48000,2,48000*16,16,64)})
  {
   int size=format.BitsPerSample/8;bool floating=format.Encoding==WaveFormatEncoding.IeeeFloat;var input=new byte[format.BlockAlign*1000];
   void Fill(){for(int i=0;i<input.Length;i+=size){var b=input.AsSpan(i,size);if(floating){if(size==4)BitConverter.TryWriteBytes(b,-.75f);else BitConverter.TryWriteBytes(b,-.75);}else switch(size){case 1:b[0]=32;break;case 2:BinaryPrimitives.WriteInt16LittleEndian(b,-24000);break;case 3:b[0]=0;b[1]=0;b[2]=160;break;case 4:BinaryPrimitives.WriteInt32LittleEndian(b,-1600000000);break;}}}Fill();
   byte[] Read(VolumeSource source){var output=new byte[input.Length+12];int n=source.Read(output,6,input.Length);Check(n==input.Length&&output.Take(6).Concat(output.TakeLast(6)).All(b=>b==0),"Read offset and length respected: "+format);return output.AsSpan(6,n).ToArray();}
   var unity=new VolumeSource(new Bytes(input,format),1);Check(Read(unity).SequenceEqual(input)&&unity.WaveFormat==format,"Unity volume preserves every byte and format: "+format);
   var half=Read(new VolumeSource(new Bytes(input,format),.5));double Sample(byte[] data)=>floating?(size==4?BitConverter.ToSingle(data):BitConverter.ToDouble(data)):size switch{1=>data[0]-128,2=>BinaryPrimitives.ReadInt16LittleEndian(data),3=>(data[0]|data[1]<<8|data[2]<<16)<<8>>8,_=>BinaryPrimitives.ReadInt32LittleEndian(data)};
   Check(Math.Abs(Sample(half)-Sample(input)*.5)<1e-9,"Attenuation preserves signed amplitude and channels: "+format);
   var mute=Read(new VolumeSource(new Bytes(input,format),0));Check(floating?Sample(mute)==0:mute.All(b=>b==(size==1?128:0)),"Mute uses actual silence, including unsigned PCM: "+format);
   var live=new VolumeSource(new Bytes(input,format),1);live.Level=0;var ramp=Read(live);Check(Sample(ramp.TakeLast(size).ToArray())==0,"Live change settles to silence within 10 ms: "+format);
  }
  Check(VolumeSource.SafeLevel(double.NaN)==0&&VolumeSource.SafeLevel(-1)==0&&VolumeSource.SafeLevel(3)==1,"Invalid volume is bounded and fails silent");
  using(var engine=new AudioEngine())
  {
   engine.Volume=.3;engine.Muted=true;Check(engine.Volume==.3&&engine.Muted,"Muting retains previous volume");engine.Muted=false;Check(engine.Volume==.3,"Unmuting restores previous level");
   bool refused=false;try{engine.PlayDop("not-a-file.dsf","test",new(){DopEnabled=true,DopDevices=["test"]});}catch(InvalidOperationException e){refused=e.Message.Contains("DoP taşıyıcısına");}Check(refused&&!engine.DeviceStarted,"Attenuated DoP is rejected before opening any audio device");
  }
  string prefs=Path.Combine(directory,"prefs-test.json");var preferences=new PlayerPreferences{Volume=.37,Muted=true};PlayerJson.Write(prefs,preferences);var restored=PlayerPreferences.Load(prefs);Check(restored.Volume==.37&&restored.Muted&&!restored.AutoPlayOnStartup,"Volume/mute persist; autoplay defaults off");
  File.WriteAllText(prefs,"{");Check(PlayerPreferences.Load(prefs).Volume==1&&!PlayerPreferences.Load(prefs).AutoPlayOnStartup,"Corrupt preferences safely recover without autoplay");
  string sessionPath=Path.Combine(directory,"model-session.json");var tracks=new[]{new Track{Path="a",Seconds=60},new Track{Path="b",Seconds=30}};var saved=new PlayerSession{Queue=["missing","a","b","a"],QueueIndex=3,TrackPath="a",Seconds=14.5,View="album",AlbumKey="key",ArtistName="artist",PlaylistId="id",Search="query",ScrollOffset=321,PanelOffset=120,SelectedPath="b",Shuffle=true,Repeat=RepeatMode.All};PlayerJson.Write(sessionPath,saved);var session=PlayerSession.Load(sessionPath)!;var mapped=session.Resolve(tracks,_=>true);
  Check(mapped.Queue.Select(t=>t.Path).SequenceEqual(new[]{"a","b","a"})&&mapped.Index==2&&mapped.Current==tracks[0]&&mapped.Seconds==14.5,"Restore preserves queue duplicates and exact index while skipping missing entries");
  Check(session.View=="album"&&session.AlbumKey=="key"&&session.ArtistName=="artist"&&session.PlaylistId=="id"&&session.Search=="query"&&session.ScrollOffset==321&&session.PanelOffset==120&&session.Shuffle&&session.Repeat==RepeatMode.All,"View, filters, selection, offsets and playback modes round-trip");
  Check(session.Resolve(tracks,_=>false).Current==null&&session.Resolve(tracks,_=>false).Index==-1,"Unavailable current track never falls back to autoplaying another track");session.Seconds=500;Check(session.Resolve(tracks,_=>true).Seconds<60,"Saved position clamped to track duration");File.WriteAllText(sessionPath,"{");Check(PlayerSession.Load(sessionPath)==null,"Corrupt session is ignored");PlayerJson.Write(sessionPath,saved);Check(!Directory.GetFiles(directory,"*.tmp").Any(),"Atomic saves leave no temporary files");
  Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Themes.Load();MainWindow? window=null;
  try
  {
   var paths=new List<string>();Track? first=null;using(var db=new LibraryStore())for(int i=0;i<45;i++)
   {
    string path=Path.Combine(directory,$"silent-state-{i:00}.wav");paths.Add(path);using(var writer=new WaveFileWriter(path,new WaveFormat(48000,16,2)))writer.Write(new byte[48000*4*20],0,48000*4*20);
    var track=new Track{Path=path,Title=$"Sessiz deneme {i:00}",Artist="Faemyr",AlbumArtist="Faemyr",Album="Oturum denemesi",Seconds=20,SampleRate=48000,Bits=16,Channels=2,SampleFormat="s16",Codec="pcm_s16le",Number=(uint)i+1};db.Save(track);first??=track;
   }
   PlayerJson.Write(Path.Combine(Paths.Data,"session.json"),new PlayerSession{Queue=paths,QueueIndex=2,TrackPath=paths[2],Seconds=7.25,View="album",AlbumKey=first!.AlbumKey,SelectedPath=paths[5]});PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{Volume=.42});new AudioPreferences{Exclusive=false}.Save(Path.Combine(Paths.Data,"audio-settings.json"));
   window=new MainWindow{ShowInTaskbar=false,Opacity=0,Width=1200,Height=760};window.Show();await Until(()=>Field<bool>(window,"sessionReady"));await Task.Delay(500);var audio=Field<AudioEngine>(window,"audio");
   Check(Field<Track>(window,"now").Path==paths[2]&&Field<int>(window,"queueIndex")==2&&Field<List<Track>>(window,"queue").Count==45,"Real window restores current song and queue");
   Check(!audio.IsPlaying&&!audio.IsPaused&&!audio.DeviceStarted&&Property<TimeSpan>(window,"PlayerPosition").TotalSeconds==7.25,"Startup restores position without initializing audio");
   Check(Field<string>(window,"view")=="album"&&Field<DataGrid>(window,"Tracks").SelectedItem is Track selected&&selected.Path==paths[5],"Real window restores album and independent row selection");
   Check(audio.Volume==.42&&Field<Slider>(window,"VolumeSlider").Value==42,"Persisted volume is applied before playback");
   await (Task)Call(window,"PreviousTrack")!;Check(Property<TimeSpan>(window,"PlayerPosition").TotalSeconds==0&&!audio.DeviceStarted,"Previous resets prepared track without starting audio");Field<Slider>(window,"Seek").Value=7.25;Call(window,"CommitSeek");Check(Property<TimeSpan>(window,"PlayerPosition").TotalSeconds==7.25&&!audio.DeviceStarted,"Seek can adjust prepared position before playback");
   await (Task)Call(window,"TogglePlayback")!;Check(audio.IsPlaying&&Field<Track>(window,"now").Path==paths[2]&&audio.Position.TotalSeconds>=7.2&&audio.Position.TotalSeconds<8.5,"Play resumes saved track/position even when a different row is selected");
   Check(audio.Signal.Contains("Ses %42")&&audio.Signal.Contains("özgün örnek değerleri değişir"),"Signal path discloses volume processing");Call(window,"Mute_Click",window,new RoutedEventArgs());Check(audio.Muted&&audio.Volume==.42&&audio.Signal.Contains("Sessiz"),"Mute control updates live engine and signal");Call(window,"Mute_Click",window,new RoutedEventArgs());Check(!audio.Muted&&audio.Volume==.42,"Unmute restores retained level");
   Field<Slider>(window,"VolumeSlider").Value=100;Check(audio.Volume==1&&!audio.Muted&&audio.Signal.Contains("işlemi atlanıyor"),"Slider at 100 percent bypasses volume processing");
   audio.Toggle();audio.Position=TimeSpan.FromSeconds(11.5);Set(window,"view","queue");Call(window,"RefreshView");await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);var scroll=(ScrollViewer)Call(window,"CurrentScroll")!;scroll.ScrollToVerticalOffset(420);await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);double offset=scroll.VerticalOffset;Check(offset>100,"Queue scroll is populated for restore test");
   Call(window,"SaveSession",false);await Field<Task>(window,"sessionWrite");var periodic=PlayerSession.Load(Path.Combine(Paths.Data,"session.json"))!;Check(periodic.View=="queue"&&Math.Abs(periodic.Seconds-11.5)<.1,"Periodic checkpoint persists state without closing");
   window.Close();window=null;window=new MainWindow{ShowInTaskbar=false,Opacity=0,Width=1200,Height=760};window.Show();await Until(()=>Field<bool>(window,"sessionReady"));await Task.Delay(500);audio=Field<AudioEngine>(window,"audio");
   Check(Field<string>(window,"view")=="queue"&&Math.Abs(Property<TimeSpan>(window,"PlayerPosition").TotalSeconds-11.5)<.1&&!audio.DeviceStarted,"Second launch restores paused position and page");Check(Math.Abs(((ScrollViewer)Call(window,"CurrentScroll")!).VerticalOffset-offset)<2,"Second launch restores scroll offset");window.Close();window=null;
   PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{Volume=0,AutoPlayOnStartup=true});window=new MainWindow{ShowInTaskbar=false,Opacity=0};window.Show();await Until(()=>Field<AudioEngine>(window,"audio").IsPlaying);audio=Field<AudioEngine>(window,"audio");Check(audio.DeviceStarted&&audio.Volume==0&&audio.Position.TotalSeconds>=11.4,"Opt-in autoplay resumes at saved time using saved volume (synthetic silence only)");window.Close();window=null;
  }
  finally{window?.Close();File.WriteAllText(Path.Combine(directory,"player-state-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
