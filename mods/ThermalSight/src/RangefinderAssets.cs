using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AssetManagement;
using Sprocket.PartImporting;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine;

namespace SprocketThermalSight;

internal static class RangefinderAssets
{
    internal const string Guid = "58b0d970-8065-48ea-9af6-6010948b9f46";
    private static readonly Dictionary<RangefinderKind, Mesh> meshes = new();
    private static readonly List<Texture2D> icons = new();
    private static readonly Dictionary<string, Sprite> sprites = new();
    private static VehicleMaterial? glass;
    private static readonly HashSet<RangefinderKind> appliedLogs=new();
    private static bool modelFailed;
    private static readonly HashSet<string> iconFailed = new();
    private static float scanAt;
    private static string PathFor(string name) => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "assets", name);
    private static string FileFor(RangefinderKind kind)=>kind==RangefinderKind.Optical ? "optical-rangefinder.obj" : kind==RangefinderKind.AutomaticLaser ? "automatic-lrf.obj" : "lrf-concept.obj";
    private static Mesh ReadMesh(RangefinderKind kind)
    {
        var data=PaintedObj.Read(PathFor(FileFor(kind)));
        var created=new Mesh{name="Rangefinder",hideFlags=HideFlags.HideAndDontSave};
        created.vertices=data.Vertices.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();
        created.uv=data.Uv.Select(v=>new Vector2(v.X,v.Y)).ToArray();
        created.normals=data.Normals.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();created.subMeshCount=data.Triangles.Length;
        for(int i=0;i<data.Triangles.Length;i++)created.SetTriangles(data.Triangles[i],i);
        created.RecalculateBounds();return created;
    }
    private static VehicleMaterial Glass()
    {
        if(glass!=null)return glass;
        var shader=Shader.Find("HDRP/Lit") ?? throw new InvalidOperationException("Rangefinder glass shader unavailable");
        var material=new Material(shader){name="Rangefinder optical glass",hideFlags=HideFlags.HideAndDontSave};
        material.SetColor("_BaseColor",new Color(.035f,.065f,.075f,1));material.SetFloat("_Smoothness",.9f);
        material.SetFloat("_DoubleSidedEnable",1);material.SetFloat("_CullMode",0);material.SetFloat("_CullModeForward",0);HDMaterial.ValidateMaterial(material);
        glass=new VehicleMaterial(material){Flags=VehicleMaterialFlags.Unpainted|VehicleMaterialFlags.DisableGrime|VehicleMaterialFlags.CustomShader};return glass;
    }
    internal static void Tick()
    {
        if (!Runtime.HasVehicleContext || modelFailed || Time.unscaledTime < scanAt) return;
        scanAt = Time.unscaledTime + .5f;
        try
        {
            foreach (var vehicle in UnityEngine.Object.FindObjectsOfType<Vehicle>())
            {
                if (vehicle == null || vehicle.Released) continue;
                var objects = vehicle.ObjectReader.Items;
                int count = objects.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
                for (int i = 0; i < count; i++)
                {
                    var components = objects[i]?.Components;
                    if (components == null) continue;
                    int n = components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                    for (int j = 0; j < n; j++)
                    {
                        var component = components[j];
                        if (!Rangefinder.Own(component) || component.models == null) continue;
                        foreach (var group in component.models)
                        {
                            if (group?.lods == null) continue;
                            foreach (var lod in group.lods)
                            {
                                var renderer = lod?.Renderer;
                                var filter = renderer?.GetComponent<MeshFilter>();
                                if (filter == null) continue;
                                var kind = RangefinderDevicePolicy.Kind(component.ComponentID);
                                if (!meshes.TryGetValue(kind, out var mesh)) { mesh = ReadMesh(kind); meshes.Add(kind, mesh); }
                                if (filter.sharedMesh != mesh)
                                {
                                    group.SetMesh(mesh);
                                    group.SetInteractionMesh(mesh);
                                    group.SetCollisionMesh(mesh);
                                }
                                var current=group.Materials;
                                var paint=ExteriorPaint.Get(component,current!=null && current.Length>0 ? current[0] : group.Material);
                                if(paint!=null)
                                {
                                    var surfaces=CamoSurfaces.Layout(FileFor(kind)).Surfaces;
                                    var materials=surfaces.Select(s=>s.Paint ? paint : Glass()).ToArray();
                                    if(current==null || current.Length!=materials.Length || materials.Where((m,k)=>current[k]?.Pointer!=m.Pointer).Any())
                                    {
                                        group.SetMaterials(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<VehicleMaterial>(materials));
                                        ExteriorPaint.Process(component,paint);
                                        if(appliedLogs.Add(kind))Plugin.Instance.Log.LogInfo($"[Rangefinder model] {kind}; exterior paint linked; glass fixed.");
                                    }
                                }
                                if(kind==RangefinderKind.AutomaticLaser)
                                    LensAssets.IntegratedWindow(renderer!.transform,"Integrated rangefinder glass",true,
                                        new Vector3(0,.1228f,.101f),new Vector3(.666667f,.657778f,1),false);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex) { modelFailed = true; Plugin.Instance.Log.LogWarning("[Rangefinder] Optional device model disabled: " + ex.Message); }
    }
    private static void Card(PartDefinition __0, PartDisplayCard __result)
    {
        if (__0 == null || __result == null || iconFailed.Contains(__0.guid)) return;
        var path = __0.guid switch
        {
            Guid => "lrf-icon-concept.png",
            "af74a44d-65e0-44ae-8ac3-61198eeb1b30" => "automatic-lrf-icon.png",
            "b93efc74-6702-4d2e-9211-dfe313ffdab3" => "optical-rangefinder-icon.png",
            _ => null
        };
        if (path == null) return;
        try
        {
            if (!sprites.TryGetValue(__0.guid, out var sprite))
            {
                var icon = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                icons.Add(icon);
                if (!ImageConversion.LoadImage(icon, File.ReadAllBytes(PathFor(path)), false)) throw new InvalidDataException("Rangefinder icon decode failed.");
                sprite = Sprite.Create(icon, new Rect(0, 0, icon.width, icon.height), new Vector2(.5f, .5f), 100);
                sprite.hideFlags = HideFlags.HideAndDontSave; sprites.Add(__0.guid,sprite);
            }
            __result.Icon = sprite;
        }
        catch (Exception ex) { iconFailed.Add(__0.guid); Plugin.Instance.Log.LogWarning("[Rangefinder] Optional icon disabled: " + ex.Message); }
    }
    internal static void Shutdown()
    {
        foreach (var mesh in meshes.Values) if (mesh != null) UnityEngine.Object.Destroy(mesh);
        foreach (var sprite in sprites.Values) if (sprite != null) UnityEngine.Object.Destroy(sprite);
        foreach (var icon in icons) if (icon != null) UnityEngine.Object.Destroy(icon);
        if(glass?.Material!=null)UnityEngine.Object.Destroy(glass.Material);glass=null;appliedLogs.Clear();
        meshes.Clear(); sprites.Clear(); icons.Clear(); iconFailed.Clear(); modelFailed = false; scanAt = 0;
    }
}
