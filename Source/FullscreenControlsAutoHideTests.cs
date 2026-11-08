using System.Windows;
namespace Ahenk;
public static class FullscreenControlsAutoHideTests
{
 public static void Run(Action<bool,string> check)
 {
  var state=new FullscreenControlsIdleState();var zone=new Rect(0,800,1920,200);var top=new Point(500,100);var bottom=new Point(500,900);
  state.Reset(0,top);check(!state.Tick(4.999,false)&&!state.Hidden&&state.Tick(5,false)&&state.Hidden,"Fullscreen controls hide exactly after five seconds of idle time");
  check(!state.Move(new Point(700,200),zone,6)&&state.Hidden,"Mouse movement outside the bottom player region never reveals hidden controls");
  check(state.Move(bottom,zone,7)&&!state.Hidden,"Moving into the full original bottom player region reveals the controls");
  check(!state.Tick(11.999,false)&&state.Tick(12,false),"Revealing the player starts a fresh five-second idle period");
  check(!state.Move(bottom,zone,13)&&state.Hidden,"Synthetic mouse events at an unchanged position cannot reveal hidden controls");
  state.Reset(0,top);state.Move(new Point(700,150),zone,4);check(!state.Tick(8.9,false)&&state.Tick(9,false),"Any physical pointer movement delays hiding while controls are visible");
  state.Reset(0,bottom);state.Move(bottom,zone,4.9);check(state.Tick(5,false),"Stationary hovering over the player does not keep it open indefinitely");
  state.Reset(0,top);check(!state.Tick(6,true)&&!state.Tick(10.99,false)&&state.Tick(11,false),"Dragging a control or using its menu postpones automatic hiding");
  state.Reset(20,top);check(!state.Hidden&&!state.Tick(24.99,false),"Leaving and re-entering fullscreen starts with a visible player");
  state.Tick(25,false);check(!state.Move(new Point(500,750),new Rect(0,850,1200,150),26)&&state.Hidden&&state.Move(new Point(500,950),new Rect(0,850,1200,150),27),"Reveal region follows the current player bounds rather than stale screen dimensions");
  state.Reset(0,top);state.Tick(5,false);check(state.CursorHidden&&state.Hidden,"Five seconds of inactivity hide both cursor and fullscreen controls");
  state.Move(new Point(700,200),zone,6);check(!state.CursorHidden&&state.Hidden,"Movement over lyrics restores the cursor while leaving the player hidden");
  check(!state.Tick(10.999,false)&&!state.CursorHidden&&state.Tick(11,false)&&state.CursorHidden&&state.Hidden,"The cursor can hide again independently while the player remains hidden");
  state.Move(new Point(700,200),zone,12);check(state.CursorHidden,"A synthetic same-position event cannot reveal an idle cursor");
  state.Move(bottom,zone,13);check(!state.CursorHidden&&!state.Hidden,"Movement into the bottom player region restores both cursor and controls");
  state.Reset(20,top);check(!state.CursorHidden&&!state.Hidden,"Leaving fullscreen clears both idle visibility states");
 }
}
