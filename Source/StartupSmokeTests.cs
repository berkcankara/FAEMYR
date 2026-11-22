using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Faemyr;
public static class StartupSmokeTests
{
 static T Field<T>(MainWindow main,string key)=>(T)typeof(MainWindow).GetField(key,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main)!;
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Themes.Load();
  var preferences=PlayerPreferences.Load(Path.Combine(Paths.Data,"player-settings.json"));preferences.AutoPlayOnStartup=false;PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),preferences);
  var main=new MainWindow{Opacity=0,ShowActivated=false,ShowInTaskbar=false};
  try
  {
   main.Show();for(int i=0;i<200&&(!Field<bool>(main,"libraryReady")||!Field<bool>(main,"sessionReady"));i++)await Task.Delay(50);
   var lyrics=Field<InnerAccentBorder>(main,"LyricsCard");double width=lyrics.ActualWidth;await Task.Delay(150);main.UpdateLayout();bool stable=Math.Abs(width-lyrics.ActualWidth)<.1;
   var result=new{LibraryReady=Field<bool>(main,"libraryReady"),SessionReady=Field<bool>(main,"sessionReady"),AudioDeviceStarted=Field<AudioEngine>(main,"audio").DeviceStarted,TrackCount=Field<List<Track>>(main,"library").Count,LyricsVisible=lyrics.IsVisible,LyricsPlainPresentation=lyrics.PlainPresentation,StableLyricsLayout=stable,main.IsVisible,main.ActualWidth,main.ActualHeight};
   File.WriteAllText(Path.Combine(directory,"startup-smoke.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
   var view=(FrameworkElement)main.Content;view.UpdateLayout();var bitmap=new RenderTargetBitmap((int)main.ActualWidth,(int)main.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(directory,"startup-preview.png")))png.Save(file);
   if(!result.LibraryReady||!result.SessionReady||result.AudioDeviceStarted||!stable)throw new Exception("Startup did not become ready with stable layout and no audio output.");
  }
  finally{main.Close();}
 }
}
