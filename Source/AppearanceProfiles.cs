using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace Ahenk;

public sealed class AppearanceProfile
{
 public int Version{get;set;}=1;
 public string Name{get;set;}="Görünümüm";
 public string Theme{get;set;}="dark";
 public AppearancePreferences Appearance{get;set;}=new();
 public InterfacePreferences Interface{get;set;}=new();
 public void Validate(){if(Version!=1)throw new InvalidDataException("Bu görünüm profilinin sürümü desteklenmiyor.");Name=(Name??"").Trim();if(Name.Length==0)Name="Görünümüm";if(Name.Length>80)Name=Name[..80];if(!Themes.Choices.Any(t=>t.Id==Theme))Theme="dark";Appearance??=new();Interface??=new();Appearance.Validate();Interface.Validate();}
 public static AppearanceProfile Read(string path){if(new FileInfo(path).Length>131072)throw new InvalidDataException("Görünüm profili 128 KB sınırını aşıyor.");var profile=JsonSerializer.Deserialize<AppearanceProfile>(File.ReadAllText(path))??throw new InvalidDataException("Görünüm profili boş.");profile.Validate();return profile;}
 public void Save(string path){Validate();PlayerJson.Write(path,this);}
 public static string DirectoryPath=>System.IO.Path.Combine(Paths.Data,"appearance-profiles");
 public string StoredPath=>System.IO.Path.Combine(DirectoryPath,Paths.Hash(Name)+".json");
 public static List<AppearanceProfile> Stored()
 {
  var profiles=new List<AppearanceProfile>();if(!Directory.Exists(DirectoryPath))return profiles;
  foreach(string file in Directory.EnumerateFiles(DirectoryPath,"*.json").Take(200))try{profiles.Add(Read(file));}catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException){}
  return profiles.OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
 }
 public static AppearanceProfile Preset(string name)=>new(){Name=name,Interface=name switch{
  "Kompakt"=>new(){AlbumColumns=7,ArtistColumns=7,PlaylistColumns=5,RowHeight=40,CardGap=4,FontScale=95,CardRadius=8,PlayerHeight=110,SidebarWidth=180},
  "Ferah"=>new(){AlbumColumns=4,ArtistColumns=4,PlaylistColumns=3,RowHeight=66,CardGap=12,FontScale=110,CardRadius=18,PlayerHeight=150,RightPanelWidth=400},
  "Odak"=>new(){ShowRightPanel=false,ShowLibraryStats=false,ShowCardDetails=false,ShowRatings=false,ShowTrackFavorites=false,ShowBitrate=false,ShowWaveforms=false,ShowSignal=false,PlayerFrames=false,HiddenNavigation=["artists","playlists","queue"],CardRadius=4},
  "Stüdyo"=>new(){PlayerHeight=180,VisualizerMode=VisualizerMode.Spectrum,TechnicalColumnsEverywhere=true,RowHeight=62,WaveformMaximum=220,TraceGlow=5,RightPanelWidth=380},
  _=>new(){VisualizerMode=VisualizerMode.Oscilloscope}
 }};
}
public partial class MainWindow
{
 AppearanceProfile CaptureAppearanceProfile(string name)
 {
  var p=InterfaceSettings.Current.Copy();p.VisualizerMode??=Scope.Mode;if(p.ShowRightPanel)p.RightPanelWidth=DetailsColumn.ActualWidth>0?DetailsColumn.ActualWidth:panelPreferences.Width;
  return new(){Name=name,Theme=Themes.Current,Appearance=Themes.Appearance.Copy(),Interface=p};
 }
 void PreviewAppearanceProfile(AppearanceProfile profile)
 {
  profile.Validate();Themes.Appearance=profile.Appearance.Copy();Themes.Apply(profile.Theme);InterfaceSettings.Current=profile.Interface.Copy();ApplyInterfacePreferences();renderedAlbum="";if(libraryReady)RefreshView();appearanceEditorRefresh?.Invoke();foreach(var read in interfaceEditorRefresh)read();NativeAppearance.Apply(this);if(settingsWindow!=null)NativeAppearance.Apply(settingsWindow);
 }
 FrameworkElement BuildProfileSettings(Window owner)
 {
  var content=new StackPanel{Margin=new Thickness(0,0,12,0)};var root=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
  void Text(string text,int size=13){var label=new TextBlock{Text=L.T(text),FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)};if(size<18)label.SetResourceReference(TextBlock.ForegroundProperty,"Muted");content.Children.Add(label);}
  Text("Görünüm profilleri",24);Text("Renklerini, yazılarını, düzenini ve visualizer görünümünü birlikte sakla. Bir profili yüklemek önizleme yapar; ana Kaydet düğmesi uygulamaya kaydeder. Ses ayarları ve müzik dosyaları profillere dahil değildir.");
  var presets=new WrapPanel{Margin=new Thickness(0,0,0,18)};content.Children.Add(presets);foreach(string title in new[]{"Varsayılan","Kompakt","Ferah","Odak","Stüdyo"}){var b=new Button{Content=L.T(title),Margin=new Thickness(0,0,8,8)};b.Click+=(_,_)=>PreviewAppearanceProfile(AppearanceProfile.Preset(title));presets.Children.Add(b);}
  Text("Kendi profillerim",18);var name=new TextBox{Text=L.T("Görünümüm"),MaxLength=80,Margin=new Thickness(0,0,0,10)};System.Windows.Automation.AutomationProperties.SetName(name,"Görünüm profili adı");content.Children.Add(name);
  var list=new ListBox{Height=150,DisplayMemberPath="Name",Margin=new Thickness(0,0,0,12)};list.SetResourceReference(Control.BackgroundProperty,"ControlSurface");list.SetResourceReference(Control.ForegroundProperty,"Ink");System.Windows.Automation.AutomationProperties.SetName(list,"Kayıtlı görünüm profilleri");content.Children.Add(list);void Reload()=>list.ItemsSource=AppearanceProfile.Stored();Reload();
  var actions=new WrapPanel{Margin=new Thickness(0,0,0,18)};var save=new Button{Content=L.T("Profili kaydet")};var load=new Button{Content=L.T("Seçili profili yükle")};actions.Children.Add(save);actions.Children.Add(load);content.Children.Add(actions);
  var files=new WrapPanel();var export=new Button{Content=L.T("Dışa aktar…")};var import=new Button{Content=L.T("İçe aktar…")};files.Children.Add(export);files.Children.Add(import);content.Children.Add(files);
  var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,18,0,0)};status.SetResourceReference(TextBlock.ForegroundProperty,"Accent");content.Children.Add(status);
  void Action(Action action){try{action();}catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException){status.Text=e.Message;}}
  save.Click+=(_,_)=>Action(()=>{if(string.IsNullOrWhiteSpace(name.Text)){status.Text=L.T("Profiline bir ad ver.");return;}var profile=CaptureAppearanceProfile(name.Text.Trim());profile.Save(profile.StoredPath);Reload();list.SelectedItem=list.Items.Cast<AppearanceProfile>().FirstOrDefault(p=>p.Name==profile.Name);status.Text=L.T("Görünüm profili kaydedildi.");});
  load.Click+=(_,_)=>Action(()=>{if(list.SelectedItem is not AppearanceProfile profile){status.Text=L.T("Önce bir profil seç.");return;}PreviewAppearanceProfile(profile);name.Text=profile.Name;status.Text=L.T("Profil önizleniyor. Ana Kaydet düğmesiyle uygula veya Vazgeç ile geri dön.");});
  export.Click+=(_,_)=>Action(()=>{var picker=new SaveFileDialog{Title=L.T("Görünüm profilini dışa aktar"),Filter=L.T("Lyra görünüm profili|*.lyra-theme.json"),FileName="Görünümüm.lyra-theme.json",DefaultExt=".lyra-theme.json",AddExtension=true};if(picker.ShowDialog(owner)!=true)return;CaptureAppearanceProfile(name.Text).Save(picker.FileName);status.Text=L.T("Görünüm profili dışa aktarıldı.");});
  import.Click+=(_,_)=>Action(()=>{var picker=new OpenFileDialog{Title=L.T("Görünüm profili seç"),Filter=L.T("Lyra görünüm profili|*.json")};if(picker.ShowDialog(owner)!=true)return;var profile=AppearanceProfile.Read(picker.FileName);PreviewAppearanceProfile(profile);name.Text=profile.Name;status.Text=L.T("Profil önizleniyor. İstersen Profili kaydet ile kişisel listene ekle.");});
  return root;
 }
}
