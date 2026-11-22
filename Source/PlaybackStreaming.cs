using System.Windows;
namespace Faemyr;
public partial class MainWindow
{
 PlaybackEntry? audibleEntry;
 PlaybackPlan MakePlaybackPlan(Track track,AudioPreferences prefs)
 {
  var albums=prefs.ReplayGain==GainMode.Album?library.Where(t=>!t.IsDsd).ToLookup(t=>t.AlbumKey):null;
  GainDecision Gain(Track t)=>prefs.ReplayGain==GainMode.Off?GainDecision.Off:
   ReplayGain.Decide(t,prefs,gainStore.Get(t,prefs.ReplayGain,albums==null?Array.Empty<Track>():albums[t.AlbumKey]));
  int index=queueIndex;
  if(index<0||index>=queue.Count||queue[index].Path!=track.Path)
   return new([track],0,new PlaybackOrder(),Gain,playbackPreferences.Gapless);
  return new(queue,index,order,Gain,playbackPreferences.Gapless);
 }
 void UpdateStreamTrack()
 {
  var entry=audio.CurrentEntry;if(entry==null||ReferenceEquals(entry,audibleEntry))return;bool newSession=entry.Id!=audibleEntry?.Id;audibleEntry=entry;
  bool changed=now?.Path!=entry.Track.Path;now=entry.Track;queueIndex=entry.QueueIndex>=0&&entry.QueueIndex<queue.Count&&queue[entry.QueueIndex].Path==entry.Track.Path?entry.QueueIndex:-1;
  order.Restore(entry.Order);resumePrepared=false;resumeSeconds=0;if(changed)ShowTrackDetails(now);SyncPlaybackVisuals(true);UpdateNextTrack();if(newSession)BeginListening(now);NotifyTrackChange();SaveSession(false);
 }
 async Task RebuildPlaybackPlan()
 {
  UpdateStreamTrack();if(audio.CurrentEntry==null||now==null||!(audio.IsPlaying||audio.IsPaused))return;
  var position=audio.Position;bool paused=audio.IsPaused;await PlayTrack(now,position,paused,true);
 }
}
public sealed class PlaybackPreferences
{
 public bool Gapless{get;set;}=true;
}
