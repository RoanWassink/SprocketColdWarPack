using BepInEx;
namespace SprocketExpandedBallistics;

internal static class CassetteDefaults
{
    internal static (IReadOnlyDictionary<string,CassetteModel> Models,string Json,IReadOnlyDictionary<string,ProtectionFactorTable> Tables) Load(MaterialReactionCatalogue reactions,ArmourResponseCatalogue? responses)
    {
        var path=Path.Combine(Paths.ConfigPath,CassetteCatalogue.FileName);string json;
        if(File.Exists(path))json=File.ReadAllText(path);
        else
        {
            using var stream=typeof(CassetteDefaults).Assembly.GetManifestResourceStream("ExpandedBallistics.Materials.CassetteTemplates")??throw new FileNotFoundException("Cassette defaults missing.");
            using var reader=new StreamReader(stream);json=reader.ReadToEnd();
        }
        var catalogue=CassetteCatalogue.Parse(json);
        if(responses==null)throw new FormatException("Armour responses are required to reject ERA cassette constituents.");
        var reactive=new HashSet<string>(StringComparer.Ordinal);
        foreach(var response in responses.Responses)if(response.Kind is "lightEra" or "heavyEra")foreach(var id in response.CompatibleMaterialIds)reactive.Add(id);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(Paths.GameRootPath,"Sprocket_Data/StreamingAssets/Technology"));
        var models=new Dictionary<string,CassetteModel>(StringComparer.Ordinal);
        foreach(var preset in catalogue.Presets)
        {
            CassetteModel model;
            try{model=new CassetteModel(preset,materials,reactions,reactive);}
            catch(FormatException ex) when(preset.Id.StartsWith("user_",StringComparison.Ordinal))
            {Plugin.ModLog.LogWarning("[Cassettes] User preset unavailable: "+preset.MaterialId+": "+ex.Message+"; native resistance only.");continue;}
            // Exact native signature prevents silently applying an edited sidecar
            // to already-installed technology, or a different saved preset version.
            if(!materials.TryGetValue(preset.MaterialId,out var native)||native!=model.Passive)
            {
                if(preset.Id.StartsWith("user_",StringComparison.Ordinal))
                {Plugin.ModLog.LogWarning("[Cassettes] User preset Technology missing/different: "+preset.MaterialId+"; native resistance only until regenerated.");continue;}
                throw new FormatException("Cassette Technology differs from sidecar: "+preset.MaterialId+"; regenerate native data.");
            }
            models.Add(preset.MaterialId,model);
        }
        // 0.22.28: the protection tables are computed here, at load (and after a composer save reloads the snapshot), never per shot.
        var tables=ProtectionFactorTable.ForMaterials(models,reactions,materials,MaterialLabel,
            (id,reason)=>Plugin.ModLog.LogWarning("[Cassettes] Loose material "+id+": no protection table ("+reason+"); native resistance only."),reactive);
        return (models,json,tables);
    }
    private static string MaterialLabel(string id)
    {
        try{var m=SprocketExpandedBallistics.Materials.MaterialDatabase.Materials.FirstOrDefault(x=>x.Id==id);return m==null?EraDisplayName.Material(id):SprocketExpandedBallistics.Materials.MaterialDatabase.MenuLabel(m);}
        catch(Exception){return id;}
    }
}
