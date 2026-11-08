namespace Ahenk;
public enum RepeatMode { Off, All, One }
public sealed record PlaybackOrderState(bool Shuffle,RepeatMode Repeat,int[] History,int[] Remaining){public int[] Priority{get;init;}=[];public ulong RandomState{get;init;}}
public sealed class PlaybackOrder
{
 ulong randomState;readonly List<int> history=[];readonly List<int> remaining=[];
 readonly List<int> priority=[];
 public bool Shuffle {get;private set;}
 public RepeatMode Repeat {get;set;}
 public PlaybackOrder(int? seed=null){randomState=(ulong)(seed.HasValue?(uint)seed.Value+1:Random.Shared.NextInt64(1,long.MaxValue));}
 public PlaybackOrderState Capture()=>new(Shuffle,Repeat,history.ToArray(),remaining.ToArray()){Priority=priority.ToArray(),RandomState=randomState};
 public void Restore(PlaybackOrderState state){Shuffle=state.Shuffle;Repeat=state.Repeat;history.Clear();history.AddRange(state.History);remaining.Clear();remaining.AddRange(state.Remaining);priority.Clear();priority.AddRange(state.Priority);if(state.RandomState!=0)randomState=state.RandomState;}
 public void Prioritize(IEnumerable<int> indices){priority.Clear();priority.AddRange(indices.Distinct());}
 public PlaybackOrder Clone(){var copy=new PlaybackOrder();copy.Restore(Capture());return copy;}
 int RandomIndex(int maximum){randomState^=randomState<<13;randomState^=randomState>>7;randomState^=randomState<<17;return (int)(randomState%(uint)maximum);}
 void Fill(int count,int current){remaining.Clear();for(int i=0;i<count;i++)if(i!=current)remaining.Add(i);if(Shuffle)for(int i=remaining.Count-1;i>0;i--){int j=RandomIndex(i+1);(remaining[i],remaining[j])=(remaining[j],remaining[i]);}}
 public void Reset(int count,int current){history.Clear();priority.Clear();Fill(count,current);}
 public void SetShuffle(bool enabled,int count,int current){Shuffle=enabled;Reset(count,current);}
 public int Peek(int count,int current,bool naturalEnd)
 {
  if(count==0)return -1;
  if(naturalEnd&&Repeat==RepeatMode.One&&current>=0)return current;
  priority.RemoveAll(i=>i<0||i>=count||i==current);if(priority.Count>0)return priority[0];
  if(!Shuffle){if(current+1<count)return current+1;return Repeat==RepeatMode.All?0:-1;}
  if(remaining.Count==0){if(Repeat!=RepeatMode.All)return -1;if(count==1)return 0;Fill(count,current);}
  return remaining[0];
 }
 public int Next(int count,int current,bool naturalEnd){int next=Peek(count,current,naturalEnd);if(next>=0&&!(naturalEnd&&Repeat==RepeatMode.One)){priority.Remove(next);if(Shuffle){remaining.Remove(next);if(current>=0)history.Add(current);}}return next;}
 public int Previous(int current){if(Shuffle){if(history.Count==0)return -1;int i=history[^1];history.RemoveAt(history.Count-1);remaining.Remove(i);if(current>=0&&!remaining.Contains(current))remaining.Add(current);return i;}return current>0?current-1:-1;}
}
