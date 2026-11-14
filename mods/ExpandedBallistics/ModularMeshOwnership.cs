using System;
using System.Collections.Generic;
using System.Linq;

namespace SprocketExpandedBallistics;

// Pure ownership/refcount ledger for generated meshes. Native wrappers hold a
// reference until they are actually replaced; this class keeps the distinction
// between current and retired generations so the renderer/collider objects are
// never destroyed while still referenced.
internal sealed class ModularMeshOwnership
{
    private sealed class OwnerState
    {
        internal HashSet<string> Current=new(StringComparer.Ordinal);
        internal readonly List<string> Retired=new();
    }

    private readonly Dictionary<IntPtr,OwnerState> owners=new();
    private readonly Dictionary<string,int> users=new(StringComparer.Ordinal);

    internal int Users(string key)=>users.TryGetValue(key,out var count)?count:0;

    internal int RetiredCount(IntPtr owner)
        =>owners.TryGetValue(owner,out var state)?state.Retired.Count:0;

    internal bool IsCurrent(IntPtr owner,string key)
        =>owners.TryGetValue(owner,out var state)&&state.Current.Contains(key);

    internal void Commit(IntPtr owner,IReadOnlyCollection<string> keys,int maximumRetiredGenerations,
        Action<string>? onRetiredCap=null)
    {
        if(maximumRetiredGenerations<1)throw new ArgumentOutOfRangeException(nameof(maximumRetiredGenerations));
        if(!owners.TryGetValue(owner,out var state))owners[owner]=state=new OwnerState();
        var next=new HashSet<string>(keys,StringComparer.Ordinal);
        foreach(var old in state.Current)
            if(!next.Contains(old))state.Retired.Add(old);
        // A size cap is not evidence that native colliders stopped referencing a mesh.
        // Retain every generation until ReleaseRetired (confirmed build) or ReleaseOwner
        // (completed native release). Warn once when crossing the advisory threshold.
        if(state.Retired.Count>maximumRetiredGenerations&&state.Retired.Count-state.Current.Count<=maximumRetiredGenerations)
            onRetiredCap?.Invoke(state.Retired[0]);
        foreach(var key in next)
        {
            if(state.Retired.Remove(key))continue; // already holds a retired reference
            if(!state.Current.Contains(key))Acquire(key);
        }
        state.Current=next;
    }

    internal void ReleaseRetired(IntPtr owner,Func<string,bool>? predicate=null)
    {
        if(!owners.TryGetValue(owner,out var state))return;
        foreach(var key in state.Retired.ToArray())
        {
            if(predicate!=null&&!predicate(key))continue;
            Release(key);
            state.Retired.Remove(key);
        }
    }

    internal void ReleaseOwner(IntPtr owner)
    {
        if(!owners.TryGetValue(owner,out var state))return;
        foreach(var key in state.Current)Release(key);
        foreach(var key in state.Retired)Release(key);
        state.Current.Clear();
        state.Retired.Clear();
        owners.Remove(owner);
    }

    internal void Clear()
    {
        owners.Clear();
        users.Clear();
    }

    private void Acquire(string key)=>users[key]=Users(key)+1;

    private void Release(string key)
    {
        if(!users.TryGetValue(key,out var count))return;
        if(count<=1)users.Remove(key);
        else users[key]=count-1;
    }
}
