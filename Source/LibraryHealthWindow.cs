using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace Ahenk;
public partial class MainWindow
{
 void LibraryHealth_Click(object sender,RoutedEventArgs e)
 {
  if(!libraryReady)return;var cancel=new CancellationTokenSource();var window=new Window{Title=L.T("Lyra — Arşiv sağlığı"),Owner=settingsWindow??this,Width=920,Height=720,WindowStartupLocation=WindowStartupLocation.CenterOwner};window.SetResourceReference(BackgroundProperty,"Canvas");window.SetResourceReference(ForegroundProperty,"Ink");
  var root=new DockPanel{Margin=new Thickness(24)};window.Content=root;var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);top.Children.Add(new TextBlock{Text=L.T("Arşiv sağlığı"),FontSize=25,FontWeight=FontWeights.Bold});
  var status=new TextBlock{Text=L.T("Eksik dosyaları kontrol et; yinelenen dosyalar içerik özetiyle doğrulanır. Müzik dosyaları silinmez veya değiştirilmez."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,16)};status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");top.Children.Add(status);
  var tools=new WrapPanel();top.Children.Add(tools);var inspect=new Button{Content=L.T("Eksik dosyaları kontrol et")};var duplicates=new Button{Content=L.T("Yinelenenleri doğrula")};var moved=new Button{Content=L.T("Taşınanları bul…")};var manual=new Button{Content=L.T("Dosyayı elle bul…")};var apply=new Button{Content=L.T("Bulunanları eşleştir"),IsEnabled=false};foreach(var button in new[]{inspect,duplicates,moved,manual,apply})tools.Children.Add(button);
  var list=new ListBox{Margin=new Thickness(0,14,0,0),HorizontalContentAlignment=HorizontalAlignment.Stretch};list.SetResourceReference(Control.BackgroundProperty,"Canvas");list.SetResourceReference(Control.ForegroundProperty,"Ink");var template=new DataTemplate();var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Label"));label.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);label.SetValue(MarginProperty,new Thickness(8,8,8,12));template.VisualTree=label;list.ItemTemplate=template;root.Children.Add(list);var items=new List<HealthItem>();bool busy=false;
  void Busy(bool value){busy=value;foreach(var b in new[]{inspect,duplicates,moved,manual})b.IsEnabled=!value;apply.IsEnabled=!value&&items.Any(i=>i.Candidate!=null);}
  var progress=new Progress<string>(text=>status.Text=text);
  async Task Inspect(bool full)
  {
   if(busy)return;Busy(true);try
   {
    var snapshot=library.Select(t=>t.Copy()).ToArray();var report=await Task.Run(()=>LibraryHealth.Inspect(snapshot,full,progress,cancel.Token));
    items=report.Items;list.ItemsSource=items;var updates=new List<Track>();foreach(var baseline in report.Baselines)foreach(var track in library.Where(t=>t.AudioPath.Equals(baseline.AudioPath,StringComparison.OrdinalIgnoreCase))){track.FileSize=baseline.FileSize;track.FileSignature=baseline.FileSignature;updates.Add(track.Copy());}
    if(updates.Count>0)await Task.Run(()=>{using var db=new LibraryStore();db.SaveMany(updates);},cancel.Token);status.Text=L.T($"{items.Count(i=>i.Kind==L.T("Eksik dosya"))} eksik dosya · {items.Count(i=>i.Kind==L.T("Aynı dosya içeriği"))} eş dosya kaydı. ")+(items.Count==0?L.T("Sorun bulunmadı."):L.T("Kayıtları aşağıda inceleyebilirsin."));
   }catch(OperationCanceledException){}catch(Exception ex){status.Text=ex.Message;}finally{Busy(false);}
  }
  inspect.Click+=async(_,_)=>await Inspect(false);duplicates.Click+=async(_,_)=>await Inspect(true);
  moved.Click+=async(_,_)=>
  {
   if(busy)return;var folder=new OpenFolderDialog{Title=L.T("Taşınan müziklerin aranacağı klasörü seç")};if(folder.ShowDialog(window)!=true)return;Busy(true);
   try{var known=library.Select(t=>t.AudioPath).ToHashSet(StringComparer.OrdinalIgnoreCase);var missing=library.Where(t=>!File.Exists(t.AudioPath)).Select(t=>t.Copy()).ToArray();var candidates=await Task.Run(()=>Walk(folder.FolderName,_=>{}).Where(p=>!known.Contains(p)&&!p.EndsWith(".cue",StringComparison.OrdinalIgnoreCase)).ToArray(),cancel.Token);items=await LibraryHealth.FindMoved(missing,candidates,progress,cancel.Token);list.ItemsSource=items;status.Text=L.T($"{items.Count(i=>i.Candidate!=null)} güvenilir eşleşme bulundu. İnceledikten sonra “Bulunanları eşleştir”i kullan.");}
   catch(OperationCanceledException){}catch(Exception ex){status.Text=ex.Message;}finally{Busy(false);}
  };
  manual.Click+=(_,_)=>
  {
   if(list.SelectedItem is not HealthItem item){status.Text=L.T("Önce eksik dosya kaydını seç.");return;}var file=new OpenFileDialog{Title=L.T("Bu kaydın yeni dosyasını seç"),Filter=L.T("Müzik dosyaları|*.flac;*.wv;*.wav;*.aiff;*.aif;*.m4a;*.mp3;*.ogg;*.opus;*.ape")};if(file.ShowDialog(window)!=true)return;items=[new(L.T("Elle seçilen eşleşme"),item.Track,item.Track.AudioPath+"\n→ "+file.FileName,file.FileName)];list.ItemsSource=items;Busy(false);
  };
  apply.Click+=async(_,_)=>
  {
   if(busy)return;Busy(true);try{int changed=0;foreach(var item in items.Where(i=>i.Candidate!=null)){RelocateLibraryFile(item.Track.AudioPath,item.Candidate!);changed++;}SaveListening();SavePlaylists();SaveSession(false);RefreshView();await RebuildPlaybackPlan();status.Text=L.T($"{changed} dosya eşleştirildi; favoriler ve çalma listeleri güncellendi.");items=[];list.ItemsSource=items;}catch(Exception ex){status.Text=ex.Message;}finally{Busy(false);}
  };
  window.Closed+=(_,_)=>cancel.Cancel();window.ShowDialog();
 }
 void RelocateLibraryFile(string oldPath,string newPath)
 {
  newPath=System.IO.Path.GetFullPath(newPath);if(!File.Exists(newPath))return;if(favorites.LoadError.Length>0)throw new InvalidDataException(L.T("Favoriler okunamadığı için eşleştirme yapılmadı: ")+favorites.LoadError);var mapping=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var replacements=library.Where(t=>t.AudioPath.Equals(oldPath,StringComparison.OrdinalIgnoreCase)).ToArray();
  foreach(var t in replacements)
  {
   string previous=t.Path;if(t.IsCue){var cue=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(newPath)!,System.IO.Path.GetFileName(t.CueSheetPath));if(File.Exists(cue))t.CueSheetPath=cue;t.Path=t.CueSheetPath+"#track="+t.Number;t.SourcePath=newPath;}else t.Path=newPath;
   t.Modified=File.GetLastWriteTimeUtc(newPath).Ticks;t.SourceModified=t.Modified;t.FileSize=new FileInfo(newPath).Length;t.FileSignature=LibraryFingerprint.Read(newPath);mapping[previous]=t.Path;store.Delete(previous);
  }
  if(mapping.Count==0)return;store.SaveMany(replacements);favorites.Relocate(mapping);foreach(var item in mapping)listening.Relocate(item.Key,item.Value);
  foreach(var playlist in playlists.Items)playlist.Tracks=playlist.Tracks.Select(p=>mapping.GetValueOrDefault(p,p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
  foreach(var t in queue.Concat(now==null?[]:new[]{now}).Distinct())if(mapping.TryGetValue(t.Path,out string? next)){t.Path=next;var replacement=replacements.First(x=>x.Path==next);t.CueSheetPath=replacement.CueSheetPath;t.SourcePath=replacement.SourcePath;t.SourceModified=replacement.SourceModified;t.Modified=replacement.Modified;t.FileSize=replacement.FileSize;t.FileSignature=replacement.FileSignature;}
  if(now!=null)ShowTrackDetails(now);SavePlaylists();SaveSession(false);
 }
}
