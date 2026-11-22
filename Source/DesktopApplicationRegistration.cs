using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Faemyr;
// The Windows media flyout resolves unpackaged app branding through the
// Start menu's shortcut catalog. Keep its automatic EXE identity so existing
// taskbar pins continue to match, rather than changing the process AppUserModelID.
public static class DesktopApplicationRegistration
{
 public sealed record Result(bool Registered,bool Changed,string Shortcut,string? Error=null);
 [ComImport,Guid("00021401-0000-0000-C000-000000000046")] class ShellLinkObject{}
 [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IShellLink
 {
  void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int count,IntPtr findData,uint flags);
  void GetIDList(out IntPtr list);void SetIDList(IntPtr list);
  void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder text,int count);
  void SetDescription([MarshalAs(UnmanagedType.LPWStr)]string text);
  void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int count);
  void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)]string path);
  void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder arguments,int count);
  void SetArguments([MarshalAs(UnmanagedType.LPWStr)]string arguments);
  void GetHotkey(out short key);void SetHotkey(short key);void GetShowCmd(out int command);void SetShowCmd(int command);
  void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int count,out int index);
  void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)]string path,int index);
  void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)]string path,uint reserved);void Resolve(IntPtr window,uint flags);
  void SetPath([MarshalAs(UnmanagedType.LPWStr)]string path);
 }
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern void SHChangeNotify(uint change,uint flags,[MarshalAs(UnmanagedType.LPWStr)]string path,IntPtr other);
 public static Result RegisterCurrent()
 {
  string executable=Environment.ProcessPath??Path.Combine(AppContext.BaseDirectory,"Faemyr.exe");
  string programs=Environment.GetFolderPath(Environment.SpecialFolder.Programs);
  var result=Register(programs,executable,true);
  try{PlayerJson.Write(Path.Combine(Paths.Data,"windows-app-registration.json"),result);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
  return result;
 }
 public static Result Register(string programs,string executable,bool notify=false)
 {
  string shortcut="";object? native=null;
  try
  {
   if(string.IsNullOrWhiteSpace(programs)||!Path.IsPathFullyQualified(programs)||!Path.IsPathFullyQualified(executable)||!File.Exists(executable))return new(false,false,shortcut,"The application or Programs folder is unavailable.");
   executable=Path.GetFullPath(executable);string folder=Path.Combine(programs,"Faemyr");shortcut=Path.Combine(folder,"Faemyr.lnk");
   native=new ShellLinkObject();var link=(IShellLink)native;var persist=(IPersistFile)native;
   bool existed=File.Exists(shortcut);
   if(existed)
   {
    try
    {
     persist.Load(shortcut,0);var target=new StringBuilder(32768);var icon=new StringBuilder(32768);var args=new StringBuilder(32768);
     link.GetPath(target,target.Capacity,IntPtr.Zero,4);link.GetIconLocation(icon,icon.Capacity,out int index);link.GetArguments(args,args.Capacity);
     if(target.ToString().Equals(executable,StringComparison.OrdinalIgnoreCase)&&icon.ToString().Equals(executable,StringComparison.OrdinalIgnoreCase)&&index==0&&args.Length==0)return new(true,false,shortcut);
    }
    catch(COMException){/* Repair an incomplete shortcut in Faemyr's own folder. */}
   }
   Directory.CreateDirectory(folder);link.SetPath(executable);link.SetArguments("");link.SetWorkingDirectory(Path.GetDirectoryName(executable)!);link.SetDescription("Faemyr");link.SetIconLocation(executable,0);link.SetShowCmd(1);persist.Save(shortcut,true);
   if(notify)SHChangeNotify(existed?0x00002000u:0x00000002u,0x0005,shortcut,IntPtr.Zero); // UPDATEITEM / CREATE, SHCNF_PATHW
   return new(true,true,shortcut);
  }
  catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or COMException or ArgumentException or NotSupportedException){return new(false,false,shortcut,ex.GetType().Name+": "+ex.Message);}
  finally{if(native!=null&&Marshal.IsComObject(native))Marshal.FinalReleaseComObject(native);}
 }
 public static (string Target,string Icon,int Index,string Arguments) Inspect(string shortcut)
 {
  object native=new ShellLinkObject();
  try{((IPersistFile)native).Load(shortcut,0);var link=(IShellLink)native;var target=new StringBuilder(32768);var icon=new StringBuilder(32768);var args=new StringBuilder(32768);link.GetPath(target,target.Capacity,IntPtr.Zero,4);link.GetIconLocation(icon,icon.Capacity,out int index);link.GetArguments(args,args.Capacity);return(target.ToString(),icon.ToString(),index,args.ToString());}
  finally{Marshal.FinalReleaseComObject(native);}
 }
}
