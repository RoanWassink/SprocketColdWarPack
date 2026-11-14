using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace SprocketReferenceImages;

internal static class BoardMeshes
{
    private static Mesh? combined;
    internal static Mesh Combined => combined ??= Build("Reference Image selection and collision",BoardMeshData.FrameVertices.Concat(BoardMeshData.ImageVertices).ToArray(),BoardMeshData.FrameNormals.Concat(BoardMeshData.ImageNormals).ToArray(),false);
    internal static Mesh Image(bool mirror)
    {
        var v=BoardPolicy.PictureVertices;var n=new float[v.Length];
        for(int i=0;i<n.Length;i+=3)n[i]=i<18?1:-1;
        return Build("Reference Image two quads",v,n,mirror,true);
    }
    private static Mesh Build(string name,float[] v,float[] n,bool mirror,bool picture=false)
    {
        var p=new Vector3[v.Length/3]; var normals=new Vector3[p.Length]; var uv=new Vector2[p.Length]; var triangles=new int[p.Length];
        for(int i=0;i<p.Length;i++)
        {
            // Rotate the approved XY board into YZ; right-handed rotation about Y.
            p[i]=picture?new Vector3(v[i*3],v[i*3+1],v[i*3+2]):new Vector3(v[i*3+2],v[i*3+1],-v[i*3]);
            normals[i]=picture?new Vector3(n[i*3],n[i*3+1],n[i*3+2]):new Vector3(n[i*3+2],n[i*3+1],-n[i*3]);
            float u=(1-p[i].z)/2; uv[i]=new Vector2(mirror?1-u:u,(p[i].y+1)/2); triangles[i]=i;
        }
        var mesh=new Mesh { name=name,hideFlags=HideFlags.HideAndDontSave };
        mesh.vertices=p; mesh.normals=normals; mesh.uv=uv;
        mesh.SetTriangles((Il2CppStructArray<int>)triangles,0);
        mesh.RecalculateBounds(); return mesh;
    }
    internal static Material Material(Material? candidate,string name)
    {
        string[] required={"_BaseColorMap","_BaseColor","_UVBase","_UVMappingMask"};
        bool clone=candidate!=null && candidate.shader!=null && candidate.shader.name=="HDRP/Lit" && required.All(candidate.HasProperty);
        var m=clone ? new Material(candidate!) : new Material(Shader.Find("HDRP/Lit") ?? throw new InvalidOperationException("HDRP/Lit unavailable."));
        m.name=name; m.hideFlags=HideFlags.HideAndDontSave;
        if(m.shader==null || !m.shader.isSupported || !required.All(m.HasProperty)) { UnityEngine.Object.Destroy(m); throw new InvalidOperationException("HDRP/Lit is unsupported or lacks required image properties."); }
        m.SetTexture("_BaseColorMap",null);
        foreach(string prop in new[]{"_MaskMap","_NormalMap","_NormalMapOS","_BentNormalMap","_DetailMap","_HeightMap","_EmissiveColorMap","_CoatMaskMap","_SubsurfaceMaskMap","_ThicknessMap","_TransmittanceColorMap"}) if(m.HasProperty(prop))m.SetTexture(prop,null);
        foreach(string prop in new[]{"_SurfaceType","_AlphaCutoffEnable","_Metallic","_Smoothness","_DoubleSidedEnable","_UVBase","_UVDetail","_LinkDetailsWithBase","_EmissiveIntensity","_EmissiveColorMode","_UseEmissiveIntensity","_NormalScale","_DetailAlbedoScale","_DetailNormalScale","_DetailSmoothnessScale","_CoatMask","_TransmissionEnable","_RefractionModel"}) if(m.HasProperty(prop))m.SetFloat(prop,0);
        m.SetColor("_BaseColor",Color.white); if(m.HasProperty("_EmissiveColor"))m.SetColor("_EmissiveColor",Color.black);
        m.SetVector("_UVMappingMask",new Vector4(1,0,0,0)); m.SetTextureScale("_BaseColorMap",Vector2.one);m.SetTextureOffset("_BaseColorMap",Vector2.zero);
        HDMaterial.ValidateMaterial(m);
        if(Plugin.Detailed)Plugin.Info("material: "+(clone?"checked native HDRP/Lit clone":"Heavy Trunnion HDRP/Lit fallback")+"; own instance "+name);
        return m;
    }
    internal static GameObject Render(Transform parent,string name,Mesh mesh,Material material,int layer)
    {
        var go=new GameObject(name) { layer=layer };
        go.transform.SetParent(parent,false); go.AddComponent<MeshFilter>().sharedMesh=mesh; go.AddComponent<MeshRenderer>().sharedMaterial=material;
        return go;
    }
    internal static void Clear()
    {
        if(combined!=null)UnityEngine.Object.Destroy(combined);
        combined=null;
    }
}
