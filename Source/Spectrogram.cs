using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Faemyr;

// A fixed ten-second history follows the consumed PCM clock, never UI wall time.
// Low-to-high logarithmic frequency bands are stored oldest-to-newest by column.
public sealed class SpectrogramHistory
{
 public const int Bands=128,Columns=600,ColumnsPerSecond=60;
 readonly byte[] data=new byte[Columns*Bands];
 long lastColumn=-1;
 public ReadOnlySpan<byte> Data=>data;
 public long LastFrame{get;private set;}=-1;
 public int SampleRate{get;private set;}
 public int FilledColumns{get;private set;}
 public void Clear(){Array.Clear(data);LastFrame=lastColumn=-1;SampleRate=0;FilledColumns=0;}
 public bool Update(ReadOnlySpan<double> levels,int sampleRate,long endFrame)
 {
  if(sampleRate<1||endFrame<0||levels.Length!=Bands)return false;
  if(sampleRate!=SampleRate||(LastFrame>=0&&endFrame<LastFrame))Clear();
  SampleRate=sampleRate;long columnIndex=endFrame*ColumnsPerSecond/sampleRate;
  long elapsed=lastColumn<0?1:columnIndex-lastColumn;if(elapsed<1)return false;
  int steps=(int)Math.Min(Columns,elapsed),keep=(Columns-steps)*Bands;
  if(keep>0)Buffer.BlockCopy(data,steps*Bands,data,0,keep);
  Array.Clear(data,keep,steps*Bands);
  // After a stall, leave an honest empty interval instead of smearing one FFT
  // over audio that was never observed. Short gaps hold the latest analysis.
  int first=elapsed>ColumnsPerSecond/4?Columns-1:Columns-steps;
  for(int column=first;column<Columns;column++)for(int band=0;band<Bands;band++)
  {
   double db=double.IsFinite(levels[band])?levels[band]:SpectrumAnalysis.FloorDb;
   data[column*Bands+band]=(byte)Math.Round(255*VuAnalysis.Unit(db,SpectrumAnalysis.FloorDb));
  }
  FilledColumns=Math.Min(Columns,FilledColumns+steps);
  LastFrame=endFrame;lastColumn=columnIndex;return true;
 }
}

public sealed partial class OscilloscopeView
{
 readonly SpectrogramHistory spectrogramHistory=new();
 SpectrumAnalysis? spectrogramAnalysis;
 double[] spectrogramLevels=new double[SpectrogramHistory.Bands];
 long spectrogramAnalysisFrame=-1;
 WriteableBitmap? spectrogramBitmap;
 readonly int[] spectrogramPixels=new int[SpectrogramHistory.Columns*SpectrogramHistory.Bands],spectrogramPalette=new int[256];
 bool spectrogramDirty=true,spectrogramColorDirty=true;
 void ResetSpectrogram(){spectrogramHistory.Clear();spectrogramAnalysisFrame=-1;spectrogramDirty=true;}
 void UpdateSpectrogram()
 {
  if(capture==null||count<2)return;
  if(spectrogramAnalysis?.SampleRate!=capture.SampleRate){spectrogramAnalysis=new SpectrumAnalysis(capture.SampleRate,SpectrogramHistory.Bands);ResetSpectrogram();}
  // Raster history scrolls at 60 columns/s, while expensive stereo FFTs are
  // limited to 30/s. No per-frame arrays or work on the audio callback thread.
  if(spectrogramAnalysisFrame<0||scopeEndFrame<spectrogramAnalysisFrame||scopeEndFrame-spectrogramAnalysisFrame>=Math.Max(1,capture.SampleRate/30))
  {
   spectrogramAnalysis.Analyze(left.AsSpan(0,count),right.AsSpan(0,count),capture.Channels).CopyTo(spectrogramLevels);
   spectrogramAnalysisFrame=scopeEndFrame;
  }
  if(spectrogramHistory.Update(spectrogramLevels,capture.SampleRate,scopeEndFrame))spectrogramDirty=true;
 }
 void UpdateSpectrogramBitmap()
 {
  if(spectrogramColorDirty)
  {
   var color=(TraceBrush as SolidColorBrush)?.Color??Colors.MediumPurple;
   for(int i=0;i<256;i++)
   {
    double strength=i/255d,white=Math.Max(0,(strength-.65)/.35)*.9,light=.22+.85*strength;
    int alpha=i==0?0:(int)Math.Round(255*(.18+.82*Math.Sqrt(strength))*color.A/255d);
    int Channel(byte value)=>(int)Math.Round((Math.Min(255,value*light)*(1-white)+255*white)*alpha/255d);
    spectrogramPalette[i]=(alpha<<24)|(Channel(color.R)<<16)|(Channel(color.G)<<8)|Channel(color.B);
   }
   spectrogramColorDirty=false;spectrogramDirty=true;
  }
  if(!spectrogramDirty)return;
  var values=spectrogramHistory.Data;
  for(int column=0;column<SpectrogramHistory.Columns;column++)for(int band=0;band<SpectrogramHistory.Bands;band++)
   spectrogramPixels[(SpectrogramHistory.Bands-1-band)*SpectrogramHistory.Columns+column]=spectrogramPalette[values[column*SpectrogramHistory.Bands+band]];
  spectrogramBitmap??=new WriteableBitmap(SpectrogramHistory.Columns,SpectrogramHistory.Bands,96,96,PixelFormats.Pbgra32,null);
  spectrogramBitmap.WritePixels(new Int32Rect(0,0,SpectrogramHistory.Columns,SpectrogramHistory.Bands),spectrogramPixels,SpectrogramHistory.Columns*4,0);spectrogramDirty=false;
 }
 void RenderSpectrogram(DrawingContext drawing)
 {
  UpdateSpectrogramBitmap();
  double left=ActualWidth>=200?28:0,height=Math.Max(2,ActualHeight-13),width=Math.Max(2,ActualWidth-left);
  RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.Linear);
  drawing.PushOpacityMask(PlotEdgeMask(left,width));
  drawing.DrawImage(spectrogramBitmap,new Rect(left,0,width,height));
  drawing.DrawLine(axis,new Point(left,height),new Point(ActualWidth,height));
  drawing.Pop();
  double maximum=spectrogramAnalysis?.MaximumFrequency??20000;
  if(left>0)foreach(double hz in height>=65?new double[]{20,100,1000,20000}:new double[]{20,20000})
  {
   double frequency=Math.Min(hz,maximum);if(hz>maximum&&hz!=20000)continue;
   double y=height*(1-Math.Log(frequency/20)/Math.Log(maximum/20));
   string text=frequency>=1000?(frequency/1000).ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+"k":frequency.ToString("0");
   Label(drawing,text,0,Math.Clamp(y-5,0,Math.Max(0,height-10)),8);
  }
  foreach(var (offset,text) in new[]{(0d,"−10 s"),(.5,"−5 s"),(1d,"0 s")})Label(drawing,text,left+width*offset,height+2,8,centered:true);
 }
}
