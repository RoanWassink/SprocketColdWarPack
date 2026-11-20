using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.97: verifies native solid damage collider geometry (SolidColliderGeometry), mirroring,
/// Sprocket's verified MeshStructure ray-triangle winding rules, Forensic derived thickness for
/// native solid blocks, SolidMarks.ForgetMesh, SolidBlockApply.ColliderSummary, and runtime/gate wiring.
/// </summary>
internal static class Feat02297SolidColliderTests
{
    private readonly record struct RayHit(double T,bool Enter,bool Exit);

    private static string F(double v,string fmt="0.###")=>v.ToString(fmt,CultureInfo.InvariantCulture);

    private static (string Json,string? Name) MeshJson(JsonElement root,int vuid)
    {
        var m=root.GetProperty("meshes").EnumerateArray().First(x=>x.GetProperty("vuid").GetInt32()==vuid).GetProperty("meshData");
        return (m.GetProperty("mesh").GetRawText(),m.TryGetProperty("name",out var n)&&n.ValueKind==JsonValueKind.String?n.GetString():null);
    }

    private static float[] Euler(float x,float y,float z)
    {
        float rx=x*MathF.PI/180,ry=y*MathF.PI/180,rz=z*MathF.PI/180;
        float cx=MathF.Cos(rx),sx=MathF.Sin(rx),cy=MathF.Cos(ry),sy=MathF.Sin(ry),cz=MathF.Cos(rz),sz=MathF.Sin(rz);
        return MM(MM(new[]{cy,0,sy,0,1,0,-sy,0,cy},new[]{1,0,0,0,cx,-sx,0,sx,cx}),new[]{cz,-sz,0,sz,cz,0,0,0,1});
    }
    private static float[] MM(float[] a,float[] b){var r=new float[9];for(int i=0;i<3;i++)for(int j=0;j<3;j++){float s=0;for(int k=0;k<3;k++)s+=a[3*i+k]*b[3*k+j];r[3*i+j]=s;}return r;}
    private static Vector3 Mul(float[] m,Vector3 v)=>new(m[0]*v.X+m[1]*v.Y+m[2]*v.Z,m[3]*v.X+m[4]*v.Y+m[5]*v.Z,m[6]*v.X+m[7]*v.Y+m[8]*v.Z);
    private static float[] Transpose(float[] m)=>new[]{m[0],m[3],m[6],m[1],m[4],m[7],m[2],m[5],m[8]};
    private static (float[] R,Vector3 T) World(Dictionary<int,JsonElement> objects,JsonElement o)
    {
        var ts=o.GetProperty("transform");var rot=ts.GetProperty("rot");var pos=ts.GetProperty("pos");
        var r=Euler(rot[0].GetSingle(),rot[1].GetSingle(),rot[2].GetSingle());
        var p=new Vector3(pos[0].GetSingle(),pos[1].GetSingle(),pos[2].GetSingle());
        int parent=o.GetProperty("pvuid").GetInt32();
        if(parent<0||!objects.TryGetValue(parent,out var po))return (r,p);
        var (pr,pt)=World(objects,po);
        return (MM(pr,r),Mul(pr,p)+pt);
    }

    // Exact machine-code rule verified on MeshStructure::WriteIntersectionsToBuffer (RVA 0x1D80615..0x1D80736):
    // n = (B - A) x (C - A); dot(n, dir) <= 0 => Enter (flag 2), dot(n, dir) > 0 => Exit (flag 1).
    private static List<RayHit> RaycastNative(SolidColliderMesh mesh,Vector3 origin,Vector3 dir)
    {
        var hits=new List<RayHit>();
        var d=Vector3.Normalize(dir);
        for(int t=0;t+2<mesh.Triangles.Length;t+=3)
        {
            var a=mesh.Vertices[mesh.Triangles[t]];
            var b=mesh.Vertices[mesh.Triangles[t+1]];
            var c=mesh.Vertices[mesh.Triangles[t+2]];
            if(IntersectTri(origin,d,a,b,c) is not {} dist)continue;
            var n=Vector3.Cross(b-a,c-a);
            float nl=n.Length();
            if(nl<=1e-12f)continue;
            float dot=Vector3.Dot(n/nl,d);
            bool exit=dot>0f;
            hits.Add(new(dist,!exit,exit));
        }
        hits.Sort((x,y)=>x.T.CompareTo(y.T));
        return hits;
    }

