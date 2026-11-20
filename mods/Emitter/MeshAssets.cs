using UnityEngine;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AmmunitionStorage;
using Object=UnityEngine.Object;
namespace SprocketEmitter;

internal sealed class MeshAssets:IDisposable
{
    internal sealed record Item(Mesh Mesh,Vector3 Size,Vector3 Center,int LongAxis,string Source);
    private static readonly string[] Items={"casing","projectile"};
    // Owned clones of the stock rack meshes. They survive Clear (the designer -> Play change replaces the scene)
    // and are destroyed only with the runtime. Native meshes are never borrowed, loaded or destroyed.
    private static readonly Dictionary<string,Mesh> stock=new();
    private static readonly HashSet<string> probed=new();
    private static readonly HashSet<IntPtr> materialLogged=new();
    private static Material? stockMaterial;
    private static string baseProperty="";
    private static readonly string[] BaseProperties={"_BaseColour","_BaseColorMap"};
    // Only our material copy is owned. Textures remain native and shared.
    internal static Material? StockMaterial {
        get {
            if(stockMaterial is null)return null;
            bool exists=stockMaterial!=null;
            bool shader=exists&&stockMaterial!.shader!=null;
            bool texture=shader&&(baseProperty=="mainTexture"?stockMaterial!.mainTexture:stockMaterial!.GetTexture(baseProperty))!=null;
            if(StockSource.MaterialValid(exists,shader,texture))return stockMaterial;
            if(exists)Object.Destroy(stockMaterial!);
            stockMaterial=null;baseProperty="";return null;
        }
    }
    private static Texture? BaseTexture(Material material,out string property) {
        foreach(var key in BaseProperties) {
            if(!material.HasProperty(key))continue;
            var texture=material.GetTexture(key);
            if(texture!=null){property=key;return texture;}
        }
        property="mainTexture";return material.mainTexture;
    }
    private static void KeepMaterial(Material? source) {
        if(StockMaterial!=null||source==null||source.shader==null)return;
        var texture=BaseTexture(source,out var property);if(texture==null)return;
        var copy=new Material(source){hideFlags=HideFlags.HideAndDontSave,name="Emitter owned stock rack material"};
        // Verify the copy too; a source disappearing during harvest is not a usable combination.
        var copiedTexture=BaseTexture(copy,out var copiedProperty);
        if(!StockSource.MaterialValid(copy!=null,copy!.shader!=null,copiedTexture!=null)){Object.Destroy(copy);return;}
        stockMaterial=copy;baseProperty=copiedProperty;
        if(materialLogged.Add(source.Pointer))Plugin.Instance.Log.LogInfo($"[EMITTER] stock material name={source.name} shader={source.shader.name} baseTexture={texture.name} keywordsEnabled={source.shaderKeywords?.Length??0} baseProperty={property}");
    }
    private readonly Dictionary<(IntPtr,string),Item> cache=new();
    private readonly Dictionary<string,Mesh> fallbacks=new();
    private readonly HashSet<string> warned=new();
    internal static Mesh Make(UnitMesh data) {
        var v=new Vector3[data.Vertices.Length/3];for(int i=0;i<v.Length;i++)v[i]=new(data.Vertices[3*i],data.Vertices[3*i+1],data.Vertices[3*i+2]);
        var mesh=new Mesh{name="Emitter "+data.Name,hideFlags=HideFlags.HideAndDontSave};
        mesh.vertices=v;mesh.triangles=data.Indices;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    private static bool Has(string item)=>stock.TryGetValue(item,out var m)&&m!=null;
    internal static bool Missing=>!StockSource.Complete(Has("casing"),Has("projectile"),StockMaterial!=null);
    // 0.1.1: look for the already-loaded shell meshes of any ammunition rack and keep one owned clone per item.
    // Run from the designer too, where racks load their detailed shells, so Play does not depend on the racks.
    // Never initiates or releases a native load request. One probe line per distinct result.
    internal static void Harvest(Vehicle vehicle,string phase) {
        if(!Missing||vehicle==null||vehicle.Released)return;
        int objects=0,racks=0,loaded=0,lod=0;bool hd=false,optimized=false;
        try {
            var items=vehicle.ObjectReader?.Items;
            if(items!=null) {
                int n=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
                for(int i=0;i<n;i++) {
                    var o=items[i];if(o==null||o.Released)continue;objects++;
                    var components=o.Components;if(components==null)continue;
                    int count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                    for(int j=0;j<count;j++) {
                        var rack=components[j]?.TryCast<AmmoRackModel>();if(rack==null)continue;
                        racks++;hd|=rack.hdModel;optimized|=rack.optimizedMode;
                        KeepMaterial(rack.loadedMaterial);
                        foreach(var item in Items) {
                            if(Has(item))continue;
                            var native=item=="casing"?rack.propellantLoadedMesh:rack.projectileLoadedMesh;
                            var lods=native?.LODs;Mesh? source=null;
                            if(lods!=null&&lods.Length>0&&lods[0]!=null){loaded++;source=lods[0];}
                            else {var direct=item=="casing"?rack.propellantMeshLOD:rack.projectileMeshLOD;if(direct!=null){lod++;source=direct;}}
                            if(source!=null)Keep(item,source,phase);
                        }
                    }
                }
            }
        }catch(Exception e){Plugin.Instance.Log.LogWarning("[EMITTER] stock probe failed: "+e.GetType().Name+": "+e.Message);}
        var line=$"stock probe phase={phase} objects={objects} racks={racks} loadedMesh={loaded} lodMesh={lod} hd={hd} optimized={optimized} casing={(Has("casing")?"yes":"no")} projectile={(Has("projectile")?"yes":"no")}";
        if(probed.Count<64&&probed.Add(line))Plugin.Instance.Trace(line);
    }
    private static void Keep(string item,Mesh source,string phase) {
        // Non-generic Object.Instantiate(Object) makes an owned CPU/GPU clone.
        var copy=Object.Instantiate((Object)source).TryCast<Mesh>();if(copy==null)return;
        var size=copy.bounds.size;if(!Valid(size)){Object.Destroy(copy);return;}
        copy.name="Emitter owned stock "+item;copy.hideFlags=HideFlags.HideAndDontSave;stock[item]=copy;
        Plugin.Instance.Log.LogInfo($"[EMITTER] stock mesh item={item} phase={phase} bounds={size} readable={copy.isReadable}");
    }
    internal Item? Get(Vehicle vehicle,string item) {
        var key=(vehicle.Pointer,item);if(cache.TryGetValue(key,out var cached)){if(cached.Mesh!=null)return cached;cache.Remove(key);}
        Harvest(vehicle,"throw");
        if(stock.TryGetValue(item,out var s)&&s!=null){var made=Describe(s,"stock");cache.Add(key,made);return made;}
        if(!Plugin.Instance.AllowFallback.Value) {
            if(warned.Add(item))Plugin.Instance.Log.LogWarning($"[EMITTER] no stock mesh for {item} (see the stock probe lines); nothing is thrown. Meshes.AllowEmbeddedFallback=true uses the plain embedded shape.");
            return null;
        }
        if(!fallbacks.TryGetValue(item,out var f)||f==null){f=Make(item=="casing"?CasingMeshData.Charge8:CasingMeshData.Projectile8);f.name="Emitter owned fallback "+item;fallbacks[item]=f;}
        // Not cached: a later harvest can still switch this item to the stock mesh.
        return Describe(f,"fallback");
    }
    private static Item Describe(Mesh mesh,string source) {
        var bounds=mesh.bounds;var size=bounds.size;
        int axis=size.x>=size.y&&size.x>=size.z?0:size.y>=size.z?1:2;
        return new Item(mesh,size,bounds.center,axis,source);
    }
    private static bool Valid(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)&&v.x>1e-7&&v.y>1e-7&&v.z>1e-7;
    internal static Vector3 Scale(Item item,EjectionSize size)=>EmitterRuntime.Unity(size.Scale(item.Size.x,item.Size.y,item.Size.z,item.LongAxis));
    internal static Quaternion AxisRotation(Item item)=>Quaternion.FromToRotation(item.LongAxis==0?Vector3.right:item.LongAxis==1?Vector3.up:Vector3.forward,Vector3.forward);
    // Per Clear: drop the caches and the embedded fallbacks; the stock clones stay (see DisposeStock).
    public void Dispose(){foreach(var m in fallbacks.Values)if(m!=null)Object.Destroy(m);fallbacks.Clear();cache.Clear();}
    internal static void DisposeStock(){foreach(var m in stock.Values)if(m!=null)Object.Destroy(m);stock.Clear();probed.Clear();if(stockMaterial!=null)Object.Destroy(stockMaterial);stockMaterial=null;baseProperty="";materialLogged.Clear();}
}
