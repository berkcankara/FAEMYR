using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
namespace Faemyr;
public static class ArtistPhotos
{
 static ArtistPhotoStore? store;
 public static ArtistPhotoStore Store=>store is not {Disposed:false}?store=new(System.IO.Path.Combine(Paths.Data,"artist-photos")):store;
 public static string PreferencesPath=>System.IO.Path.Combine(Paths.Data,"artist-photo-settings.json");
 public static ArtistPhotoPreferences Preferences{get;set;}=ArtistPhotoPreferences.Load(PreferencesPath);
 public static bool Allowed(string name)=>!Preferences.AlbumCoverArtists.Contains(ArtistPhotoStore.Identity(name));
 public static Task<ArtistPhoto?> Get(string name,bool priority=false)=>Preferences.Enabled?Store.Get(name,priority):Task.FromResult(Store.Cached(name));
 public static event Action<string>? Changed;
 public static void Notify(string name)=>Changed?.Invoke(name);
 public static void Close()=>store?.Dispose();
}
public sealed class ArtistPhotoView:ThumbnailImage
{
 public static readonly DependencyProperty ArtistProperty=DependencyProperty.Register(nameof(Artist),typeof(string),typeof(ArtistPhotoView),new PropertyMetadata("",Changed));
 public static readonly DependencyProperty FallbackProperty=DependencyProperty.Register(nameof(Fallback),typeof(string),typeof(ArtistPhotoView),new PropertyMetadata(null,Changed));
 public string Artist{get=>(string)GetValue(ArtistProperty);set=>SetValue(ArtistProperty,value);}
 public string? Fallback{get=>(string?)GetValue(FallbackProperty);set=>SetValue(FallbackProperty,value);}
 int request;
 public ArtistPhotoView(){Loaded+=(_,_)=>{ArtistPhotos.Changed+=RefreshName;Refresh();};Unloaded+=(_,_)=>{request++;ArtistPhotos.Changed-=RefreshName;};}
 static void Changed(DependencyObject node,DependencyPropertyChangedEventArgs args){var view=(ArtistPhotoView)node;if(view.IsLoaded)view.Refresh();}
 void RefreshName(string name){if(name.Length==0||ArtistPhotoStore.Identity(name).Equals(ArtistPhotoStore.Identity(Artist),StringComparison.OrdinalIgnoreCase))Refresh();}
 async void Refresh()
 {
  int generation=++request;string artist=Artist;Path=Fallback;ToolTip=null;
  if(!ArtistPhotos.Preferences.OnCards||!ArtistPhotos.Allowed(artist))return;
  try{var record=await ArtistPhotos.Get(artist);if(IsLoaded&&generation==request&&Artist==artist&&ArtistPhotos.Allowed(artist)){Path=ArtistPhotos.Store.ImagePath(record)??Fallback;ToolTip=record is {File.Length:>0}?record.Credit:null;}}catch(OperationCanceledException){}
 }
}
public partial class MainWindow
{
 int artistPhotoGeneration;
 void InitializeArtistPhotos()
 {
  ArtistPhotos.Changed+=ArtistPhotoChanged;ArtistHero.ContextMenuOpening+=(_,_)=>BuildArtistPhotoMenu(artistName,ArtistHero);ArtistHero.ContextMenu=new ContextMenu();Closed+=(_,_)=>{ArtistPhotos.Changed-=ArtistPhotoChanged;ArtistPhotos.Close();};
 }
 void ArtistPhotoChanged(string name)
 {
  if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(()=>ArtistPhotoChanged(name));return;}if(view=="artist"&&(name.Length==0||ArtistPhotoStore.Identity(name).Equals(artistName,StringComparison.OrdinalIgnoreCase)))RenderArtist(library.Where(t=>ArtistCatalog.Matches(t,artistName)));
 }
 async void LoadArtistPortrait(string name,string? fallback)
 {
  int generation=++artistPhotoGeneration;ArtistPortrait.Path=fallback;ArtistPortrait.ToolTip=null;ArtistPhotoCredit.Visibility=Visibility.Collapsed;ArtistPhotoCredit.Tag=null;
  SetArtistHeroPresentation(false);ArtistBackdrop.Effect=new BlurEffect{Radius=35};
  if(!ArtistPhotos.Allowed(name))return;
  try
  {
   var photo=await ArtistPhotos.Get(name,true);string? path=ArtistPhotos.Store.ImagePath(photo);if(closed||generation!=artistPhotoGeneration||view!="artist"||artistName!=name||!ArtistPhotos.Allowed(name)||path==null)return;
   var image=await ThumbnailCache.Load(path);if(closed||generation!=artistPhotoGeneration||view!="artist"||artistName!=name||image==null)return;
   bool rectangular=ArtistHeroArtwork.IsRectangular(image);
   if(rectangular)
   {
    image=await ArtistHeroArtwork.LoadOriginal(path);
    if(closed||generation!=artistPhotoGeneration||view!="artist"||artistName!=name||!ArtistPhotos.Allowed(name)||image==null)return;
   }
   SetArtistHeroPresentation(rectangular);ArtistPortrait.Path=rectangular?null:path;ArtistBackdrop.Source=image;ArtistPhotoCredit.Visibility=photo!.Local?Visibility.Collapsed:Visibility.Visible;ArtistPhotoCredit.Tag=photo.SourceUrl;ArtistPhotoCredit.ToolTip=photo.Credit;ArtistPortrait.ToolTip=photo.Credit;
  }catch(OperationCanceledException){}
 }
 void SetArtistHeroPresentation(bool rectangular)
 {
  artistHeroRectangular=rectangular;
  ArtistPortraitFrame.Visibility=rectangular?Visibility.Collapsed:Visibility.Visible;
  ArtistHeading.Margin=new Thickness(rectangular?30:198,26,30,26);
  ArtistHeading.VerticalAlignment=rectangular?VerticalAlignment.Bottom:VerticalAlignment.Center;
  ArtistKind.Visibility=ArtistSummary.Visibility=rectangular?Visibility.Collapsed:Visibility.Visible;
  ArtistBackdrop.Opacity=rectangular?1:.45;ArtistBackdrop.Effect=rectangular?null:new BlurEffect{Radius=12};
  UpdateArtistProfileLayout();
 }
 void ArtistPhotoCredit_Click(object sender,RoutedEventArgs e){if(ArtistPhotoCredit.Tag is string url)OpenPhotoSource(url);}
 static void OpenPhotoSource(string url){if(Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="commons.wikimedia.org")try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){}}
 void ArtistPhotoMenu_Opening(object sender,ContextMenuEventArgs e){if(sender is FrameworkElement target&&target.Tag is string name)BuildArtistPhotoMenu(name,target);}
 void BuildArtistPhotoMenu(string name,FrameworkElement target)
 {
  var menu=target.ContextMenu??=new ContextMenu();menu.Items.Clear();var refresh=new MenuItem{Header=L.T("Fotoğrafı internetten yeniden ara"),IsEnabled=ArtistPhotos.Preferences.Enabled};menu.Items.Add(refresh);
  refresh.Click+=async(_,_)=>{ArtistPhotos.Preferences.AlbumCoverArtists.Remove(ArtistPhotoStore.Identity(name));SaveArtistPhotoPreferences();try{var result=await ArtistPhotos.Store.Get(name,true,true);ArtistPhotos.Notify(name);Status.Text=ArtistPhotos.Store.ImagePath(result)!=null?L.T("Sanatçı fotoğrafı güncellendi."):L.T("Fotoğraf bulunamadı veya servise ulaşılamadı; albüm kapağı kullanılıyor.");}catch(OperationCanceledException){}};
  var source=new MenuItem{Header=L.T("Fotoğraf kaynağını aç"),IsEnabled=ArtistPhotos.Store.Cached(name) is {SourceUrl.Length:>0}};source.Click+=(_,_)=>{if(ArtistPhotos.Store.Cached(name) is {} record)OpenPhotoSource(record.SourceUrl);};menu.Items.Add(source);
  var local=new MenuItem{Header=L.T("Bilgisayarımdan fotoğraf seç…")};local.Click+=async(_,_)=>{var picker=new OpenFileDialog{Title=L.T("Sanatçı fotoğrafı seç"),Filter=L.T("Görseller|*.png;*.jpg;*.jpeg;*.webp;*.bmp")};if(picker.ShowDialog(this)!=true)return;try{if(new FileInfo(picker.FileName).Length>8388608)throw new IOException(L.T("Fotoğraf en fazla 8 MB olabilir."));await Task.Run(()=>ArtistPhotos.Store.Import(name,File.ReadAllBytes(picker.FileName)));ArtistPhotos.Preferences.AlbumCoverArtists.Remove(ArtistPhotoStore.Identity(name));SaveArtistPhotoPreferences();ArtistPhotos.Notify(name);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.IO.FileFormatException){MessageBox.Show(this,ex.Message,L.T("Faemyr — sanatçı fotoğrafı"));}};menu.Items.Add(local);
  var cover=new MenuItem{Header=L.T("Albüm kapağını kullan")};cover.Click+=(_,_)=>{ArtistPhotos.Preferences.AlbumCoverArtists.Add(ArtistPhotoStore.Identity(name));SaveArtistPhotoPreferences();ArtistPhotos.Notify(name);};menu.Items.Add(cover);
 }
 void SaveArtistPhotoPreferences(){try{PlayerJson.Write(ArtistPhotos.PreferencesPath,ArtistPhotos.Preferences);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Status.Text=ex.Message;}}
 SettingsSection BuildArtistPhotoSettings()
 {
  var root=new StackPanel();root.Children.Add(new TextBlock{Text=L.T("Sanatçı fotoğrafları"),FontSize=24,FontWeight=FontWeights.Bold});var hint=new TextBlock{Text=L.T("Fotoğraflar Wikipedia / Wikimedia Commons üzerinden, sanatçı adıyla aranır ve bilgisayarında saklanır. Yalnızca açtığın profil ve görünür sanatçı kartları için arama yapılır. Sanatçı adları bu servislere gönderilir; müzik dosyaların ve dosya yolların gönderilmez. Fotoğraf bulunamazsa albüm kapağı kullanılır."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,20)};hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");root.Children.Add(hint);
  var enabled=new CheckBox{Content=L.T("İnternetten sanatçı fotoğrafı bul"),IsChecked=ArtistPhotos.Preferences.Enabled,Margin=new Thickness(0,0,0,14)};var cards=new CheckBox{Content=L.T("Fotoğrafları sanatçı kartlarında da kullan"),IsChecked=ArtistPhotos.Preferences.OnCards,Margin=new Thickness(0,0,0,20)};root.Children.Add(enabled);root.Children.Add(cards);root.Children.Add(new TextBlock{Text=L.T("Sanatçı kartına veya profil başlığına sağ tıklayarak fotoğrafı yeniden arayabilir, kaynağını açabilir, kendi fotoğrafını seçebilir ya da albüm kapağına dönebilirsin. Profildeki Fotoğraf kaynağı düğmesi fotoğrafçıyı ve lisansı gösterir."),TextWrapping=TextWrapping.Wrap});
  return new(new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},()=>{if(ArtistPhotos.Preferences.Enabled!=(enabled.IsChecked==true))ArtistPhotos.Close();ArtistPhotos.Preferences.Enabled=enabled.IsChecked==true;ArtistPhotos.Preferences.OnCards=cards.IsChecked==true;SaveArtistPhotoPreferences();ArtistPhotos.Notify("");},()=>{},()=>true);
 }
}
