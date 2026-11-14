using System.Reflection;
using System.Security.Cryptography;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles.AssetManagement;
using UnityEngine;
namespace SprocketEngineExpansion.Native;

internal static class PowerpackMeshes
{
    private static readonly Dictionary<string,Mesh> meshes=new();
    private static readonly Dictionary<string,Core.PowerpackBridgeMesh> payloads=new();
    private static readonly Dictionary<string,VehicleMesh> vehicles=new();
    internal static Core.PowerpackBridgeMesh Data(string name)
    {
        if(payloads.TryGetValue(name,out var found))return found;
        var entry=PowerpackMeshManifest.Entries.Single(e=>e.Name==name);
        using var stream=typeof(PowerpackMeshes).Assembly.GetManifestResourceStream("Powerpack."+name+".meshbin")
            ??throw new InvalidOperationException("Missing powerpack mesh "+name);
        using var buffer=new MemoryStream();stream.CopyTo(buffer);byte[] bytes=buffer.ToArray();
        if(Convert.ToHexString(SHA256.HashData(bytes))!=entry.DataHash.ToUpperInvariant())throw new InvalidDataException("Powerpack mesh hash "+name);
        var data=Core.MeshPayload.Read(new MemoryStream(bytes));payloads.Add(name,data);return data;
    }
    internal static Mesh Get(string name)
    {
        if(meshes.TryGetValue(name,out var mesh))return mesh;
        var data=Data(name);
        mesh=new Mesh {name="Powerpack "+name,hideFlags=HideFlags.HideAndDontSave};
        mesh.vertices=new Il2CppStructArray<Vector3>(data.Vertices.Select(v=>new Vector3((float)v.X,(float)v.Y,(float)v.Z)).ToArray());
        mesh.triangles=new Il2CppStructArray<int>(data.Triangles);
        mesh.RecalculateNormals();mesh.RecalculateBounds();meshes.Add(name,mesh);return mesh;
    }
    internal static VehicleMesh Vehicle(string part)
    {
        if(vehicles.TryGetValue(part,out var found))return found;
        var value=ScriptableObject.CreateInstance<VehicleMesh>();value.name="Powerpack "+part;
        value.hideFlags=HideFlags.HideAndDontSave;
        var mesh=Get(part+"-lod0");value.levelsOfDetail=new Il2CppReferenceArray<Mesh>(new[]{mesh});
        value.shadowProxy=mesh;value.interactionMesh=mesh;value.damageModelMesh=mesh;
        vehicles.Add(part,value);return value;
    }
    internal static int Triangles(string name)=>PowerpackMeshManifest.Entries.Single(e=>e.Name==name).Triangles;
}
