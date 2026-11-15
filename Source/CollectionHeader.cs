using System.Windows;
using System.Windows.Controls;

namespace Ahenk;
public partial class MainWindow
{
 void UpdateCollectionHeaderLayout()
 {
  bool compact=view is "albums" or "artists";
  if(compact)SectionTitle.Visibility=Visibility.Collapsed;
  CollectionToolbar.Visibility=Visible(!compact&&view is not "album" and not "playlist");
  // Reuse the existing selector so its selection and event handler remain the
  // same when navigating between the collection and an artist's album section.
  var host=view=="albums"?PageHeader:CollectionToolbar;
  if(!ReferenceEquals(AlbumSortTools.Parent,host))
  {
   (AlbumSortTools.Parent as Panel)?.Children.Remove(AlbumSortTools);
   host.Children.Add(AlbumSortTools);
  }
  Grid.SetColumn(AlbumSortTools,1);
  AlbumSortTools.HorizontalAlignment=HorizontalAlignment.Right;
  AlbumSortTools.VerticalAlignment=VerticalAlignment.Center;
 }
}
