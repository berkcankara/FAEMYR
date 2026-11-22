using System.Windows.Threading;
namespace Faemyr;
public partial class MainWindow
{
 readonly DispatcherTimer deviceMonitor=new(){Interval=TimeSpan.FromSeconds(4)};bool checkingDevices;
 void StartDeviceMonitoring()
 {
  deviceMonitor.Tick+=async(_,_)=>
  {
   if(checkingDevices||closed)return;checkingDevices=true;
   try
   {
    var devices=await Task.Run(AudioEngine.Devices);if(closed)return;string? active=audio.ActiveDeviceId;
    if(active!=null&&!devices.Any(d=>d.Id==active))
    {
     resumeSeconds=audio.Position.TotalSeconds;resumePrepared=now!=null;playCts?.Cancel();audio.Stop();Scope.Reset();SetSignal(L.T("Çıkış aygıtı çıkarıldı. Aygıtı bağlayıp Oynat'a basabilirsin."));SyncPlaybackVisuals(true);
    }
    var shown=DeviceChoice.Items.OfType<DeviceOption>().Where(d=>d.Id!=null&&d.Name!=L.T("Kayıtlı aygıt · bağlı değil")).Select(d=>(d.Id,d.Name)).ToArray();if(!shown.SequenceEqual(devices.Select(d=>((string?)d.Id,d.Name))))await RefreshDevicesAsync();
   }catch(Exception ex){if(!closed)Status.Text=L.T("Ses aygıtları kontrol edilemedi: ")+ex.Message;}finally{checkingDevices=false;}
  };deviceMonitor.Start();
 }
}
