using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

internal enum ComposerMeshAction { Flip, Inward, Fill, CheekPreset, ConformCheek, CheekShell, LayerConvert, BackToBox }
internal readonly record struct FillTriangle(Vector3 A,Vector3 B,Vector3 C);
internal sealed record FillBounds(string Label,Vector3 Min,Vector3 Max)
{
    internal bool Overlaps(FillBounds b)=>Min.X<=b.Max.X&&Max.X>=b.Min.X&&Min.Y<=b.Max.Y&&Max.Y>=b.Min.Y&&Min.Z<=b.Max.Z&&Max.Z>=b.Min.Z;
    internal static FillBounds Of(string label,IEnumerable<Vector3> points)
    {
        var p=points.ToArray();if(p.Length==0)throw new InvalidOperationException("No bounds geometry.");
        return new(label,p.Aggregate(Vector3.Min),p.Aggregate(Vector3.Max));
    }
    internal static IEnumerable<Vector3> RendererCorners(Vector3 centre,Vector3 extents,Func<Vector3,Vector3> map)
    {
        for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            yield return map(centre+new Vector3(extents.X*x,extents.Y*y,extents.Z*z));
    }
}
internal sealed record FillSettings(double ClearanceMm=10,double MaximumMm=500,double ReserveMm=50)
{
    internal void Validate()
    {
        if(!double.IsFinite(ClearanceMm)||!double.IsFinite(MaximumMm)||!double.IsFinite(ReserveMm)||ClearanceMm<0||MaximumMm<3||MaximumMm>32767||ReserveMm<3||ReserveMm>MaximumMm)
            throw new InvalidOperationException("Fill settings need clearance >= 0 and 3 <= reserve <= maximum <= 32767 mm.");
    }
}
internal sealed record CheekSettings(double MaximumMm=450,double RearBoundaryM=.35,double RowDepthMm=150,double WallAngleDegrees=60)
{
    internal void Validate(){if(!double.IsFinite(MaximumMm)||MaximumMm<3||MaximumMm>2000||!double.IsFinite(RearBoundaryM)||!double.IsFinite(RowDepthMm)||RowDepthMm<1||!double.IsFinite(WallAngleDegrees)||WallAngleDegrees<0||WallAngleDegrees>85)throw new InvalidOperationException("Invalid cheek thickness, rear limit or wall angle.");}
}
internal sealed record ComposerMeshPlan(string Json,PlateMeshSnapshot After,int Flipped,int Misses,int Limited,string Summary,bool NewTopology=false,int Tight=0,int EdgeCorners=0)
{
    internal ComposerMeshAction Action {get;init;}
    internal int Total {get;init;}
    internal bool[][] Missing {get;init;}=Array.Empty<bool[]>();
    internal double MaximumMm {get;init;}=500;
    internal bool Confirmed {get;init;}
    /// <summary>0.22.57 Cheek fill (shell): the real back point of every corner (ribs, not the face normal). Null for every other action.</summary>
    internal Vector3[][]? BackCorners {get;init;}
    /// <summary>0.22.57 Cheek fill (shell): checks the saved JSON of the native copy (ribs, te) before the mesh is replaced. Null for every other action.</summary>
    internal Func<string,string?>? CopyCheck {get;init;}
    internal bool NeedsConfirmation=>Action==ComposerMeshAction.Fill&&(Misses>0||Limited>0);
    internal bool CanCommit=>!NeedsConfirmation||Confirmed;
    internal string Label=>ComposerFill.Label(Action);
    internal string FillInspection=>$"{Total-Misses} of {Total} corners found a wall.\n{Misses} corners found no wall.\n{Limited} corners stop at the maximum of {MaximumMm:0.#} mm.\nChoose a thickness for corners without a wall.\nUndo removes this change in one step.";
    internal ComposerMeshPlan Accept(double reserveMm)
    {
        if(!double.IsFinite(reserveMm)||reserveMm<3||reserveMm>MaximumMm)throw new InvalidOperationException($"Enter a thickness from 3 to {MaximumMm:0.#} mm.");
        var t=After.ThicknessList().ToArray();
        for(int i=0;i<Missing.Length;i++)for(int j=0;j<Missing[i].Length;j++)if(Missing[i][j])t[i][j]=(ushort)Math.Round(reserveMm,MidpointRounding.AwayFromZero);
        var payload=ComposerFill.Rewrite(Json,new bool[After.FaceCount],t,new bool[After.FaceCount]);
        return this with{Json=payload,After=ComposerFill.Read(payload),Confirmed=true,Summary=Summary+$" Corners without a wall use {reserveMm:0.#} mm."};
    }
    internal (double VolumeM3,double MassKg,string[] Warnings) Inspect(double density,IReadOnlyList<FillBounds> obstacles)
    {
        if(!double.IsFinite(density)||density<=0)throw new InvalidOperationException("Material density unavailable.");
        var volumes=BackCorners==null?After.Faces.Select(ComposerFill.Volume).ToArray():After.Faces.Select((f,i)=>ComposerFill.PrismVolume(f.Corners,BackCorners[i])).ToArray();
        var solids=After.Faces.Select((f,i)=>FillBounds.Of("insert",f.Corners.Concat(BackCorners?[i]??f.Corners.Select((p,j)=>p-f.Normal*f.Thickness[j]*.001f)))).ToArray();
        return (volumes.Sum(),volumes.Sum()*density,obstacles.Where(b=>solids.Any(b.Overlaps)).Select(b=>b.Label).Distinct().ToArray());
    }
}

// Managed planning only. Native thickening is position - normal * t, proven in WAVE2A-NOTES.
// Rays are double-sided Moller-Trumbore tests of the host's gated, tessellated solid; no physics calls.
internal static class ComposerFill
{
    internal static string Label(ComposerMeshAction action)=>action switch{ComposerMeshAction.Flip=>"Flip",ComposerMeshAction.Inward=>"Thicken inward",ComposerMeshAction.Fill=>"Fill to wall",ComposerMeshAction.ConformCheek=>"Conform cheek fill",ComposerMeshAction.CheekShell=>"Cheek fill (shell)",ComposerMeshAction.LayerConvert=>"Convert to layers",ComposerMeshAction.BackToBox=>"Back to box",_=>"T-80U preset"};
    internal static double? Ray(Vector3 p,Vector3 direction,IReadOnlyList<FillTriangle> walls,double maximumM)
    {
        if(!Finite(p)||!Finite(direction)||direction.LengthSquared()<1e-12||!double.IsFinite(maximumM)||maximumM<=0)throw new ArgumentException("Invalid ray.");
        var d=Vector3.Normalize(direction);double nearest=double.PositiveInfinity;
        foreach(var t in walls)
        {
            var e1=t.B-t.A;var e2=t.C-t.A;var h=Vector3.Cross(d,e2);double a=Vector3.Dot(e1,h);
            if(Math.Abs(a)<1e-10)continue;
            var s=p-t.A;double u=Vector3.Dot(s,h)/a;if(u< -1e-6||u>1+1e-6)continue;
            var q=Vector3.Cross(s,e1);double v=Vector3.Dot(d,q)/a;if(v< -1e-6||u+v>1+1e-6)continue;
            double distance=Vector3.Dot(e2,q)/a;
            if(distance>1e-5&&distance<=maximumM+1e-6&&distance<nearest)nearest=distance;
        }
        return double.IsPositiveInfinity(nearest)?null:nearest;
    }
    internal static bool InwardFlip(SnapFace f,Vector3 reference,IReadOnlyList<FillTriangle> walls)
    {
        var centre=f.Corners.Aggregate(Vector3.Zero,(a,b)=>a+b)/f.Corners.Length;
        var toward=reference-centre;var extrusion=-Vector3.Normalize(f.Normal);
        if(toward.LengthSquared()<1e-12)throw new InvalidOperationException("Reference point lies on a face; choose an interior point.");
        var dot=Vector3.Dot(extrusion,Vector3.Normalize(toward));
        if(Math.Abs(dot)<.15f)
        {
            var forward=Ray(centre,extrusion,walls,1);var reverse=Ray(centre,-extrusion,walls,1);
            if(forward.HasValue!=reverse.HasValue)return reverse.HasValue;
            if(forward.HasValue&&reverse.HasValue&&Math.Abs(forward.Value-reverse.Value)>.01)return reverse.Value<forward.Value;
        }
        return dot<0; // a hatch miss changes only this face and falls back to the reference point
    }
    internal static ComposerMeshPlan Plan(string json,PlateMeshSnapshot before,bool[]? selection,ComposerMeshAction action,Vector3 reference,IReadOnlyList<FillTriangle> walls,FillSettings settings)
    {
        settings.Validate();var only=CompositeThicknessPlan.SelectedCount(selection,before.FaceCount)>0;
        var flipped=new bool[before.FaceCount];var along=new bool[before.FaceCount];var thickness=before.ThicknessList().ToArray();var missing=before.Faces.Select(f=>new bool[f.VertexCount]).ToArray();int misses=0,limited=0,tight=0,edgeCorners=0,total=0;
        for(int i=0;i<before.FaceCount;i++)
        {
            if(only&&selection![i]!=true)continue;
            var f=before.Faces[i];
            if(f.HoleLoops!=0)throw new InvalidOperationException("Select faces without hole loops for this edit; the host may have openings.");
            if(f.Corners.Length<3)throw new InvalidOperationException("Degenerate selected face.");
            // Flip only reverses the corner order; the direction tools need a real normal.
            if(action!=ComposerMeshAction.Flip&&(!Finite(f.Normal)||f.Normal.LengthSquared()<.5f))throw new InvalidOperationException($"Face {i+1} is degenerate (no normal); deselect it first.");
            flipped[i]=action==ComposerMeshAction.Flip||action==ComposerMeshAction.Inward&&InwardFlip(f,reference,walls);
            if(action==ComposerMeshAction.Flip){edgeCorners+=f.Along.Count(a=>!a);continue;}
            along[i]=true;
            if(action!=ComposerMeshAction.Fill)continue;
            var normal=Vector3.Normalize(flipped[i]?-f.Normal:f.Normal);
            for(int j=0;j<f.VertexCount;j++)
            {
                total++;
                // Search beyond the cap as well, so a far wall caps instead of becoming a hatch miss.
                var hit=Ray(f.Corners[j],-normal,walls,100);
                double mm=hit.HasValue?hit.Value*1000-settings.ClearanceMm:settings.ReserveMm;
                if(!hit.HasValue){misses++;missing[i][j]=true;}
                if(hit.HasValue&&mm>settings.MaximumMm)limited++;
                // A wall closer than clearance + 3 mm keeps the native minimum of 3 mm; it is counted, never grown through.
                if(hit.HasValue&&mm<3)tight++;
                thickness[i][j]=(ushort)Math.Clamp(Math.Round(mm,MidpointRounding.AwayFromZero),3,Math.Floor(settings.MaximumMm));
            }
        }
        var payload=Rewrite(json,flipped,action==ComposerMeshAction.Fill?thickness:null,along);
        var after=Read(payload);int count=flipped.Count(x=>x);
        var summary=action==ComposerMeshAction.Flip?$"Flipped {count} faces; corner thickness, directions and material retained"+(edgeCorners>0?$"; {edgeCorners} corners follow an edge (native Along edge) and keep their direction, use Thicken inward for those":"")+".":
            action==ComposerMeshAction.Inward?$"Inward direction set; {count} faces turned toward the inside.":
            $"{total-misses} of {total} corners found a wall; {misses} without a wall; {limited} capped at {settings.MaximumMm:0.#} mm"+(tight>0?$", {tight} kept at 3 mm (wall within clearance)":"")+$"; clearance {settings.ClearanceMm:0.#} mm. Curved walls are linear between corners.";
        return new(payload,after,count,misses,limited,summary,false,tight,edgeCorners){Action=action,Total=total,Missing=missing,MaximumMm=settings.MaximumMm};
    }
    // Preserve the full packed payload (generation, rivets, unknown fields and whitespace).
    // Per-corner data follows its corner when a face is flipped. The native save format packs it:
    // tm = one byte per corner (thicken mode), te = 16 bits per corner (edge index for Along edge);
    // JSON arrays are reversed token by token. Two flips restore the original bytes.
    internal const int PackedCorners=4;
    internal static string Rewrite(string json,bool[] flipped,ushort[][]? thickness,bool[] along)
    {
        using var doc=JsonDocument.Parse(json);var faces=doc.RootElement.GetProperty("faces");int cursor=0,index=0;
        var edits=new List<(int At,int Length,string Value)>();
        foreach(var face in faces.EnumerateArray())
        {
            var raw=face.GetRawText();int start=json.IndexOf(raw,cursor,StringComparison.Ordinal);cursor=start+raw.Length;
            int i=index++;if(!flipped[i]&&thickness==null&&!along[i])continue;
            int corners=face.GetProperty("v").GetArrayLength();
            string next=Regex.Replace(raw,"\"(v|t|tm|te)\"\\s*:\\s*(\\[[^\\[\\]]*\\]|-?[0-9]+)",m=>
            {
                string key=m.Groups[1].Value,value=m.Groups[2].Value;string changed=value;
                if(key=="t"&&thickness!=null)changed="["+string.Join(",",flipped[i]?thickness[i].Reverse():thickness[i])+ "]";
                else if(key=="tm"&&along[i])changed="0";
                else if(flipped[i]&&value.StartsWith('['))
                {
                    // Reverse the tokens in place so the original separators and whitespace survive;
                    // two flips then restore the exact payload bytes.
                    var tokens=Regex.Matches(value,@"-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?").Select(x=>x.Value).Reverse().ToArray();int n=0;
                    changed=Regex.Replace(value,@"-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?",_=>tokens[n++]);
                }
                else if(flipped[i]&&key is "tm" or "te")changed=ReversePacked(value,corners,key=="tm"?8:16,i);
                return m.Value[..m.Groups[2].IndexWithin(m)]+changed;
            });
            if(along[i]&&!face.TryGetProperty("tm",out _))next=next[..^1]+",\"tm\":0}";
            edits.Add((start,raw.Length,next));
        }
        foreach(var e in edits.OrderByDescending(x=>x.At))json=json.Remove(e.At,e.Length).Insert(e.At,e.Value);
        return json;
    }
    /// <summary>Reverses the per-corner slots of a packed tm/te number. Refuses layouts that are not proven (more than 4 corners, sign, stray bits).</summary>
    internal static string ReversePacked(string value,int corners,int bits,int face)
    {
        if(value=="0")return value;
        if(!ulong.TryParse(value,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var packed)||packed.ToString(System.Globalization.CultureInfo.InvariantCulture)!=value)
            throw new InvalidOperationException($"Face {face+1}: unknown packed corner data; flip refused, nothing changed.");
        if(corners>PackedCorners||corners*bits<64&&packed>>(corners*bits)!=0)
            throw new InvalidOperationException($"Face {face+1}: per-corner direction data on a face with {corners} corners has no proven layout; flip refused, nothing changed.");
        ulong mask=(1UL<<bits)-1,result=0;
        for(int j=0;j<corners;j++)result|=((packed>>(j*bits))&mask)<<((corners-1-j)*bits);
        return result.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    /// <summary>Along face normal per corner, decoded from tm (absent = Along; number = one byte per corner; array = one value per corner).</summary>
    internal static bool[] AlongFlags(JsonElement face,int corners,bool signedBits=false)
    {
        if(!face.TryGetProperty("tm",out var tm))return Enumerable.Repeat(true,corners).ToArray();
        if(tm.ValueKind==JsonValueKind.Array)return tm.EnumerateArray().Select(x=>x.GetInt32()==0).ToArray();
        var packed=signedBits?JsonBits.Tm(tm):tm.GetUInt64();
        if(packed!=0&&corners>PackedCorners)throw new InvalidOperationException("Per-corner direction data on a face with more than 4 corners has no proven layout.");
        return Enumerable.Range(0,corners).Select(j=>((packed>>(8*j))&0xff)==0).ToArray();
    }
    private static int IndexWithin(this Group group,Match match)=>group.Index-match.Index;
    internal static PlateMeshSnapshot Read(string json,bool signedBits=false)
    {
        using var d=JsonDocument.Parse(json);var root=d.RootElement;
        var flat=root.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();var vertices=new Vector3[flat.Length/3];
        for(int i=0;i<vertices.Length;i++)vertices[i]=new(flat[3*i],flat[3*i+1],flat[3*i+2]);
        var faces=new List<SnapFace>();
        foreach(var f in root.GetProperty("faces").EnumerateArray())
        {
            var ids=f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray();var points=ids.Select(i=>vertices[i]).ToArray();
            var t=f.GetProperty("t").EnumerateArray().Select(x=>x.GetUInt16()).ToArray();
            var modes=AlongFlags(f,ids.Length,signedBits);
            if(t.Length!=ids.Length||modes.Length!=ids.Length||points.Any(p=>!Finite(p)))throw new InvalidOperationException("Invalid mesh payload.");
            var n=Vector3.Zero;for(int j=0;j<points.Length;j++)n+=Vector3.Cross(points[j],points[(j+1)%points.Length]);
            // A degenerate polygon elsewhere on the plate must not block an edit of other faces: it reads with a zero normal,
            // and Plan refuses it only when it is selected (native normal check).
            faces.Add(new(ids,points,t,modes,n.LengthSquared()<1e-14?Vector3.Zero:Vector3.Normalize(n),false,false));
        }
        return new(0,faces.Count,faces.Sum(f=>f.VertexCount),faces.ToArray(),vertices,"managed-fill");
    }
    internal static double Volume(SnapFace f)
    {
        // Each planar triangle has linear thickness: integral(t dA) = area * mean(corner t).
        double v=0;for(int j=1;j+1<f.VertexCount;j++)
            v+=Vector3.Cross(f.Corners[j]-f.Corners[0],f.Corners[j+1]-f.Corners[0]).Length()*.5*(f.Thickness[0]+f.Thickness[j]+f.Thickness[j+1])/3000d;
        return v;
    }
    /// <summary>0.22.57: volume of one face's block with its real back points (divergence theorem over front, back and sides; positive when the back faces the same way as the front).</summary>
    internal static double PrismVolume(Vector3[] front,Vector3[] back)
    {
        int n=front.Length;if(n<3||back.Length!=n)return 0;
        var c=front.Concat(back).Aggregate(Vector3.Zero,(a,b)=>a+b)/(2*n);double v=0;
        void Tri(Vector3 a,Vector3 b,Vector3 d)=>v+=Vector3.Dot(a-c,Vector3.Cross(b-c,d-c))/6;
        for(int j=1;j+1<n;j++){Tri(front[0],front[j],front[j+1]);Tri(back[0],back[j+1],back[j]);}
        for(int j=0;j<n;j++){int k=(j+1)%n;Tri(front[j],back[k],front[k]);Tri(front[j],back[j],back[k]);}
        return v;
    }
    internal static (double Distance,Vector3 Normal)? RaySurface(Vector3 origin,IReadOnlyList<FillTriangle> walls)
    {
        (double Distance,Vector3 Normal)? best=null;
        foreach(var wall in walls)
        {
            var hit=Ray(origin,Vector3.UnitZ,new[]{wall},5);
            if(hit.HasValue&&(!best.HasValue||hit.Value<best.Value.Distance))
                best=(hit.Value,Vector3.Normalize(Vector3.Cross(wall.B-wall.A,wall.C-wall.A)));
        }
        return best;
    }
    internal static float GunHalfGap(IReadOnlyList<FillBounds> guns)=>guns.Count==0?.35f:(guns.Max(b=>b.Max.X)-guns.Min(b=>b.Min.X))*.5f+.05f;
    internal static ComposerMeshPlan Cheeks(IReadOnlyList<FillTriangle> walls,FillBounds host,FillSettings settings,CheekSettings? cheekSettings=null,float gunHalfGap=.35f,bool gunFallback=true,float gunCentre=0)
    {
        settings.Validate();var limits=cheekSettings??new();limits.Validate();
        float bottom=host.Min.Y+.03f,top=host.Max.Y-.03f,rear=(float)limits.RearBoundaryM;
        double minimumForward=Math.Cos(limits.WallAngleDegrees*Math.PI/180);
        if(top-bottom<.10f||host.Max.Z-rear<.10f)throw new InvalidOperationException("No room for cheek fillers beside the gun and ahead of the rear limit.");
        var vertices=new List<float>();var faces=new List<object>();var edges=new List<int>();
        int ny=(int)Math.Ceiling((top-bottom)/.10f);
        foreach(int side in new[]{-1,1})for(int y=0;y<ny;y++)
        {
            float edge=gunCentre+side*gunHalfGap,outer=side<0?host.Min.X:host.Max.X;
            if(side*(outer-edge)<=.02f)continue;
            int nx=(int)Math.Ceiling(Math.Abs(outer-edge)/.10f);
            float?[]? previous=null;
            for(int x=0;x<nx;x++)
            {
                var corners=new List<Vector3>();var depths=new float?[4];int corner=0;
                foreach(var (dx,dy) in new[]{(0,0),(1,0),(1,1),(0,1)})
                {
                    int index=corner++;
                    float px=edge+(outer-edge)*(x+dx)/nx,py=bottom+(top-bottom)*(y+dy)/ny;
                    var origin=new Vector3(px,py,rear);var hit=RaySurface(origin,walls);
                    if(!hit.HasValue||Math.Abs(hit.Value.Normal.Z)+1e-6<minimumForward)continue;
                    float depth=(float)hit.Value.Distance;
                    // A cast side/rear surface can face partly forwards. Stop at a sudden depth jump too.
                    if(previous?[dy] is float prior&&depth>prior+limits.RowDepthMm*.001)continue;
                    depths[index]=depth;corners.Add(origin+Vector3.UnitZ*(depth-(float)settings.ClearanceMm*.001f));
                }
                // Keep a clipped boundary cell when exactly one corner misses the front wall.
                // Stop after the wall ends, preserving the existing outward depth-jump guard.
                if(corners.Count<3){if(previous==null)continue;break;}
                previous=new[]{depths[1],depths[2]};
                foreach(var triangle in corners.Count==3?new[]{new[]{0,1,2}}:new[]{new[]{0,1,2},new[]{0,2,3}})
                {
                    var p=triangle.Select(i=>corners[i]).ToArray();var n=Vector3.Normalize(Vector3.Cross(p[1]-p[0],p[2]-p[0]));
                    if(n.Z<0){Array.Reverse(p);n=-n;}
                    if(!Finite(n)||n.Z<.2f)continue;
                    var t=new ushort[3];bool valid=true;
                    for(int j=0;j<3;j++)
                    {
                        double bound=(p[j].Z-rear)/n.Z*1000;
                        if(side*n.X>1e-6)bound=Math.Min(bound,(side*(p[j].X-gunCentre)-gunHalfGap)/(side*n.X)*1000);
                        double mm=Math.Floor(Math.Min(bound,limits.MaximumMm));
                        if(mm<3){valid=false;break;}t[j]=(ushort)mm;
                    }
                    if(!valid)continue;
                    var ids=new int[3];
                    for(int j=0;j<3;j++){ids[j]=vertices.Count/3;vertices.AddRange(new[]{p[j].X,p[j].Y,p[j].Z});}
                    for(int j=0;j<3;j++)edges.AddRange(new[]{ids[j],ids[(j+1)%3]});
                    faces.Add(new{v=ids,t,tm=0,te=0});
                }
            }
        }
        if(faces.Count==0)throw new InvalidOperationException("No cheek surface found; check the turret attachment and orientation.");
        var json=JsonSerializer.Serialize(new{majorVersion=0,minorVersion=3,vertices,edges,edgeFlags=edges.Where((_,i)=>i%2==0).Select(_=>0),faces});
        return new(json,Read(json),0,0,0,$"Adds two cheek fillers, left and right of the gun. Maximum thickness {limits.MaximumMm:0.#} mm. "+(gunFallback?"Gun size unavailable; using a 700 mm centre opening. ":"")+"Undo removes it in one step.",true){Action=ComposerMeshAction.CheekPreset,MaximumMm=limits.MaximumMm};
    }
    internal static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
    /// <summary>Names of the components the cheek preset warns about: crew, gun, ammunition and carousel.</summary>
    internal static bool IsObstacleType(string? typeOrId)
    {
        if(string.IsNullOrEmpty(typeOrId))return false;
        var id=typeOrId.ToLowerInvariant();
        return id.Contains("crew",StringComparison.Ordinal)||id.Contains("cannon",StringComparison.Ordinal)||
            id.Contains("ammunition",StringComparison.Ordinal)||id.Contains("ammorack",StringComparison.Ordinal)||
            id.Contains("ammobox",StringComparison.Ordinal)||id.Contains("carousel",StringComparison.Ordinal)||
            id.Contains("carrousel",StringComparison.Ordinal);
    }
}

// ======== 0.22.60 lagen als vlakken (pure; kept in this file so the golden source ledgers of the existing suite stay as they are) ========
/// <summary>0.22.60 "lagen als vlakken": how the solid layers of a recipe map onto faces. Per layer: one face per solid layer. Per block: one face per run of solid layers between two air layers.</summary>
internal enum LayerLayoutKind { PerLayer, PerBlock }

/// <summary>One face kind of a plate in layers. <paramref name="Layers"/> are indices into the preset layers (front first); the fractions are the share of the recipe depth (air included) from the front.</summary>
internal sealed record LayerGroup(int Rank,int[] Layers,double StartFraction,double EndFraction,double DepthMm,string Name,string ShortName);

internal sealed record LayerLayout(LayerLayoutKind Kind,LayerGroup[] Groups,double DepthMm)
{
    internal static double LayerDepth(CassetteLayer l)=>l.Material=="air"?l.ThicknessMm:l.ThicknessMm/Math.Cos(l.TiltDegrees*Math.PI/180);
    internal static string DefaultLabel(string id)=>id switch{"rha"=>"rolled homogeneous armour","castArmour"=>"cast armour","elastomerNERA"=>"NERA","highHardnessSteel"=>"high-hardness steel",
        _=>System.Text.RegularExpressions.Regex.Replace(id,"(?<=[a-z0-9])([A-Z])"," $1").ToLowerInvariant()};
    internal static LayerLayout Build(CassettePreset preset,LayerLayoutKind kind,Func<string,string>? label=null)
    {
        label??=DefaultLabel;var layers=preset.Layers;var cumulative=new double[layers.Length+1];
        for(int i=0;i<layers.Length;i++)cumulative[i+1]=cumulative[i]+LayerDepth(layers[i]);
        double depth=cumulative[^1];
        if(!(depth>0)||!double.IsFinite(depth))throw new InvalidOperationException("The plate's cassette has no depth.");
        var runs=new List<int[]>();
        for(int i=0;i<layers.Length;)
        {
            if(layers[i].Material=="air"){i++;continue;}
            int j=i;if(kind==LayerLayoutKind.PerBlock)while(j+1<layers.Length&&layers[j+1].Material!="air")j++;
            runs.Add(Enumerable.Range(i,j-i+1).ToArray());i=j+1;
        }
        if(runs.Count==0)throw new InvalidOperationException("The plate's cassette has no solid layers.");
        var groups=new LayerGroup[runs.Count];
        for(int r=0;r<runs.Count;r++)
        {
            var run=runs[r];
            var what=kind==LayerLayoutKind.PerLayer?label(layers[run[0]].Material):string.Join(" + ",run.Select(i=>label(layers[i].Material)));
            groups[r]=new(r,run,cumulative[run[0]]/depth,cumulative[run[^1]+1]/depth,cumulative[run[^1]+1]-cumulative[run[0]],
                $"{(kind==LayerLayoutKind.PerLayer?"layer":"block")} {r+1} of {runs.Count}: {what}",what);
        }
        return new(kind,groups,depth);
    }
}

/// <summary>One face of the converted plate, for the window, the tests and the pictures (plate frame, metres).</summary>
internal sealed record LayerFaceInfo(int Source,int Rank,int[] Vertices,Vector3[] Front,Vector3[] Back,ushort[] Thickness,byte[] Modes,int[] RibEdges,Vector3 Normal);
/// <summary>One face of the plate before the conversion: its corners and real back points (native formula).</summary>
internal sealed record LayerSourceInfo(int Index,bool Converted,string? Skip,Vector3[] Front,Vector3[] Back,Vector3 Normal,bool DirectionChanged,bool Ribbed,Vector3[] LineDirection,double[] LineLengthMm);
internal sealed record LayerConversion(ComposerMeshPlan Plan,LayerLayout Layout,ThicknessMode Mode,LayerSourceInfo[] Sources,LayerFaceInfo[] Faces,int ConvertedFaces,int SkippedFaces,
    int DirectionChangedFaces,int RibbedFaces,double ThinnestMm,double VolumeBeforeM3,double VolumeAfterM3,double MassBeforeKg,double MassAfterKg,double? TrueMassKg,string Inspection)
{
    /// <summary>0.22.61: the result of a box conversion (null for a loose plate or a filling, which keep the 0.22.60 planner).</summary>
    internal LayerBoxInfo? Box {get;init;}
    /// <summary>0.22.68: what the clean-up of the output removed (points and edges before and after, helper points left).</summary>
    internal LayerCleanup? Cleanup {get;init;}
    /// <summary>0.22.64: why side walls could not be kept (plain words, for the window and the log); null when none was left out.</summary>
    internal string? DropReason {get;init;}
}

/// <summary>
/// 0.22.60 "Convert to layers" (managed planning only). Every face with at most four corners is split through its thickness into one face per
/// solid layer (or per block of touching layers), scaled to the face's own depth at every corner; air layers become real gaps. The front stays
/// where it is. A corner on Along edge with a chosen rib (tm 4) keeps following that rib: every layer gets its own piece of rib (a loose edge
/// to the next boundary point on the same line) and its own whole-millimetre thickness from the native formula (out-0.22.57/ALONG-EDGE-FORMULA.md),
/// so the faces of all original faces around one rib share their boundary points. Every other corner stacks along the face normal (tm 0); where a
/// deeper layer of a wedge no longer lies parallel to the original face, its corner follows a rib along the original face normal instead, so the
/// layers stay inside the original shape. Faces with more than four corners stay as they are.
/// </summary>
internal static partial class LayerFaces
{
    internal const string Title="Convert to layers - inspect before applying";
    internal const string Tooltip="Splits each face into one face per armour layer. Air layers become real gaps.";
    internal const int MinimumMm=3;
    /// <summary>A normal-stacked corner whose layer face is tilted so much that the native back point would leave the corner's line by more than this (mm) gets a rib instead.</summary>
    internal const double ParallelDriftMm=.25;
    private static readonly double SinOneDegree=Math.Sin(-Math.PI/180);
    private sealed record SourceFace(int[] V,ushort[] T,byte[] Tm,int[] Te,string Raw,bool Packed);
    private sealed record Line(Vector3 Origin,Vector3 U,double LengthMm,bool Rib,int OriginalEdge,string Key,bool Changed);

