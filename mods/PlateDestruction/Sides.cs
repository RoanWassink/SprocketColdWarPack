using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
using Object=UnityEngine.Object;
namespace PlateDestructionP0;

// Mirrored plate pairs (0.2.0). A mirrored add-on plate such as a side skirt is ONE PlateStructure; the game renders a combined mesh
// (source + mirror image) and builds the armour from it. This file holds the Unity-side data per plate (the "plan") and the managed mesh work.

/// <summary>One renderer of a mirrored plate (high or low detail) and the meshes needed to show only one half.</summary>
internal sealed class SidePair
{
    internal string Name="";
    internal VehicleMeshRenderer? Renderer;
    internal MeshFilter? Filter;
    internal Mesh? Source;      // the non-mirrored source mesh (PlateStructureMesh.MaxDetailMesh / MinDetailMesh)
    internal Mesh? Combined;    // PlateStructure.mirroredHighDetail / mirroredLowDetail: what the renderer shows
    internal Mesh? WasMesh;     // the renderer's mesh before we changed anything (restored on reset)
    internal Mesh? Half;        // private copy that holds only the mirrored half (built on demand, destroyed on reset)
    internal bool Valid;
    internal string Why="";
}

/// <summary>Everything the break needs for one destructible plate. PerSide=false means: treat it as one piece (the 0.1.1 behaviour) and say why.</summary>
internal sealed class SidePlan
{
    internal long Root;internal int Vuid;
    internal bool Mirrored,PerSide;
    internal string Why="";
    internal SideGeometry? Geo;
    internal Mat4? Mirror;
    internal int PlateDetSign=1;
    internal readonly List<SidePair> Pairs=new();
}

internal static class SideOps
{
    internal static Mat4 ToMat4(Matrix4x4 m)
    {
        var f=new float[16];
        for(int r=0;r<4;r++)for(int c=0;c<4;c++)f[r*4+c]=m[r,c];
        return new Mat4(f);
    }
    internal static V3 ToV3(Vector3 v)=>new(v.x,v.y,v.z);
    internal static V3[] ToV3(Il2CppStructArray<Vector3> a)
    {
        var r=new V3[a.Length];
        for(int i=0;i<r.Length;i++){var v=a[i];r[i]=new V3(v.x,v.y,v.z);}
        return r;
    }
    internal static string F(float v)=>v.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
    internal static string B(Bounds b)=>$"[{F(b.min.x)},{F(b.min.y)},{F(b.min.z)}..{F(b.max.x)},{F(b.max.y)},{F(b.max.z)}]";
    private static string MeshInfo(Mesh? m)=>m==null?"null":$"'{m.name}' v={m.vertexCount} readable={m.isReadable}";

    // Builds the plan and one probe text per plate. Called from Update (main thread), never from inside the damage model build.
    internal static SidePlan Build(PlateStructure p,VehicleComponent c,long root,int vuid,bool perSideAllowed,StringBuilder probe)
    {
        var plan=new SidePlan{Root=root,Vuid=vuid};
        bool mirrored=false;try{mirrored=p.Mirrored;}catch{}
        plan.Mirrored=mirrored;
        probe.Append($"[PROBE] vuid={vuid} Mirrored={mirrored}");
        try{plan.PlateDetSign=Mat4.Sign(ToMat4(c.transform.localToWorldMatrix).Det3());probe.Append($" plateDetSign={plan.PlateDetSign}");}catch{}
        if(!mirrored){plan.Why="not mirrored";return plan;}

        Matrix4x4 mm=default;bool haveM=false;
        try{p.GetMirrorMatrix(out mm);haveM=true;}catch(Exception e){probe.Append(" mirrorMatrix=ERR "+e.Message);}
        Mat4? mirror=null;float det=0;
        if(haveM)
        {
            mirror=ToMat4(mm);det=mirror.Det3();
            probe.Append($" mirrorDet={F(det)} mirrorMatrixRow0=({F(mm.m00)},{F(mm.m01)},{F(mm.m02)},{F(mm.m03)}) row1=({F(mm.m10)},{F(mm.m11)},{F(mm.m12)},{F(mm.m13)}) row2=({F(mm.m20)},{F(mm.m21)},{F(mm.m22)},{F(mm.m23)})");
        }
        var pm=p.Mesh;Mesh? maxM=null,minM=null;Bounds lb=default;bool haveLb=false;
        try{maxM=pm?.MaxDetailMesh;minM=pm?.MinDetailMesh;if(pm!=null){lb=pm.LocalBounds;haveLb=true;}}catch(Exception e){probe.Append(" mesh=ERR "+e.Message);}
        Mesh? mh=null,ml=null;try{mh=p.mirroredHighDetail;ml=p.mirroredLowDetail;}catch{}
        probe.Append($" | highDetail(source)={MeshInfo(maxM)} lowDetail(source)={MeshInfo(minM)} mirroredHighDetail={MeshInfo(mh)} mirroredLowDetail={MeshInfo(ml)} localBounds={(haveLb?B(lb):"n/a")}");

        AddPair(plan,probe,"high",p.highDetailRenderer,maxM,mh);
        AddPair(plan,probe,"low",p.lowDetailRenderer,minM,ml);
        try
        {
            var sh=p.shadowProxyRenderer;var f=sh?.Filter;var sm=f==null?null:f.sharedMesh;
            probe.Append($" | shadowProxy filter mesh={MeshInfo(sm)}");
        }
        catch{}

        string why="";
        if(!perSideAllowed)why="per-side break switched off or builder patch not installed";
        else if(!haveM||mirror==null)why="mirror matrix not readable";
        else if(det>=0)why="mirror matrix is not a reflection (determinant "+F(det)+")";
        else if(plan.Pairs.Count==0)why="no renderer with a mesh";
        else foreach(var pr in plan.Pairs)if(!pr.Valid){why=pr.Name+" renderer: "+pr.Why;break;}
        if(why.Length==0)
        {
            Bounds ob;
            var srcForBounds=plan.Pairs[0].Source;
            if(srcForBounds!=null)ob=srcForBounds.bounds;else if(haveLb)ob=lb;else{ob=default;why="no bounds";}
            if(why.Length==0)
            {
                plan.Mirror=mirror;
                plan.Geo=new SideGeometry(mirror!,ToV3(ob.min),ToV3(ob.max));
                probe.Append($" | origBounds={B(ob)} mirrorBounds=[{F(plan.Geo.MirrMin.X)},{F(plan.Geo.MirrMin.Y)},{F(plan.Geo.MirrMin.Z)}..{F(plan.Geo.MirrMax.X)},{F(plan.Geo.MirrMax.Y)},{F(plan.Geo.MirrMax.Z)}]");
                plan.PerSide=true;
            }
        }
        plan.Why=why;
        probe.Append(plan.PerSide?" => per-side break POSSIBLE":" => per-side break NOT possible: "+why+" (the plate breaks as one piece, both sides)");
        return plan;
    }

