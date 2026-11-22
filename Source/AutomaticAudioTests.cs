using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace Faemyr;
public static class AutomaticAudioTests
{
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<object>();var hardware=new List<object>();
  void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(!ok)throw new Exception(name);}
  try
  {
   Check(AutomaticAudioOutput.RequiresOpticalPcmGuard(8,new WaveFormat(96000,24,2))&&AutomaticAudioOutput.RequiresOpticalPcmGuard(7,new WaveFormat(192000,32,2))&&!AutomaticAudioOutput.RequiresOpticalPcmGuard(8,new WaveFormat(44100,16,2))&&!AutomaticAudioOutput.RequiresOpticalPcmGuard(1,new WaveFormat(96000,24,2)),"Packed high-resolution optical PCM blocked independently of automatic/manual choice");var mix=AudioFormats.Extensible(WaveFormat.CreateIeeeFloatWaveFormat(96000,2));
   foreach(int form in new[]{0,7,8,10})foreach(int rate in new[]{44100,48000,192000})
   {int queried=0;var choice=AutomaticAudioOutput.Select(new WaveFormat(rate,24,2),mix,form,_=>{queried++;return true;});Check(!choice.Exclusive&&ReferenceEquals(choice.Format,mix)&&queried==0,$"Unqueryable receiver {form}/{rate}: shared mix, no inferred optical limit");}
   foreach(int rate in new[]{44100,48000,88200,96000,176400,192000,352800,384000})foreach(int bits in new[]{16,24,32})
   {var source=new WaveFormat(rate,bits,2);var choice=AutomaticAudioOutput.Select(source,mix,1,f=>AudioFormats.SameSignalFormat(source,f));Check(choice.Exclusive&&ReferenceEquals(source,choice.Format),$"Native format and exact source provider retained: {rate}/{bits}");}
   var widened=AutomaticAudioOutput.Select(new WaveFormat(44100,16,2),mix,1,f=>f.SampleRate==44100&&f.BitsPerSample==24);Check(widened.Exclusive&&widened.Format.SampleRate==44100&&widened.Format.BitsPerSample==24,"Same-rate precision extension precedes rate reduction");
   var lowered=AutomaticAudioOutput.Select(new WaveFormat(192000,24,2),mix,9,f=>f.SampleRate<=96000&&f.BitsPerSample==24);Check(lowered.Exclusive&&lowered.Format.SampleRate==96000&&lowered.Format.BitsPerSample==24,"Highest reported rate below source selected on HDMI");
   var precision=AutomaticAudioOutput.Select(new WaveFormat(192000,24,2),mix,1,f=>f.SampleRate==192000&&f.BitsPerSample==32||f.SampleRate==96000&&f.BitsPerSample==24);Check(precision.Exclusive&&precision.Format.SampleRate==192000&&precision.Format.BitsPerSample==32,"Lossless wider container at original rate preferred to downsampling");
   var depth=AutomaticAudioOutput.Select(new WaveFormat(192000,24,2),mix,1,f=>f.SampleRate==192000&&f.BitsPerSample==16||f.SampleRate==96000&&f.BitsPerSample==24);Check(depth.Exclusive&&depth.Format.SampleRate==96000&&depth.Format.BitsPerSample==24,"Preserve 24-bit precision before choosing a larger kHz number with 16-bit truncation");
   var noSupport=AutomaticAudioOutput.Select(new WaveFormat(44100,16,2),mix,1,_=>false);Check(!noSupport.Exclusive&&ReferenceEquals(noSupport.Format,mix),"No supported native format falls back to exact Windows mix");
   var onlyHigher=AutomaticAudioOutput.Select(new WaveFormat(44100,16,2),mix,1,f=>f.SampleRate==192000);Check(!onlyHigher.Exclusive,"Artificial highest-kHz upsampling not selected");
   var surround=AutomaticAudioOutput.Select(new WaveFormat(96000,24,6),mix,1,_=>throw new Exception("Must not guess multichannel layouts"));Check(!surround.Exclusive,"Multichannel conversion uses Windows channel layout");
   var softwareVolume=AutomaticAudioOutput.Select(new WaveFormat(44100,16,2),mix,1,_=>throw new Exception("Must retain software volume"),false);Check(!softwareVolume.Exclusive&&ReferenceEquals(softwareVolume.Format,mix),"Software-only Windows master volume never bypassed automatically");
   var floating=WaveFormat.CreateIeeeFloatWaveFormat(96000,2);Check(ReferenceEquals(AutomaticAudioOutput.Select(floating,mix,1,f=>AudioFormats.SameSignalFormat(f,floating)).Format,floating),"Supported float bytes retain actual float header");
   foreach(uint code in new uint[]{0x88890008,0x8889000A,0x8889000E,0x88890016,0x80070057})Check(AutomaticAudioOutput.CanRetryShared(new COMException("test",unchecked((int)code))),"Known pre-start compatibility error permits one retry: "+code.ToString("X8"));
   Check(!AutomaticAudioOutput.CanRetryShared(new InvalidDataException())&&!AutomaticAudioOutput.CanRetryShared(new COMException("invalidated",unchecked((int)0x88890004))),"Corrupt PCM and removed endpoint never trigger format retry");
   Check(AutomaticAudioOutput.CanRetryShared(new ArgumentException()),"Windows E_INVALIDARG mapping also permits pre-start compatibility fallback");
   var attempts=new List<bool>();AutomaticAudioOutput.StartWithFallback(shared=>{attempts.Add(shared);if(!shared)throw new AutomaticOutputRetryException(new COMException());});Check(attempts.SequenceEqual(new[]{false,true}),"Output-open fallback bounded to one shared retry");
   attempts.Clear();bool propagated=false;try{AutomaticAudioOutput.StartWithFallback(shared=>{attempts.Add(shared);throw new AutomaticOutputRetryException(new COMException());});}catch(AutomaticOutputRetryException){propagated=true;}Check(propagated&&attempts.Count==2,"Repeated failures propagate without retry loop");
   attempts.Clear();propagated=false;try{AutomaticAudioOutput.StartWithFallback(shared=>{attempts.Add(shared);throw new InvalidDataException();});}catch(InvalidDataException){propagated=true;}Check(propagated&&attempts.Count==1,"Decoder failures do not retry or restart sound");
   string settings=Path.Combine(directory,"migration.json");Check(AudioPreferences.Load(settings).AutomaticOutput,"New installation defaults to automatic");File.WriteAllText(settings,"{\"Exclusive\":true,\"PcmRateLimits\":{\"optical\":96000},\"PreampDb\":-3}");var preferences=AudioPreferences.Load(settings);Check(preferences.AutomaticOutput&&preferences.Exclusive&&preferences.PcmRate("optical",192000)==96000&&preferences.PreampDb==-3,"Legacy settings migrate without losing manual choices");preferences.AutomaticOutput=false;preferences.Save(settings);Check(!AudioPreferences.Load(settings).AutomaticOutput,"Explicit manual choice survives restart");preferences.AutomaticOutput=true;var copy=preferences.Copy();copy.PcmRateLimits.Clear();Check(copy.AutomaticOutput&&preferences.PcmRateLimits.Count==1,"Playback snapshot carries independent automatic and manual settings");File.WriteAllText(settings,"null");Check(AudioPreferences.Load(settings).AutomaticOutput,"Null settings recover safely to automatic");
   using(var endpoints=new MMDeviceEnumerator())using(var selected=AutomaticAudioOutput.OpenDevice(endpoints,"{not-a-connected-device}",true))using(var expected=endpoints.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia))Check(selected.ID==expected.ID,"Foreign computer's missing saved endpoint resolves to current default in automatic mode");
   foreach(var (inputBits,outputBits,floatingInput) in new[]{(16,24,false),(16,32,false),(24,32,false),(32,24,false),(32,24,true)})
   {
    var format=floatingInput?WaveFormat.CreateIeeeFloatWaveFormat(44100,2):new WaveFormat(44100,inputBits,2);string path=Path.Combine(directory,$"offline-precision-{inputBits}-{outputBits}-{floatingInput}.wav");
    using(var writer=new WaveFileWriter(path,format))for(int i=0;i<44100;i++)for(int channel=0;channel<2;channel++){double value=channel==0?.125:-.0625;byte[] bytes=floatingInput?BitConverter.GetBytes((float)value):BitConverter.GetBytes((int)(value*Math.Pow(2,inputBits-1)));writer.Write(bytes,0,inputBits/8);}
    byte[] original=File.ReadAllBytes(path);using var reader=new WaveFileReader(path);using var converted=AudioEngine.CompatibilitySource(reader,new WaveFormat(44100,outputBits,2));using var result=new MemoryStream();byte[] block=new byte[converted.WaveFormat.BlockAlign*517];int n;while((n=ExactWasapiOut.ValidateRead(converted,block,block.Length))>0)result.Write(block,0,n);byte[] output=result.ToArray();int width=outputBits/8,frames=output.Length/(width*2);bool safe=true;
    for(int frame=256;frame<frames-256;frame++)for(int channel=0;channel<2;channel++){int p=(frame*2+channel)*width;int sample=width==2?BitConverter.ToInt16(output,p):width==3?(output[p]|output[p+1]<<8|output[p+2]<<16)<<8>>8:BitConverter.ToInt32(output,p);safe&=Math.Abs(sample/Math.Pow(2,outputBits-1)-(channel==0?.125:-.0625))<.002;}
    Check(safe&&Math.Abs(frames-44100)<256&&original.SequenceEqual(File.ReadAllBytes(path)),$"Offline PCM precision {inputBits}{(floatingInput?" float":" integer")} → {outputBits}: sign, amplitude, channels and duration verified");
   }
   foreach(var option in AudioEngine.Devices())
   {
    using var endpoints=new MMDeviceEnumerator();using var device=endpoints.GetDevice(option.Id);using var client=device.AudioClient;
    if(device.AudioMeterInformation.MasterPeakValue>.001){hardware.Add(new{option.Name,skipped="Endpoint already audible; not interrupted"});continue;}
    float before=device.AudioEndpointVolume.MasterVolumeLevelScalar;bool muted=device.AudioEndpointVolume.Mute;
    foreach(var (rate,bits) in new[]{(44100,16),(192000,24)})
    {
     var format=new WaveFormat(rate,bits,2);string path=Path.Combine(directory,$"zero-{Paths.Hash(option.Id)[..8]}-{rate}.wav");using(var writer=new WaveFileWriter(path,format)){byte[] zero=new byte[format.AverageBytesPerSecond];for(int i=0;i<6;i++)writer.Write(zero,0,zero.Length);}
     var track=await MetadataReader.Read(path,default,directory);var order=new PlaybackOrder();order.Reset(1,0);using var engine=new AudioEngine{Volume=.35};string? error=null;engine.Failed+=text=>error=text;
     engine.PlayStreaming(new(track,0,GainDecision.Off,order.Capture(),TimeSpan.Zero),new([track],0,order,_=>GainDecision.Off,false),option.Id,false,preferences:new(){AutomaticOutput=true});
     async Task Started(){for(int i=0;i<200&&!engine.DeviceStarted&&error==null;i++)await Task.Delay(15);if(error!=null)throw new Exception(error);}
     await Started();await Task.Delay(130);Check(engine.DeviceStarted&&engine.Position.TotalSeconds>0&&engine.Signal.Contains("Otomatik çıkış"),"Automatic zero PCM starts with advancing hardware clock: "+option.Name+"/"+rate);
     var decision=AutomaticAudioOutput.Select(format,client.MixFormat,AutomaticAudioOutput.FormFactor(device),f=>client.IsFormatSupported(AudioClientShareMode.Exclusive,f),AutomaticAudioOutput.HardwareVolume(device));bool fallback=engine.Signal.Contains("Exclusive açılamadı");Check(AudioFormats.SameSignalFormat(engine.OutputFormat!,fallback?client.MixFormat:decision.Format),"Actual endpoint header matches policy: "+option.Name+"/"+rate);
     engine.Toggle();double paused=engine.Position.TotalSeconds;await Task.Delay(80);Check(engine.IsPaused&&!engine.DeviceStarted&&Math.Abs(engine.Position.TotalSeconds-paused)<.02,"Automatic pause stops endpoint: "+option.Name+"/"+rate);engine.Toggle();await Started();
     engine.Position=TimeSpan.FromSeconds(2);await Started();Check(engine.DeviceStarted&&engine.Position.TotalSeconds>=1.9&&engine.Signal.Contains("Otomatik çıkış"),"Seek reselects format and retains automatic policy: "+option.Name+"/"+rate);
     hardware.Add(new{option.Name,source=format.ToString(),output=engine.OutputFormat!.ToString(),engine.Signal});engine.Stop();Check(!engine.DeviceStarted&&error==null&&engine.Volume==.35&&Math.Abs(device.AudioEndpointVolume.MasterVolumeLevelScalar-before)<1e-6&&device.AudioEndpointVolume.Mute==muted,"Stop releases output; application and system volume untouched: "+option.Name+"/"+rate);
    }
   }
   await SettingsUi(directory,Check);
  }
  finally{File.WriteAllText(Path.Combine(directory,"automatic-audio-results.json"),JsonSerializer.Serialize(new{version=typeof(AudioEngine).Assembly.GetName().Version?.ToString(),checks,hardware,note="Hardware received generated zero PCM only. Receiver audibility and remote hardware limits cannot be measured by software probes."},new JsonSerializerOptions{WriteIndented=true}));}
 }
 static async Task SettingsUi(string directory,Action<bool,string> check)
 {
  Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Themes.Load();var preferences=new AudioPreferences{AutomaticOutput=true,Exclusive=true};using(var endpoints=new MMDeviceEnumerator())using(var device=endpoints.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia))preferences.PcmRateLimits[device.ID]=96000;preferences.Save(Path.Combine(Paths.Data,"audio-settings.json"));
  var window=new MainWindow{Opacity=0,ShowInTaskbar=false};
  try
  {
   window.Show();for(int i=0;i<400&&!(bool)typeof(MainWindow).GetField("libraryReady",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;i++)await Task.Delay(10);
   SettingsSection Build()=>(SettingsSection)typeof(MainWindow).GetMethod("BuildAudioSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[window])!;
   var section=Build();var panel=(StackPanel)((ScrollViewer)section.View).Content;var auto=panel.Children.OfType<CheckBox>().Single(c=>AutomationProperties.GetName(c)=="Otomatik ses çıkışı");var exclusive=panel.Children.OfType<CheckBox>().Single(c=>Equals(c.Content,"Manuel · WASAPI Exclusive"));var limit=panel.Children.OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="PCM örnekleme sınırı");
   check(auto.IsChecked==true&&!exclusive.IsEnabled&&!limit.IsEnabled&&limit.SelectedIndex==1,"Settings open in automatic mode and preserve disabled manual ceiling");auto.IsChecked=false;check(exclusive.IsEnabled&&limit.IsEnabled&&exclusive.IsChecked==true,"Turning automatic off exposes preserved manual controls");section.Save();check(!AudioPreferences.Load(Path.Combine(Paths.Data,"audio-settings.json")).AutomaticOutput,"Settings manual choice persists");
   section=Build();panel=(StackPanel)((ScrollViewer)section.View).Content;auto=panel.Children.OfType<CheckBox>().Single(c=>AutomationProperties.GetName(c)=="Otomatik ses çıkışı");auto.IsChecked=true;section.Cancel();check(!AudioPreferences.Load(Path.Combine(Paths.Data,"audio-settings.json")).AutomaticOutput,"Uncommitted draft does not alter saved mode");section.Save();var saved=AudioPreferences.Load(Path.Combine(Paths.Data,"audio-settings.json"));check(saved.AutomaticOutput&&saved.Exclusive&&saved.PcmRateLimits.Values.Contains(96000),"Saving automatic retains manual mode and per-device limit");
  }
  finally{window.Close();}
 }
}