    internal static LayerConversion Convert(string json,CassetteModel recipe,Func<string,string>? label=null,Func<string,double>? density=null,Vector3? forward=null,double wallMm=5,Vector3? centre=null)
    {
        var parsed=Parse(json);
        // 0.22.61: a kneaded box (front faces with side or back faces around them) is layered from front to back; everything else as 0.22.60.
        if(forward is {} axis)
        {
            var oriented=OrientClosed(parsed);
            if(BoxGate(oriented,axis,centre) is {} box)return ConvertBox(json,oriented,recipe,box,label,density,wallMm);
        }
        LayerConversion? result=null;
        foreach(var kind in new[]{LayerLayoutKind.PerLayer,LayerLayoutKind.PerBlock})
        {
            var layout=LayerLayout.Build(recipe.Preset,kind,label);
            var attempt=Attempt(json,parsed,recipe,layout,density,out var thinnest,out var problem);
            if(attempt!=null){result=attempt;break;}
            if(problem!=null)throw new InvalidOperationException(problem);
            if(kind==LayerLayoutKind.PerBlock)throw new InvalidOperationException($"This plate is too thin for layers: even a block of touching layers would be thinner than 3 mm (thinnest {thinnest:0.#} mm). Make the plate thicker, or use a cassette with thicker layers.");
        }
        return result!;
    }

    private static (float[] Vertices,int[] Edges,int[] Flags,bool HasFlags,SourceFace[] Faces) Parse(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        var vertices=root.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        var edges=root.TryGetProperty("edges",out var e)?e.EnumerateArray().Select(x=>x.GetInt32()).ToArray():Array.Empty<int>();
        bool hasFlags=root.TryGetProperty("edgeFlags",out var fl);
        var flags=hasFlags?fl.EnumerateArray().Select(x=>x.GetInt32()).ToArray():new int[edges.Length/2];
        if(flags.Length!=edges.Length/2)flags=flags.Concat(new int[Math.Max(0,edges.Length/2-flags.Length)]).Take(edges.Length/2).ToArray();
        var faces=new List<SourceFace>();
        foreach(var f in root.GetProperty("faces").EnumerateArray())
        {
            var v=f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            var t=f.GetProperty("t").EnumerateArray().Select(x=>x.GetUInt16()).ToArray();
            bool packed=true;var tm=new byte[v.Length];var te=new int[v.Length];
            foreach(var p in f.EnumerateObject())if(p.Name is not ("v" or "t" or "tm" or "te"))packed=false;
            if(f.TryGetProperty("tm",out var tmv))
            {
                if(tmv.ValueKind==JsonValueKind.Array){int j=0;foreach(var x in tmv.EnumerateArray()){if(j<tm.Length)tm[j]=(byte)x.GetInt32();j++;}}
                else{var bits=JsonBits.Tm(tmv);if(v.Length>ComposerFill.PackedCorners){if(bits!=0)packed=false;}else for(int j=0;j<v.Length;j++)tm[j]=(byte)(bits>>(8*j));}
            }
            if(f.TryGetProperty("te",out var tev))
            {
                if(tev.ValueKind==JsonValueKind.Array){int j=0;foreach(var x in tev.EnumerateArray()){if(j<te.Length)te[j]=x.GetInt32();j++;}}
                else{var bits=JsonBits.Te(tev);if(v.Length>ComposerFill.PackedCorners){if(bits!=0)packed=false;}else for(int j=0;j<v.Length;j++)te[j]=(int)((bits>>(16*j))&0xffff);}
            }
            faces.Add(new(v,t,tm,te,f.GetRawText(),packed));
        }
        return (vertices,edges,flags,hasFlags,faces.ToArray());
    }

