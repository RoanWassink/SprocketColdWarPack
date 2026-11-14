using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using SprocketTelescopicMast;
using SprocketTelescopicMast.Io;
using SprocketUiKit;
using UnityEngine;

internal static class Continuation091Tests
{
    private static int checks;
    private static void Check(bool condition,string why) { checks++; if(!condition) throw new Exception("0.9.1b: "+why); }
    private sealed class Signals : IVehicleSignals
    {
        public bool IsPlayer => true;
        public bool Braking => false;
        public List<CannonShot> Shots = new();
        public IReadOnlyList<CannonShot> Fired => Shots;
        public IReadOnlyList<CannonState> Cannons { get; set; } = new[] { new CannonState(275,8,true,true,false,1) };
        public IReadOnlyList<HingeState> Hinges => Array.Empty<HingeState>();
        public IReadOnlyList<MastState> Masts => Array.Empty<MastState>();
        public int KeyPresses(int action) => 0;
        public bool KeyHeld(int action) => false;
    }
    private static void Register()
    {
        IoRegistry.ClearForTests();
        IoRegistry.RegisterInput(new CannonFiredInput());IoRegistry.RegisterInput(new CannonReloadedInput());
        IoRegistry.RegisterInput(new CannonReloadingInput());IoRegistry.RegisterInput(new CannonLoadedInput());
        IoRegistry.RegisterInput(new KeyInput());IoRegistry.RegisterInput(new VehicleBrakingInput());
        IoRegistry.RegisterOutput(new HingeOutputForTests());IoRegistry.RegisterOutput(new RotatorOutput());
    }
    private sealed class HingeOutputForTests : IOutputType
    {
        public string Id=>"hinge";public string DisplayName=>"Hinge";public SignalKind SignalKind=>SignalKind.Level;
        public ParamSchema Schema=>ParamSchema.Empty;public IReadOnlyList<IoCommand> Commands=>new[]{IoCommand.Follow};
        public string Describe(IReadOnlyDictionary<string,string> p)=>"Hinge";
    }
    public static int Run()
    { Register();Always();CannonDefaults();VehicleIsolation();Hints();Panel();return checks; }
    private static void Always()
    {
        var editor=new FlowEditor(new());var on=editor.Place("always",0,0)!;
        var output=editor.Place("rotator",250,0,new("plate","rotator","Plate"))!;
        var hinge=editor.Place("hinge",250,200,new("flap","hinge","Flap"))!;
        Check(FlowPresentation.Name(on.Type)=="Always on","palette creates Always on logic");
        Check(FlowValidation.Ports(on)==0 && FlowEditor.HasOutput(on),"no input, one output");
        editor.Flow.Connect(on.Id,output.Id,0);editor.Flow.Connect(on.Id,hinge.Id,0);
        Check(FlowValidation.Validate(editor.Flow).Count==0,"Always on wiring valid");
        Check(FlowValidation.ValidateWire(editor.Flow,new(output.Id,on.Id,0))!=null,"Always on rejects input wire");
        var json=FlowJson.Write(editor.Flow);var round=FlowJson.Parse(json);
        Check(FlowJson.Write(round)==json && round.Nodes.Single(n=>n.Type=="always").Json==on.Json,"always lossless storage round trip");
        var storage=new FlowStorage();storage.Attach("model","plate",round);storage.Attach("flap-model","flap",round);
        Check(storage.Flow!.Nodes.Any(n=>n.Type=="always"),"stored per-part flow retains always");
        var preview=new FlowPreview();preview.Configure(round,_=>true,new[]{new FlowPart("plate","rotator","Plate"),new FlowPart("flap","hinge","Flap")});
        for(int i=0;i<1000;i++)
        {
            preview.Tick(.02);Check(preview.Engine.Rotator(output.Id).Run,"always holds run through all ticks");
            Check(preview.Engine.Target(hinge.Id),"always holds Follow open");
        }
        Check(preview.Position(output.Id)>0 && preview.Engine.RotatorFooters[output.Id].Contains("Spinning"),"Plate continues rotating");
        Check(FlowPresentation.Summary(round.Nodes.Single(n=>n.Type=="always"),preview.Engine)=="ON","footer ON");
        var reloaded=new FlowEngine();reloaded.Configure(FlowJson.ReadEnvelope(FlowJson.Envelope("plate",round)).Flow);reloaded.Tick(0,null);
        Check(reloaded.Rotator(output.Id).Run,"saved/reloaded Always on resumes without an input event");
    }
    private static void CannonDefaults()
    {
        var s=new Signals();
        foreach(var type in new[]{"cannon.fired","cannon.reloaded","cannon.reloading","cannon.loaded"})
        {
            var e=new FlowEditor(new());var node=e.Place(type,0,0)!;
            Check(node.Get("fireGroup")=="0" && node.Get("cannon")=="any","new "+type+" defaults Any group");
            Check(IoRegistry.FindInput(type)!.Describe(node.InputParams).Contains("any cannon"),"any cannon description");
            var input=new InputInstance(type,node.InputParams);
            if(type=="cannon.fired")s.Shots.Add(new(275,8));
            if(type=="cannon.loaded")s.Cannons=new[]{new CannonState(275,8,false,false,true,1)};
            var sample=IoRegistry.FindInput(type)!.Read(input,s);
            Check(sample.Level || sample.Events>0,"new "+type+" responds to group 4");
        }
        var editor=new FlowEditor(new());var fired=editor.Place("cannon.fired",0,0)!;
        var output=editor.Place("rotator",200,0,new("plate","rotator","Plate"))!;
        editor.Flow.Connect(fired.Id,output.Id,0);
        var engine=new FlowEngine();engine.Configure(editor.Flow);engine.Tick(0,s);
        Check(engine.Rotator(output.Id).Run,"group 4 fires new rotator driver");
        s.Shots.Clear();engine.Tick(.02,s);Check(!engine.Rotator(output.Id).Run,"fired stays momentary, no accidental latch");
        fired.Set("fireGroup","1");var json=FlowJson.Write(editor.Flow);var saved=FlowJson.Parse(json);
        Check(saved.Nodes.First().Get("fireGroup")=="1" && FlowJson.Write(saved)==json,"explicit saved group 1 unchanged");
        engine=new();engine.Configure(saved);s.Shots.Add(new(275,8));engine.Tick(0,s);
        Check(!engine.Rotator(output.Id).Run,"saved group 1 rejects counted group 4 shot (original failure reproduced)");
        s.Shots.Clear();engine.Tick(.1,s);s.Shots.Add(new(275,1));engine.Tick(.2,s);
        Check(engine.Rotator(output.Id).Run,"saved group 1 still accepts group 1");
        Check(CannonParams.Groups[0]==new ParamOption("0","Any group") && CannonParams.Groups.Count==FireGroups.Max+1,"Any group editor choice");
    }
    private static void VehicleIsolation()
    {
        // Same VUID, part IDs and flow JSON on two live vehicle clones must never share shots or engine state.
        var buffer=new CannonShotBuffer();IntPtr a=new(1780889562784),b=new(1781005740864);
        var editor=new FlowEditor(new());var fired=editor.Place("cannon.fired",0,0)!;
        var output=editor.Place("rotator",200,0,new("same-part-id","rotator","Plate"))!;editor.Flow.Connect(fired.Id,output.Id,0);
        FlowEngine Engine(){var e=new FlowEngine();e.Configure(FlowJson.Parse(FlowJson.Write(editor.Flow)));return e;}
        var ea=Engine();var eb=Engine();var sa=new Signals();var sb=new Signals();
        buffer.Add(a,new(275,8));buffer.Take(b,sb.Shots);eb.Tick(0,sb);
        Check(!eb.Rotator(output.Id).Run && sb.Shots.Count==0,"other vehicle cannot drain event with same VUID");
        buffer.Take(a,sa.Shots);ea.Tick(0,sa);Check(ea.Rotator(output.Id).Run && sa.Shots.Count==1,"event reaches its own vehicle once");
        sa.Shots.Clear();buffer.Take(a,sa.Shots);ea.Tick(.02,sa);Check(!ea.Rotator(output.Id).Run && sa.Shots.Count==0,"no event replay on second tick");
        buffer.Add(b,new(275,8));buffer.Add(a,new(275,8));buffer.Discard(a);buffer.Take(b,sb.Shots);eb.Tick(.1,sb);
        Check(eb.Rotator(output.Id).Run && sb.Shots.Count==1,"discard/release of one vehicle preserves the other");
        var fresh=Engine();sa.Shots.Clear();buffer.Take(a,sa.Shots);fresh.Tick(0,sa);
        Check(!fresh.Rotator(output.Id).Run,"fresh session cannot replay discarded shot");
    }
    private static void Hints()
    {
        var f=new FlowDocument();var source=new FlowNode("source","cannon.fired");var target=new FlowNode("target","rotator");target.Set("partId","plate");
        f.Nodes.AddRange(new[]{source,target});
        for(int p=0;p<4;p++)Check(FlowPresentation.MomentaryToLevel(f,new(source.Id,target.Id,p)),"fired hint for rotator port "+p);
        source.Data["type"]="key";Check(FlowPresentation.MomentaryToLevel(f,new(source.Id,target.Id,0)),"pressed key hint");
        source.Set("signal","held");Check(!FlowPresentation.MomentaryToLevel(f,new(source.Id,target.Id,0)),"held key is a level");
        source.Data["type"]="always";Check(!FlowPresentation.MomentaryToLevel(f,new(source.Id,target.Id,0)),"always is a level");
        source.Data["type"]="cannon.reloading";Check(!FlowPresentation.MomentaryToLevel(f,new(source.Id,target.Id,0)),"reloading level no hint");
        source.Data["type"]="cannon.fired";target.Data["type"]="hinge";
        Check(FlowPresentation.MomentaryToLevel(f,new(source.Id,target.Id,0)),"Follow hinge hint");
        foreach(var mode in new[]{"Toggle","Open","Close"}){target.Set("mode",mode);Check(!FlowPresentation.MomentaryToLevel(f,new(source.Id,target.Id,0)),mode+" edge command has no warning");}
        target.Data["type"]="memory";target.Data["params"]=new System.Text.Json.Nodes.JsonObject();
        Check(!FlowPresentation.MomentaryToLevel(f,new(source.Id,target.Id,0)),"Memory accepts event");
    }
    private static void Panel()
    {
        HingePanel.Destroy();HingeRuntime.Current=null;UiKit.Context=UiContexts.Designer;Time.unscaledTime=0;HingePanel.Create(new());
        var e=new FlowEditor(new());var source=e.Place("cannon.fired",0,0)!;var part=new FlowPart("plate","rotator","Plate");var target=e.Place("rotator",200,0,part)!;
        e.Flow.Connect(source.Id,target.Id,0);HingeRuntime.Current=new(){Flow=e.Flow,Parts=new(){part}};UiKit.Latest.Open();UiKit.Poll();
        Check(UiKit.Buttons.ContainsKey("Always on"),"actual Logic palette contains Always on");
        var graph=NodeGraph.Latest;var wire=graph.Wires.Keys.Single();graph.Click!(new(){Kind=GraphHitKind.Wire,WireId=wire});UiKit.Poll();
        bool Visible()=>UiKit.Warnings.Any(w=>w.Text==FlowPresentation.MomentaryWarning && w.Visible());
        Check(Visible(),"actual selected-wire inspector displays Kit warning");
        source.Data["type"]="cannon.reloading";Check(!Visible(),"actual warning hides for level to level");
        source.Data["type"]="cannon.fired";graph.Click!(new(){Kind=GraphHitKind.Node,NodeId=target.Id});UiKit.Poll();
        Check(Visible(),"output inspector also warns about incoming momentary wire");
        HingePanel.Destroy();HingeRuntime.Current=null;
    }
}
