using System.IO;
using System.Text.Json;

namespace Faemyr;
public static class DesktopApplicationRegistrationTests
{
 public static void Run(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{name,passed=pass});if(!pass)throw new Exception(name);}
  try
  {
   string programs=Path.Combine(directory,"Programs");string app=Path.Combine(directory,"Taşınabilir müzik çalar");Directory.CreateDirectory(app);string executable=Path.Combine(app,"Faemyr.exe");File.Copy(Environment.ProcessPath!,executable);
   var created=DesktopApplicationRegistration.Register(programs,executable);
   Check(created.Registered&&created.Changed&&File.Exists(created.Shortcut),"A per-user Faemyr Start menu shortcut is created without an installer");
   var link=DesktopApplicationRegistration.Inspect(created.Shortcut);
   Check(link.Target==executable&&link.Arguments=="","The shortcut points to the exact portable executable without additional commands");
   Check(link.Icon==executable&&link.Index==0,"Windows receives the application's embedded Faemyr icon");
   Check(Path.GetFileNameWithoutExtension(created.Shortcut)=="Faemyr","The Windows shortcut catalog receives the display name Faemyr");
   var written=File.GetLastWriteTimeUtc(created.Shortcut);var unchanged=DesktopApplicationRegistration.Register(programs,executable);
   Check(unchanged.Registered&&!unchanged.Changed&&File.GetLastWriteTimeUtc(created.Shortcut)==written,"Normal startups do not rewrite the shortcut or repeatedly notify the shell");
   string movedFolder=Path.Combine(directory,"Moved Faemyr");Directory.CreateDirectory(movedFolder);string moved=Path.Combine(movedFolder,"Faemyr.exe");File.Copy(executable,moved);
   var updated=DesktopApplicationRegistration.Register(programs,moved);Check(updated.Registered&&updated.Changed&&DesktopApplicationRegistration.Inspect(updated.Shortcut).Target==moved,"Moving the portable app repairs the managed shortcut target");
   File.WriteAllText(created.Shortcut,"Incomplete shortcut");var repaired=DesktopApplicationRegistration.Register(programs,moved);Check(repaired.Registered&&repaired.Changed&&DesktopApplicationRegistration.Inspect(repaired.Shortcut).Target==moved,"An incomplete Faemyr shortcut is repaired");
   string blocked=Path.Combine(directory,"blocked");File.WriteAllText(blocked,"Not a directory");var unavailable=DesktopApplicationRegistration.Register(blocked,moved);Check(!unavailable.Registered&&unavailable.Error!=null,"An unavailable Programs directory cannot prevent the player from starting");
   Check(!DesktopApplicationRegistration.Register(programs,Path.Combine(app,"missing.exe")).Registered,"No shortcut is created for a missing executable");
   string unrelated=Path.Combine(programs,"Another app.lnk");File.WriteAllText(unrelated,"Unrelated user shortcut");DesktopApplicationRegistration.Register(programs,moved);Check(File.ReadAllText(unrelated)=="Unrelated user shortcut","Other Start menu entries are preserved");
  }
  finally{File.WriteAllText(Path.Combine(directory,"app-registration-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
