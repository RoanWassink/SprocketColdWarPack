using System.Numerics;
using System.Text.Json;
namespace SprocketExpandedBallistics;

internal sealed record ConformCheekResult(ComposerMeshPlan Plan,Vector3[] WallVertices,Vector3[] InwardNormals,int[][] Triangles,bool[] RearClamped,int[] SourceTriangles,int OmittedAtGun);

// Uses only the native built INNER surface. No sampling grid, native writes or callbacks.
internal static class ConformCheekFill
{
    internal const string DirectionNote="The game grows thickness opposite the face normal. Faces point backward so the filling grows forward toward the wall.";
    internal static ConformCheekResult Plan(IReadOnlyList<FillTriangle> inner,double offsetMm,FillSettings settings,CheekSettings limits,float gunHalfGap=.35f,bool gunFallback=true,float gunCentre=0,IReadOnlyList<FillTriangle>? shell=null)
    {
        settings.Validate();limits.Validate();
        if(!double.IsFinite(offsetMm)||offsetMm<settings.ClearanceMm+3||offsetMm>limits.MaximumMm)
            throw new InvalidOperationException($"Enter a wall distance from {settings.ClearanceMm+3:0.#} to {limits.MaximumMm:0.#} mm.");
        if(!float.IsFinite(gunHalfGap)||gunHalfGap<=0||!float.IsFinite(gunCentre))throw new InvalidOperationException("Gun opening unavailable.");
        float rear=(float)limits.RearBoundaryM;
        bool Beside(Vector3[] p)=>p.All(v=>v.X<=gunCentre-gunHalfGap+1e-6f)||p.All(v=>v.X>=gunCentre+gunHalfGap-1e-6f);
        var vertices=new List<Vector3>();var sums=new List<Vector3>();var ids=new Dictionary<Vector3,int>();
        var triangles=new List<int[]>();var sources=new List<int>();
        int Id(Vector3 p){if(ids.TryGetValue(p,out int id))return id;id=vertices.Count;ids.Add(p,id);vertices.Add(p);sums.Add(Vector3.Zero);return id;}
        double forward=Math.Cos(limits.WallAngleDegrees*Math.PI/180);
        for(int i=0;i<inner.Count;i++)
        {
            var t=inner[i];var p=new[]{t.A,t.B,t.C};
            if(p.Any(v=>!ComposerFill.Finite(v)))throw new InvalidOperationException("Inner wall has invalid vertices.");
            var cross=Vector3.Cross(t.B-t.A,t.C-t.A);if(cross.LengthSquared()<1e-14f)continue;
            var n=Vector3.Normalize(cross);
            // Built back-face winding is inward. Do not accept rear/side/outside triangles by abs(n.Z).
            if(-n.Z+1e-6<forward||p.Any(v=>v.Z<rear)||!Beside(p))continue;
            var v=p.Select(Id).ToArray();triangles.Add(v);sources.Add(i);
            foreach(int id in v)sums[id]+=n;
        }
        if(triangles.Count==0)throw new InvalidOperationException("No inner front-wall triangles beside the gun. Check the turret attachment and wall angle.");
        var normals=sums.Select(Vector3.Normalize).ToArray();var shifted=new Vector3[vertices.Count];var clamped=new bool[vertices.Count];
        for(int i=0;i<vertices.Count;i++)
        {
            float distance=(float)offsetMm*.001f;
            if(vertices[i].Z+normals[i].Z*distance<rear){distance=(rear-vertices[i].Z)/normals[i].Z;clamped[i]=true;}
            shifted[i]=vertices[i]+normals[i]*distance;
        }
        // Reject whole triangles that would enter the opening after the inward offset. Never invent cuts.
        var kept=Enumerable.Range(0,triangles.Count).Where(i=>Beside(triangles[i].Select(v=>shifted[v]).ToArray())).ToArray();
        int omitted=triangles.Count-kept.Length;
        var used=kept.SelectMany(i=>triangles[i]).Distinct().OrderBy(i=>i).ToArray();
        var remap=used.Select((id,i)=>(id,i)).ToDictionary(p=>p.id,p=>p.i);
        var points=used.Select(i=>shifted[i]).ToArray();var outputTriangles=new List<int[]>();var faces=new List<object>();
        var edges=new List<int>();var edgeSet=new HashSet<(int,int)>();int misses=0,tight=0;
        foreach(int k in kept)
        {
            var v=triangles[k].Select(id=>remap[id]).ToArray();
            Vector3 Normal(int[] corners){var sum=Vector3.Zero;for(int j=0;j<3;j++)sum+=Vector3.Cross(points[corners[j]],points[corners[(j+1)%3]]);return sum;}
            var n=Normal(v);
            if(n.LengthSquared()<1e-14f)throw new InvalidOperationException("Wall distance collapses a triangle at the rear limit. Use a smaller distance.");
            n=Vector3.Normalize(n);if(n.Z>0){Array.Reverse(v);n=Vector3.Normalize(Normal(v));}
            if(n.Z>=-1e-5f)throw new InvalidOperationException("Wall distance folds a cheek triangle. Use a smaller distance.");
            var thickness=new ushort[3];
            for(int j=0;j<3;j++)
            {
                var hit=WallRay(points[v[j]],-n,inner);
                var shellHit=shell==null?null:WallRay(points[v[j]],-n,shell);
                // An opening must not let a ray choose another inner wall beyond the turret skin.
                if(hit.HasValue&&shellHit.HasValue&&hit.Value>shellHit.Value+.00001)hit=null;
                double mm;
                if(hit.HasValue)mm=hit.Value*1000-settings.ClearanceMm;
                else
                {
                    misses++;
                    // A rear-limited corner has less space, including when its ray misses an opening.
                    mm=Vector3.Distance(points[v[j]],vertices[used[v[j]]])*1000-settings.ClearanceMm;
                }
                if(mm<3){tight++;throw new InvalidOperationException("Less than 3 mm remains at the wall clearance. Use a smaller rear boundary or a larger distance.");}
                if(mm>32767)throw new InvalidOperationException("Wall is too far away to fill safely.");
                thickness[j]=(ushort)Math.Round(mm,MidpointRounding.AwayFromZero);
                if(shellHit.HasValue&&thickness[j]*.001>shellHit.Value-settings.ClearanceMm*.001+.000501)
                    throw new InvalidOperationException("A corner without an inner wall would grow through the turret. Use a smaller distance.");
                for(int e=0;e<3;e++)
                {
                    int a=v[e],b=v[(e+1)%3];var key=(Math.Min(a,b),Math.Max(a,b));
                    if(edgeSet.Add(key))edges.AddRange(new[]{a,b});
                }
            }
            if(!Beside(v.SelectMany((id,j)=>new[]{points[id],points[id]-n*thickness[j]*.001f}).ToArray()))
                throw new InvalidOperationException("A filled triangle would enter the gun opening. Use a smaller distance.");
            outputTriangles.Add(v);faces.Add(new{v,t=thickness,tm=0,te=0});
        }
        if(faces.Count==0)throw new InvalidOperationException("No cheek triangles remain outside the gun opening. Use a smaller distance.");
        string json=JsonSerializer.Serialize(new{majorVersion=0,minorVersion=3,vertices=points.SelectMany(p=>new[]{p.X,p.Y,p.Z}),edges,edgeFlags=Enumerable.Repeat(0,edges.Count/2),faces});
        var rearFlags=used.Select(i=>clamped[i]).ToArray();
        string summary=$"Uses {faces.Count} turret triangles, plate {offsetMm:0.#} mm behind the inner wall; {settings.ClearanceMm:0.#} mm wall clearance. {misses} corners found no wall and use the remaining wall distance minus clearance. {rearFlags.Count(v=>v)} vertices stop at the rear limit. {omitted} triangles omitted to keep the gun opening clear. "+
            (gunFallback?"Gun size unavailable; using a 700 mm centre opening. ":"")+DirectionNote+" Undo removes this change in one step.";
        var plan=new ComposerMeshPlan(json,ComposerFill.Read(json),0,misses,rearFlags.Count(v=>v),summary,true,tight){Action=ComposerMeshAction.ConformCheek,Total=faces.Count*3,MaximumMm=limits.MaximumMm};
        return new(plan,used.Select(i=>vertices[i]).ToArray(),used.Select(i=>normals[i]).ToArray(),outputTriangles.ToArray(),rearFlags,kept.Select(i=>sources[i]).ToArray(),omitted);
    }
    // Double precision at inherited cuts: float cross products can miss a shared corner
    // by a few micrometres and select another wall behind it. Old Fill to wall stays unchanged.
    internal static double? WallRay(Vector3 p,Vector3 direction,IReadOnlyList<FillTriangle> walls)
    {
        double best=double.PositiveInfinity;var d=Vector3.Normalize(direction);
        foreach(var t in walls)
        {
            double ex=(double)t.B.X-t.A.X,ey=(double)t.B.Y-t.A.Y,ez=(double)t.B.Z-t.A.Z;
            double fx=(double)t.C.X-t.A.X,fy=(double)t.C.Y-t.A.Y,fz=(double)t.C.Z-t.A.Z;
            double hx=d.Y*fz-d.Z*fy,hy=d.Z*fx-d.X*fz,hz=d.X*fy-d.Y*fx,det=ex*hx+ey*hy+ez*hz;
            if(Math.Abs(det)<1e-14)continue;
            double sx=(double)p.X-t.A.X,sy=(double)p.Y-t.A.Y,sz=(double)p.Z-t.A.Z;
            double u=(sx*hx+sy*hy+sz*hz)/det;if(u< -1e-5)continue;
            double qx=sy*ez-sz*ey,qy=sz*ex-sx*ez,qz=sx*ey-sy*ex;
            double v=(d.X*qx+d.Y*qy+d.Z*qz)/det;if(v< -1e-5||u+v>1+1e-5)continue;
            double distance=(fx*qx+fy*qy+fz*qz)/det;if(distance>1e-5&&distance<best)best=distance;
        }
        return double.IsPositiveInfinity(best)?null:best;
    }
}
