using System.Security.Cryptography;
using SprocketTelescopicMast;
using SprocketTelescopicMast.Io;
using SprocketUiKit;
using UnityEngine;

internal static class RotatorTests
{
    private static int checks;
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Rotator: " + why); }
    private static void Near(double a,double b,string why,double epsilon=1e-5) => Check(Math.Abs(a-b)<epsilon,why+": "+a+" / "+b);
    private static FlowNode Output(string id="r",string part="plate")
    {
        var n=new FlowNode(id,"rotator"); n.Set("partId",part);
        foreach (var p in new RotatorOutput().Schema.Params) n.Set(p.Id,p.Default);
        return n;
    }
    private sealed class Signals : IVehicleSignals, IRotatorSignals
    {
        public bool IsPlayer { get; set; }=true;
        public bool Braking => false;
        public IReadOnlyList<CannonShot> Fired => Array.Empty<CannonShot>();
        public IReadOnlyList<CannonState> Cannons => Array.Empty<CannonState>();
        public IReadOnlyList<HingeState> Hinges => Array.Empty<HingeState>();
        public IReadOnlyList<MastState> Masts => Array.Empty<MastState>();
        public IReadOnlyList<RotatorState> Rotators { get; set; }=Array.Empty<RotatorState>();
        public bool[] Held=new bool[5]; public int KeyPresses(int n)=>0; public bool KeyHeld(int n)=>Held[n];
    }
    public static int Run()
    {
        IoRegistry.ClearForTests(); IoRegistry.RegisterInput(new KeyInput()); IoRegistry.RegisterInput(new RotatorStateInput()); IoRegistry.RegisterOutput(new RotatorOutput());
        Geometry(); Motion(); Ports(); Templates(); State(); Storage(); Com(); Panel(); return checks;
    }
    private static void Geometry()
    {
        foreach(bool plate in new[]{false,true})
        {
            byte[] data=plate ? RotatorMeshData.Plate() : RotatorMeshData.Base();
            Check(Convert.ToHexString(SHA256.HashData(data)).Equals(plate?RotatorMeshData.PlateDataSHA256:RotatorMeshData.BaseDataSHA256,StringComparison.OrdinalIgnoreCase),"compiled mesh byte hash");
            byte[] indexData=plate?RotatorMeshData.PlateIndices():RotatorMeshData.BaseIndices();
            Check(Convert.ToHexString(SHA256.HashData(indexData)).Equals(plate?RotatorMeshData.PlateIndexSHA256:RotatorMeshData.BaseIndexSHA256,StringComparison.OrdinalIgnoreCase),"compiled index byte hash");
            var unit=RotatorMesh.Create(plate,1,1);
            Check(unit.triangles.Length==indexData.Length/4 && unit.triangles.Length==(plate?RotatorMeshData.PlateIndexCount:RotatorMeshData.BaseIndexCount),"runtime uses exported index count");
            Check(unit.vertices.Length==(plate?RotatorMeshData.PlateVertexCount:RotatorMeshData.BaseVertexCount),"runtime indexed vertex count");
            for(int i=0;i<unit.triangles.Length;i++) Check(unit.triangles[i]==BitConverter.ToInt32(indexData,i*4),"runtime retains topology/winding");
            for(int i=0;i<unit.vertices.Length;i++)
            {
                Near(unit.uv[i].x,BitConverter.ToSingle(data,i*32+24),"runtime UV U",1e-7);
                Near(unit.uv[i].y,BitConverter.ToSingle(data,i*32+28),"runtime UV V",1e-7);
                var n=unit.normals[i]; Near(n.x*n.x+n.y*n.y+n.z*n.z,1,"unit authored normal",1e-6);
            }
            string source=Path.Combine("rotator-assets",plate?"rotator_plate.stl":"rotator_base.stl");
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).Equals(plate?RotatorMeshData.PlateSourceSHA256:RotatorMeshData.BaseSourceSHA256,StringComparison.OrdinalIgnoreCase),"compiled STL hash");
            foreach(float x in new[]{.1f,.7f,1,3,5}) foreach(float z in new[]{.1f,.7f,1,3,5})
            {
                var mesh=RotatorMesh.Create(plate,x,z); Check(mesh.vertices.Length==data.Length/32,"indexed STL vertices used");
                float minimumY=float.MaxValue;
                double volume=0;
                for(int i=0;i<mesh.vertices.Length;i++)
                {
                    var v=mesh.vertices[i]; int p=i*32;
                    Near(v.x*x,BitConverter.ToSingle(data,p)*x,"native X application",1e-6);
                    Near(v.z*z,BitConverter.ToSingle(data,p+8)*x,"native Z application cancels Z scale",1e-6);
                    Near(v.y,BitConverter.ToSingle(data,p+4),"height unchanged",1e-6);
                    minimumY=Math.Min(minimumY,v.y);
                }
                Near(minimumY,plate?.06:0,"assembly mounting plane");
                for(int i=0;i<mesh.triangles.Length;i+=3)
                {
                    var a=mesh.vertices[mesh.triangles[i]]; var b=mesh.vertices[mesh.triangles[i+1]]; var c=mesh.vertices[mesh.triangles[i+2]];
                    volume+=(a.x*(b.y*c.z-b.z*c.y)+a.y*(b.z*c.x-b.x*c.z)+a.z*(b.x*c.y-b.y*c.x))/6.0;
                    var normal=mesh.normals[mesh.triangles[i]];
                    Check(float.IsFinite(normal.x+normal.y+normal.z),"finite compensated normal");
                }
                Near(Math.Abs(volume)*x*z,(plate?RotatorMeshData.PlateVolume:RotatorMeshData.BaseVolume)*x*x,"steel volume follows round mesh",2e-6);
                Near(RotatorMesh.Mass(plate,x,1),(plate?RotatorMeshData.PlateVolume:RotatorMeshData.BaseVolume)*x*x*7850,"steel mass",.001);
            }
        }
    }
    private static void Motion()
    {
        var n=Output();var m=new RotatorMotion(); var run=new RotatorCommand(true,false,false,false);
        m.Tick(n,run,.15); Near(m.Velocity,15,"half ramp"); Near(m.Angle,1.125,"ramp integration");
        m.Tick(n,run,.15); Near(m.Velocity,30,"full 0.3s ramp"); Near(m.Angle,4.5,"full ramp distance");
        m.Tick(n,default,.15); Near(m.Velocity,15,"braking half ramp");
        m.Tick(n,default,.15); Near(m.Velocity,0,"braking takes 0.3s"); double stopped=m.Angle;
        m.Tick(n,default,1);Near(m.Angle,stopped,"all off holds angle");
        m.Reset();n.Set("direction","CCW");m.Tick(n,run,.3);Near(m.Velocity,-30,"CCW");
        m.Tick(n,run with { Reverse=true },.3);Near(m.Velocity,30,"reverse flips standard CCW");
        m.Tick(n,run with { Fast=true },.3);Near(m.Velocity,-90,"fast speed");
        foreach(double dt in new[]{.001,.016,.1,.5,2.0})
        {
            m.Reset();n.Set("fast","999999");
            for(int i=0;i<30;i++){m.Tick(n,run with {Fast=true},dt);Check(Math.Abs(m.Velocity)<=720,"720 hard limit at every dt");Check(m.Angle>=0&&m.Angle<360,"normalized angle");}
        }
        n=Output();
        foreach(var (start,target) in new[]{(350.0,10),(10.0,350),(170.0,190),(190.0,170),(0.0,359),(359.0,0)})
        {
            m.Reset(); n.Set("slow","360"); m.Tick(n,run,.3); // 54 degrees
            double seconds=RotatorMotion.Wrap(start-54)/360;
            m.Tick(n,run,seconds); m.Tick(n,default,.3); // stop adds 54
            // Use target relative to the actual angle, preserving both wrap and ordinary shortest paths.
            double goal=RotatorMotion.Wrap(m.Angle+RotatorMotion.Delta(start,target)); n.Set("target",goal.ToString(System.Globalization.CultureInfo.InvariantCulture));
            double delta=RotatorMotion.Delta(m.Angle,goal); double before=m.Angle;
            m.Tick(n,new(true,true,false,true),.02);
            Check(Math.Sign(RotatorMotion.Delta(before,m.Angle))==Math.Sign(delta),"home wins run/reverse and starts shortest direction");
            for(int i=0;i<1500;i++) m.Tick(n,new(true,true,false,true),.01);
            Near(RotatorMotion.Delta(m.Angle,goal),0,"home reaches target",1e-5);Near(m.Velocity,0,"home holds");
            Check(m.Footer(n).Contains("holding"),"home footer");
        }
        m.Reset();n=Output();m.Tick(n,run,.3); Check(m.Footer(n).Contains("Spinning CW")&&m.Footer(n).Contains("30"),"live spin footer");
        m.Reset();n.Set("fast","720");m.Tick(n,run with {Fast=true},.3);double old=m.Velocity;
        n.Set("target",Math.Floor(m.Angle+1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        m.Tick(n,new(true,false,true,true),.01);Check(Math.Abs(old-m.Velocity)<=24.00001,"home asserted near target preserves bounded braking from high speed");
        for(int i=0;i<3000;i++)m.Tick(n,new(false,false,true,true),.01);
        Near(RotatorMotion.Delta(m.Angle,n.Number("target")),0,"moving-to-home eventually holds");Near(m.Velocity,0,"moving-to-home settles");
        m.Reset();Near(m.Angle,0,"spawn/designer angle reset");Near(m.Velocity,0,"spawn/designer velocity reset");
    }
    private static void Ports()
    {
        var f=new FlowDocument(); var n=Output();f.Nodes.Add(n);
        for(int i=0;i<4;i++){var k=new FlowNode("k"+i,"key");k.Set("signal","held");k.Set("action",(i+1).ToString());f.Nodes.Add(k);f.Wires.Add(new(k.Id,n.Id,i));}
        Check(FlowValidation.Ports(n)==4&&FlowValidation.Validate(f).Count==0,"four valid ports");
        Check(FlowPresentation.Port(n,0)=="run"&&FlowPresentation.Port(n,1)=="reverse"&&FlowPresentation.Port(n,2)=="fast"&&FlowPresentation.Port(n,3)=="home","exact port names/order");
        var engine=new FlowEngine();engine.Configure(f); var signals=new Signals();double t=0;
        foreach(int mask in Enumerable.Range(0,16))
        {
            for(int i=0;i<4;i++)signals.Held[i+1]=(mask&(1<<i))!=0;
            engine.Tick(t+=.2,signals); var c=engine.Rotator(n.Id);
            Check(c==new RotatorCommand((mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0),"all port combinations");
        }
        signals.IsPlayer=false;engine.Tick(t+=.2,signals);Check(engine.Rotator(n.Id)==default,"AI sees no player keys");
        engine.PartExists=_=>false;engine.Tick(t+=.2,signals);Check(engine.Rotator(n.Id)==default&&engine.Status[n.Id]=="missing part","missing plate cannot run");
        Check(FlowValidation.ValidateWire(f,new("k0",n.Id,4))!=null,"fifth input rejected");
        signals.IsPlayer=true;engine.PartExists=_=>true;
        for(int i=0;i<12;i++){signals.Held[1]=i%2==0;engine.Tick(t+=.02,signals);}
        Check(engine.Status[n.Id]=="loop paused"&&engine.Rotator(n.Id)==default,"chattering output pauses safely");
        signals.Held[1]=true;for(int i=0;i<120;i++)engine.Tick(t+=.02,signals);
        Check(engine.Rotator(n.Id).Run&&!engine.Status.ContainsKey(n.Id),"stable command recovers after two-second loop guard");
    }
    private static void Templates()
    {
        var part=new FlowPart("p","rotator","Radar");
        foreach(string id in new[]{FlowTemplates.Spin,FlowTemplates.Home})
        {
            var f=new FlowDocument();var editor=new FlowEditor(f);int saves=0;editor.Saved=()=>saves++;
            Check(editor.Template(id,new[]{new FlowPart("h","hinge","Hinge"),part}),"template finds rotator");
            Check(saves==1&&f.Rev==1&&f.Nodes.Count==2&&f.Wires.Count==1,"one atomic connected template edit");
            Check(f.Wires[0].Port==(id==FlowTemplates.Home?3:0),"correct template port");
            var key=f.Nodes.Single(n=>n.Type=="key");Check(key.Get("signal")=="held","held key input");
            var n=f.Nodes.Single(n=>n.Type=="rotator");Check(n.Number("slow")==30&&n.Number("fast")==90&&n.Get("direction")=="CW"&&n.Number("target")==0,"output defaults");
            var preview=new FlowPreview();preview.Configure(f,_=>true,new[]{part});preview.Test(key);preview.Tick(.1);preview.Tick(.2);
            Check(preview.Engine.Rotator(n.Id)==(id==FlowTemplates.Home?new(false,false,false,true):new(true,false,false,false)),"template engine actually drives correct command");
            if(id==FlowTemplates.Spin){Check(preview.Position(n.Id)>0,"designer simulation rotates");preview.Test(key);preview.Tick(.3);Check(!preview.Engine.Rotator(n.Id).Run,"release clears held run");}
            n.Set("fast","200");editor.SelectNode(n.Id);Check(editor.Template(id,new[]{part}),"selected plate reused");Check(f.Nodes.Count(n=>n.Type=="rotator")==1&&n.Number("fast")==200,"no duplicate or lost settings");
            var json=FlowJson.Write(f);Check(FlowJson.Write(FlowJson.Parse(json))==json,"template JSON round trip");
        }
        Check(FlowTemplates.Target(FlowTemplates.Brake,new(),null,new[]{part})==null,"brake does not claim rotator as API output");
        Check(!new FlowEditor(new()).Template(FlowTemplates.Spin,Array.Empty<FlowPart>()),"no rotator is no-op");
    }
    private static void State()
    {
        var input=new RotatorStateInput();var instance=new InputInstance(input.Id,new Dictionary<string,string>{{"plate","p"},{"from","350"},{"to","10"}});var s=new Signals();
        foreach(var (angle,expected) in new[]{(349.0,false),(350.0,true),(359.9,true),(0.0,true),(360.0,true),(10.0,true),(10.1,false),(-1.0,true)})
        {s.Rotators=new[]{new RotatorState("p","Radar",angle)};Check(input.Read(instance,s).Level==expected,"wrapped state range "+angle);}
        s.Rotators=new[]{new RotatorState("other","Radar",0)};Check(!input.Read(instance,s).Level,"part identity does not match by title");
        Check(!input.Read(instance,null).Level,"no context off");
        Check(RotatorMotion.InRange(45,30,60)&&!RotatorMotion.InRange(300,30,60),"ordinary range");
    }
    private static void Storage()
    {
        string plateId=Guid.NewGuid().ToString("D");
        var f=new FlowDocument();var editor=new FlowEditor(f);editor.Template(FlowTemplates.Spin,new[]{new FlowPart(plateId,"rotator","Radar")});
        var store=new FlowStorage();string a=store.Attach("base",Guid.NewGuid().ToString("D"),f),b=store.Attach("plate",plateId,f);store.Set(f);
        store.Publish(f);
        var copies=new Dictionary<string,string>{{"base",FlowJson.Envelope(store.Id("base")!,store.Flow!)},{"plate",FlowJson.Envelope(store.Id("plate")!,store.Flow!)}};
        Check(copies.Count==2,"base and plate each receive a flow copy");
        var loaded=new FlowStorage();foreach(var pair in copies){var env=FlowJson.ReadEnvelope(pair.Value);loaded.Attach(pair.Key,env.PartId,env.Flow);}
        Check(loaded.Flow!=null&&FlowJson.Write(loaded.Flow)==FlowJson.Write(f),"base/plate clean managed reload");
        Check(loaded.Contains(a)&&loaded.Contains(b),"both stable ioPartIds survive");
        var fresh=new RotatorMotion();Near(fresh.Angle,0,"flow reload starts angle zero");
        Check(!FlowJson.Write(f).Contains("velocity")&&!FlowJson.Write(f).Contains("currentAngle"),"transient motion absent from flow");
    }
    private static void Com()
    {
        var gate=new RotatorComGate();int count=0;
        for(int i=0;i<1000;i++)if(gate.Tick(i*.001,true,true))count++;
        Check(count==10,"moving COM maximum ten Hz");Check(gate.Tick(1,true,false),"one final stopped COM refresh");
        Check(!gate.Tick(1.01,false,false)&&!gate.Tick(2,false,false),"no idle refreshes");
    }
    private static void Panel()
    {
        HingePanel.Destroy();var f=new FlowDocument();var n=Output();f.Nodes.Add(n);
        HingeRuntime.Current=new(){Flow=f,Parts=new(){new("plate","rotator","Radar")}};
        UiKit.Context=UiContexts.Designer;Time.unscaledTime=100;HingePanel.Create(new());UiKit.Latest.Open();
        var graph=NodeGraph.Latest;Check(graph.Nodes[n.Id].Ports.Count==4,"actual panel exposes four input ports");
        graph.Click!(new(){Kind=GraphHitKind.Node,NodeId=n.Id});UiKit.Poll();
        Check(UiKit.SegmentedFields.Contains("Direction")&&!UiKit.SegmentedFields.Contains("When the signal arrives"),"rotator inspector direction uses Segmented without hinge modes");
        Check(new[]{"Slow speed","Fast speed","Target angle"}.All(UiKit.StepperFields.Contains),"rotator inspector numeric parameters use steppers");
        int structure=graph.StructuralCalls;
        for(int i=0;i<30;i++){Time.unscaledTime+=.11f;HingePanel.Update();UiKit.Poll();}
        Check(graph.StructuralCalls==structure,"rotator refresh keeps retained graph");HingePanel.Destroy();HingeRuntime.Current=null;
    }
}
