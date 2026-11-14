using System.Numerics;
namespace SprocketExpandedBallistics;

internal static class PlateStackGeometry
{
    internal sealed record Layer(Vector3[] Surface,Vector3[] Solid,Vector3? Centre=null);
    internal sealed record Plan(Vector3 Normal,Vector3[] Translations,double[] ThicknessMm,bool FootprintsDiffer=false,Quaternion[]? Rotations=null,Vector3[]? Centres=null);
    internal static int[] ParentFirst(IReadOnlyList<int> parents)
    {
        var result=new List<int>();var visiting=new bool[parents.Count];var done=new bool[parents.Count];
        void Visit(int i){if(done[i])return;if(visiting[i])throw new InvalidOperationException("Plate attachment hierarchy contains a cycle.");visiting[i]=true;var p=parents[i];if(p>=0){if(p>=parents.Count)throw new InvalidOperationException("Invalid parent index.");Visit(p);}visiting[i]=false;done[i]=true;result.Add(i);}
        for(int i=0;i<parents.Count;i++)Visit(i);return result.ToArray();
    }
    internal static Quaternion AlignNormal(Vector3 from,Vector3 to)
    {
        from=Vector3.Normalize(from);to=Vector3.Normalize(to);var dot=Math.Clamp(Vector3.Dot(from,to),-1,1);
        if(dot>1-1e-7f)return Quaternion.Identity;
        if(dot<-1+1e-7f){var basis=Math.Abs(from.X)<.8f?Vector3.UnitX:Vector3.UnitY;return Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(from,basis)),MathF.PI);}
        var cross=Vector3.Cross(from,to);return Quaternion.Normalize(new Quaternion(cross,1+dot));
    }
    private static Vector3 EdgeAxis(Vector3[] points,Vector3 normal,Vector3? preferred=null)
    {
        var u=Vector3.Normalize(points[1]-points[0]);var v=Vector3.Cross(normal,u);
        var projected=points.Select(p=>(P:p,X:Vector3.Dot(p,u),Y:Vector3.Dot(p,v))).OrderBy(p=>p.X).ThenBy(p=>p.Y).ToArray();
        var hull=new List<(Vector3 P,float X,float Y)>();
        float Cross((Vector3 P,float X,float Y) a,(Vector3 P,float X,float Y) b,(Vector3 P,float X,float Y) c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        foreach(var p in projected){while(hull.Count>1&&Cross(hull[^2],hull[^1],p)<=1e-7f)hull.RemoveAt(hull.Count-1);hull.Add(p);}
        int lower=hull.Count;
        for(int i=projected.Length-2;i>=0;i--){var p=projected[i];while(hull.Count>lower&&Cross(hull[^2],hull[^1],p)<=1e-7f)hull.RemoveAt(hull.Count-1);hull.Add(p);}
        if(hull.Count>1)hull.RemoveAt(hull.Count-1);
        var edges=Enumerable.Range(0,hull.Count).Select(i=>hull[(i+1)%hull.Count].P-hull[i].P).Where(e=>e.LengthSquared()>1e-12f).ToArray();
        var longest=edges.Max(e=>e.LengthSquared());
        var candidates=edges.Where(e=>e.LengthSquared()>=longest-1e-5f).Select(Vector3.Normalize);
        return preferred.HasValue?candidates.OrderByDescending(e=>Math.Abs(Vector3.Dot(e,preferred.Value))).First():candidates.First();
    }
    internal static bool TrySurface(IReadOnlyList<Vector3[]> faces,out Vector3[] surface,out string reason)
        =>TrySurface(faces,out surface,out _,out reason);
    internal static bool TrySurface(IReadOnlyList<Vector3[]> faces,out Vector3[] surface,out Vector3 centre,out string reason)
    {
        surface=Array.Empty<Vector3>();centre=default;reason="A plate has no usable flat face.";
        if(faces.Count==0)return false;
        var valid=new List<(Vector3[] Points,Vector3 Normal,float Area)>();
        foreach(var points in faces)
        {
            if(points.Length<3||points.Any(v=>!Finite(v)))return false;
            Vector3 area=default;
            for(int i=1;i<points.Length-1;i++)area+=Vector3.Cross(points[i]-points[0],points[i+1]-points[0]);
            if(area.LengthSquared()<1e-12f)return false;
            var normal=Vector3.Normalize(area);
            if(points.Any(v=>Math.Abs(Vector3.Dot(v-points[0],normal))>1e-5)){reason="Curved or non-planar plates cannot be stacked.";return false;}
            valid.Add((points,normal,area.Length()));
        }
        var broad=valid.OrderByDescending(f=>f.Area).First();
        var planes=new List<float>();
        foreach(var face in valid)
        {
            var parallel=Math.Abs(Vector3.Dot(face.Normal,broad.Normal));
            if(parallel>.99999f)
            {
                var offset=Vector3.Dot(face.Points[0],broad.Normal);
                if(!planes.Any(p=>Math.Abs(p-offset)<1e-5))planes.Add(offset);
            }
            else if(parallel>1e-5){reason="Wedge or angled side faces cannot be stacked.";return false;}
        }
        if(planes.Count>2){reason="Stepped structures are not flat plates.";return false;}
        if(planes.Count==2&&valid.SelectMany(f=>f.Points).Any(p=>!planes.Any(d=>Math.Abs(Vector3.Dot(p,broad.Normal)-d)<1e-5)))
        {reason="Wedge or stepped side faces cannot be stacked.";return false;}
        var broadOffset=Vector3.Dot(broad.Points[0],broad.Normal);
        var broadFaces=valid.Where(f=>Math.Abs(Vector3.Dot(f.Normal,broad.Normal))>.99999f&&Math.Abs(Vector3.Dot(f.Points[0],broad.Normal)-broadOffset)<1e-5).ToArray();
        surface=broadFaces.SelectMany(f=>f.Points).Distinct().ToArray();
        centre=broadFaces.Aggregate(Vector3.Zero,(sum,f)=>sum+PolygonCentre(f.Points,f.Normal)*f.Area)/broadFaces.Sum(f=>f.Area);
        reason="";return true;
    }
    private static Vector3 PolygonCentre(Vector3[] points,Vector3 normal)
    {
        double area=0;Vector3 weighted=default;
        for(int i=1;i<points.Length-1;i++)
        {var signed=Vector3.Dot(Vector3.Cross(points[i]-points[0],points[i+1]-points[0]),normal);area+=signed;weighted+=(points[0]+points[i]+points[i+1])*(signed/3);}
        return weighted/(float)area;
    }
    internal static bool TryPlan(IReadOnlyList<Layer> layers,double gapMm,out Plan? plan,out string reason,bool reverseDirection=false)
    {
        plan=null;reason="Select at least two flat addon plates.";
        if(layers.Count<2||layers.Count>64)return false;
        if(!double.IsFinite(gapMm)||gapMm<0||gapMm>1000){reason="Gap must be between 0 and 1000 mm.";return false;}
        var normals=new Vector3[layers.Count];var centres=new Vector3[layers.Count];
        var rotations=new Quaternion[layers.Count];var aligned=new Layer[layers.Count];Vector3 referenceNormal=default,referenceAxis=default;
        var low=new double[layers.Count];var high=new double[layers.Count];
        Vector3 normal=default;
        for(int i=0;i<layers.Count;i++)
        {
            var layer=layers[i];
            if(layer.Surface.Length<3||layer.Solid.Length<4||layer.Surface.Concat(layer.Solid).Any(v=>!Finite(v))){reason="A plate has missing or invalid built geometry.";return false;}
            var a=layer.Surface[0];Vector3 n=default;
            for(int j=1;j<layer.Surface.Length-1;j++){n=Vector3.Cross(layer.Surface[j]-a,layer.Surface[j+1]-a);if(n.LengthSquared()>1e-12f)break;}
            if(n.LengthSquared()<=1e-12f){reason="A plate has no usable flat face.";return false;}
            n=Vector3.Normalize(n);normals[i]=n;if(i==0){referenceNormal=n;normal=reverseDirection?-n:n;referenceAxis=EdgeAxis(layer.Surface,n);}
            if(layer.Surface.Any(v=>Math.Abs(Vector3.Dot(v-a,n))>1e-5)){reason="Curved or non-planar plates cannot be stacked.";return false;}
            centres[i]=layer.Centre??PolygonCentre(layer.Surface,n);
            if(!Finite(centres[i])){reason="A plate has an invalid surface centre.";return false;}
            rotations[i]=i==0?Quaternion.Identity:AlignNormal(n,referenceNormal);
            var centre=centres[i];var rotation=rotations[i];
            if(i>0)
            {
                var normalAligned=layer.Surface.Select(p=>centre+Vector3.Transform(p-centre,rotation)).ToArray();
                var alignedAxis=EdgeAxis(normalAligned,referenceNormal,referenceAxis);if(Vector3.Dot(alignedAxis,referenceAxis)<0)alignedAxis=-alignedAxis;
                var angle=MathF.Atan2(Vector3.Dot(referenceNormal,Vector3.Cross(alignedAxis,referenceAxis)),Vector3.Dot(alignedAxis,referenceAxis));
                rotation=Quaternion.Normalize(Quaternion.CreateFromAxisAngle(referenceNormal,angle)*rotation);rotations[i]=rotation;
            }
            layer=new Layer(layer.Surface.Select(p=>centre+Vector3.Transform(p-centre,rotation)).ToArray(),layer.Solid.Select(p=>centre+Vector3.Transform(p-centre,rotation)).ToArray(),centre);aligned[i]=layer;
            low[i]=layer.Solid.Min(v=>(double)Vector3.Dot(v,normal));high[i]=layer.Solid.Max(v=>(double)Vector3.Dot(v,normal));
            if(high[i]-low[i]<1e-6){reason="A plate has no built armour thickness.";return false;}
            var u=Vector3.Normalize(layer.Surface[1]-layer.Surface[0]);var v=Vector3.Normalize(Vector3.Cross(referenceNormal,u));
            var width=layer.Surface.Max(p=>Vector3.Dot(p,u))-layer.Surface.Min(p=>Vector3.Dot(p,u));
            var height=layer.Surface.Max(p=>Vector3.Dot(p,v))-layer.Surface.Min(p=>Vector3.Dot(p,v));
            if(Math.Min(width,height)<(high[i]-low[i])*2){reason="The selected structure is too deep to be a flat slab.";return false;}
        }
        var offsets=new Vector3[layers.Count];var thickness=new double[layers.Count];
        for(int i=0;i<layers.Count;i++)
        {
            thickness[i]=(high[i]-low[i])*1000;
            if(i==0)continue;
            var targetLow=high[i-1]+Vector3.Dot(offsets[i-1],normal)+gapMm*.001;
            var tangent=centres[0]-centres[i];tangent-=normal*Vector3.Dot(tangent,normal);
            offsets[i]=tangent+normal*(float)(targetLow-low[i]);
        }
        var axis=Vector3.Normalize(layers[0].Surface[1]-layers[0].Surface[0]);var other=Vector3.Cross(normal,axis);
        float Span(Layer l,Vector3 a)=>l.Surface.Max(v=>Vector3.Dot(v,a))-l.Surface.Min(v=>Vector3.Dot(v,a));
        var different=aligned.Skip(1).Any(l=>Math.Abs(Span(l,axis)-Span(aligned[0],axis))>1e-5||Math.Abs(Span(l,other)-Span(aligned[0],other))>1e-5);
        plan=new(normal,offsets,thickness,different,rotations,centres);reason="";return true;
    }
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
}

