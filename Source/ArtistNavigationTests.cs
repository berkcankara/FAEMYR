using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.IO;
using System.Text.Json;

namespace Ahenk;
public static class ArtistNavigationTests
{
 public static void Run(Action<bool,string> check)
 {
  check(ArtistCatalog.Split("AC/DC / Simon & Garfunkel").SequenceEqual(new[]{"AC/DC","Simon & Garfunkel"}),"Artist links preserve slash band names and ampersands");
  check(ArtistCatalog.Split(" Ada / Deniz / ada ").SequenceEqual(new[]{"Ada","Deniz"}),"Multi-artist labels link individual unique names");
  var guest=new Track{Artist="Guest / Ada",AlbumArtist="Label",Album="Collection",Fields=[new("Vorbis / ARTIST","Guest; Ada"),new("Vorbis / MUSICBRAINZ_ARTISTID","identifier")]};
  var other=new Track{Artist="Other",AlbumArtist="Label",Album="Collection"};var unrelated=new Track{Artist="Guesthouse",AlbumArtist="Other",Album="Elsewhere"};
  check(ArtistCatalog.Matches(guest,"guest")&&!ArtistCatalog.Matches(unrelated,"Guest"),"Artist profiles match exact performer names independently of album artist");
  check(!ArtistCatalog.Matches(guest,"Label")&&!ArtistCatalog.Names(guest).Contains("identifier"),"Album-only names and artist IDs do not create artist profiles");
  var tracks=new[]{guest,other,unrelated};var albums=ArtistCatalog.AlbumKeys(tracks,"Guest");check(tracks.Count(t=>albums.Contains(t.AlbumKey))==2,"Guest artist profile retains complete participating albums");
  check(ArtistCatalog.IsArtistField("Sanatçı")&&!ArtistCatalog.IsArtistField("APE / Album Artist")&&!ArtistCatalog.IsArtistField("Vorbis / MUSICBRAINZ_ALBUMARTISTID"),"Only performer metadata values become navigation links");
  guest.Fields.Add(new("Vorbis / ALBUMARTIST","Album-only alias"));
  check(!ArtistCatalog.Names(guest).Contains("Album-only alias")&&ArtistCatalog.ProfileNames(tracks).SetEquals(new[]{"Guest","Other","Guesthouse"}),"Album-only fields and slash guest credits do not add separate profiles");
  var linkin=new Track{Artist="Linkin Park",AlbumArtist="Linkin Park",Album="Minutes to Midnight",Fields=[
   new("Vorbis / ARTIST","Linkin Park"),new("Vorbis / PERFORMER","Charlie Bisharat (violin)"),
   new("Vorbis / PERFORMER","Chester Bennington (vocals)"),
   new("Kapsayıcı / PERFORMER","Charlie Bisharat (violin);Chester Bennington (vocals)"),
   new("Vorbis / performer","Mike Shinoda (vocals)"),new("Vorbis / ALBUMARTIST","Linkin Park")]};
  check(ArtistCatalog.Names(linkin).SequenceEqual(new[]{"Linkin Park"}),"Minutes to Midnight session credits and combined container credits do not create artist profiles");
  check(!ArtistCatalog.Matches(linkin,"Charlie Bisharat (violin)")&&!ArtistCatalog.Matches(linkin,"Chester Bennington (vocals)"),"Session musicians cannot open a profile through artist matching");
  check(linkin.Fields.Count==6&&linkin.Fields.Where(f=>f.Key.Contains("PERFORMER",StringComparison.OrdinalIgnoreCase)).All(f=>!f.IsArtist),"All original performer credits remain available as plain metadata");
  var solo=new Track{Artist="Mike Shinoda",AlbumArtist="Mike Shinoda",Album="Solo"};
  check(ArtistCatalog.ProfileNames(new[]{linkin,solo}).SetEquals(new[]{"Linkin Park","Mike Shinoda"})&&ArtistCatalog.AlbumKeys(new[]{linkin,solo},"Mike Shinoda").SetEquals(new[]{solo.AlbumKey}),"A musician with an actual artist release retains only their own artist profile and albums");
  check(!ArtistCatalog.IsArtistField("Vorbis / PERFORMER")&&!ArtistCatalog.IsArtistField("Kapsayıcı / performer")&&ArtistCatalog.IsArtistField("Kapsayıcı / ARTIST"),"Only recording-artist fields are linked regardless of tag casing or source");
  var featureLabels=new[]{"Ada feat. Deniz","Ada FEAT Deniz","Ada ft. Deniz","Ada Ft Deniz","Ada featuring Deniz","Ada (feat. Deniz)","Ada [ft. Deniz]","Ada feat. Deniz / Guest; Other"};
  check(featureLabels.All(label=>ArtistCatalog.Credits(label).Select(c=>c.Profile).SequenceEqual(new[]{"Ada"})),"Feature spellings, brackets, casing and guest separators share the same main-artist identity");
  check(ArtistCatalog.Credits("Little Feat").Single().Profile=="Little Feat"&&ArtistCatalog.Credits("AC/DC / Ada feat. Guest").Single().Profile=="AC/DC","Internal slash in a band name survives while spaced slash credits use the leading artist");
  var ampersandLabels=new[]{"Röyksopp & Alison Goldfrapp","Röyksopp&Robyn","Röyksopp & Astrid S & Other","Röyksopp & Guest / Other; Another","Röyksopp & Guest feat. Other","Röyksopp feat. Guest & Other"};
  check(ampersandLabels.All(label=>ArtistCatalog.Credits(label).Single().Profile=="Röyksopp"),"Ampersand credits consistently merge into the leading artist with or without spaces and mixed feature markers");
  check(ampersandLabels.All(label=>ArtistCatalog.Credits(label).Single().Label==label),"Ampersand and featured credit text remains intact for display");
  var royksopp=new Track{Artist="Röyksopp",AlbumArtist="Röyksopp",Album="Profound Mysteries"};var royksoppRemix=new Track{Artist="Röyksopp & Alison Goldfrapp",AlbumArtist="Röyksopp",Album="Profound Mysteries Remixes"};var goldfrapp=new Track{Artist="Alison Goldfrapp",AlbumArtist="Alison Goldfrapp",Album="Solo"};var ampersandReleases=new[]{royksopp,royksoppRemix,goldfrapp};
  check(ArtistCatalog.Cards(ampersandReleases).Count==2&&ArtistCatalog.Cards(ampersandReleases).Single(c=>c.Name=="Röyksopp") is {Albums:2,Tracks:2},"Ampersand collaborations no longer create extra artist cards and merge album/track counts");
  check(ArtistCatalog.AlbumKeys(ampersandReleases,"Röyksopp").SetEquals(new[]{royksopp.AlbumKey,royksoppRemix.AlbumKey})&&ArtistCatalog.AlbumKeys(ampersandReleases,"Alison Goldfrapp").SetEquals(new[]{goldfrapp.AlbumKey}),"Guest solo releases remain independent while ampersand credits belong to the main artist");
  check(ArtistCatalog.Names(new Track{Fields=[new("Vorbis / ARTIST","Other Artist & Guest")]}).SequenceEqual(new[]{"Other Artist"})&&ArtistCatalog.ResolveProfile(ampersandReleases,"RÖYKSOPP & Alison Goldfrapp")=="Röyksopp","Ampersand normalization applies to raw tag fallback and navigation aliases across all artists");
  check(ArtistCatalog.Credits("&ME").Single().Profile=="&ME"&&ArtistCatalog.Credits("Ada").Single().Profile=="Ada","Leading ampersand and unfeatured artist names remain valid identities");
  var featured=new Track{Artist="Ada feat. Deniz",AlbumArtist="Ada",Album="One",Fields=[new("Vorbis / ARTIST","Ada feat. Deniz"),new("Kapsayıcı / ARTIST","Ada feat. Deniz")]};
  var plain=new Track{Artist="Ada",AlbumArtist="Ada",Album="Two"};var deniz=new Track{Artist="Deniz",AlbumArtist="Deniz",Album="Solo"};
  var releases=new[]{featured,plain,deniz};var artistCards=ArtistCatalog.Cards(releases);
  check(artistCards.Count==2&&artistCards.Single(c=>c.Name=="Ada").Albums==2&&artistCards.Single(c=>c.Name=="Ada").Tracks==2,"Plain and featured releases merge into one main-artist card with complete album and track counts");
  check(ArtistCatalog.AlbumKeys(releases,"Ada").SetEquals(new[]{featured.AlbumKey,plain.AlbumKey})&&ArtistCatalog.AlbumKeys(releases,"Deniz").SetEquals(new[]{deniz.AlbumKey}),"Guest credits join the main artist while a guest's own artist releases stay separate");
  check(ArtistCatalog.ResolveProfile(releases,"ADA FEAT. Deniz")=="Ada"&&ArtistCatalog.ResolveProfile(releases,"Missing feat. Deniz")==null,"Navigation resolves feature aliases to the existing main profile without falling through to a guest");
  check(ArtistCatalog.Cards(releases,"Ada feat").Count==0&&ArtistCatalog.Cards(releases,"Ada").Single().Tracks==2,"Artist search uses merged profile names and retains complete counts");
  var repeated=new Track{Artist="Daft Punk / Pharrell Williams / Nile Rodgers",Fields=[new("Vorbis / ARTIST","Daft Punk"),new("Vorbis / ARTIST","Pharrell Williams"),new("Kapsayıcı / ARTIST","Daft Punk;Pharrell Williams;Nile Rodgers")]};
  check(ArtistCatalog.Names(repeated).SequenceEqual(new[]{"Daft Punk"}),"Joined slash guest credits do not create profiles through repeated raw artist fields");
  check(ArtistCatalog.Names(new Track{Fields=[new("Vorbis / ARTIST","Ada ft. Deniz")]}).SequenceEqual(new[]{"Ada"}),"Missing canonical artist falls back to normalized ARTIST metadata");
  check(ArtistCatalog.Cards(new[]{new Track{Artist="BUCK-TICK"},new Track{Artist="Buck-Tick"}}).Single().Tracks==2,"Artist identity casing stays consistent even under Turkish culture");
  var albumLabel=new ArtistLinks{Names="Ada / Label",AllowedNames=new HashSet<string>(["ada"],StringComparer.CurrentCultureIgnoreCase)};
  check(albumLabel.Inlines.OfType<Hyperlink>().Count()==1&&string.Concat(albumLabel.Inlines.OfType<Hyperlink>().Single().Inlines.OfType<Run>().Select(run=>run.Text))=="Ada / Label","Album labels preserve the full credit and link only the existing leading artist");
  albumLabel.AllowedNames=new HashSet<string>(["Label"],StringComparer.CurrentCultureIgnoreCase);
  check(!albumLabel.Inlines.OfType<Hyperlink>().Any()&&albumLabel.Inlines.OfType<Run>().Single().Text=="Ada / Label","Guest-only allowed names cannot turn the slash credit into a guest profile link");
  var host=new StackPanel();var links=new ArtistLinks{Names="Ada / Deniz",Suffix=" · 4 parça"};host.Children.Add(links);
  string requested="";int requests=0;host.AddHandler(ArtistLinks.ArtistRequestedEvent,new RoutedEventHandler((_,e)=>{requested=((ArtistRequestedEventArgs)e).Artist;requests++;}));
  links.Inlines.OfType<Hyperlink>().Last().RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
  check(requested=="Ada"&&requests==1,"Slash credit raises one navigation event to the leading artist through its parent");
  check(links.Inlines.OfType<Hyperlink>().Count()==1&&links.Inlines.LastInline is Run suffix&&suffix.Text==" · 4 parça","Album counts remain plain text beside the leading artist link");
  links.Names="Guest";var updatedLinks=links.Inlines.OfType<Hyperlink>().ToArray();string updatedName=string.Concat(updatedLinks.SelectMany(link=>link.Inlines.OfType<Run>()).Select(run=>run.Text));check(updatedLinks.Length==1&&updatedName=="Guest"&&links.Inlines.LastInline is Run tail&&tail.Text==" · 4 parça","Updated player artist replaces old links without stale names: "+updatedName);
  links.Names="Ada feat. Deniz";var featureLink=links.Inlines.OfType<Hyperlink>().Single();featureLink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
  check(requested=="Ada"&&string.Concat(featureLink.Inlines.OfType<Run>().Select(run=>run.Text))=="Ada feat. Deniz","Player, album, table and metadata links preserve full credit text while requesting the main profile");
  links.AllowedNames=new HashSet<string>(["Ada"],StringComparer.OrdinalIgnoreCase);
  check(links.Inlines.OfType<Hyperlink>().Count()==1&&featured.Artist=="Ada feat. Deniz"&&featured.Fields[0].Value==featured.Artist,"Allowed album links use the normalized identity without rewriting track credits");
  links.AllowedNames=null;links.Names="Röyksopp & Alison Goldfrapp";var ampLink=links.Inlines.OfType<Hyperlink>().Single();ampLink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));check(requested=="Röyksopp"&&string.Concat(ampLink.Inlines.OfType<Run>().Select(run=>run.Text))=="Röyksopp & Alison Goldfrapp"&&royksoppRemix.Artist==links.Names,"Shared player, album and metadata artist links navigate to the main profile without rewriting tags");
  var slashLabels=new[]{"Sidewalks and Skeletons / Goo Munday","Sidewalks and Skeletons / CASHFORGOLD / Ferngazer","Sidewalks and Skeletons/ Goo Munday","Sidewalks and Skeletons /Goo Munday","Sidewalks and Skeletons / Guest & Other","Sidewalks and Skeletons feat. Guest / Other"};
  check(slashLabels.All(label=>ArtistCatalog.Credits(label).Single() is {Profile:"Sidewalks and Skeletons"}&&ArtistCatalog.Credits(label).Single().Label==label),"Spaced slash variations and mixed markers use the main identity without altering displayed credits");
  var sidewalk=new Track{Artist="Sidewalks and Skeletons / Goo Munday",AlbumArtist="Sidewalks and Skeletons",Album="Exorcism"};var sidewalkSolo=new Track{Artist="Sidewalks and Skeletons",AlbumArtist="Sidewalks and Skeletons",Album="White Light"};var gooSolo=new Track{Artist="Goo Munday",AlbumArtist="Goo Munday",Album="Solo"};var sidewalkTracks=new[]{sidewalk,sidewalkSolo,gooSolo};
  check(ArtistCatalog.Cards(sidewalkTracks).Count==2&&ArtistCatalog.Cards(sidewalkTracks).Single(c=>c.Name=="Sidewalks and Skeletons") is {Albums:2,Tracks:2}&&ArtistCatalog.AlbumKeys(sidewalkTracks,"Goo Munday").SetEquals(new[]{gooSolo.AlbumKey}),"Slash guests merge into the host profile while their own solo releases stay independent");
  check(ArtistCatalog.Names(new Track{Fields=[new("Vorbis / ARTIST","Other Artist / Guest")]}).SequenceEqual(new[]{"Other Artist"})&&ArtistPhotoStore.Identity("Sidewalks and Skeletons / Goo Munday")=="Sidewalks and Skeletons","Raw artist fallback and artist photo lookups share slash normalization across the app");
  links.Names="Sidewalks and Skeletons / Goo Munday";var slashLink=links.Inlines.OfType<Hyperlink>().Single();slashLink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));check(requested=="Sidewalks and Skeletons"&&string.Concat(slashLink.Inlines.OfType<Run>().Select(run=>run.Text))==links.Names,"Slash credits in player, album, track and metadata links open the host profile");
 }
 public static void RunArchive(string directory,Action<bool,string> check)
 {
  string path=Path.Combine(directory,"artist-archive.json");if(!File.Exists(path))return;
  var tracks=JsonSerializer.Deserialize<List<Track>>(File.ReadAllText(path))!;var cards=ArtistCatalog.Cards(tracks);
  check(cards.All(c=>!System.Text.RegularExpressions.Regex.IsMatch(c.Name,@"\s(?:feat\.?|ft\.?|featuring)\s",System.Text.RegularExpressions.RegexOptions.IgnoreCase)),"Actual archive has no separate featured-credit profile cards");
  check(cards.All(c=>!System.Text.RegularExpressions.Regex.IsMatch(c.Name,@"\S\s*&\s*\S")),"Actual archive has no compound ampersand-credit profile cards for any artist");
  var sidewalkTracks=tracks.Where(t=>t.Artist.StartsWith("Sidewalks and Skeletons",StringComparison.OrdinalIgnoreCase)).ToList();check(cards.Single(c=>c.Name=="Sidewalks and Skeletons").Tracks==sidewalkTracks.Count&&cards.Single(c=>c.Name=="Sidewalks and Skeletons").Albums==sidewalkTracks.Select(t=>t.AlbumKey).Distinct().Count(),"Actual Sidewalks and Skeletons profile includes every slash-credit Exorcism track");
  check(!ArtistCatalog.ProfileNames(tracks.Where(t=>t.AlbumArtist=="Sidewalks and Skeletons")).Any(n=>n!="Sidewalks and Skeletons"),"Sidewalks and Skeletons albums create no guest profiles anywhere in the archive");
  check(cards.Single(c=>c.Name=="Howard Shore") is {Albums:6,Tracks:217},"Actual archive merges all Howard Shore credits into six albums and 217 tracks");
  check(ArtistCatalog.ResolveProfile(tracks,"Linkin Park feat. Rakim")=="Linkin Park"&&ArtistCatalog.ResolveProfile(tracks,"Röyksopp feat. Robyn")=="Röyksopp", "Actual archive applies the same identity and navigation rule across different artists");
  check(cards.All(c=>!c.Name.StartsWith("Daft Punk;",StringComparison.OrdinalIgnoreCase))&&cards.Count(c=>c.Name.Equals("BUCK-TICK",StringComparison.OrdinalIgnoreCase))==1,"Actual archive removes joined container aliases and case-only duplicate cards");
 }
}