    private static double? IntersectTri(Vector3 o,Vector3 d,Vector3 a,Vector3 b,Vector3 c)
    {
        double e1x=b.X-a.X,e1y=b.Y-a.Y,e1z=b.Z-a.Z;
        double e2x=c.X-a.X,e2y=c.Y-a.Y,e2z=c.Z-a.Z;
        double px=d.Y*e2z-d.Z*e2y,py=d.Z*e2x-d.X*e2z,pz=d.X*e2y-d.Y*e2x;
        double det=e1x*px+e1y*py+e1z*pz;
        if(Math.Abs(det)<1e-14)return null;
        double inv=1.0/det;
        double tx=o.X-a.X,ty=o.Y-a.Y,tz=o.Z-a.Z;
        double u=(tx*px+ty*py+tz*pz)*inv;
        if(u<0||u>1)return null;
        double qx=ty*e1z-tz*e1y,qy=tz*e1x-tx*e1z,qz=tx*e1y-ty*e1x;
        double v=(d.X*qx+d.Y*qy+d.Z*qz)*inv;
        if(v<0||u+v>1)return null;
        double dist=(e2x*qx+e2y*qy+e2z*qz)*inv;
        return dist>0?dist:null;
    }

    private static bool StrictlyPaired(IReadOnlyList<RayHit> hits,out double totalChord)
    {
        totalChord=0;
        if(hits.Count==0||hits.Count%2!=0)return false;
        for(int i=0;i+1<hits.Count;i+=2)
        {
            if(!hits[i].Enter||hits[i].Exit)return false;
            if(!hits[i+1].Exit||hits[i+1].Enter)return false;
            double len=hits[i+1].T-hits[i].T;
            if(!(len>=0))return false;
            totalChord+=len;
        }
        return true;
    }

    private static SolidColliderMesh ExtrudePrism(Vector2[] basePoly,float zBack,float zFront)
    {
        int n=basePoly.Length;
        var verts=new Vector3[n*2];
        for(int i=0;i<n;i++)
        {
            verts[i]=new(basePoly[i].X,basePoly[i].Y,zBack);
            verts[n+i]=new(basePoly[i].X,basePoly[i].Y,zFront);
        }
        var faces=new List<int[]>();
        faces.Add(Enumerable.Range(0,n).Reverse().ToArray()); // back z=zBack
        faces.Add(Enumerable.Range(0,n).Select(i=>n+i).ToArray()); // front z=zFront
        for(int i=0;i<n;i++)
        {
            int nxt=(i+1)%n;
            faces.Add(new[]{i,nxt,n+nxt,n+i});
        }
        var res=SolidColliderGeometry.Build(verts,faces);
        if(res.Mesh==null)throw new InvalidOperationException("Unexpected prism build failure: "+res.Refusal);
        return res.Mesh;
    }

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;
        void Check(bool ok,string msg){checks++;if(!ok)failures++;sink(ok,"FEAT97: "+msg);}
        string root=Fix02228Tests.SourceRoot();

