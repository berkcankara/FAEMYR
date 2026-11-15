using System.Windows;

namespace Ahenk;
public partial class MainWindow
{
 void InitializePlayerBoxAlignment()
 {
  SidebarBorder.SizeChanged+=(_,_)=>UpdatePlayerBoxAlignment();
  DetailPanel.SizeChanged+=(_,_)=>UpdatePlayerBoxAlignment();
  PlayerPanel.SizeChanged+=(_,_)=>UpdatePlayerBoxAlignment();
 }

 void UpdatePlayerBoxAlignment()
 {
  var preferences=InterfaceSettings.Current;
  double leftWidth,rightWidth;
  if(fullscreenPlayer)
  {
   // The fullscreen player has no side panels; retain its manual box sizing.
   leftWidth=rightWidth=Math.Max(preferences.PlayerSideWidth,preferences.PlaySize+212);
   PlayerPanel.Padding=new Thickness(16,12,16,12);
   TransportBox.Padding=new Thickness(10,6,10,6);
   VolumeControls.Margin=new Thickness(8,1,8,0);
  }
  else
  {
   // Include the separator's one-pixel border in the surrounding gutter.
   const double gutter=8;
   var edge=PlayerPanel.BorderThickness;
   PlayerPanel.Padding=new Thickness(Math.Max(0,gutter-edge.Left),Math.Max(0,gutter-edge.Top),Math.Max(0,gutter-edge.Right),Math.Max(0,gutter-edge.Bottom));
   leftWidth=SidebarBorder.ActualWidth>0?SidebarBorder.ActualWidth:preferences.SidebarWidth-SidebarBorder.Margin.Left-SidebarBorder.Margin.Right;
   double maximum=Math.Clamp((ActualWidth>0?ActualWidth:Width)-preferences.SidebarWidth-500,280,640);
   rightWidth=DetailPanel.IsVisible&&!detailsSliding&&DetailPanel.ActualWidth>0?DetailPanel.ActualWidth:
    Math.Clamp(preferences.RightPanelWidth??panelPreferences.Width,280,maximum)-DetailPanel.Margin.Left-DetailPanel.Margin.Right;
   TransportBox.Padding=new Thickness(6,6,6,6);
   VolumeControls.Margin=new Thickness(0,1,0,0);
  }
  SetWidth(TransportColumn,Math.Max(0,leftWidth));
  SetWidth(SignalColumn,preferences.ShowSignal?Math.Max(0,rightWidth):0);
  static void SetWidth(System.Windows.Controls.ColumnDefinition column,double width)
  {
   if(!column.Width.IsAbsolute||Math.Abs(column.Width.Value-width)>.1)column.Width=new GridLength(width);
  }
 }
}
