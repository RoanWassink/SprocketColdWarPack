using Mono.Cecil;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

// Read the native Address attributes afresh, across ALL assemblies (not just Sprocket.*).
// Fail closed if the generator's recorded binary hash or supported game image changes.
internal sealed class NativeAudit : IDisposable
{
    private static readonly HashSet<string> SupportedHashes=new(StringComparer.OrdinalIgnoreCase)
    {
        "3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B",
        "77FC601748991DC2101435C6B72C7A5A4B3DC3C68429B349C2EFEE711F6D5388"
    };
    // Key: native assembly + ':' + full method signature. Every exception needs a nonempty rationale.
    // No exceptions approved for 0.5.1.
    private static readonly Dictionary<string,string> Exceptions=new();
    private sealed record Entry(string Assembly,string Name,uint Rva,uint Length);
    private readonly List<AssemblyDefinition> assemblies=new();
    private readonly Dictionary<string,TypeDefinition> types=new();
    private readonly Dictionary<uint,List<Entry>> addresses=new();
    private readonly Dictionary<string,Entry> targets=new();
    private readonly string report;
    private string? failure;
    private string hash="",generatorHash="";
    private int patches,shared,exceptions;
    private readonly List<string> errors=new();
    internal NativeAudit(string interop,string report,string? nativeDump=null)
    {
        this.report=report;
        try
        {
            string game=Path.GetFullPath(Path.Combine(interop,"..",".."));
            string image=Path.Combine(game,"GameAssembly.dll");
            using(var stream=File.OpenRead(image))hash=Convert.ToHexString(SHA256.Create().ComputeHash(stream));
            if(!SupportedHashes.Contains(hash))throw new InvalidDataException("GameAssembly SHA256 differs from the reviewed builds; regenerate/review native evidence first");
            string dump;
            if(nativeDump!=null)
            {
                dump=Path.GetFullPath(nativeDump);
                using var provenance=JsonDocument.Parse(File.ReadAllText(Path.Combine(dump,"provenance.json")));
                var root=provenance.RootElement;
                if(root.GetProperty("GameAssemblySHA256").GetString()!=hash)throw new InvalidDataException("Cpp2IL dump image SHA256 mismatch");
                string metadata=Path.Combine(game,"Sprocket_Data","il2cpp_data","Metadata","global-metadata.dat");
                if(root.GetProperty("MetadataSHA256").GetString()!=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(metadata))))throw new InvalidDataException("Cpp2IL dump metadata SHA256 mismatch");
                var inventory=root.GetProperty("Assemblies");
                var files=Directory.GetFiles(dump,"*.dll");
                if(files.Length!=inventory.EnumerateObject().Count())throw new InvalidDataException("Cpp2IL assembly inventory mismatch");
                foreach(string file in files)
                    if(!inventory.TryGetProperty(Path.GetFileName(file),out var expected)||expected.GetString()!=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))))throw new InvalidDataException("Cpp2IL assembly SHA256 mismatch: "+Path.GetFileName(file));
                generatorHash="SHA256 manifest: "+hash;
            }
            else
            {
            string generator=Path.Combine(game,"MLLoader","MelonLoader","Dependencies","Il2CppAssemblyGenerator");
            generatorHash=Regex.Match(File.ReadAllText(Path.Combine(generator,"Config.cfg")),"GameAssemblyHash\\s*=\\s*\"([0-9A-Fa-f]+)\"").Groups[1].Value;
            using(var stream=File.OpenRead(image))
                if(!Convert.ToHexString(SHA512.Create().ComputeHash(stream)).Equals(generatorHash,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Cpp2IL generator input SHA512 does not match current GameAssembly");
                dump=Path.Combine(generator,"Cpp2IL","cpp2il_out");
            }
            foreach(string file in Directory.GetFiles(dump,"*.dll").OrderBy(x=>x))
            {
                var asm=AssemblyDefinition.ReadAssembly(file);assemblies.Add(asm);
                foreach(var type in asm.MainModule.GetTypes())
                {
                    types.Add(asm.Name.Name+":"+type.FullName,type);
                    foreach(var method in type.Methods)
                    {
                        var entry=Read(method);
                        if(entry==null)continue;
                        if(!addresses.TryGetValue(entry.Rva,out var aliases))addresses.Add(entry.Rva,aliases=new());
                        aliases.Add(entry);
                    }
                }
            }
            if(addresses.Count==0)throw new InvalidDataException("Empty native inventory");
            Console.WriteLine($"ICF image SHA256 {hash}; dump provenance MATCH; {assemblies.Count} assemblies, {addresses.Values.Sum(x=>x.Count)} native methods");
        }
        catch(Exception ex){failure=ex.Message;Console.WriteLine("FAIL ICF provenance: "+failure);}
    }
    private static Entry? Read(MethodDefinition method)
    {
        var attr=method.CustomAttributes.SingleOrDefault(a=>a.AttributeType.Name=="AddressAttribute");
        if(attr==null)return null;
        var fields=attr.Fields.ToDictionary(f=>f.Name,f=>(string)f.Argument.Value);
        uint Number(string key)=>fields.TryGetValue(key,out var value)?Convert.ToUInt32(value.Replace("0x",""),16):0;
        uint rva=Number("RVA");
        return rva==0?null:new(method.Module.Assembly.Name.Name,method.FullName,rva,Number("Length"));
    }
    private static string NormalType(string name)
    {
        name=name.Replace("Il2CppSystem.","System.");
        const string prefix="Il2CppInterop.Runtime.InteropTypes.Arrays.";
        if(name==prefix+"Il2CppStringArray")return "System.String[]";
        foreach(string array in new[]{"Il2CppReferenceArray`1<","Il2CppStructArray`1<"})
            if(name.StartsWith(prefix+array)&&name.EndsWith(">"))return NormalType(name[(prefix.Length+array.Length)..^1])+"[]";
        return name;
    }
    internal bool Check(MethodDefinition method,string label)
    {
        patches++;
        if(failure!=null)return false;
        string typeKey=method.Module.Assembly.Name.Name+":"+method.DeclaringType.FullName;
        if(!types.TryGetValue(typeKey,out var type))return Error("native type missing: "+label);
        // Generated explicit-interface wrapper names replace dots with underscores.
        var candidates=type.Methods.Where(m=>(m.Name==method.Name||m.Name.Replace('.','_')==method.Name)&&
            m.Parameters.Select(p=>NormalType(p.ParameterType.FullName)).SequenceEqual(method.Parameters.Select(p=>NormalType(p.ParameterType.FullName)))&&
            NormalType(m.ReturnType.FullName)==NormalType(method.ReturnType.FullName)).ToList();
        if(candidates.Count!=1)return Error($"native signature missing/ambiguous ({candidates.Count}): {label}");
        var entry=Read(candidates[0]);
        if(entry==null||entry.Length==0)return Error("native RVA/length missing: "+label);
        string key=entry.Assembly+":"+entry.Name;
        targets[key]=entry;
        var aliases=addresses[entry.Rva];
        bool folded=aliases.Count>1;
        if(folded)
        {
            shared++;
            if(!Exceptions.TryGetValue(key,out string? reason)||string.IsNullOrWhiteSpace(reason))
                return Error($"SHARED RVA 0x{entry.Rva:X} length {entry.Length}: {label}; {aliases.Count-1} other methods: "+string.Join("; ",aliases.Where(a=>a!=entry).Take(8).Select(a=>a.Assembly+":"+a.Name)));
            exceptions++;
            Console.WriteLine($"EXCEPTION ICF {key}: {reason}");
        }
        Console.WriteLine($"ICF 0x{entry.Rva:X} {entry.Length} bytes; other methods={aliases.Count-1}: {label}");
        return true;
    }
    private bool Error(string message){errors.Add(message);Console.WriteLine("FAIL ICF "+message);return false;}
    internal bool Finish()
    {
        var text=new StringBuilder("# ICF check 0.7.0\n\n");
        text.AppendLine($"GameAssembly SHA256: `{hash}`. Generator provenance: `{generatorHash}` (matched current binary). Fresh Address attributes read from **all {assemblies.Count} Cpp2IL assemblies**, {addresses.Values.Sum(x=>x.Count)} native methods; cross-assembly aliases included. Hash changes, missing native signatures/lengths and unapproved aliases fail closed.");
        text.AppendLine($"\n{patches} patch registrations; {targets.Count} distinct native targets; {shared} shared registrations; {exceptions} exceptions; {errors.Count+(failure==null?0:1)} ICF problems.\n");
        text.AppendLine("| Target (native signature) | RVA | Length (bytes) | Other methods at RVA |\n|---|---:|---:|---:|");
        foreach(var entry in targets.Values.OrderBy(x=>x.Name))text.AppendLine($"| `{entry.Name}` | 0x{entry.Rva:X} | {entry.Length} | {addresses[entry.Rva].Count-1} |");
        text.AppendLine("\n## Removed target and cleanup\n");
        var removed=addresses.Values.SelectMany(x=>x).SingleOrDefault(x=>x.Name=="System.Void Sprocket.Vehicles.VehicleComponent::Release()");
        if(removed!=null)text.AppendLine($"Removed `VehicleComponent.Release`: 0x{removed.Rva:X}, {removed.Length} bytes, {addresses[removed.Rva].Count-1} other methods across all assemblies. Existing unique `VehiclePowertrain.Clear` prefix retires the bridge and collision index before native teardown; BuildLayout and engine rebuild retain their existing cleanup. The render child remains parented to the native powertrain.");
        text.AppendLine("\nExceptions: none. The exception dictionary in NativeAudit.cs requires a full native signature and a nonempty reason before an alias can be accepted.\n");
        if(failure!=null)text.AppendLine("FAIL provenance: "+failure);
        foreach(var error in errors)text.AppendLine("FAIL: "+error);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);File.WriteAllText(report,text.ToString());
        Console.WriteLine($"{(failure==null&&errors.Count==0?"PASS":"FAIL")} ICF: {patches} registrations / {targets.Count} unique targets / {shared} shared / {exceptions} exceptions; report {report}");
        return failure==null&&errors.Count==0;
    }
    public void Dispose(){foreach(var asm in assemblies)asm.Dispose();}
}
