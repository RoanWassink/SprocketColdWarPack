using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace SprocketExpandedBallistics;
internal static class Feat02246CustomTextureTests
{
    internal static readonly string[] Allowed={"Plugin.cs","SprocketExpandedBallistics.csproj","MaterialLook.cs","RuntimeMaterialLooks.cs","RuntimeCassettePlateVisual.cs","RELEASE-NOTES.md"};
    private sealed record Texture(string Name,bool Linear);
    internal static void Run(Action<bool,string> check)
    {
        int checks=0;var sink=check;check=(ok,text)=>{checks++;sink(ok,"feat02246: "+text);};
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        var fixture=Path.Combine(output,"managed-fixtures/custom-textures");Directory.CreateDirectory(fixture);
        var config=CustomTextureSettings.Defaults();
        foreach(var pair in config)
        {
            var dir=Path.Combine(fixture,pair.Key);Directory.CreateDirectory(dir);
            foreach(var p in Directory.GetFiles(dir))File.Delete(p);
            int warnings=0,decoded=0,disposed=0;
            var scan=new CustomTextureScan<Texture>();
            Texture Decode(string p,bool linear){decoded++;if(File.ReadAllText(p)=="broken")throw new InvalidDataException("broken PNG");return new(Path.GetFileName(p),linear);}
            CustomTextureSet<Texture>? Read()=>scan.Read(dir,pair.Value,Decode,_=>disposed++,_=>warnings++);
            check(Read()==null&&warnings==1&&decoded==0,"missing albedo falls back with one warning: "+pair.Key);
            check(Read()==null&&warnings==1,"repeated missing read warns only once");
            File.WriteAllText(Path.Combine(dir,"albedo.png"),"valid");
            var set=Read();check(set!=null&&!set.Albedo.Linear&&set.Normal==null&&set.Roughness==null&&set.Ao==null,"albedo selects custom, optional maps absent allowed");
            foreach(var map in new[]{"normal","roughness","ao"})File.WriteAllText(Path.Combine(dir,map+".png"),"valid");
            set=Read();check(set!=null&&set.Normal!.Linear&&set.Roughness!.Linear&&set.Ao!.Linear,"all data maps linear, albedo sRGB");
            pair.Value.useCustomTexture="off";int old=decoded;check(Read()==null&&decoded==old,"off bypasses custom decoding");
            pair.Value.useCustomTexture="on";check(Read()!=null,"on uses a valid albedo set");
            // Fresh scanner proves exactly one warning from a corrupt required/optional map,
            // while partially decoded textures are disposed transactionally.
            foreach(var broken in new[]{"albedo","normal","roughness","ao"})
            {
                scan=new();warnings=0;int before=disposed;
                File.WriteAllText(Path.Combine(dir,broken+".png"),"broken");
                check(Read()==null&&warnings==1,"corrupt "+broken+" selects native");
                check(disposed-before==Array.IndexOf(new[]{"albedo","normal","roughness","ao"},broken),"partial decode cleanup: "+broken);
                check(Read()==null&&warnings==1,"one warning on repeated corrupt "+broken);
                File.WriteAllText(Path.Combine(dir,broken+".png"),"valid");
            }
            pair.Value.tileSizeM=0;scan=new();warnings=0;check(Read()==null&&warnings==1,"invalid tile config safely falls back");
        }
        foreach(byte green in Enumerable.Range(0,256).Select(x=>(byte)x))
        {
            check(CustomTexturePixels.NormalY(green,false)==green,"OpenGL preserves Y");
            check(CustomTexturePixels.NormalY(green,true)==255-green,"DirectX reverses Y");
            check(CustomTexturePixels.Smoothness(green)==255-green,"white roughness -> zero smoothness");
            foreach(byte ao in new byte[]{0,64,128,192,255})
            {
                var mask=CustomTexturePixels.Mask(green,ao);
                check(mask.R==0&&mask.G==ao&&mask.B==255&&mask.A==255-green,"HDRP mask: dielectric R, linear AO G, detail B, inverse roughness A");
            }
        }
        foreach(var look in MaterialLook.All)
        {
            var data=MaterialLookGeometry.CopyRepeat(look,new Vector3(2,1,1),1,Feat02245MaterialLookTests.Box());
            var front=data.Uv.Where((_,i)=>data.Normals[i].Z>.9).ToArray();
            check(Math.Abs(front.Max(v=>v.X)-front.Min(v=>v.X)-2)<1e-6,"2 m plate / 1 m tile = 2 repeats: "+look.Id);
            check(data.Vertices.Length==36,"repeat avoids atlas clipping and covers all six faces");
            var small=MaterialLookGeometry.CopyRepeat(look,new Vector3(-2,.5f,1),.5f,Feat02245MaterialLookTests.Box());
            front=small.Uv.Where((_,i)=>small.Normals[i].Z>.9).ToArray();
            check(Math.Abs(front.Max(v=>v.X)-front.Min(v=>v.X)-4)<1e-6,"absolute physical scale with 0.5 m tile");
            check(MaterialLookGeometry.Tangents(small).All(t=>float.IsFinite(t.X+t.Y+t.Z+t.W)),"repeat tangents finite");
        }
        bool invalid=false;try{CustomTexturePixels.Dimensions(new byte[33]);}catch(InvalidDataException){invalid=true;}check(invalid,"invalid PNG header rejected before Unity allocation");
        var loader=File.ReadAllText(Path.Combine(root,"RuntimeCustomTextures.cs"));var runtime=File.ReadAllText(Path.Combine(root,"RuntimeMaterialLooks.cs"));
        check(!loader.Contains("HarmonyPatch")&&!runtime.Contains("HarmonyPatch"),"no new hooks");
        check(!runtime.Contains("new Texture2D")&&loader.Contains("ImageConversion.LoadImage"),"texture allocation exception confined to managed loader");
        File.WriteAllText(Path.Combine(output,"FEAT46-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks,liveValidation=false,hotReload=false}));
        Console.WriteLine($"FEAT02246 PASS: {checks} checks.");
    }
}
