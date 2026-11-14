using System.Text.Json;

namespace SprocketExpedition.Core.Encounters;

public sealed class CatalogueJsonException : Exception
{
    public CatalogueJsonException(string message)
        : base(message)
    {
    }

    public CatalogueJsonException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

// Strict, dependency-free loader for authored catalogue JSON. Unknown or
// duplicate properties are rejected so a typo cannot silently drop content.
// Loading validates shape and safe local references only; evidence and minimum
// content checks are performed by CatalogueValidator.
public static class CatalogueJson
{
    private static readonly string[] RootKeys =
    {
        "label", "fixture", "version", "priceUnitId", "eras"
    };

    private static readonly string[] EraKeys =
    {
        "era", "maps", "enemies"
    };

    private static readonly string[] MapKeys =
    {
        "mapId", "contentHash", "layouts", "requiredMods", "qualification"
    };

    private static readonly string[] EnemyKeys =
    {
        "blueprintId", "snapshotHash", "role", "threatScore",
        "checkedPrice", "requiredMods", "qualification", "formationFamily",
        "massKg", "minimumRisk", "maxPerBattle"
    };

    private static readonly string[] ModKeys =
    {
        "id", "version"
    };

    public static CatalogueDefinition Load(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new CatalogueJsonException("catalogue JSON is empty");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new CatalogueJsonException(
                $"catalogue JSON is malformed: {exception.Message}", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new CatalogueJsonException(
                    "catalogue root must be a JSON object");
            }

            EnsureAllowedKeys(root, "catalogue", RootKeys);

            string label = RequiredString(root, "label", "catalogue");
            bool isFixture = RequiredBool(root, "fixture", "catalogue");
            string version = RequiredString(root, "version", "catalogue");
            string priceUnitId = RequiredString(root, "priceUnitId", "catalogue");

            var erasElement = RequiredArray(root, "eras", "catalogue");
            var eras = new List<CatalogueEraContent>();
            int eraIndex = 0;
            foreach (var eraElement in erasElement.EnumerateArray())
            {
                eras.Add(ParseEra(
                    eraElement,
                    $"catalogue.eras[{eraIndex}]",
                    priceUnitId));
                eraIndex++;
            }

            return new CatalogueDefinition(
                version, label, isFixture, priceUnitId, eras);
        }
    }

