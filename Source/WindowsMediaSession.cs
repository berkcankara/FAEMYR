using System.Diagnostics;
using System.IO;
using Windows.Media;
using Windows.Storage.Streams;

namespace Ahenk;
// Publishes metadata and commands only. This class never creates an audio
// player, decoder or output device, and never changes the user's volume.
public sealed class WindowsMediaSession : IDisposable
{
 readonly SystemMediaTransportControls controls;
 readonly Action<DesktopMediaAction> command;readonly Action<TimeSpan> seek;
 volatile bool disposed;bool published,enabled;int artworkGeneration;string metadata="",state="";long timelineAt;
 InMemoryRandomAccessStream? artworkStream;
 TimeSpan lastPosition;string? error;
 public bool Published=>published;
 public string? Error=>error;
 public SystemMediaTransportControls Controls=>controls;
 public WindowsMediaSession(IntPtr hwnd,Action<DesktopMediaAction> command,Action<TimeSpan> seek)
 {
  this.command=command;this.seek=seek;
  controls=SystemMediaTransportControlsInterop.GetForWindow(hwnd);
  controls.IsEnabled=false;controls.PlaybackStatus=MediaPlaybackStatus.Closed;
  controls.ButtonPressed+=ButtonPressed;controls.PlaybackPositionChangeRequested+=PositionRequested;
 }
 public static DesktopMediaAction ActionFor(SystemMediaTransportControlsButton button)=>button switch
 {
  SystemMediaTransportControlsButton.Play=>DesktopMediaAction.Play,SystemMediaTransportControlsButton.Pause=>DesktopMediaAction.Pause,
  SystemMediaTransportControlsButton.Previous=>DesktopMediaAction.Previous,SystemMediaTransportControlsButton.Next=>DesktopMediaAction.Next,
  SystemMediaTransportControlsButton.Stop=>DesktopMediaAction.Stop,_=>DesktopMediaAction.None
 };
 void ButtonPressed(SystemMediaTransportControls sender,SystemMediaTransportControlsButtonPressedEventArgs args)
 {
  if(disposed)return;var action=ActionFor(args.Button);if(action!=DesktopMediaAction.None)command(action);
 }
 void PositionRequested(SystemMediaTransportControls sender,PlaybackPositionChangeRequestedEventArgs args)
 {if(!disposed)seek(args.RequestedPlaybackPosition);}
 public void Update(Track? track,bool playing,bool paused,bool ready,bool canPlay,bool canPrevious,bool canNext,TimeSpan position,TimeSpan duration)
 {
  if(disposed)return;
  try
  {
   // A restored, unplayed library must not displace another app's active media
   // session. Once playback starts, retain the paused/stopped card for resume.
   published=track!=null&&(published||playing||paused);
   if(!published)
   {
    if(enabled){enabled=false;++artworkGeneration;metadata=state="";controls.PlaybackStatus=MediaPlaybackStatus.Closed;controls.IsEnabled=false;controls.DisplayUpdater.ClearAll();controls.DisplayUpdater.Update();artworkStream?.Dispose();artworkStream=null;}
    return;
   }
   if(!enabled){controls.IsEnabled=true;enabled=true;}
   var status=playing?MediaPlaybackStatus.Playing:paused?MediaPlaybackStatus.Paused:MediaPlaybackStatus.Stopped;
   string nextState=$"{status}|{ready}|{canPlay}|{canPrevious}|{canNext}";bool changed=nextState!=state;
   if(changed){controls.IsPlayEnabled=canPlay;controls.IsPauseEnabled=ready&&playing;controls.IsPreviousEnabled=canPrevious;controls.IsNextEnabled=canNext;controls.IsStopEnabled=ready&&(playing||paused);controls.PlaybackStatus=status;state=nextState;}
   string nextMetadata=$"{track!.Path}\n{track.Title}\n{track.Artist}\n{track.Album}\n{track.AlbumArtist}\n{track.Cover}";
   bool trackChanged=nextMetadata!=metadata;
   if(trackChanged)
   {
    metadata=nextMetadata;int generation=++artworkGeneration;var updater=controls.DisplayUpdater;updater.ClearAll();artworkStream?.Dispose();artworkStream=null;updater.Type=MediaPlaybackType.Music;
    updater.MusicProperties.Title=track.Title??"";updater.MusicProperties.Artist=track.Artist??"";
    updater.MusicProperties.AlbumTitle=track.Album??"";updater.MusicProperties.AlbumArtist=track.AlbumArtist??"";updater.Update();
    _=LoadArtwork(track.Cover,generation);
   }
   long clock=Stopwatch.GetTimestamp();
   if(changed||trackChanged||Math.Abs((position-lastPosition).TotalSeconds)>1.5||(clock-timelineAt)/(double)Stopwatch.Frequency>=1)
   {
    duration=duration<TimeSpan.Zero?TimeSpan.Zero:duration;position=position<TimeSpan.Zero?TimeSpan.Zero:position>duration?duration:position;
    controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties{StartTime=TimeSpan.Zero,MinSeekTime=TimeSpan.Zero,EndTime=duration,MaxSeekTime=duration,Position=position});
    lastPosition=position;timelineAt=clock;
   }
  }
  catch(Exception ex){error=ex.GetType().Name+": "+ex.Message;}
 }
 async Task LoadArtwork(string? path,int generation)
 {
  if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))return;
  try
  {
   // Give Windows a memory-backed image rather than a file-broker reference:
   // portable libraries and network/removable paths may be inaccessible to its broker.
   using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,4096,true);
   if(file.Length<=0||file.Length>16*1024*1024)return;
   byte[] bytes=new byte[file.Length];await file.ReadExactlyAsync(bytes);
   if(disposed||generation!=artworkGeneration)return;
   var stream=new InMemoryRandomAccessStream();
   try
   {
    using(var writer=new DataWriter(stream)){writer.WriteBytes(bytes);await writer.StoreAsync();writer.DetachStream();}
    stream.Seek(0);if(disposed||generation!=artworkGeneration){stream.Dispose();return;}
    controls.DisplayUpdater.Thumbnail=RandomAccessStreamReference.CreateFromStream(stream);artworkStream=stream;controls.DisplayUpdater.Update();
   }
   catch{stream.Dispose();throw;}
  }
  catch(Exception){/* Missing or unreadable art must never interrupt playback. */}
 }
 public void Dispose()
 {
  if(disposed)return;disposed=true;++artworkGeneration;
  try{controls.ButtonPressed-=ButtonPressed;controls.PlaybackPositionChangeRequested-=PositionRequested;controls.PlaybackStatus=MediaPlaybackStatus.Closed;controls.IsEnabled=false;controls.DisplayUpdater.ClearAll();controls.DisplayUpdater.Update();}catch(Exception ex){error=ex.Message;}
  artworkStream?.Dispose();artworkStream=null;
 }
}
