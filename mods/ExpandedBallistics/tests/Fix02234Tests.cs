using System.Numerics;
using System.Security.Cryptography;
using System.Text;
namespace SprocketExpandedBallistics;

internal static class Fix02234Tests
{
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var sink=check;check=(ok,text)=>{sink(ok,"fix02234: "+text);count++;};
        string root=Fix02228Tests.SourceRoot();
        string output=Fix02228Tests.OutputRoot();
        var runtime=File.ReadAllText(Path.Combine(root,"NativeMaterialReactions.cs"));
        check(!runtime.Contains("NativePlateMirror")&&!runtime.Contains("PlateFrontAssignment")&&!runtime.Contains("native front plane unresolved")&&!runtime.Contains("NativeAngle"),"overlay geometry cannot alter protection");
        Discovery(check,root);
        Console.WriteLine($"0.22.34 PASS: {count} checks; unchanged protection geometry and five-plate toggle/load retries; factor oracle moved to Fix02236Tests.");
    }

    private static void Discovery(Action<bool,string> check,string root)
    {
        var queue=new OverlayBuildQueue<int>();var lifecycle=new CassetteDesignerState();
        var drawn=new HashSet<int>();int[] inventory=Array.Empty<int>();float now=0;
        var overlays=new Dictionary<int,OverlayCopy>();
        var front=new OverlaySurface(new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitY},new[]{0,1,2},Enumerable.Repeat(Vector3.UnitZ,3).ToArray());
        var back=front with{Vertices=front.Vertices.Select(v=>v-Vector3.UnitZ*.2f).ToArray(),Triangles=new[]{0,2,1},Normals=Enumerable.Repeat(-Vector3.UnitZ,3).ToArray()};
        var side=new OverlaySurface(new[]{front.Vertices[0],front.Vertices[1],back.Vertices[1],back.Vertices[0]},new[]{0,1,2,0,2,3},Enumerable.Repeat(-Vector3.UnitY,4).ToArray());
        lifecycle.Observe(true,1);
        void Discover(){foreach(int plate in inventory)if(!drawn.Contains(plate))queue.Schedule(plate,plate,now,.12f);}
        void Frame(bool readable=true)
        {
            foreach(var job in queue.TakeDue(now,2))
                if(job.Plate==5&&!readable)queue.Schedule(job.Key,job.Plate,now,.25f);
                else {overlays[job.Plate]=CassetteNativeOverlay.Copy(front,back,side);drawn.Add(job.Plate);}
        }
        Discover();check(queue.Count==0,"designer can enter before vehicle data arrives");
        inventory=new[]{1,2,3,4,5};lifecycle.Toggle(false);Discover();
        check(lifecycle.Visible(false,false)&&queue.Count==5,"toggle-on discovers and queues all five without selection/edit");
        now=.2f;Frame(false);check(drawn.Count==2&&queue.Count==3,"per-frame build budget respected");
        now=.3f;Frame(false);now=.4f;Frame(false);
        check(drawn.SetEquals(new[]{1,2,3,4})&&queue.Count==1,"four readable plates shown, fifth stays queued");
        for(int i=0;i<100;i++){now+=.3f;Discover();Frame(false);}
        check(queue.Count==1&&!drawn.Contains(5),"unreadable retry persists beyond old forty-attempt limit");
        now+=.3f;Frame();check(drawn.Count==5&&queue.Count==0&&overlays.Count==5&&overlays.Values.All(v=>v.Triangles.Length==12&&v.Uv.All(uv=>uv.Y>=0&&uv.Y<=1)),"all five complete production overlay copies appear when readable without an edit");
        Discover();check(queue.Count==0,"cached plates do not rebuild on discovery");
        queue.Schedule(6,6,now,.12f);queue.Clear();drawn.Clear();overlays.Clear();inventory=new[]{11,12,13,14,15};Discover();
        now+=1;Frame();Frame();Frame();check(drawn.SetEquals(inventory)&&queue.Count==0&&overlays.Keys.ToHashSet().SetEquals(inventory),"vehicle replacement discovers all plates and discards old work without edit");
        queue.Schedule(16,16,now,.12f);queue.Remove(16);check(queue.Count==0,"released plate cancels pending retry");
        queue.Schedule(17,17,now,.12f);lifecycle.Toggle(false);queue.Clear();
        check(!lifecycle.Visible(false,false)&&queue.Count==0,"toggle-off cancels pending work");
        var runtime=File.ReadAllText(Path.Combine(root,"RuntimeCassettePlateVisual.cs"));
        check(runtime.Contains("GetComponentsInChildren<VehicleEditorUI>(false)")&&runtime.Contains("source.HasEditor?source.Editor:null"),"runtime discovers editor through active native designer UI without inspector callback");
        check(runtime.Contains("foreach(var item in pending.TakeDue(Time.unscaledTime,8))")&&runtime.Contains("pending.Schedule(plate.Pointer.ToInt64(),plate,Time.unscaledTime,tries<40?.25f:1f)"),"runtime uses tested budget queue and unbounded delayed retries");
        check(runtime.IndexOf("plates[plate.Pointer]=plate;",StringComparison.Ordinal)<runtime.IndexOf("if(!plate.HasMesh)",StringComparison.Ordinal),"unreadable component registered before mesh readiness check");
    }
}
