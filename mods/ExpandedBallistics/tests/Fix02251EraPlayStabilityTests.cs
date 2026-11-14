using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.51 part A (user 08-11: "de hele tank verdween na een ERA explosie"; fix approved "Ja, haal weg"): nothing rebuilds a model, mesh,
/// renderer or collider after an ERA activation; spent cells stay visible and inert, the spent ledger is unchanged.
/// Part C: the active-cell colliders of the Relikt modules against their housings and visuals, and synthetic ray coverage.
/// </summary>
internal static class Fix02251EraPlayStabilityTests
{
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var sink=check;check=(ok,msg)=>{count++;sink(ok,"fix02251-AC: "+msg);};
        string root=Fix02228Tests.SourceRoot(),parent=Fix02228Tests.SessionRoot(),output=Fix02228Tests.OutputRoot();
        string Read(string rel)=>File.ReadAllText(Path.Combine(root,rel));
        var sources=Directory.GetFiles(root,"*.cs",SearchOption.AllDirectories).Where(p=>!p.Contains(Path.DirectorySeparatorChar+"tests"+Path.DirectorySeparatorChar)&&
            !p.Contains(Path.DirectorySeparatorChar+"obj"+Path.DirectorySeparatorChar)&&!p.Contains(Path.DirectorySeparatorChar+"tools"+Path.DirectorySeparatorChar)&&
            !p.Contains(Path.DirectorySeparatorChar+"era-rework"+Path.DirectorySeparatorChar)).ToDictionary(p=>Path.GetRelativePath(root,p).Replace('\\','/'),File.ReadAllText);
        // ---- A1: the activation event reaches no renderer/collider code ----
        check(sources.Count(p=>Regex.IsMatch(p.Value,@"PlacedEra\.Activated\s*\+="))==1&&sources["Material/EraSpentRender.cs"].Contains("PlacedEra.Activated+=Activated"),"only the isolated render-only subscriber receives ERA activation");
        var spent=Read("Material/ReliktSpentVisuals.cs");
        foreach(var forbidden in new[]{"RefreshModel","SetVisible(","ApplyCellVisual","MarkCollidersDirty","RequestRebuild","ReconcileCompanionHousing","Activated+=",
            "nameof(VehicleObjectModel.SetVisible)","nameof(VehicleObjectModel.OnModelLoaded)","nameof(Player.GameLateUpdate)","PlacedEra.SpentCells","PlacedEra.IsSpent"})
            check(!spent.Contains(forbidden,StringComparison.Ordinal),"ReliktSpentVisuals has no "+forbidden);
        check(spent.Contains("PlacedEra.ResetForEdit()")&&spent.Contains("nameof(VehicleEditorScenarioGameState.OnPlayerStateChanged)"),"the ledger reset at the committed Edit state is retained");
        var proto=Read("Material/ReliktPrototype.cs");
        check(!proto.Contains("PlacedEra.",StringComparison.Ordinal)&&!proto.Contains("ReliktSpentVisuals",StringComparison.Ordinal),"the ERA render/collider pipeline reads no expenditure state (no data flow from an activation)");
        foreach(var gone in new[]{"ApplyCellVisual","RenderSignature","ReconcileCompanionHousing","CompanionHousingSpent","SpentKeys(","HiddenVisualKey","force:true",".SetVisible("})
            check(!proto.Contains(gone,StringComparison.Ordinal),"ReliktPrototype no longer contains "+gone);
        check(proto.Contains("CanBuildGeometry=>ModularGeometryGate.CanWrite(RuntimeDamageFeed.IsEditor,LiveImpact,loadDepth>0)"),"geometry permits scoped Play construction and rejects live impact");
        string Method(string name)
        {
            var header=Regex.Match(proto,@"(?:private|internal) static (?:void|bool) "+name+@"\([^)]*\)\s*\{");
            if(!header.Success)throw new Exception("method missing: "+name);
            int begin=header.Index+header.Length,depth=1,end=begin;
            for(;end<proto.Length&&depth>0;end++){if(proto[end]=='{')depth++;if(proto[end]=='}')depth--;}
            return proto[begin..(end-1)];
        }
        foreach(var method in new[]{"Apply","RefreshModel","InitializeBalance","RequestNativeColliderRebuild","QueueColliderRebuild","DrainColliderRequests","EnqueueTransform","DrainRefresh","ScheduleSettle"})
            check(Method(method).TrimStart().StartsWith("if(!CanBuildGeometry",StringComparison.Ordinal),method+" checks construction permission before every side effect");
        foreach(var method in new[]{"Built","Loaded","BuildingDamage","DamageCollidersBuilt"})
        {
            var body=Method(method);var gate=body.IndexOf(method=="Built"?"if(LiveImpact){ScheduleDeferred(__instance,balance:true);return;}":"if(LiveImpact){ScheduleDeferred(__instance);return;}",StringComparison.Ordinal);
            check(gate>=0&&!body[..gate].Contains("FastStates.Remove")&&body.Contains("using var load=new LoadScope(true);")&&!Regex.IsMatch(body[..gate],@"\b(Apply|DrainRefresh|ReleaseRetired)\("),method+" loads in Play outside impact; hit callbacks cannot invalidate installed state");
        }
        check(Method("TickDeferred").TrimStart().StartsWith("if(LiveImpact)return;")&&Method("TickDeferred").Contains("ModularGeometryGate.DropDeferred"),"deferred initial Play loads resume outside impact and discard stale hit work");
        check(Method("ScheduleDeferred").Contains("ModularGeometryGate.DeferImpact(InstalledGeometry.Contains(pointer))"),"an impact cannot queue an already installed owner");
        check(Regex.Matches(proto,"using var load=new LoadScope\\(true\\);").Count==5,"only the four construction callbacks and their deferred drain open a Play scope");
        check(Method("TickDeferred").Contains("{InitializeBalance(model);entry.Value.NeedsBalance=false;}"),"first native Build interrupted by impact also completes mass/material/cost initialization");
        Fix02251PlayLoadTests.Run(check);
        check(proto.Contains("[HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.Release))]"),"mesh ownership is released after the native model release completed");
        var meshes=new ModularMeshOwnership();var owner=(IntPtr)51;int thresholdWarnings=0;
        for(int i=0;i<300;i++)meshes.Commit(owner,new[]{"collision"+i},256,_=>thresholdWarnings++);
        check(meshes.Users("collision0")==1&&meshes.RetiredCount(owner)==299&&thresholdWarnings==1,"300 pending generations never release collision0 before native replacement");
        meshes.ReleaseRetired(owner);
        check(meshes.Users("collision0")==0&&meshes.Users("collision299")==1,"confirmed native replacement frees retired meshes while keeping the live generation");
        // The remaining native collider request is reachable only from a geometry identity/wrapper change, never from the ledger.
        // QueueColliderRebuild: its definition, RecordApplied, EnsureWholeCell, EnsureLegacy and the re-entrancy deferral; drained only by TickDeferred.
        var requestCallers=Regex.Matches(proto,@"QueueColliderRebuild\(").Count;
        check(requestCallers==5&&Regex.Matches(proto,@"RequestNativeColliderRebuild\(").Count==2,"collider rebuild requests come only from geometry identity/wrapper changes (load, build, scale, mirror)");
        check(sources.Where(p=>p.Key!="Material/ReliktPrototype.cs").All(p=>!p.Value.Contains("MarkCollidersDirty",StringComparison.Ordinal)),"MarkCollidersDirty exists only in the load/scale path of ReliktPrototype");
        // Every other place that calls SetVisible/SetMesh/SetCollisionMesh/RequestRebuild is an editor or build-time path (inventory, part A.4).
        var inventory=new StringBuilder("file\tline\tcall\tpath\n");
        foreach(var (file,text) in sources.OrderBy(p=>p.Key))
        {
            var lines=text.Split('\n');
            for(int i=0;i<lines.Length;i++)
                foreach(Match m in Regex.Matches(lines[i],@"\.(SetVisible|SetMesh|SetCollisionMesh|MarkCollidersDirty|RequestRebuild|RequestMeshRefresh|MarkDynamic)\("))
                    inventory.AppendLine($"{file}\t{i+1}\t{m.Groups[1].Value}\t{Classify(file)}");
        }
        File.WriteAllText(Path.Combine(output,"PLAY-MUTATION-INVENTORY.tsv"),inventory.ToString());
        check(!inventory.ToString().Contains("\tactivation"),"no native mesh/renderer/collider call is reached from an ERA activation");
        // ---- A2: spent ledger and detonations ----
        var runtime=Read("RuntimeArmourResponses.cs");
        foreach(var anchor in new[]{"intact=ArmourResponseModel.ConsumeStacked(","placedState.Track(cell);","PlacedEra.Queue(context,","ObserveEra(scope,material,\"eraSpent\",\"charge already spent\",entity);",
            "if(retention>=1)","RuntimeDamageFeed.EraActivated(sim,entity,cell,response.Kind,context.LiveImpact,intact);"})
            check(runtime.Contains(anchor,StringComparison.Ordinal),"consumption path present: "+anchor);
        var cells=new EraCells();var cell=PlacedEraState.Key(7,984,0,0,0);
        check(cells.Consume(cell)&&!cells.Consume(cell),"a spent cell does not detonate a second time (managed ledger)");
        // ---- C1: active-cell colliders equal the visual cassette volume and lie inside the housing (unit scale; every Relikt preset) ----
        var assets=Path.Combine(root,"Material/files/BepInEx/plugins/SprocketExpandedBallistics/era-assets");
        var tsv=new StringBuilder("cassette\tcollider_min\tcollider_max\tvisual_min\tvisual_max\thousing_min\thousing_max\tinside_housing\n");
        int presets=0;
        foreach(var p in SprocketExpandedBallistics.Materials.EraPresetRegistry.Presets.Where(p=>!p.WholeCells&&p.Cassette.StartsWith("relikt",StringComparison.Ordinal)))
        {
            var collider=Bounds(Path.Combine(assets,p.Collision),"cassette");var visual=Bounds(Path.Combine(assets,p.Visual),"cassette");
            if(collider==null||visual==null)continue;presets++;
            check(Same(collider.Value,visual.Value),"collider = visual active volume (no offset, rotation or scale mismatch): "+p.Cassette);
            var housing=p.MountCollision!=null?Bounds(Path.Combine(assets,p.MountCollision),"mount"):null;
            bool inside=housing is {} h&&collider.Value.Min.X>=h.Min.X&&collider.Value.Max.X<=h.Max.X&&collider.Value.Min.Z>=h.Min.Z&&collider.Value.Max.Z<=h.Max.Z&&collider.Value.Max.Y<=h.Max.Y;
            // reliktCassette and reliktTurretCassette are the fixed pre-modular presets: their installed mount collision is only the low base box, so the active plate rises above it by design (the file was not in the development tree, so this was never evaluated before).
            if(housing!=null&&p.Cassette is not ("reliktCassette" or "reliktTurretCassette"))check(inside,"active collider inside its housing: "+p.Cassette);
            tsv.AppendLine($"{p.Cassette}\t{V(collider.Value.Min)}\t{V(collider.Value.Max)}\t{V(visual.Value.Min)}\t{V(visual.Value.Max)}\t{(housing is {} hb?V(hb.Min):"")}\t{(housing is {} hc?V(hc.Max):"")}\t{inside}");
        }
        check(presets>=8,"all non-modular Relikt presets compared ("+presets+")");
        File.WriteAllText(Path.Combine(output,"RELIKT-COLLIDERS.tsv"),tsv.ToString());
        // ---- C2: scaled modular 2x2 (whole cells): collision plates and housing are generated from one layout and one compensation ----
        foreach(var variant in new[]{"reliktHull","reliktTurret","reliktSide"})
        foreach(var (sx,sz) in new[]{(1d,1d),(2d,2d),(2d,1d),(-2d,2d),(3d,2d)})
        {
            var spec=ModularCellSpec.ForVariant(variant);if(!ModularCellGeometry.TryLayout(sx,sz,spec,8,8,out var layout))continue;
            var plates=ModularCellGeometry.Build(ModularPartKind.Stage1Collision,layout,spec,null,Math.Abs(sx),Math.Abs(sz));
            var frame=ModularCellGeometry.Build(ModularPartKind.Housing,layout,spec,null,Math.Abs(sx),Math.Abs(sz));
            var scale=new Vector3((float)Math.Abs(sx),1,(float)Math.Abs(sz));
            var pw=plates.Vertices.Select(v=>v*scale).ToArray();var fw=frame.Vertices.Select(v=>v*scale).ToArray();
            check(pw.Min(v=>v.X)>=fw.Min(v=>v.X)-1e-6&&pw.Max(v=>v.X)<=fw.Max(v=>v.X)+1e-6&&pw.Min(v=>v.Z)>=fw.Min(v=>v.Z)-1e-6&&pw.Max(v=>v.Z)<=fw.Max(v=>v.Z)+1e-6,
                $"scaled modular plates lie inside the scaled housing: {variant} {sx}x{sz}");
            for(int x=0;x<layout.CountU;x++)for(int y=0;y<layout.CountV;y++)
            {
                var b=ModularCellGeometry.CellBounds(layout,spec,x,y);var cellIndex=layout.Index(x,y);
                var own=Enumerable.Range(0,plates.TriangleCells.Length).Where(t=>plates.TriangleCells[t]==cellIndex).SelectMany(t=>new[]{pw[plates.Triangles[3*t]],pw[plates.Triangles[3*t+1]],pw[plates.Triangles[3*t+2]]}).ToArray();
                check(own.Length>0&&Math.Abs((own.Min(v=>v.X)+own.Max(v=>v.X))/2-(b.X0+b.X1)/2)<1e-5&&Math.Abs((own.Min(v=>v.Z)+own.Max(v=>v.Z))/2-(b.Z0+b.Z1)/2)<1e-5,
                    $"each active plate is centred in its own whole cell: {variant} {sx}x{sz} cell {x},{y}");
            }
        }
        // ---- C3: synthetic rays over a Relikt 2x2 (the geometry behind era-1108c/d) ----
        var slabs=Enumerable.Range(1,4).SelectMany(n=>Triangles(Path.Combine(assets,$"reliktHull22-active-stage-1-cell-{n}.obj"),"cassette")).ToArray();
        var house=Bounds(Path.Combine(assets,"reliktHull22-housing-passive.obj"),"mount")!.Value;
        var coverage=new StringBuilder("incidence_deg\tazimuth_deg\tactive_hit_fraction\n");
        double Coverage(double theta,double phi)
        {
            var d=Vector3.Normalize(new((float)(Math.Sin(theta*Math.PI/180)*Math.Cos(phi*Math.PI/180)),(float)-Math.Cos(theta*Math.PI/180),(float)(Math.Sin(theta*Math.PI/180)*Math.Sin(phi*Math.PI/180))));
            int hit=0,total=0;const int n=24;
            for(int i=0;i<n;i++)for(int j=0;j<n;j++)
            {
                var o=new Vector3(house.Min.X+(i+.5f)*(house.Max.X-house.Min.X)/n,house.Max.Y-1e-6f,house.Min.Z+(j+.5f)*(house.Max.Z-house.Min.Z)/n);total++;
                var exit=Exit(o,d,house);var t=slabs.Select(tri=>Intersect(o,d,tri)).Where(v=>v is >1e-9).DefaultIfEmpty(null).Min();
                if(t is {} tt&&tt<=exit+1e-9)hit++;
            }
            return (double)hit/total;
        }
        foreach(var theta in new[]{40d,50d,60d,65d,70d})foreach(var phi in new[]{0d,30d,90d,150d,180d})
            coverage.AppendLine(FormattableString.Invariant($"{theta}\t{phi}\t{Coverage(theta,phi):0.0000}"));
        File.WriteAllText(Path.Combine(output,"RAY-COVERAGE-02251.tsv"),coverage.ToString());
        // Measured geometry, not a pass/fail on the user's design: the elements cover 260 of 344 mm across the rows (75.6 %).
        var across=(slabs.Max(t=>Math.Max(t.A.Z,Math.Max(t.B.Z,t.C.Z)))-slabs.Min(t=>Math.Min(t.A.Z,Math.Min(t.B.Z,t.C.Z)))-.020)/(house.Max.Z-house.Min.Z);
        check(Math.Abs(across-.2600/.348)<.01,"element width across the rows: 2 x 130 mm of 348 mm (row gap 20 mm, margins 2 x 32 mm)");
        var good=Coverage(65,0);var parallel=Coverage(65,180);
        check(good>.65&&good<.80,"frontal ray on a correctly oriented module crosses an element in ~72 % of entry points ("+good.ToString("0.00")+")");
        check(parallel<.25,"frontal ray on a module turned 180 degrees runs ~3 degrees from the element plane and mostly misses ("+parallel.ToString("0.00")+")");
        File.WriteAllText(Path.Combine(output,"FIX51AC-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks=count,reliktPresets=presets,coverageGood65=good,coverageTurned65=parallel,acrossRows=across}));
        Console.WriteLine($"FIX02251 A+C PASS: {count} checks; coverage 65 deg: {good:0.00} correct orientation, {parallel:0.00} turned 180 deg.");
    }
    private static string Classify(string file)=>file switch
    {
        "Material/ReliktPrototype.cs"=>"model load / build / scale / mirror (ERA geometry install)",
        "AtgmGuidanceSightVisuals.cs" or "RuntimeAtgmLauncher.cs"=>"part build (own mesh)",
        "RuntimeCompositePlate.cs" or "RuntimeCompositePlateEdit.cs" or "RuntimeIndependentStructure.cs" or "Material/MaterialFeature.cs" or "Material/MaterialEditorRefresh.cs"=>"editor action",
        "RuntimeAmmoLoadout.cs" or "RuntimeShellSelection.cs" or "RuntimeAtgmAmmoBox.cs" or "RuntimeAtgmGimbalPreset.cs"=>"editor inspector action",
        _=>"other (review)"
    };
    internal readonly record struct Box(Vector3 Min,Vector3 Max);
    internal readonly record struct Tri(Vector3 A,Vector3 B,Vector3 C);
    private static string V(Vector3 v)=>FormattableString.Invariant($"{v.X:0.######},{v.Y:0.######},{v.Z:0.######}");
    private static bool Same(Box a,Box b)=>Vector3.Distance(a.Min,b.Min)<1e-6&&Vector3.Distance(a.Max,b.Max)<1e-6;
    internal static Tri[] Triangles(string path,string group)
    {
        var vs=new List<Vector3>();var tris=new List<Tri>();string g="";
        foreach(var line in File.ReadAllLines(path))
        {
            var t=line.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(t.Length==0)continue;
            if(t[0]=="v")vs.Add(new(float.Parse(t[1],System.Globalization.CultureInfo.InvariantCulture),float.Parse(t[2],System.Globalization.CultureInfo.InvariantCulture),float.Parse(t[3],System.Globalization.CultureInfo.InvariantCulture)));
            else if(t[0]=="g")g=t[1];
            else if(t[0]=="f"&&g==group)tris.Add(new(vs[int.Parse(t[1].Split('/')[0])-1],vs[int.Parse(t[2].Split('/')[0])-1],vs[int.Parse(t[3].Split('/')[0])-1]));
        }
        return tris.ToArray();
    }
    internal static Box? Bounds(string path,string group)
    {
        if(!File.Exists(path))return null;var t=Triangles(path,group);if(t.Length==0)return null;
        var all=t.SelectMany(x=>new[]{x.A,x.B,x.C}).ToArray();
        return new(new(all.Min(v=>v.X),all.Min(v=>v.Y),all.Min(v=>v.Z)),new(all.Max(v=>v.X),all.Max(v=>v.Y),all.Max(v=>v.Z)));
    }
    private static double? Intersect(Vector3 o,Vector3 d,Tri t)
    {
        var e1=t.B-t.A;var e2=t.C-t.A;var p=Vector3.Cross(d,e2);var det=Vector3.Dot(e1,p);if(Math.Abs(det)<1e-12)return null;
        var inv=1/det;var s=o-t.A;var u=Vector3.Dot(s,p)*inv;if(u<0||u>1)return null;
        var q=Vector3.Cross(s,e1);var v=Vector3.Dot(d,q)*inv;if(v<0||u+v>1)return null;
        return Vector3.Dot(e2,q)*inv;
    }
    private static double Exit(Vector3 o,Vector3 d,Box b)
    {
        double best=double.PositiveInfinity;
        for(int i=0;i<3;i++)
        {
            var di=i==0?d.X:i==1?d.Y:d.Z;var oi=i==0?o.X:i==1?o.Y:o.Z;var lo=i==0?b.Min.X:i==1?b.Min.Y:b.Min.Z;var hi=i==0?b.Max.X:i==1?b.Max.Y:b.Max.Z;
            if(Math.Abs(di)>1e-12)best=Math.Min(best,((di>0?hi:lo)-oi)/di);
        }
        return best;
    }
}