    private static LayerConversion? Attempt(string json,(float[] Vertices,int[] Edges,int[] Flags,bool HasFlags,SourceFace[] Faces) mesh,CassetteModel recipe,LayerLayout layout,Func<string,double>? density,out double thinnest,out string? problem)
    {
        thinnest=double.PositiveInfinity;problem=null;
        var p0=mesh.Vertices;int v0=p0.Length/3;Vector3 P(int i)=>new(p0[3*i],p0[3*i+1],p0[3*i+2]);
        var groups=layout.Groups;int G=groups.Length;
        // Boundary fractions: the front of every group and the end of every group (touching groups share one boundary).
        var fractions=new List<double>{0};int Boundary(double f){for(int i=0;i<fractions.Count;i++)if(Math.Abs(fractions[i]-f)<1e-12)return i;fractions.Add(f);return fractions.Count-1;}
        var front=new int[G];var end=new int[G];
        for(int g=0;g<G;g++){front[g]=Boundary(groups[g].StartFraction);end[g]=Boundary(groups[g].EndFraction);}
        var sources=new List<LayerSourceInfo>();var lines=new List<Line[]?>();
        int skipped=0,changed=0;
        // 1. Corner lines of every face that can be converted.
        for(int i=0;i<mesh.Faces.Length;i++)
        {
            var f=mesh.Faces[i];var corners=f.V.Select(P).ToArray();
            var n=CheekShellFill.Newell(corners);
            string? skip=f.V.Length>4?"more than 4 corners":f.V.Length<3?"degenerate":!f.Packed?"unknown face data":n.LengthSquared()<1e-14f||!ComposerFill.Finite(n)?"degenerate":null;
            if(skip==null&&f.V.Distinct().Count()!=f.V.Length)skip="degenerate";
            if(skip!=null)
            {
                skipped++;var nn=n.LengthSquared()>1e-14f?Vector3.Normalize(n):Vector3.Zero;
                sources.Add(new(i,false,skip,corners,corners.Select((c,j)=>c-nn*(j<f.T.Length?f.T[j]:0)*.001f).ToArray(),nn,false,false,Array.Empty<Vector3>(),Array.Empty<double>()));lines.Add(null);continue;
            }
            n=Vector3.Normalize(n);var faceLines=new Line[f.V.Length];bool faceChanged=false;
            var back=new Vector3[f.V.Length];
            for(int j=0;j<f.V.Length;j++)
            {
                var v=corners[j];var next=corners[(j+1)%corners.Length];var prev=corners[(j+corners.Length-1)%corners.Length];
                bool rib=false;Vector3 u=-n;double length=f.T[j];int edge=-1;string key=$"n:{i}:{j}";
                if(f.Tm[j]==CheekShellFill.AlongEdgeManual)
                {
                    int e=f.Te[j];
                    if(e>=0&&2*e+1<mesh.Edges.Length&&(mesh.Edges[2*e]==f.V[j]||mesh.Edges[2*e+1]==f.V[j]))
                    {
                        int w=mesh.Edges[2*e]==f.V[j]?mesh.Edges[2*e+1]:mesh.Edges[2*e];var dir=P(w)-v;
                        if(w>=0&&w<v0&&dir.LengthSquared()>1e-14f)
                        {
                            var cand=Vector3.Normalize(dir);double d=Vector3.Dot(n,cand);
                            // The native rule: a rib at least 1 degree behind the face; otherwise the game itself uses the face normal.
                            if(d<SinOneDegree){rib=true;u=cand;length=f.T[j]/-d;edge=e;key=$"r:{f.V[j]}:{e}";}
                        }
                    }
                    back[j]=rib?CheekShellFill.NativeBack(v,next,prev,u,f.T[j],n):v-n*(f.T[j]*.001f);
                }
                else
                {
                    // Auto and the other native modes choose their own direction; the layers stack along the face normal.
                    if(f.Tm[j]!=0)faceChanged=true;
                    back[j]=v-n*(f.T[j]*.001f);
                }
                faceLines[j]=new(v,u,length,rib,edge,key,f.Tm[j]!=0&&!rib&&f.Tm[j]!=CheekShellFill.AlongEdgeManual);
            }
            if(faceChanged)changed++;
            lines.Add(faceLines);
            sources.Add(new(i,true,null,corners,back,n,faceChanged,false,faceLines.Select(l=>l.U).ToArray(),faceLines.Select(l=>l.LengthMm).ToArray()));
        }
        // 2. Corners that follow the same rib from the same vertex share their boundary points when their own lengths along the rib agree within
        // 1 mm (a shell built by Cheek fill (shell)); they then use the mean length. A face whose thickness was changed by hand keeps its own points.
        var byRib=new Dictionary<string,List<(int Face,int Corner,double Length)>>(StringComparer.Ordinal);
        for(int i=0;i<lines.Count;i++)if(lines[i] is {} fl)for(int j=0;j<fl.Length;j++)if(fl[j].Rib){if(!byRib.TryGetValue(fl[j].Key,out var list))byRib[fl[j].Key]=list=new();list.Add((i,j,fl[j].LengthMm));}
        var shared=new Dictionary<string,double>(StringComparer.Ordinal);
        foreach(var (key,list) in byRib)
        {
            var sorted=list.OrderBy(x=>x.Length).ToList();int cluster=0;
            for(int s=0;s<sorted.Count;)
            {
                int e=s;while(e+1<sorted.Count&&sorted[e+1].Length-sorted[s].Length<=1)e++;
                var mean=sorted.Skip(s).Take(e-s+1).Average(x=>x.Length);var name=$"{key}:{cluster++}";shared[name]=mean;
                for(int k=s;k<=e;k++){var (face,corner,_)=sorted[k];lines[face]![corner]=lines[face]![corner] with{Key=name};}
                s=e+1;
            }
        }
        double Length(Line l)=>l.Rib?shared[l.Key]:l.LengthMm;
        // 3. Points, edges and faces.
        var newVertices=new List<Vector3>();var points=new Dictionary<(string,int),int>();
        int Point(int originalVertex,Line l,int boundary)
        {
            if(fractions[boundary]==0)return originalVertex;
            if(points.TryGetValue((l.Key,boundary),out var id))return id;
            var q=l.Origin+l.U*(float)(fractions[boundary]*Length(l)*.001);
            if(!ComposerFill.Finite(q))throw new InvalidOperationException("Convert to layers: a layer point is not finite.");
            id=v0+newVertices.Count;newVertices.Add(q);points[(l.Key,boundary)]=id;return id;
        }
        Vector3 At(int id)=>id<v0?P(id):newVertices[id-v0];
        var edgeIndex=new Dictionary<(int,int),int>();var origEdges=mesh.Edges.Length/2;
        for(int e=0;e<origEdges;e++)edgeIndex.TryAdd((Math.Min(mesh.Edges[2*e],mesh.Edges[2*e+1]),Math.Max(mesh.Edges[2*e],mesh.Edges[2*e+1])),e);
        var newEdges=new List<int>();var newFlags=new List<int>();
        int Edge(int a,int b,int flag)
        {
            var k=(Math.Min(a,b),Math.Max(a,b));if(edgeIndex.TryGetValue(k,out var e))return e;
            e=origEdges+newEdges.Count/2;newEdges.Add(a);newEdges.Add(b);newFlags.Add(flag);edgeIndex[k]=e;return e;
        }
        var faceTexts=new List<string>();var faces=new List<LayerFaceInfo>();var backCorners=new List<Vector3[]>();int ribbed=0;double volumeBefore=0,volumeAfter=0,trueMass=0;
        double GroupDensity(LayerGroup g)
        {
            if(density==null)return 0;double mass=0;
            foreach(var i in g.Layers){var l=recipe.Preset.Layers[i];mass+=density(l.Material)*LayerLayout.LayerDepth(l);}
            return mass/g.DepthMm;
        }
        for(int i=0;i<mesh.Faces.Length;i++)
        {
            var f=mesh.Faces[i];var src=sources[i];var fl=lines[i];
            volumeBefore+=ComposerFill.PrismVolume(src.Front,src.Back);
            if(fl==null)
            {
                faceTexts.Add(f.Raw);backCorners.Add(src.Back);volumeAfter+=ComposerFill.PrismVolume(src.Front,src.Back);
                faces.Add(new(i,-1,f.V,src.Front,src.Back,f.T,f.Tm,f.Te,src.Normal));continue;
            }
            int n=f.V.Length;bool faceRibbed=false;
            for(int g=0;g<G;g++)
            {
                var ids=new int[n];for(int j=0;j<n;j++)ids[j]=Point(f.V[j],fl[j],front[g]);
                var corners=ids.Select(At).ToArray();var nn=CheekShellFill.Newell(corners);
                if(nn.LengthSquared()<1e-14f){problem=$"Convert to layers: face {i+1} would get a degenerate layer; nothing changed.";return null;}
                var ng=Vector3.Normalize(nn);
                if(Vector3.Dot(ng,src.Normal)<=0){problem=$"Convert to layers: a layer of face {i+1} would face the wrong way; nothing changed.";return null;}
                var t=new ushort[n];var modes=new byte[n];var ribs=new int[n];var back=new Vector3[n];
                for(int j=0;j<n;j++)
                {
                    var l=fl[j];var p=corners[j];var next=corners[(j+1)%n];var prev=corners[(j+n-1)%n];
                    double len=(groups[g].EndFraction-groups[g].StartFraction)*Length(l);
                    var target=l.Origin+l.U*(float)(fractions[end[g]]*Length(l)*.001);
                    bool useRib=l.Rib;
                    if(!useRib)
                    {
                        // Normal stacking is exact while the layer face is parallel to the original face; a tilted layer of a wedge gets a rib along the original normal.
                        double depth=Vector3.Dot(p-target,ng)*1000;
                        double drift=Math.Max(0,depth)*Vector3.Cross(ng,-l.U).Length();
                        if(drift<=ParallelDriftMm)
                        {
                            int tt=(int)Math.Round(depth,MidpointRounding.AwayFromZero);thinnest=Math.Min(thinnest,tt);
                            if(tt<MinimumMm)return null;
                            t[j]=(ushort)tt;modes[j]=0;ribs[j]=0;back[j]=p-ng*(tt*.001f);continue;
                        }
                        useRib=true;faceRibbed=true;
                    }
                    double d=Vector3.Dot(ng,l.U);
                    if(d>SinOneDegree){problem=$"Convert to layers: a layer of face {i+1} would lie along its rib (corner {j+1}); nothing changed.";return null;}
                    double offsetDepth=Vector3.Dot(CheekShellFill.Offset(p,next,prev),ng)*1000;
                    int value=(int)Math.Round(len*-d+offsetDepth,MidpointRounding.AwayFromZero);
                    // The last layer never ends behind the original back: on a shallow rib one whole millimetre is several millimetres along the rib.
                    if(g==G-1)while(value>MinimumMm&&Vector3.Dot(CheekShellFill.NativeBack(p,next,prev,l.U,(ushort)value,ng)-target,l.U)>.0001f)value--;
                    thinnest=Math.Min(thinnest,value);
                    if(value<MinimumMm)return null;
                    if(value>short.MaxValue){problem="Convert to layers: a layer would be thicker than the game allows; nothing changed.";return null;}
                    t[j]=(ushort)value;modes[j]=CheekShellFill.AlongEdgeManual;
                    // The rib of this corner: the original rib for the first layer, otherwise a loose edge to the next point on the same line (only its direction counts).
                    if(l.Rib&&fractions[front[g]]==0)ribs[j]=l.OriginalEdge;
                    else{int to=g+1<G?Point(f.V[j],l,front[g+1]):Point(f.V[j],l,end[G-1]);ribs[j]=Edge(ids[j],to,0);}
                    back[j]=CheekShellFill.NativeBack(p,next,prev,l.U,(ushort)value,ng);
                }
                var nb=CheekShellFill.Newell(back);
                if(nb.LengthSquared()<1e-16f||Vector3.Dot(nb,ng)<=0){problem=$"Convert to layers: the back of a layer of face {i+1} would turn over; nothing changed.";return null;}
                // Boundary edges of the layer face (the first layer reuses the original ones).
                for(int j=0;j<n;j++)
                {
                    int a=ids[j],b=ids[(j+1)%n];
                    var k=(Math.Min(f.V[j],f.V[(j+1)%n]),Math.Max(f.V[j],f.V[(j+1)%n]));int flag=edgeIndex.TryGetValue(k,out var oe)&&oe<origEdges?mesh.Flags[oe]:0;
                    Edge(a,b,flag);
                }
                ulong tm=0,te=0;for(int j=0;j<n;j++){tm|=(ulong)modes[j]<<(8*j);te|=(ulong)(uint)ribs[j]<<(16*j);}
                faceTexts.Add("{\"v\":["+string.Join(",",ids)+"],\"t\":["+string.Join(",",t)+"],\"tm\":"+tm.ToString(CultureInfo.InvariantCulture)+",\"te\":"+te.ToString(CultureInfo.InvariantCulture)+"}");
                faces.Add(new(i,g,ids,corners,back,t,modes,ribs,ng));backCorners.Add(back);
                var v=ComposerFill.PrismVolume(corners,back);volumeAfter+=v;trueMass+=v*GroupDensity(groups[g]);
            }
            if(faceRibbed){ribbed++;sources[i]=src with{Ribbed=true};}
        }
        if(origEdges+newEdges.Count/2>=65535){problem="Convert to layers: the plate would get too many edges for the game's corner data; nothing changed.";return null;}
        // 4. The payload: the original JSON with the vertex, edge and face arrays extended; every other property is kept as it was.
        var payload=Assemble(json,newVertices,newEdges,newFlags,mesh.HasFlags,faceTexts);
        var after=ComposerFill.Read(payload,true);
        if(after.FaceCount!=faces.Count)throw new InvalidOperationException("Convert to layers: payload face count mismatch.");
        int converted=sources.Count(s=>s.Converted);
        var mode=layout.Kind==LayerLayoutKind.PerLayer?ThicknessMode.Layers:ThicknessMode.LayerBlocks;
        double rho=recipe.Passive.Density;
        var lines2=new List<string>
        {
            $"{converted} faces become {converted*G} faces: {G} layers each.",
            layout.Kind==LayerLayoutKind.PerLayer?"One face per layer.":"Touching layers share one face, because some layers would be thinner than 3 mm.",
            $"Thinnest layer: {thinnest.ToString("0",CultureInfo.InvariantCulture)} mm."
        };
        if(changed>0)lines2.Add($"{changed} faces now thicken straight back; their rear may shift slightly.");
        if(skipped>0)lines2.Add($"{skipped} faces have more than 4 corners and stay as they are.");
        lines2.Add($"Weight {(volumeBefore*rho).ToString("0.#",CultureInfo.InvariantCulture)} kg now, {(volumeAfter*rho).ToString("0.#",CultureInfo.InvariantCulture)} kg as layers.");
        var inspection=string.Join("\n",lines2);
        var summary=$"{converted} faces became {converted*G} faces ({(layout.Kind==LayerLayoutKind.PerLayer?"one face per layer":"touching layers share one face")}). Undo removes this change in one step.";
        var plan=new ComposerMeshPlan(payload,after,0,0,0,summary,true){Action=ComposerMeshAction.LayerConvert,Total=after.Faces.Sum(x=>x.VertexCount),MaximumMm=short.MaxValue,BackCorners=backCorners.ToArray(),CopyCheck=copy=>VerifyCopy(payload,copy)};
        return new(plan,layout,mode,sources.ToArray(),faces.ToArray(),converted,skipped,changed,ribbed,thinnest,volumeBefore,volumeAfter,volumeBefore*rho,volumeAfter*rho,density==null?null:trueMass+faces.Where(x=>x.Rank<0).Sum(x=>ComposerFill.PrismVolume(x.Front,x.Back))*rho,inspection);
    }

    /// <summary>The copy of the game must keep every planned point, edge and face with its tm/te token text (the same check as Cheek fill (shell)).</summary>
    internal static string? VerifyCopy(string planned,string copy)
    {
        var problem=CheekShellFill.VerifyCopy(planned,copy);
        return problem==null?null:"The game did not keep the layer faces"+problem[CheekShellFill.LinkedBackMarker.Length..]+"; nothing changed.";
    }

    private static string Assemble(string json,List<Vector3> vertices,List<int> edges,List<int> flags,bool hasFlags,List<string> faces)
    {
        using var doc=JsonDocument.Parse(json);var parts=new List<string>();
        static string Extend(string raw,IEnumerable<string> items)
        {
            var list=items.ToList();if(list.Count==0)return raw;
            var body=raw.Trim();body=body[1..^1].Trim();
            return "["+(body.Length==0?"":body+",")+string.Join(",",list)+"]";
        }
        static string F(float x)=>x.ToString("R",CultureInfo.InvariantCulture);
        bool wroteFlags=false;
        foreach(var p in doc.RootElement.EnumerateObject())
        {
            string value=p.Name switch
            {
                "vertices"=>Extend(p.Value.GetRawText(),vertices.SelectMany(v=>new[]{F(v.X),F(v.Y),F(v.Z)})),
                "edges"=>Extend(p.Value.GetRawText(),edges.Select(e=>e.ToString(CultureInfo.InvariantCulture))),
                "edgeFlags"=>Extend(p.Value.GetRawText(),flags.Select(e=>e.ToString(CultureInfo.InvariantCulture))),
                "faces"=>"["+string.Join(",",faces)+"]",
                _=>p.Value.GetRawText()
            };
            if(p.Name=="edgeFlags")wroteFlags=true;
            parts.Add(JsonSerializer.Serialize(p.Name)+":"+value);
        }
        if(!hasFlags&&!wroteFlags&&flags.Count>0)throw new InvalidOperationException("Convert to layers: the plate mesh has no edge flags.");
        return "{"+string.Join(",",parts)+"}";
    }
}

/// <summary>
/// 0.22.60 rule "which face is which layer": count from the outside. For every face (at most four corners), the number of other faces of the same
/// plate that lie in front of it, seen outwards along the face's own thickening direction: its normal where its corners grow along the normal, and
/// back along its ribs where they follow a chosen edge (tm 4). Five lines of sight: from the centroid along the face direction, and from near each
/// corner along that corner's own rib (or the normal); the median count is the rank, so one line that leaves a strongly fanned stack through a side,
/// or crosses a neighbour that was enlarged by hand, does not decide. Crossings closer than 1 mm to each other count once (a line through the shared
/// edge of two neighbouring faces of the same layer); crossings within 1.5 mm of the start do not count. Faces with more than four corners are not
/// ranked (-1) and are not counted.
/// </summary>
internal static class LayerRanks
{
    internal const double StartMarginM=.0015,ClusterM=.0025,MaximumDistanceM=20;
    /// <summary>Corner sample points lie this fraction of the way from the corner to the centroid.</summary>
    internal const float CornerInset=.15f;
    internal static int[] FromJson(string json){var mesh=LayerMesh.Read(json);return Compute(mesh.Polygons,mesh.Directions,mesh.SideWalls);}
    /// <summary>Every face's corners and the outward line of sight of every corner (empty for a face that is not ranked), from the saved mesh JSON (native face order).</summary>
    internal static (Vector3[][] Polygons,Vector3[][] Directions) Read(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        var v=root.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();Vector3 P(int i)=>new(v[3*i],v[3*i+1],v[3*i+2]);
        var edges=root.TryGetProperty("edges",out var e)?e.EnumerateArray().Select(x=>x.GetInt32()).ToArray():Array.Empty<int>();
        var polygons=new List<Vector3[]>();var directions=new List<Vector3[]>();
        foreach(var f in root.GetProperty("faces").EnumerateArray())
        {
            var ids=f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            if(ids.Any(i=>i<0||3*i+2>=v.Length)){polygons.Add(Array.Empty<Vector3>());directions.Add(Array.Empty<Vector3>());continue;}
            var p=ids.Select(P).ToArray();polygons.Add(p);
            var n=CheekShellFill.Newell(p);if(n.LengthSquared()<1e-14f||ids.Length>ComposerFill.PackedCorners){directions.Add(Array.Empty<Vector3>());continue;}
            n=Vector3.Normalize(n);ulong tm=0,te=0;
            if(f.TryGetProperty("tm",out var tmv)&&tmv.ValueKind==JsonValueKind.Number)tm=JsonBits.Tm(tmv);
            if(f.TryGetProperty("te",out var tev)&&tev.ValueKind==JsonValueKind.Number)te=JsonBits.Te(tev);
            var corner=new Vector3[ids.Length];
            for(int j=0;j<ids.Length;j++)
            {
                var u=-n;
                if(((tm>>(8*j))&0xff)==CheekShellFill.AlongEdgeManual)
                {
                    int edge=(int)((te>>(16*j))&0xffff);
                    if(2*edge+1<edges.Length&&(edges[2*edge]==ids[j]||edges[2*edge+1]==ids[j]))
                    {
                        int w=edges[2*edge]==ids[j]?edges[2*edge+1]:edges[2*edge];
                        if(w>=0&&3*w+2<v.Length&&(P(w)-p[j]).LengthSquared()>1e-14f){var cand=Vector3.Normalize(P(w)-p[j]);if(Vector3.Dot(cand,n)<-0.0174524f)u=cand;}
                    }
                }
                corner[j]=-u;
            }
            directions.Add(corner);
        }
        return (polygons.ToArray(),directions.ToArray());
    }
    /// <param name="sideWalls">0.22.61: faces recognised as side walls are not ranked (-2) and are not counted in front of other faces.</param>
    internal static int[] Compute(IReadOnlyList<Vector3[]> polygons,IReadOnlyList<Vector3[]>? directions=null,IReadOnlyList<bool>? sideWalls=null)
    {
        int n=polygons.Count;var usable=new bool[n];var centre=new Vector3[n];var sphere=new (Vector3 C,float R)[n];var sight=new Vector3[n][];var mean=new Vector3[n];
        for(int i=0;i<n;i++)
        {
            var p=polygons[i];if(p.Length<3||p.Length>4||p.Any(x=>!ComposerFill.Finite(x)))continue;
            var nn=CheekShellFill.Newell(p);if(nn.LengthSquared()<1e-14f)continue;
            if(sideWalls!=null&&i<sideWalls.Count&&sideWalls[i])continue;
            usable[i]=true;var c=Vector3.Zero;foreach(var q in p)c+=q;c/=p.Length;centre[i]=c;
            float r=0;foreach(var q in p)r=MathF.Max(r,Vector3.Distance(q,c));sphere[i]=(c,r+1e-4f);
            var normal=Vector3.Normalize(nn);var own=directions!=null&&i<directions.Count&&directions[i].Length==p.Length?directions[i]:null;
            sight[i]=p.Select((_,j)=>own!=null&&own[j].LengthSquared()>.5f?own[j]:normal).ToArray();
            var sum=sight[i].Aggregate(Vector3.Zero,(a,b)=>a+b);mean[i]=sum.LengthSquared()>1e-12f?Vector3.Normalize(sum):normal;
        }
        var ranks=new int[n];var hits=new List<double>();var counts=new List<int>(5);
        int Count(int i,Vector3 o,Vector3 d)
        {
            hits.Clear();
            for(int j=0;j<n;j++)
            {
                if(j==i||!usable[j])continue;
                var oc=sphere[j].C-o;float along=Vector3.Dot(oc,d);if(along< -sphere[j].R)continue;
                float side=oc.LengthSquared()-along*along;if(side>sphere[j].R*sphere[j].R)continue;
                var p=polygons[j];
                for(int k=1;k+1<p.Length;k++)
                    if(Ray(o,d,p[0],p[k],p[k+1]) is {} t&&t>StartMarginM&&t<MaximumDistanceM){hits.Add(t);break;}
            }
            hits.Sort();int count=0;double last=double.NegativeInfinity;
            foreach(var t in hits){if(t-last>ClusterM)count++;last=t;}
            return count;
        }
        for(int i=0;i<n;i++)
        {
            if(!usable[i]){ranks[i]=sideWalls!=null&&i<sideWalls.Count&&sideWalls[i]?-2:-1;continue;}
            counts.Clear();counts.Add(Count(i,centre[i],mean[i]));
            var p=polygons[i];for(int j=0;j<p.Length;j++)counts.Add(Count(i,p[j]+(centre[i]-p[j])*CornerInset,sight[i][j]));
            counts.Sort();ranks[i]=counts[^2];
        }
        return ranks;
    }
    private static double? Ray(Vector3 o,Vector3 d,Vector3 a,Vector3 b,Vector3 c)
    {
        double ex=b.X-a.X,ey=b.Y-a.Y,ez=b.Z-a.Z,fx=c.X-a.X,fy=c.Y-a.Y,fz=c.Z-a.Z;
        double hx=d.Y*fz-d.Z*fy,hy=d.Z*fx-d.X*fz,hz=d.X*fy-d.Y*fx;double det=ex*hx+ey*hy+ez*hz;
        if(Math.Abs(det)<1e-14)return null;double inv=1/det;
        double sx=o.X-a.X,sy=o.Y-a.Y,sz=o.Z-a.Z;double u=(sx*hx+sy*hy+sz*hz)*inv;if(u< -1e-7||u>1+1e-7)return null;
        double qx=sy*ez-sz*ey,qy=sz*ex-sx*ez,qz=sx*ey-sy*ex;double v=(d.X*qx+d.Y*qy+d.Z*qz)*inv;if(v< -1e-7||u+v>1+1e-7)return null;
        return (fx*qx+fy*qy+fz*qz)*inv;
    }
    /// <summary>"Layers: N faces assigned, M not assigned." for the composer.</summary>
    internal static string Line(int[] ranks,int groups)
    {
        int assigned=ranks.Count(r=>r>=0&&r<groups),walls=ranks.Count(r=>r==-2);
        return $"Layers: {assigned} faces assigned, {ranks.Length-assigned-walls} not assigned"+(walls>0?$", {walls} side walls.":".");
    }
}

/// <summary>
/// The per-mesh table of a plate in layers, built once per mesh (pointer and content): the native vertex offset of every face in a front/back marker
/// range (the authored face widths, culled faces 0) and the rank of every face. A hit resolves its face by one binary search, without allocations.
/// </summary>
internal sealed class LayerHitIndex
{
    internal readonly int[] Prefix;internal readonly int Total;internal readonly int[] Ranks;internal readonly LayerLayoutKind Kind;internal readonly int Groups;internal readonly string Signature;
    internal int Assigned{get;}
    internal LayerHitIndex(int[] widths,int[] ranks,LayerLayoutKind kind,int groups,string signature)
    {
        Prefix=new int[widths.Length+1];for(int i=0;i<widths.Length;i++)Prefix[i+1]=Prefix[i]+Math.Max(0,widths[i]);
        Total=Prefix[^1];Ranks=ranks;Kind=kind;Groups=groups;Signature=signature;
        for(int i=0;i<ranks.Length;i++)if(ranks[i]>=groups)ranks[i]=-1;
        Assigned=ranks.Count(r=>r>=0);SideWalls=ranks.Count(r=>r==-2);
    }
    internal int SideWalls{get;}
    /// <summary>0.22.61: the faces as solids (front corners and real back points; null without the saved JSON), for a piece whose entry triangle names no face.</summary>
    internal LayerMesh? Solids{get;private set;}
    /// <summary>0.22.61: the face whose solid holds a point of the plate frame (a layer face before a side wall); -1 when none does.</summary>
    internal int FaceContaining(Vector3 point)=>Solids?.FaceContaining(point,Ranks)??-1;
    /// <summary>The table from the native snapshot (face widths) and the saved mesh JSON (ranks along each face's own line of sight); without JSON, or when its face count differs, the face normals of the snapshot.</summary>
    internal static LayerHitIndex Build(PlateMeshSnapshot snapshot,string? json,LayerLayoutKind kind,int groups)
    {
        var index=new LayerHitIndex(snapshot.Faces.Select(f=>f.Culled?0:f.VertexCount+f.HoleLoops).ToArray(),RanksOf(snapshot,json),kind,groups,snapshot.Signature);
        if(json!=null&&LayerMesh.Read(json) is {} mesh&&mesh.Polygons.Length==snapshot.Faces.Length)index.Solids=mesh;
        return index;
    }
    internal static int[] RanksOf(PlateMeshSnapshot snapshot,string? json)
    {
        if(json!=null)
        {
            var mesh=LayerMesh.Read(json);
            if(mesh.Polygons.Length==snapshot.Faces.Length)
                return LayerRanks.Compute(snapshot.Faces.Select((f,i)=>f.HoleLoops==0?f.Corners:Array.Empty<Vector3>()).ToArray(),mesh.Directions,mesh.SideWalls);
        }
        return LayerRanks.Compute(snapshot.Faces.Select(f=>f.HoleLoops==0?f.Corners:Array.Empty<Vector3>()).ToArray());
    }
    /// <summary>The face whose vertex block holds <paramref name="local"/> (vertex offset inside the marker range); -1 outside.</summary>
    internal int FaceAt(int local)
    {
        if(local<0||local>=Total)return -1;
        int lo=0,hi=Prefix.Length-1;
        while(hi-lo>1){int mid=(lo+hi)>>1;if(Prefix[mid]<=local)lo=mid;else hi=mid;}
        return lo;
    }
    /// <summary>The face of one native triangle (its three vertex ids) inside a marker range of size <paramref name="rangeSize"/> starting at <paramref name="rangeStart"/>; -1 when the range is not this plate's face layout or the vertices lie in different faces.</summary>
    internal int Face(int rangeStart,int rangeSize,int a,int b,int c)
    {
        if(rangeSize!=Total)return -1;
        int fa=FaceAt(a-rangeStart);if(fa<0||FaceAt(b-rangeStart)!=fa||FaceAt(c-rangeStart)!=fa)return -1;
        return fa;
    }
    internal int RankOf(int face)=>face>=0&&face<Ranks.Length?Ranks[face]:-1;
}

/// <summary>One confirmed piece of a plate in layers along the shot line (metres on the persistent ray), with the face it entered (-1: unknown).</summary>
internal readonly record struct LayerChord(double StartM,double EndM,double Angle,int Rank,bool Back);

/// <summary>A recipe prepared for the hit per layer: per face kind, the recipe layers it stands for in both traversal directions, their share of its depth, and its passive RHA.</summary>
internal sealed class LayeredRecipe
{
    internal CassetteModel Model{get;}internal LayerLayout Layout{get;}
    internal readonly int[][] Forward,Reverse;internal readonly double[] GroupRha;internal readonly bool Tilted;internal readonly double HeshRetention;
    internal readonly string[] RecipeIds;
    private LayeredRecipe(CassetteModel model,LayerLayout layout,int[][] forward,int[][] reverse,double[] rha,bool tilted,double hesh,string[] recipes)
    {Model=model;Layout=layout;Forward=forward;Reverse=reverse;GroupRha=rha;Tilted=tilted;HeshRetention=hesh;RecipeIds=recipes;}
    /// <param name="rhaFactor">The native rhaFactor of a layer material (the Technology file the cassette model was built from).</param>
    internal static LayeredRecipe Build(CassetteModel model,LayerLayout layout,Func<string,double> rhaFactor)
    {
        var forward=new Dictionary<int,int>();for(int i=0;i<model.Layers.Length;i++)forward[model.Layers[i].SourceLayerIndex]=i;
        var reverse=new Dictionary<int,int>();for(int i=0;i<model.ReverseLayers.Length;i++)reverse[model.ReverseLayers[i].SourceLayerIndex]=i;
        var f=new int[layout.Groups.Length][];var r=new int[layout.Groups.Length][];var rha=new double[layout.Groups.Length];
        for(int g=0;g<layout.Groups.Length;g++)
        {
            var group=layout.Groups[g];
            f[g]=group.Layers.Select(i=>forward[i]).ToArray();r[g]=group.Layers.Reverse().Select(i=>reverse[i]).ToArray();
            double sum=0;foreach(var i in group.Layers){var l=model.Preset.Layers[i];var own=rhaFactor(l.Material);if(!double.IsFinite(own)||own<0)throw new InvalidOperationException("Layer material unavailable: "+l.Material);sum+=own*LayerLayout.LayerDepth(l);}
            rha[g]=sum/group.DepthMm;
        }
        bool tilted=false;foreach(var g in model.Groups)tilted|=Math.Abs(g.Sin)>1e-9;
        var hesh=Math.Clamp(model.HeshRetention(ProtectionFactorTable.HeshReferenceCalibreMm).Retention,1e-3,1);
        var recipes=model.Layers.Select(l=>l.Recipe?.Id).Where(x=>x!=null).Distinct().ToArray()!;
        return new(model,layout,f,r,rha,tilted,hesh,recipes!);
    }
    internal int Slot(string id){for(int i=0;i<RecipeIds.Length;i++)if(RecipeIds[i]==id)return i;return -1;}
}

/// <summary>
/// 0.22.60: the hit per layer. Each piece of a plate in layers pays the steel equivalent of its own face: its real normal thickness (path x cos),
/// split over its recipe layers in the ratio of the recipe, its real angle plus the recipe tilt as the table counts it (mean of with and against
/// tilt), and the air measured along the shot line to the previous and next piece of the same plate (coupling free up to 50 mm). What the recipe
/// tracks across layers (repeat factor, classic dose, rod and jet work, the native line of sight) runs on along the shot in the order the faces are
/// reached; the steel equivalent of a piece is the growth of the composer protection L of everything reached so far (CassetteModel.Evaluate and
/// ProtectionFactorTable.Protection, the same formulas). Factor = steel equivalent / path. A piece without a face or rank keeps the recipe table.
/// </summary>
internal static class LayerHit
{
    private struct Side{internal double Native,Normal,Joules,Jet,Dose,Retention,Last;}
    /// <summary>
    /// 0.22.69: how a plate in layers is paid. Uniform (default): every millimetre of solid material of the plate on the shot line counts with the same factor, the
    /// table factor of the recipe at the angle of the piece times the recipe depth (air included) over the solid depth, so a plate at its recipe depth pays exactly what the
    /// same plate as one block pays. Nothing in it depends on which face a piece belongs to (layer, side wall, rank). PerLayer: the calculation of 0.22.60 to 0.22.68.
    /// </summary>
    internal enum HitModel{Uniform,PerLayer}
    internal static HitModel Model{get;set;}=HitModel.Uniform;
    /// <summary>The factor on every solid millimetre of a plate in layers: recipe depth (air included) over the depth of its solid layers.</summary>
    internal static double SolidScale(LayeredRecipe recipe)
    {
        double solid=0;foreach(var g in recipe.Layout.Groups)solid+=g.DepthMm;
        return solid>0&&double.IsFinite(solid)?recipe.Layout.DepthMm/solid:1;
    }
    /// <summary>0.22.69: the factor of every piece, from its own angle only.</summary>
    internal static void UniformFactors(LayeredRecipe recipe,ProtectionFactorTable table,ProtectionThreat threat,IReadOnlyList<LayerChord> chords,Span<double> factors)
    {
        double scale=SolidScale(recipe);
        for(int i=0;i<chords.Count;i++)factors[i]=table.Factor(threat,chords[i].Angle)*scale;
    }
    internal static MaterialThreatTags Tags(ProtectionThreat threat)=>threat switch
    {
        ProtectionThreat.Jet=>new MaterialThreatTags("","chemicalJet"),
        ProtectionThreat.LongRod=>new MaterialThreatTags("","longRod"),
        ProtectionThreat.Hesh=>new MaterialThreatTags("hesh",""),
        _=>new MaterialThreatTags("ap","kineticFullBore")
    };
    /// <summary>Factors for the sorted pieces of one plate along one shot line; returns how many pieces kept the recipe table (no face or rank).</summary>
    internal static int Factors(LayeredRecipe recipe,ProtectionFactorTable table,ProtectionThreat threat,IReadOnlyList<LayerChord> chords,Span<double> factors,Span<double> thicknessMm)
    {
        var tags=Tags(threat);bool rod=tags.UsesRod,classic=tags.UsesClassic;
        var with=new Side{Retention=1};var against=new Side{Retention=1};
        int slots=recipe.RecipeIds.Length;Span<int> countWith=slots<=32?stackalloc int[32]:new int[slots];Span<int> countAgainst=slots<=32?stackalloc int[32]:new int[slots];
        countWith.Clear();countAgainst.Clear();int fallback=0;
        for(int i=0;i<chords.Count;i++)
        {
            var c=chords[i];double path=(c.EndM-c.StartM)*1000;
            thicknessMm[i]=double.IsFinite(c.Angle)?path*Math.Cos(c.Angle*Math.PI/180):double.NaN;
            // 0.22.61: a side wall is rolled steel with its own path and angle (factor 1 per mm of path); it is not a layer of the recipe.
            if(c.Rank==SideWallRank&&path>0&&double.IsFinite(c.Angle)){factors[i]=1;continue;}
            if(c.Rank<0||c.Rank>=recipe.Layout.Groups.Length||!(path>0)||!double.IsFinite(c.Angle)||!(thicknessMm[i]>0)){factors[i]=table.Factor(threat,c.Angle);fallback++;continue;}
            int before=i-1,after=i+1;while(before>=0&&chords[before].Rank==SideWallRank)before--;while(after<chords.Count&&chords[after].Rank==SideWallRank)after++;
            double frontGap=before>=0?Math.Max(-1,(c.StartM-chords[before].EndM)*1000):0,rearGap=after<chords.Count?Math.Max(-1,(chords[after].StartM-c.EndM)*1000):0;
            double a=Increment(recipe,c.Rank,c.Back,path,c.Angle,frontGap,rearGap,threat,tags,rod,classic,ref with,countWith,1);
            double steel=a;
            if(recipe.Tilted){double b=Increment(recipe,c.Rank,c.Back,path,c.Angle,frontGap,rearGap,threat,tags,rod,classic,ref against,countAgainst,-1);steel=(a+b)*.5;}
            var factor=steel/path;
            factors[i]=double.IsFinite(factor)&&factor>=0?GroupedProtection.Scale(factor,table.Composer):table.Factor(threat,c.Angle);
        }
        return fallback;
    }
    private static double Increment(LayeredRecipe r,int g,bool reverse,double pathMm,double angle,double frontGap,double rearGap,ProtectionThreat threat,MaterialThreatTags tags,bool rod,bool classic,ref Side s,Span<int> counts,int sign)
    {
        var model=r.Model;var group=r.Layout.Groups[g];var radians=angle*Math.PI/180;double cos=Math.Cos(radians);double normal=pathMm*cos;double scale=normal/group.DepthMm;
        s.Native+=pathMm*r.GroupRha[g];s.Normal+=normal*r.GroupRha[g];
        var work=model.Work;double energy=ProtectionFactorTable.ReferenceEnergyJ;
        if(threat!=ProtectionThreat.Hesh)
        {
            var ray=new Vector3(0,(float)(Math.Sin(radians)*sign),(float)-cos);
            var order=reverse?r.Reverse[g]:r.Forward[g];var layers=model.Traversal(reverse);var category=tags.Category;
            for(int k=0;k<order.Length;k++)
            {
                var layer=layers[order[k]];var recipe=layer.Recipe;if(recipe==null)continue;
                int slot=r.Slot(recipe.Id);double repeat=1;if(slot>=0){repeat=Math.Pow(.85,counts[slot]);counts[slot]++;}
                if(!layer.Supported||!tags.Matches(recipe.ThreatTags))continue;
                var threatScale=CassetteModel.ScaleFor(recipe,category);if(threatScale is null)continue;
                var raw=CassetteModel.RawAngle(ray,CassetteModel.TiltNormal(Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,model.Groups[layer.Group]));var effective=Math.Min(CassetteModel.MaximumEffectiveAngle,raw);
                double front=k==0?frontGap:0,rear=k==order.Length-1?rearGap:0;
                var coupling=recipe.Mode is "backed" or "sandwich"?MaterialReactionModel.Coupling(rear,recipe.MaximumGapMm)*(recipe.Mode=="sandwich"?MaterialReactionModel.Coupling(front,recipe.MaximumGapMm):1):1;
                var factor=layer.NeraAirFactor*repeat*threatScale.Value;var t=layer.Layer.ThicknessMm*scale;
                if(classic)
                {
                    if(t<recipe.MinimumThicknessMm||t>recipe.MaximumThicknessMm)continue;
                    if(recipe.Mode is "backed" or "sandwich"){var dose=MaterialWorkMath.ClassicDose(recipe,t,effective,coupling,true)*factor;s.Retention*=(1+s.Dose)/(1+s.Dose+dose);s.Dose+=dose;}
                    else s.Retention*=1-MaterialWorkMath.ClassicDirectLoss(recipe,t,effective,recipe.MaximumLoss)*factor;
                    continue;
                }
                var w=MaterialWorkMath.Calculate(recipe,work,rod,t,effective,coupling,true,ProtectionFactorTable.RodDiameterM);
                s.Joules+=w.Joules*factor;s.Jet+=w.JetMm*factor;
            }
        }
        double total=threat switch
        {
            ProtectionThreat.Jet=>s.Native+Math.Min(s.Jet,.75*work.JetBudgetMm),
            ProtectionThreat.LongRod=>ProtectionFactorTable.Kinetic(s.Native,ProtectionFactorTable.RodReferenceMm,Math.Min(s.Joules,.75*energy)/energy,true),
            ProtectionThreat.FullBoreKE=>ProtectionFactorTable.Kinetic(s.Native,ProtectionFactorTable.FullBoreReferenceMm,Math.Min(energy*(1-Math.Pow(s.Retention,2/1.43)),.75*energy)/energy,false),
            _=>s.Normal/r.HeshRetention
        };
        double increment=total-s.Last;s.Last=total;return increment;
    }
    /// <summary>The report label of one piece: "T-80U cellular cast armour, layer 3 of 32: NERA (4 mm, 31°)".</summary>
    /// <summary>0.22.61: the rank of a side wall (a face that runs along the ribs of the layers).</summary>
    internal const int SideWallRank=-2;
    /// <summary>0.22.61: where a side wall and a face of the plate claim the same stretch of the shot line, only the face pays: the side wall keeps the rest.</summary>
    internal static List<LayerChord> ClipSideWalls(IReadOnlyList<LayerChord> chords)
    {
        var faces=chords.Where(c=>c.Rank!=SideWallRank).ToList();var result=new List<LayerChord>(faces);
        foreach(var wall in chords.Where(c=>c.Rank==SideWallRank))
        {
            double cursor=wall.StartM;
            foreach(var f in faces.Where(f=>f.EndM>wall.StartM&&f.StartM<wall.EndM).OrderBy(f=>f.StartM))
            {
                if(f.StartM>cursor)result.Add(wall with{StartM=cursor,EndM=Math.Min(wall.EndM,f.StartM)});
                cursor=Math.Max(cursor,f.EndM);if(cursor>=wall.EndM)break;
            }
            if(cursor<wall.EndM)result.Add(wall with{StartM=cursor});
        }
        result.Sort((a,b)=>a.StartM.CompareTo(b.StartM));
        return result;
    }
    internal static string Label(string tableLabel,LayeredRecipe recipe,LayerChord chord,double thicknessMm)=>
        chord.Rank==SideWallRank&&double.IsFinite(thicknessMm)?string.Create(CultureInfo.InvariantCulture,$"{tableLabel}, side wall ({thicknessMm:0} mm, {chord.Angle:0}°)"):
        chord.Rank>=0&&chord.Rank<recipe.Layout.Groups.Length&&double.IsFinite(thicknessMm)
            ?string.Create(CultureInfo.InvariantCulture,$"{tableLabel}, {recipe.Layout.Groups[chord.Rank].Name} ({thicknessMm:0} mm, {chord.Angle:0}°)")
            :tableLabel+" (layer face not found: recipe table)";
    /// <summary>
    /// The composer estimate of a plate in layers, from the same calculation as the hit: a flat plate at the recipe's nominal depth, shot at
    /// <paramref name="angle"/>, every face kind one piece, air as real gaps along the shot. Steel equivalent in mm.
    /// </summary>
    internal static double Estimate(LayeredRecipe recipe,ProtectionFactorTable table,ProtectionThreat threat,double angle,HitModel? model=null)
    {
        var chords=StraightStack(recipe,angle,recipe.Layout.DepthMm);var f=new double[chords.Length];var t=new double[chords.Length];
        if((model??Model)==HitModel.Uniform)UniformFactors(recipe,table,threat,chords,f);else Factors(recipe,table,threat,chords,f,t);
        double sum=0;for(int i=0;i<chords.Length;i++)sum+=(chords[i].EndM-chords[i].StartM)*1000*f[i];
        return sum;
    }
    /// <summary>The pieces of a flat plate in layers of total depth <paramref name="depthMm"/> (air included) for a shot at <paramref name="angle"/>.</summary>
    internal static LayerChord[] StraightStack(LayeredRecipe recipe,double angle,double depthMm)
    {
        var cos=Math.Cos(angle*Math.PI/180);var groups=recipe.Layout.Groups;var chords=new LayerChord[groups.Length];
        for(int g=0;g<groups.Length;g++)chords[g]=new(groups[g].StartFraction*depthMm/cos*.001,groups[g].EndFraction*depthMm/cos*.001,angle,g,false);
        return chords;
    }
}

// ======== 0.22.61 lagen als vlakken, deel 2: a kneaded box, side walls, the overlay per layer and the weight (pure) ========
internal enum BoxFaceKind{Front,Back,Side,Other}
/// <summary>A side face cut into strips: output face index, the recipe fractions it spans along its ribs, and whether that span is an air layer.</summary>
internal readonly record struct LayerStrip(int Face,double From,double To,bool Air);
/// <summary>What a box conversion did (for the window, the tests and the report).</summary>
internal sealed record LayerBoxInfo(Vector3 Forward,int FrontFaces,int BackFaces,int SideFaces,int SideWallsKept,int SideWallsWhole,int SideWallsDropped,double DepthMm,double RecipeDepthMm,
    double OuterDeviationMm,int RibLines,int RayLines,int ThicknessLines,LayerStrip[] Strips,int Components);
internal sealed record LayerBoxGate(Vector3 Forward,BoxFaceKind[] Kinds,Vector3[] Normals);

internal static partial class LayerFaces
{
    /// <summary>
    /// 0.22.61 face classes against the designer's forward axis (the front of the vehicle, in the mesh frame): front when the normal points within
    /// 55 degrees of forward (cos 55 = 0.5736), back within 55 degrees of backward, side otherwise. A box turned 30 degrees keeps its front (cos 30 = 0.866)
    /// and its sides (cos 60 = 0.5).
    /// </summary>
    internal const double FrontCos=0.5735764363510461;
    internal const string DirectionLine="The layers run between the two largest faces, starting at the one that faces the front most.";
    internal static BoxFaceKind Classify(Vector3 normal,Vector3 forward){var c=Vector3.Dot(normal,forward);return c>FrontCos?BoxFaceKind.Front:c< -FrontCos?BoxFaceKind.Back:BoxFaceKind.Side;}
    private static readonly double SinTenDegrees=Math.Sin(10*Math.PI/180);
    /// <summary>
    /// A box: at least one front face, and a back face, or a front region whose every outer edge is shared with a side face (an open box without a back).
    /// A loose plate and a Cheek fill (shell) filling (front faces only, open edges) are not a box and keep the 0.22.60 planner.
    /// </summary>
    /// <summary>0.22.63: true when the mesh is a box in the sense of Convert to layers (a front face with a back or side faces around it, against the forward axis).</summary>
    internal static bool IsBox(string json,Vector3 forward,Vector3? centre=null){try{return BoxGate(OrientClosed(Parse(json)),forward,centre)!=null;}catch(Exception){return false;}}
    /// <summary>
    /// 0.22.65: a closed box whose faces do not all wind the same way (a saved box can have single faces turned over, as the Challenger box of the user has: five of its six
    /// normals point inward) is turned outward before the sides are chosen. Only a closed mesh (every edge shared by exactly two faces) of triangles and quads is touched; the faces
    /// that wind against their neighbour are reversed (corner order, thicknesses, modes and edge references together), and the whole mesh when its volume is negative.
    /// A mesh that is open, or already wound the same way outward, comes back unchanged.
    /// </summary>
    private static (float[] Vertices,int[] Edges,int[] Flags,bool HasFlags,SourceFace[] Faces) OrientClosed((float[] Vertices,int[] Edges,int[] Flags,bool HasFlags,SourceFace[] Faces) mesh)
    {
        var faces=mesh.Faces;int n=faces.Length;var p0=mesh.Vertices;
        if(n<4||faces.Any(f=>f.V.Length<3||f.V.Length>4||f.V.Distinct().Count()!=f.V.Length||f.V.Any(v=>v<0||3*v+2>=p0.Length)))return mesh;
        var users=new Dictionary<(int,int),List<(int Face,bool Forward)>>();
        for(int i=0;i<n;i++)for(int j=0;j<faces[i].V.Length;j++)
        {
            int a=faces[i].V[j],b=faces[i].V[(j+1)%faces[i].V.Length];var key=(Math.Min(a,b),Math.Max(a,b));
            if(!users.TryGetValue(key,out var list))users[key]=list=new();list.Add((i,a<b));
        }
        // An edge of three faces (a partition wall meeting a front) is not crossed when orienting; the faces on its two sides are oriented from their own neighbours.
        bool closed=users.Values.All(l=>l.Count==2);
        var flip=new bool?[n];var queue=new Queue<int>();var component=new int[n];int components=0;
        for(int start=0;start<n;start++)
        {
            if(flip[start]!=null)continue;flip[start]=false;component[start]=components++;queue.Enqueue(start);
            while(queue.Count>0)
            {
                int i=queue.Dequeue();
                for(int j=0;j<faces[i].V.Length;j++)
                {
                    int a=faces[i].V[j],b=faces[i].V[(j+1)%faces[i].V.Length];var list=users[(Math.Min(a,b),Math.Max(a,b))];
                    if(list.Count!=2)continue; // a boundary edge of an open surface
                    var mine=list.First(x=>x.Face==i);var other=list.First(x=>x.Face!=i);
                    // Consistent: the two faces run along the shared edge in opposite directions (after the flips).
                    bool wantFlip=(mine.Forward^flip[i]!.Value)==(other.Forward);
                    if(flip[other.Face]==null){flip[other.Face]=wantFlip;component[other.Face]=component[i];queue.Enqueue(other.Face);}
                    else if(flip[other.Face]!=wantFlip)return mesh; // not an orientable surface: leave it alone
                }
            }
        }
        Vector3 P(int i)=>new(p0[3*i],p0[3*i+1],p0[3*i+2]);double volume=0;
        if(closed)
        {
            for(int i=0;i<n;i++)
            {
                var ids=flip[i]==true?faces[i].V.Reverse().ToArray():faces[i].V;
                for(int j=1;j+1<ids.Length;j++)volume+=Vector3.Dot(P(ids[0]),Vector3.Cross(P(ids[j]),P(ids[j+1])))/6.0;
            }
            bool all=volume<0;if(all)for(int i=0;i<n;i++)flip[i]=!flip[i];
        }
        else
        {
            // An open surface has no volume: per connected piece the side that points away from the centre of the piece, weighted by area, is the outside.
            for(int c=0;c<components;c++)
            {
                var members=Enumerable.Range(0,n).Where(i=>component[i]==c).ToArray();
                var centre=members.SelectMany(i=>faces[i].V).Distinct().Select(P).Aggregate(Vector3.Zero,(a,b)=>a+b)/members.SelectMany(i=>faces[i].V).Distinct().Count();
                double vote=0;
                foreach(var i in members)
                {
                    var ids=flip[i]==true?faces[i].V.Reverse().ToArray():faces[i].V;var pts=ids.Select(P).ToArray();var nn=CheekShellFill.Newell(pts);
                    var mid=pts.Aggregate(Vector3.Zero,(a,b)=>a+b)/pts.Length;vote+=Vector3.Dot(nn,mid-centre);
                }
                if(vote<0)foreach(var i in members)flip[i]=!flip[i];
            }
        }
        if(!flip.Any(x=>x==true))return mesh;
        var result=new SourceFace[n];
        for(int i=0;i<n;i++)
        {
            var f=faces[i];
            result[i]=flip[i]==true?new SourceFace(f.V.Reverse().ToArray(),f.T.Reverse().ToArray(),f.Tm.Reverse().ToArray(),f.Te.Reverse().ToArray(),f.Raw,f.Packed):f;
        }
        return (mesh.Vertices,mesh.Edges,mesh.Flags,mesh.HasFlags,result);
    }
    /// <summary>0.22.64: the layers run between the two sides of the block that lie opposite each other with the highest score (PairGate); without such a pair the 0.22.63 rule (front by the angle to the forward axis, an open box) applies.</summary>
    private static LayerBoxGate? BoxGate((float[] Vertices,int[] Edges,int[] Flags,bool HasFlags,SourceFace[] Faces) mesh,Vector3 forward,Vector3? centre)
    {
        // An open box (front faces with side faces around them, no back) stays as it was in 0.22.63: nothing lies opposite its front.
        var angle=AngleGate(mesh,forward);
        if(angle!=null&&!angle.Kinds.Contains(BoxFaceKind.Back))return angle;
        // 0.22.66: a block that is open at the back (the faces at the rim are missing) takes the side opposite the open rim as its front.
        var open=OpenGate(mesh);if(open!=null)return open;
        return PairGate(mesh,forward,centre)??angle;
    }
    /// <summary>
    /// 0.22.64: the faces of the block that lie opposite each other, grouped over a kink, and the pair of sides with the highest score; the layers run between them.
    /// Opposite: for a face the face behind it (normal at least 120 degrees away, its centre on the inner side) whose normal is closest to the reverse; two neighbouring faces whose
    /// normals differ by less than 70 degrees belong to the same side when the faces opposite them are neighbours too with the same angle (within 20 degrees): a kink of a beam
    /// continues through the block, a wedge does not. Score of a pair of sides: sum over its faces of area x (1 + |cos(normal, forward)|). The side that points most to the front
    /// (area-weighted forward cosine) is the front; within 0.1 of each other, the side farther from the centre of the vehicle. Every other face is a side wall. Null without a pair.
    /// </summary>
    private static LayerBoxGate? PairGate((float[] Vertices,int[] Edges,int[] Flags,bool HasFlags,SourceFace[] Faces) mesh,Vector3 forward,Vector3? centre)
    {
        if(!ComposerFill.Finite(forward)||forward.LengthSquared()<1e-8f)return null;
        var f=Vector3.Normalize(forward);var p0=mesh.Vertices;Vector3 P(int i)=>new(p0[3*i],p0[3*i+1],p0[3*i+2]);int n=mesh.Faces.Length;
        var normals=new Vector3[n];var area=new double[n];var middle=new Vector3[n];var valid=new bool[n];
        for(int i=0;i<n;i++)
        {
            var face=mesh.Faces[i];
            if(face.V.Length<3||face.V.Length>4||!face.Packed||face.V.Distinct().Count()!=face.V.Length||face.V.Any(v=>v<0||3*v+2>=p0.Length))continue;
            var pts=face.V.Select(P).ToArray();var nn=CheekShellFill.Newell(pts);if(nn.LengthSquared()<1e-14f)continue;
            normals[i]=Vector3.Normalize(nn);area[i]=nn.Length()*.5;middle[i]=pts.Aggregate(Vector3.Zero,(a,b)=>a+b)/pts.Length;valid[i]=true;
        }
        if(valid.Count(x=>x)<4)return null;
        var neighbours=new bool[n,n];
        for(int i=0;i<n;i++)for(int j=i+1;j<n;j++)if(valid[i]&&valid[j]&&mesh.Faces[i].V.Intersect(mesh.Faces[j].V).Count()>=2)neighbours[i,j]=neighbours[j,i]=true;
        var opposite=new int[n];
        for(int i=0;i<n;i++)
        {
            opposite[i]=-1;if(!valid[i])continue;double best=-.5;
            for(int j=0;j<n;j++)
            {
                if(j==i||!valid[j])continue;double d=Vector3.Dot(normals[i],normals[j]);
                if(d>=best||Vector3.Dot(middle[j]-middle[i],-normals[i])<=0)continue;best=d;opposite[i]=j;
            }
        }
        // A kink edge of a beam has more than one other face on each end (the faces above and below continue through it); the edge of a sheared box has only three faces at its ends.
        var valence=new Dictionary<int,int>();
        for(int i=0;i<n;i++)if(valid[i])foreach(var v in mesh.Faces[i].V)valence[v]=valence.GetValueOrDefault(v)+1;
        var parent=Enumerable.Range(0,n).ToArray();int Find(int x){while(parent[x]!=x)x=parent[x]=parent[parent[x]];return x;}
        for(int i=0;i<n;i++)for(int k=i+1;k<n;k++)
        {
            if(!neighbours[i,k])continue;
            if(mesh.Faces[i].V.Intersect(mesh.Faces[k].V).Any(v=>valence[v]<4))continue;double d=Vector3.Dot(normals[i],normals[k]);if(d<=Math.Cos(70*Math.PI/180))continue;
            int j=opposite[i],l=opposite[k];if(j<0||l<0||j==l||!neighbours[j,l])continue;
            double a1=Math.Acos(Math.Clamp(d,-1,1)),a2=Math.Acos(Math.Clamp(Vector3.Dot(normals[j],normals[l]),-1,1));
            if(Math.Abs(a1-a2)>20*Math.PI/180)continue;
            parent[Find(i)]=Find(k);parent[Find(j)]=Find(l);
        }
        var groups=Enumerable.Range(0,n).Where(i=>valid[i]).GroupBy(Find).Select(g=>g.ToArray()).ToArray();
        Vector3 Mean(int[] g){var s=Vector3.Zero;foreach(var i in g)s+=normals[i]*(float)area[i];return s.LengthSquared()<1e-14f?Vector3.Zero:Vector3.Normalize(s);}
        double Score(int[] g)=>g.Sum(i=>area[i]*(1+Math.Abs(Vector3.Dot(normals[i],f))));
        double bestScore=0;(int[] A,int[] B)? pair=null;
        for(int a=0;a<groups.Length;a++)for(int b=a+1;b<groups.Length;b++)
        {
            if(Vector3.Dot(Mean(groups[a]),Mean(groups[b]))>-.5)continue;
            if(!groups[a].Any(i=>opposite[i]>=0&&groups[b].Contains(opposite[i]))&&!groups[b].Any(i=>opposite[i]>=0&&groups[a].Contains(opposite[i])))continue;
            var s=Score(groups[a])+Score(groups[b]);if(s>bestScore){bestScore=s;pair=(groups[a],groups[b]);}
        }
        if(pair==null)return null;
        var (g1,g2)=pair.Value;double Forward(int[] g)=>g.Sum(i=>area[i]*Vector3.Dot(normals[i],f))/g.Sum(i=>area[i]);
        double f1=Forward(g1),f2=Forward(g2);bool firstFront=f1>=f2;
        if(Math.Abs(f1-f2)<.1&&centre is {} c)
        {
            Vector3 Mid(int[] g){var s=Vector3.Zero;double w=0;foreach(var i in g){s+=middle[i]*(float)area[i];w+=area[i];}return s/(float)w;}
            firstFront=Vector3.Distance(Mid(g1),c)>=Vector3.Distance(Mid(g2),c);
        }
        var front=firstFront?g1:g2;var back=firstFront?g2:g1;
        var kinds=new BoxFaceKind[n];
        for(int i=0;i<n;i++)kinds[i]=!valid[i]?BoxFaceKind.Other:front.Contains(i)?BoxFaceKind.Front:back.Contains(i)?BoxFaceKind.Back:BoxFaceKind.Side;
        var axis=Mean(front);if(axis==Vector3.Zero)return null;
        return new(axis,kinds,normals);
    }
    /// <summary>
    /// 0.22.66: for a surface with boundary edges (a block without a back): the boundary vertices are the open rim; the faces that lie entirely on the other vertices and point away from
    /// the rim (their normal more than 60 degrees from the direction of the rim) are the front, the other faces are side walls. The mesh must have at least three side faces (a loose
    /// plate or a filling is not a block) and the mean normal of the front must oppose the direction of the rim. Null otherwise.
    /// </summary>
    private static LayerBoxGate? OpenGate((float[] Vertices,int[] Edges,int[] Flags,bool HasFlags,SourceFace[] Faces) mesh)
    {
        var p0=mesh.Vertices;Vector3 P(int i)=>new(p0[3*i],p0[3*i+1],p0[3*i+2]);int n=mesh.Faces.Length;
        var normals=new Vector3[n];var area=new double[n];var valid=new bool[n];
        for(int i=0;i<n;i++)
        {
            var face=mesh.Faces[i];
            if(face.V.Length<3||face.V.Length>4||!face.Packed||face.V.Distinct().Count()!=face.V.Length||face.V.Any(v=>v<0||3*v+2>=p0.Length))continue;
            var nn=CheekShellFill.Newell(face.V.Select(P).ToArray());if(nn.LengthSquared()<1e-14f)continue;normals[i]=Vector3.Normalize(nn);area[i]=nn.Length()*.5;valid[i]=true;
        }
        if(valid.Count(x=>x)<5)return null;
        var uses=new Dictionary<(int,int),int>();
        for(int i=0;i<n;i++)if(valid[i])for(int j=0;j<mesh.Faces[i].V.Length;j++){int a=mesh.Faces[i].V[j],b=mesh.Faces[i].V[(j+1)%mesh.Faces[i].V.Length];var k=(Math.Min(a,b),Math.Max(a,b));uses[k]=uses.GetValueOrDefault(k)+1;}
        if(!uses.Values.Any(c=>c==1))return null;
        var rim=new HashSet<int>();foreach(var (k,c) in uses)if(c==1){rim.Add(k.Item1);rim.Add(k.Item2);}
        var inner=Enumerable.Range(0,n).Where(i=>valid[i]).SelectMany(i=>mesh.Faces[i].V).Where(v=>!rim.Contains(v)).Distinct().ToArray();
        if(inner.Length<3)return null;
        var towardRim=rim.Select(P).Aggregate(Vector3.Zero,(a,b)=>a+b)/rim.Count-inner.Select(P).Aggregate(Vector3.Zero,(a,b)=>a+b)/inner.Length;
        if(towardRim.LengthSquared()<1e-10f)return null;towardRim=Vector3.Normalize(towardRim);
        var kinds=new BoxFaceKind[n];var front=new List<int>();
        for(int i=0;i<n;i++)
        {
            kinds[i]=!valid[i]?BoxFaceKind.Other:BoxFaceKind.Side;
            if(valid[i]&&mesh.Faces[i].V.All(v=>!rim.Contains(v))&&Vector3.Dot(normals[i],towardRim)<-.5){kinds[i]=BoxFaceKind.Front;front.Add(i);}
        }
        if(front.Count==0||kinds.Count(k=>k==BoxFaceKind.Side)<3)return null;
        var axis=front.Aggregate(Vector3.Zero,(a,i)=>a+normals[i]*(float)area[i]);if(axis.LengthSquared()<1e-14f)return null;axis=Vector3.Normalize(axis);
        if(Vector3.Dot(axis,towardRim)>-.7f)return null;
        return new(axis,kinds,normals);
    }
    private static LayerBoxGate? AngleGate((float[] Vertices,int[] Edges,int[] Flags,bool HasFlags,SourceFace[] Faces) mesh,Vector3 forward)
    {
        if(!ComposerFill.Finite(forward)||forward.LengthSquared()<1e-8f)return null;
        var f=Vector3.Normalize(forward);var p0=mesh.Vertices;Vector3 P(int i)=>new(p0[3*i],p0[3*i+1],p0[3*i+2]);
        var kinds=new BoxFaceKind[mesh.Faces.Length];var normals=new Vector3[mesh.Faces.Length];
        for(int i=0;i<mesh.Faces.Length;i++)
        {
            var face=mesh.Faces[i];kinds[i]=BoxFaceKind.Other;
            if(face.V.Length<3||face.V.Length>4||!face.Packed||face.V.Distinct().Count()!=face.V.Length||face.V.Any(v=>v<0||3*v+2>=p0.Length))continue;
            var n=CheekShellFill.Newell(face.V.Select(P).ToArray());if(n.LengthSquared()<1e-14f)continue;
            normals[i]=Vector3.Normalize(n);kinds[i]=Classify(normals[i],f);
        }
        if(!kinds.Contains(BoxFaceKind.Front))return null;
        if(kinds.Contains(BoxFaceKind.Back))return new(f,kinds,normals);
        var frontEdges=new Dictionary<(int,int),int>();var sideEdges=new HashSet<(int,int)>();
        for(int i=0;i<mesh.Faces.Length;i++)
        {
            var v=mesh.Faces[i].V;
            for(int j=0;j<v.Length;j++)
            {
                var k=(Math.Min(v[j],v[(j+1)%v.Length]),Math.Max(v[j],v[(j+1)%v.Length]));
                if(kinds[i]==BoxFaceKind.Front)frontEdges[k]=frontEdges.TryGetValue(k,out var c)?c+1:1;
                else if(kinds[i]==BoxFaceKind.Side)sideEdges.Add(k);
            }
        }
        var outer=frontEdges.Where(e=>e.Value==1).Select(e=>e.Key).ToArray();
        return outer.Length>0&&outer.All(sideEdges.Contains)?new(f,kinds,normals):null;
    }
    private static LayerConversion ConvertBox(string json,(float[] Vertices,int[] Edges,int[] Flags,bool HasFlags,SourceFace[] Faces) mesh,CassetteModel recipe,LayerBoxGate gate,Func<string,string>? label,Func<string,double>? density,double wallMm)
    {
        foreach(var kind in new[]{LayerLayoutKind.PerLayer,LayerLayoutKind.PerBlock})
        {
            var layout=LayerLayout.Build(recipe.Preset,kind,label);
            var diag=new BoxDiag();
            var attempt=AttemptBox(json,mesh,recipe,layout,gate,density,wallMm,true,diag,out var thinnest,out var problem);
            // 0.22.64: a wall corner led along the old surface (tm 4 on a helper edge) may run parallel to an edge of a layer, and the saved mesh would then read that layer as a wall;
            // the plan is checked by reading its own JSON back, and without helper edges the walls stay straight (sticking out by at most their thickness).
            if(attempt==null&&problem==RecognitionProblem)attempt=AttemptBox(json,mesh,recipe,layout,gate,density,wallMm,false,diag,out thinnest,out problem);
            if(attempt!=null)return attempt;
            if(problem==RecognitionProblem)problem="This shape cannot be turned into layers yet: after saving and loading the mod could not tell the layers from the side walls. Make the box a little simpler.";
            if(problem!=null)throw new InvalidOperationException(problem);
            if(kind==LayerLayoutKind.PerBlock&&diag.NoBack)throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,$"This plate has no back and no edges running back, so its own thickness ({diag.OwnMm:0} mm) is the depth. That is too thin for layers. Give it more thickness or shape it into a box."));
            if(kind==LayerLayoutKind.PerBlock)throw new InvalidOperationException($"This box is too thin for layers: even a block of touching layers would be thinner than 3 mm (thinnest {thinnest:0.#} mm). Make the box deeper, or use a cassette with thicker layers.");
        }
        throw new InvalidOperationException("Convert to layers: no layout.");
    }
    /// <summary>One line of a box corner: from the front vertex along its rib (a box edge to the back, or straight back to the first face it meets) to its end.</summary>
    private sealed record BoxLine(string Key,int Origin,Vector3 From,Vector3 U,double LengthMm,int End,Vector3 EndPoint,bool Rib,bool Ray,int Face);

