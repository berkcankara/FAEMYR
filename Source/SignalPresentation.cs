using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Ahenk;
public sealed class SignalDisplayPreferences
{
 public bool ShowHeading{get;set;}=true;
 public List<string> Hidden{get;set;}=["gain","decoder","volume","wavpack","notes"];
 public static readonly (string Key,string Label)[] Choices=[("source","Kaynak ses biçimi"),("device","Çıkış aygıtı"),("output","Çıkış modu ve Windows biçimi"),("gain","ReplayGain"),("decoder","Decoder ve kesintisiz oynatma"),("volume","Ses seviyesi"),("wavpack","WavPack bilgisi"),("notes","Ek teknik notlar")];
 public void Validate()=>Hidden=(Hidden??[]).Where(key=>Choices.Any(choice=>choice.Key==key)).Distinct().ToList();
 public bool Shows(string key)=>!Hidden.Contains(key);
 public string Format(string raw)
 {
  var lines=raw.Split('\n');
  // Playback status and failures always remain readable, independently of the
  // optional technical fields. This never modifies audio configuration.
  if(!lines.Any(line=>line.StartsWith("Kaynak:")||line.StartsWith("DSD:")||line.StartsWith("WASAPI")||line.StartsWith("DoP ")))return L.T(raw);
  var visible=new List<string>();
  foreach(var line in lines)
  {
   if(line.StartsWith("Kaynak:")||line.StartsWith("DSD:"))
   {
    int arrow=line.IndexOf('→');bool source=Shows("source"),device=Shows("device");
    if(arrow<0){if(source)visible.Add(line);}
    else if(source&&device)visible.Add(line);
    else if(source)visible.Add(line[..arrow].TrimEnd());
    else if(device)visible.Add("Çıkış: "+line[(arrow+1)..].Trim());
    continue;
   }
   if(line.StartsWith("Ses tamponu")){visible.Add(line);continue;}
   string category=line.StartsWith("WASAPI")||line.StartsWith("DoP ")?"output":line.StartsWith("ReplayGain")?"gain":line.StartsWith("Akış decoder")?"decoder":line.StartsWith("Ses %")||line.StartsWith("Sessiz ·")?"volume":line.StartsWith("WavPack:")?"wavpack":"notes";
   if(Shows(category))visible.Add(line);
  }
  return visible.Count>0?L.Signal(string.Join("\n",visible)):L.T("Bilgileri seçmek için sağ tıkla.");
 }
}
public partial class MainWindow
{
 SignalDisplayPreferences signalDisplay=new();string signalRaw="";
 readonly List<(MenuItem Item,TextBlock Check,Func<bool> Selected)> signalMenuChecks=[];
 string SignalDisplayPath=>Path.Combine(Paths.Data,"signal-display.json");
 void InitializeSignalPresentation()
 {
  signalDisplay=PlayerJson.Read<SignalDisplayPreferences>(SignalDisplayPath)??new();signalDisplay.Validate();
  var menu=new ContextMenu();SignalBox.ContextMenu=menu;
  void Add(string label,Func<bool> selected,Action toggle)
  {
   var header=new StackPanel{Orientation=Orientation.Horizontal};var check=new TextBlock{Text="✓",Width=22};check.SetResourceReference(TextBlock.ForegroundProperty,"Accent");header.Children.Add(check);header.Children.Add(new TextBlock{Text=L.T(label)});
   var item=new MenuItem{Header=header,IsCheckable=true,StaysOpenOnClick=true};AutomationProperties.SetName(item,label);signalMenuChecks.Add((item,check,selected));menu.Items.Add(item);
   item.Click+=(_,_)=>{toggle();SaveSignalPresentation();};
  }
  Add("Başlığı göster",()=>signalDisplay.ShowHeading,()=>signalDisplay.ShowHeading=!signalDisplay.ShowHeading);menu.Items.Add(new Separator());
  foreach(var choice in SignalDisplayPreferences.Choices)Add(choice.Label,()=>signalDisplay.Shows(choice.Key),()=>{if(!signalDisplay.Hidden.Remove(choice.Key))signalDisplay.Hidden.Add(choice.Key);});
  menu.Items.Add(new Separator());var reset=new MenuItem{Header=L.T("Varsayılanlara dön")};reset.Click+=(_,_)=>{signalDisplay=new();SaveSignalPresentation();};menu.Items.Add(reset);menu.Opened+=(_,_)=>SyncSignalMenu();
  signalRaw=Signal.Text;RenderSignalPresentation();
 }
 void SetSignal(string text){if(signalRaw==text)return;signalRaw=text;RenderSignalPresentation();}
 void RenderSignalPresentation()
 {
  SignalHeading.Visibility=Visible(signalDisplay.ShowHeading);Signal.Margin=new Thickness(0,signalDisplay.ShowHeading?4:0,0,0);Signal.Text=signalDisplay.Format(signalRaw);SyncSignalMenu();
 }
 void SyncSignalMenu(){foreach(var row in signalMenuChecks){row.Item.IsChecked=row.Selected();row.Check.Visibility=Visible(row.Item.IsChecked);}}
 void SaveSignalPresentation()
 {
  signalDisplay.Validate();RenderSignalPresentation();try{PlayerJson.Write(SignalDisplayPath,signalDisplay);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Status.Text="Sinyal yolu görünümü kaydedilemedi: "+ex.Message;}
 }
}
