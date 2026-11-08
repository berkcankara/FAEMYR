using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace Ahenk;
public sealed record ThemeChoice(string Id,string Text){public string Name=>L.T(Text);}
public static class Themes
{
 public static readonly ThemeChoice[] Choices=[new("dark","Koyu"),new("light","Açık"),new("warm","Sıcak"),new("oled","OLED · Tam siyah")];
 public static string Current {get;private set;}="dark";
 public static AppearancePreferences Appearance {get;set;}=new();
 public static string AppearancePath=>Path.Combine(Paths.Data,"appearance.json");
 public static string DefaultAccent {get;private set;}="#C8F281";
 static readonly Dictionary<string,Color> baseColors=[];
 public static string DefaultColor(string key){var c=baseColors.GetValueOrDefault(key,Colors.Gray);return $"#{c.R:X2}{c.G:X2}{c.B:X2}";}
 public static void Load(){Appearance=AppearancePreferences.Load(AppearancePath);try{Apply(JsonSerializer.Deserialize<string>(File.ReadAllText(Path.Combine(Paths.Data,"theme.json")))??"dark");}catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){Apply("dark");}}
 public static void Apply(string id)
 {
  if(!Choices.Any(t=>t.Id==id))id="dark";Current=id;
  string[] names=["Canvas","Surface","PanelSurface","PlayerSurface","CaptionSurface","ControlSurface","HoverSurface","SelectedSurface","Line","Ink","Muted","Accent","OnAccent","GlassSurface","HeaderSurface","AlternateSurface"];
  string[] values=id switch{
   "oled"=>["#000000","#000000","#FF000000","#FF000000","#000000","#000000","#111111","#181818","#242424","#F0F0F0","#A0A0A0","#C8F281","#142015","#FF000000","#FF000000","#00000000"],
   "light"=>["#F4F3F1","#FFFFFF","#E6FFFFFF","#F2FFFFFF","#F4F3F1","#E7E6E3","#DCDAD7","#DAD6CF","#D1CECA","#191919","#65615D","#486129","#FFFFFF","#B3F4F3F1","#D9E9E6E2","#09101010"],
   "warm"=>["#171310","#241E19","#D91D1814","#ED211B16","#14110E","#332B23","#43382E","#4A3E31","#46392F","#F5EBDE","#B8AA98","#E7B776","#21170C","#B3171310","#80312620","#09FFFFFF"],
   _=>["#0B0B0B","#181818","#D9111111","#ED181818","#080808","#242424","#303030","#353535","#303030","#F0F0F0","#A0A0A0","#C8F281","#142015","#B30B0B0B","#80202020","#09FFFFFF"]};
  baseColors.Clear();for(int i=0;i<names.Length;i++)baseColors[names[i]]=(Color)ColorConverter.ConvertFromString(values[i]);
  baseColors["SidebarSurface"]=baseColors["PanelSurface"];baseColors["LyricsSurface"]=baseColors["Surface"];baseColors["UpNextSurface"]=baseColors["Surface"];baseColors["AlbumTint"]=Color.FromRgb(48,48,48);
  DefaultAccent=values[11];ApplyAppearance();
 }
 public static void ApplyAppearance()
 {
  Appearance.Validate();var accent=(Color)ColorConverter.ConvertFromString(Appearance.Accent??DefaultAccent);var playing=(Color)ColorConverter.ConvertFromString(Appearance.PlayingColor??Appearance.Accent??DefaultAccent);
  void Set(string key,Color c){var b=new SolidColorBrush(c);b.Freeze();Application.Current.Resources[key]=b;}
  Color Custom(string key,Color fallback){if(Current=="oled"&&new[]{"Canvas","SidebarSurface","PanelSurface","PlayerSurface","CaptionSurface","Surface","ControlSurface","GlassSurface","HeaderSurface","LyricsSurface","UpNextSurface","AlbumTint","AlternateSurface"}.Contains(key))return key=="AlternateSurface"?Colors.Transparent:Colors.Black;if(!Appearance.Colors.TryGetValue(key,out string? text))return fallback;var color=(Color)ColorConverter.ConvertFromString(text);color.A=fallback.A;return color;}
  foreach(var item in baseColors)Set(item.Key,Custom(item.Key,item.Value));
  Set("Accent",accent);Set("PlayingAccent",playing);Set("OnAccent",Custom("OnAccent",AppearancePreferences.ForegroundOn(accent)));
  foreach(string key in new[]{"VisualizerAccent","LyricsAccent","RatingAccent"})Set(key,Custom(key,accent));
  var canvas=Custom("GlassSurface",((SolidColorBrush)Application.Current.Resources["Canvas"]).Color);canvas.A=(byte)Math.Round(Appearance.ListOpacity*255/100);Set("GlassSurface",canvas);Application.Current.Resources["ListBlurRadius"]=Appearance.BlurRadius;
 }
 public static void Save(){Directory.CreateDirectory(Paths.Data);string path=Path.Combine(Paths.Data,"theme.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(Current));File.Move(path+".tmp",path,true);}
}
public partial class MainWindow
{
 void InitializeThemes()=>UpdateAccentControls();
 void UpdateAccentControls(){ShuffleButton.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty,order.Shuffle?"Accent":"Ink");RepeatButton.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty,order.Repeat==RepeatMode.Off?"Ink":"Accent");}
}
