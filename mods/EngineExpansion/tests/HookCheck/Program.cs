// Resolves every Harmony patch in the built plugin against the installed interop assemblies (read-only).
// usage: dotnet run -- <plugin.dll> <BepInEx\interop dir>
using Mono.Cecil;
using System.Security.Cryptography;
using System.Text.Json;
string plugin=args[0],interop=args[1];
var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(interop);resolver.AddSearchDirectory(Path.Combine(interop,"..","core"));
var read=new ReaderParameters{AssemblyResolver=resolver};
var types=new Dictionary<string,TypeDefinition>();
foreach(var file in Directory.GetFiles(interop,"*.dll"))
{
    try{foreach(var t in AssemblyDefinition.ReadAssembly(file,read).MainModule.GetTypes())types.TryAdd(t.FullName,t);}catch{}
}
var asm=AssemblyDefinition.ReadAssembly(plugin,read);
int ok=0,bad=0;
foreach(var type in asm.MainModule.GetTypes())
{
    bool unsupported=type.CustomAttributes.Any(a=>a.AttributeType.Name=="HarmonyPatch"&&a.ConstructorArguments.Count>0)||
        type.Methods.Any(m=>m.CustomAttributes.Any(a=>a.AttributeType.Name is "HarmonyTargetMethod" or "HarmonyTargetMethods")||
            m.CustomAttributes.Any(a=>a.AttributeType.Name=="HarmonyPatch"&&a.ConstructorArguments.Count<2));
    if(unsupported){Console.WriteLine("FAIL unsupported Harmony target declaration: "+type.FullName);bad++;}
}
using var native=new NativeAudit(interop,args.Length>=4?args[3]:Path.Combine(Path.GetDirectoryName(plugin)!,"ICF-CHECK.md"));
foreach(var owner in asm.MainModule.GetTypes())
foreach(var patch in owner.Methods)
foreach(var attr in patch.CustomAttributes.Where(a=>a.AttributeType.Name=="HarmonyPatch"&&a.ConstructorArguments.Count>=2))
{
    var targetRef=(TypeReference)attr.ConstructorArguments[0].Value;
    string name=(string)attr.ConstructorArguments[1].Value;
    string? key=(targetRef is GenericInstanceType g?g.ElementType:targetRef).FullName;
    string label=$"{owner.Name}.{patch.Name} -> {targetRef.FullName}::{name}";
    if(!types.TryGetValue(key,out var target)){Console.WriteLine("MISSING TYPE "+label);bad++;continue;}
    var candidates=target.Methods.Where(m=>m.Name==name).ToList();
    if(attr.ConstructorArguments.Count>=3&&attr.ConstructorArguments[2].Value is CustomAttributeArgument[] argTypes)
    {
        var wanted=argTypes.Select(a=>((TypeReference)a.Value).FullName).ToArray();
        candidates=candidates.Where(m=>m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(wanted)).ToList();
    }
    if(candidates.Count!=1){Console.WriteLine($"{(candidates.Count==0?"MISSING":"AMBIGUOUS")} METHOD {label} ({candidates.Count})");bad++;continue;}
    var method=candidates[0];
    if(!native.Check(method,label))bad++;
    string? problem=null;
    foreach(var p in patch.Parameters)
    {
        if(p.Name.StartsWith("__")&&int.TryParse(p.Name[2..],out int index))
        {
            if(index>=method.Parameters.Count){problem=$"{p.Name} beyond {method.Parameters.Count} parameters";break;}
            string want=method.Parameters[index].ParameterType.FullName.TrimEnd('&'),have=p.ParameterType.FullName.TrimEnd('&');
            var wantType=method.Parameters[index].ParameterType.GetElementType().Resolve();
            // A patch may declare a base type or interface of the native parameter.
            bool assignable=want==have||have=="Il2CppSystem.Object"||IsBase(wantType,have);
            if(!assignable){problem=$"{p.Name} is {have}, native {want}";break;}
        }
        else if(p.Name=="__result"&&method.ReturnType.FullName!=p.ParameterType.FullName.TrimEnd('&')){problem=$"__result {p.ParameterType.FullName} vs {method.ReturnType.FullName}";break;}
        else if(p.Name=="__instance"&&method.IsStatic){problem="__instance on static target";break;}
    }
    if(problem!=null){Console.WriteLine($"SIGNATURE {label}: {problem}");bad++;continue;}
    Console.WriteLine($"OK {label}({string.Join(",",method.Parameters.Select(p=>p.ParameterType.Name))}) {(method.IsStatic?"static":"instance")}");ok++;
}
// Verify bytes embedded in the actual delivered DLL, not merely files beside its source.
if(args.Length>=3)
{
    string model=Path.GetFullPath(args[2]);
    using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(model,"logs","mesh-manifest.json")));
    var strings=asm.MainModule.GetType("SprocketEngineExpansion.Native.PowerpackMeshManifest").Methods.SelectMany(m=>m.HasBody?m.Body.Instructions:Enumerable.Empty<Mono.Cecil.Cil.Instruction>()).Where(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Ldstr).Select(i=>(string)i.Operand).ToHashSet();
    foreach(var entry in manifest.RootElement.EnumerateArray())
    {
        string name=entry.GetProperty("name").GetString()!,hash=entry.GetProperty("payload_sha256").GetString()!,stlHash=entry.GetProperty("stl_sha256").GetString()!;
        var resource=asm.MainModule.Resources.OfType<EmbeddedResource>().SingleOrDefault(r=>r.Name=="Powerpack."+name+".meshbin");
        bool match=resource!=null&&Convert.ToHexString(SHA256.HashData(resource.GetResourceData())).Equals(hash,StringComparison.OrdinalIgnoreCase)&&
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(model,"parts",name+".stl")))) .Equals(stlHash,StringComparison.OrdinalIgnoreCase)&&strings.Contains(hash)&&strings.Contains(stlHash);
        Console.WriteLine($"{(match?"OK":"FAIL")} DLL payload + current STL + embedded manifest: {name}");
        if(!match)bad++;
    }
}
if(!native.Finish()&&bad==0)bad++;
Console.WriteLine($"{(bad==0?"PASS":"FAIL")} {ok} Harmony targets resolved, {bad} problems");
return bad==0?0:1;

static bool IsBase(TypeDefinition? type,string name)
{
    for(var t=type;t!=null;t=t.BaseType?.Resolve())
    {
        if(t.FullName==name)return true;
        if(t.Interfaces.Any(i=>i.InterfaceType.FullName==name))return true;
    }
    return false;
}
