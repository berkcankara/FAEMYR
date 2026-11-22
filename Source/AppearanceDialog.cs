using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Faemyr;
public partial class MainWindow
{
 Action? appearanceEditorRefresh;
 SettingsSection BuildAppearanceSettings(Window dialog,Action<bool> setValid)
 {
  var original=Themes.Appearance.Copy();string originalTheme=Themes.Current;var draft=original.Copy();bool updating=false;
  var root=new Grid{Margin=new Thickness(0)};root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
  var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};root.Children.Add(scroll);var content=new StackPanel{Margin=new Thickness(0,0,12,0)};scroll.Content=content;
  content.Children.Add(new TextBlock{Text=L.T("Renkler ve görünüm"),FontSize=24,FontWeight=FontWeights.Bold});
  var intro=new TextBlock{Text=L.T("Değişiklikleri anında gör. Kaydettiğinde sonraki açılışta da hatırlanır."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,9,0,20),FontSize=13};intro.SetResourceReference(TextBlock.ForegroundProperty,"Muted");content.Children.Add(intro);
  content.Children.Add(new TextBlock{Text=L.T("Tema"),FontSize=13,Margin=new Thickness(0,0,0,5)});
  var theme=new ComboBox{ItemsSource=Themes.Choices,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=Themes.Current,Margin=new Thickness(0,0,0,18)};System.Windows.Automation.AutomationProperties.SetName(theme,L.T("Tema"));content.Children.Add(theme);
  var moduleTransparency=new CheckBox{Content=L.T("Modül saydamlığı"),IsChecked=draft.ModuleTransparency,Style=(Style)FindResource("AppearanceSwitch"),FontSize=14,FontWeight=FontWeights.SemiBold};System.Windows.Automation.AutomationProperties.SetName(moduleTransparency,L.T("Modül saydamlığı"));content.Children.Add(moduleTransparency);
  var moduleHint=new TextBlock{Text=L.T("Yan paneller ve oynatıcı kutuları, bulanık albüm arka planını hafifçe geçirir."),TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,4,0,18)};moduleHint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");content.Children.Add(moduleHint);
  content.Children.Add(new TextBlock{Text=L.T("Dil"),FontSize=13});
  var language=new ComboBox{ItemsSource=new[]{new ThemeChoice("tr",L.T("Türkçe")),new ThemeChoice("en",L.T("English"))},DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=L.ReadLanguage()};System.Windows.Automation.AutomationProperties.SetName(language,"Language");content.Children.Add(language);
  var languageHint=new TextBlock{Text=L.T("Dil değişikliği Faemyr yeniden açıldığında uygulanır."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18)};languageHint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");content.Children.Add(languageHint);
  var preview=new Border{CornerRadius=new CornerRadius(16),Padding=new Thickness(18),Margin=new Thickness(0,0,0,20)};preview.SetResourceReference(Border.BackgroundProperty,"Surface");var previewRow=new StackPanel{Orientation=Orientation.Horizontal};preview.Child=previewRow;content.Children.Add(preview);
  var previewPlay=new Border{Width=48,Height=48,CornerRadius=new CornerRadius(24),Margin=new Thickness(0,0,16,0)};previewPlay.SetResourceReference(Border.BackgroundProperty,"Accent");var icon=new SvgIcon{Icon="play",Width=23,Height=23};icon.SetResourceReference(SvgIcon.BrushProperty,"OnAccent");previewPlay.Child=icon;previewRow.Children.Add(previewPlay);
  var previewText=new StackPanel{VerticalAlignment=VerticalAlignment.Center};previewRow.Children.Add(previewText);var playing=new TextBlock{Text=L.T("Çalan parça"),FontSize=19,FontWeight=FontWeights.SemiBold};playing.SetResourceReference(TextBlock.ForegroundProperty,"PlayingAccent");previewText.Children.Add(playing);var caption=new TextBlock{Text=L.T("Oynatma düğmesi ve parça vurgusu"),FontSize=12,Margin=new Thickness(0,5,0,0)};caption.SetResourceReference(TextBlock.ForegroundProperty,"Muted");previewText.Children.Add(caption);
  var target=new ComboBox{ItemsSource=ColorRoles.All,DisplayMemberPath="Name",SelectedIndex=0,Margin=new Thickness(0,0,0,10)};System.Windows.Automation.AutomationProperties.SetName(target,L.T("Düzenlenecek renk"));content.Children.Add(target);
  var follows=new CheckBox{Content=L.T("Vurgu rengiyle aynı"),IsChecked=draft.PlayingColor==null,Margin=new Thickness(0,0,0,12),Visibility=Visibility.Collapsed};follows.SetResourceReference(Control.ForegroundProperty,"Ink");content.Children.Add(follows);
  var palette=new WrapPanel{Margin=new Thickness(0,0,0,15)};content.Children.Add(palette);
  var hexRow=new Grid{Margin=new Thickness(0,0,0,10)};hexRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});hexRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});hexRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});content.Children.Add(hexRow);
  var swatch=new Border{Width=39,Height=39,CornerRadius=new CornerRadius(10),Margin=new Thickness(0,0,12,0)};hexRow.Children.Add(swatch);
  var hex=new TextBox{MaxLength=7,FontFamily=new("Consolas"),FontSize=16,Margin=new Thickness(0,0,12,0),ToolTip="#RRGGBB"};Grid.SetColumn(hex,1);hexRow.Children.Add(hex);System.Windows.Automation.AutomationProperties.SetName(hex,L.T("HEX renk kodu"));
  var themeColor=new Button{Content=L.T("Varsayılan renk"),Margin=new Thickness(0),Padding=new Thickness(13,8,13,8)};Grid.SetColumn(themeColor,2);hexRow.Children.Add(themeColor);
  var error=new TextBlock{Text=L.T("Altı haneli bir renk kodu gir: #RRGGBB"),Foreground=new SolidColorBrush(Color.FromRgb(227,116,116)),Visibility=Visibility.Collapsed,FontSize=12,Margin=new Thickness(0,0,0,8)};content.Children.Add(error);
  var channels=new List<Slider>();var channelLabels=new List<TextBlock>();
  foreach(string label in new[]{"R","G","B"}){var row=new Grid{Margin=new Thickness(0,0,0,7)};row.ColumnDefinitions.Add(new(){Width=new GridLength(30)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new GridLength(42)});content.Children.Add(row);row.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center,FontSize=12});var slider=new Slider{Minimum=0,Maximum=255,TickFrequency=1,IsSnapToTickEnabled=true,IsMoveToPointEnabled=true,Style=(Style)FindResource("ModernSeek")};Grid.SetColumn(slider,1);row.Children.Add(slider);System.Windows.Automation.AutomationProperties.SetName(slider,label+L.T(" renk kanalı"));channels.Add(slider);var number=new TextBlock{HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,FontSize=12};Grid.SetColumn(number,2);row.Children.Add(number);channelLabels.Add(number);}
  var divider=new Border{Height=1,Margin=new Thickness(0,14,0,18)};divider.SetResourceReference(Border.BackgroundProperty,"Line");content.Children.Add(divider);
  var surfaceTitle=new TextBlock{Text=L.T("Albüm ve çalma listesi arka planı"),FontSize=17,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,15)};content.Children.Add(surfaceTitle);
  Slider SurfaceControl(string title,double max,double value,out TextBlock number)
  {
   var labelRow=new DockPanel{Margin=new Thickness(0,0,0,4)};content.Children.Add(labelRow);number=new TextBlock{HorizontalAlignment=HorizontalAlignment.Right,FontSize=12};DockPanel.SetDock(number,Dock.Right);labelRow.Children.Add(number);labelRow.Children.Add(new TextBlock{Text=title,FontSize=13});
   var slider=new Slider{Minimum=0,Maximum=max,Value=value,TickFrequency=1,IsSnapToTickEnabled=true,IsMoveToPointEnabled=true,Margin=new Thickness(0,0,0,12),Style=(Style)FindResource("ModernSeek")};System.Windows.Automation.AutomationProperties.SetName(slider,title);content.Children.Add(slider);return slider;
  }
  var opacity=SurfaceControl(L.T("Liste opaklığı"),100,draft.ListOpacity,out var opacityNumber);var blur=SurfaceControl(L.T("Liste bulanıklığı"),60,draft.BlurRadius,out var blurNumber);
  var footer=new DockPanel{Margin=new Thickness(0,20,0,0)};Grid.SetRow(footer,1);root.Children.Add(footer);var reset=new Button{Content=L.T("Tümünü sıfırla"),Padding=new Thickness(12,10,12,10)};DockPanel.SetDock(reset,Dock.Left);footer.Children.Add(reset);
  string Role()=>((ColorRole)target.SelectedItem).Key;
  string CurrentHex(){if(target.SelectedIndex==0)return draft.Accent??Themes.DefaultAccent;if(target.SelectedIndex==1)return draft.PlayingColor??draft.Accent??Themes.DefaultAccent;if(draft.Colors.TryGetValue(Role(),out string? value))return value;var color=((SolidColorBrush)FindResource(Role())).Color;return $"#{color.R:X2}{color.G:X2}{color.B:X2}";}
  void Apply(){Themes.Appearance=draft;Themes.ApplyAppearance();UpdateAccentControls();opacityNumber.Text=$"%{draft.ListOpacity:0}";blurNumber.Text=$"%{draft.BlurRadius:0} px";UpdatePageBackdrop();}
  void Sync()
  {
   updating=true;string value=CurrentHex();var c=(Color)ColorConverter.ConvertFromString(value);hex.Text=value;swatch.Background=new SolidColorBrush(c);byte[] rgb=[c.R,c.G,c.B];for(int i=0;i<3;i++){channels[i].Value=rgb[i];channelLabels[i].Text=rgb[i].ToString();}follows.Visibility=target.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;follows.IsChecked=draft.PlayingColor==null;error.Visibility=Visibility.Collapsed;setValid(true);updating=false;
  }
  void SetColor(string value){if(target.SelectedIndex==1)draft.PlayingColor=value;else if(target.SelectedIndex==0)draft.Accent=value;else draft.Colors[Role()]=value;Apply();Sync();}
  foreach(var color in new[]{"#C8F281","#F4F4F4","#59B8FF","#A89BFF","#EC8BBC","#FF976C","#E6C35C","#53D2B0","#4775DE","#D34B60"}){var preset=new Button{Width=34,Height=34,Margin=new Thickness(0,0,10,7),Padding=new Thickness(0),Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),ToolTip=color};System.Windows.Automation.AutomationProperties.SetName(preset,L.T("Renk ")+color);preset.Click+=(_,_)=>SetColor(color);palette.Children.Add(preset);}
  hex.TextChanged+=(_,_)=>{if(updating)return;string? value=AppearancePreferences.NormalizeHex(hex.Text);error.Visibility=value==null?Visibility.Visible:Visibility.Collapsed;setValid(value!=null);if(value==null)return;if(target.SelectedIndex==1)draft.PlayingColor=value;else if(target.SelectedIndex==0)draft.Accent=value;else draft.Colors[Role()]=value;Apply();updating=true;var c=(Color)ColorConverter.ConvertFromString(value);swatch.Background=new SolidColorBrush(c);byte[] rgb=[c.R,c.G,c.B];for(int i=0;i<3;i++){channels[i].Value=rgb[i];channelLabels[i].Text=rgb[i].ToString();}follows.IsChecked=draft.PlayingColor==null;updating=false;};
  foreach(var channel in channels)channel.ValueChanged+=(_,_)=>{if(updating)return;SetColor($"#{(int)channels[0].Value:X2}{(int)channels[1].Value:X2}{(int)channels[2].Value:X2}");};
  target.SelectionChanged+=(_,_)=>Sync();follows.Checked+=(_,_)=>{if(updating)return;draft.PlayingColor=null;Apply();Sync();};follows.Unchecked+=(_,_)=>{if(updating)return;draft.PlayingColor=draft.Accent??Themes.DefaultAccent;Apply();Sync();};
  themeColor.Click+=(_,_)=>{if(target.SelectedIndex==1)draft.PlayingColor=null;else if(target.SelectedIndex==0)draft.Accent=null;else draft.Colors.Remove(Role());Apply();Sync();};
  opacity.ValueChanged+=(_,_)=>{if(updating)return;draft.ListOpacity=opacity.Value;Apply();};blur.ValueChanged+=(_,_)=>{if(updating)return;draft.BlurRadius=blur.Value;Apply();};
  moduleTransparency.Checked+=(_,_)=>ChangeModuleTransparency(true);moduleTransparency.Unchecked+=(_,_)=>ChangeModuleTransparency(false);
  void ChangeModuleTransparency(bool value){if(updating)return;draft.ModuleTransparency=value;Apply();}
  reset.Click+=(_,_)=>{draft=new();updating=true;opacity.Value=draft.ListOpacity;blur.Value=draft.BlurRadius;moduleTransparency.IsChecked=draft.ModuleTransparency;updating=false;Apply();Sync();};
  theme.SelectionChanged+=(_,_)=>{if(updating||theme.SelectedValue is not string id)return;Themes.Apply(id);ApplyInterfacePreferences();NativeAppearance.Apply(this);NativeAppearance.Apply(dialog);renderedAlbum="";RefreshView();UpdateAccentControls();Sync();};
  appearanceEditorRefresh=()=>{updating=true;draft=Themes.Appearance;theme.SelectedValue=Themes.Current;opacity.Value=draft.ListOpacity;blur.Value=draft.BlurRadius;moduleTransparency.IsChecked=draft.ModuleTransparency;updating=false;Apply();Sync();};
  Apply();Sync();
  return new(root,()=>{draft.Save(Themes.AppearancePath);Themes.Save();L.Save(language.SelectedValue as string??"tr");},()=>{Themes.Appearance=original;Themes.Apply(originalTheme);NativeAppearance.Apply(this);renderedAlbum="";RefreshView();UpdateAccentControls();},()=>AppearancePreferences.NormalizeHex(hex.Text)!=null);
 }
}
