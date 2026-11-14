namespace SprocketExpandedBallistics;

// The adapter must identify the exact original renderer in its native slot. Never
// restore a replacement, or infer ownership from a hierarchy/name/material.
internal interface IOverlayNativeRenderer
{
    long Identity { get; }
    bool Current { get; }
    bool Hidden { get; set; }
}

internal sealed class OverlayVisibility
{
    private sealed record Entry(long Owner,IOverlayNativeRenderer Renderer,bool Original);
    private readonly Dictionary<long,Entry> hidden=new();
    private readonly Dictionary<long,List<Entry>> owners=new();
    private readonly List<long> remove=new();
    private readonly Action<Exception> error;
    internal OverlayVisibility(Action<Exception> error){this.error=error;}
    internal int Count=>hidden.Count;
    internal bool Intact(long owner)
    {
        if(!owners.TryGetValue(owner,out var entries))return false;
        foreach(var e in entries)if(!e.Renderer.Current||!e.Renderer.Hidden)return false;
        return entries.Count>0;
    }
    internal void Hide(long owner,IOverlayNativeRenderer renderer)
    {
        if(!renderer.Current)throw new InvalidOperationException("Native renderer was replaced.");
        if(hidden.TryGetValue(renderer.Identity,out var existing))
        {
            if(existing.Owner!=owner)throw new InvalidOperationException("Native renderer already belongs to another overlay.");
            return;
        }
        // Record BEFORE the setter: it may throw after changing the visual flag.
        var entry=new Entry(owner,renderer,renderer.Hidden);
        hidden.Add(renderer.Identity,entry);
        if(!owners.TryGetValue(owner,out var entries))owners.Add(owner,entries=new());
        entries.Add(entry);
        try{renderer.Hidden=true;if(!renderer.Hidden)throw new InvalidOperationException("Renderer.forceRenderingOff did not stick.");}
        catch{Restore(owner);throw;}
    }
    internal void Restore(long owner)=>RestoreWhere(owner,null);
    internal void RestoreAll()=>RestoreWhere(null,null);
    internal void RestoreOrphans(Func<long,bool> owned)=>RestoreWhere(null,owned);
    private void RestoreWhere(long? owner,Func<long,bool>? owned)
    {
        remove.Clear();
        foreach(var pair in hidden)
        {
            if(owner.HasValue&&pair.Value.Owner!=owner.Value||owned!=null&&owned(pair.Value.Owner))continue;
            try
            {
                var e=pair.Value;
                if(e.Renderer.Current)e.Renderer.Hidden=e.Original;
                remove.Add(pair.Key);
            }
            catch(Exception ex){error(ex);} // retain failed restores for the next main-thread attempt
        }
        foreach(var key in remove)
        {
            var e=hidden[key];hidden.Remove(key);
            var entries=owners[e.Owner];entries.Remove(e);if(entries.Count==0)owners.Remove(e.Owner);
        }
    }
}