    private static LayerConversion? AttemptBox(string json,(float[] Vertices,int[] Edges,int[] Flags,bool HasFlags,SourceFace[] Faces) mesh,CassetteModel recipe,LayerLayout layout,LayerBoxGate gate,Func<string,double>? density,double wallMm,bool guide,BoxDiag diag,out double thinnest,out string? problem)
    {
        thinnest=double.PositiveInfinity;problem=null;
        var p0=mesh.Vertices;int v0=p0.Length/3;Vector3 P(int i)=>new(p0[3*i],p0[3*i+1],p0[3*i+2]);
        var groups=layout.Groups;int G=groups.Length;var kinds=gate.Kinds;var normals=gate.Normals;var f=gate.Forward;
        // Every boundary of the recipe (front and end of every group): strips need the points of the air layers too.
        var bounds=groups.SelectMany(g=>new[]{g.StartFraction,g.EndFraction}).Append(0).Append(1).Distinct().OrderBy(x=>x).ToList();
        for(int i=bounds.Count-1;i>0;i--)if(bounds[i]-bounds[i-1]<1e-12)bounds.RemoveAt(i);
        int B(double fraction){for(int i=0;i<bounds.Count;i++)if(Math.Abs(bounds[i]-fraction)<1e-12)return i;throw new InvalidOperationException("Convert to layers: boundary missing.");}
        var start=groups.Select(g=>B(g.StartFraction)).ToArray();var end=groups.Select(g=>B(g.EndFraction)).ToArray();
        bool AirSpan(int k){double mid=(bounds[k]+bounds[k+1])/2;return !groups.Any(g=>mid>g.StartFraction&&mid<g.EndFraction);}
        var frontVertices=new HashSet<int>();var frontAt=new Dictionary<int,List<int>>();
        for(int i=0;i<mesh.Faces.Length;i++)if(kinds[i]==BoxFaceKind.Front)foreach(var v in mesh.Faces[i].V){frontVertices.Add(v);if(!frontAt.TryGetValue(v,out var list))frontAt[v]=list=new();list.Add(i);}
        var edgeAt=new Dictionary<int,List<int>>();int origEdges=mesh.Edges.Length/2;
        for(int e=0;e<origEdges;e++)foreach(var v in new[]{mesh.Edges[2*e],mesh.Edges[2*e+1]}){if(!edgeAt.TryGetValue(v,out var list))edgeAt[v]=list=new();list.Add(e);}
        // 1. One line per front vertex: an edge of the box from that vertex to a point that is not on the front (the rib), or else straight back to
        // the first face the line meets; the rib must lie at least 10 degrees behind every front face at the vertex.
        var lines=new Dictionary<int,BoxLine>();int ribLines=0,rayLines=0;
        bool Behind(int v,Vector3 u)=>frontAt[v].All(i=>Vector3.Dot(normals[i],u)< -SinTenDegrees);
        foreach(var v in frontVertices.OrderBy(x=>x))
        {
            var origin=P(v);BoxLine? best=null;double score=double.NegativeInfinity;
            if(edgeAt.TryGetValue(v,out var incident))
                foreach(var e in incident)
                {
                    int w=mesh.Edges[2*e]==v?mesh.Edges[2*e+1]:mesh.Edges[2*e];if(w<0||w>=v0||frontVertices.Contains(w))continue;
                    var dir=P(w)-origin;var length=dir.Length();if(!(length>1e-5f))continue;var u=dir/length;
                    if(Vector3.Dot(u,-f)<SinTenDegrees||!Behind(v,u))continue;
                    var s=Vector3.Dot(u,-f);if(s>score){score=s;best=new($"b:{v}",v,origin,u,length*1000,w,P(w),true,false,-1);}
                }
            if(best==null&&Behind(v,-f))
            {
                double nearest=double.PositiveInfinity;
                for(int i=0;i<mesh.Faces.Length;i++)
                {
                    var face=mesh.Faces[i];if(kinds[i]==BoxFaceKind.Other||face.V.Contains(v))continue;
                    for(int k=1;k+1<face.V.Length;k++)
                    {
                        var hit=ComposerFill.Ray(origin,-f,new[]{new FillTriangle(P(face.V[0]),P(face.V[k]),P(face.V[k+1]))},50);
                        if(hit is {} t&&t>.001&&t<nearest)nearest=t;
                    }
                }
                if(double.IsFinite(nearest))best=new($"b:{v}",v,origin,-f,nearest*1000,-1,origin-f*(float)nearest,true,true,-1);
            }
            if(best!=null){lines[v]=best;if(best.Ray)rayLines++;else ribLines++;}
        }
        // Corners without a box line keep the 0.22.60 rule of their face (its own thickness along its normal or its own rib).
        // 0.22.66: no edge and no line runs back from the front: the own thickness of the face is the only depth.
        diag.NoBack=ribLines+rayLines==0;diag.OwnMm=Enumerable.Range(0,mesh.Faces.Length).Where(i=>kinds[i]==BoxFaceKind.Front).SelectMany(i=>mesh.Faces[i].T.Select(t=>(double)t)).DefaultIfEmpty(0).Max();
        int thicknessLines=0;var faceLine=new Dictionary<(int Face,int Corner),BoxLine>();int changed=0;
        for(int i=0;i<mesh.Faces.Length;i++)
        {
            if(kinds[i]!=BoxFaceKind.Front)continue;var face=mesh.Faces[i];bool faceChanged=false;
            for(int j=0;j<face.V.Length;j++)
            {
                if(lines.TryGetValue(face.V[j],out var line)){faceLine[(i,j)]=line;continue;}
                var v=P(face.V[j]);var n=normals[i];bool rib=false;var u=-n;double length=face.T[j];
                if(face.Tm[j]==CheekShellFill.AlongEdgeManual)
                {
                    int e=face.Te[j];
                    if(e>=0&&2*e+1<mesh.Edges.Length&&(mesh.Edges[2*e]==face.V[j]||mesh.Edges[2*e+1]==face.V[j]))
                    {
                        int w=mesh.Edges[2*e]==face.V[j]?mesh.Edges[2*e+1]:mesh.Edges[2*e];var dir=P(w)-v;
                        if(w>=0&&w<v0&&dir.LengthSquared()>1e-14f){var cand=Vector3.Normalize(dir);double d=Vector3.Dot(n,cand);if(d<SinOneDegree){rib=true;u=cand;length=face.T[j]/-d;}}
                    }
                }
                else if(face.Tm[j]!=0)faceChanged=true;
                faceLine[(i,j)]=new($"t:{i}:{j}",face.V[j],v,u,length,-1,v+u*(float)(length*.001),rib,false,i);thicknessLines++;
            }
            if(faceChanged)changed++;
        }
        // 2. Points on the lines: the front vertex at fraction 0, the existing end vertex (or one helper point) at fraction 1, new points between.
        var newVertices=new List<Vector3>();var points=new Dictionary<(string,int),int>();
        int Point(BoxLine l,int k)
        {
            if(k==0)return l.Origin;
            if(k==bounds.Count-1&&l.End>=0)return l.End;
            if(points.TryGetValue((l.Key,k),out var id))return id;
            var q=k==bounds.Count-1?l.EndPoint:l.From+l.U*(float)(bounds[k]*l.LengthMm*.001);
            if(!ComposerFill.Finite(q))throw new InvalidOperationException("Convert to layers: a layer point is not finite.");
            id=v0+newVertices.Count;newVertices.Add(q);points[(l.Key,k)]=id;return id;
        }
        Vector3 At(int id)=>id<v0?P(id):newVertices[id-v0];
        var edgeIndex=new Dictionary<(int,int),int>();
        for(int e=0;e<origEdges;e++)edgeIndex.TryAdd((Math.Min(mesh.Edges[2*e],mesh.Edges[2*e+1]),Math.Max(mesh.Edges[2*e],mesh.Edges[2*e+1])),e);
        var newEdges=new List<int>();var newFlags=new List<int>();
        int Edge(int a,int b,int flag)
        {
            var k=(Math.Min(a,b),Math.Max(a,b));if(edgeIndex.TryGetValue(k,out var e))return e;
            e=origEdges+newEdges.Count/2;newEdges.Add(a);newEdges.Add(b);newFlags.Add(flag);edgeIndex[k]=e;return e;
        }
        int FlagOf(int a,int b)=>edgeIndex.TryGetValue((Math.Min(a,b),Math.Max(a,b)),out var oe)&&oe<origEdges?mesh.Flags[oe]:0;
        // 3. The back: every corner of a back face must be the end of a line, so the last layers close the box where the back was.
        var lineEnds=new Dictionary<int,List<BoxLine>>();
        foreach(var l in lines.Values)if(l.End>=0){if(!lineEnds.TryGetValue(l.End,out var list))lineEnds[l.End]=list=new();list.Add(l);}
        // 0.22.63: a back corner that no edge from the front reaches is no reason to refuse: the front corners then run straight back to the first face they meet (a ray line); the check below measures the result.
        double rho=recipe.Passive.Density;double Rho(string id)=>density?.Invoke(id)??rho;
        double GroupDensity(LayerGroup g){double mass=0;foreach(var i in g.Layers){var l=recipe.Preset.Layers[i];mass+=Rho(l.Material)*LayerLayout.LayerDepth(l);}return mass/g.DepthMm;}
        var rawKept=new List<int>();var dropReasons=new List<string>();var helperAt=new Dictionary<int,List<(Vector3 U,int Vertex)>>();
        var sources=new List<LayerSourceInfo>();var faceTexts=new List<string>();var faces=new List<LayerFaceInfo>();var backCorners=new List<Vector3[]>();var strips=new List<LayerStrip>();
        // 0.22.62: the old outer shape (the old faces as a closed surface); a wall point the game builds must not lie outside it (limit 0.5 mm here, 1 mm in the final check).
        var oldShape=Enumerable.Range(0,mesh.Faces.Length).Where(i=>mesh.Faces[i].V.Length>=3&&mesh.Faces[i].V.All(v=>v>=0&&3*v+2<p0.Length)).Select(i=>mesh.Faces[i].V.Select(P).ToArray()).ToArray();
        double volumeBefore=0,volumeAfter=0,trueMass=0,deviation=0;int kept=0,whole=0,dropped=0,skipped=0,converted=0;
        Vector3[] OwnBack(int i)
        {
            var face=mesh.Faces[i];var c=face.V.Select(P).ToArray();var n=normals[i]==Vector3.Zero?Vector3.Zero:normals[i];var back=new Vector3[c.Length];
            for(int j=0;j<c.Length;j++)
            {
                back[j]=c[j]-n*(face.T[j]*.001f);
                if(face.Tm[j]==CheekShellFill.AlongEdgeManual&&face.Te[j] is var e&&e>=0&&2*e+1<mesh.Edges.Length&&(mesh.Edges[2*e]==face.V[j]||mesh.Edges[2*e+1]==face.V[j]))
                {int w=mesh.Edges[2*e]==face.V[j]?mesh.Edges[2*e+1]:mesh.Edges[2*e];var dir=P(w)-c[j];if(dir.LengthSquared()>1e-14f){var u=Vector3.Normalize(dir);if(Vector3.Dot(n,u)<SinOneDegree)back[j]=CheekShellFill.NativeBack(c[j],c[(j+1)%c.Length],c[(j+c.Length-1)%c.Length],u,face.T[j],n);}}
            }
            return back;
        }
        Vector3 BackNormalAt(BoxLine l)
        {
            if(l.End>=0)for(int i=0;i<mesh.Faces.Length;i++)if(kinds[i]==BoxFaceKind.Back&&mesh.Faces[i].V.Contains(l.End))return normals[i];
            return -f;
        }
        for(int i=0;i<mesh.Faces.Length;i++)
        {
            var face=mesh.Faces[i];var corners=face.V.Select(P).ToArray();
            if(kinds[i]!=BoxFaceKind.Other)volumeBefore+=ComposerFill.PrismVolume(corners,OwnBack(i));
            switch(kinds[i])
            {
                case BoxFaceKind.Front:
                {
                    converted++;int n=face.V.Length;var fl=Enumerable.Range(0,n).Select(j=>faceLine[(i,j)]).ToArray();
                    sources.Add(new(i,true,null,corners,fl.Select(l=>l.EndPoint).ToArray(),normals[i],false,false,fl.Select(l=>l.U).ToArray(),fl.Select(l=>l.LengthMm).ToArray()));
                    for(int g=0;g<G;g++)
                    {
                        var ids=fl.Select(l=>Point(l,start[g])).ToArray();var c=ids.Select(At).ToArray();var nn=CheekShellFill.Newell(c);
                        if(nn.LengthSquared()<1e-14f){problem=$"Convert to layers: face {i+1} would get a degenerate layer; nothing changed.";return null;}
                        var ng=Vector3.Normalize(nn);
                        if(Vector3.Dot(ng,normals[i])<=0){problem=$"Convert to layers: a layer of face {i+1} would face the wrong way; nothing changed.";return null;}
                        var t=new ushort[n];var modes=new byte[n];var ribs=new int[n];var back=new Vector3[n];
                        for(int j=0;j<n;j++)
                        {
                            var l=fl[j];var p=c[j];var next=c[(j+1)%n];var prev=c[(j+n-1)%n];
                            double len=(groups[g].EndFraction-groups[g].StartFraction)*l.LengthMm;var target=At(Point(l,end[g]));
                            bool useRib=l.Rib;
                            if(!useRib)
                            {
                                double depth=Vector3.Dot(p-target,ng)*1000;double drift=Math.Max(0,depth)*Vector3.Cross(ng,-l.U).Length();
                                if(drift<=ParallelDriftMm)
                                {
                                    int tt=(int)Math.Round(depth,MidpointRounding.AwayFromZero);thinnest=Math.Min(thinnest,tt);if(tt<MinimumMm)return null;
                                    t[j]=(ushort)tt;back[j]=p-ng*(tt*.001f);continue;
                                }
                            }
                            double d=Vector3.Dot(ng,l.U);
                            if(d>SinOneDegree){problem=$"Convert to layers: a layer of face {i+1} would lie along its rib (corner {j+1}); nothing changed.";return null;}
                            double offsetDepth=Vector3.Dot(CheekShellFill.Offset(p,next,prev),ng)*1000;
                            int value=(int)Math.Round(len*-d+offsetDepth,MidpointRounding.AwayFromZero);
                            if(g==G-1)while(value>MinimumMm&&Vector3.Dot(CheekShellFill.NativeBack(p,next,prev,l.U,(ushort)value,ng)-target,l.U)>.0001f)value--;
                            thinnest=Math.Min(thinnest,value);if(value<MinimumMm)return null;
                            if(value>short.MaxValue){problem="Convert to layers: a layer would be thicker than the game allows; nothing changed.";return null;}
                            t[j]=(ushort)value;modes[j]=CheekShellFill.AlongEdgeManual;
                            // Its own piece of rib: from the front of this layer to its end (the strips of a side wall use the same edges).
                            ribs[j]=Edge(ids[j],Point(l,end[g]),0);
                            back[j]=CheekShellFill.NativeBack(p,next,prev,l.U,(ushort)value,ng);
                            if(g==G-1)deviation=Math.Max(deviation,Math.Abs(Vector3.Dot(back[j]-target,BackNormalAt(l)))*1000);
                        }
                        var nb=CheekShellFill.Newell(back);
                        if(nb.LengthSquared()<1e-16f||Vector3.Dot(nb,ng)<=0){problem=$"Convert to layers: the back of a layer of face {i+1} would turn over; nothing changed.";return null;}
                        for(int j=0;j<n;j++)Edge(ids[j],ids[(j+1)%n],FlagOf(face.V[j],face.V[(j+1)%n]));
                        ulong tm=0,te=0;for(int j=0;j<n;j++){tm|=(ulong)modes[j]<<(8*j);te|=(ulong)(uint)ribs[j]<<(16*j);}
                        faceTexts.Add(FaceText(ids,t,tm,te));faces.Add(new(i,g,ids,c,back,t,modes,ribs,ng));backCorners.Add(back);
                        var vol=ComposerFill.PrismVolume(c,back);volumeAfter+=vol;trueMass+=vol*GroupDensity(groups[g]);
                    }
                    break;
                }
                case BoxFaceKind.Back:
                    sources.Add(new(i,false,"back face",corners,OwnBack(i),normals[i],false,false,Array.Empty<Vector3>(),Array.Empty<double>()));break;
                case BoxFaceKind.Side:
                {
                    sources.Add(new(i,false,"side face",corners,OwnBack(i),normals[i],false,false,Array.Empty<Vector3>(),Array.Empty<double>()));
                    // Corner -> (line, end of that line): a front vertex is the start of its line, a back vertex the end of the one line that reaches it.
                    var n=face.V.Length;var map=new (BoxLine Line,bool AtEnd)?[n];
                    for(int j=0;j<n;j++)
                    {
                        if(lines.TryGetValue(face.V[j],out var l))map[j]=(l,false);
                        else if(lineEnds.TryGetValue(face.V[j],out var ends)&&ends.Count==1)map[j]=(ends[0],true);
                    }
                    bool Strippable()
                    {
                        if(n!=4||map.Any(m=>m==null))return false;
                        var distinct=map.Select(m=>m!.Value.Line).Distinct().ToArray();if(distinct.Length!=2)return false;
                        foreach(var l in distinct)
                        {
                            var at=Enumerable.Range(0,n).Where(j=>map[j]!.Value.Line==l).ToArray();
                            if(at.Length!=2||map[at[0]]!.Value.AtEnd==map[at[1]]!.Value.AtEnd||(at[1]-at[0]!=1&&at[1]-at[0]!=3))return false;
                        }
                        return true;
                    }
                    bool OnLine(int a,int b)=>lines.Values.Any(l=>l.End>=0&&(l.Origin==a&&l.End==b||l.Origin==b&&l.End==a));
                    if(Strippable())
                    {
                        // 0.22.63: all strips of the face are made first; when one corner cannot be placed within the wall's own thickness of the old shape, the whole wall is left out and counted.
                        var pendTexts=new List<string>();var pendFaces=new List<LayerFaceInfo>();var pendBack=new List<Vector3[]>();var pendStrips=new List<LayerStrip>();double pendVol=0;var pendHelpers=new List<(int Slot,Vector3 Point,int From,Vector3 U,int Vertex)>();bool failed=false;
                        int wallInt=Math.Clamp((int)Math.Round(wallMm,MidpointRounding.AwayFromZero),MinimumMm,100);
                        var pendFaceStart=faces.Count;
                        for(int k=0;k+1<bounds.Count&&!failed;k++)
                        {
                            var ids=new int[n];var t=new ushort[n];
                            for(int j=0;j<n;j++)
                            {
                                var (l,atEnd)=map[j]!.Value;int kk=atEnd?k+1:k;ids[j]=Point(l,kk);
                                t[j]=(ushort)wallInt; // 0.22.62: the wall thickness is a setting; the thickness of the old faces does not count
                            }
                            var c=ids.Select(At).ToArray();var nn=CheekShellFill.Newell(c);if(nn.LengthSquared()<1e-16f){failed=true;break;}
                            var ns=Vector3.Normalize(nn);
                            // A wall corner at an acute edge may lie in the neighbouring face: straight inward when that is inside; else led along the old surface (tm 4 on a helper edge, the game ignores its length);
                            // else straight inward when it sticks out by no more than the wall's own thickness (0.22.63); otherwise the wall is left out.
                            var back=new Vector3[n];var modes=new byte[n];var ribs=new int[n];var helper=new (bool Use,Vector3 U,int Vertex)[n];
                            for(int j=0;j<n;j++)
                            {
                                var straight=c[j]-ns*(t[j]*.001f);double d0=LayerShape.OutsideDistance(straight,oldShape);
                                if(d0<=.0005){back[j]=straight;continue;}
                                if(guide&&SurfaceRib(c[j],ns,t[j],c[(j+1)%n],c[(j+n-1)%n],oldShape,Enumerable.Range(0,v0+newVertices.Count).Select(id=>(id,At(id))).ToArray()) is {} guided){back[j]=guided.Back;modes[j]=CheekShellFill.AlongEdgeManual;helper[j]=(true,guided.U,guided.Vertex);continue;}
                                if(d0<=t[j]*.001+.0005){back[j]=straight;continue;}
                                failed=true;break;
                            }
                            if(failed)break;
                            int baseSlot=pendHelpers.Count;
                            for(int j=0;j<n;j++)if(helper[j].Use)pendHelpers.Add((pendFaces.Count*8+j,c[j]+helper[j].U*.005f,ids[j],helper[j].U,helper[j].Vertex));
                            pendTexts.Add(string.Join("|",ids)+"#"+string.Join(",",t)+"#"+string.Join(",",modes));
                            pendFaces.Add(new(i,-2,ids,c,back,t,modes,ribs,ns));pendBack.Add(back);pendStrips.Add(new(0,bounds[k],bounds[k+1],AirSpan(k)));
                            pendVol+=ComposerFill.PrismVolume(c,back);
                        }
                        if(failed){dropped++;dropReasons.Add($"side face {i+1}: a corner of the wall would stick out of the old shape by more than the thickness of the wall");}
                        else
                        {
                            kept++;
                            var made=new Dictionary<int,int>();
                            foreach(var h in pendHelpers)
                            {
                                // 0.22.68: as few helper points as possible: the end of the rib is an existing point on that line when there is one, else a helper shared by every corner that needs the same direction at this point, else a new point that lies inside the old shape.
                                int hv=h.Vertex;var from=At(h.From);
                                for(int cand=0;cand<v0+newVertices.Count&&hv<0;cand++)
                                {
                                    if(cand==h.From)continue;var d=At(cand)-from;var len=d.Length();if(len<.002f)continue;
                                    if(Vector3.Dot(d/len,h.U)>Math.Cos(.5*Math.PI/180)&&LayerShape.OutsideDistance(At(cand),oldShape)<=.001)hv=cand;
                                }
                                helperAt.TryGetValue(h.From,out var shared);
                                if(hv<0&&shared!=null)foreach(var x in shared)if(Vector3.Dot(x.U,h.U)>Math.Cos(.5*Math.PI/180)){hv=x.Vertex;break;}
                                if(hv<0)
                                {
                                    var point=h.Point;foreach(var length in new[]{.005f,.002f,.001f,.0005f}){point=from+h.U*length;if(LayerShape.OutsideDistance(point,oldShape)<=.0008)break;}
                                    hv=v0+newVertices.Count;newVertices.Add(point);if(shared==null)helperAt[h.From]=shared=new();shared.Add((h.U,hv));
                                }
                                made[h.Slot]=Edge(h.From,hv,0);
                            }
                            for(int s=0;s<pendFaces.Count;s++)
                            {
                                var pf=pendFaces[s];var ribsS=new int[n];ulong tmS=0,teS=0;
                                for(int j=0;j<n;j++){if(pf.Modes[j]==CheekShellFill.AlongEdgeManual)ribsS[j]=made[s*8+j];tmS|=(ulong)pf.Modes[j]<<(8*j);teS|=(ulong)(uint)ribsS[j]<<(16*j);}
                                for(int j=0;j<n;j++)Edge(pf.Vertices[j],pf.Vertices[(j+1)%n],map[j]!.Value.Line==map[(j+1)%n]!.Value.Line?0:FlagOf(face.V[j],face.V[(j+1)%n]));
                                strips.Add(new(faces.Count,pendStrips[s].From,pendStrips[s].To,pendStrips[s].Air));
                                faceTexts.Add(FaceText(pf.Vertices,pf.Thickness,tmS,teS));faces.Add(new(i,-2,pf.Vertices,pf.Front,pf.Back,pf.Thickness,pf.Modes,ribsS,pf.Normal));backCorners.Add(pf.Back);
                            }
                            volumeAfter+=pendVol;trueMass+=pendVol*Rho("rha");
                        }
                    }
                    else if(!Enumerable.Range(0,n).Any(j=>OnLine(face.V[j],face.V[(j+1)%n])))
                    {
                        // 0.22.62: kept whole, but along its own normal (tm 0) so that the game builds what the plan says (Auto would pick an edge at run time). 0.22.63: left out when it would stick out by more than its own thickness.
                        var wt=new ushort[n];int wallInt=Math.Clamp((int)Math.Round(wallMm,MidpointRounding.AwayFromZero),MinimumMm,100);bool ok=true;
                        for(int j=0;j<n;j++){wt[j]=(ushort)wallInt;if(LayerShape.OutsideDistance(corners[j]-normals[i]*(wallInt*.001f),oldShape)>wallInt*.001+.0005)ok=false;}
                        if(!ok){dropped++;dropReasons.Add($"side face {i+1}: it would stick out of the old shape by more than its thickness");}
                        else
                        {
                            whole++;
                            var own=corners.Select((q,j)=>q-normals[i]*(wt[j]*.001f)).ToArray();faceTexts.Add(FaceText(face.V,wt,0,0));faces.Add(new(i,-1,face.V,corners,own,wt,new byte[n],new int[n],normals[i]));backCorners.Add(own);
                            var vol=ComposerFill.PrismVolume(corners,own);volumeAfter+=vol;trueMass+=vol*rho;
                        }
                    }
                    else{dropped++;dropReasons.Add($"side face {i+1}: it cannot be cut into one strip per layer and its edges carry new points");}
                    break;
                }
                default:
                {
                    skipped++;var nn=normals[i];var own=corners.Select((q,j)=>q-nn*(j<face.T.Length?face.T[j]:0)*.001f).ToArray();
                    sources.Add(new(i,false,"other face",corners,own,nn,false,false,Array.Empty<Vector3>(),Array.Empty<double>()));
                    faceTexts.Add(face.Raw);faces.Add(new(i,-1,face.V,corners,own,face.T,face.Tm,face.Te,nn));rawKept.Add(faces.Count-1);backCorners.Add(own);
                    var vol=ComposerFill.PrismVolume(corners,own);volumeBefore+=vol;volumeAfter+=vol;trueMass+=vol*rho;break;
                }
            }
        }
        if(origEdges+newEdges.Count/2>=65535){problem="Convert to layers: the box would get too many edges for the game's corner data; nothing changed.";return null;}
        var payload=Assemble(json,newVertices,newEdges,newFlags,mesh.HasFlags,faceTexts);
        // 0.22.68: nothing in the output that serves nothing: an edge belongs to a face or is a thickness rib; a point belongs to a face or ends such a rib; no two points on one spot.
        var cleanup=LayerCompact.Run(payload,rawKept);payload=cleanup.Json;
        for(int fi=0;fi<faces.Count;fi++)faces[fi]=faces[fi] with{Vertices=faces[fi].Vertices.Select(x=>cleanup.VertexMap[x]).ToArray(),RibEdges=faces[fi].RibEdges.Select(e=>cleanup.EdgeMap.TryGetValue(e,out var ne)?ne:0).ToArray()};
        // 0.22.62: measure what the game builds (native back points per tm value), not the plan: nothing may lie more than 1 mm outside the old outer shape.
        {
            // Layers keep 1 mm; a side wall (5 mm steel on the inside of a face) may stick out of the old shape by its own thickness at an acute edge.
            var rankOf=faces.Select(f=>f.Rank).ToArray();double wallLimit=Math.Clamp((int)Math.Round(wallMm,MidpointRounding.AwayFromZero),MinimumMm,100)+.5;
            var (worst,where)=LayerShape.Deviation(oldShape,payload,fi=>fi<rankOf.Length&&rankOf[fi]>=0?1.0:fi<rankOf.Length&&rankOf[fi]==-2?wallLimit:wallLimit);
            if(worst>0){problem=$"This shape cannot be turned into layers yet: the layers would stick out of the box by {(worst+1).ToString("0.#",CultureInfo.InvariantCulture)} mm ({where}). Make the corners of the box meet at the back, or make the box deeper. Nothing changed.";return null;}
        }
        {
            // What the saved mesh will say: every layer face is assigned to its layer and every wall strip is read as a wall.
            var read=LayerRanks.FromJson(payload);
            for(int fi=0;fi<faces.Count&&fi<read.Length;fi++)
                if(faces[fi].Rank>=0&&read[fi]<0||faces[fi].Rank==-2&&read[fi]!=-2){problem=RecognitionProblem;return null;}
        }
        var after=ComposerFill.Read(payload,true);
        if(after.FaceCount!=faces.Count)throw new InvalidOperationException("Convert to layers: payload face count mismatch.");
        // One connected whole: faces joined through shared vertices.
        var parent=Enumerable.Range(0,faces.Count).ToArray();int Find(int x){while(parent[x]!=x)x=parent[x]=parent[parent[x]];return x;}
        var owner=new Dictionary<int,int>();
        for(int i=0;i<faces.Count;i++)foreach(var v in faces[i].Vertices){if(owner.TryGetValue(v,out var o))parent[Find(i)]=Find(o);else owner[v]=i;}
        int components=Enumerable.Range(0,faces.Count).Select(Find).Distinct().Count();
        double boxDepth=lines.Values.Select(l=>l.LengthMm*Vector3.Dot(l.U,-f)).DefaultIfEmpty(0).Max();
        var info=new LayerBoxInfo(f,converted,kinds.Count(k=>k==BoxFaceKind.Back),kinds.Count(k=>k==BoxFaceKind.Side),kept,whole,dropped,boxDepth,layout.DepthMm,deviation,ribLines,rayLines,thicknessLines,strips.ToArray(),components);
        var mode=layout.Kind==LayerLayoutKind.PerLayer?ThicknessMode.Layers:ThicknessMode.LayerBlocks;
        var text=new List<string>
        {
            $"{converted} faces become {converted*G} faces: {G} layers each.",
            layout.Kind==LayerLayoutKind.PerLayer?"One face per layer.":"Touching layers share one face, because some layers would be thinner than 3 mm.",
            DirectionLine,
            $"Thinnest layer: {thinnest.ToString("0",CultureInfo.InvariantCulture)} mm."
        };
        if(kept>0)text.Add($"{kept} side walls kept as steel.");
        if(kept+whole>0)text.Add($"Side walls are {Math.Clamp((int)Math.Round(wallMm,MidpointRounding.AwayFromZero),MinimumMm,100)} mm thick, inward from the old side faces (setting: Layer box side wall thickness).");
        if(dropped>0){text.Add($"{dropped} side walls could not be kept.");text.Add("Left out: "+string.Join("; ",dropReasons)+".");}
        if(boxDepth<layout.DepthMm-.5)text.Add($"The box is {boxDepth.ToString("0",CultureInfo.InvariantCulture)} mm deep; the recipe is made for {layout.DepthMm.ToString("0",CultureInfo.InvariantCulture)} mm. Protection scales with the depth.");
        if(deviation>1)text.Add($"Where the back is not flat the outer shape moves by up to {deviation.ToString("0.#",CultureInfo.InvariantCulture)} mm.");
        if(changed>0)text.Add($"{changed} faces now thicken straight back; their rear may shift slightly.");
        if(skipped>0)text.Add($"{skipped} faces have more than 4 corners and stay as they are.");
        text.Add($"Weight {(volumeBefore*rho).ToString("0.#",CultureInfo.InvariantCulture)} kg now, {(trueMass).ToString("0.#",CultureInfo.InvariantCulture)} kg as layers.");
        var summary=$"{converted} faces became {converted*G} faces ({(layout.Kind==LayerLayoutKind.PerLayer?"one face per layer":"touching layers share one face")}), front to back{(kept>0?$", {kept} side walls as steel":"")}. Undo removes this change in one step.";
        var plan=new ComposerMeshPlan(payload,after,0,0,0,summary,true){Action=ComposerMeshAction.LayerConvert,Total=after.Faces.Sum(x=>x.VertexCount),MaximumMm=short.MaxValue,BackCorners=backCorners.ToArray(),CopyCheck=copy=>VerifyCopy(payload,copy)};
        return new LayerConversion(plan,layout,mode,sources.ToArray(),faces.ToArray(),converted,skipped,changed,0,thinnest,volumeBefore,volumeAfter,volumeBefore*rho,volumeAfter*rho,density==null?null:trueMass,string.Join("\n",text)){Box=info,DropReason=dropped>0?string.Join("; ",dropReasons):null,Cleanup=cleanup};
    }
    /// <summary>
    /// 0.22.62: a direction in the plane of an old face that passes through the point (so the back point stays on the old surface), as close as possible to straight inward
    /// from the wall, turned a few degrees so that no edge of a layer is parallel to it. Null when no such direction keeps the back point inside the old shape.
    /// </summary>
    private sealed class BoxDiag{internal bool NoBack;internal double OwnMm;}
    private const string RecognitionProblem="__recognition";
    private static (Vector3 U,Vector3 Back,int Vertex)? SurfaceRib(Vector3 point,Vector3 wallNormal,int t,Vector3 next,Vector3 prev,IReadOnlyList<Vector3[]> oldShape,IReadOnlyList<(int Id,Vector3 P)> existing)
    {
        foreach(var poly in oldShape)
        {
            if(poly.Length<3||LayerOverlay.Distance(point,poly)>.0005)continue;
            var nf=CheekShellFill.Newell(poly);if(nf.LengthSquared()<1e-14f)continue;nf=Vector3.Normalize(nf);
            // 0.22.68: an existing point in the plane of that old face ends the rib; no new point is made. The most inward one wins.
            (Vector3 U,Vector3 Back,int Vertex)? best=null;float bestD=0;
            foreach(var (id,q) in existing)
            {
                var dir=q-point;var len=dir.Length();if(len<.002f||LayerOverlay.Distance(q,poly)>.0005)continue;var u=dir/len;
                float d=Vector3.Dot(wallNormal,u);if(d>-.3f||d>=bestD)continue;
                var back=CheekShellFill.NativeBack(point,next,prev,u,(ushort)t,wallNormal);
                if(LayerShape.OutsideDistance(back,oldShape)<=.0005){best=(u,back,id);bestD=d;}
            }
            if(best!=null)return best;
            var tangent=-wallNormal-nf*Vector3.Dot(-wallNormal,nf);if(tangent.LengthSquared()<.0025f)continue;tangent=Vector3.Normalize(tangent);
            foreach(var degrees in new[]{6f,-6f,12f,-12f,25f,-25f})
            {
                var u=Vector3.Transform(tangent,Quaternion.CreateFromAxisAngle(nf,degrees*MathF.PI/180));
                if(Vector3.Dot(wallNormal,u)>-.1f)continue;
                var back=CheekShellFill.NativeBack(point,next,prev,u,(ushort)t,wallNormal);
                if(LayerShape.OutsideDistance(back,oldShape)<=.0005)return (u,back,-1);
            }
        }
        return null;
    }
    private static string FaceText(int[] ids,ushort[] t,ulong tm,ulong te)=>
        "{\"v\":["+string.Join(",",ids)+"],\"t\":["+string.Join(",",t)+"],\"tm\":"+unchecked((int)(uint)tm).ToString(CultureInfo.InvariantCulture)+",\"te\":"+unchecked((long)te).ToString(CultureInfo.InvariantCulture)+"}";
}

/// <summary>
/// 0.22.61: the faces of a plate in layers as solids, read from the saved mesh JSON (native face order): corners, vertex ids, real back points (native
/// formula), the outward line of sight of every corner, the far end of every corner's rib, and the side walls.
/// Side wall rule (0.22.68: not for a face whose every corner uses a thickness rib): a face with an edge that runs along a thickness rib of the plate (parallel within 1 degree to a rib edge, tm 4, that shares a vertex
/// with it) is a side wall. A layer face never has such an edge (its edges lie in its own plane and its ribs are at least 1 degree behind it); a strip
/// of a side wall always has two (over a layer it owns the rib edge itself; over an air layer its edges continue the ribs of the next layer). The rule
/// needs nothing stored per face and survives save and load and a layer moved by hand along its ribs.
/// </summary>
internal sealed record LayerMesh(Vector3[][] Polygons,int[][] Ids,Vector3[][] Backs,Vector3[][] Directions,int[][] RibEnds,bool[] SideWalls,Vector3[] Normals)
{
    internal static readonly double ParallelCos=Math.Cos(Math.PI/180);
    internal static LayerMesh Read(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        var v=root.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();Vector3 P(int i)=>new(v[3*i],v[3*i+1],v[3*i+2]);
        var edges=root.TryGetProperty("edges",out var e)?e.EnumerateArray().Select(x=>x.GetInt32()).ToArray():Array.Empty<int>();
        var polygons=new List<Vector3[]>();var ids=new List<int[]>();var backs=new List<Vector3[]>();var directions=new List<Vector3[]>();var ends=new List<int[]>();var normals=new List<Vector3>();
        var ribs=new List<(int A,int B,Vector3 U)>();
        foreach(var f in root.GetProperty("faces").EnumerateArray())
        {
            var id=f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            var t=f.TryGetProperty("t",out var tv)?tv.EnumerateArray().Select(x=>(int)x.GetUInt16()).ToArray():new int[id.Length];
            if(id.Any(i=>i<0||3*i+2>=v.Length)||t.Length!=id.Length){polygons.Add(Array.Empty<Vector3>());ids.Add(id);backs.Add(Array.Empty<Vector3>());directions.Add(Array.Empty<Vector3>());ends.Add(Array.Empty<int>());normals.Add(Vector3.Zero);continue;}
            var p=id.Select(P).ToArray();polygons.Add(p);ids.Add(id);
            var nn=CheekShellFill.Newell(p);var n=nn.LengthSquared()<1e-14f?Vector3.Zero:Vector3.Normalize(nn);normals.Add(n);
            if(n==Vector3.Zero||id.Length>ComposerFill.PackedCorners){backs.Add(p.Select((q,j)=>q-n*(t[j]*.001f)).ToArray());directions.Add(Array.Empty<Vector3>());ends.Add(Enumerable.Repeat(-1,id.Length).ToArray());continue;}
            ulong tm=0,te=0;
            if(f.TryGetProperty("tm",out var tmv)&&tmv.ValueKind==JsonValueKind.Number)tm=JsonBits.Tm(tmv);
            if(f.TryGetProperty("te",out var tev)&&tev.ValueKind==JsonValueKind.Number)te=JsonBits.Te(tev);
            var corner=new Vector3[id.Length];var back=new Vector3[id.Length];var end=new int[id.Length];
            for(int j=0;j<id.Length;j++)
            {
                var u=-n;end[j]=-1;back[j]=p[j]-n*(t[j]*.001f);
                if(((tm>>(8*j))&0xff)==CheekShellFill.AlongEdgeManual)
                {
                    int edge=(int)((te>>(16*j))&0xffff);
                    if(2*edge+1<edges.Length&&(edges[2*edge]==id[j]||edges[2*edge+1]==id[j]))
                    {
                        int w=edges[2*edge]==id[j]?edges[2*edge+1]:edges[2*edge];
                        if(w>=0&&3*w+2<v.Length&&(P(w)-p[j]).LengthSquared()>1e-14f)
                        {
                            var cand=Vector3.Normalize(P(w)-p[j]);
                            if(Vector3.Dot(cand,n)< -0.0174524f)
                            {
                                u=cand;end[j]=w;ribs.Add((id[j],w,cand));
                                back[j]=CheekShellFill.NativeBack(p[j],p[(j+1)%p.Length],p[(j+p.Length-1)%p.Length],cand,(ushort)t[j],n);
                            }
                        }
                    }
                }
                corner[j]=-u;
            }
            directions.Add(corner);backs.Add(back);ends.Add(end);
        }
        // Side walls: an edge of the face parallel to a rib that shares one of its vertices.
        var ribsAt=new Dictionary<int,List<Vector3>>();
        foreach(var (a,b,u) in ribs)foreach(var x in new[]{a,b}){if(!ribsAt.TryGetValue(x,out var list))ribsAt[x]=list=new();list.Add(u);}
        var side=new bool[polygons.Count];
        for(int i=0;i<polygons.Count;i++)
        {
            var id=ids[i];var p=polygons[i];if(p.Length<3)continue;
            // 0.22.68: a face whose every corner uses a thickness rib is a layer face, never a wall (a wall strip has a rib only where it was led along the old surface): a helper rib that happens to run
            // along an edge of a layer must not turn that layer into a wall (seen on the T-90A plate of 21:32: its outermost layer, 77 mm, was read as a wall).
            if(ends[i].Length>0&&ends[i].All(e=>e>=0))continue;
            for(int j=0;j<id.Length&&!side[i];j++)
            {
                int a=id[j],b=id[(j+1)%id.Length];var d=p[(j+1)%p.Length]-p[j];if(d.LengthSquared()<1e-14f)continue;d=Vector3.Normalize(d);
                foreach(var x in new[]{a,b})if(ribsAt.TryGetValue(x,out var list)&&list.Any(u=>Math.Abs(Vector3.Dot(u,d))>=ParallelCos)){side[i]=true;break;}
            }
        }
        return new(polygons.ToArray(),ids.ToArray(),backs.ToArray(),directions.ToArray(),ends.ToArray(),side,normals.ToArray());
    }
    /// <summary>The face whose solid (front polygon, real back, sides) holds the point; a layer face before a side wall, the smallest solid first; -1 when none.</summary>
    internal int FaceContaining(Vector3 point,IReadOnlyList<int> ranks)
    {
        int best=-1;double volume=double.PositiveInfinity;bool bestWall=true;
        for(int i=0;i<Polygons.Length;i++)
        {
            var f=Polygons[i];var b=Backs[i];if(f.Length<3||b.Length!=f.Length)continue;
            var min=Vector3.Min(f.Aggregate(Vector3.Min),b.Aggregate(Vector3.Min))-new Vector3(1e-4f);var max=Vector3.Max(f.Aggregate(Vector3.Max),b.Aggregate(Vector3.Max))+new Vector3(1e-4f);
            if(point.X<min.X||point.Y<min.Y||point.Z<min.Z||point.X>max.X||point.Y>max.Y||point.Z>max.Z)continue;
            if(!Inside(point,f,b))continue;
            bool wall=i<ranks.Count&&ranks[i]==LayerHit.SideWallRank;var vol=Math.Abs(ComposerFill.PrismVolume(f,b));
            if(best<0||bestWall&&!wall||wall==bestWall&&vol<volume){best=i;volume=vol;bestWall=wall;}
        }
        return best;
    }
    /// <summary>Ray parity against the closed prism (front fan, back fan, side quads as two triangles each).</summary>
    internal static bool Inside(Vector3 point,Vector3[] front,Vector3[] back)
    {
        var d=Vector3.Normalize(new Vector3(.5773f,.5779f,.5768f));int crossings=0;int n=front.Length;
        void Tri(Vector3 a,Vector3 b,Vector3 c){if(ComposerFill.Ray(point,d,new[]{new FillTriangle(a,b,c)},100) is {} t&&t>0)crossings++;}
        for(int j=1;j+1<n;j++){Tri(front[0],front[j],front[j+1]);Tri(back[0],back[j+1],back[j]);}
        for(int j=0;j<n;j++){int k=(j+1)%n;Tri(front[j],front[k],back[k]);Tri(front[j],back[k],back[j]);}
        return crossings%2==1;
    }
    /// <summary>Recipe fraction of a vertex: the front of a layer face is the start of its layer (or block), the far end of its rib the end.</summary>
    internal Dictionary<int,double> Fractions(IReadOnlyList<int> ranks,LayerLayout layout)
    {
        var map=new Dictionary<int,double>();
        for(int i=0;i<Ids.Length;i++)
        {
            if(i>=ranks.Count||ranks[i]<0||ranks[i]>=layout.Groups.Length)continue;var g=layout.Groups[ranks[i]];
            foreach(var id in Ids[i])map.TryAdd(id,g.StartFraction);
            foreach(var w in RibEnds[i])if(w>=0)map.TryAdd(w,g.EndFraction);
        }
        return map;
    }
}

/// <summary>
/// 0.22.61 overlay of a plate in layers. The native copy (front, built back, sides; d = depth across the face from 0 at the front to 1 at the back)
/// is re-mapped onto the recipe strip per face: a layer face shows only its own layer (or block), d -> start + d x (end - start) of that layer; a strip of
/// a side wall shows the recipe at its place along the ribs (the layer it spans), and a strip over an air layer is left out (empty space). Seen from the
/// side this is the section of the stack with the real layer thicknesses. Faces without a rank keep the whole recipe, as before.
/// </summary>
internal static class LayerOverlay
{
    internal const float ToleranceM=.0006f;
    /// <summary>What the overlay made of every triangle of the native copy (for the dump and the tests): the role found, the face, whether it is drawn and the depth it shows.</summary>
    internal sealed class OverlayReport
    {
        internal readonly List<string> Roles=new();internal readonly List<int> Faces=new();internal readonly List<bool> Drawn=new();internal readonly List<float> Colour=new();
    }
    private const float MatchM=.002f;
    /// <summary>
    /// 0.22.68 painted: only what is certainly inside is left out. A triangle is placed by its points, not by where it lies: each point is compared with the points the mod itself knows (the front
    /// corners of every face and the back corners from the native formulas, within 2 mm); a triangle with three front corners of one face is the front of that face, with three back corners its back,
    /// with corners of both its side. It is left out (zero area) only when every role it can have is certainly inside: the front of a layer face that has another layer face in front of it, the back of
    /// one that has another behind it, the side of a layer face that a side wall covers. Anything else, also a triangle that fits no face, is drawn, in the colour of its face (the front of the
    /// outermost layer in the colour of the first layer, the back of the innermost in the colour of the last) or of the nearest layer.
    /// </summary>
    internal static OverlayCopy RemapPainted(OverlayCopy copy,LayerMesh mesh,IReadOnlyList<int> ranks,LayerLayout layout,OverlayReport? report=null)
    {
        int n=mesh.Polygons.Length,G=layout.Groups.Length;var fractions=mesh.Fractions(ranks,layout);
        bool IsLayer(int i)=>i<ranks.Count&&ranks[i]>=0&&ranks[i]<G&&mesh.Polygons[i].Length>=3&&mesh.Backs[i].Length==mesh.Polygons[i].Length;
        bool IsWall(int i)=>i<ranks.Count&&ranks[i]==LayerHit.SideWallRank&&mesh.Polygons[i].Length>=3;
        // known points
        var cells=new Dictionary<(int,int,int),List<(int Face,bool Front)>>();
        (int,int,int) Cell(Vector3 p)=>((int)Math.Floor(p.X/MatchM),(int)Math.Floor(p.Y/MatchM),(int)Math.Floor(p.Z/MatchM));
        void Add(Vector3 p,int face,bool front){var c=Cell(p);if(!cells.TryGetValue(c,out var l))cells[c]=l=new();l.Add((face,front));}
        for(int i=0;i<n;i++)
        {
            if(mesh.Polygons[i].Length<3)continue;
            foreach(var p in mesh.Polygons[i])Add(p,i,true);
            if(mesh.Backs[i].Length==mesh.Polygons[i].Length)foreach(var p in mesh.Backs[i])Add(p,i,false);
        }
        List<(int Face,bool Front)> Near(Vector3 p)
        {
            var result=new List<(int,bool)>();var c=Cell(p);
            for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++)for(int dz=-1;dz<=1;dz++)
                if(cells.TryGetValue((c.Item1+dx,c.Item2+dy,c.Item3+dz),out var l))foreach(var (face,front) in l)
                {
                    var poly=front?mesh.Polygons[face]:mesh.Backs[face];
                    if(poly.Any(q=>Vector3.Distance(q,p)<=MatchM)&&!result.Contains((face,front)))result.Add((face,front));
                }
            return result;
        }
        // layer faces that have another layer face in front of / behind them (looked at, not ranked)
        var tris=new Dictionary<int,FillTriangle[]>();
        FillTriangle[] Both(Vector3[] poly)=>poly.Length==4?new[]{new FillTriangle(poly[0],poly[1],poly[2]),new FillTriangle(poly[0],poly[2],poly[3]),new FillTriangle(poly[1],poly[2],poly[3]),new FillTriangle(poly[1],poly[3],poly[0])}
            :Enumerable.Range(1,poly.Length-2).Select(k=>new FillTriangle(poly[0],poly[k],poly[k+1])).ToArray();
        for(int i=0;i<n;i++)if(IsLayer(i))tris[i]=Both(mesh.Polygons[i]);
        // A point is shadowed when another layer face of the same stack (normal within 25 degrees) lies in front of it (front test) or behind it (back test): looked at from the triangle itself, so
        // the extent of the faces and the cut of the quads do not matter.
        bool Shadowed(Vector3 origin,Vector3 dir,int self)
        {
            foreach(var (j,t) in tris)
            {
                if(j==self||Vector3.Dot(mesh.Normals[j],mesh.Normals[self])<.9f)continue;
                foreach(var tr in t)if(ComposerFill.Ray(origin,dir,new[]{tr},50) is {} h&&h>.00005)return true;
            }
            return false;
        }
        var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        for(int t=0;t+2<copy.Triangles.Length;t+=3)
        {
            int a=copy.Triangles[t],b=copy.Triangles[t+1],c=copy.Triangles[t+2];var pa=copy.Vertices[a];var pb=copy.Vertices[b];var pc=copy.Vertices[c];
            var na=Near(pa);var nb=Near(pb);var nc=Near(pc);
            var roles=new List<(string Kind,int Face)>();
            foreach(var (face,front) in na)
            {
                bool inB=nb.Contains((face,front)),inC=nc.Contains((face,front));
                if(inB&&inC)roles.Add((front?"front":"back",face));
            }
            foreach(var face in na.Select(x=>x.Face).Distinct())
            {
                bool Has(List<(int Face,bool Front)> l)=>l.Any(x=>x.Face==face);
                if(!Has(nb)||!Has(nc))continue;
                if(roles.Any(r=>r.Face==face))continue;
                roles.Add(("side",face));
            }
            var mid0=(pa+pb+pc)/3;
            bool FrontShadowed(int face)=>Shadowed(mid0,mesh.Normals[face],face);
            bool BackShadowed(int face)=>Shadowed(mid0+mesh.Normals[face]*.001f,-mesh.Normals[face],face);
            bool Sure((string Kind,int Face) r)
            {
                if(!IsLayer(r.Face))return false;
                return r.Kind=="front"?FrontShadowed(r.Face):r.Kind=="back"?BackShadowed(r.Face):WallCovers(pa,pb,pc);
            }
            bool WallCovers(Vector3 x,Vector3 y,Vector3 z)
            {
                for(int w=0;w<n;w++)
                {
                    if(!IsWall(w))continue;var corners=mesh.Polygons[w];
                    int matched=(corners.Any(q=>Vector3.Distance(q,x)<=MatchM)?1:0)+(corners.Any(q=>Vector3.Distance(q,y)<=MatchM)?1:0)+(corners.Any(q=>Vector3.Distance(q,z)<=MatchM)?1:0);
                    if(matched==3)return true;
                    // two points on the wall and the third within 5 mm of it (the back corner of a layer is computed with whole millimetres)
                    if(matched==2&&Distance((x+y+z)/3,corners)<=.005)return true;
                }
                return false;
            }
            bool hide=roles.Count>0&&roles.All(Sure);
            string kind=roles.Count==0?"unknown":roles[0].Kind;int owner=roles.Count==0?-1:roles[0].Face;
            if(hide)
            {
                for(int k=0;k<3;k++){triangles.Add(vertices.Count);vertices.Add(pa);normals.Add(copy.Normals[a]);uv.Add(copy.Uv[a]);}
                report?.Roles.Add(kind);report?.Faces.Add(owner);report?.Drawn.Add(false);report?.Colour.Add(float.NaN);continue;
            }
            // the colour
            Func<Vector3,float,float?>? map=null;
            // several roles can share one place (the back of one layer touches the front of the next): the outermost front, then the innermost back, then the first
            var layerRoles=roles.Where(r=>IsLayer(r.Face)).ToList();
            (string Kind,int Face)? layerRole=layerRoles.Cast<(string Kind,int Face)?>().FirstOrDefault(r=>r!.Value.Kind=="front"&&!FrontShadowed(r.Value.Face))
                ??layerRoles.Cast<(string Kind,int Face)?>().FirstOrDefault(r=>r!.Value.Kind=="back"&&!BackShadowed(r.Value.Face))
                ??layerRoles.Cast<(string Kind,int Face)?>().FirstOrDefault();
            var wallRole=roles.Cast<(string Kind,int Face)?>().FirstOrDefault(r=>IsWall(r!.Value.Face)&&r.Value.Kind=="front");
            if(layerRole is {} lr&&lr.Kind=="front"){var g=layout.Groups[FrontShadowed(lr.Face)?ranks[lr.Face]:0];map=(p,d)=>(float)(g.StartFraction+d*(g.EndFraction-g.StartFraction));kind="front";owner=lr.Face;}
            else if(layerRole is {} lb&&lb.Kind=="back"){var g=layout.Groups[BackShadowed(lb.Face)?ranks[lb.Face]:G-1];map=(p,d)=>(float)(g.StartFraction+d*(g.EndFraction-g.StartFraction));kind="back";owner=lb.Face;}
            else if(wallRole is {} wr&&Along(mesh,wr.Face,fractions) is {} along){map=(p,d)=>(float)along.Map(p);kind="wall";owner=wr.Face;}
            else if(layerRole is {} ls){var g=layout.Groups[ranks[ls.Face]];map=(p,d)=>(float)(g.StartFraction+d*(g.EndFraction-g.StartFraction));kind="side";owner=ls.Face;}
            else
            {
                // fits no face: the colour of the nearest layer face
                int best=-1;double bd=double.PositiveInfinity;var mid=(pa+pb+pc)/3;
                for(int i=0;i<n;i++)if(IsLayer(i)){double d=Math.Min(Distance(mid,mesh.Polygons[i]),Distance(mid,mesh.Backs[i]));if(d<bd){bd=d;best=i;}}
                if(best>=0){var g=layout.Groups[ranks[best]];map=(p,d)=>(float)(g.StartFraction+Math.Clamp(float.IsNaN(d)?0f:d,0,1)*(g.EndFraction-g.StartFraction));}
                if(roles.Count==0){kind="unknown";owner=best;} // a role without a layer (the inner side of a wall) keeps its own name; only a triangle that fits no face is "unknown"
            }
            float shown=0;
            foreach(var (i,p,d) in new[]{(a,pa,copy.Uv[a].Y),(b,pb,copy.Uv[b].Y),(c,pc,copy.Uv[c].Y)})
            {
                var value=map?.Invoke(p,d)??d;shown=value;triangles.Add(vertices.Count);vertices.Add(p);normals.Add(copy.Normals[i]);uv.Add(new(copy.Uv[i].X,value));
            }
            report?.Roles.Add(kind);report?.Faces.Add(owner);report?.Drawn.Add(true);report?.Colour.Add(shown);
        }
        return new(vertices.ToArray(),triangles.ToArray(),normals.ToArray(),uv.ToArray()){FallbackTriangles=copy.FallbackTriangles,FallbackArea=copy.FallbackArea,SideArea=copy.SideArea};
    }
    /// <summary>
    /// <paramref name="painted"/> (0.22.66, setting Layer box look = painted): only the outside of the block is drawn: the front of the first layer and the back of the last layer in the colour of that layer,
    /// and the side walls, each strip in the colour of the layer it spans (a strip over air shows the dark air colour); the loose layer faces inside are not drawn, nor the sides of a layer that has a wall
    /// in front of it. A side without a wall keeps the layers themselves (the look of 0.22.65). Every triangle stays (zero area) so the native coverage check still passes.
    /// </summary>
    internal static OverlayCopy Remap(OverlayCopy copy,LayerMesh mesh,IReadOnlyList<int> ranks,LayerLayout layout,bool painted=false)
    {
        if(painted)return RemapPainted(copy,mesh,ranks,layout);
        var fractions=mesh.Fractions(ranks,layout);
        var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        for(int t=0;t+2<copy.Triangles.Length;t+=3)
        {
            int a=copy.Triangles[t],b=copy.Triangles[t+1],c=copy.Triangles[t+2];
            var pa=copy.Vertices[a];var pb=copy.Vertices[b];var pc=copy.Vertices[c];var centre=(pa+pb+pc)/3;
            void Collapse(){for(int k=0;k<3;k++){triangles.Add(vertices.Count);vertices.Add(pa);normals.Add(copy.Normals[a]);uv.Add(copy.Uv[a]);}}
            float da=copy.Uv[a].Y,db=copy.Uv[b].Y,dc=copy.Uv[c].Y;
            int part=da==0&&db==0&&dc==0?0:da==1&&db==1&&dc==1?1:2;
            // Coincident triangles (the end cap shared by two neighbouring strips) cannot be told apart by position: a triangle that also belongs to a face
            // that is not empty space is kept and shows that face; only a triangle that belongs to nothing but strips over air is left out.
            int owner=-1;bool onlyAir=false;
            foreach(var candidate in Owners(mesh,centre,part))
            {
                // 0.22.63: no side wall is drawn (the layers show through them); they stay in the mesh for the hit, the weight and the report.
                if(candidate<ranks.Count&&ranks[candidate]==LayerHit.SideWallRank){if(owner<0)onlyAir=true;continue;}
                owner=candidate;onlyAir=false;break;
            }
            if(onlyAir){Collapse();continue;} // only side walls here: not drawn (0.22.62: kept as a zero-area triangle so the native-coverage check still sees every triangle)
            Func<Vector3,float,float?> map=(p,d)=>d;
            if(owner>=0&&owner<ranks.Count&&ranks[owner]>=0&&ranks[owner]<layout.Groups.Length)
            {
                var g=layout.Groups[ranks[owner]];map=(p,d)=>(float)(g.StartFraction+d*(g.EndFraction-g.StartFraction));
            }
            else if(owner>=0&&owner<ranks.Count&&ranks[owner]==LayerHit.SideWallRank&&Along(mesh,owner,fractions) is {} along)
            {
                double mid=(along.From+along.To)/2;
                if(!layout.Groups.Any(g=>mid>g.StartFraction&&mid<g.EndFraction)){Collapse();continue;} // a strip over an air layer: empty space
                map=(p,d)=>(float)along.Map(p);
            }
            foreach(var (i,p,d) in new[]{(a,pa,da),(b,pb,db),(c,pc,dc)})
            {
                triangles.Add(vertices.Count);vertices.Add(p);normals.Add(copy.Normals[i]);uv.Add(new(copy.Uv[i].X,map(p,d)??d));
            }
        }
        return new(vertices.ToArray(),triangles.ToArray(),normals.ToArray(),uv.ToArray()){FallbackTriangles=copy.FallbackTriangles,FallbackArea=copy.FallbackArea,SideArea=copy.SideArea};
    }
    /// <summary>A side wall strip along its ribs: the recipe fractions at its two ends and the axis between them.</summary>
    /// <summary>A strip of a side wall between its two ribs: each rib runs from a corner at the strip's first fraction to the next corner at its last; a point takes the share along both ribs, weighted by its distance to each.</summary>
    private static (double From,double To,Func<Vector3,double> Map)? Along(LayerMesh mesh,int face,Dictionary<int,double> fractions)
    {
        var ids=mesh.Ids[face];var p=mesh.Polygons[face];int n=ids.Length;if(n!=4||ids.Any(i=>!fractions.ContainsKey(i)))return null;
        double lo=ids.Min(i=>fractions[i]),hi=ids.Max(i=>fractions[i]);if(hi-lo<1e-9)return null;
        var ribs=new List<(Vector3 A,Vector3 B)>();
        for(int j=0;j<n;j++)
        {
            int k=(j+1)%n;double fj=fractions[ids[j]],fk=fractions[ids[k]];
            if(Math.Abs(fj-lo)<1e-9&&Math.Abs(fk-hi)<1e-9)ribs.Add((p[j],p[k]));else if(Math.Abs(fj-hi)<1e-9&&Math.Abs(fk-lo)<1e-9)ribs.Add((p[k],p[j]));
        }
        if(ribs.Count!=2)return null;
        double Share((Vector3 A,Vector3 B) r,Vector3 q,out double distance)
        {
            var axis=r.B-r.A;var s=Math.Clamp(Vector3.Dot(q-r.A,axis)/axis.LengthSquared(),0,1);distance=Vector3.Distance(q,r.A+axis*s);return s;
        }
        return (lo,hi,q=>{var s0=Share(ribs[0],q,out var d0);var s1=Share(ribs[1],q,out var d1);var w=d0+d1<1e-9?.5:d0/(d0+d1);return lo+((1-w)*s0+w*s1)*(hi-lo);});
    }
    /// <summary>The face a native overlay triangle belongs to: the front polygon (part 0), the back polygon (part 1) or a side quad (part 2) closest to its centre.</summary>
    internal static int Owner(LayerMesh mesh,Vector3 point,int part)
    {
        int best=-1;double distance=ToleranceM;
        for(int i=0;i<mesh.Polygons.Length;i++)
        {
            var f=mesh.Polygons[i];var b=mesh.Backs[i];if(f.Length<3||b.Length!=f.Length)continue;
            if(part==0)Check(f);else if(part==1)Check(b);
            else for(int j=0;j<f.Length;j++)Check(new[]{f[j],f[(j+1)%f.Length],b[(j+1)%f.Length],b[j]});
            void Check(Vector3[] polygon){var d=Distance(point,polygon);if(d<distance){distance=d;best=i;}}
        }
        return best;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LayerMesh,(Vector3,Vector3)[]> boxes=new();
    /// <summary>The box around a face (front and real back points), cached per mesh: the owner search skips every face the point is not near.</summary>
    private static (Vector3,Vector3) Bounds(LayerMesh mesh,int face)
    {
        var table=boxes.GetValue(mesh,m=>m.Polygons.Select((f,i)=>f.Length<3||m.Backs[i].Length!=f.Length?(Vector3.Zero,Vector3.Zero):(f.Concat(m.Backs[i]).Aggregate(Vector3.Min),f.Concat(m.Backs[i]).Aggregate(Vector3.Max))).ToArray());
        return table[face];
    }
    /// <summary>Every face with a polygon of that part within the tolerance of the point (coincident polygons, such as the end cap two neighbouring strips share), the nearest first.</summary>
    internal static List<int> Owners(LayerMesh mesh,Vector3 point,int part)
    {
        var found=new List<(int Face,double Distance)>();
        for(int i=0;i<mesh.Polygons.Length;i++)
        {
            var f=mesh.Polygons[i];var b=mesh.Backs[i];if(f.Length<3||b.Length!=f.Length)continue;
            var (lo,hi)=Bounds(mesh,i);if(point.X<lo.X-ToleranceM||point.Y<lo.Y-ToleranceM||point.Z<lo.Z-ToleranceM||point.X>hi.X+ToleranceM||point.Y>hi.Y+ToleranceM||point.Z>hi.Z+ToleranceM)continue;
            double d=double.PositiveInfinity;
            if(part==0)d=Distance(point,f);else if(part==1)d=Distance(point,b);
            else for(int j=0;j<f.Length;j++)d=Math.Min(d,Distance(point,new[]{f[j],f[(j+1)%f.Length],b[(j+1)%f.Length],b[j]}));
            if(d<ToleranceM)found.Add((i,d));
        }
        return found.OrderBy(x=>x.Distance).ThenBy(x=>x.Face).Select(x=>x.Face).ToList();
    }
    /// <summary>Distance from a point to a (fan-triangulated) polygon.</summary>
    internal static double Distance(Vector3 p,Vector3[] polygon)
    {
        double best=double.PositiveInfinity;
        for(int j=1;j+1<polygon.Length;j++)best=Math.Min(best,TriangleDistance(p,polygon[0],polygon[j],polygon[j+1]));
        // 0.22.67: a quad that is not planar (a kneaded old face, the layers of a slanted piece) is cut by the native tessellator along either diagonal; the two cuts lie up to
        // several millimetres apart, so the point counts as on the face when it lies on either of them.
        if(polygon.Length==4)
        {
            best=Math.Min(best,TriangleDistance(p,polygon[1],polygon[2],polygon[3]));best=Math.Min(best,TriangleDistance(p,polygon[1],polygon[3],polygon[0]));
        }
        return best;
    }
    private static double TriangleDistance(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
    {
        var ab=b-a;var ac=c-a;var ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);if(d1<=0&&d2<=0)return Vector3.Distance(p,a);
        var bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);if(d3>=0&&d4<=d3)return Vector3.Distance(p,b);
        float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0){float v=d1/(d1-d3);return Vector3.Distance(p,a+ab*v);}
        var cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);if(d6>=0&&d5<=d6)return Vector3.Distance(p,c);
        float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0){float w=d2/(d2-d6);return Vector3.Distance(p,a+ac*w);}
        float va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0){float w=(d4-d3)/((d4-d3)+(d5-d6));return Vector3.Distance(p,b+(c-b)*w);}
        float denom=1/(va+vb+vc);float vv=vb*denom,ww=vc*denom;return Vector3.Distance(p,a+ab*vv+ac*ww);
    }
}