        // ================= 1. Unit box & proof blueprints =================
        {
            var unit=ExtrudePrism(new[]{new Vector2(-0.5f,-0.5f),new(0.5f,-0.5f),new(0.5f,0.5f),new(-0.5f,0.5f)},0f,0.5f);
            Check(SolidColliderGeometry.IsWatertightOutward(unit,out var uReason)&&Math.Abs(unit.VolumeM3-0.5)<1e-6,
                "1 unit box 1x1x0.5 m: watertight outward mesh, volume "+F(unit.VolumeM3)+" m3 ("+uReason+")");

            using var proof=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/solid-block/Solid block proof.blueprint")));
            using var chall=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/solid-block/Solid block proof - Challenger 1 Mk3.blueprint")));
            foreach(var (label,bp,depthM,cy) in new[]{
                ("Proof box 500 mm",proof,0.5,0.2f),
                ("Challenger proof box 600 mm",chall,0.6,0.275f)})
            {
                var (json,_)=MeshJson(bp.RootElement,4);
                var shape=SolidShape.FromJson(json);
                var res=SolidColliderGeometry.FromJson(json);
                Check(res.Mesh!=null&&res.Refusal==null,"1 "+label+": SolidColliderGeometry built cleanly");
                var m=res.Mesh!;
                Check(SolidColliderGeometry.IsWatertightOutward(m,out var r)&&Math.Abs(m.VolumeM3-shape.VolumeM3)<1e-6,
                    $"1 {label}: watertight outward ({m.Triangles.Length/3} tris), volume {F(m.VolumeM3,"0.######")} == {F(shape.VolumeM3,"0.######")} m3 ({r})");
                foreach(var deg in new[]{0.0,30.0,45.0})
                {
                    double rad=deg*Math.PI/180;
                    var dir=new Vector3((float)Math.Sin(rad),0f,(float)Math.Cos(rad));
                    var mid=new Vector3(0.013f,cy+0.011f,(float)(depthM*0.5));
                    var hits=RaycastNative(m,mid-dir*2f,dir);
                    double expectedMm=depthM*1000.0/Math.Cos(rad);
                    double chord=0;
                    bool ok=hits.Count==2&&StrictlyPaired(hits,out chord)&&Math.Abs(chord*1000.0-expectedMm)<1e-3;
                    var detail=string.Join(",",hits.Select(h=>$"{(h.Enter?"E":"")}{(h.Exit?"X":"")}@{h.T-2:0.####}"));
                    Check(ok,$"1 {label} @ {deg:0}°: 2 hits (Enter->Exit) [{detail}], chord {F(chord*1000,"0.###")} == {F(expectedMm,"0.###")} mm");
                }
            }
        }

        // ================= 2. Object 292 real curved cast cheeks (44 thick faces of 417..642 mm!) =================
        {
            using var bp292=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/Object-292-1721.blueprint")));
            var (json292,_)=MeshJson(bp292.RootElement,22);
            var shape292=SolidShape.FromJson(json292);
            var res292=SolidColliderGeometry.FromJson(json292);
            Check(res292.Mesh!=null&&res292.Refusal==null,"2 Object 292 mesh 22: SolidColliderGeometry built");
            var m292=res292.Mesh!;
            string r292="";
            Check(m292.Pieces==2&&SolidColliderGeometry.IsWatertightOutward(m292,out r292)
                &&Math.Abs(m292.VolumeM3-shape292.VolumeM3)<0.01*shape292.VolumeM3,
                $"2 Object 292 mesh 22: 2 pieces, watertight outward ({m292.Triangles.Length/3} tris), vol {F(m292.VolumeM3,"0.#####")} vs SolidShape {F(shape292.VolumeM3,"0.#####")} m3 ({r292})");

            // Compute pieces from JSON to sample both cheeks identically to Feat02282SolidBlockFullTests
            using var jdoc=JsonDocument.Parse(json292);
            var vFlat=jdoc.RootElement.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            var pts=new Vector3[vFlat.Length/3];for(int i=0;i<pts.Length;i++)pts[i]=new(vFlat[3*i],vFlat[3*i+1],vFlat[3*i+2]);
            var fcs=jdoc.RootElement.GetProperty("faces").EnumerateArray().Select(f=>f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray()).ToArray();
            var objects=bp292.RootElement.GetProperty("objects").EnumerateArray().ToDictionary(o=>o.GetProperty("vuid").GetInt32(),o=>o);
            var (rot,pos)=World(objects,objects[323]);var inv=Transpose(rot);
            var world=pts.Select(q=>Mul(rot,q)+pos).ToArray();
            var uf=Enumerable.Range(0,fcs.Length).ToArray();
            int UF(int i){while(uf[i]!=i)i=uf[i]=uf[uf[i]];return i;}
            var owner=new Dictionary<(int,int),int>();
            for(int f=0;f<fcs.Length;f++)for(int j=0;j<fcs[f].Length;j++){int a=fcs[f][j],b=fcs[f][(j+1)%fcs[f].Length];var k=(Math.Min(a,b),Math.Max(a,b));if(owner.TryGetValue(k,out var g))uf[UF(f)]=UF(g);else owner[k]=f;}
            var groups=Enumerable.Range(0,fcs.Length).GroupBy(UF).ToArray();

            int tested=0,paired=0,odd=0;
            foreach(var g in groups)
            {
                var cp=g.SelectMany(f=>fcs[f]).Distinct().Select(i=>world[i]).ToArray();
                var lo=cp.Aggregate(Vector3.Min);var hi=cp.Aggregate(Vector3.Max);var mid=(lo+hi)*0.5f;
                foreach(var h in new[]{0.25f,0.5f,0.75f})foreach(var yaw in new[]{0f,30f,-30f})
                {
                    var dirW=new Vector3(-MathF.Sin(yaw*MathF.PI/180),0f,-MathF.Cos(yaw*MathF.PI/180));
                    var originW=new Vector3(mid.X+0.0037f,lo.Y+(hi.Y-lo.Y)*h+0.0029f,mid.Z)-dirW*3f;
                    var hits=RaycastNative(m292,Mul(inv,originW-pos),Vector3.Normalize(Mul(inv,dirW)));
                    if(hits.Count==0)continue;
                    tested++;
                    if(hits.Count%2!=0)odd++;
                    if(StrictlyPaired(hits,out var chord)&&chord>0.01)paired++;
                }
            }
            Check(tested>=14&&odd==0&&paired==tested,
                $"2 Object 292 18 cheek rays ({tested} intersecting): {paired}/{tested} strictly paired Enter->Exit, {odd} odd counts (0 expected; unaffected by 44 thick faces)");
        }

        // ================= 3. Open mesh, non-manifold, hole & degenerate refusals =================
        {
            using var bp293=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/Object-293-2038.blueprint")));
            var (json293,_)=MeshJson(bp293.RootElement,2);
            var open=SolidColliderGeometry.FromJson(json293);
            Check(open.Mesh==null&&!string.IsNullOrEmpty(open.Refusal),"3 Object 293 open cheeks refused cleanly: "+open.Refusal);

            // Non-manifold internal wall bisecting a closed box (all shared edges count==3, zero open edges)
            var nmPts=new Vector3[]{
                new(0,0,0),new(1,0,0),new(2,0,0),new(2,1,0),new(1,1,0),new(0,1,0),
                new(0,0,1),new(1,0,1),new(2,0,1),new(2,1,1),new(1,1,1),new(0,1,1)};
            var nmFaces=new[]{
                new[]{0,1,4,5},new[]{1,2,3,4},new[]{6,7,10,11},new[]{7,8,9,10},
                new[]{0,1,7,6},new[]{1,2,8,7},new[]{2,3,9,8},new[]{3,4,10,9},new[]{4,5,11,10},new[]{5,0,6,11},
                new[]{1,4,10,7}}; // internal septum
            var nonManifold=SolidColliderGeometry.Build(nmPts,nmFaces);
            Check(nonManifold.Mesh==null&&nonManifold.Refusal?.Contains("non-manifold",StringComparison.Ordinal)==true,"3 non-manifold inner wall refused cleanly: "+nonManifold.Refusal);

            var boxPts=new Vector3[]{new(0,0,0),new(1,0,0),new(1,1,0),new(0,1,0),new(0,0,1),new(1,0,1),new(1,1,1),new(0,1,1)};
            var boxFaces=new[]{new[]{0,3,2,1},new[]{4,5,6,7},new[]{0,1,5,4},new[]{2,3,7,6},new[]{1,2,6,5},new[]{0,4,7,3}};

            var snap=ComposerFill.Read(json293,true);
            var holeFaces=(SnapFace[])snap.Faces.Clone();
            holeFaces[0]=holeFaces[0] with{HoleLoops=1};
            var hole=SolidColliderGeometry.FromSnapshot(snap with{Faces=holeFaces});
            Check(hole.Mesh==null&&hole.Refusal?.Contains("hole",StringComparison.OrdinalIgnoreCase)==true,"3 face with HoleLoops>0 refused cleanly: "+hole.Refusal);

            var flatPts=boxPts.Select(p=>p with{Z=0}).ToArray();
            var zeroVol=SolidColliderGeometry.Build(flatPts,boxFaces);
            Check(zeroVol.Mesh==null&&!string.IsNullOrEmpty(zeroVol.Refusal),"3 zero-volume collapsed box refused cleanly: "+zeroVol.Refusal);
        }

        // ================= 4. Concave quad & concave 5-gon triangulation =================
        {
            // Concave arrowhead quad: area = 0.4 m^2, depth = 0.5 m => volume = 0.2 m^3
            var dart=ExtrudePrism(new[]{new Vector2(0,0),new(1,1),new(0,0.4f),new(-1,1)},0f,0.5f);
            Check(SolidColliderGeometry.IsWatertightOutward(dart,out var dr)&&Math.Abs(dart.VolumeM3-0.2)<1e-6,
                $"4 concave quad prism: watertight outward, volume {F(dart.VolumeM3,"0.######")} == 0.2 m3 ({dr})");
            var notchMiss=RaycastNative(dart,new Vector3(0.01f,0.75f,-1f),Vector3.UnitZ);
            var cleftHit=RaycastNative(dart,new Vector3(0.01f,0.20f,-1f),Vector3.UnitZ);
            var wingHit=RaycastNative(dart,new Vector3(0.45f,0.65f,-1f),Vector3.UnitZ);
            double cc=0,wc=0;
            Check(notchMiss.Count==0&&StrictlyPaired(cleftHit,out cc)&&Math.Abs(cc-0.5)<1e-5&&StrictlyPaired(wingHit,out wc)&&Math.Abs(wc-0.5)<1e-5,
                $"4 concave quad raycasts: notch misses ({notchMiss.Count} hits), cleft={F(cc)} m, wing={F(wc)} m");

            // Concave pentagon (unit square minus top V-notch of base 1 and height 0.6 => area 0.7), depth 0.3 => vol 0.21 m^3
            var pent=ExtrudePrism(new[]{new Vector2(0,0),new(1,0),new(1,1),new(0.5f,0.4f),new(0,1)},0f,0.3f);
            Check(SolidColliderGeometry.IsWatertightOutward(pent,out var pr)&&Math.Abs(pent.VolumeM3-0.21)<1e-6,
                $"4 concave 5-gon prism: watertight outward, volume {F(pent.VolumeM3,"0.######")} == 0.21 m3 ({pr})");
            var pNotch=RaycastNative(pent,new Vector3(0.5f,0.75f,-1f),Vector3.UnitZ);
            var pLeft=RaycastNative(pent,new Vector3(0.2f,0.7f,-1f),Vector3.UnitZ);
            var pRight=RaycastNative(pent,new Vector3(0.8f,0.7f,-1f),Vector3.UnitZ);
            var pBase=RaycastNative(pent,new Vector3(0.5f,0.2f,-1f),Vector3.UnitZ);
            double lc=0,rc=0,bc=0;
            Check(pNotch.Count==0&&StrictlyPaired(pLeft,out lc)&&Math.Abs(lc-0.3)<1e-5&&StrictlyPaired(pRight,out rc)&&Math.Abs(rc-0.3)<1e-5&&StrictlyPaired(pBase,out bc)&&Math.Abs(bc-0.3)<1e-5,
                $"4 concave 5-gon raycasts: notch misses ({pNotch.Count}), left={F(lc)} m, right={F(rc)} m, base={F(bc)} m");
        }

        // ================= 5. Flipped-winding recovery =================
        {
            var pts=new Vector3[]{new(-0.5f,-0.5f,0),new(0.5f,-0.5f,0),new(0.5f,0.5f,0),new(-0.5f,0.5f,0),
                new(-0.5f,-0.5f,0.5f),new(0.5f,-0.5f,0.5f),new(0.5f,0.5f,0.5f),new(-0.5f,0.5f,0.5f)};
            var good=new[]{new[]{0,3,2,1},new[]{4,5,6,7},new[]{0,1,5,4},new[]{2,3,7,6},new[]{1,2,6,5},new[]{0,4,7,3}};
            var allFlipped=good.Select(f=>f.Reverse().ToArray()).ToArray();
            var halfFlipped=good.Select((f,i)=>i%2==0?f.Reverse().ToArray():f).ToArray();
            foreach(var (tag,fcs) in new[]{("all-inverted",allFlipped),("checker-inverted",halfFlipped)})
            {
                var res=SolidColliderGeometry.Build(pts,fcs);
                Check(res.Mesh!=null&&res.Mesh.ReversedFaces>0&&SolidColliderGeometry.IsWatertightOutward(res.Mesh,out _)&&Math.Abs(res.Mesh.VolumeM3-0.5)<1e-6,
                    $"5 {tag}: repaired outward (reversed={res.Mesh?.ReversedFaces}), vol={F(res.Mesh?.VolumeM3??0)} m3");
                var hits=RaycastNative(res.Mesh!,new Vector3(0.05f,0.13f,-2f),Vector3.UnitZ);
                double ch=0;
                Check(hits.Count==2&&StrictlyPaired(hits,out ch)&&Math.Abs(ch-0.5)<1e-5,
                    $"5 {tag} raycast: [0]=Enter z={F(hits[0].T-2)}, [1]=Exit z={F(hits[1].T-2)}, chord={F(ch)} m");
            }
        }

        // ================= 6. Mirroring (AppendMirrored) =================
        {
            var pts=new Vector3[]{new(0.2f,-0.2f,-0.25f),new(0.8f,-0.2f,-0.25f),new(0.8f,0.2f,-0.25f),new(0.2f,0.2f,-0.25f),
                new(0.2f,-0.2f,0.25f),new(0.8f,-0.2f,0.25f),new(0.8f,0.2f,0.25f),new(0.2f,0.2f,0.25f)};
            var fcs=new[]{new[]{0,3,2,1},new[]{4,5,6,7},new[]{0,1,5,4},new[]{2,3,7,6},new[]{1,2,6,5},new[]{0,4,7,3}};
            var single=SolidColliderGeometry.Build(pts,fcs).Mesh!;
            var mirrorX=new SolidAffine(-1,0,0,0,0,1,0,0,0,0,1,0);
            var mirrored=SolidColliderGeometry.AppendMirrored(single,mirrorX);
            string mr="";
            Check(mirrorX.LinearDeterminant==-1&&mirrored.Vertices.Length==16&&mirrored.Triangles.Length==72&&mirrored.Pieces==2
                &&Math.Abs(mirrored.VolumeM3-2*single.VolumeM3)<1e-6&&SolidColliderGeometry.IsWatertightOutward(mirrored,out mr),
                $"6 AppendMirrored (det=-1): 16 verts, 24 tris, 2 pieces, vol={F(mirrored.VolumeM3)} m3, watertight outward ({mr})");
            var hitPos=RaycastNative(mirrored,new Vector3(0.51f,0.01f,-2f),Vector3.UnitZ);
            var hitNeg=RaycastNative(mirrored,new Vector3(-0.51f,0.01f,-2f),Vector3.UnitZ);
            double cp=0,cn=0;
            Check(hitPos.Count==2&&StrictlyPaired(hitPos,out cp)&&Math.Abs(cp-0.5)<1e-5
                &&hitNeg.Count==2&&StrictlyPaired(hitNeg,out cn)&&Math.Abs(cn-0.5)<1e-5,
                $"6 mirrored raycasts: +X chord={F(cp)} m (2 hits), -X chord={F(cn)} m (2 hits)");

            var rot180=new SolidAffine(-1,0,0,0,0,1,0,0,0,0,-1,2f);
            var rotMirrored=SolidColliderGeometry.AppendMirrored(single,rot180);
            Check(rot180.LinearDeterminant==1&&SolidColliderGeometry.IsWatertightOutward(rotMirrored,out _),
                "6 AppendMirrored (det=+1): keeps winding without flipping");
        }

        // ================= 7. Forensic DeriveThickness for native solid block vs legacy SolidMarker =================
        {
            var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets.ToDictionary(p=>p.MaterialId,p=>p);
            CombatCompositeLayers.Configure(id=>id!=null&&presets.TryGetValue(id,out var found)?found:null);
            try
            {
                const int entity=701;
                const string stage="Main",hitId="sim:0:0:1",mat="ebCassette_t80u",label="T-80U cellular cast armour";
                var piece=new PaidMaterialPiece(entity,mat,label,1.0,1.5,0.65,30.0);
                var start=new CombatReportEvent("layerStart",stage,"Reached layer",700,null,hitId,
                    new CombatLayerInfo(hitId,new[]{new CombatLayerPart(entity,CompositePlate.SolidBlockName,mat,label,null,30.0,true)},false));
                var reaction=new CombatReportEvent("materialReaction",stage,"Solid",700,375,hitId){PaidIntervals=new[]{piece}};
                var shot=new CombatReportShot(1,"Outgoing","120 mm APFSDS",1650,new[]{start,reaction},0,700,"Test",4,4,"Test");

                var applied=LayeredPlateReport.Apply(shot);
                var part=applied.Events.First(e=>e.Kind=="layerStart").Layer!.Parts[0];
                double expect=500.0*Math.Cos(30.0*Math.PI/180.0);
                Check(part.ThicknessMm is {} tmm&&Math.Abs(tmm-expect)<1e-6&&part.Name==CompositePlate.SolidBlockName+LayeredPlateReport.DerivedLabel,
                    $"7 DeriveThickness on native solid block (no SolidMarker): {F(part.ThicknessMm??0)} == {F(expect)} mm + label");

                var forensic=CombatForensics.Build(applied);
                Check(forensic.Layers.Count==1&&forensic.Layers[0].Sublayers is {Count:>1} subs&&subs.Count==presets[mat].Layers.Length,
                    $"7 Forensic renders full band with {forensic.Layers.FirstOrDefault()?.Sublayers?.Count??0} T-80U recipe sublayers");

                var legacyMarker=new CombatReportEvent(LayeredPlateReport.SolidMarker,stage,"marker",HitId:hitId,RelatedEntity:entity);
                var legacyEvents=new[]{start,reaction,legacyMarker};
                var untouched=LayeredPlateReport.DeriveThickness(legacyEvents,legacyEvents);
                Check(ReferenceEquals(untouched,legacyEvents),"7 legacy shot WITH SolidMarker is left untouched by DeriveThickness");
            }
            finally{CombatCompositeLayers.Configure(null);}
        }

        // ================= 8. SolidMarks.ForgetMesh & SolidBlockApply.ColliderSummary =================
        {
            SolidMarks.Reset();
            bool prev=SolidBlockApply.NativeCollider;
            try
            {
                SolidMarks.Built(101,0xAAA,true,false,false,()=>CompositePlate.Name,"Build",false);
                SolidMarks.Built(102,0xBBB,true,false,false,()=>CompositePlate.SolidBlockName,"Build",false);
                Check(SolidMarks.Answer(101,out _,out _)==SolidMarkAnswer.Unmarked&&SolidMarks.Answer(102,out _,out _)==SolidMarkAnswer.Marked,
                    "8 SolidMarks recorded plate 101 unmarked (mesh 0xAAA) and plate 102 marked (mesh 0xBBB)");
                SolidMarks.ForgetMesh(0xAAA);
                Check(SolidMarks.Answer(101,out _,out _)==SolidMarkAnswer.Unknown&&SolidMarks.Answer(102,out _,out _)==SolidMarkAnswer.Marked,
                    "8 SolidMarks.ForgetMesh(0xAAA) forgets stale entry without disturbing other meshes");

                SolidBlockApply.NativeCollider=true;
                Check(SolidBlockApply.ColliderSummary==SolidBlockApply.SolidCollider&&SolidBlockApply.Note(2,0.3693,7866).Contains(SolidBlockApply.SolidCollider,StringComparison.Ordinal),
                    "8 SolidBlockApply.ColliderSummary reports SolidCollider when NativeCollider=true");
                SolidBlockApply.NativeCollider=false;
                Check(SolidBlockApply.ColliderSummary==SolidBlockApply.ThinShell&&SolidBlockApply.Note(2,0.3693,7866).Contains(SolidBlockApply.ThinShell,StringComparison.Ordinal),
                    "8 SolidBlockApply.ColliderSummary reports ThinShell when NativeCollider=false");
            }
            finally{SolidMarks.Reset();SolidBlockApply.NativeCollider=prev;}
        }

        // ================= 9. Runtime glue, GameBuildGate & hook-count source invariants =================
        {
            string Read(string f)=>File.ReadAllText(Path.Combine(root,f)).Replace("\r\n","\n");
            var rsc=Read("RuntimeSolidCollider.cs");
            Check(rsc.Contains("[HarmonyPrefix,HarmonyPatch(typeof(PlateStructure),nameof(PlateStructure.BuildDamageModelColliders))]",StringComparison.Ordinal)
                &&rsc.Contains("AddMesh(",StringComparison.Ordinal)
                &&rsc.Contains("Mirrored",StringComparison.Ordinal)
                &&rsc.Contains("GetMirrorMatrix",StringComparison.Ordinal)
                &&rsc.Contains("PlateMeshAccess.Gate.Busy",StringComparison.Ordinal)
                &&rsc.Contains("return true;",StringComparison.Ordinal),
                "9 RuntimeSolidCollider patches BuildDamageModelColliders as Prefix, uses 6-arg AddMesh, mirrors via GetMirrorMatrix, checks Gate.Busy, falls back with return true");

            var plugin=Read("Plugin.cs");
            Check(plugin.Contains("RuntimeSolidCollider.Configure(Config);",StringComparison.Ordinal)
                &&plugin.Contains("if(GameBuildGate.AllowHotPath)try{new Harmony(\"sprocket.expandedballistics.solid-collider\").PatchAll(typeof(RuntimeSolidCollider));}",StringComparison.Ordinal)
                &&plugin.Contains("new Harmony(\"sprocket.expandedballistics.solid-collider\").UnpatchSelf();RuntimeSolidCollider.Clear();",StringComparison.Ordinal),
                "9 Plugin.cs configures RuntimeSolidCollider, gates its PatchAll behind GameBuildGate.AllowHotPath, and unpatches/clears in Unload");

            var gp=Read("RuntimeGroupedProtection.cs");
            Check(gp.Contains("if(RuntimeSolidCollider.IsNativeSolid(plate)){if(diag!=null)diag.Exit=\"nativeSolid\";return;}",StringComparison.Ordinal),
                "9 RuntimeGroupedProtection.SolidCavity skips cavity via IsNativeSolid(plate) with exit=\"nativeSolid\"");

            foreach(var untouched in new[]{"NativeMaterialReactions.cs","NativeMaterialIntervals.cs","MaterialIntervalLedger.cs"})
                Check(!Read(untouched).Contains("RuntimeSolidCollider",StringComparison.Ordinal),"9 "+untouched+" untouched");

            int hooks=Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories)
                .Where(f=>{var r=f.Replace('\\','/');return !r.Contains("/tests/")&&!r.Contains("/bin/")&&!r.Contains("/obj/");})
                .Sum(f=>File.ReadLines(f).Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal)));
            Check(hooks==48,"9 total [HarmonyPatch lines in non-test sources is "+hooks+" (expected 48)");
        }

        Console.WriteLine($"FEAT97: {checks} checks, {failures} failures");
    }
}
