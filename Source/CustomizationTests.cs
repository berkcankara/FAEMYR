using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using NAudio.Wave;
namespace Ahenk;
public static class CustomizationTests
{
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
 static void Set(MainWindow window,string name,object value)=>typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,value);
 static object? Call(MainWindow window,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args);
 static IEnumerable<DependencyObject> Nodes(DependencyObject node){yield return node;foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())foreach(var descendant in Nodes(child))yield return descendant;}
 static async Task Wait(Func<bool> condition){for(int i=0;i<600;i++){if(condition())return;await Task.Delay(10);}throw new Exception("Customization UI timeout");}
 public static async Task Run(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Themes.Load();MainWindow? window=null;
  try
  {
   var invalid=new InterfacePreferences{FontScale=double.NaN,SidebarWidth=999,FontFamily="file:///font",AlbumColumns=-4,VisualizerFps=500,RightPanelWidth=double.PositiveInfinity,HiddenNavigation=["settings","queue"],ColumnOrder=["duration","bad","duration"]};invalid.Validate();
   Check(invalid.FontScale==100&&invalid.SidebarWidth==260&&invalid.FontFamily=="Segoe UI"&&invalid.AlbumColumns==3&&invalid.VisualizerFps==120&&invalid.RightPanelWidth==null&&invalid.HiddenNavigation.SetEquals(["queue"])&&invalid.ColumnOrder.SequenceEqual(["duration"]),"Untrusted layout values are bounded and Settings remains accessible");
   var original=new InterfacePreferences{HiddenNavigation=["queue"],ColumnOrder=["duration"]};var copy=original.Copy();copy.HiddenNavigation.Clear();copy.ColumnOrder.Clear();Check(original.HiddenNavigation.Count==1&&original.ColumnOrder.Count==1,"Cancel snapshots do not share mutable layout collections");
   string malformed=Path.Combine(directory,"malformed.json");File.WriteAllText(malformed,"{");Check(InterfacePreferences.Load(malformed).AlbumColumns==6,"Corrupt layout file falls back safely");
   string future=Path.Combine(directory,"future.json");File.WriteAllText(future,"{\"Version\":99}");bool rejected=false;try{AppearanceProfile.Read(future);}catch(InvalidDataException){rejected=true;}Check(rejected,"Unsupported imported profile version is refused");
   Directory.CreateDirectory(AppearanceProfile.DirectoryPath);File.Copy(future,Path.Combine(AppearanceProfile.DirectoryPath,"future.json"));Check(AppearanceProfile.Stored().Count==0,"Unsupported saved profiles cannot break Settings");
   string source=Path.Combine(directory,"silent.wav");var format=new WaveFormat(44100,16,2);using(var writer=new WaveFileWriter(source,format))writer.Write(new byte[format.AverageBytesPerSecond],0,format.AverageBytesPerSecond);
   var fixture=await MetadataReader.Read(source,default);var rows=new List<Track>();for(int i=0;i<16;i++){var t=await MetadataReader.Read(source,default);t.Path=source+"#fixture"+i;t.SourcePath=source;t.Title="Track "+i;t.Artist=t.AlbumArtist="Artist "+i;t.Album="A very long album title that must remain inside its own header area "+i;rows.Add(t);}using(var db=new LibraryStore())db.SaveMany(rows);
   PlayerJson.Write(Path.Combine(Paths.Data,"player-settings.json"),new PlayerPreferences{Volume=0});new AudioPreferences{Exclusive=false}.Save(Path.Combine(Paths.Data,"audio-settings.json"));
   window=new MainWindow{Opacity=0,ShowInTaskbar=false,Width=1440,Height=900};window.Show();await Wait(()=>Field<bool>(window,"libraryReady")&&Field<bool>(window,"sessionReady"));
   var signalText=Field<TextBlock>(window,"Signal");var signalBox=Field<Border>(window,"SignalBox");string savedSignal=signalText.Text;bool FitsSignal(){var rect=signalText.TransformToAncestor(signalBox).TransformBounds(new Rect(signalText.RenderSize));return rect.Top>=signalBox.Padding.Top-.5&&rect.Bottom<=signalBox.ActualHeight-signalBox.Padding.Bottom+.5&&signalText.ActualHeight+signalText.Margin.Top>=signalText.DesiredSize.Height-.5;}
   foreach(double signalScale in new double[]{100,125})
   {
    var signalProfile=AppearanceProfile.Preset("Varsayılan");signalProfile.Interface.PlayerHeight=110;signalProfile.Interface.PlayerSideWidth=260;signalProfile.Interface.FontScale=signalScale;Call(window,"PreviewAppearanceProfile",signalProfile);signalText.Text="Kaynak: 16 bit / 44,1 kHz / 2 kanal → Soundbar (Realtek (R) Audio)\nWASAPI Paylaşımlı · 32 bit / Windows: 96 kHz\nReplayGain kapalı · özgün örnekler\nAkış decoder · uyumlu parçalarda kesintisiz\nSes %55 · dijital zayıflatma · özgün örnek değerleri değişir\nWavPack: Hybrid + correction · lossless";window.UpdateLayout();await Task.Delay(50);window.UpdateLayout();Check(FitsSignal()&&signalText.TextTrimming==TextTrimming.None,"Complete wrapped signal information fits at minimum player height and font scale "+signalScale);Check(Math.Abs(Field<Border>(window,"TransportBox").ActualHeight-signalBox.ActualHeight)<.5,"Left and right player frames retain matching heights with long signal text: "+signalScale);Call(window,"EnterFullscreenPlayer");window.UpdateLayout();await Task.Delay(50);window.UpdateLayout();Check(FitsSignal()&&Field<ContentControl>(window,"FullscreenControls").ActualHeight>=Field<Border>(window,"PlayerPanel").ActualHeight-.5,"Complete signal information fits the fullscreen player at font scale "+signalScale);Call(window,"ExitFullscreenPlayer");
   }
   signalText.Text=savedSignal;
   void CheckViewButtons(string label)
   {
    var parent=Field<Grid>(window,"ScopeAndSeek");Rect Bounds(FrameworkElement element)=>element.TransformToAncestor(parent).TransformBounds(new Rect(element.RenderSize));
    var full=Bounds(Field<Button>(window,"FullscreenButton"));var mini=Bounds(Field<Button>(window,"MiniPlayerButton"));var scopeBounds=Bounds(Field<OscilloscopeView>(window,"Scope"));var total=Field<TextBlock>(window,"TotalTime");var time=Bounds(total);
    Check(Math.Abs((full.Left+full.Right-mini.Left-mini.Right)/2)<.5&&full.Bottom<=mini.Top&&mini.Bottom<=parent.RowDefinitions[0].ActualHeight&&full.Top>=0&&!full.IntersectsWith(scopeBounds)&&!mini.IntersectsWith(scopeBounds)&&(total.Visibility!=Visibility.Visible||Math.Abs((mini.Left+mini.Right-time.Left-time.Right)/2)<.5),"Fullscreen and mini buttons stack above duration, share its center and remain outside the visualizer: "+label);
   }
   foreach(string preset in new[]{"Varsayılan","Kompakt","Ferah","Odak","Stüdyo"})
   {
    Call(window,"PreviewAppearanceProfile",AppearanceProfile.Preset(preset));window.UpdateLayout();var p=InterfaceSettings.Current;
    Check(Field<ListBox>(window,"Albums").Items.Cast<AlbumCard[]>().All(chunk=>chunk.Length<=p.AlbumColumns)&&Field<Grid>(window,"WindowRoot").ColumnDefinitions[0].ActualWidth==p.SidebarWidth&&Field<Border>(window,"DetailPanel").Visibility==(p.ShowRightPanel?Visibility.Visible:Visibility.Collapsed),preset+" applies gallery density and panel layout in the actual window");
    CheckViewButtons(preset);
   }
   foreach(var (visual,times) in new[]{(true,false),(false,true),(false,false)}){var profile=AppearanceProfile.Preset("Varsayılan");profile.Interface.ShowVisualizer=visual;profile.Interface.ShowTimes=times;profile.Interface.FontScale=125;profile.Interface.PlayerHeight=110;Call(window,"PreviewAppearanceProfile",profile);window.Width=1120;window.UpdateLayout();CheckViewButtons("compact with visualizer="+visual+", times="+times);}
   var custom=AppearanceProfile.Preset("Varsayılan");custom.Name="My / profile";custom.Interface.AlbumColumns=3;custom.Interface.ArtistColumns=8;custom.Interface.FontScale=120;custom.Interface.CardRadius=29;custom.Interface.ShowRatings=false;custom.Interface.PlaySize=64;custom.Interface.PlayerHeight=110;custom.Interface.RightPanelWidth=400;custom.Interface.ColumnOrder=["duration","title"];custom.Appearance.Colors["Canvas"]="#123456";custom.Appearance.Colors["LyricsAccent"]="#EF9876";custom.Appearance.Accent="#ABCDEF";
   Call(window,"PreviewAppearanceProfile",custom);window.UpdateLayout();
   Check(((SolidColorBrush)window.FindResource("Canvas")).Color==Color.FromRgb(18,52,86)&&((SolidColorBrush)window.FindResource("LyricsAccent")).Color==Color.FromRgb(239,152,118),"Independent background and lyrics colors reach live resources");
   Check((CornerRadius)window.FindResource("UiCardRadius")==new CornerRadius(29)&&(Visibility)window.FindResource("UiShowRatings")==Visibility.Collapsed&&Math.Abs(Field<Button>(window,"PlayButton").ActualHeight-64)<.1,"Live card shape, rating visibility and play button sizing apply");
   Check(Field<Grid>(window,"TransportControlsHost").RowDefinitions[0].ActualHeight>=68&&Field<Border>(window,"PlayerPanel").ActualHeight>=132,"Large play button is never clipped by the smallest player height");
   Check(Field<DataGridTextColumn>(window,"DurationColumn").DisplayIndex==0&&Field<DataGridTemplateColumn>(window,"TrackTitleColumn").DisplayIndex==1,"Custom column ordering reaches actual list headers");
   custom.Save(custom.StoredPath);var imported=AppearanceProfile.Read(custom.StoredPath);Check(imported.Name==custom.Name&&imported.Interface.AlbumColumns==3&&imported.Appearance.Colors["Canvas"]=="#123456"&&Path.GetDirectoryName(custom.StoredPath)==AppearanceProfile.DirectoryPath,"Named profile round-trip uses a safe local filename");
   Call(window,"PreviewAppearanceProfile",imported);Set(window,"view","artists");Call(window,"RefreshView");Check(Field<ListBox>(window,"Artists").Items.Cast<ArtistCard[]>().All(chunk=>chunk.Length<=8),"Artist grouping follows customized density");
   Set(window,"view","album");Set(window,"albumKey",rows[0].AlbumKey);Call(window,"RefreshView");window.Width=1120;window.Height=720;window.UpdateLayout();await Task.Delay(100);window.UpdateLayout();
   var text=Field<FrameworkElement>(window,"AlbumName");var tools=Field<FrameworkElement>(window,"AlbumQualityBadge");var titleRect=text.TransformToAncestor(window).TransformBounds(new Rect(text.RenderSize));var toolsRect=tools.TransformToAncestor(window).TransformBounds(new Rect(tools.RenderSize));Check(text.ActualWidth>0&&titleRect.Right<=window.ActualWidth&&(!titleRect.IntersectsWith(toolsRect)),"Long album title and actions remain separate at the minimum window size");
   InterfaceSettings.Current.Save(InterfaceSettings.Path);Themes.Appearance.Save(Themes.AppearancePath);Themes.Save();string before=File.ReadAllText(InterfaceSettings.Path),beforeColors=File.ReadAllText(Themes.AppearancePath);string visualPath=Path.Combine(Paths.Data,"visualizer.json");string? beforeMode=File.Exists(visualPath)?File.ReadAllText(visualPath):null;var prior=InterfaceSettings.Current.Copy();
   async Task Dialog(bool save)
   {
    Exception? failure=null;var completion=new TaskCompletionSource();_ = window.Dispatcher.BeginInvoke(async ()=>{Window? dialog=null;try{dialog=Application.Current.Windows.OfType<Window>().Single(w=>w.Title=="Lyra — Ayarlar");var slider=Nodes(dialog).OfType<Slider>().Single(s=>AutomationProperties.GetName(s)=="Sol menü genişliği");slider.Value=230;await Task.Delay(150);window.UpdateLayout();Check(InterfaceSettings.Current.SidebarWidth==230&&Field<Grid>(window,"WindowRoot").ColumnDefinitions[0].ActualWidth==230,"Layout slider previews immediately "+(save?"before save":"before cancel"));Call(window,"PreviewAppearanceProfile",AppearanceProfile.Preset("Stüdyo"));if(save)Nodes(dialog).OfType<Button>().Single(b=>Equals(b.Content,"Kaydet")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));else dialog.Close();}catch(Exception ex){failure=ex;dialog?.Close();}finally{completion.SetResult();}});Call(window,"OpenSettings","layout");await completion.Task;if(failure!=null)throw failure;
   }
   await Dialog(false);Check(InterfaceSettings.Current.SidebarWidth==prior.SidebarWidth&&InterfaceSettings.Current.AlbumColumns==prior.AlbumColumns&&Themes.Appearance.Colors["Canvas"]=="#123456"&&File.ReadAllText(InterfaceSettings.Path)==before&&File.ReadAllText(Themes.AppearancePath)==beforeColors&&(File.Exists(visualPath)?File.ReadAllText(visualPath):null)==beforeMode,"Cancel restores layout, colors and visualizer without persisting the preview");
   await Dialog(true);Check(InterfacePreferences.Load(InterfaceSettings.Path).PlayerHeight==180&&InterfacePreferences.Load(InterfaceSettings.Path).VisualizerMode==VisualizerMode.Spectrum,"Save commits the final preview profile and visualizer selection");
   window.Close();window=null;window=new MainWindow{Opacity=0,ShowInTaskbar=false};window.Show();await Wait(()=>Field<bool>(window,"sessionReady"));window.UpdateLayout();Check(InterfaceSettings.Current.PlayerHeight==180&&Field<OscilloscopeView>(window,"Scope").Mode==VisualizerMode.Spectrum&&Field<RowDefinition>(window,"PlayerRow").ActualHeight==180,"Customized layout and spectrum selection survive restart");Check(!Field<AudioEngine>(window,"audio").DeviceStarted,"Customization and restart never start audio output");
   var scope=Field<OscilloscopeView>(window,"Scope");((MenuItem)scope.ContextMenu.Items[3]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(scope.Mode==VisualizerMode.Spectrogram&&VisualizerPreferences.Load(visualPath).Mode==VisualizerMode.Spectrogram&&InterfacePreferences.Load(InterfaceSettings.Path).VisualizerMode==VisualizerMode.Spectrogram,"Real player right-click menu selects and persists spectrogram in both preference files");
   var playerSettings=(SettingsSection)Call(window,"BuildInterfaceSettings","player")!;var modes=Nodes(playerSettings.View).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="Visualizer türü");Check(modes.Items.Count==4&&Equals(modes.Items[3],"Spektrogram")&&modes.SelectedIndex==3,"Player settings offer spectrogram and reflect the current right-click selection");playerSettings.Cancel();
   window.Close();window=null;window=new MainWindow{Opacity=0,ShowInTaskbar=false};window.Show();await Wait(()=>Field<bool>(window,"sessionReady"));Check(Field<OscilloscopeView>(window,"Scope").Mode==VisualizerMode.Spectrogram&&!Field<AudioEngine>(window,"audio").DeviceStarted,"Spectrogram selection survives a real application restart without starting an audio endpoint");
  }
  finally{window?.Close();File.WriteAllText(Path.Combine(directory,"customization-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
