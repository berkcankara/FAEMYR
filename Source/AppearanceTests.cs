using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Ahenk;
public static class AppearanceTests
{
 public static void Run(string directory,Action<bool,string> check)
 {
  check(AppearancePreferences.NormalizeHex(" aB12ff ")=="#AB12FF","Custom color accepts and normalizes six-digit HEX");
  check(AppearancePreferences.NormalizeHex("#GG1212")==null&&AppearancePreferences.NormalizeHex("#123")==null&&AppearancePreferences.NormalizeHex("#001122FF")==null,"Invalid and translucent accent colors are rejected");
  string path=Path.Combine(directory,"appearance-test.json");var p=new AppearancePreferences{Accent="#ABCDEF",PlayingColor="#F04188",ListOpacity=63,BlurRadius=37};p.Save(path);var restored=AppearancePreferences.Load(path);
  check(restored.Accent==p.Accent&&restored.PlayingColor==p.PlayingColor,"Accent and independent playing color persist");check(restored.ListOpacity==63&&restored.BlurRadius==37,"List opacity and blur preferences persist");
  p.ListOpacity=999;p.BlurRadius=-100;p.Validate();check(p.ListOpacity==100&&p.BlurRadius==0,"Surface preferences are bounded safely");p.ListOpacity=double.NaN;p.BlurRadius=double.PositiveInfinity;p.Validate();check(p.ListOpacity==70&&p.BlurRadius==24,"Invalid numeric surface settings recover defaults");
  File.WriteAllText(path,"{bad");check(AppearancePreferences.Load(path).Accent==null&&AppearancePreferences.Load(path).ListOpacity==70,"Corrupt appearance settings recover defaults");
  check(AppearancePreferences.ForegroundOn(Colors.White)==Colors.Black&&AppearancePreferences.ForegroundOn(Colors.Black)==Colors.White,"Play icon foreground contrasts with light and dark custom colors");
  var original=Themes.Appearance;try
  {
   Themes.Appearance=new(){Accent="#1122FF",ListOpacity=63,BlurRadius=37};Themes.Apply("dark");
   Color Resource(string key)=>((SolidColorBrush)Application.Current.Resources[key]).Color;
   check(Resource("PlayingAccent")==Resource("Accent"),"Playing track follows accent by default");
   Themes.Appearance.PlayingColor="#F04188";Themes.ApplyAppearance();check(Resource("PlayingAccent")!=Resource("Accent")&&Resource("PlayingAccent")==Color.FromRgb(240,65,136),"Playing track can use its own color");
   Themes.Apply("light");check(Resource("Accent")==Color.FromRgb(17,34,255)&&Resource("PlayingAccent")==Color.FromRgb(240,65,136),"Custom colors survive changing the base theme");
   check(Resource("GlassSurface").A==(byte)Math.Round(.63*255)&&(double)Application.Current.Resources["ListBlurRadius"]==37,"Surface preferences update the live resources");
   Themes.Appearance=new();Themes.ApplyAppearance();check(Resource("Accent")==((SolidColorBrush)new BrushConverter().ConvertFromString(Themes.DefaultAccent)!).Color&&Resource("PlayingAccent")==Resource("Accent"),"Reset restores linked theme colors");
  }finally{Themes.Appearance=original;Themes.Apply("dark");}
 }
}
