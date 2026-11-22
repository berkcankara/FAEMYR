using System.Windows;
namespace Faemyr;
public partial class MainWindow
{
 PlaybackEntry? audibleEntry;
 PlaybackPlan MakePlaybackPlan(Track track,AudioPreferences prefs)
 {
  var albums=library.Where(t=>!t.IsDsd).ToLookup(t=>t.AlbumKey);var snapshot=queue.ToArray();int index=queueIndex;
  var playbackOrder=order;if(index<0||index>=snapshot.Length||snapshot[index].Path!=track.Path){snapshot=[track];index=0;playbackOrder=new PlaybackOrder();}
  return new(snapshot,index,playbackOrder,t=>ReplayGain.Decide(t,prefs,gainStore.Get(t,prefs.ReplayGain,albums[t.AlbumKey])),playbackPreferences.Gapless);
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
