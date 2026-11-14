using Sprocket.Vehicles;
using Il2CppSystem.Runtime.Serialization;

namespace SprocketCarouselAutoloader;

internal static class CarouselPersistence
{
    private const string EnabledKey = "sprocketCarouselEnabledV1", CannonKey = "sprocketCarouselCannonV1";
    private const string LayoutKey = "sprocketCarouselLayoutV2";
    internal readonly record struct Settings(bool Enabled, int CannonId, CarouselLayout Layout);

    internal static void Write(SerializationInfo info, bool enabled, int cannonId, CarouselLayout layout)
    {
        info.AddValue(EnabledKey, enabled);
        info.AddValue(CannonKey, cannonId);
        info.AddValue(LayoutKey, (int)layout);
    }

    internal static Settings Read(SerializationInfo info)
    {
        var entries = info.GetEnumerator();
        var keys = new HashSet<string>();
        while (entries.MoveNext()) keys.Add(entries.Name);
        var enabledKey = keys.Contains(EnabledKey) ? EnabledKey : null;
        var cannonKey = keys.Contains(CannonKey) ? CannonKey : null;
        var layoutKey = keys.Contains(LayoutKey) ? LayoutKey : null;
        return new(enabledKey != null && info.GetBoolean(enabledKey!),
            cannonKey != null ? info.GetInt32(cannonKey) : -1,
            layoutKey != null ? (CarouselLayout)Math.Clamp(info.GetInt32(layoutKey), 0, 1) : CarouselLayout.HorizontalCassette);
    }

    internal static void CheckNativeJsonRoundTrip()
    {
        // Synthetic blueprint only: no user files, assets or scene objects are loaded or changed.
        const string fixture = "{\"v\":\"2.0\",\"header\":{\"v\":\"0.2\",\"name\":\"Carousel serialization check\",\"gameVersion\":\"0.2.55.5\",\"mass\":0,\"creationDate\":\"1945.01.01\",\"class\":\"\",\"desc\":\"\",\"cost\":0},\"blueprints\":[],\"meshes\":[],\"objects\":[{\"guid\":\"00000000-0000-0000-0000-000000000000\",\"vuid\":1,\"pvuid\":-1,\"flags\":0,\"turretBasket\":304,\"vanillaSentinel\":2671}]}";
        var serializer = new VehicleBlueprintSerializer();
        var old = serializer.DeserializeJSON(fixture);
        if (Read(old.VehicleObjects[0].State) != new Settings(false, -1, CarouselLayout.HorizontalCassette))
            throw new InvalidOperationException("Default blueprint settings failed.");
        foreach (var layout in new[] { CarouselLayout.HorizontalCassette, CarouselLayout.VerticalCharge })
        foreach (var enabled in new[] { false, true })
        foreach (var cannonId in new[] { -1, 275, 370 })
        {
            var blueprint = serializer.DeserializeJSON(fixture);
            // Loaded object State includes identity keys; freshly captured State does not.
            // Match ToBlueprint's shape so GetObjectData can add identity keys once.
            var captured = new SerializationInfo(Il2CppInterop.Runtime.Il2CppType.Of<Il2CppSystem.Object>(),
                new FormatterConverter().Cast<IFormatterConverter>());
            var entries = blueprint.VehicleObjects[0].State.GetEnumerator();
            while (entries.MoveNext())
                if (entries.Name is not ("guid" or "vuid" or "pvuid" or "flags"))
                    captured.AddValue(entries.Name, entries.Value, entries.ObjectType);
            blueprint.VehicleObjects[0].State = captured;
            Write(blueprint.VehicleObjects[0].State, enabled, cannonId, layout);
            BustleRuntime.WriteSettings(blueprint.VehicleObjects[0].State, enabled, cannonId, enabled);
            var json = serializer.SerializeToJSON(blueprint, true);
            var restored = serializer.DeserializeJSON(json);
            var info = restored.VehicleObjects[0].State;
            if (Read(info) != new Settings(enabled, cannonId, layout) || BustleRuntime.ReadSettings(info) != (enabled, cannonId, true) ||
                BustleRuntime.ReadMirrorSetting(info) != enabled ||
                info.GetInt32("vanillaSentinel") != 2671 ||
                info.GetInt32("turretBasket") != 304)
                throw new InvalidOperationException("Native blueprint JSON round-trip lost carousel or vanilla data.");
        }
    }
}
