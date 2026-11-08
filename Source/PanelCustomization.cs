using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ahenk;
public sealed class PanelPreferences
{
 public double Width {get;set;}=340;
 public bool ShowCover {get;set;}=true;
 public bool ShowTitle {get;set;}=true;
 public bool ShowArtist {get;set;}=true;
 public bool ShowLyrics {get;set;}=true;
 public bool ShowTechnicalButton {get;set;}=true;
 public bool ShowAudioOutput {get;set;}=true;
 public HashSet<string> HiddenFields {get;set;}=new(StringComparer.OrdinalIgnoreCase);
 public bool IsVisible(string key)=>!HiddenFields.Contains(key);
 public PanelPreferences Copy()=>new(){Width=Width,ShowCover=ShowCover,ShowTitle=ShowTitle,ShowArtist=ShowArtist,ShowLyrics=ShowLyrics,ShowTechnicalButton=ShowTechnicalButton,ShowAudioOutput=ShowAudioOutput,HiddenFields=new(HiddenFields,StringComparer.OrdinalIgnoreCase)};
 public static PanelPreferences Load(string path)
 {
  try{var p=JsonSerializer.Deserialize<PanelPreferences>(File.ReadAllText(path))??new();p.HiddenFields=new(p.HiddenFields??[],StringComparer.OrdinalIgnoreCase);p.Width=double.IsFinite(p.Width)?Math.Clamp(p.Width,280,640):340;return p;}
  catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){return new();}
 }
 public void Save(string path)
 {
  Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);string temp=path+".tmp";
  try{File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
}
public sealed class PanelFieldChoice : INotifyPropertyChanged
{
 public string Key {get;init;}="";public string Label=>L.T(Key);bool visible;
 public bool Visible {get=>visible;set{if(visible==value)return;visible=value;PropertyChanged?.Invoke(this,new(nameof(Visible)));}}
 public event PropertyChangedEventHandler? PropertyChanged;
}
public partial class MainWindow
{
 public static readonly DependencyProperty PanelValueSizeProperty=DependencyProperty.Register(nameof(PanelValueSize),typeof(double),typeof(MainWindow),new PropertyMetadata(12.0));
 public static readonly DependencyProperty PanelLabelSizeProperty=DependencyProperty.Register(nameof(PanelLabelSize),typeof(double),typeof(MainWindow),new PropertyMetadata(10.0));
 public double PanelValueSize{get=>(double)GetValue(PanelValueSizeProperty);set=>SetValue(PanelValueSizeProperty,value);}
 public double PanelLabelSize{get=>(double)GetValue(PanelLabelSizeProperty);set=>SetValue(PanelLabelSizeProperty,value);}
 PanelPreferences panelPreferences=new();Track? inspectedTrack;string panelCoverPath="";
 string PanelPreferencesPath=>System.IO.Path.Combine(Paths.Data,"panel-settings.json");
 static readonly string[] CorePanelFields=["Dosya","Sanatçı","Albüm","Biçim","Ortalama bitrate","Süre",L.T("WavPack — decoder raporu")];
 void InitializeDetailPanel()
 {
  panelPreferences=PanelPreferences.Load(PanelPreferencesPath);DetailsColumn.Width=new GridLength(panelPreferences.Width);
  var menu=new ContextMenu();var customize=new MenuItem{Header=L.T("Paneli özelleştir…")};customize.Click+=(_,_)=>CustomizePanel();menu.Items.Add(customize);DetailPanel.ContextMenu=menu;
  // Read-only text boxes normally own a separate context menu; use the same panel menu everywhere.
  DetailPanel.PreviewMouseRightButtonDown+=(_,e)=>e.Handled=true;
  DetailPanel.PreviewMouseRightButtonUp+=(_,e)=>{menu.PlacementTarget=DetailPanel;menu.IsOpen=true;e.Handled=true;};
  DetailPanel.SizeChanged+=(_,_)=>UpdatePanelScale();SizeChanged+=(_,_)=>UpdatePanelScale();
  DetailsSplitter.DragCompleted+=(_,_)=>{UpdatePanelScale();SavePanelPreferences();InterfaceSettings.Current.RightPanelWidth=panelPreferences.Width;if(settingsWindow==null)try{InterfaceSettings.Current.Save(InterfaceSettings.Path);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Status.Text=L.T("Panel genişliği kaydedilemedi: ")+ex.Message;}};
  Loaded+=(_,_)=>UpdatePanelScale();ApplyPanelVisibility();
 }
 void UpdatePanelScale()
 {
  double maximum=Math.Clamp(ActualWidth-InterfaceSettings.Current.SidebarWidth-500,280,640);DetailsColumn.MaxWidth=maximum;
  double width=DetailPanel.ActualWidth>0?DetailPanel.ActualWidth:panelPreferences.Width;
  double fontScale=InterfaceSettings.Current.FontScale/100*InterfaceSettings.Current.PanelTextScale/100;double scale=Math.Clamp((width-40)/300,.8,1.8);
  DetailTitle.FontSize=Math.Clamp(27*scale,23,40)*fontScale;DetailArtist.FontSize=Math.Clamp(14*Math.Sqrt(scale),13,19)*fontScale;
  PanelValueSize=Math.Clamp(12*Math.Sqrt(scale),11.5,16)*fontScale;PanelLabelSize=Math.Clamp(10.5*Math.Sqrt(scale),10,14)*fontScale;
 }
 void SavePanelPreferences()
 {
  if(InterfaceSettings.Current.ShowRightPanel)panelPreferences.Width=DetailsColumn.Width.IsAbsolute?Math.Clamp(DetailsColumn.Width.Value,280,DetailsColumn.MaxWidth):DetailsColumn.ActualWidth>0?Math.Clamp(DetailsColumn.ActualWidth,280,640):340;
  try{panelPreferences.Save(PanelPreferencesPath);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Status.Text=L.T("Panel ayarları kaydedilemedi: ")+ex.Message;}
 }
 static Visibility Visible(bool v)=>v?Visibility.Visible:Visibility.Collapsed;
 void ApplyPanelVisibility()
 {
  PanelCover.Visibility=Visible(panelPreferences.ShowCover);DetailTitle.Visibility=Visible(panelPreferences.ShowTitle);DetailArtist.Visibility=Visible(panelPreferences.ShowArtist);
  RawDetailsButton.Visibility=Visible(panelPreferences.ShowTechnicalButton);AudioPanel.Visibility=Visible(panelPreferences.ShowAudioOutput);
  UpdateLyricsVisibility();
  if(inspectedTrack!=null)Metadata.ItemsSource=DetailsFor(inspectedTrack).Where(f=>panelPreferences.IsVisible(f.Key)).ToList();
  UpdatePanelScale();
 }
 static List<MetadataField> DetailsFor(Track t)
 {
  var fields=new List<MetadataField>{new("Dosya",t.AudioPath),new("Sanatçı",t.Artist),new("Albüm",t.Album),new("Biçim",t.Quality),new("Ortalama bitrate",t.BitRateLabel+" · "+t.BitRateSource),new("Süre",t.Duration)};
  if(t.WavPackInfo.Length>0)fields.Add(new(L.T("WavPack — decoder raporu"),t.WavPackInfo));fields.AddRange(t.Fields);return fields;
 }
 void ShowTrackDetails(Track t)
 {
  bitrates?.Enqueue(t);
  bool changed=inspectedTrack?.Path!=t.Path;inspectedTrack=t;DetailTitle.Text=t.Title;DetailArtist.Names=t.Artist;DetailQuality.Text=t.Quality;
  string coverPath=File.Exists(t.Cover)?t.Cover:library.FirstOrDefault(row=>row.AlbumKey==t.AlbumKey&&File.Exists(row.Cover))?.Cover??"";
  if(panelCoverPath!=coverPath)
  {
   panelCoverPath=coverPath;PanelCover.Background=new SolidColorBrush(Color.FromRgb(32,32,32));CoverFallback.Visibility=Visibility.Visible;
   if(File.Exists(coverPath))try{var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=720;image.UriSource=new Uri(coverPath);image.EndInit();image.Freeze();PanelCover.Background=new ImageBrush(image){Stretch=Stretch.UniformToFill};CoverFallback.Visibility=Visibility.Collapsed;}
   catch(Exception ex)when(ex is IOException or NotSupportedException or ArgumentException or FormatException){panelCoverPath="";}
  }
  ApplyPanelVisibility();SelectLyricsTrack(t);if(fullscreenPlayer)UpdateFullscreenLayout();if(changed)DetailScroll.ScrollToTop();
 }
 void CustomizePanel()=>OpenSettings("panel");
 SettingsSection BuildPanelSettings(Window dialog)
 {
  var choices=CorePanelFields.Concat(library.SelectMany(t=>t.Fields.Select(f=>f.Key))).Concat(panelPreferences.HiddenFields).Distinct(StringComparer.OrdinalIgnoreCase)
   .OrderBy(key=>Array.IndexOf(CorePanelFields,key)<0?1:0).ThenBy(key=>key,StringComparer.CurrentCultureIgnoreCase)
   .Select(key=>new PanelFieldChoice{Key=key,Visible=panelPreferences.IsVisible(key)}).ToList();
  var original=panelPreferences.Copy();var originalWidth=DetailsColumn.Width;
  var layout=new Grid{Margin=new Thickness(0)};foreach(var h in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})layout.RowDefinitions.Add(new(){Height=h});
  var intro=new StackPanel();intro.Children.Add(new TextBlock{Text=L.T("Sağ panelinde neler görünsün?"),FontSize=23,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});intro.Children.Add(new TextBlock{Text=L.T("Seçimlerin tüm parçalar için saklanır. Dosyaların etiketleri değişmez."),Foreground=(Brush)FindResource("Muted"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,18)});layout.Children.Add(intro);
  var sections=new WrapPanel{Margin=new Thickness(0,0,0,16)};Grid.SetRow(sections,1);layout.Children.Add(sections);
  CheckBox Option(string text,bool enabled){var c=new CheckBox{Content=text,IsChecked=enabled,Foreground=(Brush)FindResource("Ink"),Margin=new Thickness(0,0,18,12)};sections.Children.Add(c);return c;}
  var cover=Option(L.T("Albüm kapağı"),panelPreferences.ShowCover);var title=Option(L.T("Parça adı"),panelPreferences.ShowTitle);var artist=Option(L.T("Sanatçı adı"),panelPreferences.ShowArtist);var lyricOption=Option(L.T("Şarkı sözleri"),panelPreferences.ShowLyrics);var technical=Option(L.T("Teknik veriler düğmesi"),panelPreferences.ShowTechnicalButton);var output=Option(L.T("Ses çıkışı kontrolleri"),panelPreferences.ShowAudioOutput);
  var filters=new StackPanel{Margin=new Thickness(0,0,0,12)};Grid.SetRow(filters,2);layout.Children.Add(filters);
  filters.Children.Add(new TextBlock{Text=L.T("BİLGİ ALANLARI"),Foreground=(Brush)FindResource("Accent"),FontSize=11,Margin=new Thickness(0,0,0,8)});
  var search=new TextBox{ToolTip=L.T("Bilgi alanı ara"),Margin=new Thickness(0,0,0,10)};System.Windows.Automation.AutomationProperties.SetName(search,L.T("Bilgi alanı ara"));filters.Children.Add(search);
  var tools=new StackPanel{Orientation=Orientation.Horizontal};filters.Children.Add(tools);var all=new Button{Content=L.T("Tümünü göster"),Padding=new Thickness(12,7,12,7)};var none=new Button{Content=L.T("Tümünü gizle"),Padding=new Thickness(12,7,12,7)};tools.Children.Add(all);tools.Children.Add(none);all.Click+=(_,_)=>choices.ForEach(c=>c.Visible=true);none.Click+=(_,_)=>choices.ForEach(c=>c.Visible=false);
  var fields=new ListBox{ItemsSource=choices,Background=Brushes.Transparent,BorderThickness=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch};Grid.SetRow(fields,3);ScrollViewer.SetHorizontalScrollBarVisibility(fields,ScrollBarVisibility.Disabled);VirtualizingPanel.SetScrollUnit(fields,ScrollUnit.Pixel);layout.Children.Add(fields);
  fields.ItemTemplate=(DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><CheckBox Content='{Binding Label}' IsChecked='{Binding Visible,Mode=TwoWay}' Foreground='{DynamicResource Ink}' Margin='8,7' FontSize='13'/></DataTemplate>");
  var rowStyle=new Style(typeof(ListBoxItem));rowStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));rowStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));var template=new ControlTemplate(typeof(ListBoxItem));template.VisualTree=new FrameworkElementFactory(typeof(ContentPresenter));rowStyle.Setters.Add(new Setter(Control.TemplateProperty,template));fields.ItemContainerStyle=rowStyle;
  var fieldView=CollectionViewSource.GetDefaultView(choices);search.TextChanged+=(_,_)=>{string query=search.Text.Trim();fieldView.Filter=item=>item is PanelFieldChoice c&&c.Label.Contains(query,StringComparison.CurrentCultureIgnoreCase);};
  var footer=new DockPanel{Margin=new Thickness(0,20,0,0)};Grid.SetRow(footer,4);layout.Children.Add(footer);var reset=new Button{Content=L.T("Varsayılanlar"),Padding=new Thickness(12,9,12,9)};DockPanel.SetDock(reset,Dock.Left);footer.Children.Add(reset);bool resetWidth=false;
  reset.Click+=(_,_)=>{cover.IsChecked=title.IsChecked=artist.IsChecked=lyricOption.IsChecked=technical.IsChecked=output.IsChecked=true;choices.ForEach(c=>c.Visible=true);resetWidth=true;};
  void Commit()
  {
   var updated=new PanelPreferences{Width=resetWidth?340:Math.Clamp(DetailsColumn.ActualWidth,280,640),ShowCover=cover.IsChecked==true,ShowTitle=title.IsChecked==true,ShowArtist=artist.IsChecked==true,ShowLyrics=lyricOption.IsChecked==true,ShowTechnicalButton=technical.IsChecked==true,ShowAudioOutput=output.IsChecked==true,HiddenFields=new(choices.Where(c=>!c.Visible).Select(c=>c.Key),StringComparer.OrdinalIgnoreCase)};
   updated.Save(PanelPreferencesPath);panelPreferences=updated;if(resetWidth)DetailsColumn.Width=new GridLength(340);ApplyPanelVisibility();
  }
  return new(layout,Commit,()=>{panelPreferences=original;DetailsColumn.Width=originalWidth;ApplyPanelVisibility();},()=>true);
 }
}

