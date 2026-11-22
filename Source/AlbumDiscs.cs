using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;
using System.Globalization;

namespace Faemyr;

public static class AlbumDiscs
{
 static readonly Regex DiscFolder=new(@"^(?:cd|disc|disk)[\s._-]*(\d+)(?:\s+of\s+\d+)?$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
 public static uint Number(Track track)
 {
  if(track.Disc>0)return track.Disc;
  string? folder=Path.GetDirectoryName(track.AudioPath);
  var match=DiscFolder.Match(Path.GetFileName(folder)??"");
  return match.Success&&uint.TryParse(match.Groups[1].Value,out uint number)&&number>0?number:1;
 }
 public static IOrderedEnumerable<Track> Order(IEnumerable<Track> tracks)=>tracks.OrderBy(t=>t.AlbumDisc).ThenBy(t=>t.Number).ThenBy(t=>t.Title,StringComparer.CurrentCultureIgnoreCase);
 public static bool Multiple(IEnumerable<Track> tracks)=>tracks.Select(t=>t.AlbumDisc).Distinct().Skip(1).Any();

 // Group the collection view, not its underlying track list. This preserves selection,
 // track commands and the existing playback queue without synthetic header tracks.
 public static void Bind(DataGrid grid,List<Track> tracks,bool grouped)
 {
  grid.Items.GroupDescriptions.Clear();
  grid.ItemsSource=tracks;
  if(grouped)
  {
   var grouping=new PropertyGroupDescription(nameof(Track.AlbumDisc));
   grouping.SortDescriptions.Add(new SortDescription(nameof(CollectionViewGroup.Name),ListSortDirection.Ascending));
   grid.Items.GroupDescriptions.Add(grouping);
  }
 }
}

public sealed class AlbumDiscHeadingConverter : IValueConverter
{
 public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>L.T("Disk")+" "+value;
 public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
