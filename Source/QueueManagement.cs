using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace Ahenk;
public partial class MainWindow
{
 sealed record QueueUndo(Track[] Tracks,int Index,PlaybackOrderState Order);
 readonly Stack<QueueUndo> queueUndo=[];Point queueDragStart;bool queueDragAllowed;readonly string queueDragFormat="Lyra.Queue."+Guid.NewGuid();
 List<Track> SelectedTracks(){var selected=Tracks.SelectedItems.OfType<Track>().ToHashSet();return Tracks.Items.OfType<Track>().Where(selected.Contains).ToList();}
 void InitializeQueueManagement()
 {
  Tracks.SelectionMode=DataGridSelectionMode.Extended;Tracks.AllowDrop=true;
  Tracks.PreviewMouseLeftButtonDown+=(_,e)=>{queueDragStart=e.GetPosition(Tracks);queueDragAllowed=view=="queue"&&!IsArtistLinkSource(e.OriginalSource)&&ItemsControl.ContainerFromElement(Tracks,e.OriginalSource as DependencyObject) is DataGridRow;};
  Tracks.PreviewMouseMove+=(_,e)=>
  {
   if(!queueDragAllowed||e.LeftButton!=MouseButtonState.Pressed)return;var p=e.GetPosition(Tracks);if(Math.Abs(p.X-queueDragStart.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(p.Y-queueDragStart.Y)<SystemParameters.MinimumVerticalDragDistance)return;
   queueDragAllowed=false;var indices=SelectedTracks().Select(t=>queue.IndexOf(t)).Where(i=>i>=0).ToArray();if(indices.Length>0){e.Handled=true;DragDrop.DoDragDrop(Tracks,new DataObject(queueDragFormat,indices),DragDropEffects.Move);}
  };
  Tracks.DragOver+=(_,e)=>{e.Effects=view=="queue"&&e.Data.GetDataPresent(queueDragFormat)?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;};
  Tracks.Drop+=(_,e)=>
  {
   if(view!="queue"||e.Data.GetData(queueDragFormat) is not int[] indices)return;int target=ItemsControl.ContainerFromElement(Tracks,e.OriginalSource as DependencyObject) is DataGridRow row&&row.Item is Track track?queue.IndexOf(track):queue.Count;
   ChangeQueue(()=>QueueEditing.Move(queue,indices,target));e.Handled=true;
  };
  Tracks.PreviewKeyDown+=(_,e)=>{if(view=="queue"&&e.Key==Key.Z&&Keyboard.Modifiers==ModifierKeys.Control){UndoQueue();e.Handled=true;}};
 }
 void RememberQueue(){queueUndo.Push(new(queue.ToArray(),queueIndex,order.Capture()));if(queueUndo.Count>20){var keep=queueUndo.Take(20).Reverse().ToArray();queueUndo.Clear();foreach(var item in keep)queueUndo.Push(item);}}
 void ChangeQueue(Action change,Action? afterReset=null,bool remember=true)
 {
  UpdateStreamTrack();var current=queueIndex>=0&&queueIndex<queue.Count?queue[queueIndex]:null;if(remember)RememberQueue();change();queueIndex=current==null?-1:queue.IndexOf(current);if(queueIndex<0&&now!=null)queueIndex=queue.FindIndex(t=>t.Path==now.Path);order.Reset(queue.Count,queueIndex);afterReset?.Invoke();UpdateNextTrack();if(view=="queue")RefreshView();UpdateQueueTools();SaveSession(false);_=RebuildPlaybackPlan();
 }
 void UndoQueue(){if(!queueUndo.TryPop(out var saved))return;ChangeQueue(()=>{queue.Clear();queue.AddRange(saved.Tracks);},()=>{if(queueIndex==saved.Index)order.Restore(saved.Order);},remember:false);if(!audio.IsPlaying&&!audio.IsPaused)queueIndex=saved.Index;UpdateQueueTools();}
 void UndoQueue_Click(object sender,RoutedEventArgs e)=>UndoQueue();
 void UpdateQueueTools(){if(QueueTools!=null){QueueTools.Visibility=Visible(view=="queue");UndoQueueButton.IsEnabled=queueUndo.Count>0;}}
 void EnqueueTracks(IEnumerable<Track> tracks,bool next)
 {
  var additions=tracks.Select(t=>t.Copy()).ToArray();if(additions.Length==0)return;int at=next?Math.Clamp(queueIndex+1,0,queue.Count):queue.Count;
  ChangeQueue(()=>queue.InsertRange(at,additions),next?()=>order.Prioritize(Enumerable.Range(at,additions.Length)):null);Status.Text=L.T($"{additions.Length} parça ")+(next?L.T("hemen sonra çalınacak."):L.T("kuyruğa eklendi."));
 }
 void RemoveQueueSelection(){var selected=SelectedTracks().ToHashSet();ChangeQueue(()=>queue.RemoveAll(selected.Contains));}
}
public static class QueueEditing
{
 public static void Move<T>(List<T> list,IEnumerable<int> selected,int target)
 {
  var indices=selected.Where(i=>i>=0&&i<list.Count).Distinct().Order().ToArray();target=Math.Clamp(target,0,list.Count);var moving=indices.Select(i=>list[i]).ToArray();int at=target-indices.Count(i=>i<target);foreach(int i in indices.Reverse())list.RemoveAt(i);list.InsertRange(at,moving);
 }
}
