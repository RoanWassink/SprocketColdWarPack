using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
namespace PlateDestructionP0;

// Unity-side helpers of dents and deformation (0.5.0: adaptive subdivision, spatial normals, cached buffers, debris warp).
// Shared meshes are never edited: every dented renderer shows a private, readable copy.

internal static class MeshOps
{
    /// <summary>Private copy of a readable mesh (vertices, normals, tangents, uv, uv2, colours, all submeshes).</summary>
    internal static Mesh Clone(Mesh src,string name)
    {
        var res=new Mesh{name=name};
        int vc=src.vertexCount;
        if(vc>65535)res.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
        res.vertices=src.vertices;
        var n=src.normals;if(n!=null&&n.Length==vc)res.normals=n;
        var t=src.tangents;if(t!=null&&t.Length==vc)res.tangents=t;
        var uv=src.uv;if(uv!=null&&uv.Length==vc)res.uv=uv;
        var uv2=src.uv2;if(uv2!=null&&uv2.Length==vc)res.uv2=uv2;
        var col=src.colors32;if(col!=null&&col.Length==vc)res.colors32=col;
        int subs=Math.Max(1,src.subMeshCount);
        res.subMeshCount=subs;
        for(int s=0;s<subs;s++)res.SetTriangles(src.GetTriangles(s),s);
        res.RecalculateBounds();
        return res;
    }
    internal static V3[] Verts(Mesh m)=>SideOps.ToV3(m.vertices);
    internal static int[] Tris(Mesh m)
    {
        var a=m.triangles;var r=new int[a.Length];for(int i=0;i<r.Length;i++)r[i]=a[i];return r;
    }
    /// <summary>Per-vertex half (PlateSide value) for a mirrored plate with a per-side plan, else null (the dent then affects every vertex in reach).</summary>
    internal static byte[]? VertexSides(V3[] v,SidePlan? plan)
    {
        if(plan is not {PerSide:true,Geo:not null})return null;
        var r=new byte[v.Length];for(int i=0;i<r.Length;i++)r[i]=(byte)plan.Geo.Choose(v[i]);return r;
    }
    internal static byte[]? TriSides(V3[] v,int[] t,SidePlan? plan)
    {
        if(plan is not {PerSide:true,Geo:not null})return null;
        var r=new byte[t.Length/3];
        for(int i=0;i<r.Length;i++){int a=t[i*3],b=t[i*3+1],c=t[i*3+2];if(a<0||b<0||c<0||a>=v.Length||b>=v.Length||c>=v.Length)continue;r[i]=(byte)SideTriangles.Side(v,t,i,plan.Geo);}
        return r;
    }
    /// <summary>Number of triangles per submesh, in order (the concatenation is what Mesh.triangles returns).</summary>
    internal static int[] SubCounts(Mesh m)
    {
        int subs=Math.Max(1,m.subMeshCount);var r=new int[subs];
        for(int s=0;s<subs;s++)r[s]=(int)(m.GetIndexCount(s)/3);
        return r;
    }

