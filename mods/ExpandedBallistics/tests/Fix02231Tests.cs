using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

internal static class Fix02231Tests
{
    private sealed class Renderer:IOverlayNativeRenderer
    {
        internal bool Alive=true,Value,ThrowRead,ThrowWrite,ThrowAfterWrite;
        internal int Writes;
        public long Identity{get;init;}
        public bool Current=>Alive;
        public bool Hidden
        {
            get{if(ThrowRead)throw new InvalidOperationException("getter");return Value;}
            set
            {
                if(ThrowWrite)throw new InvalidOperationException("setter");
                Value=value;Writes++;
                if(ThrowAfterWrite){ThrowAfterWrite=false;throw new InvalidOperationException("after write");}
            }
        }
    }
    internal static void Run(Action<bool,string> check)
    {
        int checks=0;var sink=check;check=(ok,text)=>{sink(ok,"fix02231: "+text);checks++;};
        var evidence=new StringBuilder("trigger\toriginal\ttracked_after_hide\ttracked_after_restore\tfinal\n");
        foreach(var trigger in new[]{"toggle off","designer edit/quiet","plate Release","plate rebuild","designer exit","scene switch","plugin unload","build exception","material/upload failure","root/renderer unavailable","vehicle save/reload"})
        foreach(bool original in new[]{false,true})
        {
            var errors=new List<Exception>();var ledger=new OverlayVisibility(errors.Add);
            var a=new Renderer{Identity=1,Value=original};var b=new Renderer{Identity=2,Value=false};var unrelated=new Renderer{Identity=3};
            ledger.Hide(10,a);ledger.Hide(10,b);
            check(a.Hidden&&b.Hidden&&ledger.Count==2,"hide exact native renderers "+trigger);
            check(ledger.Intact(10)&&!ledger.Intact(999),"overlay ownership "+trigger);
            ledger.Hide(10,a);check(ledger.Count==2&&a.Writes==1,"repeat hide preserves original "+trigger);
            if(trigger is "designer exit" or "scene switch" or "plugin unload")ledger.RestoreAll();else ledger.Restore(10);
            check(a.Hidden==original&&!b.Hidden&&ledger.Count==0,"original flags restored "+trigger);
            check(unrelated.Writes==0&&errors.Count==0,"unrelated plate untouched "+trigger);
            int writes=a.Writes+b.Writes;ledger.RestoreAll();ledger.Restore(10);
            check(writes==a.Writes+b.Writes,"restore idempotent "+trigger);
            evidence.AppendLine($"{trigger}\t{original}\t2\t{ledger.Count}\t{a.Hidden}");
        }
        {
            var ledger=new OverlayVisibility(_=>{});var a=new Renderer{Identity=1};var b=new Renderer{Identity=2};
            ledger.Hide(10,a);ledger.Hide(20,b);ledger.Restore(10);
            check(!a.Hidden&&b.Hidden&&ledger.Count==1,"plate removal restores only that owner");
            Func<long,bool> owned=key=>key==20;ledger.RestoreOrphans(owned);check(b.Hidden,"active view retained");
            ledger.RestoreOrphans(_=>false);check(!b.Hidden&&ledger.Count==0,"orphan error recovery restores missing view");
            ledger.Hide(10,a);a.Alive=false;var replacement=new Renderer{Identity=1};
            check(!ledger.Intact(10),"replacement invalidates view");int before=a.Writes;ledger.RestoreAll();
            check(a.Writes==before&&replacement.Writes==0&&ledger.Count==0,"replaced/dead original and replacement never written");
        }
        {
            int errors=0;var ledger=new OverlayVisibility(_=>errors++);var a=new Renderer{Identity=1};var b=new Renderer{Identity=2};
            ledger.Hide(10,a);ledger.Hide(10,b);a.ThrowWrite=true;ledger.RestoreAll();
            check(errors==1&&ledger.Count==1&&!b.Hidden,"one restore exception cannot prevent remaining restores");
            a.ThrowWrite=false;ledger.RestoreOrphans(_=>false);check(!a.Hidden&&ledger.Count==0,"failed restore retained for retry");
            a.ThrowAfterWrite=true;bool rejected=false;try{ledger.Hide(10,a);}catch(InvalidOperationException){rejected=true;}
            check(rejected&&!a.Hidden&&ledger.Count==0,"setter throwing after write rolls back recorded original");
            a.ThrowRead=true;rejected=false;try{ledger.Hide(10,a);}catch(InvalidOperationException){rejected=true;}
            check(rejected&&ledger.Count==0,"unreadable original never hidden");
        }
        var vertices=new[]{new Vector3(1,2,3),new Vector3(-2,1,.1f)};
        var normals=new[]{Vector3.UnitZ,Vector3.Normalize(new Vector3(1,2,3))};var beforeVertices=vertices.ToArray();
        foreach(var scale in new[]{Vector3.One,new Vector3(2,.5f,3),new Vector3(-2,.5f,-3),new Vector3(.01f,10,1)})
        {
            var offset=OverlayNormalOffset.Apply(vertices,normals,scale);
            for(int i=0;i<vertices.Length;i++)
            {
                var world=(offset[i]-vertices[i])*scale;
                check(Math.Abs(world.Length()-.0004f)<2e-6,"0.4 mm world offset at scale "+scale);
                check(Vector3.Dot(Vector3.Normalize(world),Vector3.Normalize(normals[i]/scale))>.9999,"offset follows transformed vertex normal "+scale);
            }
            check(vertices.SequenceEqual(beforeVertices),"offset never mutates copied/native input");
        }
        foreach(var scale in new[]{Vector3.Zero,new Vector3(float.NaN,1,1)})
        {bool rejected=false;try{OverlayNormalOffset.Apply(vertices,normals,scale);}catch(InvalidOperationException){rejected=true;}check(rejected,"invalid scale safely skips overlay");}
        check(OverlayMaterialPlan.Candidates[0]=="HDRP/Lit","HDRP/Lit preferred");
        check(OverlayMaterialPlan.TextureInput("Shader Graphs/VehicleLit",_=>true)==null,"unverified VehicleLit paint inputs rejected");
        check(OverlayMaterialPlan.TextureInput("HDRP/Lit",n=>n!="_UVMappingMask")==null,"HDRP requires verified mapping properties");
        foreach(var shader in OverlayMaterialPlan.Candidates)
        {
            string input=OverlayMaterialPlan.TextureInput(shader,_=>true)!;
            check(!string.IsNullOrEmpty(input),"verified texture input "+shader);
            check(OverlayMaterialPlan.TextureInput(shader,_=>false)==null,"missing shader texture input rejected "+shader);
            foreach(float alpha in new[]{1f,.5f})foreach(float smoothness in new[]{0f,.15f,1f})
            {
                var plan=new OverlayMaterialPlan(shader,input,alpha,smoothness);var f=plan.Floats().ToDictionary(p=>p.Name,p=>p.Value);
                check(f["_Metallic"]==0&&f["_Smoothness"]==smoothness,"matte, nonmetal material "+shader);
                check(f["_SurfaceType"]==(alpha<.999f?1:0)&&f["_ZWrite"]==(alpha<.999f?0:1),"explicit surface/depth state "+shader);
                check(plan.Queue==(alpha<.999f?3000:2010)&&f["_DstBlend"]==(alpha<.999f?10:0),"surface queue and blending "+shader);
                check(f["_UVBase"]==0&&OverlayMaterialPlan.UvMask==new Vector4(1,0,0,0),"UV0 selected without planar/triplanar "+shader);
            }
        }
        // Actual depth coordinates from the production mesh-copy algorithm.
        var front=new OverlaySurface(new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitY},new[]{0,1,2},Enumerable.Repeat(Vector3.UnitZ,3).ToArray());
        var back=front with{Vertices=front.Vertices.Select(v=>v+new Vector3(.3f,0,-.2f)).ToArray(),Normals=Enumerable.Repeat(-Vector3.UnitZ,3).ToArray()};
        var side=new OverlaySurface(new[]{front.Vertices[0],front.Vertices[1],back.Vertices[1],back.Vertices[0]},new[]{0,1,2,0,2,3},Enumerable.Repeat(-Vector3.UnitY,4).ToArray());
        var mesh=CassetteNativeOverlay.Copy(front,back,side);
        check(mesh.Uv.All(uv=>uv.X==.5f)&&mesh.Uv.Take(3).All(uv=>uv.Y==0)&&mesh.Uv.Skip(3).Take(3).All(uv=>uv.Y==1),"native front/back carry d in UV0.y");
        var preset=new CassettePreset("test","test","1950.01.01",1,1000,new[]{new CassetteLayer("rha",20,0,"up"),new CassetteLayer("ceramicArray",30,0,"up"),new CassetteLayer("air",50,0,"up")});
        var strip=CassetteDepthStrip.Build(preset);
        foreach(var (d,material) in new[]{(.1f,"rha"),(.3f,"ceramicArray"),(.9f,"air")})
        {
            var uv=Vector2.Lerp(mesh.Uv[6],mesh.Uv[9],d);
            var sampled=uv*OverlayMaterialPlan.UvMask.X;
            check(strip[Math.Clamp((int)(sampled.Y*strip.Length),0,strip.Length-1)]==CassetteDepthStrip.Colour(material),"UV0 shader mapping samples expected layer at "+d);
        }
        check(strip.Count(c=>c==0x090c10ff)==2,"exactly one dark texel per internal boundary");
        var root=Fix02228Tests.SourceRoot();var runtime=File.ReadAllText(Path.Combine(root,"RuntimeCassettePlateVisual.cs"));
        check(runtime.Contains("renderer.forceRenderingOff=value")&&!runtime.Contains("renderer.enabled=false"),"native write limited to reversible visual flag");
        check(runtime.Contains("hidden.Restore(pointer.ToInt64())")&&runtime.Contains("hidden.RestoreAll()")&&runtime.Contains("hidden.RestoreOrphans(hasView)"),"per-plate, global and exception restore paths connected");
        check(runtime.Contains("current.Pointer==renderer.Pointer&&current.GetInstanceID()==instance"),"exact current slot and Unity lifetime guard");
        check(File.ReadAllText(Path.Combine(root,"PlateMeshAccess.cs")).Contains("RuntimeCassettePlateVisual.BeginEdit();")&&runtime.Contains("if(PlateMeshAccess.Gate.Quiet){BeginEdit();return;}"),"edit notification and quiet paths restore before drawing; mouse alone retains cache");
        check(runtime.Contains("plate.highDetailRenderer")&&runtime.Contains("plate.lowDetailRenderer")&&runtime.Contains("plate.shadowProxyRenderer")&&!runtime.Replace("GetComponentsInChildren<VehicleEditorUI>(false)","").Contains("GetComponentsInChildren"),"only exact plate render slots touched; hierarchy lookup restricted to designer UI");
        check(runtime.Contains("HDMaterial.ValidateMaterial(material)")&&runtime.IndexOf("material.SetTexture(input",StringComparison.Ordinal)<runtime.IndexOf("HDMaterial.ValidateMaterial(material)",StringComparison.Ordinal),"installed HDRP validator runs after texture/properties");
        check(runtime.Contains("mesh.uv=data.Uv")&&runtime.Contains("FilterMode.Point")&&runtime.Contains("TextureWrapMode.Clamp")&&runtime.Contains("SetTextureScale(input,Vector2.one)")&&runtime.Contains("SetTextureOffset(input,Vector2.zero)"),"UV0 upload, point/clamp and identity texture transform");
        check(runtime.Contains("if(!materialLogged)")&&runtime.Contains("materialLogged=true;")&&runtime.Contains("\"Smoothness\",.15f"),"one material session log; configurable 0.15 smoothness default");
        check(File.ReadAllText(Path.Combine(root,"Plugin.cs")).Contains("finally{try{RuntimeCassettePlateVisual.Clear();}finally{ShellFeature.Unload();}}"),"unload restores even if another feature cleanup throws");
        check(runtime.Contains("lifecycle.Reset();Left();")&&runtime.Contains("try{FollowCore();}")&&runtime.Contains("[Plate visual] Follow:"),"scene signal and follow failures restore native visibility");
        var destroyed=new List<string>();var resources=new OverlayResources<string>(item=>{destroyed.Add(item);if(item=="mesh")throw new InvalidOperationException("destroy");});
        resources.Own("root");resources.Own("material");resources.Own("mesh");try{resources.Clear();}catch(InvalidOperationException){}
        check(resources.Count==0&&destroyed.Count==3,"resource cleanup continues after individual destroy failure");
        var output=Fix02228Tests.OutputRoot();File.WriteAllText(Path.Combine(output,"VISIBILITY-CHECK.tsv"),evidence.ToString());
        Console.WriteLine($"0.22.31 PASS: {checks} checks; visibility evidence in test-output/VISIBILITY-CHECK.tsv.");
    }
}
