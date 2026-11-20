using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SprocketCarouselAutoloader;

internal static class BeltVisualTests
{
    static Spec Spec(double length = .5, double calibre = 25, double projectile = 75, double charge = 137,
        double width = .3, double height = .3)
    {
        var s = new Spec("Belt", ModelKind.Bustle,width,height,length,calibre,projectile,charge,1);
        return s with { Capacity = BeltLayout.Of(s).Capacity };
    }
    static bool Near(double a, double b) => Math.Abs(a-b) < 1e-6;
    internal static void Run(Action<bool,string> check)
    {
        foreach (var unit in BeltMeshData.All)
        {
            var path = Path.Combine("Models",unit.Name+".stl");
            check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == unit.StlSha256,unit.Name+" STL SHA256");
            var vertices = File.ReadLines(path).Select(l=>l.Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries))
                .Where(p=>p.Length == 4 && p[0] == "vertex")
                .SelectMany(p=>p.Skip(1).Select(v=>float.Parse(v,CultureInfo.InvariantCulture))).ToArray();
            check(vertices.Length == unit.Indices.Length*3 && unit.Triangles == unit.StlFacets,unit.Name+" every STL facet retained");
            for (int i=0;i<unit.Indices.Length;i++) for(int axis=0;axis<3;axis++)
                check(Near(vertices[i*3+axis],unit.Positions[unit.Indices[i]*3+axis]),unit.Name+" independent facet reparse");
        }
        check(BeltMeshData.Link.Triangles == 228 && BeltMeshData.Link.Triangles <= 250,"approved revised link limit");
        var bradley = BeltVisualGeometry.Create(Spec());
        var tight = BeltVisualGeometry.Create(Spec(.22));
        check(bradley.Layout.Capacity == 288 && bradley.Layout.PerLayer == 24 && bradley.Layout.Layers == 12,"300x300x500 / 25x212 = 288");
        check(tight.Layout.Capacity == 144 && tight.Layout.PerLayer == 12,"300x300x220 / 25x212 = 144");
        check(bradley.Detail == BeltDetail.Links && tight.Detail == BeltDetail.Links,"Bradley and tight retain links");
        check(!bradley.RaisedChute && tight.RaisedChute,"tight +z clearance raises chute above the box");
        check(bradley.Fill.Triangles == 12 && bradley.Fill.Materials.All(m=>m==BeltVisualGeometry.FillMaterial),"one darker 12-triangle fill block");
        check(bradley.Model.Meshes.Count(p=>p.Name.StartsWith("Belt link ")) == 22,"one link per adjacent pair, no end overhang");
        check(bradley.Model.Meshes.Count(p=>p.Role == PartRole.Round && p.Name.StartsWith("Granaat")) == 24,"only top-layer rounds built");
        check(bradley.Model.Meshes.Where(p=>p.Name.StartsWith("Belt link ")).All(p=>p.Slot>0 && p.Slot%gColumns()!=0),"links appear only after both adjacent rounds");
        int gColumns() => bradley.Layout.Columns;
        check(bradley.MaximumTriangles == 9280 && bradley.MaximumTriangles <= AutoloaderVisualPolicy.TriangleBudget,"Bradley maximum 9280 triangles");
        var g = bradley.Layout;
        foreach(int n in new[]{0,1,g.PerLayer,g.PerLayer+1,g.Capacity,-1,g.Capacity+1})
        {
            var v=g.Stock(n); int stored=Math.Clamp(n,0,g.Capacity);
            check(v.Stored == stored && v.UnderLayers*g.PerLayer+v.TopCount == stored,"stock reconstructed from fill + exposed layer");
            check(v.TopCount == (stored==0 ? 0 : 1+(stored-1)%g.PerLayer),"exact top count including exact multiples");
            check(Near(v.FillHeight,v.UnderLayers*g.Diameter) && Near(v.FillHeight+g.Diameter/2,v.TopCentre) == (stored>0),"fill touches underside of top layer");
        }
        var parts=bradley.Model.Meshes.Where(p=>p.Role==PartRole.Round).OrderBy(p=>p.Slot).ToArray();
        var prefix=Enumerable.Range(0,6).Select(m=>AutoloaderVisualPolicy.Prefix(parts,m,g.PerLayer)).ToArray();
        for(int n=0;n<=g.Capacity;n++)
        {
            var v=g.Stock(n);
            check(v.TopCount<=g.PerLayer && v.UnderLayers<=g.Layers-1,"layer within bounds");
            check(v.FullLayers==n/g.PerLayer && v.Remainder==n%g.PerLayer,"quotient and remainder");
            check(v.FullRows*g.Columns+v.PartialColumns==v.TopCount,"fallback rows represent exact stock");
            for(int m=0;m<6;m++)
                check(prefix[m][v.TopCount] == parts.Where(p=>p.Slot<v.TopCount).Sum(p=>p.Materials.Count(x=>x==m)),"prefix selects exact top count, per material");
        }
        // A larger exposed layer first loses only its links; 8 mm needs the second fallback.
        var withoutLinks=BeltVisualGeometry.Create(Spec(.5,15,45,82));
        var tiny=BeltVisualGeometry.Create(Spec(.5,8,24,48));
        check(withoutLinks.Detail == BeltDetail.RoundsOnly && withoutLinks.MaximumTriangles <= 12000,"first fallback omits links");
        check(tiny.Detail == BeltDetail.RibbedBlock && tiny.Ribs != null && tiny.Model.Meshes.All(p=>p.Role!=PartRole.Round) && tiny.MaximumTriangles<=12000,"8 mm second fallback has blocks and pitch ribs, no separate rounds");
        foreach(var s in new[]{Spec(),Spec(.22),Spec(.5,8,24,48),Spec(.5,15,45,82),Spec(.1,25,75,0,.025,.025),Spec(2,1,3,4,2,2)})
        {
            var model=BeltVisualGeometry.Create(s);
            check(model.MaximumTriangles<=12000,"fallback itself remains within budget");
            check(model.Model.Meshes.All(p=>p.Vertices.All(float.IsFinite) && p.Indices.All(i=>i>=0&&i<p.Vertices.Count/3)),"finite vertices and valid indices");
        }
        // The steady update consumes only value-state and cached prefix buffers, as the adapter does.
        int sink=0;
        for(int i=0;i<10000;i++) sink+=prefix[3][g.Stock(i%289).TopCount];
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++) { var v=g.Stock(i%289); sink+=prefix[3][v.TopCount]; }
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        GC.KeepAlive(sink);
        check(bytes==0,"Belt steady managed stock/layer/prefix update: zero allocated bytes");
        Console.WriteLine($"Belt visual stock microbenchmark: 100000 changes, {bytes} managed bytes; Unity native calls require a live profiler.");
        var adapter=File.ReadAllText("AutoloaderVisuals.cs");
        check(adapter.Contains("internal readonly Transform Transform = Child.transform") && adapter.Contains("visual.Stock?.AmountStored") &&
            adapter.Contains("visual.Prefix[slot][visible]*3") && adapter.Contains("Plugin.DetailedLogging && belt.Detail"),"cached native wrappers, prefix draw and gated once-per-VUID logging");
        check(File.ReadAllBytes("Assets/belt-feed-box-icon.png").Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) &&
            File.ReadAllText("BustleVisuals.cs").Contains("__result.Icon=BeltIcon.Icon()"),"approved PNG wired to Belt card");
        Directory.CreateDirectory("logs/ingame-0.3.2");
        foreach(var (name,model,fraction) in new[]{("bradley-100",bradley,1d),("bradley-60",bradley,.6),("bradley-5",bradley,.05),("tight-60",tight,.6)})
            Export(name,model,(int)Math.Round(model.Model.Spec.Capacity*fraction));
        File.WriteAllLines("logs/0.3.2-geometry.txt",new[]{
            "Box 136; link 228; chute 84; round 168; lower fill 12 triangles.",
            $"Bradley 300x300x500, 25x212: {bradley.Layout.Capacity} capacity, {bradley.MaximumTriangles} maximum triangles.",
            $"15 mm: {withoutLinks.Detail}, {withoutLinks.MaximumTriangles} maximum triangles.",
            $"8 mm: {tiny.Detail}, {tiny.MaximumTriangles} maximum triangles.",
            "100000 managed stock/layer/prefix changes: 0 bytes. Native/Unity allocations not measured offline."
        });
        Console.WriteLine("Belt model hashes/facets/packing/layers/fill/prefix/budget/icon/managed allocations PASS; four mesh snapshots exported.");
    }

    internal static void Export(string name,BeltVisualModel model,int stock)
    {
        var state=model.Layout.Stock(stock);
        var items=new List<object>();
        foreach(var p in model.Model.Meshes)
        {
            if(p.Role==PartRole.Round && p.Slot>=state.TopCount) continue;
            var vertices=p.Vertices.ToArray();
            if(p.Role==PartRole.Round) for(int i=1;i<vertices.Length;i+=3) vertices[i]+=(float)state.FillHeight;
            items.Add(new { p.Name,vertices,indices=p.Indices,materials=p.Materials });
        }
        if(state.FillHeight>0)
        {
            var p=model.Fill; var v=p.Vertices.ToArray(); var s=model.Model.Spec; var g=model.Layout;
            for(int i=0;i<v.Length;i+=3) { v[i]=(float)(g.X0+v[i]*g.Width); v[i+1]=(float)(g.Y0+v[i+1]*state.FillHeight); v[i+2]=(float)(g.Z0+v[i+2]*g.Depth); }
            items.Add(new { p.Name,vertices=v,indices=p.Indices,materials=p.Materials });
        }
        File.WriteAllText($"logs/ingame-0.3.2/{name}.json",JsonSerializer.Serialize(new{spec=model.Model.Spec,state,items},new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
    }
}
