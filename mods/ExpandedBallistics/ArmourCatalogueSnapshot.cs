namespace SprocketExpandedBallistics;

// One validated snapshot shared by impact simulation and material descriptions.
// Editors capture their own original file text for transaction checks, then reload here.
internal sealed record ArmourCatalogueSnapshot(
    ArmourResponseCatalogue? Catalogue,
    SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmourCatalogue? Experimental,
    string? ResponseJson,
    string? Error,
    string? ExperimentalError,
    SprocketExpandedBallistics.Era.EraPartBindingCatalogue? Bindings=null,
    MaterialReactionCatalogue? MaterialReactions=null,
    string? MaterialReactionJson=null,
    string? MaterialReactionError=null,
    IReadOnlyDictionary<string,CassetteModel>? Cassettes=null,
    string? CassetteJson=null,
    IReadOnlyDictionary<string,ProtectionFactorTable>? Protection=null)
{
    internal string Revision =>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes((ResponseJson??"")+"|"+(Experimental?.Revision??"")+"|"+(MaterialReactionJson??"")+"|"+(CassetteJson??""))));
    internal static ArmourCatalogueSnapshot Stage(string? responseJson,string? experimentalJson,bool initial,string? materialJson=null)=>
        initial?Parse(responseJson,experimentalJson,materialJson):Validated(responseJson,experimentalJson,materialJson);
    internal static ArmourCatalogueSnapshot Read(Func<string?> readResponses,Func<string?> readExperimental,bool initial,Func<string?>? readMaterials=null)
    {
        var responseJson=readResponses();
        string? experimentalJson;
        string? experimentalIoError=null;
        try {experimentalJson=readExperimental();}
        catch(Exception ex) when(initial)
        {
            // Initial optional-sidecar IO failure must not remove valid legacy HEAT.
            experimentalJson=null;experimentalIoError=ex.Message;
        }
        // Reload IO failures propagate before publication, retaining the previous pair.
        string? materialJson=null;string? materialIoError=null;
        try {materialJson=readMaterials?.Invoke();}
        catch(Exception ex) when(initial){materialIoError=ex.Message;}
        var result=Stage(responseJson,experimentalJson,initial,materialJson);
        return result with{ExperimentalError=experimentalIoError??result.ExperimentalError,MaterialReactionError=materialIoError??result.MaterialReactionError};
    }
    internal static ArmourCatalogueSnapshot Validated(string? responseJson,string? experimentalJson,string? materialJson=null)
    {
        var candidate=Parse(responseJson,experimentalJson,materialJson);
        if(candidate.Error!=null||candidate.ExperimentalError!=null||candidate.MaterialReactionError!=null)throw new FormatException("Armour reload rejected; previous snapshot retained. "+(candidate.Error??candidate.ExperimentalError??candidate.MaterialReactionError));
        return candidate;
    }
    internal static ArmourCatalogueSnapshot Empty {get;}=new(null,null,null,null,null);
    internal static ArmourCatalogueSnapshot Parse(string? responseJson,string? experimentalJson,string? materialJson=null)
    {
        ArmourResponseCatalogue? catalogue=null;
        SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmourCatalogue? experimental=null;
        string? error=null;string? experimentalError=null;
        if(responseJson!=null){try{catalogue=ArmourResponses.Parse(responseJson);}catch(Exception ex){error=ex.Message;}}
        if(experimentalJson!=null){try{experimental=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(experimentalJson);}catch(Exception ex){experimentalError=ex.Message;}}
        MaterialReactionCatalogue? materialReactions=null;string? materialError=null;
        if(materialJson!=null){try{materialReactions=MaterialReactionCatalogue.Parse(materialJson);}catch(Exception ex){materialError=ex.Message;}}
        return new(catalogue,experimental,catalogue==null?null:responseJson,error,experimentalError,MaterialReactions:materialReactions,MaterialReactionJson:materialReactions==null?null:materialJson,MaterialReactionError:materialError);
    }
}