    internal static SubdivMesh FromUnityMesh(Mesh m,SidePlan? plan,PlateCells? pc,double maxDepthM,PlateStructure? plate=null)
    {
        var mv=m.vertices;int vc=mv.Length;
        var basis=new V3[vc];
        for(int i=0;i<vc;i++){var p=mv[i];basis[i]=new V3(p.x,p.y,p.z);}

        V3[]? normals=null;
        var mn=m.normals;
        if(mn!=null&&mn.Length==vc){normals=new V3[vc];for(int i=0;i<vc;i++){var q=mn[i];normals[i]=new V3(q.x,q.y,q.z);}}

        V4[]? tangents=null;
        var mt=m.tangents;
        if(mt!=null&&mt.Length==vc){tangents=new V4[vc];for(int i=0;i<vc;i++){var q=mt[i];tangents[i]=new V4(q.x,q.y,q.z,q.w);}}

        V2[]? uv=null;
        var muv=m.uv;
        if(muv!=null&&muv.Length==vc){uv=new V2[vc];for(int i=0;i<vc;i++){var q=muv[i];uv[i]=new V2(q.x,q.y);}}

        V2[]? uv2=null;
        var muv2=m.uv2;
        if(muv2!=null&&muv2.Length==vc){uv2=new V2[vc];for(int i=0;i<vc;i++){var q=muv2[i];uv2[i]=new V2(q.x,q.y);}}

        Col32[]? colors=null;
        var mcol=m.colors32;
        if(mcol!=null&&mcol.Length==vc){colors=new Col32[vc];for(int i=0;i<vc;i++){var q=mcol[i];colors[i]=new Col32(q.r,q.g,q.b,q.a);}}

        V3[]? thickVec=null;
        if(plate!=null)
        {
            try
            {
                var tv=plate.Mesh?.ThicknessVectors;
                if(tv!=null&&tv.Length>0)
                {
                    if(tv.Length==vc)
                    {
                        thickVec=new V3[vc];
                        for(int i=0;i<vc;i++){var q=tv[i];thickVec[i]=new V3(q.x,q.y,q.z);}
                    }
                    else if(vc==tv.Length*2&&plan?.Mirror!=null)
                    {
                        thickVec=new V3[vc];
                        int half=tv.Length;
                        for(int i=0;i<half;i++)
                        {
                            var q=tv[i];var v3=new V3(q.x,q.y,q.z);
                            thickVec[i]=v3;
                            thickVec[half+i]=plan.Mirror.Dir(v3);
                        }
                    }
                }
            }catch{}
        }

        int subs=Math.Max(1,m.subMeshCount);
        var subTris=new int[subs][];
        bool hasCells=pc!=null;
        bool hasSides=plan is {PerSide:true,Geo:not null};
        var subCells=hasCells?new int[subs][]:null;
        var subSides=hasSides?new byte[subs][]:null;
        var cplan=pc?.Plan;
        var geo=plan?.Geo;
        for(int s=0;s<subs;s++)
        {
            var raw=m.GetTriangles(s);
            int[] t=new int[raw.Length];
            for(int i=0;i<t.Length;i++)t[i]=raw[i];
            subTris[s]=t;
            int nt=t.Length/3;
            if(hasCells)
            {
                var sc=new int[nt];
                for(int ti=0;ti<nt;ti++)sc[ti]=cplan!.CellOfTriangle(basis,t,ti);
                subCells![s]=sc;
            }
            if(hasSides)
            {
                var ss=new byte[nt];
                for(int ti=0;ti<nt;ti++)
                {
                    int a=t[ti*3],b=t[ti*3+1],c=t[ti*3+2];
                    if(a>=0&&b>=0&&c>=0&&a<vc&&b<vc&&c<vc)ss[ti]=(byte)SideTriangles.Side(basis,t,ti,geo!);
                }
                subSides![s]=ss;
            }
        }
        var sm=new SubdivMesh(basis,normals,tangents,uv,uv2,colors,subTris,VertexSides(basis,plan),subCells,subSides,maxDepthM,thickVec);
        if(normals==null)SmartNormals.Compute(sm,null,50.0,null);
        return sm;
    }