/// <summary>
/// 0.22.61 weight of a plate in layers: the density that belongs to its real faces, sum(volume of the face x density of its layer or block, side walls as
/// steel, faces without a layer at the recipe density) / sum(volume). The game multiplies it with its own armour volume, so the weight follows the faces,
/// also after edits by hand.
/// </summary>
internal static class LayerWeight
{
    internal static (double Density,double MassKg,double VolumeM3) Of(LayerMesh mesh,IReadOnlyList<int> ranks,LayerLayout layout,CassettePreset preset,Func<string,double> density,double recipeDensity)
    {
        double mass=0,volume=0;
        var groupDensity=layout.Groups.Select(g=>g.Layers.Sum(i=>density(preset.Layers[i].Material)*LayerLayout.LayerDepth(preset.Layers[i]))/g.DepthMm).ToArray();
        for(int i=0;i<mesh.Polygons.Length;i++)
        {
            var f=mesh.Polygons[i];var b=mesh.Backs[i];if(f.Length<3||b.Length!=f.Length)continue;
            var v=Math.Abs(ComposerFill.PrismVolume(f,b));if(!double.IsFinite(v))continue;
            int r=i<ranks.Count?ranks[i]:-1;
            double rho=r>=0&&r<groupDensity.Length?groupDensity[r]:r==LayerHit.SideWallRank?density("rha"):recipeDensity;
            mass+=v*rho;volume+=v;
        }
        return (volume>0?mass/volume:recipeDensity,mass,volume);
    }
}

