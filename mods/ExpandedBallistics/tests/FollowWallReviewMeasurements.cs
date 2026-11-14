using System.Globalization;
using System.Numerics;
using System.Text.Json;
namespace SprocketExpandedBallistics;
internal static partial class FollowWallCoverage
{
    private sealed record MeasurePrism(Vector3[] Front,Vector3[] Back,Tet[] Cells,bool Layer)
    {
        internal Vector3 Min=>Front.Concat(Back).Aggregate(Vector3.Min);
        internal Vector3 Max=>Front.Concat(Back).Aggregate(Vector3.Max);
        internal bool Has(Vector3 p)=>Cells.Any(t=>t.Has(p));
    }
    internal static void ReviewMeasure(string original,LayerConversion result,string selection,string recipe,string output)
    {
        // Measure reuses the same seed-0 inside samples; no geometry or density is changed here.
        if(!samples.ContainsKey(original))_=Measure(original,result);var source=LayerFaces.ClosedSurface(original);var native=LayerMesh.Read(result.Plan.Json);var ranks=LayerRanks.FromJson(result.Plan.Json);
        var all=Enumerable.Range(0,native.Polygons.Length).Select(i=>new MeasurePrism(native.Polygons[i],native.Backs[i],Prisms(new[]{(native.Polygons[i],native.Backs[i])}),result.Faces[i].Rank>=0)).ToArray();
        var cover=result.Sources.Where(s=>s.Converted).Select(s=>new MeasurePrism(s.Front,s.Back,Prisms(new[]{(s.Front,s.Back)}),true)).Concat(result.Faces.Where(f=>f.Rank>=0).Select(f=>new MeasurePrism(f.Front,f.Back,Prisms(new[]{(f.Front,f.Back)}),true))).ToArray();
        var outputRows=new List<object>();
        foreach(int side in new[]{-1,1})
        {
            var shell=source.Where(p=>p[0].X*side>0).ToArray();var points=shell.SelectMany(p=>p).ToArray();var origin=points.Aggregate(Vector3.Min);var originalMax=points.Aggregate(Vector3.Max);
            var ps=all.Where(p=>p.Front[0].X*side>0).ToArray();var cs=cover.Where(p=>p.Front[0].X*side>0).ToArray();
            var min=ps.Select(p=>p.Min).Append(origin).Aggregate(Vector3.Min);var max=ps.Select(p=>p.Max).Append(originalMax).Aggregate(Vector3.Max);
            int x0=(int)Math.Floor((min.X-origin.X)/Step-.37f),y0=(int)Math.Floor((min.Y-origin.Y)/Step-.43f),z0=(int)Math.Floor((min.Z-origin.Z)/Step-.61f);
            int nx=(int)Math.Ceiling((max.X-origin.X)/Step-.37f)-x0+1,ny=(int)Math.Ceiling((max.Y-origin.Y)/Step-.43f)-y0+1,nz=(int)Math.Ceiling((max.Z-origin.Z)/Step-.61f)-z0+1;
            int size=nx*ny*nz;var count=new ushort[size];var layerCount=new ushort[size];var covered=new bool[size];var isInside=new bool[size];
            int Index(int x,int y,int z)=>((x-x0)*ny+(y-y0))*nz+(z-z0);
            Vector3 At(int x,int y,int z)=>origin+new Vector3((x+.37f)*Step,(y+.43f)*Step,(z+.61f)*Step);
            foreach(var p in samples[original][side]){int x=(int)Math.Round((p.X-origin.X)/Step-.37f),y=(int)Math.Round((p.Y-origin.Y)/Step-.43f),z=(int)Math.Round((p.Z-origin.Z)/Step-.61f);isInside[Index(x,y,z)]=true;}
            void Raster(MeasurePrism prism,bool coverOnly)
            {
                var pmin=prism.Min;var pmax=prism.Max;
                int xa=Math.Max(x0,(int)Math.Ceiling((pmin.X-origin.X)/Step-.37f)),xb=Math.Min(x0+nx-1,(int)Math.Floor((pmax.X-origin.X)/Step-.37f));
                int ya=Math.Max(y0,(int)Math.Ceiling((pmin.Y-origin.Y)/Step-.43f)),yb=Math.Min(y0+ny-1,(int)Math.Floor((pmax.Y-origin.Y)/Step-.43f));
                int za=Math.Max(z0,(int)Math.Ceiling((pmin.Z-origin.Z)/Step-.61f)),zb=Math.Min(z0+nz-1,(int)Math.Floor((pmax.Z-origin.Z)/Step-.61f));
                for(int x=xa;x<=xb;x++)for(int y=ya;y<=yb;y++)for(int z=za;z<=zb;z++)if(prism.Has(At(x,y,z))){int key=Index(x,y,z);if(coverOnly)covered[key]=true;else{count[key]++;if(prism.Layer)layerCount[key]++;}}
            }
            foreach(var p in ps)Raster(p,false);foreach(var p in cs)Raster(p,true);
            var slices=new List<object>();
            for(int slice=0;slice<6;slice++)
            {
                float lo=origin.Z+(originalMax.Z-origin.Z)*slice/6,hi=origin.Z+(originalMax.Z-origin.Z)*(slice+1)/6;long inside=0,fill=0,solid=0;
                foreach(var p in samples[original][side])if(p.Z>=lo&&(p.Z<hi||slice==5))
                {int x=(int)Math.Round((p.X-origin.X)/Step-.37f),y=(int)Math.Round((p.Y-origin.Y)/Step-.43f),z=(int)Math.Round((p.Z-origin.Z)/Step-.61f);int key=Index(x,y,z);inside++;if(covered[key])fill++;if(layerCount[key]>0)solid++;}
                slices.Add(new{slice,zMin=lo,zMax=hi,inside,covered=fill,solid,filledFraction=inside>0?(double)fill/inside:0,solidFraction=inside>0?(double)solid/inside:0});
            }
            double cell=Math.Pow(Step,3);long union=count.LongCount(n=>n>0),layerUnion=layerCount.LongCount(n=>n>0);long multiplicity=count.Sum(n=>(long)n),layerMultiplicity=layerCount.Sum(n=>(long)n);
            double volume=0;var moment=Vector3.Zero;
            foreach(var poly in shell)for(int k=1;k+1<poly.Length;k++){double v=Vector3.Dot(poly[0],Vector3.Cross(poly[k],poly[k+1]))/6.0;volume+=v;moment+=(poly[0]+poly[k]+poly[k+1])*(float)(v/4);}
            var centre=moment/(float)volume;var centreUsed=samples[original][side].OrderBy(p=>Vector3.DistanceSquared(p,centre)).First();
            var chords=new List<object>();
            foreach(int degrees in new[]{-30,0,30})
            {
                float theta=degrees*MathF.PI/180;var direction=new Vector3(MathF.Sin(theta),-MathF.Cos(theta),0);var rayOrigin=centreUsed-direction*5;
                var intervals=ps.SelectMany(p=>p.Cells.Select(t=>t.RayInterval(rayOrigin,direction)).Where(t=>t!=null).Select(t=>t!.Value)).ToList();
                var layerIntervals=ps.Where(p=>p.Layer).SelectMany(p=>p.Cells.Select(t=>t.RayInterval(rayOrigin,direction)).Where(t=>t!=null).Select(t=>t!.Value)).ToList();
                var cuts=shell.SelectMany(p=>Enumerable.Range(1,p.Length-2).Select(k=>ComposerFill.Ray(rayOrigin,direction,new[]{new FillTriangle(p[0],p[k],p[k+1])},20))).Where(t=>t!=null).Select(t=>t!.Value).OrderBy(t=>t).ToList();
                var unique=new List<double>();foreach(var t in cuts)if(unique.Count==0||t-unique[^1]>1e-6)unique.Add(t);
                double closed=0;for(int i=0;i+1<unique.Count;i+=2)closed+=unique[i+1]-unique[i];
                chords.Add(new{angleDegrees=degrees,closedMm=closed*1000,solidIncludingWallsMm=UnionLength(intervals)*1000,solidLayersMm=UnionLength(layerIntervals)*1000,summedIndividualSolidMm=ps.Sum(p=>UnionLength(p.Cells.Select(t=>t.RayInterval(rayOrigin,direction)).Where(t=>t!=null).Select(t=>t!.Value).ToList()))*1000});
            }
            outputRows.Add(new{cheek=side,closedVolumeM3=volume,slices,overlap=new{nativeFaceVolumeSumM3=ps.Sum(p=>Math.Abs(ComposerFill.PrismVolume(p.Front,p.Back))),nativeLayerVolumeSumM3=ps.Where(p=>p.Layer).Sum(p=>Math.Abs(ComposerFill.PrismVolume(p.Front,p.Back))),allUnionEstimateM3=union*cell,layerUnionEstimateM3=layerUnion*cell,duplicatedAllVolumeEstimateM3=(multiplicity-union)*cell,duplicatedLayerVolumeEstimateM3=(layerMultiplicity-layerUnion)*cell,overlapCells=count.LongCount(n=>n>1),outsideOriginalUnionEstimateM3=count.Where((n,i)=>n>0&&!isInside[i]).LongCount()*cell},centre=new[]{centreUsed.X,centreUsed.Y,centreUsed.Z},volumeCentroid=new[]{centre.X,centre.Y,centre.Z},chords});
        }
        var directory=Path.Combine(output,"review-round3");Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,$"measure-{selection}-{recipe}.json"),JsonSerializer.Serialize(new{selection,recipe,step=Step,seed=0,note="Fixed-lattice multiplicity minus union measures volume counted more than once in native face-based weight; no correction applied. Extra coverage and layer unions use planned tetrahedral prisms. Chords are geometric union lengths, not native ballistic simulation. Centre is the nearest inside lattice point to the signed-volume centroid.",cases=outputRows},new JsonSerializerOptions{WriteIndented=true}));

    }
    private static double UnionLength(List<(double A,double B)> intervals)
    {
        double length=0,start=0,end=0;bool any=false;foreach(var (a,b) in intervals.OrderBy(p=>p.A)){if(!any){start=a;end=b;any=true;}else if(a<=end)end=Math.Max(end,b);else{length+=end-start;start=a;end=b;}}return any?length+end-start:0;
    }
}
