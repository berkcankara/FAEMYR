using System.Globalization;
namespace Faemyr;

public sealed record AlbumAudioInfo(string Text,string Details)
{
 static readonly CultureInfo Turkish=CultureInfo.GetCultureInfo("tr-TR");
 static string Channels(int count)=>count switch{1=>"Mono",2=>"Stereo",>2=>L.T($"{count} kanal"),_=>L.T("Kanal bilgisi yok")};
 static string Rate(int hz)=>hz>0?(hz/1000d).ToString("0.###",Turkish)+" kHz":L.T("Örnekleme bilgisi yok");
 static string Format(Track track)
 {
  string resolution=track.IsDsd?(track.DsdRate>0?$"DSD{track.DsdRate/44100} · {(track.DsdRate/1000000d).ToString("0.####",Turkish)} MHz":"DSD"):(track.Bits>0?$"{track.Bits}-bit/":"")+Rate(track.SampleRate);
  return resolution+", "+Channels(track.Channels);
 }
 public static AlbumAudioInfo From(IEnumerable<Track> source)
 {
  var tracks=source.ToArray();var groups=tracks.GroupBy(Format).ToArray();if(groups.Length==0)return new(L.T("Ses bilgisi yok"),"");
  string details=string.Join('\n',groups.Select(g=>L.T($"{g.Key} · {g.Count()} parça")));
  if(groups.Length<=2)return new(string.Join('\n',groups.Select(g=>g.Key)),details);
  if(tracks.Any(t=>t.IsDsd||t.Bits<=0||t.SampleRate<=0))return new(L.T("Karma ses biçimleri"),details);
  string Span(IEnumerable<int> values){var sorted=values.Distinct().Order().ToArray();return sorted.Length==1?sorted[0].ToString(Turkish):sorted[0]+"–"+sorted[^1];}
  int min=tracks.Min(t=>t.SampleRate),max=tracks.Max(t=>t.SampleRate);string rates=min==max?Rate(min):(min/1000d).ToString("0.###",Turkish)+"–"+Rate(max);
  var channels=tracks.Select(t=>t.Channels).Distinct().ToArray();return new(Span(tracks.Select(t=>t.Bits))+"-bit/"+rates+"\n"+(channels.Length==1?Channels(channels[0]):L.T("Farklı kanallar")),details);
 }
}