/// <summary>0.22.62: what the game builds from a mesh JSON (native back points per tm value, ALONG-EDGE-FORMULA) against the old outer shape.</summary>
internal static class LayerShape
{
    /// <summary>Distance (metres) of a point outside the closed surface of the old faces: 0 when inside (majority of three ray parities) or on it.</summary>
    internal static double OutsideDistance(Vector3 point,IReadOnlyList<Vector3[]> polygons)
    {
        double best=double.PositiveInfinity;var tris=new List<FillTriangle>();
        foreach(var poly in polygons)for(int k=1;k+1<poly.Length;k++){tris.Add(new(poly[0],poly[k],poly[k+1]));best=Math.Min(best,LayerOverlay.Distance(point,new[]{poly[0],poly[k],poly[k+1]}));}
        if(best<1e-9)return 0;
        int votes=0;
        foreach(var d in new[]{new Vector3(.5773f,.5779f,.5768f),new Vector3(-.3f,.9f,.31f),new Vector3(.2f,-.4f,.89f)})
        {var dir=Vector3.Normalize(d);int c=0;foreach(var tr in tris)if(ComposerFill.Ray(point,dir,new[]{tr},100) is {} h&&h>0)c++;if(c%2==1)votes++;}
        return votes>=2?0:best;
    }
    /// <summary>Largest distance (mm) of any front or native back point of the mesh outside the old shape, and where. tm 0 or an invalid tm 4: along the normal; tm 4: along the stored edge; other tm (Auto): along the normal (the game decides at run time).</summary>
    internal static (double Mm,string Where) Deviation(IReadOnlyList<Vector3[]> oldShape,string json,Func<int,double>? limitMm=null)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        var v=root.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();Vector3 P(int i)=>new(v[3*i],v[3*i+1],v[3*i+2]);
        var edges=root.TryGetProperty("edges",out var e)?e.EnumerateArray().Select(x=>x.GetInt32()).ToArray():Array.Empty<int>();
        double worst=0;string where="";int index=0;
        foreach(var f in root.GetProperty("faces").EnumerateArray())
        {
            var id=f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray();var t=f.TryGetProperty("t",out var tv)?tv.EnumerateArray().Select(x=>(int)x.GetUInt16()).ToArray():new int[id.Length];
            ulong tm=f.TryGetProperty("tm",out var tmv)&&tmv.ValueKind==JsonValueKind.Number?JsonBits.Tm(tmv):0,te=f.TryGetProperty("te",out var tev)&&tev.ValueKind==JsonValueKind.Number?JsonBits.Te(tev):0;
            var p=id.Select(P).ToArray();var nn=CheekShellFill.Newell(p);var n=nn.LengthSquared()<1e-14f?Vector3.Zero:Vector3.Normalize(nn);
            for(int j=0;j<p.Length;j++)
            {
                var back=p[j]-n*(t[j]*.001f);
                if(((tm>>(8*j))&0xff)==CheekShellFill.AlongEdgeManual)
                {
                    int edge=(int)((te>>(16*j))&0xffff);
                    if(2*edge+1<edges.Length&&(edges[2*edge]==id[j]||edges[2*edge+1]==id[j]))
                    {int w=edges[2*edge]==id[j]?edges[2*edge+1]:edges[2*edge];if((P(w)-p[j]).LengthSquared()>1e-14f)back=CheekShellFill.NativeBack(p[j],p[(j+1)%p.Length],p[(j+p.Length-1)%p.Length],Vector3.Normalize(P(w)-p[j]),(ushort)t[j],n);}
                }
                foreach(var (kind,pt) in new[]{("front",p[j]),("back",back)}){var d=OutsideDistance(pt,oldShape)*1000-(limitMm?.Invoke(index)??0);if(d>worst){worst=d;where=$"face {index+1}, corner {j+1}, {kind} point";}}
            }
            index++;
        }
        return (worst,where);
    }
}