    /// <summary>Uploads vertex positions, normals, tangents (and new topology + sheared rim walls if subdivided/torn) to tg.Work using cached native buffers.</summary>
    internal static void Upload(DentTarget tg,bool topologyChanged)
    {
        var m=tg.Work;var sm=tg.Mesh;
        if(m==null||sm==null)return;
        int vc=sm.Basis.Count;
        bool hasTan=sm.Tangents.Count==vc;
        bool filterBroken=tg.AppliedBroken.Count>0&&sm.SubTriCell!=null;
        if(!topologyChanged&&tg.HasRimCaps)topologyChanged=true;

        RimCapData? rim=null;
        if(topologyChanged&&filterBroken&&Plugin.Instance.RimCapsEnabled.Value)
            rim=RimCapBuilder.Build(sm,c=>tg.AppliedBroken.Contains(c),tg.ThicknessMm/1000.0/tg.Scale);
        if(topologyChanged)tg.HasRimCaps=rim!=null&&rim.Verts.Count>0;

        int rimVc=rim?.Verts.Count??0;
        int totalVc=vc+rimVc;

        if(topologyChanged)
        {
            m.Clear(false);
            if(totalVc>65535&&m.indexFormat!=UnityEngine.Rendering.IndexFormat.UInt32)
                m.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
            tg.VertBuf=new Il2CppStructArray<Vector3>(totalVc);
            tg.NormBuf=new Il2CppStructArray<Vector3>(totalVc);
            tg.TanBuf=hasTan?new Il2CppStructArray<Vector4>(totalVc):null;
        }
        else
        {
            if(tg.VertBuf==null||tg.VertBuf.Length!=totalVc)tg.VertBuf=new Il2CppStructArray<Vector3>(totalVc);
            if(tg.NormBuf==null||tg.NormBuf.Length!=totalVc)tg.NormBuf=new Il2CppStructArray<Vector3>(totalVc);
            if(hasTan&&(tg.TanBuf==null||tg.TanBuf.Length!=totalVc))tg.TanBuf=new Il2CppStructArray<Vector4>(totalVc);
            else if(!hasTan)tg.TanBuf=null;
        }

        var vb=tg.VertBuf!;var nb=tg.NormBuf!;var tb=tg.TanBuf;
        for(int i=0;i<vc;i++)
        {
            var p=sm.Position(i);vb[i]=new Vector3(p.X,p.Y,p.Z);
            var n=sm.Normals[i];nb[i]=new Vector3(n.X,n.Y,n.Z);
            if(tb!=null){var t=sm.Tangents[i];tb[i]=new Vector4(t.X,t.Y,t.Z,t.W);}
        }
        if(rim!=null&&rimVc>0)
        {
            for(int i=0;i<rimVc;i++)
            {
                var p=rim.Verts[i];vb[vc+i]=new Vector3(p.X,p.Y,p.Z);
                var n=rim.Normals[i];nb[vc+i]=new Vector3(n.X,n.Y,n.Z);
                if(tb!=null)
                {
                    var t=i<rim.Tangents.Count?rim.Tangents[i]:new V4(1,0,0,1);
                    tb[vc+i]=new Vector4(t.X,t.Y,t.Z,t.W);
                }
            }
        }
        m.vertices=vb;
        m.normals=nb;
        if(tb!=null)m.tangents=tb;

        if(topologyChanged)
        {
            if(sm.UV.Count==vc)
            {
                var a=new Il2CppStructArray<Vector2>(totalVc);
                for(int i=0;i<vc;i++)a[i]=new Vector2(sm.UV[i].X,sm.UV[i].Y);
                if(rim!=null)for(int i=0;i<rimVc;i++){var q=i<rim.UV.Count?rim.UV[i]:new V2(0,0);a[vc+i]=new Vector2(q.X,q.Y);}
                m.uv=a;
            }
            if(sm.UV2.Count==vc)
            {
                var a=new Il2CppStructArray<Vector2>(totalVc);
                for(int i=0;i<vc;i++)a[i]=new Vector2(sm.UV2[i].X,sm.UV2[i].Y);
                if(rim!=null)for(int i=0;i<rimVc;i++){var q=i<rim.UV2.Count?rim.UV2[i]:new V2(0,0);a[vc+i]=new Vector2(q.X,q.Y);}
                m.uv2=a;
            }
            if(sm.Colors.Count==vc)
            {
                var a=new Il2CppStructArray<Color32>(totalVc);
                for(int i=0;i<vc;i++){var q=sm.Colors[i];a[i]=new Color32(q.R,q.G,q.B,q.A);}
                if(rim!=null)for(int i=0;i<rimVc;i++){var q=i<rim.Colors.Count?rim.Colors[i]:new Col32(150,145,140,255);a[vc+i]=new Color32(q.R,q.G,q.B,q.A);}
                m.colors32=a;
            }
            int subs=sm.SubTris.Length;
            m.subMeshCount=subs;
            for(int s=0;s<subs;s++)
            {
                var st=sm.SubTris[s];
                bool appendRim=s==0&&rim!=null&&rim.Tris.Count>0;
                if(!filterBroken&&!appendRim){m.SetTriangles(st.ToArray(),s,false);continue;}
                var sc=sm.SubTriCell?[s];int nt=st.Count/3;
                var kept=new List<int>(st.Count+(appendRim?rim!.Tris.Count:0));
                for(int ti=0;ti<nt;ti++)
                {
                    if(filterBroken&&sc!=null)
                    {
                        int c=ti<sc.Count?sc[ti]:-1;
                        if(c>=0&&tg.AppliedBroken.Contains(c))continue;
                    }
                    kept.Add(st[ti*3]);kept.Add(st[ti*3+1]);kept.Add(st[ti*3+2]);
                }
                if(appendRim)for(int k=0;k<rim!.Tris.Count;k++)kept.Add(rim.Tris[k]+vc);
                m.SetTriangles(kept.ToArray(),s,false);
            }
        }
        m.RecalculateBounds();
    }

