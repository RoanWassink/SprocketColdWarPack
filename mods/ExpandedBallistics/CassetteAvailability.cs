using System.Globalization;
namespace SprocketExpandedBallistics;

/// <summary>Whether a cassette preset can be used on a vehicle, with the short reason the list shows.</summary>
internal readonly record struct CassetteAvailabilityResult(bool Available,string Reason);

/// <summary>
/// 0.22.7, pure. The game only knows a Technology inside the vehicle's technology frame, which is built for the
/// vehicle's date: a 1985 cassette is "unknown armour material" (default values, no cassette effect) on a vehicle
/// dated before 1985. The composer therefore dims presets the target vehicle cannot use and says why.
/// Dates are <c>yyyy.MM.dd</c> text; a vehicle date is the native TechDate key <c>year*10000+month*100+day</c>.
/// </summary>
internal static class CassetteAvailability
{
    internal const string Restart="Restart Sprocket to use";
    // Both the reaction snapshot and the exact native material lookup must agree.
    // An unreadable lookup cannot authorize an Apply operation.
    internal static CassetteAvailabilityResult ForUse(string date,int? vehicleKey,bool modelLoaded,bool? inFrame)
    {
        if(DateBlocks(date,vehicleKey))return new(false,From(date));
        return modelLoaded&&inFrame==true?new(true,From(date)):new(false,Restart);
    }
    /// <summary>
    /// 0.22.14: a material loaded at startup is in every frame; one registered live during this session only reaches the
    /// frames of Play and of later designer sessions when the native frame hook is installed.
    /// </summary>
    internal static bool LiveReachesPlay(bool registeredLive,bool frameHook)=>!registeredLive||frameHook;
    /// <summary>
    /// 0.22.14: the result of a live registration. <paramref name="designer"/> is <see cref="ForUse"/> on the designer vehicle;
    /// without the frame hook the hit in Play would stay native, so it is never "ready to apply" but "Restart Sprocket to use".
    /// </summary>
    internal static CassetteAvailabilityResult AfterLiveRegistration(CassetteAvailabilityResult designer,bool frameHook)=>
        designer.Available&&!frameHook?new(false,Restart):designer;
    internal const int Sentinel=99991231;
    internal static int? Key(string? date)=>DateTime.TryParseExact(date,"yyyy.MM.dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)?d.Year*10000+d.Month*100+d.Day:null;
    internal static int Key(int year,int month,int day)=>year*10000+month*100+day;
    /// <summary>"1985.01.01" from a key (the final-era sentinel 9999.12.31 included).</summary>
    internal static string Text(int key)=>FormattableString.Invariant($"{key/10000:0000}.{key/100%100:00}.{key%100:00}");
    internal static string Year(string date)=>date.Length>=4?date[..4]:date;
    /// <summary>"From 1985" for the list tag and the tooltips.</summary>
    internal static string From(string date)=>"From "+Year(date);
    /// <summary>
    /// <paramref name="vehicleKey"/> null: no vehicle context, nothing is dimmed. <paramref name="inFrame"/> is the native
    /// <c>Tech.TryGetTech</c> answer for the preset's material (null: not asked). A date that is reached but still not in the
    /// frame means the Technology file is not loaded (restart / reload), reported separately from the date.
    /// </summary>
    internal static CassetteAvailabilityResult Evaluate(string presetDate,int? vehicleKey,bool? inFrame)
    {
        var from=From(presetDate);
        if(vehicleKey is not {} vehicle)return new(true,from);
        if(Key(presetDate) is {} need&&need>vehicle)return new(false,from);
        if(inFrame==false)return new(false,"Not loaded");
        return new(true,from);
    }
    /// <summary>The sentence behind a refusal: the preset, the date it exists from and the vehicle's date.</summary>
    internal static string Explain(string label,string presetDate,int? vehicleKey,CassetteAvailabilityResult result)
    {
        if(result.Available)return "";
        if(result.Reason=="Not loaded")return label+" is not in this vehicle's technology yet: restart Sprocket, or press Reload armour materials.";
        return $"{label} exists from {presetDate}; this vehicle is dated {(vehicleKey is {} v?Text(v):"unknown")}.";
    }
    /// <summary>True when the preset is dated after the vehicle: the game keeps it out of the vehicle technology frame.</summary>
    internal static bool DateBlocks(string? presetDate,int? vehicleKey)=>
        vehicleKey!=Sentinel&&Key(presetDate) is {} need&&vehicleKey is {} vehicle&&need>vehicle;
    /// <summary>The hit-path date gate: the Coldwar3 sentinel 9999.12.31 allows every preset.</summary>
    internal static bool AvailableOnVehicle(string? presetDate,int? vehicleKey)=>!DateBlocks(presetDate,vehicleKey);
    /// <summary>
    /// One concrete reason why a cassette must stay native-only on this vehicle, or null when it may react.
    /// The caller logs it once per plate; the same text goes into the hit report.
    /// </summary>
    internal static string? HitBlockReason(string? presetDate,int? vehicleKey,bool? inFrame)
    {
        if(!AvailableOnVehicle(presetDate,vehicleKey))
            return "Material not in vehicle technology ("+Year(presetDate??"")+"+); native resistance only.";
        if(inFrame==false)return "Technology file missing or not loaded; native resistance only.";
        return null;
    }
    /// <summary>
    /// What to tell the player when a plate carries a cassette the game does not know on this vehicle (it then falls back to
    /// default values: no cassette effect). <paramref name="presetDate"/> is null when the preset itself is unknown.
    /// </summary>
    internal static string UnknownMaterial(string materialId,string label,string? presetDate,int? vehicleKey,bool shortForm)
    {
        if(presetDate!=null&&DateBlocks(presetDate,vehicleKey))
            return shortForm
                ?$"Unknown material: {label} exists from {Year(presetDate)}, this vehicle is dated {Text(vehicleKey!.Value)}. Default values, no cassette effect."
                :$"This plate carries {label} (exists from {presetDate}), but this vehicle is dated {Text(vehicleKey!.Value)}. The game does not know the material and uses default values: no cassette effect. Apply a preset that is available now, or date the vehicle {Year(presetDate)} or later.";
        return shortForm
            ?$"Unknown material {materialId}: Technology missing or not loaded. Default values, no cassette effect."
            :$"This plate carries {label} ({materialId}), which the game does not know on this vehicle: its Technology file is missing or not loaded yet. It uses default values, so there is no cassette effect. Restart Sprocket, or apply another preset.";
    }
    /// <summary>
    /// Newest Technology date among the solid layers (a cassette is never earlier than its newest material);
    /// null when no layer has a known date.
    /// </summary>
    internal static (DateTime Date,string Material)? Newest(IEnumerable<CassetteLayer> layers,Func<string,DateTime?> materialDate)
    {
        (DateTime Date,string Material)? best=null;
        foreach(var l in layers)
        {
            if(l.Material=="air"||materialDate(l.Material) is not {} d)continue;
            if(best==null||d>best.Value.Date)best=(d,l.Material);
        }
        return best;
    }
    internal static string DateText(DateTime date)=>date.ToString("yyyy.MM.dd",CultureInfo.InvariantCulture);
}
