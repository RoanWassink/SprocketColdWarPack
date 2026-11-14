namespace SprocketExpandedBallistics;
// Same key, measurement and explicit lifecycle invalidation as 0.22.38.
// 1024 retains four 200-instance sets; insertion beyond the bound keeps the old clear policy.
internal sealed class PlacedDepthCache<TKey> where TKey:notnull
{
    internal const int Capacity=1024;
    private readonly Dictionary<TKey,double> values=new();
    internal int Count=>values.Count;
    internal bool TryGetValue(TKey key,out double depth)=>values.TryGetValue(key,out depth);
    internal double this[TKey key]
    {set{if(values.Count>=Capacity&&!values.ContainsKey(key))values.Clear();values[key]=value;}}
    internal void Clear()=>values.Clear();
}