    private static CatalogueEraContent ParseEra(
        JsonElement element,
        string context,
        string priceUnitId)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new CatalogueJsonException($"{context}: era entry must be an object");
        }

        EnsureAllowedKeys(element, context, EraKeys);
        string eraId = RequiredString(element, "era", context);

        var mapsElement = RequiredArray(element, "maps", context);
        var maps = new List<MapEntry>();
        int mapIndex = 0;
        foreach (var mapElement in mapsElement.EnumerateArray())
        {
            maps.Add(ParseMap(mapElement, eraId, $"{context}.maps[{mapIndex}]"));
            mapIndex++;
        }

        var enemiesElement = RequiredArray(element, "enemies", context);
        var enemies = new List<EnemyDesignEntry>();
        int enemyIndex = 0;
        foreach (var enemyElement in enemiesElement.EnumerateArray())
        {
            enemies.Add(ParseEnemy(
                enemyElement,
                eraId,
                $"{context}.enemies[{enemyIndex}]",
                priceUnitId));
            enemyIndex++;
        }

        return new CatalogueEraContent(
            new EraReference(eraId), maps, enemies);
    }

    private static MapEntry ParseMap(
        JsonElement element,
        string eraId,
        string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new CatalogueJsonException($"{context}: map entry must be an object");
        }

        EnsureAllowedKeys(element, context, MapKeys);

        string mapId = RequiredString(element, "mapId", context);
        string contentHash = RequiredString(element, "contentHash", context);

        var layoutsElement = RequiredArray(element, "layouts", context);
        var layouts = new List<string>();
        int layoutIndex = 0;
        foreach (var layout in layoutsElement.EnumerateArray())
        {
            layouts.Add(RequiredStringValue(
                layout, $"{context}.layouts[{layoutIndex}]", "layout"));
            layoutIndex++;
        }

        var mods = ParseMods(element, context);
        ContentQualification qualification = ParseEnum<ContentQualification>(
            RequiredString(element, "qualification", context),
            context,
            "qualification");

        return new MapEntry(
            new EraReference(eraId),
            mapId,
            contentHash,
            layouts,
            mods,
            qualification);
    }

    private static EnemyDesignEntry ParseEnemy(
        JsonElement element,
        string eraId,
        string context,
        string priceUnitId)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new CatalogueJsonException($"{context}: enemy entry must be an object");
        }

        EnsureAllowedKeys(element, context, EnemyKeys);

        string blueprintId = RequiredString(element, "blueprintId", context);
        string snapshotHash = RequiredString(element, "snapshotHash", context);
        EnemyRole role = ParseEnum<EnemyRole>(
            RequiredString(element, "role", context), context, "role");
        decimal threatScore = RequiredDecimal(element, "threatScore", context);
        decimal checkedPrice = RequiredDecimal(element, "checkedPrice", context);
        var mods = ParseMods(element, context);
        var qualification = ParseEnum<ContentQualification>(
            RequiredString(element, "qualification", context),
            context,
            "qualification");
        string? formationFamily = OptionalString(element, "formationFamily", context);
        int? massKg = OptionalInt32(element, "massKg", context);
        if (massKg is <= 0)
        {
            throw new CatalogueJsonException(
                $"{context}: property 'massKg' must be a positive integer");
        }
        int? maxPerBattle = OptionalInt32(element, "maxPerBattle", context);
        if (maxPerBattle is <= 0)
            throw new CatalogueJsonException($"{context}: maxPerBattle must be a positive integer");
        EncounterRisk? minimumRisk = OptionalEnum<EncounterRisk>(
            element, "minimumRisk", context);

        var blueprint = new BlueprintReference(
            blueprintId,
            snapshotHash,
            new EraReference(eraId),
            mods);

        return new EnemyDesignEntry(
            blueprint,
            role,
            threatScore,
            new ScenarioValue(checkedPrice, priceUnitId),
            qualification)
        {
            FormationFamily = formationFamily,
            MassKg = massKg,
            MinimumRisk = minimumRisk,
            MaxPerBattle = maxPerBattle
        };
    }

    private static IReadOnlyList<ModRequirement> ParseMods(
        JsonElement parent,
        string context)
    {
        var modsElement = RequiredArray(parent, "requiredMods", context);
        var mods = new List<ModRequirement>();
        int modIndex = 0;
        foreach (var modElement in modsElement.EnumerateArray())
        {
            string modContext = $"{context}.requiredMods[{modIndex}]";
            if (modElement.ValueKind != JsonValueKind.Object)
            {
                throw new CatalogueJsonException($"{modContext}: mod entry must be an object");
            }

            EnsureAllowedKeys(modElement, modContext, ModKeys);
            mods.Add(new ModRequirement(
                RequiredString(modElement, "id", modContext),
                RequiredString(modElement, "version", modContext)));
            modIndex++;
        }

        return mods;
    }

    private static void EnsureAllowedKeys(
        JsonElement element,
        string context,
        string[] allowedKeys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new CatalogueJsonException(
                    $"{context}: duplicate property '{property.Name}'");
            }

            if (!allowedKeys.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new CatalogueJsonException(
                    $"{context}: unknown property '{property.Name}'");
            }
        }
    }

    private static JsonElement RequiredProperty(
        JsonElement element,
        string name,
        string context)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            throw new CatalogueJsonException(
                $"{context}: missing required property '{name}'");
        }

        return value;
    }

    private static JsonElement RequiredArray(
        JsonElement element,
        string name,
        string context)
    {
        var value = RequiredProperty(element, name, context);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new CatalogueJsonException(
                $"{context}: property '{name}' must be an array");
        }

        return value;
    }

    private static string RequiredString(
        JsonElement element,
        string name,
        string context) =>
        RequiredStringValue(
            RequiredProperty(element, name, context), context, name);

    private static string? OptionalString(
        JsonElement element,
        string name,
        string context)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return RequiredStringValue(value, context, name);
    }

    private static int? OptionalInt32(
        JsonElement element,
        string name,
        string context)
    {
        if (!element.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var parsed))
        {
            throw new CatalogueJsonException(
                $"{context}: property '{name}' must be a 32-bit integer");
        }

        return parsed;
    }

    private static T? OptionalEnum<T>(
        JsonElement element,
        string name,
        string context)
        where T : struct, Enum
    {
        if (!element.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        string text = RequiredStringValue(value, context, name);
        return ParseEnum<T>(text, context, name);
    }

    private static string RequiredStringValue(
        JsonElement value,
        string context,
        string name)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new CatalogueJsonException(
                $"{context}: property '{name}' must be a string");
        }

        return value.GetString()
            ?? throw new CatalogueJsonException(
                $"{context}: property '{name}' must not be null");
    }

    private static bool RequiredBool(
        JsonElement element,
        string name,
        string context)
    {
        var value = RequiredProperty(element, name, context);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new CatalogueJsonException(
                $"{context}: property '{name}' must be a boolean")
        };
    }

    private static decimal RequiredDecimal(
        JsonElement element,
        string name,
        string context)
    {
        var value = RequiredProperty(element, name, context);
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetDecimal(out decimal parsed))
        {
            throw new CatalogueJsonException(
                $"{context}: property '{name}' must be a decimal number");
        }

        return parsed;
    }

    private static T ParseEnum<T>(
        string value,
        string context,
        string name)
        where T : struct, Enum
    {
        if (!Enum.TryParse(value, ignoreCase: false, out T parsed)
            || !Enum.IsDefined(typeof(T), parsed))
        {
            throw new CatalogueJsonException(
                $"{context}: '{name}' value '{value}' is not a known {typeof(T).Name}");
        }

        return parsed;
    }
}
