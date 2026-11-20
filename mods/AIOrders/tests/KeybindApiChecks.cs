using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// Offline evidence from the actual referenced DLL: never loads or executes game code.
internal static class KeybindApiChecks
{
    public static void Run(string path)
    {
        using var file = File.OpenRead(path);
        using var pe = new PEReader(file);
        var md = pe.GetMetadataReader();
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(c => unchecked((ushort)c.Value));
        var methods = new Dictionary<string, List<string>>();
        foreach (var typeHandle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(typeHandle); var typeName = md.GetString(type.Name);
            foreach (var handle in type.GetMethods())
            {
                var method = md.GetMethodDefinition(handle); var name = md.GetString(method.Name);
                if (!(typeName == "ModKeybind" && name == "get_WasPressedThisFrame") && !(typeName == "InputRuntime" && name == "Sample")) continue;
                var bytes = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
                var lines = new List<string>();
                for (var i = 0; i < bytes.Length;)
                {
                    var offset = i; ushort code = bytes[i++];
                    if (code == 0xfe) code = (ushort)(0xfe00 | bytes[i++]);
                    var op = codes[code]; var operand = "";
                    var size = op.OperandType switch {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineI or OperandType.ShortInlineVar or OperandType.ShortInlineBrTarget => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, i),
                        _ => 4 };
                    if (op.OperandType is OperandType.InlineMethod or OperandType.InlineField)
                    {
                        var token = MetadataTokens.EntityHandle(BitConverter.ToInt32(bytes, i));
                        operand = token.Kind switch {
                            HandleKind.MethodDefinition => md.GetString(md.GetMethodDefinition((MethodDefinitionHandle)token).Name),
                            HandleKind.MemberReference => md.GetString(md.GetMemberReference((MemberReferenceHandle)token).Name),
                            HandleKind.FieldDefinition => md.GetString(md.GetFieldDefinition((FieldDefinitionHandle)token).Name),
                            _ => token.Kind.ToString() };
                    }
                    else if (size > 0) operand = Convert.ToHexString(bytes.AsSpan(i, size));
                    lines.Add($"{offset:X4}: {op.Name} {operand}"); i += size;
                }
                methods[name] = lines;
                Console.WriteLine($"EVIDENCE {typeName}.{name}: {path}");
                foreach (var line in lines) Console.WriteLine(line);
            }
        }
        var getter = methods["get_WasPressedThisFrame"];
        Require(getter.Any(l => l.EndsWith("call Sample")) && getter.Any(l => l.Contains("ldflda State")) &&
            getter.Any(l => l.EndsWith("ldfld Item2")) && !getter.Any(l => l.Contains("stfld") || l.Contains("stsfld")),
            "Installed Keybinds pressed getter reads cached State.Pressed without clearing it");
        var sample = methods["Sample"];
        var frame = sample.FindIndex(l => l.EndsWith("ldsfld sampledFrame"));
        Require(frame >= 0 && sample.Skip(frame).Take(5).Any(l => l.EndsWith("call get_frameCount")) &&
            sample.Skip(frame).Take(5).Any(l => l.Contains("bne.un")) && sample.Skip(frame).Take(5).Any(l => l.EndsWith("ret ")),
            "Installed Keybinds Sample returns on repeated reads in the same frame");
    }
    private static void Require(bool value, string name)
    { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
}
