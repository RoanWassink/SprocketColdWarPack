namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.100: automatic RuntimeMaterialLooks handoff when sprocket.floppyparts is loaded,
/// lazy RuntimeCustomTextures initialization, and preservation of EB armour protection and 48 hooks.
/// </summary>
internal static class Feat022100FloppyPartsHandoffTests
{
    private static readonly string[] MaterialIds={"ebWood","ebRubber","ebFabric","ebGlass"};

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;
        void Check(bool ok,string msg){checks++;if(!ok)failures++;sink(ok,"FEAT100: "+msg);}

        // 1. Pure MaterialLook handoff policy
        Check(MaterialLook.FloppyPartsGuid=="sprocket.floppyparts","1 FloppyPartsGuid is exact 'sprocket.floppyparts'");

        string? queriedGuid=null;
        Check(!MaterialLook.OverlaysEnabled(guid=>{queriedGuid=guid;return guid==MaterialLook.FloppyPartsGuid;}),
            "1 OverlaysEnabled returns false when sprocket.floppyparts is loaded");
        Check(queriedGuid==MaterialLook.FloppyPartsGuid,"1 OverlaysEnabled queries exact sprocket.floppyparts GUID");

        Check(MaterialLook.OverlaysEnabled(guid=>guid=="sprocket.keybinds"),
            "1 OverlaysEnabled returns true when only unrelated plugins are loaded");
        Check(MaterialLook.OverlaysEnabled(_=>false),
            "1 OverlaysEnabled returns true when sprocket.floppyparts is not loaded");
        Check(MaterialLook.OverlaysEnabled(null),
            "1 OverlaysEnabled returns true on null callback");
        Check(MaterialLook.OverlaysEnabled(_=>throw new InvalidOperationException("chainloader unavailable")),
            "1 OverlaysEnabled falls back safely to true when callback throws");

        foreach(var id in MaterialIds)
            Check(MaterialLook.For(id)?.Id==id,"1 MaterialLook.For keeps resolving "+id);

        // 2. MaterialLookCache.Count tracking
        int disposed=0;
        var cache=new MaterialLookCache<string>(_=>disposed++);
        Check(cache.Count==0,"2 fresh MaterialLookCache starts empty");
        cache.Put(1L,"sig-a","view-a");
        cache.Put(2L,"sig-b","view-b");
        Check(cache.Count==2,"2 MaterialLookCache.Count reflects 2 entries");
        cache.Remove(1L);
        Check(cache.Count==1&&disposed==1,"2 Remove decrements Count and disposes once");
        cache.Clear();
        Check(cache.Count==0&&disposed==2,"2 Clear resets Count to 0 and disposes remaining entry");

        // 3. Source invariants
        var root=Fix02228Tests.SourceRoot();
        string Read(string rel)=>File.ReadAllText(Path.Combine(root,rel)).Replace("\r\n","\n");

        var runtime=Read("RuntimeMaterialLooks.cs");
        Check(runtime.Contains("IL2CPPChainloader.Instance?.Plugins",StringComparison.Ordinal)
            &&runtime.Contains("MaterialLook.OverlaysEnabled(",StringComparison.Ordinal),
            "3 RuntimeMaterialLooks checks IL2CPPChainloader.Instance?.Plugins dynamically via MaterialLook.OverlaysEnabled");
        Check(runtime.Contains("SuppressedByFloppyParts()",StringComparison.Ordinal)
            &&runtime.Contains("suppressedLogged=false",StringComparison.Ordinal),
            "3 RuntimeMaterialLooks exposes SuppressedByFloppyParts() and resets suppressedLogged in Clear()");

        foreach(var methodPrefix in new[]{
            "internal static void NoteModel(VehicleObjectModel model)\n    {\n        if(SuppressedByFloppyParts())return;",
            "internal static void Note(PlateStructure plate,bool changed=false)\n    {\n        if(SuppressedByFloppyParts())return;",
            "internal static void Overlay(bool active)\n    {\n        if(SuppressedByFloppyParts())return;",
            "internal static void BeginEdit(){if(SuppressedByFloppyParts())return;",
            "if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;\n        if(SuppressedByFloppyParts())return;",
            "private static void Build(PlateStructure plate,bool designer)\n    {\n        if(SuppressedByFloppyParts())return;"})
        {
            Check(runtime.Contains(methodPrefix,StringComparison.Ordinal),
                "3 Guarded entry point: "+methodPrefix.Split('\n')[0]);
        }

        Check(runtime.Contains("if(plate==null)return;long key=plate.Pointer.ToInt64();",StringComparison.Ordinal),
            "3 Release guards against null plate");
        Check(runtime.Contains("RuntimeCustomTextures.Initialize();",StringComparison.Ordinal)
            &&runtime.IndexOf("RuntimeCustomTextures.Initialize();",StringComparison.Ordinal)
              >runtime.IndexOf("private static Material? GetMaterial(MaterialLook look)",StringComparison.Ordinal),
            "3 RuntimeCustomTextures.Initialize() is deferred into GetMaterial");
        Check(!runtime.Contains("HarmonyPatch",StringComparison.Ordinal)
            &&!runtime.Contains("new Texture2D",StringComparison.Ordinal)
            &&!runtime.Contains("SetArmourMaterial(",StringComparison.Ordinal),
            "3 RuntimeMaterialLooks has no HarmonyPatch, Texture2D allocation, or SetArmourMaterial write");

        var adapter=Read("RuntimeCassettePlateVisual.cs");
        Check(!adapter.Contains("RuntimeCustomTextures.Initialize(",StringComparison.Ordinal),
            "3 RuntimeCassettePlateVisual.Configure no longer eagerly initializes RuntimeCustomTextures");

        var plugin=Read("Plugin.cs");
        Check(!plugin.Contains("sprocket.floppyparts",StringComparison.Ordinal),
            "3 Plugin.cs does not add a BepInDependency on sprocket.floppyparts");
        Check(plugin.Contains("\"0.22.102\"",StringComparison.Ordinal)
            &&Read("SprocketExpandedBallistics.csproj").Contains("<Version>0.22.102</Version>",StringComparison.Ordinal)
            &&Read("RELEASE-NOTES.md").Contains("## 0.22.102",StringComparison.Ordinal),
            "3 Plugin.cs, csproj, and RELEASE-NOTES.md carry version 0.22.102");

        int hooks=Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories)
            .Where(f=>{var r=f.Replace('\\','/');return !r.Contains("/tests/")&&!r.Contains("/bin/")&&!r.Contains("/obj/");})
            .Sum(f=>File.ReadLines(f).Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal)));
        Check(hooks==48,$"3 Non-test [HarmonyPatch line count stays 48 (actual={hooks})");

        // 4. Armour protection & menu availability data untouched
        var visibility=Read("Material/MaterialMenuVisibility.cs");
        var availability=Read("Material/MaterialAvailability.cs");
        var editor=Read("Material/defaults/sprocket.expandedballistics.materials.editor.json");
        foreach(var id in MaterialIds)
        {
            var tech=Read($"native-data/Technology/{id}.json");
            Check(visibility.Contains($"\"{id}\"",StringComparison.Ordinal)
                &&availability.Contains($"\"{id}\"",StringComparison.Ordinal)
                &&editor.Contains($"\"{id}\"",StringComparison.Ordinal)
                &&tech.Contains($"\"type\": \"{id}\"",StringComparison.Ordinal)
                &&tech.Contains("\"rhaFactor\"",StringComparison.Ordinal),
                "4 Armour protection/availability/menu preserved for "+id);
        }

        Console.WriteLine($"FEAT100: {checks} checks, {failures} failures");
    }
}
