using System.Globalization;
namespace SprocketExpandedBallistics;

internal sealed record SectionLabel(int Layer,float SourceX,float SourceY,float X,float Y,string Number,string Text,bool Numbered);
internal static class CassetteSectionLabels
{
    internal const float RowSpacing=22,Padding=36;
    internal static float Height(int layers,float height)=>Math.Max(height,Padding*2+Math.Max(0,layers-1)*RowSpacing);
    internal static float DiagramWidth(float width)=>Math.Max(8,width-Math.Min(245,width*.4f));
    internal static string ShortName(string material,string full)=>material switch
    {
        "cwepGlassTextolite"=>"Textolite","highHardnessSteel"=>"Hard steel","advancedArmourSteel"=>"Advanced steel",
        "rha"=>"RHA","air"=>"Air","elastomerNERA"=>"Elastomer","aramidSpallLiner"=>"Aramid",
        "ceramicArray"=>"Ceramic","boronCarbide"=>"Boron carbide","aluminaCeramic"=>"Alumina","siliconCarbide"=>"SiC",_=>full.Split(" - ",StringSplitOptions.None)[0]
    };
    internal static SectionLabel[] Build(IReadOnlyList<CassetteLayer> layers,CassetteSectionGeometry geometry,float width,float height,Func<string,string> label)
    {
        var result=new SectionLabel[layers.Count];bool numbered=layers.Count>12;
        float diagram=DiagramWidth(width),x=diagram+12;
        int maxChars=Math.Max(5,(int)((width-x-12-(numbered?28:0))/8));
        for(int i=0;i<result.Length;i++)
        {
            var band=geometry.Bands[i];var layer=layers[i];
            var suffix=" "+layer.ThicknessMm.ToString("0.#",CultureInfo.InvariantCulture)+" mm";
            var name=ShortName(layer.Material,label(layer.Material));int room=Math.Max(1,maxChars-suffix.Length);
            if(name.Length>room)name=name[..Math.Max(1,room-1)]+"\u2026";
            float preferred=Padding+(height-2*Padding)*((i+.5f)/Math.Max(1,result.Length));
            float y=Math.Max(preferred,i==0?Padding:result[i-1].Y+RowSpacing);
            result[i]=new(i,Math.Clamp((band.X0+band.X1)/2,8,Math.Max(8,diagram-8)),geometry.ShotY,x,y,
                (i+1).ToString(CultureInfo.InvariantCulture),name+suffix,numbered);
        }
        for(int i=result.Length-1;i>=0;i--)
        {
            float maxY=i==result.Length-1?height-Padding:result[i+1].Y-RowSpacing;
            result[i]=result[i] with{Y=Math.Min(result[i].Y,maxY)};
        }
        return result;
    }
}
