using SprocketExpandedBallistics.Materials;
namespace SprocketExpandedBallistics;

internal static class Fix02218Tests
{
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var assert=check;check=(ok,text)=>{assert(ok,"fix02218: "+text);count++;};
        const MaterialMenuVisibility.MenuGroup B=MaterialMenuVisibility.MenuGroup.Basic,C=MaterialMenuVisibility.MenuGroup.Composites;
        var ids=new[]{"rha","sheetMetal","ceramicArray","cwepGlassTextolite","kontakt5Active","ebCassette_a","ebCassette_user_nera_v1","cwepNeraCassette","somePartOnly"};
        bool Part(string id)=>id=="somePartOnly";
        string[] Filter(bool compositePlate,MaterialMenuVisibility.MenuGroup g)=>ids.Where(i=>MaterialMenuVisibility.Visible(i,Part(i),compositePlate,g)).ToArray();

        // Filter per mode.
        var basicPlain=Filter(false,B);
        check(basicPlain.SequenceEqual(new[]{"rha","sheetMetal","kontakt5Active"}),"basic: steel, ceramic, composite materials and ERA, no cassettes, no retired, no part-only");
        check(Filter(false,C).Length==0,"composites on a plain plate: empty (composite-plate rule stays)");
        var composites=Filter(true,C);
        check(composites.Length==0,"composites: preset and user cassettes only");
        check(Filter(true,B).SequenceEqual(basicPlain),"basic list is the same on a composite plate");
        check(ids.All(i=>MaterialMenuVisibility.Visible(i,Part(i),true)==(Filter(true,B).Contains(i)||Filter(true,C).Contains(i))),"the two groups together equal the old unfiltered list");
        check(!MaterialMenuVisibility.Visible("cwepNeraCassette",false,true,B)&&!MaterialMenuVisibility.Visible("cwepNeraCassette",false,true,C),"retired preset stays hidden in both groups");
        check(MaterialMenuVisibility.GroupOf("ebCassette_user_x")==C&&MaterialMenuVisibility.GroupOf("rha")==B&&MaterialMenuVisibility.GroupOf("reliktActive")==B,"group of id");

        // Index conversion after filtering, with and without prompt.
        foreach(var list in new[]{basicPlain,composites})
        {
            for(int i=0;i<list.Length;i++)
            {
                check(MaterialMenuVisibility.SelectedId(list,i,false)==list[i],"no prompt: index maps to "+list[i]);
                check(MaterialMenuVisibility.SelectedId(list,i+1,true)==list[i],"prompt: index+1 maps to "+list[i]);
            }
            check(MaterialMenuVisibility.SelectedId(list,0,true)==null,"prompt entry selects nothing");
            check(MaterialMenuVisibility.SelectedId(list,list.Length,false)==null&&MaterialMenuVisibility.SelectedId(list,-1,false)==null,"out of range selects nothing");
        }
        check(MaterialMenuVisibility.SelectedId(composites,1,false)==null,"second composites item is the user cassette, not a basic material");

        // Initial state and memory.
        var state=new MaterialMenuVisibility.GroupState();
        check(state.Resolve(1,"ebCassette_user_nera_v1",true,true)==C,"initial: cassette material starts on Composites");
        var rhaState=new MaterialMenuVisibility.GroupState();
        check(rhaState.Resolve(1,"rha",true,true)==B,"initial: RHA starts on Basic");
        rhaState.Choose(1,C);
        check(rhaState.Resolve(1,"rha",true,true)==C,"choice is kept for the same plate");
        check(rhaState.Resolve(1,"ebCassette_a",true,true)==C,"choice is kept when the material changes");
        check(rhaState.Resolve(2,"rha",true,true)==B,"another plate starts from its own material");
        check(rhaState.Resolve(1,"ebCassette_a",true,true)==C,"back on the first plate: follows its material again");

        // Empty group is dimmed / never selected.
        check(rhaState.Resolve(3,"rha",true,false)==B,"no composites: stays on Basic");
        rhaState.Choose(3,C);
        check(rhaState.Resolve(3,"rha",true,false)==B,"remembered empty Composites falls back to Basic");
        check(state.Resolve(4,"ebCassette_a",false,true)==C&&state.Resolve(5,"rha",false,true)==C,"empty Basic falls back to Composites");
        check(MaterialMenuVisibility.EmptyReason(C,false).Contains("composite plates only")&&MaterialMenuVisibility.EmptyReason(C,true).Contains("vehicle date"),"reason for empty composites");
        check(MaterialMenuVisibility.EmptyReason(B,true).Contains("basic")&&MaterialMenuVisibility.GroupLabel(B)=="Basic materials"&&MaterialMenuVisibility.GroupLabel(C)=="Composites","labels and basic reason");
        var plain=Filter(false,B).Length>0;var comp=Filter(false,C).Length>0;
        check(plain&&!comp,"plain plate: Basic enabled, Composites dimmed");
        Console.WriteLine($"0.22.18 checks PASS: {count} checks.");
    }
}