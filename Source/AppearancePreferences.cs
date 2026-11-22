using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Faemyr;
public sealed class AppearancePreferences
{
 public string? Accent {get;set;}
 public string? PlayingColor {get;set;}
 public double ListOpacity {get;set;}=70;
 public double BlurRadius {get;set;}=24;
 public bool ModuleTransparency {get;set;}
 public Dictionary<string,string> Colors{get;set;}=new(StringComparer.Ordinal);
 public AppearancePreferences Copy()=>new(){Accent=Accent,PlayingColor=PlayingColor,ListOpacity=ListOpacity,BlurRadius=BlurRadius,ModuleTransparency=ModuleTransparency,Colors=new(Colors??[],StringComparer.Ordinal)};
 public static string? NormalizeHex(string? value)
 {
  string text=(value??"").Trim();if(!text.StartsWith('#'))text="#"+text;
  return Regex.IsMatch(text,@"^#[0-9A-Fa-f]{6}$")?text.ToUpperInvariant():null;
 }
 public void Validate(){Accent=NormalizeHex(Accent);PlayingColor=NormalizeHex(PlayingColor);ListOpacity=double.IsFinite(ListOpacity)?Math.Clamp(ListOpacity,0,100):70;BlurRadius=double.IsFinite(BlurRadius)?Math.Clamp(BlurRadius,0,60):24;Colors=(Colors??[]).Where(p=>ColorRoles.All.Any(r=>r.Key==p.Key)&&p.Key is not ("Accent" or "PlayingAccent")&&NormalizeHex(p.Value)!=null).ToDictionary(p=>p.Key,p=>NormalizeHex(p.Value)!);}
 public static AppearancePreferences Load(string path){try{var p=JsonSerializer.Deserialize<AppearancePreferences>(File.ReadAllText(path))??new();p.Validate();return p;}catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){return new();}}
 public void Save(string path){Validate();Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+".tmp";try{File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
 public static Color ForegroundOn(Color color)
 {
  static double Linear(byte v){double s=v/255.0;return s<=.04045?s/12.92:Math.Pow((s+.055)/1.055,2.4);}
  double l=.2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);
  return (l+.05)/.05>=1.05/(l+.05)?System.Windows.Media.Colors.Black:System.Windows.Media.Colors.White;
 }
}
public sealed record ColorRole(string Key,string Text){public string Name=>L.T(Text);}
public static class ColorRoles
{
 public static readonly ColorRole[] All=[new("Accent","Vurgu rengi"),new("PlayingAccent","Çalan parça rengi"),new("Canvas","Ana arka plan"),new("SidebarSurface","Sol menü"),new("PanelSurface","Sağ panel"),new("PlayerSurface","Alt oynatıcı"),new("CaptionSurface","Pencere başlığı"),new("Surface","Kart ve kutular"),new("ControlSurface","Düğmeler ve girişler"),new("HoverSurface","Üzerine gelinen alan"),new("SelectedSurface","Seçili alan"),new("Line","İnce kenarlıklar"),new("Ink","Ana yazı"),new("Muted","İkincil yazı"),new("OnAccent","Vurgulu düğme simgesi"),new("GlassSurface","Parça listesi zemini"),new("HeaderSurface","Liste sütun başlıkları"),new("AlternateSurface","Alternatif liste satırı"),new("AlbumTint","Albüm renk geçişi"),new("VisualizerAccent","Visualizer"),new("LyricsSurface","Şarkı sözü kartı"),new("LyricsAccent","Aktif şarkı sözü ve parıltı"),new("UpNextSurface","Sıradaki parça kartı"),new("RatingAccent","Yıldızlar")];
}
