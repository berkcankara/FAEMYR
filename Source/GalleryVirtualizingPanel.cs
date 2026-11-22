using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Faemyr;

// The section heading adds a known amount to a row. The default
// VirtualizingStackPanel estimates unseen variable-height rows and revises
// those estimates during reverse scrolling, which can move the offset
// backwards. Keep exact offsets while still recycling off-screen containers.
public sealed class GalleryVirtualizingPanel : VirtualizingPanel, IScrollInfo
{
 public bool ArtistGallery{get;set;}
 double[] offsets=[0];
 bool rowsChanged=true;
 (double Row,double Heading) measuredHeights;
 double extentWidth,extentHeight,viewportWidth,viewportHeight,verticalOffset;
 ScrollViewer? owner;
 public GalleryVirtualizingPanel(){ClipToBounds=true;}
 double FontSize(int size)=>TryFindResource("UiFont"+size) is double value?value:size;
 int Columns=>TryFindResource(ArtistGallery?"UiArtistColumns":"UiAlbumColumns") is int value?Math.Max(1,value):6;
 double Line(int size)
 {
  var family=TryFindResource("UiFontFamily") as FontFamily??new FontFamily("Segoe UI");
  // Round up to leave room for device-pixel rounding and fallback glyphs.
  return Math.Ceiling(family.LineSpacing*FontSize(size))+1;
 }
 (double Row,double Heading) Heights(double width)
 {
  var margin=TryFindResource("UiCardMargin") is Thickness value?value:new Thickness(8,0,8,22);
  bool details=TryFindResource("UiCardDetails") is not Visibility.Collapsed;
  double square=Math.Max(0,width/Columns-margin.Left-margin.Right);
  return (square+margin.Top+margin.Bottom+Line(ArtistGallery?17:15)+(ArtistGallery?19:17)+(details?Line(12):0),Line(22)+17);
 }
 static bool HasHeading(object item)=>item is GalleryRow<AlbumCard> album?album.Heading.Length>0:item is GalleryRow<ArtistCard> artist&&artist.Heading.Length>0;
 int RowAt(double position)
 {
  int index=Array.BinarySearch(offsets,position);
  return Math.Clamp(index<0?~index-1:index,0,Math.Max(0,offsets.Length-2));
 }
 protected override Size MeasureOverride(Size available)
 {
  var items=ItemsControl.GetItemsOwner(this);if(items==null)return new Size();
  double width=double.IsFinite(available.Width)?Math.Max(0,available.Width):Math.Max(0,items.ActualWidth);
  var heights=Heights(width);int count=items.Items.Count;
  if(rowsChanged||heights!=measuredHeights||offsets.Length!=count+1)
  {
   if(offsets.Length!=count+1)offsets=new double[count+1];
   for(int i=0;i<count;i++)offsets[i+1]=offsets[i]+heights.Row+(HasHeading(items.Items[i])?heights.Heading:0);
   measuredHeights=heights;rowsChanged=false;
  }
  double height=offsets[count],viewport=double.IsFinite(available.Height)?Math.Max(0,available.Height):height;
  bool changed=extentHeight!=height||extentWidth!=width||viewportHeight!=viewport||viewportWidth!=width;
  extentHeight=height;extentWidth=viewportWidth=width;viewportHeight=viewport;
  double clamped=Math.Clamp(verticalOffset,0,Math.Max(0,height-viewport));changed|=clamped!=verticalOffset;verticalOffset=clamped;
  if(changed)owner?.InvalidateScrollInfo();
  if(count==0){ClearRealized(items,0,-1);return new Size(width,Math.Min(height,viewport));}
  // Keep one viewport before/after the current viewport ready for reversal.
  int first=RowAt(Math.Max(0,verticalOffset-viewport)),last=RowAt(Math.Min(height,verticalOffset+2*viewport));
  ClearRealized(items,first,last);
  var generator=ItemContainerGenerator;var start=generator.GeneratorPositionFromIndex(first);int childIndex=start.Offset==0?start.Index:start.Index+1;
  using(generator.StartAt(start,GeneratorDirection.Forward,true))
  for(int i=first;i<=last;i++,childIndex++)
  {
   var child=(UIElement)generator.GenerateNext(out bool newlyRealized);
   if(newlyRealized||childIndex>=InternalChildren.Count||!ReferenceEquals(InternalChildren[childIndex],child))
   {
    if(childIndex>=InternalChildren.Count)AddInternalChild(child);else InsertInternalChild(childIndex,child);
    generator.PrepareItemContainer(child);
   }
   child.Measure(new Size(width,offsets[i+1]-offsets[i]));
  }
  return new Size(width,Math.Min(height,viewport));
 }
 void ClearRealized(ItemsControl items,int first,int last)
 {
  var generator=ItemContainerGenerator;
  for(int i=InternalChildren.Count-1;i>=0;i--)
  {
   int index=items.ItemContainerGenerator.IndexFromContainer(InternalChildren[i]);
   if(index>=first&&index<=last)continue;
   if(index>=0)
   {
    var pos=new GeneratorPosition(i,0);
    if(generator is IRecyclingItemContainerGenerator recycling)recycling.Recycle(pos,1);else generator.Remove(pos,1);
   }
   RemoveInternalChildRange(i,1);
  }
 }
 protected override Size ArrangeOverride(Size finalSize)
 {
  var items=ItemsControl.GetItemsOwner(this);
  if(items!=null)foreach(UIElement child in InternalChildren)
  {
   int index=items.ItemContainerGenerator.IndexFromContainer(child);
   if(index>=0&&index+1<offsets.Length)child.Arrange(new Rect(0,offsets[index]-verticalOffset,finalSize.Width,offsets[index+1]-offsets[index]));
  }
  return finalSize;
 }
 protected override void OnItemsChanged(object sender,ItemsChangedEventArgs args)
 {
  rowsChanged=true;base.OnItemsChanged(sender,args);
  if(args.Action==NotifyCollectionChangedAction.Reset){if(InternalChildren.Count>0)RemoveInternalChildRange(0,InternalChildren.Count);verticalOffset=0;}
  InvalidateMeasure();
 }
 protected override void BringIndexIntoView(int index)
 {
  if(index<0||index+1>=offsets.Length)return;
  if(offsets[index]<verticalOffset)SetVerticalOffset(offsets[index]);
  else if(offsets[index+1]>verticalOffset+viewportHeight)SetVerticalOffset(offsets[index+1]-viewportHeight);
 }
 public ScrollViewer? ScrollOwner{get=>owner;set=>owner=value;}
 public bool CanHorizontallyScroll{get;set;}
 public bool CanVerticallyScroll{get;set;}=true;
 public double ExtentWidth=>extentWidth;
 public double ExtentHeight=>extentHeight;
 public double ViewportWidth=>viewportWidth;
 public double ViewportHeight=>viewportHeight;
 public double HorizontalOffset=>0;
 public double VerticalOffset=>verticalOffset;
 public void SetHorizontalOffset(double offset){}
 public void SetVerticalOffset(double offset)
 {
  if(double.IsNaN(offset))return;
  double next=Math.Clamp(offset,0,Math.Max(0,extentHeight-viewportHeight));if(next==verticalOffset)return;
  verticalOffset=next;InvalidateMeasure();owner?.InvalidateScrollInfo();
 }
 public void LineUp()=>SetVerticalOffset(verticalOffset-48);
 public void LineDown()=>SetVerticalOffset(verticalOffset+48);
 public void PageUp()=>SetVerticalOffset(verticalOffset-viewportHeight);
 public void PageDown()=>SetVerticalOffset(verticalOffset+viewportHeight);
 public void MouseWheelUp()=>SetVerticalOffset(verticalOffset-96);
 public void MouseWheelDown()=>SetVerticalOffset(verticalOffset+96);
 public void LineLeft(){} public void LineRight(){} public void PageLeft(){} public void PageRight(){} public void MouseWheelLeft(){} public void MouseWheelRight(){}
 public Rect MakeVisible(Visual visual,Rect rectangle)
 {
  if(visual==null||rectangle.IsEmpty||!IsAncestorOf(visual))return Rect.Empty;
  var position=visual.TransformToAncestor(this).TransformBounds(rectangle);
  double top=position.Top+verticalOffset,bottom=position.Bottom+verticalOffset;
  if(top<verticalOffset)SetVerticalOffset(top);else if(bottom>verticalOffset+viewportHeight)SetVerticalOffset(bottom-viewportHeight);
  return Rect.Intersect(new Rect(position.X,top-verticalOffset,position.Width,position.Height),new Rect(0,0,viewportWidth,viewportHeight));
 }
}
