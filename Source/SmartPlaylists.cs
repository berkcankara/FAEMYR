using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
namespace Faemyr;
public static class TrackQuery
{
 public static string Help=>L.T("Sözcük veya tırnak içindeki ifadelerle ara. Koşullar birlikte uygulanır.\n\nartist:Metallica\nalbum:\"Bad Apple!!\"\nformat:flac\nyear:1990..2000\nbits:>=24 rate:>=96000\nrating:>=4\nplays:0\nadded:30d\nplayed:never\nfavorite:true\nmissing:true\nduration:<300\n\nSüre saniye, örnekleme oranı Hz olarak yazılır. added:30d son 30 günde eklenenlerdir. -artist:Deneme koşulu dışlar.");
 public static int Year(Track t)=>AlbumSorting.Year([t]);
 static Func<double,bool> Numeric(string value)
 {
  var range=value.Split("..",StringSplitOptions.None);if(range.Length==2&&double.TryParse(range[0],NumberStyles.Float,CultureInfo.InvariantCulture,out double low)&&double.TryParse(range[1],NumberStyles.Float,CultureInfo.InvariantCulture,out double high))return actual=>actual>=low&&actual<=high;
  var match=Regex.Match(value,@"^(>=|<=|>|<|=)?([0-9]+(?:\.[0-9]+)?)$");if(!match.Success)throw new FormatException(L.T("Sayısal filtre geçersiz: ")+value);double n=double.Parse(match.Groups[2].Value,CultureInfo.InvariantCulture);return match.Groups[1].Value switch{">="=>actual=>actual>=n,"<="=>actual=>actual<=n,">"=>actual=>actual>n,"<"=>actual=>actual<n,_=>actual=>actual==n};
 }
 static bool Contains(string text,string value)=>text.Contains(value,StringComparison.CurrentCultureIgnoreCase);
 public static Func<Track,bool> Compile(string query,ListeningStore? history,out string error)
 {
  error="";var terms=new List<Func<Track,bool>>();try
  {
   foreach(Match match in Regex.Matches(query,"(?<not>-?)(?:(?<field>[a-zA-Z]+):(?<value>\"[^\"]*\"|\\S+)|(?<text>\"[^\"]*\"|\\S+))"))
   {
    string field=match.Groups["field"].Value.ToLowerInvariant(),value=(field.Length>0?match.Groups["value"].Value:match.Groups["text"].Value).Trim('"');Func<Track,ListeningStats> stats=t=>history?.Get(t)??new(){AddedAt=t.AddedAt};Func<Track,bool> test;
    switch(field)
    {
     case "":test=t=>Contains(t.Title+" "+t.Artist+" "+t.Album+" "+t.AlbumArtist+" "+t.AudioPath,value);break;
     case "artist":test=t=>Contains(t.Artist+" "+t.AlbumArtist,value);break;case "album":test=t=>Contains(t.Album,value);break;
     case "format":test=t=>Contains(t.Codec,value)||System.IO.Path.GetExtension(t.AudioPath).TrimStart('.').Equals(value,StringComparison.OrdinalIgnoreCase);break;
     case "year":case "bits":case "rate":case "rating":case "plays":case "duration":
      var compare=Numeric(value);test=t=>compare(field switch{"year"=>Year(t),"bits"=>t.Bits,"rate"=>t.SampleRate,"rating"=>t.Rating,"plays"=>stats(t).PlayCount,_=>t.Seconds});break;
     case "favorite":case "missing":if(!bool.TryParse(value,out bool flag))throw new FormatException("true veya false kullan: "+field);test=t=>(field=="favorite"?t.IsFavorite:!File.Exists(t.AudioPath))==flag;break;
     case "added":if(!int.TryParse(value.TrimEnd('d','D'),out int days)||days<0)throw new FormatException(L.T("Eklenme filtresi için gün sayısı kullan: added:30d"));var since=DateTimeOffset.UtcNow.AddDays(-Math.Min(days,365000));test=t=>stats(t).AddedAt>=since;break;
     case "played":if(!value.Equals("never",StringComparison.OrdinalIgnoreCase))throw new FormatException(L.T("Dinlenmemişler için played:never kullan."));test=t=>stats(t).PlayCount==0;break;
     default:throw new FormatException(L.T("Bilinmeyen filtre: ")+field);
    }
    if(match.Groups["not"].Value=="-"){var positive=test;test=t=>!positive(t);}terms.Add(test);
   }
  }catch(FormatException ex){error=ex.Message;return _=>false;}
  if(terms.Count==0)return static _=>true;
  if(terms.Count==1)return terms[0];
  return track=>{foreach(var term in terms)if(!term(track))return false;return true;};
 }
 public static List<Track> Sort(IEnumerable<Track> tracks,string mode,ListeningStore? history)=>mode switch
 {
  "added"=>tracks.OrderByDescending(t=>history?.Get(t).AddedAt??t.AddedAt).ThenBy(t=>t.Title).ToList(),
  "played"=>tracks.OrderByDescending(t=>history?.Get(t).LastPlayed).ThenBy(t=>t.Title).ToList(),
  "plays"=>tracks.OrderByDescending(t=>history?.Get(t).PlayCount??0).ThenBy(t=>t.Title).ToList(),
  "year"=>tracks.OrderByDescending(Year).ThenBy(t=>t.Album).ThenBy(t=>t.Number).ToList(),
  _=>tracks.OrderBy(t=>t.Artist).ThenBy(t=>t.Album).ThenBy(t=>t.Disc).ThenBy(t=>t.Number).ThenBy(t=>t.Title).ToList()
 };
}
public sealed record SmartPreset(string Name,string Query,string Sort="library");
public partial class MainWindow
{
 static readonly SmartPreset[] SmartPresets=[new(L.T("Tüm parçalar"),""),new(L.T("Son eklenenler · 30 gün"),"added:30d","added"),new(L.T("Hiç dinlemediklerim"),"plays:0"),new(L.T("Son dinlenenler"),"plays:>0","played"),new(L.T("En çok dinlenenler"),"plays:>0","plays"),new("1990–2000","year:1990..2000","year"),new(L.T("Yüksek çözünürlük"),"bits:>=24 rate:>=96000"),new(L.T("Yüksek puanlılar"),"rating:>=4")];
 void InitializeSmartLists(){SmartView.ItemsSource=SmartPresets;SmartView.DisplayMemberPath="Name";SmartView.SelectedIndex=0;}
 void SmartView_Changed(object sender,SelectionChangedEventArgs e){if(libraryReady)RefreshView();}
 void UpdateSmartTools(){SmartTools.Visibility=Visible(view=="tracks");}
 void FilterHelp_Click(object sender,RoutedEventArgs e)=>MessageBox.Show(settingsWindow??this,TrackQuery.Help,L.T("Faemyr — Filtre yardımı"),MessageBoxButton.OK,MessageBoxImage.Information);
 void NewSmartPlaylist_Click(object sender,RoutedEventArgs e)=>EditSmartPlaylist(null);
 void EditSmartPlaylist(LocalPlaylist? existing)
 {
  var draft=existing?.Copy()??new LocalPlaylist{Name="",SmartQuery="plays:0"};
  var owner=new Window{Title=existing==null?L.T("Akıllı çalma listesi oluştur"):L.T("Akıllı listeyi düzenle"),Owner=this,Width=620,Height=730,MinWidth=460,MinHeight=420,WindowStartupLocation=WindowStartupLocation.CenterOwner};owner.SetResourceReference(BackgroundProperty,"Canvas");owner.SetResourceReference(ForegroundProperty,"Ink");
  owner.DataContext=draft;var root=new DockPanel{Margin=new Thickness(28)};owner.Content=root;var stack=new StackPanel();root.Children.Add(new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});stack.Children.Add(new TextBlock{Text=L.T("Akıllı çalma listesi"),FontSize=25,FontWeight=FontWeights.Bold});
  stack.Children.Add(new TextBlock{Text=L.T("Koleksiyonun ve dinleme geçmişin değiştikçe liste kendini günceller."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,20)});
  stack.Children.Add(BuildPlaylistCoverEditor(draft,owner));
  stack.Children.Add(new TextBlock{Text=L.T("Liste adı"),Margin=new Thickness(0,0,0,5)});var name=new TextBox{Text=existing?.Name??"",ToolTip=L.T("Liste adı"),Margin=new Thickness(0,0,0,14)};stack.Children.Add(name);
  stack.Children.Add(new TextBlock{Text=L.T("Hazır kural"),Margin=new Thickness(0,0,0,5)});
  var presets=new ComboBox{ItemsSource=SmartPresets.Skip(1),DisplayMemberPath="Name",ToolTip=L.T("Hazır kural seç")};stack.Children.Add(presets);
  stack.Children.Add(new TextBlock{Text=L.T("Filtre koşulları"),Margin=new Thickness(0,14,0,5)});var query=new TextBox{Text=existing?.SmartQuery??"plays:0",TextWrapping=TextWrapping.Wrap,Height=80,AcceptsReturn=false,Margin=new Thickness(0,0,0,12)};stack.Children.Add(query);
  stack.Children.Add(new TextBlock{Text=L.T("Sıralama"),Margin=new Thickness(0,0,0,5)});
  var sort=new ComboBox{ItemsSource=new[]{new SmartPreset(L.T("Sanatçı / albüm"),"","library"),new(L.T("Eklenme tarihi"),"","added"),new(L.T("Son dinlenme"),"","played"),new(L.T("Çalma sayısı"),"","plays"),new(L.T("Yıl"),"","year")},DisplayMemberPath="Name"};sort.SelectedItem=sort.Items.Cast<SmartPreset>().First(p=>p.Sort==(existing?.SmartSort??"library"));stack.Children.Add(sort);
  var preview=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,16)};preview.SetResourceReference(TextBlock.ForegroundProperty,"Accent");stack.Children.Add(preview);
  var help=new Button{Content=L.T("Filtre örnekleri"),HorizontalAlignment=HorizontalAlignment.Left};help.Click+=FilterHelp_Click;stack.Children.Add(help);
  var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Insert(0,footer);footer.Children.Add(new Button{Content=L.T("Vazgeç"),IsCancel=true});var save=new Button{Content=existing==null?L.T("Oluştur"):L.T("Kaydet"),IsDefault=true};save.SetResourceReference(Control.BackgroundProperty,"Accent");save.SetResourceReference(Control.ForegroundProperty,"OnAccent");footer.Children.Add(save);
  void Preview(){var match=TrackQuery.Compile(query.Text,listening,out string error);preview.Text=error.Length>0?error:L.T($"Şu an {library.Count(match)} parça eşleşiyor.");save.IsEnabled=error.Length==0&&!string.IsNullOrWhiteSpace(name.Text);}
  query.TextChanged+=(_,_)=>Preview();name.TextChanged+=(_,_)=>Preview();presets.SelectionChanged+=(_,_)=>{if(presets.SelectedItem is SmartPreset p){query.Text=p.Query;sort.SelectedItem=sort.Items.Cast<SmartPreset>().First(x=>x.Sort==p.Sort);if(string.IsNullOrWhiteSpace(name.Text))name.Text=p.Name;}};Preview();
  save.Click+=(_,_)=>
  {
   draft.Name=name.Text.Trim();draft.SmartQuery=query.Text.Trim();draft.SmartSort=((SmartPreset)sort.SelectedItem).Sort;
   try{SavePlaylistDraft(draft,existing);playlistId=draft.Id;view="playlist";Search.Text="";RefreshView();owner.DialogResult=true;}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or NotSupportedException){preview.Text=L.T("Liste kaydedilemedi: ")+ex.Message;}
  };owner.ShowDialog();
 }
}
