using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Faemyr;
public sealed class LyricsPreferences
{
 public bool FollowCurrentLine{get;set;}=true;
}
public partial class MainWindow
{
 LyricsPreferences lyricsPreferences=new();
 TextBlock? lyricsSettingsStatus,lyricsSettingsTrack,lyricsSettingsNetwork;
 string lyricsStatusText=L.T("Henüz çalan parça yok.");
 string LyricsSettingsPath=>Path.Combine(Paths.Data,"lyrics-settings.json");
 static string LyricsServiceDescription=>L.T("İnternette bul / Yeniden ara düğmesi parça bilgilerini LRCLIB’e, gerekirse sanatçı ve parça adını lyrics.ovh’ye gönderir. Müzik dosyası gönderilmez. Bulunan sözler çevrimdışı kullanım için yerel olarak saklanır.");
 void SetLyricsStatus(string message,bool showInPanel=true)
 {
  lyricsStatusText=message;LyricsStatus.Text=message;lyricsStatusInPanel=showInPanel;ApplyLyricsPresentation();UpdateLyricsSettingsInfo();
 }
 void UpdateLyricsSettingsInfo()
 {
  if(lyricsSettingsTrack!=null)lyricsSettingsTrack.Text=now==null?L.T("Henüz çalan parça yok."):now.Title+" · "+now.Artist;
  if(lyricsSettingsStatus!=null)lyricsSettingsStatus.Text=lyricsStatusText;
  if(lyricsSettingsNetwork!=null)lyricsSettingsNetwork.Text=LyricsServiceDescription+(lyricsDocument?.Source=="lyrics.ovh"?L.T("\n\nGeçerli sözler zaman bilgisi içermez; kayıt sürümü farklı olabilir."):"");
 }
 SettingsSection BuildLyricsSettings()
 {
  var stack=new StackPanel();
  void Label(string text,bool heading=false)
  {
   var block=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=heading?19:12,FontWeight=heading?FontWeights.SemiBold:FontWeights.Normal,Margin=new Thickness(0,heading?20:6,0,10)};
   block.SetResourceReference(TextBlock.ForegroundProperty,heading?"Ink":"Muted");stack.Children.Add(block);
  }
  Label(L.T("Şarkı sözleri"),true);
  var follow=new CheckBox{Content=L.T("Çalan satırı takip et"),IsChecked=lyricsPreferences.FollowCurrentLine,Margin=new Thickness(0,10,0,8)};follow.SetResourceReference(Control.ForegroundProperty,"Ink");stack.Children.Add(follow);
  Label(L.T("Zamanlı sözlerde çalan satıra yumuşakça kaydırır. Kapalıyken çalan satır vurgulanır; sözleri kendin kaydırabilirsin. Bu tercih sonraki açılışta da hatırlanır."));
  Label(L.T("Geçerli parçanın söz bilgisi"),true);
  lyricsSettingsTrack=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=14,Margin=new Thickness(0,4,0,10)};lyricsSettingsTrack.SetResourceReference(TextBlock.ForegroundProperty,"Ink");stack.Children.Add(lyricsSettingsTrack);
  lyricsSettingsStatus=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};lyricsSettingsStatus.SetResourceReference(TextBlock.ForegroundProperty,"Muted");stack.Children.Add(lyricsSettingsStatus);
  Label(L.T("Söz servisleri"),true);lyricsSettingsNetwork=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,LineHeight=21};lyricsSettingsNetwork.SetResourceReference(TextBlock.ForegroundProperty,"Muted");stack.Children.Add(lyricsSettingsNetwork);UpdateLyricsSettingsInfo();
  void Save()
  {
   var next=new LyricsPreferences{FollowCurrentLine=follow.IsChecked==true};PlayerJson.Write(LyricsSettingsPath,next);lyricsPreferences=next;
   SmoothScroll.Cancel(LyricsScroll);lyricActive=-2;UpdateLyricsPlayback();
  }
  return new(new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Save,()=>{},()=>true);
 }
 void ReleaseLyricsSettings(){lyricsSettingsStatus=null;lyricsSettingsTrack=null;lyricsSettingsNetwork=null;}
}
