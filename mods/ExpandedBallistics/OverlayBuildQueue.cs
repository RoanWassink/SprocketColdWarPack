namespace SprocketExpandedBallistics;

// A pending plate remains pending until built, removed, or hidden. Discovery must
// not postpone an already queued retry; a busy/unreadable mesh has no retry limit.
internal sealed class OverlayBuildQueue<T>
{
    private readonly Dictionary<long,(T Plate,float Due)> items=new();
    internal int Count=>items.Count;
    internal bool Contains(long key)=>items.ContainsKey(key);
    internal void Schedule(long key,T plate,float now,float delay,bool replace=false)
    {
        if(replace||!items.ContainsKey(key))items[key]=(plate,now+delay);
    }
    internal (long Key,T Plate)[] TakeDue(float now,int budget)
    {
        var ready=items.Where(p=>p.Value.Due<=now).Take(budget).Select(p=>(Key:p.Key,Plate:p.Value.Plate)).ToArray();
        foreach(var item in ready)items.Remove(item.Key);
        return ready;
    }
    internal void Remove(long key)=>items.Remove(key);
    internal void Clear()=>items.Clear();
}

// Content only: excludes selection, camera, renderer/mesh addresses and catalogue revisions.
internal static class OverlayContent
{
    internal static string Signature(string mesh,string material,string preset)=>mesh+"|"+material+"|"+preset;
    internal static bool Same(string built,string current)=>StringComparer.Ordinal.Equals(built,current);
}
