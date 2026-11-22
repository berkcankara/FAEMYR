using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace Faemyr;
// WPF reports native Explorer failures through its public Shell trace source.
// Keep those failures observable instead of counting managed button objects as
// proof that the thumbnail toolbar was installed by Windows.
sealed class TaskbarDiagnostics : TraceListener,IDisposable
{
 readonly List<string> errors=[];readonly Window window;readonly SourceLevels previous;
 public TaskbarDiagnostics(Window owner)
 {
  window=owner;var source=PresentationTraceSources.ShellSource;previous=source.Switch.Level;source.Listeners.Add(this);source.Switch.Level=SourceLevels.All;
 }
 public override void Write(string? message){}
 public override void WriteLine(string? message){}
 public override void TraceEvent(TraceEventCache? cache,string source,TraceEventType type,int id,string? message)=>Record(type,message);
 public override void TraceEvent(TraceEventCache? cache,string source,TraceEventType type,int id,string? format,params object?[]? args)=>Record(type,args is {Length:>0}?string.Format(System.Globalization.CultureInfo.InvariantCulture,format??"",args):format);
 void Record(TraceEventType type,string? message){if(type is TraceEventType.Error or TraceEventType.Critical or TraceEventType.Warning){lock(errors)errors.Add(message??type.ToString());}}
 public void Snapshot()
 {
  try
  {
   string[] messages;lock(errors)messages=errors.ToArray();bool? connected=typeof(Window).GetField("_taskbarList",BindingFlags.Instance|BindingFlags.NonPublic) is {} field?field.GetValue(window)!=null:null;
   Directory.CreateDirectory(Paths.Data);File.WriteAllText(Path.Combine(Paths.Data,"taskbar-status.json"),JsonSerializer.Serialize(new{version=typeof(MainWindow).Assembly.GetName().Version?.ToString(),date=DateTimeOffset.Now,connected,buttons=window.TaskbarItemInfo?.ThumbButtonInfos.Count??0,errors=messages},new JsonSerializerOptions{WriteIndented=true}));
  }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
 }
 public new void Dispose(){PresentationTraceSources.ShellSource.Listeners.Remove(this);PresentationTraceSources.ShellSource.Switch.Level=previous;base.Dispose();}
}
public partial class MainWindow
{
 TaskbarDiagnostics? taskbarDiagnostics;
 void InitializeTaskbarDiagnostics()
 {
  taskbarDiagnostics=new(this);
  ContentRendered+=(_,_)=>Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>
  {
   if(closed)return;var info=TaskbarItemInfo;
   // A custom-chrome window may be initialized before Explorer creates its taskbar
   // button. Reapply once after rendering; Explorer restart is handled by WPF.
   TaskbarItemInfo=null;TaskbarItemInfo=info;taskbarDiagnostics?.Snapshot();
  });
  Closed+=(_,_)=>{taskbarDiagnostics?.Dispose();taskbarDiagnostics=null;};
 }
}
