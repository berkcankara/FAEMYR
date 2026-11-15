using System.Windows.Automation;
namespace Ahenk;
public partial class MainWindow
{
 void UpdateSearchPrompt()
 {
  string prompt=view switch{"albums"=>L.T("hangi albümü arıyorsun?"),"artists"=>L.T("hangi sanatçıyı arıyorsun?"),"playlists"=>L.T("hangi listeyi arıyorsun?"),"favorites"=>L.T("favorilerinde ne arıyorsun?"),"artist"=>L.T("sanatçının hangi albümünü arıyorsun?"),_=>L.T("hangi parçayı arıyorsun?")};
  Search.Tag=topSearchExpanded?prompt:L.T("Bul");AutomationProperties.SetHelpText(Search,prompt);
 }
}
