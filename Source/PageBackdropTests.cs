using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Faemyr;

public static class PageBackdropTests
{
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
 static void Set(MainWindow window,string name,object? value)=>typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,value);
 static void Call(MainWindow window,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args);
 static IEnumerable<DependencyObject> Nodes(DependencyObject node){yield return node;foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())foreach(var nested in Nodes(child))yield return nested;}
 static async Task Wait(Func<bool> ready){for(int i=0;i<400;i++){if(ready())return;await Task.Delay(15);}throw new Exception("Backdrop test timeout");}

 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  Environment.SetEnvironmentVariable("LYRA_ARTIST_PHOTOS_OFFLINE","1");Environment.SetEnvironmentVariable("LYRA_ARTIST_BIOGRAPHY_OFFLINE","1");
  var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}MainWindow? window=null;
  try
  {
   Track Make(string name,Color color)
   {
    string path=Path.Combine(directory,name+".flac"),cover=Path.Combine(directory,name+".png");File.WriteAllText(path,"Metadata fixture; never played.");
    byte[] pixels=Enumerable.Range(0,64*64).SelectMany(_=>new[]{color.B,color.G,color.R,(byte)255}).ToArray();var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(64,64,96,96,PixelFormats.Bgra32,null,pixels,64*4)));using(var file=File.Create(cover))png.Save(file);
    return new(){Path=path,Title=name,Album=name,Artist="Fixture Artist",AlbumArtist="Fixture Artist",Cover=cover,Codec="flac",SampleRate=44100,Bits=16,Channels=2,Seconds=60,Number=1,BitRate=800000,Modified=File.GetLastWriteTimeUtc(path).Ticks,Fields=[new("Vorbis / LYRICS","Fixture lyrics")]};
   }
   var red=Make("Red",Colors.Red);var blue=Make("Blue",Colors.Blue);using(var db=new LibraryStore())db.SaveMany([red,blue]);
   PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{AutoPlayOnStartup=false});
   PlayerJson.Write(Path.Combine(Paths.Data,"session.json"),new PlayerSession{Queue=[red.Path,blue.Path],QueueIndex=0,TrackPath=red.Path,View="albums"});
   new InterfacePreferences{AlbumArtworkBackground=true,ShowWaveforms=false,ShowBitrate=false,ArtworkBlur=55,ArtworkOpacity=24}.Save(InterfaceSettings.Path);Themes.Apply("oled");
   window=new MainWindow{Opacity=0,ShowActivated=false,ShowInTaskbar=false};window.Show();await Wait(()=>Field<bool>(window,"sessionReady"));
   var layer=Field<Grid>(window,"PaletteLayer");var previous=Field<Grid>(window,"PreviousPalette");var ambient=Field<Image>(window,"AmbientCover");var gradient=Field<Border>(window,"PaletteGradient");
   Color ColorAtStart()=>((LinearGradientBrush)gradient.Background).GradientStops[0].Color;
   bool Red()=>ColorAtStart().R>ColorAtStart().B*2;bool Blue()=>ColorAtStart().B>ColorAtStart().R*2;
   void Open(string page){Set(window,"view",page);Call(window,"RefreshView");window.UpdateLayout();}
   void Current(Track? track){Set(window,"now",track);if(track!=null)Call(window,"ShowTrackDetails",track);else Call(window,"RefreshView");}
   await Wait(()=>layer.Visibility==Visibility.Visible&&layer.Opacity>=.999);Check(Red()&&ambient.Source is BitmapImage&&Math.Abs(ambient.Opacity-.24)<.001,"Albums use restored playback artwork with the configured opacity");
   var brush=gradient.Background;Open("artists");Check(ReferenceEquals(brush,gradient.Background)&&layer.Visibility==Visibility.Visible,"Artists uses the same playback artwork without rebuilding the backdrop");
   Set(window,"albumKey",blue.AlbumKey);Open("album");Check(Blue(),"A browsed album takes priority over the playing album");
   if(SystemParameters.ClientAreaAnimation)Check(previous.Visibility==Visibility.Visible,"Album changes crossfade the outgoing artwork");
   Current(blue);Current(red);Check(Blue(),"Playback changes cannot replace the browsed album backdrop");
   Open("albums");Check(Red(),"Returning to albums restores the playback palette");await Wait(()=>previous.Visibility==Visibility.Collapsed);
   Current(blue);Check(Blue(),"Changing the playing track updates the gallery backdrop");await Wait(()=>previous.Visibility==Visibility.Collapsed);
   Open("tracks");if(SystemParameters.ClientAreaAnimation){Check(layer.Visibility==Visibility.Visible,"Outgoing color stays visible while fading");await Task.Delay(110);Check(layer.Opacity>0&&layer.Opacity<1,"Fade-out has intermediate opacity");}
   await Wait(()=>layer.Visibility==Visibility.Collapsed);foreach(string page in new[]{"favorites","queue","playlists","playlist","artist"}){Open(page);Check(layer.Visibility==Visibility.Collapsed,"No background artwork on "+page);}
   Open("artists");if(SystemParameters.ClientAreaAnimation){await Task.Delay(110);Check(layer.Opacity>0&&layer.Opacity<1,"Fade-in has intermediate opacity");}
   Open("queue");Open("albums");await Wait(()=>layer.Visibility==Visibility.Visible&&layer.Opacity>=.999);Check(Blue(),"Rapid navigation cannot let an old fade hide the new page");
   Current(null);await Wait(()=>layer.Visibility==Visibility.Collapsed);Check(layer.Opacity==0,"No current track leaves the gallery with the default theme");
   Current(red);await Wait(()=>layer.Visibility==Visibility.Visible&&layer.Opacity>=.999);InterfaceSettings.Current.AlbumArtworkBackground=false;Call(window,"ApplyInterfacePreferences",false);await Wait(()=>layer.Visibility==Visibility.Collapsed);Check(layer.Opacity==0,"Existing artwork preference disables the new background too");
   var modules=new[]{("SidebarBorder","SidebarSurface",191),("DetailPanel","PanelSurface",191),("TransportBox","Surface",191),("ScopeFrame","Surface",191),("SignalBox","Surface",191),("LyricsCard","LyricsSurface",71),("NextTrackCard","UpNextSurface",71)};
   bool OrdinarySurfaces()=>modules.All(m=>ReferenceEquals(Field<Border>(window,m.Item1).Background,Application.Current.Resources[m.Item2]));
   bool TranslucentSurfaces()=>modules.All(m=>{var module=Field<Border>(window,m.Item1);var c=((SolidColorBrush)module.Background).Color;var original=((SolidColorBrush)Application.Current.Resources[m.Item2]).Color;return c.A==m.Item3&&c.R==original.R&&c.G==original.G&&c.B==original.B&&module.Opacity==1;});
   Check(!Themes.Appearance.ModuleTransparency&&OrdinarySurfaces(),"Existing installations keep ordinary module surfaces by default");
   SettingsSection Appearance()=>(SettingsSection)typeof(MainWindow).GetMethod("BuildAppearanceSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[new Window(),(Action<bool>)(_=>{})])!;
   CheckBox Switch(SettingsSection section)=>Nodes(section.View).OfType<CheckBox>().Single(c=>Equals(c.Content,L.T("Modül saydamlığı")));
   var appearance=Appearance();var toggle=Switch(appearance);toggle.IsChecked=true;
   Check(Themes.Appearance.ModuleTransparency&&TranslucentSurfaces(),"The real appearance switch previews high-opacity tinted surfaces without reducing content opacity");
   foreach(var theme in Themes.Choices){Themes.Apply(theme.Id);Check(TranslucentSurfaces(),"Module translucency follows theme and keeps separate surface colors: "+theme.Id);}
   appearance.Cancel();Check(!Themes.Appearance.ModuleTransparency&&OrdinarySurfaces(),"Cancelling appearance restores the exact original surfaces");
   appearance=Appearance();toggle=Switch(appearance);toggle.IsChecked=true;appearance.Save();Check(AppearancePreferences.Load(Themes.AppearancePath).ModuleTransparency&&Themes.Appearance.Copy().ModuleTransparency,"The switch is included in saved appearance settings and profile copies");
   toggle.IsChecked=false;Check(OrdinarySurfaces(),"Turning the switch off restores the prior opaque theme resources");
   Check(!Field<AudioEngine>(window,"audio").DeviceStarted,"Backdrop checks never open an audio output device");
  }
  finally{window?.Close();File.WriteAllText(Path.Combine(directory,"page-backdrop-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
