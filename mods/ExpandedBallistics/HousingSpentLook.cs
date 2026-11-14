namespace SprocketExpandedBallistics;

// 0.22.70: render-only spent look of an ERA housing. Pure rules and the binding book shared by
// the native adapter (EraSpentRender) and the managed tests. No hooks, no model/collider/mass access.
internal enum HousingMember{Missing,Duplicate,Foreign,Intact,Spent}

internal static class HousingSpentPolicy
{
    internal const int FailClosed=-1;
    // The 0.22.50 count rule: visuals[number of spent own units]. Every declared own unit must be exactly one
    // sibling of the same object and root; a missing, duplicate or foreign member keeps the current look.
    internal static int State(string[]? ids,string[]? visuals,Func<string,HousingMember> member)
    {
        if(ids==null||visuals==null||ids.Length==0||visuals.Length!=ids.Length+1)return FailClosed;
        if(ids.Distinct(StringComparer.Ordinal).Count()!=ids.Length)return FailClosed;
        int spent=0;
        foreach(var id in ids)
        {
            var state=member(id);
            if(state==HousingMember.Spent)spent++;
            else if(state!=HousingMember.Intact)return FailClosed;
        }
        return spent;
    }
    // Materials and camo stay on the renderer: the drawn mesh may only change when it has exactly the submesh
    // slots of the intact source, and the renderer has one material per slot.
    internal static bool Fits(int sourceSubmeshes,int targetSubmeshes,int materials)
        =>sourceSubmeshes>0&&targetSubmeshes==sourceSubmeshes&&materials==targetSubmeshes;
    // Only a renderer of this housing (component = group = renderer VUID) that shows the registered intact mesh.
    internal static bool Candidate(int component,int group,int renderer,bool showsIntact)
        =>SpentCellRenderPolicy.ExactOwner(component,group,renderer,showsIntact);
}

// Adapter over native renderers/meshes, implemented by EraSpentRender (Unity) and by the test double.
internal interface IHousingLookHost<TSlot,TMesh> where TSlot:class where TMesh:class
{
    TMesh? Current(TSlot slot);
    // Native wrappers are not reference-stable: identity is decided by the host (Unity object equality).
    bool Same(TMesh? a,TMesh b);
    int Instance(TSlot slot);
    bool Alive(TSlot slot);
    void Bind(TSlot slot,TMesh mesh);
    TMesh Create(string asset);
    void Destroy(TMesh mesh);
    int Submeshes(TMesh mesh);
}

// The private copies of one housing. Every copy is owned here and destroyed exactly once on Restore.
internal sealed class HousingLookBook<TSlot,TMesh> where TSlot:class where TMesh:class
{
    private sealed record View(TSlot Slot,int Instance,TMesh Source,TMesh Copy);
    private readonly List<View> views=new();
    internal int State{get;private set;}
    internal string? Asset{get;private set;}
    internal int Count=>views.Count;
    internal IEnumerable<TMesh> Copies=>views.Select(v=>v.Copy);
    private static bool Owns(IHousingLookHost<TSlot,TMesh> host,View v)
        =>host.Alive(v.Slot)&&host.Instance(v.Slot)==v.Instance&&host.Same(host.Current(v.Slot),v.Copy);
    internal bool AllOwned(IHousingLookHost<TSlot,TMesh> host)=>views.All(v=>Owns(host,v));
    // Put back the original mesh only where the renderer still shows our copy; a newer native binding is kept.
    internal void Restore(IHousingLookHost<TSlot,TMesh> host)
    {
        foreach(var v in views){if(Owns(host,v))host.Bind(v.Slot,v.Source);host.Destroy(v.Copy);}
        views.Clear();State=0;Asset=null;
    }
    // candidates: the renderers of this housing that show its intact mesh, with that mesh.
    // Prepares every copy before any binding changes; on any failure nothing is bound and nothing leaks.
    internal string? Apply(IHousingLookHost<TSlot,TMesh> host,IReadOnlyList<(TSlot Slot,TMesh Source)> candidates,int state,string asset,int materials)
    {
        if(state<=0)throw new ArgumentOutOfRangeException(nameof(state));
        if(candidates.Count==0)return "no renderer of this housing shows its intact mesh";
        var prepared=new List<View>();
        try
        {
            foreach(var (slot,source) in candidates)
            {
                var copy=host.Create(asset);
                prepared.Add(new View(slot,host.Instance(slot),source,copy));
                if(!HousingSpentPolicy.Fits(host.Submeshes(source),host.Submeshes(copy),materials))
                {
                    foreach(var v in prepared)host.Destroy(v.Copy);
                    return $"submesh slots {host.Submeshes(source)} -> {host.Submeshes(copy)} do not fit {materials} renderer material(s)";
                }
            }
        }
        catch{foreach(var v in prepared)host.Destroy(v.Copy);throw;}
        views.AddRange(prepared);
        foreach(var v in prepared)host.Bind(v.Slot,v.Copy);
        State=state;Asset=asset;
        return null;
    }
}
