using System.Reflection.Metadata.Ecma335;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Read metadata only: no Unity/game assemblies are loaded or executed.
internal static class AssemblyChecks
{
    public static void Run(string kitPath, string pluginPath)
    {
        using var kitFile = File.OpenRead(kitPath);
        using var kitPe = new PEReader(kitFile);
        var kit = kitPe.GetMetadataReader();
        foreach (var name in new[] { "CanvasView", "Painter", "CanvasPointer" })
        {
            var type = kit.TypeDefinitions.Select(kit.GetTypeDefinition).Single(t => kit.GetString(t.Namespace) == "SprocketUiKit" && kit.GetString(t.Name) == name);
            Require((type.Attributes & System.Reflection.TypeAttributes.Public) != 0, "Installed Kit exposes public " + name);
        }

        using var pluginFile = File.OpenRead(pluginPath);
        using var pluginPe = new PEReader(pluginFile);
        var plugin = pluginPe.GetMetadataReader();
        var pluginType = plugin.TypeDefinitions.Select(plugin.GetTypeDefinition)
            .Single(t => plugin.GetString(t.Namespace) == "SprocketAIOrders" && plugin.GetString(t.Name) == "Plugin");
        var dependencies = ReadDependencies(plugin, pluginType);
        Require(dependencies.Any(d => d.Guid == "sprocket.uikit" && d.Range == ">=0.1.2 <0.2.0"), "Built DLL declares sprocket.uikit dependency >=0.1.2 <0.2.0");
        Require(dependencies.Any(d => d.Guid == "sprocket.keybinds" && d.Range == ">=0.1.7 <0.2.0"), "Built DLL declares sprocket.keybinds dependency >=0.1.7 <0.2.0");
        Require(dependencies.Any(d => d.Guid == "sprocket.expedition" && d.Flags == 2), "Built DLL declares sprocket.expedition as soft dependency");
        Require(dependencies.Count == 3, "Built DLL declares exactly three dependencies");
        Require(plugin.GetAssemblyDefinition().Version == new Version(0, 5, 4, 0), "Built assembly version is 0.5.4.0");
        var userStrings = new HashSet<string>(StringComparer.Ordinal);
        for (var handle = MetadataTokens.UserStringHandle(1); !handle.IsNil; handle = plugin.GetNextHandle(handle))
            userStrings.Add(plugin.GetUserString(handle));
        foreach (var hash in new[]
        {
            "3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B",
            "77FC601748991DC2101435C6B72C7A5A4B3DC3C68429B349C2EFEE711F6D5388",
        })
            Require(userStrings.Contains(hash), "Built DLL contains qualified GameAssembly hash " + hash);
        var patchCount = plugin.CustomAttributes.Select(plugin.GetCustomAttribute).Count(attr =>
        {
            if (attr.Constructor.Kind != HandleKind.MemberReference) return false;
            var member = plugin.GetMemberReference((MemberReferenceHandle)attr.Constructor);
            if (member.Parent.Kind != HandleKind.TypeReference) return false;
            var type = plugin.GetTypeReference((TypeReferenceHandle)member.Parent);
            return plugin.GetString(type.Namespace) == "HarmonyLib" && plugin.GetString(type.Name) == "HarmonyPatch";
        });
        Require(patchCount == 3, "Built DLL contains exactly three HarmonyPatch attributes");
        foreach (var reference in plugin.TypeReferences.Select(plugin.GetTypeReference)
            .Where(t => plugin.GetString(t.Namespace) == "SprocketUiKit"))
        {
            var name = plugin.GetString(reference.Name);
            var definition = kit.TypeDefinitions.Select(kit.GetTypeDefinition).Single(t => kit.GetString(t.Namespace) == "SprocketUiKit" && kit.GetString(t.Name) == name);
            Require((definition.Attributes & System.Reflection.TypeAttributes.Public) != 0, "Plugin references public Kit type " + name);
        }
    }

    // BepInDependency(string guid, string range) or BepInDependency(string guid, DependencyFlags flags): the
    // constructor signature tells which one; the attribute blob then holds a string or an int32.
    private static List<(string Guid, string? Range, int? Flags)> ReadDependencies(MetadataReader plugin, TypeDefinition pluginType)
    {
        var list = new List<(string Guid, string? Range, int? Flags)>();
        foreach (var handle in pluginType.GetCustomAttributes())
        {
            var attr = plugin.GetCustomAttribute(handle);
            if (attr.Constructor.Kind != HandleKind.MemberReference) continue;
            var member = plugin.GetMemberReference((MemberReferenceHandle)attr.Constructor);
            if (member.Parent.Kind != HandleKind.TypeReference) continue;
            var type = plugin.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (plugin.GetString(type.Name) != "BepInDependency") continue;
            var signature = plugin.GetBlobReader(member.Signature);
            signature.ReadByte(); signature.ReadByte(); signature.ReadByte(); signature.ReadByte(); // header, count, return type, first parameter (string)
            var secondIsString = signature.ReadByte() == 0x0E;
            var blob = plugin.GetBlobReader(attr.Value);
            if (blob.ReadUInt16() != 1) continue;
            var guid = blob.ReadSerializedString() ?? "";
            if (secondIsString) list.Add((guid, blob.ReadSerializedString(), null));
            else list.Add((guid, null, blob.ReadInt32()));
        }
        return list;
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }
}
