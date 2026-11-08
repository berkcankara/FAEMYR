using System.IO;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace Ahenk;
public static class ExclusiveDiagnostics
{
 public static void Probe(string directory)
 {
  Directory.CreateDirectory(directory);using var enumerator=new MMDeviceEnumerator();using var selected=enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);var devices=new List<object>();
  foreach(var option in AudioEngine.Devices())
  {
   using var device=enumerator.GetDevice(option.Id);using var client=device.AudioClient;var formats=new List<object>();
   foreach(int rate in new[]{44100,48000,96000,192000})foreach(int bits in new[]{16,24,32})foreach(var format in new[]{new WaveFormat(rate,bits,2),AudioFormats.Extensible(new WaveFormat(rate,bits,2))})
   {
    bool? supported=null;string? failure=null;try{supported=client.IsFormatSupported(AudioClientShareMode.Exclusive,format);}catch(Exception ex){failure=ex.Message;}formats.Add(new{rate,bits,header=format.Encoding.ToString(),supported,failure});
   }
   object? Property(PropertyKey key)=>device.Properties.Contains(key)?device.Properties[key].Value:null;
   devices.Add(new{option.Name,isDefault=option.Id==selected.ID,formFactor=Property(PropertyKeys.PKEY_AudioEndpoint_FormFactor),eventDriven=Property(PropertyKeys.PKEY_AudioEndpoint_Supports_EventDriven_Mode),mix=client.MixFormat.ToString(),defaultPeriod=client.DefaultDevicePeriod,minimumPeriod=client.MinimumDevicePeriod,muted=device.AudioEndpointVolume.Mute,masterLevel=device.AudioEndpointVolume.MasterVolumeLevelScalar,hardwareVolumeSupport=device.AudioEndpointVolume.HardwareSupport.ToString(),formats});
  }
  File.WriteAllText(Path.Combine(directory,"exclusive-probe.json"),JsonSerializer.Serialize(new{devices,outputStarted=false},new JsonSerializerOptions{WriteIndented=true}));
 }
}
