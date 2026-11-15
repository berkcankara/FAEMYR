using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Ahenk;
public partial class MainWindow
{
 ArtistBiographyService artistBiographies=null!;CancellationTokenSource? artistBiographyCts;string artistBiographyKey="";
 bool artistProfileMounted,artistHeroRectangular;
 int artistHeroIndex,artistToolbarIndex,artistAlbumsIndex;
 void InitializeArtistProfile()
 {
  artistBiographies=new(Path.Combine(Paths.Data,"artist-biographies"));
  MainContent.SizeChanged+=(_,_)=>UpdateArtistProfileLayout();
  Closed+=(_,_)=>{artistBiographyCts?.Cancel();artistBiographyCts?.Dispose();artistBiographies.Dispose();};
 }
 void UpdateArtistProfileHost()
 {
  bool profile=view=="artist";
  if(profile!=artistProfileMounted)
  {
   artistProfileMounted=profile;
   if(profile)
   {
    artistHeroIndex=MainContent.Children.IndexOf(ArtistHero);artistToolbarIndex=MainContent.Children.IndexOf(CollectionToolbar);artistAlbumsIndex=TrackArea.Children.IndexOf(AlbumScroll);
    MainContent.Children.Remove(ArtistHero);MainContent.Children.Remove(CollectionToolbar);TrackArea.Children.Remove(AlbumScroll);
    ArtistProfileContent.Children.Insert(0,ArtistHero);ArtistProfileContent.Children.Insert(1,CollectionToolbar);ArtistProfileContent.Children.Insert(2,AlbumScroll);
    ScrollViewer.SetVerticalScrollBarVisibility(Albums,ScrollBarVisibility.Disabled);
   }
   else
   {
    ArtistProfileContent.Children.Remove(ArtistHero);ArtistProfileContent.Children.Remove(CollectionToolbar);ArtistProfileContent.Children.Remove(AlbumScroll);
    MainContent.Children.Insert(artistHeroIndex,ArtistHero);MainContent.Children.Insert(artistToolbarIndex,CollectionToolbar);TrackArea.Children.Insert(artistAlbumsIndex,AlbumScroll);AlbumScroll.Margin=new Thickness(0);
    Albums.ClearValue(ScrollViewer.VerticalScrollBarVisibilityProperty);CollectionToolbar.Margin=new Thickness(0,0,0,16);
    var p=InterfaceSettings.Current;ArtistHero.Margin=new Thickness(-p.ContentPadding,-(p.ContentPadding-2),-(p.ContentPadding-4),22);
   }
  }
  ArtistProfileScroll.Visibility=Visible(profile);UpdateArtistProfileLayout();
  if(!profile){artistBiographyCts?.Cancel();artistBiographyKey="";}
 }
 void UpdateArtistProfileLayout()
 {
  if(!artistProfileMounted)return;
  double padding=InterfaceSettings.Current.ContentPadding;
  ArtistProfileScroll.Margin=new Thickness(-padding,-(padding-2),-(padding-4),0);
  double width=ArtistProfileScroll.ActualWidth>0?ArtistProfileScroll.ActualWidth:MainContent.ActualWidth+padding*2-4;
  ArtistHero.Height=artistHeroRectangular?Math.Clamp(width*.39,320,480):340;
  ArtistHero.Margin=new Thickness(0,0,0,28);CollectionToolbar.Margin=new Thickness(padding,0,padding,16);
  AlbumScroll.Margin=new Thickness(padding,0,padding,0);ArtistAboutCard.Margin=new Thickness(padding,28,padding,32);
 }
 async void LoadArtistBiography(string name)
 {
  string language=L.Current,key=ArtistBiographyService.Key(name,language);if(artistBiographyKey==key)return;
  artistBiographyKey=key;artistBiographyCts?.Cancel();artistBiographyCts?.Dispose();artistBiographyCts=new();var ct=artistBiographyCts.Token;
  ArtistProfileScroll.ScrollToTop();ArtistBiographyHeading.Text=L.T("Sanatçı hakkında");ArtistBiographyText.Text=L.T("Wikipedia bilgileri yükleniyor…");ArtistBiographySource.Content=L.T("Wikipedia'da oku ↗");ArtistBiographySource.Tag=null;ArtistBiographyCredits.Visibility=Visibility.Collapsed;
  try
  {
   var biography=await artistBiographies.Get(name,language,ct);
   if(closed||ct.IsCancellationRequested||artistBiographyKey!=key||view!="artist"||artistName!=name||L.Current!=language)return;
   ArtistBiographyText.Text=biography?.Text.Length>0?biography.Text:L.T("Bu sanatçı için seçili dilde Wikipedia özeti bulunamadı.");
   ArtistBiographySource.Tag=biography?.Url;ArtistBiographyCredits.Visibility=Visible(biography?.Text.Length>0);
  }
  catch(OperationCanceledException){if(!closed&&!ct.IsCancellationRequested&&artistBiographyKey==key)ArtistBiographyText.Text=L.T("Wikipedia'ya şu anda ulaşılamıyor. Profili yeniden açarak tekrar deneyebilirsin.");}
  catch(Exception ex)when(ex is IOException or HttpRequestException or JsonException)
  {if(!closed&&!ct.IsCancellationRequested&&artistBiographyKey==key)ArtistBiographyText.Text=L.T("Wikipedia'ya şu anda ulaşılamıyor. Profili yeniden açarak tekrar deneyebilirsin.");}
 }
 void ArtistBiographySource_Click(object sender,RoutedEventArgs e)
 {
  if(ArtistBiographySource.Tag is string url&&ArtistBiographyService.ArticleUrl(url,L.Current))OpenArtistBiographyLink(url);
 }
 void ArtistBiographyLicense_Click(object sender,RoutedEventArgs e)=>OpenArtistBiographyLink("https://creativecommons.org/licenses/by-sa/4.0/");
 static void OpenArtistBiographyLink(string url){try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){}}
}