    /// <summary>0.5.0: the working copy shows only the triangles of unbroken cells (works seamlessly on subdivided meshes).</summary>
    internal static void ApplyCells(DentTarget tg,Func<int,bool> isBroken)
    {
        var sm=tg.Mesh;
        if(sm==null||sm.SubTriCell==null||tg.Work==null)return;
        for(int s=0;s<sm.SubTris.Length;s++)
        {
            var st=sm.SubTris[s];var sc=sm.SubTriCell[s];int n=st.Count/3;
            var kept=new List<int>(st.Count);
            for(int i=0;i<n;i++)
            {
                int c=i<sc.Count?sc[i]:-1;
                if(c>=0&&isBroken(c))continue;
                kept.Add(st[i*3]);kept.Add(st[i*3+1]);kept.Add(st[i*3+2]);
            }
            tg.Work.SetTriangles(kept.ToArray(),s,false);
        }
        tg.Work.RecalculateBounds();
    }

    /// <summary>0.5.0: compact mesh of the triangles of the cells that <paramref name="take"/> accepts from <paramref name="tg"/>.Mesh, optionally warped for flying debris.</summary>
    internal static Mesh? ExtractCells(DentTarget tg,Func<int,bool> take,string name,V3? warpBlastLocal=null,double warpDepthM=0,uint seed=0,double smoothAngleDeg=50)
    {
        var sm=tg.Mesh;
        if(sm==null||sm.SubTriCell==null)return null;
        int vc=sm.Basis.Count;
        var map=new Dictionary<int,int>();
        var order=new List<int>();
        var subs=new List<int[]>(sm.SubTris.Length);
        int total=0;

        int Remap(int ix)
        {
            if(!map.TryGetValue(ix,out int m)){m=order.Count;map[ix]=m;order.Add(ix);}
            return m;
        }

        for(int s=0;s<sm.SubTris.Length;s++)
        {
            var st=sm.SubTris[s];var sc=sm.SubTriCell[s];int n=st.Count/3;
            var l=new List<int>();
            for(int ti=0;ti<n;ti++)
            {
                int c=ti<sc.Count?sc[ti]:-1;if(c<0||!take(c))continue;
                int ia=st[ti*3],ib=st[ti*3+1],ic=st[ti*3+2];
                if(ia<0||ib<0||ic<0||ia>=vc||ib>=vc||ic>=vc)continue;
                l.Add(Remap(ia));l.Add(Remap(ib));l.Add(Remap(ic));
            }
            subs.Add(l.ToArray());total+=l.Count/3;
        }
        if(total==0)return null;

        var vv=new V3[order.Count];
        for(int i=0;i<vv.Length;i++)vv[i]=sm.Position(order[i]);

        bool warped=warpBlastLocal is {} bl&&warpDepthM>0;
        if(warped)DebrisWarp.Apply(vv,warpBlastLocal!.Value,warpDepthM,seed);

        V3[] norms;
        if(warped)
        {
            var allTris=new int[total*3];int pos=0;
            foreach(var sub in subs){Array.Copy(sub,0,allTris,pos,sub.Length);pos+=sub.Length;}
            norms=SmartNormals.ComputeSimple(vv,allTris,smoothAngleDeg);
        }
        else
        {
            norms=new V3[order.Count];
            for(int i=0;i<norms.Length;i++)norms[i]=sm.Normals[order[i]];
        }

        var res=new Mesh{name=name};
        if(order.Count>65535)res.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
        var uvv=new Vector3[order.Count];for(int i=0;i<uvv.Length;i++)uvv[i]=new Vector3(vv[i].X,vv[i].Y,vv[i].Z);
        res.vertices=uvv;
        var unn=new Vector3[order.Count];for(int i=0;i<unn.Length;i++)unn[i]=new Vector3(norms[i].X,norms[i].Y,norms[i].Z);
        res.normals=unn;
        if(sm.Tangents.Count==vc){var a=new Vector4[order.Count];for(int i=0;i<a.Length;i++){var q=sm.Tangents[order[i]];a[i]=new Vector4(q.X,q.Y,q.Z,q.W);}res.tangents=a;}
        if(sm.UV.Count==vc){var a=new Vector2[order.Count];for(int i=0;i<a.Length;i++){var q=sm.UV[order[i]];a[i]=new Vector2(q.X,q.Y);}res.uv=a;}
        if(sm.UV2.Count==vc){var a=new Vector2[order.Count];for(int i=0;i<a.Length;i++){var q=sm.UV2[order[i]];a[i]=new Vector2(q.X,q.Y);}res.uv2=a;}
        if(sm.Colors.Count==vc){var a=new Color32[order.Count];for(int i=0;i<a.Length;i++){var q=sm.Colors[order[i]];a[i]=new Color32(q.R,q.G,q.B,q.A);}res.colors32=a;}
        res.subMeshCount=subs.Count;
        for(int s=0;s<subs.Count;s++)res.SetTriangles(subs[s],s);
        res.RecalculateBounds();
        return res;
    }

