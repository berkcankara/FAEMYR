using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NAudio.CoreAudioApi;

namespace Faemyr;
public partial class MainWindow
{
 AudioPreferences audioPreferences=new();ReplayGainStore gainStore=null!;CancellationTokenSource? gainScan;bool updatingAudioChoices;
 string AudioSettingsPath=>Path.Combine(Paths.Data,"audio-settings.json");
 void InitializeAudioFeatures()
 {
  audioPreferences=AudioPreferences.Load(AudioSettingsPath);gainStore=new(Path.Combine(Paths.Data,"replaygain"));
  updatingAudioChoices=true;
  if(audioPreferences.DeviceId is {} id){DeviceChoice.ItemsSource=new[]{new DeviceOption(null,L.T("Windows varsayılan aygıtı")),new DeviceOption(id,L.T("Kayıtlı ses aygıtı"))};DeviceChoice.SelectedIndex=1;}
  ModeChoice.SelectedIndex=audioPreferences.AutomaticOutput?2:audioPreferences.Exclusive?1:0;updatingAudioChoices=false;
  DeviceChoice.SelectionChanged+=(_,_)=>PersistAudioChoices();ModeChoice.SelectionChanged+=(_,_)=>PersistAudioChoices();
 }
 void PersistAudioChoices()
 {
  if(updatingAudioChoices)return;audioPreferences.DeviceId=(DeviceChoice.SelectedItem as DeviceOption)?.Id;audioPreferences.AutomaticOutput=ModeChoice.SelectedIndex==2;if(!audioPreferences.AutomaticOutput)audioPreferences.Exclusive=ModeChoice.SelectedIndex==1;
  try{audioPreferences.Save(AudioSettingsPath);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Status.Text=L.T("Ses ayarları kaydedilemedi: ")+ex.Message;}
 }
 List<Track> GainAlbum(Track track)=>library.Where(t=>t.AlbumKey==track.AlbumKey&&!t.IsDsd).ToList();
 SettingsSection BuildAudioSettings(Window owner)
 {
  var draft=audioPreferences.Copy();var stack=new StackPanel();var scroll=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  void Text(string text,bool heading=false){var label=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=heading?19:12,FontWeight=heading?FontWeights.SemiBold:FontWeights.Normal,Margin=new Thickness(0,heading?18:6,0,8)};label.SetResourceReference(TextBlock.ForegroundProperty,heading?"Ink":"Muted");stack.Children.Add(label);}
  Text(L.T("Başlangıç ve oturum"),true);var autoPlay=new CheckBox{Content=L.T("Açılışta son parçayı kaldığı yerden otomatik oynat"),IsChecked=playerPreferences.AutoPlayOnStartup,Margin=new Thickness(0,6,0,8)};autoPlay.SetResourceReference(Control.ForegroundProperty,"Ink");stack.Children.Add(autoPlay);
  Text(L.T("Son parça, konum, çalma kuyruğu ve açık sayfa otomatik hatırlanır. Bu seçenek kapalıyken Faemyr ses başlatmadan bekler. Ses seviyesi ve sessiz durumu da korunur."));
  var gapless=new CheckBox{Content=L.T("Uyumlu parçalar arasında kesintisiz geçiş"),IsChecked=playbackPreferences.Gapless,Margin=new Thickness(0,12,0,6)};gapless.SetResourceReference(Control.ForegroundProperty,"Ink");stack.Children.Add(gapless);Text(L.T("Sıradaki parça önceden hazırlanır. Örnekleme oranı, kanal sayısı ve işleme biçimi aynıysa çıkış açık tutularak birleştirilir. Biçim değişiyorsa aygıt özgün biçimde yeniden açılır. DoP ayrı çıkış yolunu kullanır. Değişiklik sonraki başlatmada uygulanır."));Text(L.T("Ses çıkışı"),true);Text(L.T("Değişiklikler sonraki parçada uygulanır. FLAC, ALAC, WAV ve AIFF özgün PCM çözünürlüğünde çözülür."));
  var devices=new ComboBox{DisplayMemberPath="Name",ItemsSource=DeviceChoice.ItemsSource,ToolTip=L.T("Ses çıkış aygıtı")};devices.SelectedItem=devices.Items.Cast<DeviceOption>().FirstOrDefault(d=>d.Id==draft.DeviceId)??devices.Items[0];stack.Children.Add(devices);
  var automatic=new CheckBox{Content=L.T("Otomatik çıkış · en iyi uyumlu ses biçimini seç"),IsChecked=draft.AutomaticOutput,Margin=new Thickness(0,12,0,6)};System.Windows.Automation.AutomationProperties.SetName(automatic,L.T("Otomatik ses çıkışı"));stack.Children.Add(automatic);
  Text(L.T("Destekleniyorsa kaynak biçimi korunur. Gerekirse aygıtın kabul ettiği PCM biçimi seçilir; daha yüksek kHz göstermek için ses yükseltilmez. Optik/uzak çıkışlarda Windows biçimi kullanılır. Exclusive açılamazsa uygun durumlarda paylaşımlı çıkış denenir. Uygulama ve cihaz ses yüksekliği değiştirilmez. Sinyal yolu kullanılan biçimi ve dönüşümü gösterir."));
  var exclusive=new CheckBox{Content=L.T("Manuel · WASAPI Exclusive"),IsChecked=draft.Exclusive,Margin=new Thickness(0,12,0,6)};stack.Children.Add(exclusive);
  Text(L.T("Otomatik seçimi kapatarak manuel ayarlarını kullanabilirsin. Exclusive kapalıysa paylaşımlı mod kullanılır. Aşağıdaki oran sınırı yalnızca manuel Exclusive modunda uygulanır. Manuel seçimler otomatik mod açıkken de saklanır."));
  Text(L.T("Manuel PCM uyumluluğu"),true);
  var rateLimit=new ComboBox{ItemsSource=new[]{L.T("Özgün oran · sınır yok"),L.T("En fazla 96 kHz · optik uyumluluk"),L.T("En fazla 48 kHz · optik uyumluluk")}};System.Windows.Automation.AutomationProperties.SetName(rateLimit,L.T("PCM örnekleme sınırı"));stack.Children.Add(rateLimit);
  string? defaultPcmDevice=null;try{using var endpoints=new MMDeviceEnumerator();using var endpoint=endpoints.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);defaultPcmDevice=endpoint.ID;}catch(System.Runtime.InteropServices.COMException){}
  string? PcmDevice()=>(devices.SelectedItem as DeviceOption)?.Id??defaultPcmDevice;
  bool loadingRate=false;
  void LoadRate(){loadingRate=true;try{var id=PcmDevice();exclusive.IsEnabled=automatic.IsChecked!=true;rateLimit.IsEnabled=id!=null&&automatic.IsChecked!=true;int limit=id!=null&&draft.PcmRateLimits.TryGetValue(id,out int value)?value:0;rateLimit.SelectedIndex=limit==96000?1:limit==48000?2:0;}finally{loadingRate=false;}}
  rateLimit.SelectionChanged+=(_,_)=>{if(!loadingRate&&PcmDevice() is {} id){int limit=rateLimit.SelectedIndex switch{1=>96000,2=>48000,_=>0};if(limit==0)draft.PcmRateLimits.Remove(id);else draft.PcmRateLimits[id]=limit;}};devices.SelectionChanged+=(_,_)=>LoadRate();LoadRate();
  automatic.Checked+=(_,_)=>LoadRate();automatic.Unchecked+=(_,_)=>LoadRate();
  Text(L.T("Yalnızca seçili aygıtta, Exclusive PCM çıkışında uygulanır. Sınırın üzerindeki dosyalar yüksek kaliteli yeniden örneklenir; bit-perfect olmaz. 16/44,1 gibi sınırın altındaki dosyalar özgün oranda kalır. Dosyalar, diğer aygıtlar ve DSD/DoP değişmez. Kaydet ve parçayı yeniden başlat."));
  Text(L.T("Optik çıkışta Windows ses çubuğu Exclusive sesini değiştirmeyebilir. Soundbar / DAC üzerindeki ses kontrolünü veya Faemyr'in sesini kullan. Faemyr sesini kısmak dijital örnekleri değiştirir; %100'de ses seviyesi işlemi atlanır."));
  Text("ReplayGain",true);var mode=new ComboBox{ItemsSource=new[]{L.T("Kapalı · özgün örnekler"),L.T("Parça kazancı"),L.T("Albüm kazancı")},SelectedIndex=(int)draft.ReplayGain};System.Windows.Automation.AutomationProperties.SetName(mode,L.T("ReplayGain modu"));stack.Children.Add(mode);
  Text(L.T("Önce dosyadaki ReplayGain etiketi, yoksa yerel analiz kullanılır. Sabit kazanç dinamik sıkıştırma uygulamaz; açıkken örnek değerleri değişir. Albüm modu parçalar arasındaki seviye farklarını korur."));
  var preamp=new TextBox{Text=draft.PreampDb.ToString("0.##",CultureInfo.CurrentCulture),ToolTip=L.T("Ön kazanç (dB), −12 ile +12"),Margin=new Thickness(0,4,0,8)};Text(L.T("Ön kazanç (dB) · −12 ile +12"));stack.Children.Add(preamp);
  var protect=new CheckBox{Content=L.T("Kırpılmayı önlemek için kazancı kayıtlı tepeye göre sınırla"),IsChecked=draft.PreventClipping,Margin=new Thickness(0,4,0,8)};stack.Children.Add(protect);
  Text(L.T("Etiket eksikse parçaya sağ tıkla → ReplayGain analizi. Dosyalar ve etiketleri değiştirilmez. Tepe bilgisi yoksa güvenli tam ölçek varsayılır."));
  Text("DSD / DoP",true);var dop=new CheckBox{Content=L.T("DSF / DFF için DoP 1.1 çıkışını etkinleştir"),IsChecked=draft.DopEnabled,Margin=new Thickness(0,4,0,8)};stack.Children.Add(dop);
  var confirmed=new CheckBox{Content=L.T("Seçtiğim DAC DoP destekliyor; bu aygıt için doğruladım"),Margin=new Thickness(0,4,0,8)};stack.Children.Add(confirmed);
  void DeviceChanged(){string? id=(devices.SelectedItem as DeviceOption)?.Id;confirmed.IsEnabled=id!=null;confirmed.IsChecked=id!=null&&draft.DopDevices.Contains(id);}
  bool syncing=false;devices.SelectionChanged+=(_,_)=>{syncing=true;DeviceChanged();syncing=false;};
  confirmed.Checked+=(_,_)=>{if(!syncing&&(devices.SelectedItem as DeviceOption)?.Id is {} id)draft.DopDevices.Add(id);};confirmed.Unchecked+=(_,_)=>{if(!syncing&&(devices.SelectedItem as DeviceOption)?.Id is {} id)draft.DopDevices.Remove(id);};DeviceChanged();
  Text(L.T("Yalnızca DoP uyumlu DAC seç. Uyumsuz aygıtta gürültü oluşabilir. Windows varsayılan aygıtına DoP gönderilmez. DoP daima Exclusive kullanır; ReplayGain ve görselleştirme devre dışıdır. Taşıyıcı oranı ve 24 bit desteği gerekir: DSD64 → 176,4 kHz; DSD128 → 352,8 kHz; DSD256 → 705,6 kHz."));
  Text(L.T("Sıkıştırılmamış mono/stereo DSF ve DFF desteklenir. DST sıkıştırılmış DFF, çok kanallı DSD ve ASIO Native DSD bu sürümde desteklenmez."));
  var validation=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};validation.SetResourceReference(TextBlock.ForegroundProperty,"Accent");stack.Children.Add(validation);
  bool Valid(){bool valid=double.TryParse(preamp.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out double value)&&double.IsFinite(value)&&value is >=-12 and <=12;validation.Text=valid?"":L.T("Ön kazancı −12 ile +12 dB arasında gir.");return valid;}
  void Save(){if(!Valid())return;draft.DeviceId=(devices.SelectedItem as DeviceOption)?.Id;draft.Exclusive=exclusive.IsChecked==true;draft.AutomaticOutput=automatic.IsChecked==true;draft.ReplayGain=(GainMode)mode.SelectedIndex;draft.PreampDb=double.Parse(preamp.Text,CultureInfo.CurrentCulture);draft.PreventClipping=protect.IsChecked==true;draft.DopEnabled=dop.IsChecked==true;draft.Save(AudioSettingsPath);playbackPreferences.Gapless=gapless.IsChecked==true;PlayerJson.Write(Path.Combine(Paths.Data,"playback-settings.json"),playbackPreferences);playerPreferences.AutoPlayOnStartup=autoPlay.IsChecked==true;PlayerJson.Write(PlayerSettingsPath,playerPreferences);audioPreferences=draft.Copy();updatingAudioChoices=true;try{DeviceChoice.SelectedItem=DeviceChoice.Items.Cast<DeviceOption>().FirstOrDefault(d=>d.Id==draft.DeviceId)??DeviceChoice.Items[0];ModeChoice.SelectedIndex=draft.AutomaticOutput?2:draft.Exclusive?1:0;}finally{updatingAudioChoices=false;}}
  foreach(var check in new[]{automatic,exclusive,protect,dop,confirmed})check.SetResourceReference(Control.ForegroundProperty,"Ink");
  return new(scroll,Save,()=>{},Valid);
 }
 void AddGainMenu(ContextMenu menu,Track track)
 {
  var parent=new MenuItem{Header=L.T("ReplayGain analizi"),IsEnabled=!track.IsDsd&&gainScan==null};menu.Items.Add(parent);
  var single=new MenuItem{Header=L.T("Parçayı yerel analiz et")};single.Click+=async(_,_)=>await AnalyzeGain([track],false);parent.Items.Add(single);
  var album=new MenuItem{Header=L.T("Albümü yerel analiz et")};album.Click+=async(_,_)=>await AnalyzeGain(GainAlbum(track),true);parent.Items.Add(album);
  var settings=new MenuItem{Header=L.T("ReplayGain / ses ayarları…")};settings.Click+=(_,_)=>OpenSettings("audio");parent.Items.Add(settings);
 }
 async Task AnalyzeGain(List<Track> tracks,bool album)
 {
  if(gainScan!=null)return;using var cancellation=new CancellationTokenSource();gainScan=cancellation;
  var label=new TextBlock{Text=L.T("Analiz hazırlanıyor…"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18)};var cancel=new Button{Content=L.T("İptal"),HorizontalAlignment=HorizontalAlignment.Right};var content=new StackPanel{Margin=new Thickness(24)};content.Children.Add(label);content.Children.Add(cancel);
  var dialog=new Window{Title=L.T("Faemyr — ReplayGain analizi"),Owner=this,Width=460,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=content};dialog.SetResourceReference(BackgroundProperty,"Canvas");dialog.SetResourceReference(ForegroundProperty,"Ink");
  cancel.Click+=(_,_)=>cancellation.Cancel();dialog.Closing+=(_,_)=>cancellation.Cancel();dialog.Show();
  try{await gainStore.AnalyzeAsync(tracks,album,new Progress<string>(text=>{if(!cancellation.IsCancellationRequested)label.Text=text;}),cancellation.Token);if(!closed)Status.Text=L.T("ReplayGain analizi kaydedildi. Dosyalar değiştirilmedi; sonraki oynatmada uygulanır.");}
  catch(OperationCanceledException){if(!closed)Status.Text=L.T("ReplayGain analizi iptal edildi.");}
  catch(Exception ex){if(!closed){Status.Text=ex.Message;MessageBox.Show(this,ex.Message,L.T("Faemyr — ReplayGain"),MessageBoxButton.OK,MessageBoxImage.Information);}}
  finally{gainScan=null;dialog.Close();}
 }
}
