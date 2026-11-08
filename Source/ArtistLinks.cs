using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Text.RegularExpressions;

namespace Ahenk;

public static class ArtistCatalog
{
 static readonly StringComparer Identity=StringComparer.OrdinalIgnoreCase;
 static readonly Regex FeatureMarker=new(@"(?:\s+|[\(\[]\s*)(?:feat(?:uring)?|ft)\.?\s+",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);
 static readonly Regex AmpersandMarker=new(@"(?<=\S)\s*&\s*(?=\S)",RegexOptions.CultureInvariant|RegexOptions.Compiled);
 static readonly Regex SlashMarker=new(@"(?<=\S)(?:\s+/\s*|/\s+)(?=\S)",RegexOptions.CultureInvariant|RegexOptions.Compiled);
 public sealed record Credit(string Label,string Profile);
 public static string[] Split(string? value)=>(value??"").Split([" / ",";","\r\n","\n"],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(Identity).ToArray();
 // Retain the full credit for display, but use only the leading artists as profile identities.
 // A spaced slash separates guests; an internal slash such as AC/DC is part of a name.
 // Split after removing the feature suffix so guest separators never create extra profiles.
 public static Credit[] Credits(string? value)
 {
  string text=value??"";var feature=FeatureMarker.Match(text);var ampersand=AmpersandMarker.Match(text);
  if(ampersand.Success&&(!feature.Success||ampersand.Index<feature.Index))feature=ampersand;
  var slash=SlashMarker.Match(text);if(slash.Success&&(!feature.Success||slash.Index<feature.Index))feature=slash;
  string head=feature.Success?text[..feature.Index].TrimEnd(' ','\t','(','[',','):text;
  var names=Split(head);if(names.Length==0)return Split(text).Select(n=>new Credit(n,n)).ToArray();
  string tail=feature.Success?text[feature.Index..]:"";
  if(tail.Length>0&&!char.IsWhiteSpace(tail[0])&&tail[0] is not ('&' or '/'))tail=" "+tail;
  return names.Select((name,i)=>new Credit(name+(i==names.Length-1?tail:""),name)).ToArray();
 }
 public static bool IsArtistField(string key)
 {
  if(key.Equals("Sanatçı",StringComparison.OrdinalIgnoreCase))return true;
  string tag=key[(key.LastIndexOf('/')+1)..].Trim().Replace("_","").Replace(" ","");
  // PERFORMER contains instrument/session credits, not the recording artist.
  // Keep those credits in metadata without turning them into profiles or links.
  return tag.Equals("artist",StringComparison.OrdinalIgnoreCase);
 }
 public static IEnumerable<string> Names(Track t)=>(string.IsNullOrWhiteSpace(t.Artist)
  ?t.Fields.Where(f=>IsArtistField(f.Key)).SelectMany(f=>Credits(f.Value))
  :Credits(t.Artist)).Select(c=>c.Profile).Distinct(Identity);
 public static HashSet<string> ProfileNames(IEnumerable<Track> tracks)=>tracks.SelectMany(Names).ToHashSet(Identity);
 public static bool Matches(Track t,string name)=>Names(t).Contains(name,Identity);
 public static HashSet<string> AlbumKeys(IEnumerable<Track> tracks,string name)=>tracks.Where(t=>Matches(t,name)).Select(t=>t.AlbumKey).ToHashSet(StringComparer.Ordinal);
 public static string? ResolveProfile(IEnumerable<Track> tracks,string label)
 {
  var profiles=ProfileNames(tracks);foreach(var credit in Credits(label))if(profiles.TryGetValue(credit.Profile,out var profile))return profile;return null;
 }
 public static List<ArtistCard> Cards(IEnumerable<Track> tracks,string query="")=>tracks
  .SelectMany(t=>Names(t).Select(name=>(Name:name,Track:t))).GroupBy(x=>x.Name,Identity)
  .Where(g=>query.Length==0||g.Key.Contains(query,StringComparison.CurrentCultureIgnoreCase))
  .OrderBy(g=>g.Key,StringComparer.CurrentCultureIgnoreCase)
  .Select(g=>new ArtistCard{Name=g.Key,Cover=g.Select(x=>x.Track.Cover).FirstOrDefault(System.IO.File.Exists),Albums=g.Select(x=>x.Track.AlbumKey).Distinct().Count(),Tracks=g.Count()}).ToList();
}

public sealed class ArtistRequestedEventArgs(string artist):RoutedEventArgs(ArtistLinks.ArtistRequestedEvent)
{
 public string Artist {get;}=artist;
}

public sealed class ArtistLinks:TextBlock
{
 public static readonly RoutedEvent ArtistRequestedEvent=EventManager.RegisterRoutedEvent("ArtistRequested",RoutingStrategy.Bubble,typeof(RoutedEventHandler),typeof(ArtistLinks));
 public static readonly DependencyProperty NamesProperty=DependencyProperty.Register(nameof(Names),typeof(string),typeof(ArtistLinks),new PropertyMetadata("",Changed));
 public static readonly DependencyProperty SuffixProperty=DependencyProperty.Register(nameof(Suffix),typeof(string),typeof(ArtistLinks),new PropertyMetadata("",Changed));
 public static readonly DependencyProperty AllowedNamesProperty=DependencyProperty.Register(nameof(AllowedNames),typeof(IReadOnlySet<string>),typeof(ArtistLinks),new PropertyMetadata(null,Changed));
 public string Names {get=>(string)GetValue(NamesProperty);set=>SetValue(NamesProperty,value);}
 public string Suffix {get=>(string)GetValue(SuffixProperty);set=>SetValue(SuffixProperty,value);}
 public IReadOnlySet<string>? AllowedNames{get=>(IReadOnlySet<string>?)GetValue(AllowedNamesProperty);set=>SetValue(AllowedNamesProperty,value);}
 static void Changed(DependencyObject sender,DependencyPropertyChangedEventArgs e)=>((ArtistLinks)sender).Rebuild();
 void Rebuild()
 {
  Inlines.Clear();bool first=true;
  foreach(var credit in ArtistCatalog.Credits(Names))
  {
   string name=credit.Profile;
   if(!first)Inlines.Add(new Run(" / "));first=false;
   if(AllowedNames!=null&&!AllowedNames.Contains(name)){Inlines.Add(new Run(credit.Label));continue;}
   var link=new Hyperlink(new Run(credit.Label)){Style=(Style)FindResource("ArtistHyperlink"),ToolTip=name+" sanatçısını aç"};
   System.Windows.Automation.AutomationProperties.SetName(link,name+" sanatçısını aç");
   link.Click+=(_,e)=>{e.Handled=true;RaiseEvent(new ArtistRequestedEventArgs(name));};Inlines.Add(link);
  }
  if(!string.IsNullOrEmpty(Suffix))Inlines.Add(new Run(Suffix));
 }
}

public partial class MainWindow
{
 void OpenArtist(string name)
 {
  if(string.IsNullOrWhiteSpace(name)||ArtistCatalog.ResolveProfile(library,name.Trim()) is not {} profile)return;
  if(fullscreenPlayer)ExitFullscreenPlayer();artistName=profile;view="artist";
  if(Search.Text.Length>0)Search.Text="";else RefreshView();
  if(Albums.Items.Count>0)Albums.ScrollIntoView(Albums.Items[0]);
 }
 void ArtistLinkRequested(object sender,RoutedEventArgs e)
 {
  if(e is not ArtistRequestedEventArgs artist)return;e.Handled=true;
  if(sender is Window window&&window!=this)window.Close();
  OpenArtist(artist.Artist);Activate();
 }
 static bool IsArtistLinkSource(object source)
 {
  for(DependencyObject? current=source as DependencyObject;current!=null;)
  {
   if(current is ArtistLinks or Hyperlink or StarRating or System.Windows.Controls.Primitives.ButtonBase)return true;
   current=current is FrameworkContentElement content?content.Parent:current is System.Windows.Media.Visual?System.Windows.Media.VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current);
  }
  return false;
 }
}
