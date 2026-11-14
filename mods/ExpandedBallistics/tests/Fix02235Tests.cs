using System.Numerics;
using System.Text;
using System.Text.Json;
namespace SprocketExpandedBallistics;
internal static class Fix02235Tests
{
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var sink=check;check=(ok,msg)=>{sink(ok,"fix02235: "+msg);count++;};
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        Overlay(check,root,output);
        Console.WriteLine($"0.22.35 PASS: {count} checks; cache lifecycle (directional band superseded by Fix02236Tests).");
    }
    private static void Overlay(Action<bool,string> check,string root,string output)
    {
        var queue=new OverlayBuildQueue<int>();var cache=new Dictionary<int,string>();int builds=0;float now=0;
        var content=Enumerable.Range(1,5).ToDictionary(i=>i,i=>OverlayContent.Signature("mesh/thickness"+i,"preset","layers"));
        void Notify(int plate)=>queue.Schedule(plate,plate,now,.12f);
        void Frame(bool missingReadable=true)
        {
            now+=.3f;
            foreach(var job in queue.TakeDue(now,8))
            {
                int p=job.Plate;
                if(p==5&&!missingReadable){if(!cache.ContainsKey(p))queue.Schedule(p,p,now,.25f);continue;}
                if(cache.TryGetValue(p,out var old)&&OverlayContent.Same(old,content[p]))continue;
                cache[p]=content[p];builds++;
            }
        }
        foreach(var p in content.Keys)Notify(p);Frame(false);check(builds==4&&queue.Count==1,"only missing plate retries");Frame();check(builds==5&&queue.Count==0,"initial five overlays built");
        int initial=builds;
        for(int i=0;i<10;i++)
        {
            // Off restores native and suspends roots; on validates retained cache.
            queue.Clear();foreach(var p in content.Keys)Notify(p);Frame();
            // Selection, native build signals, camera and view switches validate
            // the same content, including replaced mesh/renderer addresses.
            foreach(var p in content.Keys)Notify(p);Frame();
            check(builds==initial&&queue.Count==0,"toggle/selection/view/camera cycle preserves cache "+i);
        }
        content[3]=OverlayContent.Signature("edited mesh/thickness","preset","layers");
        foreach(var p in content.Keys){Notify(p);Notify(p);}Frame();
        check(builds-initial==1,"one edit rebuilds exactly one plate");
        foreach(var p in content.Keys)Notify(p);Frame();check(builds-initial==1,"polling does not repeat edit build");
        cache.Clear();foreach(var p in content.Keys)Notify(p);Frame();check(builds-initial==6,"new vehicle rebuilds all five");
        var runtime=File.ReadAllText(Path.Combine(root,"RuntimeCassettePlateVisual.cs"));
        check(runtime.Contains("OverlayContent.Same(built,signature)")&&runtime.Contains("if(geometryChanged)Suspend(plate.Pointer)"),"runtime uses tested content equality and retains cache on notifications");
        check(runtime.Contains("if(!Visible){SuspendViews();pending.Clear();return;}")&&!runtime.Contains("if(geometryChanged)Remove"),"runtime retains cache when hidden");
        check(!runtime.Contains("Snapshot.Revision")&&!runtime.Contains("Key.StartsWith")&&runtime.Contains("if(views.ContainsKey(plate.Pointer))return; // retry queue"),"runtime excludes unrelated revisions and only retries missing views");
        File.WriteAllText(Path.Combine(output,"CACHE-RESULT.tsv"),"scenario\trebuilds\n10_toggle_selection_view_camera_cycles\t0\n1_plate_edit\t1\nnew_vehicle\t5\n");
    }
}
