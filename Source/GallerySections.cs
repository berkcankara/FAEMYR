using System.Globalization;
using System.Text;

namespace Faemyr;

// Keep the existing row virtualization: only the first row in each section
// carries a heading, and the remaining rows are realized on demand.
public sealed record GalleryRow<T>(string Heading, T[] Cards);

public static class GallerySections
{
 public static string Initial(string? name, CultureInfo culture)
 {
  if(string.IsNullOrWhiteSpace(name))return "#";
  foreach(var rune in name.Normalize(NormalizationForm.FormC).EnumerateRunes())
  {
   if(Rune.IsWhiteSpace(rune)||Rune.IsPunctuation(rune))continue;
   return Rune.IsLetter(rune)?rune.ToString().ToUpper(culture):"#";
  }
  return "#";
 }

 public static GalleryRow<T>[] Rows<T>(IEnumerable<T> sorted,int columns,Func<T,string>? key=null,bool descending=false)
 {
  columns=Math.Max(1,columns);
  if(key==null)return sorted.Chunk(columns).Select(cards=>new GalleryRow<T>("",cards)).ToArray();
  var culture=CultureInfo.CurrentCulture;
  var comparer=StringComparer.Create(culture,false);
  var groups=sorted.GroupBy(card=>Initial(key(card),culture),StringComparer.Ordinal);
  var ordered=descending?groups.OrderBy(g=>g.Key=="#"?1:0).ThenByDescending(g=>g.Key,comparer):groups.OrderBy(g=>g.Key=="#"?0:1).ThenBy(g=>g.Key,comparer);
  return ordered.SelectMany(g=>g.Chunk(columns).Select((cards,index)=>new GalleryRow<T>(index==0?g.Key:"",cards))).ToArray();
 }
}
