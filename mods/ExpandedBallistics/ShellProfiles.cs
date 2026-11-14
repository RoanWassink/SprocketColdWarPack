using System.Text.Json;

namespace SprocketExpandedBallistics;

internal sealed record ShellFlight(string Propulsion, string Guidance, string Carrier);

internal sealed record ShellProfile(string Id, string Label, DartSettings Settings,
    string Behavior = "ap", double ChemicalPenetrationMm = 0,
    double NativeExplosivePower = 0, double SpallMultiplier = 1,
    double ConeHalfAngleDegrees = 90, double ExplosionScale = 1, double SecondPlatePenetrationFactor = .15,
    double AirGapLossPerCalibre = .35, double ReferenceCalibreMm = 100, AtgmSettings? Atgm = null,ShellNativeDate? MinimumDate=null,
    ShellFlight? Flight=null,IReadOnlyList<string>? NativeTechnologyIds=null,WireOptions? Wire=null,string ThreatClass="unknown",double TandemPrecursorPenetrationMm=0,IReadOnlyList<string>? ArmourThreatTags=null,
    string KineticKind="",string ChemicalSource="profile",double StandoffFreeCalibres=0,
    ShellNativeDate? RetiredAfter=null,bool KeepAfterRetirement=false)
{
    internal string EffectiveKineticKind=>KineticKind.Length==0?"ap":KineticKind;
}