/// <summary>
/// 0.22.65: tm and te as the game writes them. The game stores tm as a signed 32 bit number (one byte per corner, at most four corners; a corner on Lengthways 0x80 makes it negative)
/// and te as a signed 64 bit number (sixteen bits per corner). A negative number is the same bit pattern read with its sign; reading it as an unsigned number throws a FormatException.
/// Only the code for layers reads this way; the older buttons keep refusing negative numbers on purpose.
/// </summary>
internal static class JsonBits
{
    internal static ulong Tm(JsonElement e)
    {
        if(e.TryGetUInt64(out var u))return u;
        if(e.TryGetInt32(out var i))return (uint)i;
        throw new FormatException("tm does not fit in 32 bits.");
    }
    internal static ulong Te(JsonElement e)
    {
        if(e.TryGetUInt64(out var u))return u;
        if(e.TryGetInt64(out var l))return unchecked((ulong)l);
        throw new FormatException("te does not fit in 64 bits.");
    }
}

/// <summary>0.22.68: points and edges of the output before and after the clean-up; helper points are the points that serve only as the end of a rib.</summary>
internal sealed record LayerCleanup(string Json,int[] VertexMap,Dictionary<int,int> EdgeMap,int VerticesBefore,int VerticesAfter,int EdgesBefore,int EdgesAfter,int Helpers,int Merged);

/// <summary>
/// 0.22.68: removes from a mesh JSON every edge that is not an edge of a face and not the thickness rib (te, tm 4) of a corner, every point that is not a corner of a face and not an end of such
/// an edge, and merges points that lie on the same spot (within 1 micrometre). Faces keep their order; v and te are renumbered. Raw faces (kept as they were) keep every edge their te names.
/// </summary>
internal static class LayerCompact
{
    internal static LayerCleanup Run(string json,IReadOnlyList<int> rawFaces)
    {
        var node=System.Text.Json.Nodes.JsonNode.Parse(json)!;var v=node["vertices"]!.AsArray();var e=node["edges"]!.AsArray();var faces=node["faces"]!.AsArray();
        int nv=v.Count/3,ne=e.Count/2;var pos=new Vector3[nv];for(int i=0;i<nv;i++)pos[i]=new(v[3*i]!.GetValue<float>(),v[3*i+1]!.GetValue<float>(),v[3*i+2]!.GetValue<float>());
        var edge=new (int A,int B)[ne];for(int i=0;i<ne;i++)edge[i]=(e[2*i]!.GetValue<int>(),e[2*i+1]!.GetValue<int>());
        var flags=node["edgeFlags"]?.AsArray();
        var raw=new HashSet<int>(rawFaces);
        // 1. merge points on one spot
        var canon=Enumerable.Range(0,nv).ToArray();var seen=new Dictionary<(long,long,long),int>();int merged=0;
        for(int i=0;i<nv;i++)
        {
            var key=((long)Math.Round(pos[i].X*1e6),(long)Math.Round(pos[i].Y*1e6),(long)Math.Round(pos[i].Z*1e6));
            if(seen.TryGetValue(key,out var first)){canon[i]=first;merged++;}else seen[key]=i;
        }
        var faceVerts=new List<int[]>();var faceTm=new ulong[faces.Count];var faceTe=new ulong[faces.Count];
        for(int f=0;f<faces.Count;f++)
        {
            faceVerts.Add(faces[f]!["v"]!.AsArray().Select(x=>canon[x!.GetValue<int>()]).ToArray());
            faceTm[f]=faces[f]!["tm"] is {} tmn?JsonBits.Tm(System.Text.Json.JsonSerializer.SerializeToElement(tmn.GetValue<long>())):0;
            faceTe[f]=faces[f]!["te"] is {} ten?JsonBits.Te(System.Text.Json.JsonSerializer.SerializeToElement(ten.GetValue<long>())):0;
        }
        // 2. edges after the merge: duplicates and degenerate edges map to one edge
        var edgeKey=new Dictionary<(int,int),int>();var edgeCanon=new int[ne];var keepEdge=new bool[ne];
        for(int i=0;i<ne;i++)
        {
            var a=canon[edge[i].A];var b=canon[edge[i].B];if(a==b){edgeCanon[i]=-1;continue;}
            var k=(Math.Min(a,b),Math.Max(a,b));if(edgeKey.TryGetValue(k,out var first)){edgeCanon[i]=first;}else{edgeKey[k]=i;edgeCanon[i]=i;}
        }
        var used=new bool[ne];
        foreach(var vs in faceVerts)for(int j=0;j<vs.Length;j++)
        {
            var a=vs[j];var b=vs[(j+1)%vs.Length];var k=(Math.Min(a,b),Math.Max(a,b));if(edgeKey.TryGetValue(k,out var ei))used[ei]=true;
        }
        for(int f=0;f<faces.Count;f++)
            for(int j=0;j<faceVerts[f].Length&&j<4;j++)
            {
                bool rib=((faceTm[f]>>(8*j))&0xff)==CheekShellFill.AlongEdgeManual;if(!rib&&!raw.Contains(f))continue;
                int idx=(int)((faceTe[f]>>(16*j))&0xffff);if(idx<ne&&edgeCanon[idx]>=0)used[edgeCanon[idx]]=true;
            }
        // 3. points: corners of faces and ends of kept edges
        var usedV=new bool[nv];foreach(var vs in faceVerts)foreach(var x in vs)usedV[x]=true;
        for(int i=0;i<ne;i++)if(used[i]){usedV[canon[edge[i].A]]=true;usedV[canon[edge[i].B]]=true;}
        int helpers=0;var cornerOf=new bool[nv];foreach(var vs in faceVerts)foreach(var x in vs)cornerOf[x]=true;
        for(int i=0;i<nv;i++)if(usedV[i]&&!cornerOf[i]&&canon[i]==i)helpers++;
        var vmap=new int[nv];var newPos=new List<Vector3>();var vnew=new int[nv];Array.Fill(vnew,-1);
        for(int i=0;i<nv;i++)if(usedV[i]&&canon[i]==i){vnew[i]=newPos.Count;newPos.Add(pos[i]);}
        for(int i=0;i<nv;i++)vmap[i]=vnew[canon[i]];
        var emap=new Dictionary<int,int>();var newEdges=new List<int>();var newFlags=new List<int>();
        for(int i=0;i<ne;i++)
            if(used[i]){emap[i]=newEdges.Count/2;newEdges.Add(vmap[edge[i].A]);newEdges.Add(vmap[edge[i].B]);newFlags.Add(flags!=null&&i<flags.Count?flags[i]!.GetValue<int>():0);}
        for(int i=0;i<ne;i++)if(!used[i]&&edgeCanon[i]>=0&&used[edgeCanon[i]])emap[i]=emap[edgeCanon[i]];
        // 4. write
        var arr=new System.Text.Json.Nodes.JsonArray();foreach(var p in newPos){arr.Add(p.X);arr.Add(p.Y);arr.Add(p.Z);}node["vertices"]=arr;
        var ea=new System.Text.Json.Nodes.JsonArray();foreach(var x in newEdges)ea.Add(x);node["edges"]=ea;
        if(flags!=null){var fa=new System.Text.Json.Nodes.JsonArray();foreach(var x in newFlags)fa.Add(x);node["edgeFlags"]=fa;}
        for(int f=0;f<faces.Count;f++)
        {
            var fo=faces[f]!.AsObject();var va=new System.Text.Json.Nodes.JsonArray();foreach(var x in faceVerts[f])va.Add(vmap[x]);fo["v"]=va;
            ulong te=0;
            for(int j=0;j<faceVerts[f].Length&&j<4;j++)
            {
                int idx=(int)((faceTe[f]>>(16*j))&0xffff);bool rib=((faceTm[f]>>(8*j))&0xff)==CheekShellFill.AlongEdgeManual;
                int mapped=(rib||raw.Contains(f))&&emap.TryGetValue(idx,out var me)?me:0;te|=(ulong)(uint)mapped<<(16*j);
            }
            if(fo["te"]!=null)fo["te"]=unchecked((long)te);
        }
        var vertexMap=Enumerable.Range(0,nv).Select(i=>vmap[i]).ToArray();
        return new LayerCleanup(node.ToJsonString(),vertexMap,emap,nv,newPos.Count,ne,newEdges.Count/2,helpers,merged);
    }
}

