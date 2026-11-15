using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Ahenk;
public partial class MainWindow
{
 FrameworkElement BuildPlaylistCoverEditor(LocalPlaylist draft,Window dialog,Func<string?>? selectCover=null)
 {
  var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,20)};
  var preview=new Border{Width=150,Height=150,CornerRadius=new CornerRadius(14)};
  var letter=new TextBlock{Text="♪",FontSize=62,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,Foreground=Brushes.White};preview.Child=letter;row.Children.Add(preview);
  void Preview(){var image=CoverImage(draft.Cover,400);preview.Background=image!=null?new ImageBrush(image){Stretch=Stretch.UniformToFill}:new SolidColorBrush((Color)ColorConverter.ConvertFromString(draft.Color));letter.Visibility=image==null?Visibility.Visible:Visibility.Collapsed;}
  Preview();var tools=new StackPanel{Margin=new Thickness(20,18,0,0)};row.Children.Add(tools);
  var choose=new Button{Content=L.T("Kapak seç…"),Margin=new Thickness(0,0,0,10)};tools.Children.Add(choose);
  choose.Click+=(_,_)=>
  {
   string? path;
   if(selectCover!=null)path=selectCover();
   else{var picker=new OpenFileDialog{Title=L.T("Çalma listesi kapağı seç"),Filter=L.T("Görseller|*.jpg;*.jpeg;*.png;*.bmp")};path=picker.ShowDialog(dialog)==true?picker.FileName:null;}
   if(path==null)return;
   if(CoverImage(path)==null){MessageBox.Show(dialog,L.T("Bu görsel açılamadı."),"Lyra");return;}
   draft.Cover=path;Preview();
  };
  var clear=new Button{Content=L.T("Renk kullan"),Margin=new Thickness(0,0,0,10)};tools.Children.Add(clear);clear.Click+=(_,_)=>{draft.Cover="";Preview();};
  var colors=new StackPanel{Orientation=Orientation.Horizontal};tools.Children.Add(colors);
  foreach(string color in new[]{"#53504B","#65517D","#335E56","#8B4C3C","#526183"})
  {
   var button=new Button{Width=25,Height=25,Padding=new Thickness(0),Margin=new Thickness(0,0,6,0),Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),ToolTip=color};
   button.Click+=(_,_)=>{draft.Color=color;draft.Cover="";Preview();};colors.Children.Add(button);
  }
  return row;
 }
 void SavePlaylistDraft(LocalPlaylist draft,LocalPlaylist? existing)
 {
  if(draft.Cover.Length>0&&draft.Cover!=existing?.Cover)draft.Cover=PlaylistArtwork.Import(draft.Cover,Paths.Data,draft.Id);
  int index=existing==null?-1:playlists.Items.IndexOf(existing);
  if(index<0)playlists.Items.Add(draft);else playlists.Items[index]=draft;
  try{playlists.Save();}catch{if(index<0)playlists.Items.Remove(draft);else playlists.Items[index]=existing!;throw;}
 }
}
