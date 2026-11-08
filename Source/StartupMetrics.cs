using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace Ahenk;
// Opt-in diagnostic mode. Normal launches neither collect nor write measurements.
public static class StartupMetrics
{
 static readonly Stopwatch clock=new();
 static readonly Dictionary<string,double> stages=[];
 static string? output;
 public static bool Enabled=>output!=null;
 public static void Start(string path){output=Path.GetFullPath(path);clock.Restart();Mark("startup");}
 public static void Mark(string name){if(!Enabled)return;stages[name]=Math.Round(clock.Elapsed.TotalMilliseconds,1);Directory.CreateDirectory(Path.GetDirectoryName(output!)!);File.WriteAllText(output+".progress.json",JsonSerializer.Serialize(stages));}
 public static void Fail(Exception error){if(Enabled)File.WriteAllText(output+".error.txt",error.ToString());}
 public static void Finish(Window window,int tracks)
 {
  if(!Enabled)return;
  window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,()=>
  {
   Mark("libraryInteractive");var process=Process.GetCurrentProcess();process.Refresh();
   Directory.CreateDirectory(Path.GetDirectoryName(output!)!);
   File.WriteAllText(output!,JsonSerializer.Serialize(new{tracks,stages,workingSetBytes=process.WorkingSet64,privateBytes=process.PrivateMemorySize64},new JsonSerializerOptions{WriteIndented=true}));
   window.Close();
  });
 }
}