/// <summary>
/// 0.22.68: what the game drew for a plate in layers and how the overlay sorted it, as one JSON document for troubleshooting (BepInEx\config\sprocket.expandedballistics.layer-overlay\plate-NNN.json):
/// the points and triangles of the native copy as the mod received them, per triangle the role found, the face, whether it is drawn and the depth it shows; per face of the mesh its kind, rank and
/// piece, the points, the thickness, tm and te of every corner, the rib used, the angle between rib and face, whether the native formula falls back to the normal (rib less than 1 degree behind the face)
/// and the back points the mod predicts; and the counts. Kept below about 5 MB: a copy with more than 150000 points writes only the counts and the faces.
/// </summary>
internal static class LayerOverlayDump
{
    private static System.Text.Json.Nodes.JsonArray Arr(params float[] x){var a=new System.Text.Json.Nodes.JsonArray();foreach(var v in x)a.Add(v);return a;}
    internal static string Build(string meshJson,LayerMesh mesh,IReadOnlyList<int> ranks,LayerLayout layout,OverlayCopy copy,LayerOverlay.OverlayReport? report,string look,long plate,string recipe)
    {
        using var doc=JsonDocument.Parse(meshJson);var root=doc.RootElement;
        var vertices=root.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();var edges=root.TryGetProperty("edges",out var ev)?ev.EnumerateArray().Select(x=>x.GetInt32()).ToArray():Array.Empty<int>();
        Vector3 P(int i)=>new(vertices[3*i],vertices[3*i+1],vertices[3*i+2]);
        var o=new System.Text.Json.Nodes.JsonObject();
        o["version"]="0.22.69";o["plate"]=plate;o["recipe"]=recipe;o["look"]=look;o["layers"]=layout.Groups.Length;
        var faces=new System.Text.Json.Nodes.JsonArray();var kinds=new int[3];
        // pieces by the direction of the layer faces
        var pieceNormals=new List<Vector3>();
        int Piece(int i){for(int p=0;p<pieceNormals.Count;p++)if(Vector3.Dot(pieceNormals[p],mesh.Normals[i])>.97f)return p;pieceNormals.Add(mesh.Normals[i]);return pieceNormals.Count-1;}
        int fi=0;
        foreach(var f in root.GetProperty("faces").EnumerateArray())
        {
            var fo=new System.Text.Json.Nodes.JsonObject();int rank=fi<ranks.Count?ranks[fi]:-1;
            string kind=rank>=0&&rank<layout.Groups.Length?"layer":rank==LayerHit.SideWallRank?"wall":"other";kinds[kind=="layer"?0:kind=="wall"?1:2]++;
            fo["i"]=fi;fo["kind"]=kind;fo["rank"]=rank;if(kind=="layer")fo["piece"]=Piece(fi);
            var ids=f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray();fo["v"]=new System.Text.Json.Nodes.JsonArray(ids.Select(x=>(System.Text.Json.Nodes.JsonNode)x).ToArray());
            var t=f.TryGetProperty("t",out var tv)?tv.EnumerateArray().Select(x=>(int)x.GetUInt16()).ToArray():new int[ids.Length];fo["t"]=new System.Text.Json.Nodes.JsonArray(t.Select(x=>(System.Text.Json.Nodes.JsonNode)x).ToArray());
            ulong tm=f.TryGetProperty("tm",out var tmv)&&tmv.ValueKind==JsonValueKind.Number?JsonBits.Tm(tmv):0,te=f.TryGetProperty("te",out var tev)&&tev.ValueKind==JsonValueKind.Number?JsonBits.Te(tev):0;
            var pts=ids.Select(P).ToArray();var nn=CheekShellFill.Newell(pts);var n=nn.LengthSquared()<1e-14f?Vector3.Zero:Vector3.Normalize(nn);
            fo["normal"]=Arr(n.X,n.Y,n.Z);
            var corners=new System.Text.Json.Nodes.JsonArray();
            for(int j=0;j<ids.Length&&j<4;j++)
            {
                var c=new System.Text.Json.Nodes.JsonObject();int mode=(int)((tm>>(8*j))&0xff);int edge=(int)((te>>(16*j))&0xffff);
                c["tm"]=mode;c["te"]=edge;
                if(mode==CheekShellFill.AlongEdgeManual&&2*edge+1<edges.Length&&(edges[2*edge]==ids[j]||edges[2*edge+1]==ids[j]))
                {
                    int w=edges[2*edge]==ids[j]?edges[2*edge+1]:edges[2*edge];var u=Vector3.Normalize(P(w)-pts[j]);float d=Vector3.Dot(n,u);
                    c["ribEnd"]=w;c["ribEndPoint"]=Arr(P(w).X,P(w).Y,P(w).Z);c["angleToFaceDeg"]=Math.Round(Math.Asin(Math.Clamp(-d,-1,1))*180/Math.PI,2);c["fallsBackToNormal"]=d>-0.0174524f;
                }
                else if(mode==CheekShellFill.AlongEdgeManual)c["ribNotUsable"]=true;
                if(fi<mesh.Backs.Length&&j<mesh.Backs[fi].Length){var b=mesh.Backs[fi][j];c["predictedBack"]=Arr(b.X,b.Y,b.Z);}
                corners.Add(c);
            }
            fo["corners"]=corners;faces.Add(fo);fi++;
        }
        o["faces"]=faces;
        var counts=new System.Text.Json.Nodes.JsonObject();counts["layerFaces"]=kinds[0];counts["walls"]=kinds[1];counts["otherFaces"]=kinds[2];counts["meshVertices"]=vertices.Length/3;counts["meshEdges"]=edges.Length/2;
        var used=new HashSet<int>();foreach(var f in root.GetProperty("faces").EnumerateArray())foreach(var x in f.GetProperty("v").EnumerateArray())used.Add(x.GetInt32());
        counts["meshVerticesInNoFace"]=vertices.Length/3-used.Count;counts["copyVertices"]=copy.Vertices.Length;counts["copyTriangles"]=copy.Triangles.Length/3;
        counts["copyDistinctPoints"]=copy.Vertices.Select(v=>((long)Math.Round(v.X*1e5),(long)Math.Round(v.Y*1e5),(long)Math.Round(v.Z*1e5))).Distinct().Count();
        if(report!=null)
        {
            var byRole=new System.Text.Json.Nodes.JsonObject();
            foreach(var g in Enumerable.Range(0,report.Roles.Count).GroupBy(i=>report.Roles[i]+(report.Drawn[i]?" drawn":" left out")))byRole[g.Key]=g.Count();
            counts["trianglesByRole"]=byRole;
        }
        o["counts"]=counts;
        if(copy.Vertices.Length<=150000)
        {
            string R(float x)=>Math.Round(x,5).ToString(CultureInfo.InvariantCulture);
            o["copyPoints"]=string.Join(",",copy.Vertices.Select(v=>R(v.X)+","+R(v.Y)+","+R(v.Z)));
            o["copyTriangles"]=string.Join(",",copy.Triangles);
            o["copyUvDepth"]=string.Join(",",copy.Uv.Select(v=>R(v.Y)));
            if(report!=null)
            {
                var legend=report.Roles.Distinct().ToList();o["roleLegend"]=new System.Text.Json.Nodes.JsonArray(legend.Select(x=>(System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(x)!).ToArray());
                o["triangleRole"]=string.Join(",",report.Roles.Select(x=>legend.IndexOf(x)));o["triangleFace"]=string.Join(",",report.Faces);o["triangleDrawn"]=string.Join("",report.Drawn.Select(x=>x?"1":"0"));
                o["triangleDepth"]=string.Join(",",report.Colour.Select(x=>float.IsNaN(x)?"-":R(x)));
            }
        }
        return o.ToJsonString();
    }
}

// ======== 0.22.71 terug naar de doos (pure) ========
/// <summary>What "Back to box" made of a plate in layers: the mesh JSON of the box and what it holds.</summary>
internal sealed record LayerBoxBack(string Json,int FrontFaces,int SideWalls,int BackFaces,bool OpenBack,int Points,int Edges,string Summary);

internal static partial class LayerFaces
{
    internal const string BackToBoxAdvice="Undo the edit, or shape a new box.";
    /// <summary>The thickness a new composite plate gets (mm); the conversion to layers ignores the thickness of a box.</summary>
    internal const int BoxFaceThicknessMm=20;
    private static InvalidOperationException Refuse(string why)=>new($"Back to box: {why}. Nothing changed. {BackToBoxAdvice}");

    /// <summary>
    /// 0.22.71: a plate in layers back to its outer faces. The front is the front faces of the first layer of every piece (on their present points), the sides are one face per original side
    /// (the strips of its layers joined between the front rim and the back rim), the back (only when a front edge has no side wall, so the box would not be a box without it) is the real back of the
    /// last layers, turned outward. Nothing else: no point and no edge that serves nothing. The faces are recognised as after saving and loading (ranks and walls from the mesh itself); points
    /// moved on an inner layer are not read. A mesh that cannot be brought back with certainty (a layer face deleted, a face that is neither layer nor wall, a recipe that does not fit) is refused.
    /// </summary>
    internal static LayerBoxBack ToBox(string json,LayerLayout layout,Vector3 forward,Func<string,string?>? verify=null)
    {
        LayerMesh mesh;int[] ranks;
        try{mesh=LayerMesh.Read(json);ranks=LayerRanks.FromJson(json);}catch(Exception){throw Refuse("the plate could not be read");}
        int n=mesh.Polygons.Length,G=layout.Groups.Length;
        if(ranks.Length!=n||G<1)throw Refuse("the plate could not be read");
        if(layout.Groups[0].StartFraction>1e-6||layout.Groups[^1].EndFraction<1-1e-6)throw Refuse("the recipe starts or ends with an air gap, so the front or back of the box cannot be found");
        var fronts=new List<int>();var lasts=new List<int>();var walls=new List<int>();int others=0;var perRank=new int[G];
        for(int i=0;i<n;i++)
        {
            if(mesh.Polygons[i].Length<3){others++;continue;}
            if(ranks[i]==LayerHit.SideWallRank)walls.Add(i);
            else if(ranks[i]>=0&&ranks[i]<G){perRank[ranks[i]]++;if(ranks[i]==0)fronts.Add(i);if(ranks[i]==G-1)lasts.Add(i);}
            else others++;
        }
        if(fronts.Count==0)throw Refuse("it has no layer faces");
        if(others>0)throw Refuse($"{others} faces are neither a layer nor a side wall (a layer face may have been deleted or changed)");
        if(perRank.Any(c=>c!=fronts.Count))throw Refuse("the layers are not complete (a layer face is missing in one of the stacks: faces per layer "+string.Join(",",perRank)+")");
        var vertices=RawVertices(json);Vector3 V(int id)=>vertices[id];
        var depth=fronts.Aggregate(Vector3.Zero,(a,i)=>a-mesh.Normals[i]);if(depth.LengthSquared()<1e-10f)throw Refuse("the front faces do not point one way");depth=Vector3.Normalize(depth);
        float Proj(Vector3 p)=>Vector3.Dot(p,depth);
        // wall strips of one original side: coplanar, share an edge across the side (both ends at the same place of the recipe: a rib joins two different places), one layer after the other
        var fractions=mesh.Fractions(ranks,layout);
        double Fraction(int id)=>fractions.TryGetValue(id,out var v)?v:double.NaN;
        var parent=walls.ToDictionary(w=>w,w=>w);int Find(int x){while(parent[x]!=x)x=parent[x]=parent[parent[x]];return x;}
        (float Lo,float Hi) Range(int w){var p=mesh.Polygons[w].Select(Proj).ToArray();return (p.Min(),p.Max());}
        foreach(var a in walls)foreach(var b in walls)
        {
            var shared=mesh.Ids[a].Intersect(mesh.Ids[b]).ToArray();
            if(b<=a||shared.Length<2)continue;
            bool across=false,known=false;
            for(int u=0;u<shared.Length&&!across;u++)for(int v=u+1;v<shared.Length;v++)
            {
                double fu=Fraction(shared[u]),fv=Fraction(shared[v]);
                if(double.IsNaN(fu)||double.IsNaN(fv))continue;known=true;if(Math.Abs(fu-fv)<1e-6){across=true;break;}
            }
            if(!known)
            {
                // no place in the recipe known: the strips follow each other along the depth
                var plane=Math.Abs(Vector3.Dot(mesh.Normals[a],mesh.Polygons[b].Aggregate(Vector3.Zero,(s2,q)=>s2+q)/mesh.Polygons[b].Length-mesh.Polygons[a][0]));
                var ra=Range(a);var rb=Range(b);float overlap=Math.Min(ra.Hi,rb.Hi)-Math.Max(ra.Lo,rb.Lo),shorter=Math.Min(ra.Hi-ra.Lo,rb.Hi-rb.Lo);
                across=Vector3.Dot(mesh.Normals[a],mesh.Normals[b])>=.9945f&&plane<=.001&&overlap<=.25f*shorter;
            }
            if(across)parent[Find(a)]=Find(b);
        }
        double Place(int w){var f=mesh.Ids[w].Select(Fraction).Where(x=>!double.IsNaN(x)).ToArray();if(f.Length>0)return f.Average();var r=Range(w);return (r.Lo+r.Hi)/2;}
        var sides=new List<int[]>();
        foreach(var column in walls.GroupBy(Find))
        {
            var strips=column.OrderBy(Place).ToArray();
            int FrontEdge(int w,bool back)
            {
                var p=mesh.Polygons[w];var ids=mesh.Ids[w];int k=p.Length;if(k!=4)throw Refuse("a side wall is not made of four-cornered strips");
                bool all=ids.All(i=>!double.IsNaN(Fraction(i)));int best=0;double score=back?double.MinValue:double.MaxValue;
                for(int e=0;e<k;e++)
                {
                    double m=all?(Fraction(ids[e])+Fraction(ids[(e+1)%k]))*.5:(Proj(p[e])+Proj(p[(e+1)%k]))*.5;
                    if(back?m>score:m<score){score=m;best=e;}
                }
                return best;
            }
            var first=strips[0];var last=strips[^1];int fe=FrontEdge(first,false),be=FrontEdge(last,true);
            var fid=mesh.Ids[first];var bid=mesh.Ids[last];
            if(first==last){sides.Add(fid.ToArray());continue;}
            int fl=fid[fe],fr=fid[(fe+1)%4];int ba=bid[be],bb=bid[(be+1)%4];
            // the back point under fr is the closer one
            bool straight=Vector3.Distance(V(bb),V(fr))+Vector3.Distance(V(ba),V(fl))<=Vector3.Distance(V(ba),V(fr))+Vector3.Distance(V(bb),V(fl));
            int br=straight?bb:ba,bl=straight?ba:bb;
            sides.Add(new[]{fl,fr,br,bl});
        }
        // is every outer edge of the front covered by a side wall? Then the box needs no back; otherwise it gets the real back of the last layers
        var frontUse=new Dictionary<(int,int),int>();
        foreach(var f in fronts){var v=mesh.Ids[f];for(int j=0;j<v.Length;j++){var k=(Math.Min(v[j],v[(j+1)%v.Length]),Math.Max(v[j],v[(j+1)%v.Length]));frontUse[k]=frontUse.GetValueOrDefault(k)+1;}}
        var wallEdges=new HashSet<(int,int)>();
        foreach(var s in sides)for(int j=0;j<s.Length;j++)wallEdges.Add((Math.Min(s[j],s[(j+1)%s.Length]),Math.Max(s[j],s[(j+1)%s.Length])));
        bool covered=sides.Count>0&&frontUse.Where(e=>e.Value==1).All(e=>wallEdges.Contains(e.Key));
        LayerBoxBack Build(bool withBack)
        {
            var faces=new List<(int[] V,int[] T)>();
            foreach(var f in fronts)faces.Add((mesh.Ids[f].ToArray(),Enumerable.Repeat(BoxFaceThicknessMm,mesh.Ids[f].Length).ToArray()));
            var centre=fronts.SelectMany(f=>mesh.Polygons[f]).Aggregate(Vector3.Zero,(a,b)=>a+b)/fronts.Sum(f=>mesh.Polygons[f].Length);
            foreach(var s in sides)
            {
                // the side faces point away from the box
                var pts=s.Select(V).ToArray();var nn=CheekShellFill.Newell(pts);var mid=pts.Aggregate(Vector3.Zero,(a,b)=>a+b)/pts.Length;
                var ids=s;if(Vector3.Dot(nn,mid-centre)<0)ids=s.Reverse().ToArray();
                faces.Add((ids,Enumerable.Repeat(BoxFaceThicknessMm,ids.Length).ToArray()));
            }
            bool open=!withBack;var newPoints=new List<Vector3>();int backCount=0;
            if(withBack&&sides.Count>0)
            {
                foreach(var l in lasts)
                {
                    var b=mesh.Backs[l];var ids=new int[b.Length];
                    for(int j=0;j<b.Length;j++)
                    {
                        int w=mesh.RibEnds[l][j];
                        int near=-1;float nearest=.002f;for(int q=0;q<vertices.Length;q++){var dist=Vector3.Distance(vertices[q],b[j]);if(dist<nearest){nearest=dist;near=q;}}
                        if(w>=0&&Vector3.Distance(V(w),b[j])<.001f)ids[j]=w;
                        else if(near>=0)ids[j]=near;
                        else{int found=newPoints.FindIndex(q=>Vector3.Distance(q,b[j])<.0005f);if(found<0){newPoints.Add(b[j]);found=newPoints.Count-1;}ids[j]=vertices.Length+found;}
                    }
                    faces.Add((ids.Reverse().ToArray(),Enumerable.Repeat(BoxFaceThicknessMm,ids.Length).ToArray()));backCount++;
                }
            }
            if(sides.Count==0)
            {
                // a loose plate: its front faces with the thickness of the whole stack (front to the real back of the last layer behind it)
                faces.Clear();
                foreach(var f in fronts)
                {
                    var poly=mesh.Polygons[f];var nrm=mesh.Normals[f];var thickness=new int[poly.Length];var mine=Lateral(poly.Aggregate(Vector3.Zero,(a,b)=>a+b)/poly.Length,nrm);
                    var match=lasts.OrderBy(l=>Vector3.Distance(Lateral(mesh.Polygons[l].Aggregate(Vector3.Zero,(a,b)=>a+b)/mesh.Polygons[l].Length,nrm),mine)).First();
                    for(int j=0;j<poly.Length;j++)
                    {
                        var bj=mesh.Backs[match].OrderBy(q=>Vector3.Distance(Lateral(q,nrm),Lateral(poly[j],nrm))).First();
                        thickness[j]=Math.Clamp((int)Math.Round(Vector3.Dot(poly[j]-bj,nrm)*1000),MinimumMm,short.MaxValue);
                    }
                    faces.Add((mesh.Ids[f].ToArray(),thickness));
                }
            }
            // assemble: the points keep their numbers (the clean-up removes the unused ones and renumbers), edges from the faces
            var node=System.Text.Json.Nodes.JsonNode.Parse(json)!;var va=node["vertices"]!.AsArray();
            foreach(var q in newPoints){va.Add(q.X);va.Add(q.Y);va.Add(q.Z);}
            var edgeIndex=new Dictionary<(int,int),int>();var edges=new System.Text.Json.Nodes.JsonArray();
            var faceArray=new System.Text.Json.Nodes.JsonArray();
            foreach(var (ids,t) in faces)
            {
                for(int j=0;j<ids.Length;j++)
                {
                    var k=(Math.Min(ids[j],ids[(j+1)%ids.Length]),Math.Max(ids[j],ids[(j+1)%ids.Length]));
                    if(!edgeIndex.ContainsKey(k)){edgeIndex[k]=edgeIndex.Count;edges.Add(k.Item1);edges.Add(k.Item2);}
                }
                faceArray.Add(new System.Text.Json.Nodes.JsonObject{["v"]=new System.Text.Json.Nodes.JsonArray(ids.Select(x=>(System.Text.Json.Nodes.JsonNode)x).ToArray()),["t"]=new System.Text.Json.Nodes.JsonArray(t.Select(x=>(System.Text.Json.Nodes.JsonNode)x).ToArray()),["tm"]=0,["te"]=0});
            }
            node["edges"]=edges;node["faces"]=faceArray;
            if(node["edgeFlags"]!=null)node["edgeFlags"]=new System.Text.Json.Nodes.JsonArray(Enumerable.Range(0,edgeIndex.Count).Select(_=>(System.Text.Json.Nodes.JsonNode)0).ToArray());
            var cleanup=LayerCompact.Run(node.ToJsonString(),Array.Empty<int>());
            if(sides.Count>0&&!IsBox(cleanup.Json,forward))throw Refuse("the faces that are left do not form a box that Convert to layers recognises");
            var kind=sides.Count==0?"a plate":open?"a box without a back":"a box";
            var summary=$"{fronts.Count} front faces, {sides.Count} side walls{(backCount>0?$", {backCount} back faces":"")}: {kind}. The thickness of the faces is {BoxFaceThicknessMm} mm (Convert to layers ignores it). Undo removes this change in one step.";
            return new LayerBoxBack(cleanup.Json,fronts.Count,sides.Count,backCount,open,cleanup.VerticesAfter,cleanup.EdgesAfter,summary);

        }
        // an open box (no back faces) when every outer edge of the front has a side wall, else a closed one; either way the box must give the same layers again (the caller checks it with the recipe the plate was made with)
        if(sides.Count==0)return Build(false);
        string? why=null;
        if(covered){var openBox=Build(false);why=verify?.Invoke(openBox.Json);if(why==null)return openBox;}
        var closedBox=Build(true);var why2=verify?.Invoke(closedBox.Json);
        if(why2==null)return closedBox;
        throw Refuse("converting the box again does not give the same layers ("+(why2.Length>140?why2[..140]+"...":why2)+")");
    }
    /// <summary>
    /// The check ToBox asks of a candidate box: converting it with the recipe the plate was made with must give the same layers again (the same number of faces, every point within
    /// <paramref name="toleranceMm"/>). Null when it does; otherwise the reason in plain words.
    /// </summary>
    internal static Func<string,string?> BoxCheck(string layersJson,CassetteModel recipe,Func<string,double>? density,Vector3 forward,double wallMm,Vector3? centre,double toleranceMm=1)
    {
        var before=Outer(layersJson);
        return box=>
        {
            try
            {
                var r=Convert(box,recipe,null,density,forward,wallMm,centre);
                var after=Outer(r.Plan.Json);
                if(after.Layers!=before.Layers||after.Fronts!=before.Fronts)return $"{after.Layers} layer faces ({after.Fronts} in front) instead of {before.Layers} ({before.Fronts})";
                if(Math.Abs(after.Walls-before.Walls)>Math.Max(2,before.Walls/20))return $"{after.Walls} side wall faces instead of {before.Walls}";
                double far=Math.Max(before.Points.Max(p=>after.Points.Min(q=>Vector3.Distance(p,q))),after.Points.Max(p=>before.Points.Min(q=>Vector3.Distance(p,q))))*1000;
                return far<=toleranceMm?null:string.Create(CultureInfo.InvariantCulture,$"the outer layers {far:0.#} mm away from the original ones");
            }
            catch(InvalidOperationException ex){return ex.Message;}
        };
    }
    /// <summary>What must come back: the number of layer faces, the number of first layers, and the front corners of the first layers and the back points of the last layers (points of inner layers do not count).</summary>
    private static (int Layers,int Fronts,int Walls,Vector3[] Points) Outer(string json)
    {
        var mesh=LayerMesh.Read(json);var ranks=LayerRanks.FromJson(json);int top=ranks.Where(r=>r>=0).DefaultIfEmpty(-1).Max();
        var points=new List<Vector3>();int fronts=0,layers=0,walls=ranks.Count(r=>r==LayerHit.SideWallRank);
        for(int i=0;i<ranks.Length&&i<mesh.Polygons.Length;i++)
        {
            if(ranks[i]<0)continue;layers++;
            if(ranks[i]==0){fronts++;points.AddRange(mesh.Polygons[i]);}
            if(ranks[i]==top)points.AddRange(mesh.Backs[i]);
        }
        return (layers,fronts,walls,points.Count==0?new[]{Vector3.Zero}:points.ToArray());
    }
    private static Vector3 Lateral(Vector3 p,Vector3 normal)=>p-normal*Vector3.Dot(p,normal);
    private static Vector3[] RawVertices(string json)
    {
        using var doc=JsonDocument.Parse(json);var v=doc.RootElement.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        return Enumerable.Range(0,v.Length/3).Select(i=>new Vector3(v[3*i],v[3*i+1],v[3*i+2])).ToArray();
    }
}