    /// <summary>New mesh that holds only the vertices used by the given index lists (one list per submesh); vertex data is copied from <paramref name="src"/>. Null when nothing is left.</summary>
    internal static Mesh? Compact(Mesh src,List<int[]> subsIn,string name)
    {
        var verts=src.vertices;int vc=verts.Length;
        var map=new Dictionary<int,int>();var order=new List<int>();var subs=new List<int[]>(subsIn.Count);int total=0;
        int Remap(int ix){if(!map.TryGetValue(ix,out int m)){m=order.Count;map[ix]=m;order.Add(ix);}return m;}
        foreach(var sub in subsIn)
        {
            var l=new List<int>(sub.Length);
            for(int i=0;i+2<sub.Length;i+=3)
            {
                int a=sub[i],b=sub[i+1],c=sub[i+2];if(a<0||b<0||c<0||a>=vc||b>=vc||c>=vc)continue;
                l.Add(Remap(a));l.Add(Remap(b));l.Add(Remap(c));
            }
            subs.Add(l.ToArray());total+=l.Count/3;
        }
        if(total==0)return null;
        var res=new Mesh{name=name};
        if(order.Count>65535)res.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
        var vv=new Vector3[order.Count];for(int i=0;i<vv.Length;i++)vv[i]=verts[order[i]];res.vertices=vv;
        var nrm=src.normals;if(nrm!=null&&nrm.Length==vc){var a=new Vector3[order.Count];for(int i=0;i<a.Length;i++)a[i]=nrm[order[i]];res.normals=a;}
        var tan=src.tangents;if(tan!=null&&tan.Length==vc){var a=new Vector4[order.Count];for(int i=0;i<a.Length;i++)a[i]=tan[order[i]];res.tangents=a;}
        var uv=src.uv;if(uv!=null&&uv.Length==vc){var a=new Vector2[order.Count];for(int i=0;i<a.Length;i++)a[i]=uv[order[i]];res.uv=a;}
        var uv2=src.uv2;if(uv2!=null&&uv2.Length==vc){var a=new Vector2[order.Count];for(int i=0;i<a.Length;i++)a[i]=uv2[order[i]];res.uv2=a;}
        var col=src.colors32;if(col!=null&&col.Length==vc){var a=new Color32[order.Count];for(int i=0;i<a.Length;i++)a[i]=col[order[i]];res.colors32=a;}
        res.subMeshCount=subs.Count;
        for(int s=0;s<subs.Count;s++)res.SetTriangles(subs[s],s);
        res.RecalculateBounds();
        return res;
    }
}

