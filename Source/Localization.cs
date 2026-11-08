using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
namespace Ahenk;

public static class L
{
 public static string Current{get;private set;}="tr";
 static readonly Dictionary<string,string> english=Read();
 static Dictionary<string,string> Read(){using var stream=typeof(L).Assembly.GetManifestResourceStream("Ahenk.Assets.en.json")!;return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;}
 public static string T(string text)=>Current=="en"&&english.TryGetValue(text,out var translated)?translated:text;
 public static string T(ref LocalizedStringHandler text)=>text.ToString();
 public static string LanguagePath=>Path.Combine(Paths.Data,"language.json");
 public static string ReadLanguage(){try{return JsonSerializer.Deserialize<string>(File.ReadAllText(LanguagePath))=="en"?"en":"tr";}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){return "tr";}}
 public static void Save(string id)=>PlayerJson.Write(LanguagePath,id=="en"?"en":"tr");
 public static void Load()=>Apply(ReadLanguage());
 public static void Apply(string id)
 {
  Current=id=="en"?"en":"tr";
  var culture=CultureInfo.GetCultureInfo(Current=="en"?"en-US":"tr-TR");CultureInfo.CurrentUICulture=culture;CultureInfo.CurrentCulture=culture;
  foreach(var pair in english)Application.Current.Resources[ResourceKey(pair.Key)]=T(pair.Key);
 }
 public static string ResourceKey(string text)=>"Loc_"+Paths.Hash(text)[..16];
 // Audio-engine diagnostics remain language-neutral internally. Only their
 // presentation is translated, so filtering and device logic stay unchanged.
 public static string Signal(string text)
 {
  if(Current!="en")return text;
  int arrow=text.IndexOf('→');if(arrow>=0&&(text.StartsWith("Kaynak:")||text.StartsWith("DSD:"))){int end=text.IndexOf('\n',arrow);if(end<0)end=text.Length;return Signal(text[..arrow])+text[arrow..end]+(end<text.Length?Signal(text[end..]):"");}
  foreach(var pair in english.Where(p=>p.Key.Length>3).OrderByDescending(p=>p.Key.Length))
   if(text.Contains(pair.Key,StringComparison.Ordinal))text=text.Replace(pair.Key,pair.Value,StringComparison.Ordinal);
  return text;
 }
}
[InterpolatedStringHandler]
public ref struct LocalizedStringHandler
{
 StringBuilder builder;
 public LocalizedStringHandler(int literalLength,int formattedCount){builder=new(literalLength+formattedCount*16);}
 public void AppendLiteral(string value)=>builder.Append(L.T(value));
 public void AppendFormatted<T>(T value)=>builder.Append(value);
 public void AppendFormatted<T>(T value,string? format)=>builder.Append(value is IFormattable f?f.ToString(format,L.Current=="en"?CultureInfo.GetCultureInfo("en-US"):CultureInfo.CurrentCulture):value?.ToString());
 public override string ToString()=>builder.ToString();
}