    private static void AddPair(SidePlan plan,StringBuilder probe,string name,VehicleMeshRenderer? r,Mesh? src,Mesh? combined)
    {
        MeshFilter? filter=null;Mesh? cur=null;
        try{filter=r?.Filter;cur=filter==null?null:filter.sharedMesh;}catch{}
        string kind=cur==null?"none":(combined!=null&&cur.Pointer==combined.Pointer)?"mirroredCombined":(src!=null&&cur.Pointer==src.Pointer)?"source":"other";
        string rn="n/a";try{rn=r?.GameObject?.name??"n/a";}catch{}
        probe.Append($" | {name} renderer '{rn}' shows {MeshInfo(cur)} ({kind})");
        if(r==null||filter==null||cur==null)return; // renderer absent or empty: nothing to split
        var pr=new SidePair{Name=name,Renderer=r,Filter=filter,Source=src,Combined=combined,WasMesh=cur};
        if(src==null)pr.Why="source mesh missing";
        else if(!src.isReadable)pr.Why="source mesh not readable";
        else if(combined==null)pr.Why="mirrored mesh missing";
        else if(kind!="mirroredCombined")pr.Why="renderer does not show the mirrored mesh (shows "+kind+")";
        else if(combined.vertexCount!=2*src.vertexCount)pr.Why=$"mirrored mesh is not source + mirror image (vertices {combined.vertexCount} vs 2 x {src.vertexCount})";
        else pr.Valid=true;
        plan.Pairs.Add(pr);
    }

    // The mirrored half only: source vertices/normals/tangents through the mirror matrix, triangle winding flipped. Never touches a shared mesh.
    internal static Mesh BuildHalf(Mesh src,Mat4 mirror)
    {
        var v=ToV3(src.vertices);var mv=SideTriangles.Transform(v,mirror);
        var res=new Mesh{name="PD_mirrorHalf_"+src.name};
        if(mv.Length>65535)res.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
        var vv=new Vector3[mv.Length];for(int i=0;i<vv.Length;i++)vv[i]=new Vector3(mv[i].X,mv[i].Y,mv[i].Z);
        res.vertices=vv;
        var n=src.normals;
        if(n!=null&&n.Length==vv.Length)
        {
            var mn=SideTriangles.TransformDirs(ToV3(n),mirror);var nn=new Vector3[mn.Length];
            for(int i=0;i<nn.Length;i++)nn[i]=new Vector3(mn[i].X,mn[i].Y,mn[i].Z);
            res.normals=nn;
        }
        var t=src.tangents;
        if(t!=null&&t.Length==vv.Length)
        {
            var nt=new Vector4[t.Length];
            for(int i=0;i<nt.Length;i++){var d=mirror.Dir(new V3(t[i].x,t[i].y,t[i].z));nt[i]=new Vector4(d.X,d.Y,d.Z,-t[i].w);}
            res.tangents=nt;
        }
        var uv=src.uv;if(uv!=null&&uv.Length==vv.Length)res.uv=uv;
        var uv2=src.uv2;if(uv2!=null&&uv2.Length==vv.Length)res.uv2=uv2;
        var col=src.colors32;if(col!=null&&col.Length==vv.Length)res.colors32=col;
        int subs=Math.Max(1,src.subMeshCount);
        res.subMeshCount=subs;
        for(int s=0;s<subs;s++)
        {
            var tri=src.GetTriangles(s);int[] arr=new int[tri.Length];for(int i=0;i<arr.Length;i++)arr[i]=tri[i];
            res.SetTriangles(SideTriangles.FlipWinding(arr),s);
        }
        res.RecalculateBounds();
        return res;
    }

    internal static void Restore(SidePlan plan)
    {
        foreach(var pr in plan.Pairs)
        {
            try{if(pr.Filter!=null&&pr.WasMesh!=null)pr.Filter.sharedMesh=pr.WasMesh;}catch{}
            try{if(pr.Half!=null)Object.Destroy(pr.Half);}catch{}
            pr.Half=null;
        }
    }
}