/// <summary>0.7.0: One localized ragged hole punched by a blast, recorded for replaying onto rebased/lazy LOD targets.</summary>
internal sealed class HoleEvent
{
    internal V3 Center,Normal,BlastLocal;
    internal double RadiusM,CurlDepthM,CurlWidthM;
    internal PlateSide Side;
    internal int HoleId;
    internal uint Seed;
}

/// <summary>One renderer (high or low detail) of a dented plate.</summary>
internal sealed class DentTarget
{
    internal string Name="";
    internal MeshFilter Filter=null!;
    internal Renderer? Rend;
    internal Mesh Original=null!;   // what the game showed before the first dent: restored on reset, never destroyed by us
    internal Mesh Base=null!;       // pristine mesh of the current variant (the original, the source half or our private mirror half): never edited
    internal Mesh? Work;            // private copy that the renderer shows (null when lazy low-detail is deferred)
    internal SubdivMesh? Mesh;
    internal TriAccel Accel=new();
    internal PlateCells? PC;
    internal float Scale=1f;        // mean lossy scale of the renderer transform (mesh units to metres)
    internal double ThicknessMm=20;
    internal bool IsLow,LowDirty,HasRimCaps;
    internal Il2CppStructArray<Vector3>? VertBuf,NormBuf;
    internal Il2CppStructArray<Vector4>? TanBuf;
    internal readonly HashSet<int> AppliedBroken=new();
    internal readonly List<DentEvent> Events=new();
    internal readonly List<HoleEvent> Holes=new();
    internal int AppliedHoles;
}

/// <summary>All dent targets of one destructible or deformed plate (vehicle root + VUID).</summary>
internal sealed class DentSet
{
    internal long Root;internal int Vuid;internal SidePlan? Plan;internal PlateCells? Cells;
    internal PlateStructure? Plate;
    internal readonly List<DentTarget> Targets=new();
    internal MeshFilter? ShadowFilter;internal Renderer? ShadowRend;internal Mesh? ShadowOrig;
    internal int OrigTris;
    internal bool CapWarned;
}

/// <summary>Raycast of the flying debris against the world (the Emitter casing pattern: UI layer, triggers, the camera and vehicles excluded).</summary>
internal sealed class UnityGround:IGround
{
    private const int UiLayer=5;
    private readonly Il2CppStructArray<RaycastHit> hits=new(32);
    private readonly Dictionary<long,bool> excluded=new();
    private bool Excluded(Collider c)
    {
        long id=c.Pointer.ToInt64();if(excluded.TryGetValue(id,out bool no))return no;
        no=c.GetComponentInParent<Vehicle>()!=null||c.GetComponentInParent<Camera>()!=null||c.GetComponentInParent<Canvas>()!=null;
        if(excluded.Count<4096)excluded[id]=no;return no;
    }
    public bool Cast(V3 from,V3 to,out V3 point,out V3 normal,out double fraction)
    {
        point=to;normal=new V3(0,1,0);fraction=1;
        var origin=new Vector3(from.X,from.Y,from.Z);var delta=new Vector3(to.X-from.X,to.Y-from.Y,to.Z-from.Z);
        float length=delta.magnitude;if(length<1e-6f)return false;
        int n=Physics.RaycastNonAlloc(origin,delta/length,hits,length,-5,QueryTriggerInteraction.Ignore);
        float best=float.PositiveInfinity;RaycastHit chosen=default;bool have=false;
        for(int i=0;i<n;i++)
        {
            var h=hits[i];var c=h.collider;if(c==null||!c.enabled||c.isTrigger||!c.gameObject.activeInHierarchy)continue;
            int layer=c.gameObject.layer;if(layer==2||layer==UiLayer)continue;
            if(h.distance>=best||Excluded(c))continue;
            best=h.distance;chosen=h;have=true;
        }
        if(!have)return false;
        point=new V3(chosen.point.x,chosen.point.y,chosen.point.z);normal=new V3(chosen.normal.x,chosen.normal.y,chosen.normal.z);
        fraction=best/length;return true;
    }
    internal void Clear()=>excluded.Clear();
}
