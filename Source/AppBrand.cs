using System.Diagnostics;
using System.IO;

namespace Faemyr;

internal static class AppBrand
{
 public const string Name="Faemyr";
 public const string Executable="Faemyr.exe";
 public const string ShortcutIcon="Faemyr-brand-v2.ico";
 public static string Version=>typeof(AppBrand).Assembly.GetName().Version?.ToString(3)??"0.10.53";
 public static bool ForwardLegacyLaunch(IEnumerable<string> arguments)
 {
  if(!string.Equals(Path.GetFileName(Environment.ProcessPath),"Lyra.exe",StringComparison.OrdinalIgnoreCase))return false;
  string canonical=Path.Combine(AppContext.BaseDirectory,Executable);if(!File.Exists(canonical))return false;
  var start=new ProcessStartInfo(canonical){UseShellExecute=false,WorkingDirectory=AppContext.BaseDirectory};
  foreach(var argument in arguments)start.ArgumentList.Add(argument);
  try{using var child=Process.Start(start);return child!=null;}catch(System.ComponentModel.Win32Exception){return false;}
 }
 public static bool Offline(string feature)=>(Environment.GetEnvironmentVariable("FAEMYR_"+feature+"_OFFLINE")??Environment.GetEnvironmentVariable("LYRA_"+feature+"_OFFLINE"))=="1";
}
