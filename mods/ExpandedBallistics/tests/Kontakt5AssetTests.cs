using System.Text.Json;
namespace SprocketExpandedBallistics;

/// <summary>0.22.21: the Kontakt-5 OpenSCAD asset set (tests\fixtures\kontakt5 = the installer payload), same expectations as tools\check.py of the asset project.</summary>
internal static class Kontakt5AssetTests
{
    private sealed record Mesh(List<double[]> V,List<(string G,int A,int B,int C)> F);
    private static string? Dir()
    {
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
        {var p=Path.Combine(d.FullName,"tests","fixtures","kontakt5");if(Directory.Exists(p))return p;}
        return null;
    }
    private static Mesh Load(string path)
    {
        var m=new Mesh(new(),new());var g="";
        foreach(var raw in File.ReadLines(path))
        {
            var line=raw.Trim();
            if(line.StartsWith("g ",StringComparison.Ordinal))g=line[2..].Trim();
            else if(line.StartsWith("v ",StringComparison.Ordinal)){var t=line.Split(' ',StringSplitOptions.RemoveEmptyEntries);m.V.Add(new[]{double.Parse(t[1],System.Globalization.CultureInfo.InvariantCulture),double.Parse(t[2],System.Globalization.CultureInfo.InvariantCulture),double.Parse(t[3],System.Globalization.CultureInfo.InvariantCulture)});}
            else if(line.StartsWith("f ",StringComparison.Ordinal)){var t=line.Split(' ',StringSplitOptions.RemoveEmptyEntries);m.F.Add((g,int.Parse(t[1])-1,int.Parse(t[2])-1,int.Parse(t[3])-1));}
        }
        return m;
    }
    private static List<List<int>> Components(Mesh m)
    {
        var parent=Enumerable.Range(0,m.V.Count).ToArray();
        int Find(int x){while(parent[x]!=x){parent[x]=parent[parent[x]];x=parent[x];}return x;}
        foreach(var f in m.F){parent[Find(f.A)]=Find(f.B);parent[Find(f.B)]=Find(f.C);}
        var groups=new Dictionary<int,List<int>>();
        for(int i=0;i<m.F.Count;i++){var r=Find(m.F[i].A);if(!groups.TryGetValue(r,out var l))groups[r]=l=new();l.Add(i);}
        return groups.Values.ToList();
    }
    private static bool Closed(Mesh m,List<int> faces)
    {
        var edges=new Dictionary<(int,int),int>();
        foreach(var i in faces){var f=m.F[i];foreach(var e in new[]{(f.A,f.B),(f.B,f.C),(f.C,f.A)})edges[e]=edges.GetValueOrDefault(e)+1;}
        return edges.All(e=>e.Value==1&&edges.GetValueOrDefault((e.Key.Item2,e.Key.Item1))==1);
    }
    private static double Volume(Mesh m,List<int> faces)
    {
        double v=0;
        foreach(var i in faces){var f=m.F[i];var a=m.V[f.A];var b=m.V[f.B];var c=m.V[f.C];
            v+=(a[0]*(b[1]*c[2]-b[2]*c[1])-a[1]*(b[0]*c[2]-b[2]*c[0])+a[2]*(b[0]*c[1]-b[1]*c[0]))/6;}
        return v;
    }
    private static (double[] Min,double[] Max) Bounds(Mesh m,IEnumerable<int> faces)
    {
        var mn=new[]{double.MaxValue,double.MaxValue,double.MaxValue};var mx=new[]{double.MinValue,double.MinValue,double.MinValue};
        foreach(var i in faces)foreach(var k in new[]{m.F[i].A,m.F[i].B,m.F[i].C})for(int a=0;a<3;a++){mn[a]=Math.Min(mn[a],m.V[k][a]);mx[a]=Math.Max(mx[a],m.V[k][a]);}
        return (mn,mx);
    }
    private static bool Same((double[] Min,double[] Max) a,JsonElement old)
    {
        var o=old.EnumerateArray().Select(x=>x.EnumerateArray().Select(y=>y.GetDouble()).ToArray()).ToArray();
        return Enumerable.Range(0,3).All(i=>Math.Abs(a.Min[i]-o[0][i])<=1e-6&&Math.Abs(a.Max[i]-o[1][i])<=1e-6);
    }
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var assert=check;check=(ok,text)=>{assert(ok,"kontakt5 assets: "+text);count++;};
        var dir=Dir();check(dir!=null,"fixture folder found");
        if(dir==null)return;
        var files=Directory.GetFiles(dir,"*.obj").OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        check(files.Length==11,"11 meshes (the 13-file set has two more icons)");
        var old=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"old-bounds.json"))).RootElement;
        foreach(var path in files)
        {
            var name=Path.GetFileNameWithoutExtension(path);var m=Load(path);var comps=Components(m);
            check(old.TryGetProperty(name,out var o),"old bounds known for "+name);
            if(!old.TryGetProperty(name,out o))continue;
            check(comps.All(c=>Closed(m,c))&&comps.All(c=>Volume(m,c)>0),"closed, positively oriented shells: "+name);
            bool cell=name.Contains("active-stage",StringComparison.Ordinal),physical=name.Contains("physical",StringComparison.Ordinal);
            // bounds: equal to the old asset (whole file and per group); only the enlarged cell cassette boxes differ and must sit inside the housing interior
            foreach(var g in o.GetProperty("groups").EnumerateObject())
            {
                var ids=Enumerable.Range(0,m.F.Count).Where(i=>m.F[i].G==g.Name).ToList();
                if(g.Name=="cassette"&&(cell||physical))
                {
                    var (mn,mx)=Bounds(m,ids);
                    check(ids.Count>0&&mn[0]>=-.148&&mx[0]<=.148&&mn[2]>=-.248&&mx[2]<=.248&&Math.Abs(mn[1]-.0435)<1e-9&&Math.Abs(mx[1]-.0565)<1e-9,"cell inside the housing interior, y 43.5..56.5 mm: "+name);
                }
                else check(ids.Count>0&&Same(Bounds(m,ids),g.Value),"group "+g.Name+" bounds equal the old asset: "+name);
            }
            if(!cell)check(Same(Bounds(m,Enumerable.Range(0,m.F.Count)),o.GetProperty("all")),"whole-file bounds equal the old asset: "+name);
            var groupNames=m.F.Select(f=>f.G).Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            check(groupNames.SequenceEqual(o.GetProperty("groups").EnumerateObject().Select(x=>x.Name).OrderBy(x=>x,StringComparer.Ordinal)),"same group names as the old asset: "+name);
            if(name.Contains("housing",StringComparison.Ordinal)||physical)
            {
                var plates=comps.Where(c=>m.F[c[0]].G=="mount").Where(c=>{var b=Bounds(m,c);var d=Enumerable.Range(0,3).Select(a=>b.Max[a]-b.Min[a]).OrderBy(x=>x).ToArray();return Math.Abs(d[0]-.002)<1e-9&&Math.Abs(Volume(m,c)-d[0]*d[1]*d[2])<1e-12&&d[1]>.09;}).ToList();
                int want=name.Contains("state-1",StringComparison.Ordinal)||name.Contains("state-2",StringComparison.Ordinal)?5:6;
                check(plates.Count==want,$"{want} separate 2 mm box plates (mass = volume x 7850): {name} found {plates.Count}");
            }
            if(cell)
            {
                var b=Bounds(m,Enumerable.Range(0,m.F.Count));
                check(comps.Count==1&&m.V.Count==8&&Math.Abs((b.Max[1]-b.Min[1])-.013)<1e-9,"cell is one box, 13.000 mm thick: "+name);
            }
            if(physical)
            {
                var cellComps=comps.Where(c=>m.F[c[0]].G=="cassette").ToList();
                var b=Bounds(m,cellComps[0]);
                check(cellComps.Count==1&&Math.Abs((b.Max[1]-b.Min[1])-.013)<1e-9,"physical cell is 13 mm: "+name);
            }
        }
        var s1=File.ReadAllBytes(Path.Combine(dir,"kontakt5-housing-state-1.obj"));var s2=File.ReadAllBytes(Path.Combine(dir,"kontakt5-housing-state-2.obj"));
        check(s1.SequenceEqual(s2),"state-1 equals state-2, as in the old set");
        Console.WriteLine($"0.22.21 Kontakt-5 asset checks PASS: {count} checks.");
    }
}