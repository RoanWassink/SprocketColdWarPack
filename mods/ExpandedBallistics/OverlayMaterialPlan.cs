using System.Numerics;
namespace SprocketExpandedBallistics;

// Used by runtime and stand-in tests; HDRP's runtime validator supplies the
// pipeline-version-specific stencil, pass and keyword state after these inputs.
internal sealed record OverlayMaterialPlan(string Shader,string TextureProperty,float Opacity,float Smoothness)
{
    internal bool Transparent=>Opacity<.999f;
    internal int Queue=>Transparent?3000:2010;
    internal const int UvChannel=0;
    internal static readonly Vector4 UvMask=new(1,0,0,0);
    internal static readonly string[] Candidates={"HDRP/Lit","Universal Render Pipeline/Lit","Standard","HDRP/Unlit","Unlit/Texture"};
    internal static string? TextureInput(string shader,Func<string,bool> has)=>shader switch
    {
        "HDRP/Lit"=>has("_BaseColorMap")&&has("_UVMappingMask")&&has("_UVBase")?"_BaseColorMap":null,
        "Universal Render Pipeline/Lit"=>has("_BaseMap")?"_BaseMap":null,
        "Standard" or "Unlit/Texture"=>has("_MainTex")?"_MainTex":null,
        "HDRP/Unlit"=>has("_UnlitColorMap")?"_UnlitColorMap":null,
        _=>null // VehicleLit has unverified paint/UV inputs; do not guess.
    };
    internal IEnumerable<(string Name,float Value)> Floats()
    {
        yield return ("_Metallic",0);yield return ("_Smoothness",Smoothness);yield return ("_Glossiness",Smoothness);
        yield return ("_UVBase",UvChannel);yield return ("_MaterialID",1);
        yield return ("_AlphaCutoffEnable",0);yield return ("_AlphaClip",0);yield return ("_AlphaCutoff",0);
        yield return ("_SurfaceType",Transparent?1:0);yield return ("_Surface",Transparent?1:0);
        yield return ("_BlendMode",0);yield return ("_Mode",Transparent?3:0);
        yield return ("_ZWrite",Transparent?0:1);yield return ("_TransparentZWrite",0);
        // HDRP Alpha mode uses premultiplied RGB; URP/Standard alpha uses SrcAlpha.
        yield return ("_SrcBlend",Transparent&&Shader!="HDRP/Lit"&&Shader!="HDRP/Unlit"?5:1);
        yield return ("_DstBlend",Transparent?10:0);yield return ("_AlphaSrcBlend",1);yield return ("_AlphaDstBlend",Transparent?10:0);
        yield return ("_CullMode",0);yield return ("_CullModeForward",0);yield return ("_Cull",0);
        yield return ("_DoubleSidedEnable",1);yield return ("_DoubleSidedNormalMode",1);
        yield return ("_EnableBlendModePreserveSpecularLighting",0);
        yield return ("_ReceivesSSR",0);yield return ("_ReceivesSSRTransparent",0);
    }
}

internal static class OverlayNormalOffset
{
    internal const float Metres=.0004f;
    internal static Vector3[] Apply(Vector3[] vertices,Vector3[] normals,Vector3 scale)=>Apply(vertices,normals,scale,out _);
    /// <summary>
    /// 0.22.74: a point whose normal has length zero or is not finite (the tip of a triangle without area) gets a displacement of zero and is counted in
    /// <paramref name="degenerate"/>; it no longer makes the overlay of the whole plate fail. Every other point is computed exactly as before.
    /// </summary>
    internal static Vector3[] Apply(Vector3[] vertices,Vector3[] normals,Vector3 scale,out int degenerate)
    {
        if(vertices.Length!=normals.Length||!float.IsFinite(scale.X)||!float.IsFinite(scale.Y)||!float.IsFinite(scale.Z)||
            Math.Abs(scale.X)<1e-6||Math.Abs(scale.Y)<1e-6||Math.Abs(scale.Z)<1e-6)
            throw new InvalidOperationException("Invalid overlay scale/normals.");
        var result=new Vector3[vertices.Length];degenerate=0;
        for(int i=0;i<result.Length;i++)
        {
            var n=normals[i]/scale; // inverse transpose: then convert world normal displacement back to local space
            if(Degenerate(n)){result[i]=vertices[i];degenerate++;continue;}
            result[i]=vertices[i]+Vector3.Normalize(n)/scale*Metres;
        }
        return result;
    }
    private static bool Degenerate(Vector3 n){var l=n.LengthSquared();return !float.IsFinite(l)||l<1e-12;}
    /// <summary>
    /// 0.22.74: gives a point without a usable normal the normal of another point of the same triangle that has one (so the shading of the other
    /// triangles at that point stays sane; the points of a triangle lie in the same layer); returns <paramref name="normals"/> itself, unchanged, when no normal is degenerate.
    /// </summary>
    internal static Vector3[] Repair(Vector3[] normals,int[] triangles,Vector3 scale,out int degenerate)
    {
        degenerate=0;var bad=new bool[normals.Length];
        for(int i=0;i<normals.Length;i++)if(Degenerate(normals[i]/scale)){bad[i]=true;degenerate++;}
        if(degenerate==0)return normals;
        var repaired=(Vector3[])normals.Clone();var done=new bool[normals.Length];
        for(int t=0;t+2<triangles.Length;t+=3)
        {
            int a=triangles[t],b=triangles[t+1],c=triangles[t+2];
            if(a<0||b<0||c<0||a>=normals.Length||b>=normals.Length||c>=normals.Length)continue;
            foreach(var (p,q,r) in new[]{(a,b,c),(b,c,a),(c,a,b)})
            {
                if(!bad[p]||done[p])continue;
                if(!bad[q]){repaired[p]=normals[q];done[p]=true;}else if(!bad[r]){repaired[p]=normals[r];done[p]=true;}
            }
        }
        return repaired;
    }
}
