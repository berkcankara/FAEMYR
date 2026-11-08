using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Ahenk;
public partial class MainWindow
{
 Button? libraryAddFolder,libraryRescan,libraryCancelScan;
 ItemsControl? libraryRoots;
 FrameworkElement BuildLibrarySettings()
 {
  var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
  var content=new StackPanel{Margin=new Thickness(0,0,12,0)};scroll.Content=content;
  content.Children.Add(new TextBlock{Text=L.T("Müzik arşivi"),FontSize=24,FontWeight=FontWeights.Bold});
  var intro=new TextBlock{Text=L.T("Müzik klasörlerini ekle veya mevcut arşivini yeniden tara. Bu bölümdeki işlemler hemen uygulanır."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,9,0,24)};
  intro.SetResourceReference(TextBlock.ForegroundProperty,"Muted");content.Children.Add(intro);
  var actions=new WrapPanel{Margin=new Thickness(0,0,0,16)};content.Children.Add(actions);
  libraryAddFolder=new Button{Content=L.T("＋ Klasör ekle"),Height=44,Margin=new Thickness(0,0,10,10)};
  libraryAddFolder.SetResourceReference(Control.BackgroundProperty,"Accent");libraryAddFolder.SetResourceReference(Control.ForegroundProperty,"OnAccent");libraryAddFolder.Click+=AddFolder_Click;actions.Children.Add(libraryAddFolder);
  libraryRescan=new Button{Content=L.T("Arşivi yenile"),Height=44,Margin=new Thickness(0,0,10,10)};libraryRescan.Click+=Rescan_Click;actions.Children.Add(libraryRescan);
  var health=new Button{Content=L.T("Arşiv sağlığı…"),Height=44,Margin=new Thickness(0,0,10,10)};health.Click+=LibraryHealth_Click;actions.Children.Add(health);
  libraryCancelScan=new Button{Content=L.T("Taramayı durdur"),Height=44,Margin=new Thickness(0,0,0,10)};libraryCancelScan.Click+=CancelScan_Click;actions.Children.Add(libraryCancelScan);
  var status=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=13,Margin=new Thickness(0,0,0,24)};
  status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");status.SetBinding(TextBlock.TextProperty,new Binding(nameof(TextBlock.Text)){Source=Status});content.Children.Add(status);
  content.Children.Add(new TextBlock{Text=L.T("Eklenen klasörler"),FontSize=17,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
  libraryRoots=new ItemsControl();var template=new DataTemplate();var path=new FrameworkElementFactory(typeof(TextBlock));path.SetBinding(TextBlock.TextProperty,new Binding());path.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);path.SetValue(MarginProperty,new Thickness(0,0,0,12));template.VisualTree=path;libraryRoots.ItemTemplate=template;content.Children.Add(libraryRoots);
  RefreshLibraryRoots();UpdateLibraryScanState();return scroll;
 }
 void RefreshLibraryRoots(){if(libraryRoots!=null)libraryRoots.ItemsSource=store.Roots();}
 void UpdateLibraryScanState()
 {
  bool scanning=scanCts!=null;
  if(libraryAddFolder!=null)libraryAddFolder.IsEnabled=!scanning;
  if(libraryRescan!=null)libraryRescan.IsEnabled=!scanning;
  if(libraryCancelScan!=null)libraryCancelScan.Visibility=Visible(scanning);
 }
 void ReleaseLibrarySettings(){libraryAddFolder=libraryRescan=libraryCancelScan=null;libraryRoots=null;}
}
