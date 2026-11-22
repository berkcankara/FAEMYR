using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NAudio.Wave;
namespace Faemyr;
public static class LocalizationTests
{
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
 static object? Call(MainWindow window,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args);
 static IEnumerable<DependencyObject> Nodes(DependencyObject node){yield return node;foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())foreach(var item in Nodes(child))yield return item;}
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<object>();void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(!ok)throw new Exception(name);}
  MainWindow? window=null;Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  try
  {
   L.Apply("tr");Check(L.T("Albümler")=="Albümler","Turkish remains unchanged");L.Save("en");Check(L.ReadLanguage()=="en","Language persists independently of audio and appearance preferences");L.Load();Check(L.T("Albümler")=="Albums"&&L.T("Kaydet")=="Save","English catalog loads");
   string metadata="Ayarlar · Sanatçı";Check(L.T($"Çalıyor · {metadata}")=="Playing · "+metadata,"Interpolated metadata is preserved verbatim");
   Check(new AlbumCard{Artist="Sanatçı",Count=2}.Detail=="Sanatçı · 2 tracks","Artist metadata is never translated");
   Check(new Track{Codec="flac",Bits=24,SampleRate=96000,Channels=2}.Quality.Contains("2 channels"),"Track quality and channel labels localized");var signal=new SignalDisplayPreferences();Check(signal.Format("Kaynak: 24 bit / 96 kHz / 2 kanal → Sanatçı Audio\nWASAPI Paylaşımlı · 32 bit / Windows: 96 kHz\nReplayGain kapalı").Contains("Source: 24 bit / 96 kHz / 2 channels → Sanatçı Audio"),"Signal presentation translates labels while preserving endpoint names");
   Check(new AlbumSortChoice(AlbumSortMode.Artist,"Sanatçı · A–Z").Label=="Artist · A–Z","Sorting labels localized independently of enum values");
   var panel=new PanelPreferences{HiddenFields=["Sanatçı"]};Check(!panel.IsVisible("Sanatçı")&&new MetadataField("Sanatçı","Albümler").Label=="Artist","Panel keys stay stable across languages");
   Themes.Appearance=new(){Accent="#A89BFF",ListOpacity=10,Colors=new(){{"Canvas","#224466"},{"PlayerSurface","#445566"}}};Themes.Apply("oled");
   foreach(string key in new[]{"Canvas","Surface","PanelSurface","SidebarSurface","PlayerSurface","CaptionSurface","ControlSurface","HeaderSurface","LyricsSurface","UpNextSurface"})Check(((SolidColorBrush)Application.Current.Resources[key]).Color==Colors.Black,"OLED surface is opaque #000000: "+key);
   var glass=((SolidColorBrush)Application.Current.Resources["GlassSurface"]).Color;Check(glass.R==0&&glass.G==0&&glass.B==0&&glass.A==(byte)Math.Round(Themes.Appearance.ListOpacity*255/100)&&(double)Application.Current.Resources["ListBlurRadius"]==Themes.Appearance.BlurRadius,"OLED preserves configured glass opacity and blur");Check(((SolidColorBrush)Application.Current.Resources["Accent"]).Color==(Color)ColorConverter.ConvertFromString("#A89BFF"),"OLED retains accent customization");Themes.Appearance.Save(Themes.AppearancePath);Themes.Save();Themes.Load();Check(Themes.Current=="oled","OLED theme persists");Themes.Apply("dark");Check(((SolidColorBrush)Application.Current.Resources["Canvas"]).Color==(Color)ColorConverter.ConvertFromString("#224466"),"Switching away restores original custom colors");Themes.Apply("oled");
   string path=Path.Combine(directory,"silent.wav");var format=new WaveFormat(44100,16,2);using(var writer=new WaveFileWriter(path,format))writer.Write(new byte[format.AverageBytesPerSecond],0,format.AverageBytesPerSecond);var track=await MetadataReader.Read(path,default);track.Title="Ayarlar";track.Artist=track.AlbumArtist="Sanatçı";track.Album="Renkler";string cover=Path.Combine(directory,"cover.png");var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(1,1,96,96,PixelFormats.Rgb24,null,new byte[]{180,40,210},3)));using(var coverFile=File.Create(cover))encoder.Save(coverFile);track.Cover=cover;using(var store=new LibraryStore())store.Save(track);
   window=new MainWindow{Opacity=0,ShowInTaskbar=false};window.Show();for(int i=0;i<600&&(!Field<bool>(window,"libraryReady")||!Field<bool>(window,"sessionReady"));i++)await Task.Delay(10);
   window.UpdateLayout();Check(Nodes(Field<Button>(window,"NavAlbums")).OfType<TextBlock>().Any(t=>t.Text=="Albums"),"XAML navigation uses English resources");Check(Field<TextBlock>(window,"PageTitle").Text=="Your music library","Dynamic collection heading translated");Check(Field<TextBox>(window,"Search").Tag?.ToString()=="Which album are you looking for?","Search placeholder translated");
   typeof(MainWindow).GetField("albumKey",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,track.AlbumKey);typeof(MainWindow).GetField("view",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,"album");Call(window,"RefreshView");window.UpdateLayout();Check(Field<FittingTextBlock>(window,"AlbumName").Text=="Renkler","Album title is unchanged");Check(Field<Border>(window,"PaletteGradient").Background is LinearGradientBrush&&Math.Abs(Field<System.Windows.Controls.Image>(window,"AmbientCover").Opacity-InterfaceSettings.Current.ArtworkOpacity/100)<.001&&Field<System.Windows.Controls.Image>(window,"AmbientCover").Effect is System.Windows.Media.Effects.BlurEffect blur&&blur.Radius==InterfaceSettings.Current.ArtworkBlur,"OLED album retains artwork, blur and configured opacity");
   Call(window,"ShowTrackDetails",track);Check(Field<TextBlock>(window,"DetailTitle").Text=="Ayarlar","Selected track title is unchanged");
   Call(window,"RenderArtist",new List<Track>{track});Check(Field<System.Windows.Controls.Image>(window,"ArtistBackdrop").Opacity==.45,"OLED artist retains blurred backdrop");Call(window,"EnterFullscreenPlayer");window.UpdateLayout();Check(Field<Border>(window,"FullscreenColorBackdrop").Background is LinearGradientBrush,"OLED fullscreen retains cover-color background");Call(window,"ExitFullscreenPlayer");var owner=new Window();var section=(SettingsSection)Call(window,"BuildAppearanceSettings",owner,(Action<bool>)(_=>{}))!;Check(Nodes(section.View).OfType<TextBlock>().Any(t=>t.Text=="Colors and appearance")&&Nodes(section.View).OfType<ComboBox>().Any(c=>c.Items.OfType<ThemeChoice>().Any(t=>t.Id=="oled"&&t.Name=="OLED · Pure black")),"English appearance settings expose OLED and language controls");
   Check(!Field<AudioEngine>(window,"audio").DeviceStarted,"Localization and OLED verification never opens audio output");L.Save("tr");L.Load();Check(L.T("Ayarlar")=="Ayarlar","Language can return to Turkish");
  }
  finally{window?.Close();L.Apply("tr");File.WriteAllText(Path.Combine(directory,"localization-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