// Managed data only: the same validation runs before UI, previews and native shots.
internal static class ShellProfiles
{
    internal const int MaximumProfiles = 32;
    internal const string Vanilla = "vanilla";
    internal const string LegacyDart = "apfsds";
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "id", "label", "penetratorDiameterFactor", "penetratorLengthInCalibres", "penetratorDensity",
        "velocityEfficiency", "velocityMultiplier", "maximumVelocityFactor", "maximumVelocity",
        "penetrationQuality", "fragmentDamageMultiplier"
    };
    private static readonly HashSet<string> Optional = new(StringComparer.Ordinal)
    { "kineticKind", "carrierDiameterFactor", "carrierMassFraction", "chemicalSource", "standoffFreeCalibres", "retiredAfter", "keepAfterRetirement", "armourThreatTags", "tandemPrecursorPenetrationMm", "threatClass", "guidanceTransport", "wireMaximumLength", "wireRetentionSeconds", "wireDisplayWidth", "wireSampleDistance", "flight", "nativeTechnologyIds", "minimumDate", "minimumEra", "behavior", "chemicalPenetrationMm", "nativeExplosivePower", "spallMultiplier", "coneHalfAngleDegrees", "explosionScale", "secondPlatePenetrationFactor", "airGapLossPerCalibre", "referenceCalibreMm", "flightSpeed", "maxTurnRate", "maximumFlightTime", "guidanceDelay", "guidanceMode", "launchSpeed", "launchSpeedMode", "launchSpeedMultiplier", "acceleration", "motorDelay", "motorBurnTime", "coastDeceleration", "topAttackAltitude", "topAttackClimbAngle", "topAttackDiveAngle", "guidanceLaw", "leadDistanceM", "rangePattern", "humpHeightM", "humpRangeMinM", "humpRangeMaxM", "humpTerminalDistanceM", "humpRampDistanceM", "directRangeBelowM", "shortRangeThresholdM", "shortRangeGuidanceDelay", "spiralAmplitudeM", "spiralFrequencyHz", "disturbanceAmplitudeM", "disturbanceFrequencyHz", "speedCalibration", "initialElevationDegrees" };

    internal static IReadOnlyList<ShellProfile> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        CheckFields(root, new HashSet<string>(StringComparer.Ordinal) { "schemaVersion", "profiles" });
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new FormatException("Unsupported shell profile schemaVersion; expected 1.");
        var profiles = root.GetProperty("profiles");
        if (profiles.ValueKind != JsonValueKind.Array || profiles.GetArrayLength() is < 1 or > MaximumProfiles)
            throw new FormatException("Expected 1 to 32 shell profiles.");
        var result = new List<ShellProfile>();
        var ids = new HashSet<string>(StringComparer.Ordinal) { Vanilla };
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Vanilla ammunition" };
        foreach (var item in profiles.EnumerateArray())
        {
            CheckFields(item, Fields, Optional);
            var id = item.GetProperty("id").GetString() ?? "";
            var label = item.GetProperty("label").GetString() ?? "";
            if (id.Length is < 1 or > 40 || id.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')) || !ids.Add(id))
                throw new FormatException($"Invalid, duplicate or reserved profile id: {id}.");
            if (string.IsNullOrWhiteSpace(label) || label.Length > 80 || label.Any(char.IsControl) || !labels.Add(label))
                throw new FormatException($"Invalid or duplicate profile label for {id}.");
            double ReadNumber(string name, JsonElement value)
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
                    throw new FormatException($"Profile '{id}': {name} is {value.GetRawText()}; expected a numeric value.");
                return number;
            }
            double Number(string name) => ReadNumber(name, item.GetProperty(name));
            double? OptionalNumber(string name) => item.TryGetProperty(name,out var value) ? ReadNumber(name, value) : null;
            var settings = new DartSettings(Number("penetratorDiameterFactor"), Number("penetratorLengthInCalibres"),
                Number("penetratorDensity"), Number("velocityEfficiency"), Number("maximumVelocityFactor"),
                Number("maximumVelocity"), Number("penetrationQuality"), Number("fragmentDamageMultiplier"),
                Number("velocityMultiplier"), OptionalNumber("carrierDiameterFactor"), OptionalNumber("carrierMassFraction"));
            ShellBallistics.ValidateSettings(settings, id);
            var behavior = item.TryGetProperty("behavior",out var mode) ? mode.GetString() ?? "" : id is "apfsds" or "aphe" ? id : "ap";
            double Option(string key,double fallback) => item.TryGetProperty(key,out var value) ? ReadNumber(key, value) : fallback;
            string TextOption(string key,string fallback) => item.TryGetProperty(key,out var value) ? value.ValueKind==JsonValueKind.String ? value.GetString()! : "<invalid type>" : fallback;
            ShellFlight? flight=null;
            if(item.TryGetProperty("flight",out var flightJson))
            {
                CheckFields(flightJson,new HashSet<string>(StringComparer.Ordinal){"propulsion","guidance","carrier"});
                flight=new(flightJson.GetProperty("propulsion").GetString()??"",flightJson.GetProperty("guidance").GetString()??"",flightJson.GetProperty("carrier").GetString()??"");
            }
            IReadOnlyList<string>? required=null;
            if(item.TryGetProperty("nativeTechnologyIds",out var technologyJson))
            {
                if(technologyJson.ValueKind!=JsonValueKind.Array)throw new FormatException($"Profile '{id}': nativeTechnologyIds must be an array.");
                required=technologyJson.EnumerateArray().Select(t=>t.ValueKind==JsonValueKind.String?t.GetString()!:"").ToArray();
            }
            var needsFlight=flight==null?ShellBalance.IsAtgm(behavior):flight.Propulsion=="rocket"||flight.Guidance!="none";
            var hasFlightTuning=item.EnumerateObject().Any(property=>AtgmFlightProfile.FlatKeys.Contains(property.Name));
            if(hasFlightTuning&&!needsFlight)
                throw new FormatException($"Profile '{id}': extended flight settings require a rocket/guided profile.");
            AtgmFlightProfile? flightProfile=hasFlightTuning
                ? new AtgmFlightProfile(
                    TextOption("guidanceLaw","legacy"),Option("leadDistanceM",25),
                    TextOption("rangePattern","none"),Option("humpHeightM",1),Option("humpRangeMinM",200),
                    Option("humpRangeMaxM",1200),Option("humpTerminalDistanceM",80),Option("humpRampDistanceM",40),
                    Option("directRangeBelowM",120),Option("shortRangeThresholdM",150),Option("shortRangeGuidanceDelay",.05),
                    Option("spiralAmplitudeM",0),Option("spiralFrequencyHz",1.5),
                    Option("disturbanceAmplitudeM",0),Option("disturbanceFrequencyHz",.8),
                    TextOption("speedCalibration","motor"),Option("initialElevationDegrees",0))
                : null;
            ShellNativeDate? retiredAfter=null;
            if(item.TryGetProperty("retiredAfter",out var retiredJson))
            {
                if(retiredJson.ValueKind!=JsonValueKind.String||
                    !DateTime.TryParseExact(retiredJson.GetString(),"yyyy.MM.dd",System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,out var retiredDate))
                    throw new FormatException($"Profile '{id}': retiredAfter must be a date in yyyy.MM.dd format.");
                retiredAfter=new ShellNativeDate(retiredDate.Year,retiredDate.Month,retiredDate.Day);
            }
            var keepAfterRetirement=false;
            if(item.TryGetProperty("keepAfterRetirement",out var keepJson))
            {
                if(keepJson.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new FormatException($"Profile '{id}': keepAfterRetirement must be a JSON boolean.");
                keepAfterRetirement=keepJson.GetBoolean();
            }
            ShellNativeDate? minimumDate=null;
            if(item.TryGetProperty("minimumDate",out var minimumJson))
            {
                var minimum=minimumJson.ValueKind==JsonValueKind.String?minimumJson.GetString():null;
                if(!ShellNativeDate.TryParse(minimum,out var parsedMinimum)||!parsedMinimum.Valid)
                    throw new FormatException($"Profile '{id}': minimumDate must be a date in yyyy.MM.dd format.");
                minimumDate=parsedMinimum;
            }
            if(item.TryGetProperty("minimumEra",out var eraJson))
            {
                // 0.22.8 and older stored an era name. Migrate to that era's
                // start date; a custom era that is not a built-in fails closed.
                var era=eraJson.ValueKind==JsonValueKind.String?eraJson.GetString():null;
                var migrated=LegacyEraStart(era);
                if(migrated is null)throw new FormatException($"Profile '{id}': minimumEra is unsupported; use minimumDate.");
                if(minimumDate is {} existing&&existing!=migrated.Value)
                    throw new FormatException($"Profile '{id}': minimumDate and minimumEra disagree.");
                minimumDate=migrated;
            }
            var profile = new ShellProfile(id,label,settings,behavior,
                Option("chemicalPenetrationMm",0),Option("nativeExplosivePower",0),
                Option("spallMultiplier",1),Option("coneHalfAngleDegrees",90),Option("explosionScale",1),Option("secondPlatePenetrationFactor",behavior=="hesh"?.1:.15),Option("airGapLossPerCalibre",behavior=="hesh"?12:.35),Option("referenceCalibreMm",100),
                needsFlight ? new AtgmSettings(Option("flightSpeed",200),Option("maxTurnRate",20),Option("maximumFlightTime",25),Option("guidanceDelay",.25),
                    flight?.Guidance??TextOption("guidanceMode","sight"), item.TryGetProperty("launchSpeed",out var launch) ? ReadNumber("launchSpeed",launch) : null,
                    TextOption("launchSpeedMode","fixed"),Option("launchSpeedMultiplier",1),Option("acceleration",0),Option("motorDelay",0),Option("motorBurnTime",0),Option("coastDeceleration",0),
                    Option("topAttackAltitude",60),Option("topAttackClimbAngle",25),Option("topAttackDiveAngle",55),flightProfile) : null,
                MinimumDate:minimumDate,Flight:flight,NativeTechnologyIds:required,ThreatClass:TextOption("threatClass","unknown"),TandemPrecursorPenetrationMm:Option("tandemPrecursorPenetrationMm",0),
                Wire:item.EnumerateObject().Any(p=>p.Name=="guidanceTransport"||p.Name.StartsWith("wire",StringComparison.Ordinal))
                    ?new WireOptions(TextOption("guidanceTransport","current"),Option("wireMaximumLength",4000),Option("wireRetentionSeconds",20),Option("wireDisplayWidth",.008),Option("wireSampleDistance",2)):null,
                KineticKind:TextOption("kineticKind",""),ChemicalSource:TextOption("chemicalSource","profile"),
                StandoffFreeCalibres:Option("standoffFreeCalibres",behavior is "heat" or "atgm" or "atgm_gun" ? 2d : 0d),
                RetiredAfter:retiredAfter,KeepAfterRetirement:keepAfterRetirement);
            if(profile.ThreatClass is not ("unknown" or "unitaryAntitankGrenade"))
                throw new FormatException($"Profile '{id}': invalid threatClass; expected unknown or unitaryAntitankGrenade.");
            if(profile.ThreatClass=="unitaryAntitankGrenade" && (profile.Behavior!="heat" || profile.Flight is {Guidance:not "none"}))
                throw new FormatException($"Profile '{id}': unitaryAntitankGrenade requires unguided heat behavior.");
            if(!double.IsFinite(profile.TandemPrecursorPenetrationMm)||profile.TandemPrecursorPenetrationMm<0||profile.TandemPrecursorPenetrationMm>2000)
                throw new FormatException($"Profile '{id}': tandemPrecursorPenetrationMm must be finite0..2000.");
            if(profile.TandemPrecursorPenetrationMm>0&&(ShellBalance.ImpactBehavior(profile)!="heat"||profile.ThreatClass!="unknown"))
                throw new FormatException($"Profile '{id}': tandem native probe requires HEAT and unknown threatClass; cannot declare unitary grenade.");
            ShellPayload.Validate(profile);
            if(item.TryGetProperty("armourThreatTags",out var tagsJson))
            {
                if(tagsJson.ValueKind!=JsonValueKind.Array||tagsJson.GetArrayLength()>16)throw new FormatException($"Profile '{id}': armourThreatTags must contain at most 16 identifiers.");
                var tags=tagsJson.EnumerateArray().Select(t=>t.ValueKind==JsonValueKind.String?t.GetString()!:"").ToArray();
                if(tags.Any(t=>t.Length is <1 or >40||t=="fragment"||t.Any(c=>!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')&&c is not ('_' or '-' or '.')))||tags.Distinct(StringComparer.Ordinal).Count()!=tags.Length)
                    throw new FormatException($"Profile '{id}': invalid or duplicate armour threat tag; fragment is reserved for native secondary fragments.");
                profile=profile with{ArmourThreatTags=Array.AsReadOnly(tags)};
            }
            if(profile.RetiredAfter is {} retired&&ShellEraSelection.EarliestAvailability(profile) is {} earliest&&retired<=earliest)
                throw new FormatException($"Profile '{id}': retiredAfter must be later than its earliest availability ({earliest.Year:0000}.{earliest.Month:00}.{earliest.Day:00}).");
            result.Add(profile);
        }
        return result;
    }

    internal static ShellNativeDate? LegacyEraStart(string? era)
    {
        var index=ShellEraCatalogue.Find(ShellEraCatalogue.BuiltIn,era);
        if(index<0)return null;
        var start=ShellEraCatalogue.BuiltIn[index].Start;
        return new ShellNativeDate(start.Year,start.Month,start.Day);
    }

    private static void CheckFields(JsonElement item, HashSet<string> allowed, HashSet<string>? optional = null)
    {
        if (item.ValueKind != JsonValueKind.Object) throw new FormatException("Expected a JSON object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in item.EnumerateObject())
            if ((!allowed.Contains(property.Name) && optional?.Contains(property.Name) != true) || !seen.Add(property.Name))
                throw new FormatException($"Unknown or duplicate shell setting: {property.Name}.");
        if (!allowed.IsSubsetOf(seen)) throw new FormatException("Missing required shell profile settings.");
    }

    internal static string CreateDefault(DartSettings settings) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        profiles = new[] { new
        {
            id = LegacyDart, label = "APFSDS",
            penetratorDiameterFactor = settings.DiameterRatio,
            penetratorLengthInCalibres = settings.LengthInCalibres,
            penetratorDensity = settings.Density,
            velocityEfficiency = settings.VelocityEfficiency,
            velocityMultiplier = settings.VelocityMultiplier,
            maximumVelocityFactor = settings.MaxVelocityFactor,
            maximumVelocity = settings.MaxVelocity,
            penetrationQuality = settings.PenetrationQuality,
            fragmentDamageMultiplier = settings.DamageMultiplier,
            retiredAfter = (string?)null
        }, new
        {
            id = "aphe", label = "APHE (enhanced spall)",
            penetratorDiameterFactor = .9, penetratorLengthInCalibres = 2d,
            penetratorDensity = 7800d, velocityEfficiency = .1,
            velocityMultiplier = 1d, maximumVelocityFactor = 1d,
            maximumVelocity = 2200d, penetrationQuality = 1.125,
            fragmentDamageMultiplier = 1d, retiredAfter = (string?)"1960.01.01"
        } }
    }, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

    internal static string Resolve(string? id, bool legacyEnabled, IReadOnlyList<ShellProfile> profiles)
    {
        var requested = id ?? (legacyEnabled ? LegacyDart : Vanilla);
        if(requested==LegacyDart && !profiles.Any(p=>p.Id==LegacyDart) && profiles.Any(p=>p.Id=="apfsds_long"))return "apfsds_long";
        return requested == Vanilla || profiles.Any(p => p.Id == requested) ? requested : Vanilla;
    }
    // Record equality compares the list fields by reference. Migration needs the
    // semantic comparison: all scalar fields plus sequence equality for the lists.
    internal static bool SameProfile(ShellProfile a,ShellProfile b)
    {
        if(ReferenceEquals(a,b))return true;
        if(!(a.NativeTechnologyIds??Array.Empty<string>()).SequenceEqual(b.NativeTechnologyIds??Array.Empty<string>()))return false;
        if(!(a.ArmourThreatTags??Array.Empty<string>()).SequenceEqual(b.ArmourThreatTags??Array.Empty<string>()))return false;
        return (a with{NativeTechnologyIds=null,ArmourThreatTags=null}) == (b with{NativeTechnologyIds=null,ArmourThreatTags=null});
    }
}

