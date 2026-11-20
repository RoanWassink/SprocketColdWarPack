// Static interop scan for Sprocket mods (read-only on game files).
// Every Unity/game API the mod DLL calls is followed into the generated interop
// wrapper IL. Four failure families are reported:
//   span     wrapper path references ReadOnlySpan`1/Span`1.GetPinnableReference,
//            missing in this interop build (0.2.0: LoadFromFile, LoadAllAssets).
//   missing  wrapper path references a member that does not resolve.
//   stripped wrapper invokes a native MethodInfo whose method has no native code
//            in GameAssembly (Cpp2IL dump of the exact binary: no Address / RVA 0).
//   alias    wrapper's NativeMethodInfoPtr field ends up holding a *different*
//            method's token (Il2CppInterop name collision; 0.2.1: the non-generic
//            AssetBundle.LoadAllAssetsAsync() invokes the generic definition
//            LoadAllAssetsAsync<T>() -> "Invalid call to method").
//   icall    wrapper resolves an internal call by name that UnityPlayer.dll lacks.
//
// usage: InteropScan <mod.dll> <gameDir> <report.txt> [--cpp2il DIR]
//        [--expect-fail SPEC]... [--expect-pass SPEC]... [--dump SPEC]... [--scan-type TYPE]...
// SPEC:  Namespace.Type::Method(ParamType,ParamType)   (parameter list optional; non-generic preferred)
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text;

if(args.Length<3){Console.Error.WriteLine("usage: InteropScan <mod.dll> <gameDir> <report.txt> [options]");return 2;}
var modPath=Path.GetFullPath(args[0]);var game=Path.GetFullPath(args[1]);var reportPath=Path.GetFullPath(args[2]);
var interopDir=Path.Combine(game,"BepInEx","interop");var coreDir=Path.Combine(game,"BepInEx","core");
var dotnetDir=Path.Combine(game,"dotnet");
var generatorDir=Path.Combine(game,"MLLoader","MelonLoader","Dependencies","Il2CppAssemblyGenerator");
var cpp2ilDir=Path.Combine(generatorDir,"Cpp2IL","cpp2il_out");
string? cpp2ilTrustNote=null;
var expectFail=new List<string>();var expectPass=new List<string>();var dumps=new List<string>();var scanTypes=new List<string>();var findStripped=new List<string>();
for(var i=3;i<args.Length;i++)
{
    if(i+1>=args.Length)return Usage("missing value after "+args[i]);
    switch(args[i])
    {
        case "--expect-fail":expectFail.Add(args[++i]);break;case "--expect-pass":expectPass.Add(args[++i]);break;
        case "--dump":dumps.Add(args[++i]);break;case "--scan-type":scanTypes.Add(args[++i]);break;
        case "--cpp2il":cpp2ilDir=Path.GetFullPath(args[++i]);break;
        // Test tooling only: a dump made by another tool (no MelonLoader Config.cfg next to it). The caller states in NOTE why
        // the dump belongs to this GameAssembly; the report repeats it. Without this option the SHA512 check below stays strict.
        case "--cpp2il-trust":cpp2ilTrustNote=args[++i];break;
        case "--find-stripped":findStripped.Add(args[++i]);break;
        default:return Usage("unknown option "+args[i]);
    }
}
var report=new StringBuilder();
report.AppendLine($"Interop scan {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
report.AppendLine($"mod={modPath}");report.AppendLine($"interop={interopDir} (read-only)");

// The native-code families are only meaningful for the exact GameAssembly the
// Cpp2IL dump was made from: MelonLoader records its SHA512 next to the dump.
var gameAssembly=Path.Combine(game,"GameAssembly.dll");
string sha512;using(var s=File.OpenRead(gameAssembly))sha512=Convert.ToHexString(SHA512.HashData(s));
var cfg=Path.Combine(generatorDir,"Config.cfg");
var recorded=File.Exists(cfg)?File.ReadLines(cfg).Select(l=>l.Trim()).FirstOrDefault(l=>l.StartsWith("GameAssemblyHash"))?.Split('"').ElementAtOrDefault(1):null;
if(!Directory.Exists(cpp2ilDir)||(cpp2ilTrustNote==null&&(recorded==null||!string.Equals(recorded,sha512,StringComparison.OrdinalIgnoreCase))))
{
    Console.Error.WriteLine($"Cpp2IL dump missing or not made from this GameAssembly (SHA512 {sha512[..16]}..., recorded {recorded?[..Math.Min(16,recorded.Length)]}...). Native checks cannot be trusted; regenerate the dump.");
    return 3;
}
if(cpp2ilTrustNote==null)report.AppendLine($"cpp2il={cpp2ilDir} (matches GameAssembly SHA512 {sha512[..16]}...)");
else report.AppendLine($"cpp2il={cpp2ilDir} (SHA512 NOT verified by the tool; GameAssembly SHA512 {sha512[..16]}...; caller states: {cpp2ilTrustNote})");
var unityPlayer=File.ReadAllBytes(Path.Combine(game,"UnityPlayer.dll"));

var resolver=new DefaultAssemblyResolver();
foreach(var d in new[]{interopDir,coreDir,dotnetDir})if(Directory.Exists(d))resolver.AddSearchDirectory(d);
var readerParams=new ReaderParameters{AssemblyResolver=resolver,ReadingMode=ReadingMode.Deferred,ReadSymbols=false};
var interopNames=new HashSet<string>(Directory.GetFiles(interopDir,"*.dll").Select(f=>Path.GetFileNameWithoutExtension(f)),StringComparer.OrdinalIgnoreCase);
var followNames=new HashSet<string>(interopNames,StringComparer.OrdinalIgnoreCase){"Il2CppInterop.Runtime","Il2CppInterop.Common"};
var checkedNames=new HashSet<string>(followNames,StringComparer.OrdinalIgnoreCase);
foreach(var f in Directory.GetFiles(coreDir,"*.dll"))checkedNames.Add(Path.GetFileNameWithoutExtension(f));
var mod=ModuleDefinition.ReadModule(modPath,readerParams);

string ScopeName(TypeReference t)
{
    while(t.IsNested&&t.DeclaringType!=null)t=t.DeclaringType;
    if(t is TypeSpecification ts)return ScopeName(ts.ElementType);
    return t.Scope switch{AssemblyNameReference a=>a.Name,ModuleDefinition m=>m.Assembly.Name.Name,ModuleReference r=>r.Name,_=>t.Scope?.Name??"?"};
}
string Sig(MethodReference m)=>$"{m.DeclaringType.FullName}::{m.Name}{(m.HasGenericParameters?"<"+string.Join(",",m.GenericParameters.Select(g=>g.Name))+">":"")}({string.Join(",",m.Parameters.Select(p=>p.ParameterType.FullName))})";
bool IsSpanPin(MemberReference m)=>m.Name=="GetPinnableReference"&&(m.DeclaringType?.Name.StartsWith("ReadOnlySpan")==true||m.DeclaringType?.Name.StartsWith("Span")==true);

// ---- Cpp2IL native table: assembly -> (metadata token -> method) ----
var cpp2ilCache=new Dictionary<string,Dictionary<int,MethodDefinition>?>(StringComparer.OrdinalIgnoreCase);
Dictionary<int,MethodDefinition>? Cpp2IlTokens(string interopAssembly)
{
    if(cpp2ilCache.TryGetValue(interopAssembly,out var hit))return hit;
    var file=Path.Combine(cpp2ilDir,interopAssembly+".dll");
    if(!File.Exists(file)&&interopAssembly.StartsWith("Il2Cpp",StringComparison.Ordinal))file=Path.Combine(cpp2ilDir,interopAssembly["Il2Cpp".Length..]+".dll");
    Dictionary<int,MethodDefinition>? map=null;
    if(File.Exists(file))
    {
        map=new();
        foreach(var t in AssemblyDefinition.ReadAssembly(file,new ReaderParameters{ReadingMode=ReadingMode.Deferred}).MainModule.GetTypes())
            foreach(var m in t.Methods)
            {
                var tok=m.CustomAttributes.FirstOrDefault(a=>a.AttributeType.Name=="TokenAttribute")?.Fields.FirstOrDefault(f=>f.Name=="Token").Argument.Value as string;
                if(tok!=null)map[Convert.ToInt32(tok,16)]=m;
            }
    }
    cpp2ilCache[interopAssembly]=map;return map;
}
long Rva(MethodDefinition m)
{
    var v=m.CustomAttributes.FirstOrDefault(a=>a.AttributeType.Name=="AddressAttribute")?.Fields.FirstOrDefault(f=>f.Name=="RVA").Argument.Value as string;
    return v==null?0:Convert.ToInt64(v,16);
}
// Final token written into each NativeMethodInfoPtr field by the type's .cctor
// (last write wins; that is exactly what the wrapper reads at run time).
var fieldTokenCache=new Dictionary<TypeDefinition,Dictionary<FieldDefinition,List<int>>>();
Dictionary<FieldDefinition,List<int>> FieldTokens(TypeDefinition type)
{
    if(fieldTokenCache.TryGetValue(type,out var hit))return hit;
    var map=new Dictionary<FieldDefinition,List<int>>();
    var cctor=type.Methods.FirstOrDefault(m=>m.Name==".cctor"&&m.HasBody);
    if(cctor!=null)
    {
        int? token=null;
        foreach(var ins in cctor.Body.Instructions)
        {
            if(ins.OpCode==OpCodes.Ldc_I4)token=(int)ins.Operand;
            else if(ins.OpCode==OpCodes.Stsfld&&ins.Operand is FieldReference fr&&fr.Name.StartsWith("NativeMethodInfoPtr_")&&token!=null)
            {
                var fd=fr.Resolve();if(fd!=null){if(!map.TryGetValue(fd,out var l))map[fd]=l=new();l.Add(token.Value);}
                token=null;
            }
        }
    }
    fieldTokenCache[type]=map;return map;
}
var unverifiedList=new SortedSet<string>();
// Native-code check of one interop wrapper; null = fine / not applicable.
string? NativeProblem(MethodDefinition md)
{
    if(!interopNames.Contains(md.Module.Assembly.Name.Name)||!md.HasBody)return null;
    var ins=md.Body.Instructions;
    // Removed from GameAssembly by IL2CPP stripping and not rebuildable by
    // Il2CppInterop: the generated wrapper only throws at run time.
    if(ins.Any(x=>x.OpCode==OpCodes.Ldstr&&(string)x.Operand=="Method unstripping failed"))
        return "stripped: removed from the game by IL2CPP; Il2CppInterop could not rebuild it (wrapper throws 'Method unstripping failed')";
    // icall family: wrapper resolves an internal call by name (unstripped Unity body).
    for(var k=0;k<ins.Count;k++)
        if(ins[k].Operand is MethodReference c&&c.Name=="ResolveICall"&&k>0&&ins[k-1].OpCode==OpCodes.Ldstr)
        {
            var name=(string)ins[k-1].Operand;
            if(IndexOf(unityPlayer,Encoding.ASCII.GetBytes(name+"\0"))<0)return $"icall '{name}' not registered in UnityPlayer.dll";
        }
    if(!ins.Any(x=>x.Operand is MethodReference c&&c.Name=="il2cpp_runtime_invoke"))return null;
    var field=ins.Where(x=>x.OpCode==OpCodes.Ldsfld&&x.Operand is FieldReference f&&f.Name.StartsWith("NativeMethodInfoPtr_")).Select(x=>((FieldReference)x.Operand).Resolve()).FirstOrDefault(f=>f!=null);
    if(field==null){unverifiedList.Add(md.FullName+" (generic method-info store)");return null;} // decided at run time by the guard
    if(!FieldTokens(field.DeclaringType).TryGetValue(field,out var tokens)||tokens.Count==0){unverifiedList.Add(md.FullName+" (no token in .cctor)");return null;}
    var token=tokens[^1];
    var table=Cpp2IlTokens(md.Module.Assembly.Name.Name);
    if(table==null){unverifiedList.Add(md.FullName+" (no Cpp2IL assembly)");return null;}
    if(!table.TryGetValue(token,out var native))return $"stripped: token 0x{token:X8} has no method in the Cpp2IL dump";
    var paramCount=md.Parameters.Count;
    if(native.Name!=md.Name||native.Parameters.Count!=paramCount||native.HasGenericParameters!=md.HasGenericParameters)
        return $"alias: field {field.Name} (written {tokens.Count}x) finally holds token 0x{token:X8} = {native.FullName}{(native.HasGenericParameters?" [generic definition]":"")}, not this wrapper's method";
    // Abstract/virtual: the wrapper dispatches through il2cpp_object_get_virtual_method
    // to the runtime override, so the declared method itself needs no body.
    var virtualDispatch=ins.Any(x=>x.Operand is MethodReference c&&c.Name=="il2cpp_object_get_virtual_method");
    if(native.IsAbstract||(virtualDispatch&&native.IsVirtual)){if(Rva(native)==0)unverifiedList.Add(md.FullName+" (virtual dispatch to runtime override)");return null;}
    if(Rva(native)==0)return $"stripped: {native.FullName} (token 0x{token:X8}) has no native code (no Address/RVA in GameAssembly)";
    return null;
}
static int IndexOf(byte[] hay,byte[] needle)
{
    for(var i=0;i<=hay.Length-needle.Length;i++){if(hay[i]!=needle[0])continue;var ok=true;for(var j=1;j<needle.Length;j++)if(hay[i+j]!=needle[j]){ok=false;break;}if(ok)return i;}
    return -1;
}

// Returns the first problem on any path from root, or null when clean.
string? ScanMethod(MethodReference root,out int visitedCount)
{
    var visited=new HashSet<string>();var queue=new Queue<(MethodReference m,string path,int depth)>();
    queue.Enqueue((root,Sig(root),0));string? problem=null;
    while(queue.Count>0&&problem==null)
    {
        var (mref,path,depth)=queue.Dequeue();
        var key=Sig(mref)+"@"+ScopeName(mref.DeclaringType);if(!visited.Add(key))continue;
        if(IsSpanPin(mref)){problem=$"[span] {mref.FullName}\n    via {path}";break;}
        MethodDefinition? md;
        try{md=mref.Resolve();}catch(AssemblyResolutionException ex){md=null;if(!checkedNames.Contains(ScopeName(mref.DeclaringType)))continue;problem=$"[missing] assembly {ex.AssemblyReference.Name} for {mref.FullName}\n    via {path}";break;}
        if(md==null)
        {
            if(checkedNames.Contains(ScopeName(mref.DeclaringType))){problem=$"[missing] {mref.FullName}\n    via {path}";break;}
            continue;
        }
        var native=NativeProblem(md);
        if(native!=null){problem=$"[{native.Split(':')[0].Split(' ')[0]}] {native}\n    via {path}";break;}
        if(!followNames.Contains(md.Module.Assembly.Name.Name)||!md.HasBody||depth>=8)continue;
        foreach(var ins in md.Body.Instructions)
        {
            switch(ins.Operand)
            {
                case MethodReference callee:
                    queue.Enqueue((callee,path+"\n      -> "+Sig(callee),depth+1));break;
                case FieldReference field:
                    if(!checkedNames.Contains(ScopeName(field.DeclaringType)))break;
                    FieldDefinition? fd=null;try{fd=field.Resolve();}catch(AssemblyResolutionException){}
                    if(fd==null)problem??=$"[missing] field {field.FullName}\n    via {path}";break;
                case TypeReference type when type is not GenericParameter:
                    if(!checkedNames.Contains(ScopeName(type)))break;
                    TypeDefinition? td=null;try{td=type.Resolve();}catch(AssemblyResolutionException){}
                    if(td==null)problem??=$"[missing] type {type.FullName}\n    via {path}";break;
            }
            if(problem!=null)break;
        }
    }
    visitedCount=visited.Count;return problem;
}
MethodReference? Find(string spec)
{
    var sep=spec.IndexOf("::",StringComparison.Ordinal);var typeName=spec[..sep];var rest=spec[(sep+2)..];
    var paren=rest.IndexOf('(');var name=paren<0?rest:rest[..paren];
    string[]? parms=paren<0?null:rest[(paren+1)..^1].Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    foreach(var file in Directory.GetFiles(interopDir,"*.dll"))
    {
        var asm=AssemblyDefinition.ReadAssembly(file,readerParams);
        var t=asm.MainModule.GetType(typeName);if(t==null)continue;
        var m=t.Methods.Where(x=>x.Name==name&&(parms==null||x.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(parms))).OrderBy(x=>x.HasGenericParameters).FirstOrDefault();
        if(m!=null)return mod.ImportReference(m);
    }
    return null;
}
string Family(string? problem)=>problem==null?"":problem[1..problem.IndexOf(']')];

// 1) Roots: every interop/core method the mod DLL calls.
var roots=new SortedDictionary<string,(MethodReference m,SortedSet<string> callers)>(StringComparer.Ordinal);
foreach(var type in mod.GetTypes())foreach(var method in type.Methods)
{
    if(!method.HasBody)continue;
    foreach(var ins in method.Body.Instructions)
        if(ins.Operand is MethodReference mr&&followNames.Contains(ScopeName(mr.DeclaringType)))
        {
            var key=Sig(mr);
            if(!roots.TryGetValue(key,out var entry))roots[key]=entry=(mr,new SortedSet<string>());
            entry.callers.Add(type.Name+"."+method.Name);
        }
}
var fails=0;var families=new SortedDictionary<string,int>();
report.AppendLine();report.AppendLine($"== Mod calls into interop: {roots.Count} distinct methods ==");
foreach(var (key,(m,callers)) in roots)
{
    var problem=ScanMethod(m,out var n);
    if(problem!=null){fails++;families[Family(problem)]=families.GetValueOrDefault(Family(problem))+1;}
    report.AppendLine($"{(problem==null?"PASS":"FAIL")}  {key}   [{n} methods followed; called from {string.Join(", ",callers)}]");
    if(problem!=null)report.AppendLine("      "+problem);
}
// 2) Probes proving each family is detected.
var probeErrors=0;
report.AppendLine();report.AppendLine("== Probes ==");
foreach(var (spec,wantFail) in expectFail.Select(s=>(s,true)).Concat(expectPass.Select(s=>(s,false))))
{
    var m=Find(spec);
    if(m==null){probeErrors++;report.AppendLine($"ERROR probe not found: {spec}");continue;}
    var problem=ScanMethod(m,out var n);var failed=problem!=null;
    var ok=failed==wantFail;if(!ok)probeErrors++;
    report.AppendLine($"{(failed?"FAIL":"PASS")}  {spec}   expected {(wantFail?"FAIL":"PASS")} -> {(ok?"OK":"PROBE WRONG")} [{n} followed]");
    if(problem!=null)report.AppendLine("      "+problem);
}
foreach(var typeName in scanTypes)
{
    report.AppendLine();report.AppendLine("== Type survey "+typeName+" (informational) ==");
    foreach(var file in Directory.GetFiles(interopDir,"*.dll"))
    {
        var t=AssemblyDefinition.ReadAssembly(file,readerParams).MainModule.GetType(typeName);if(t==null)continue;
        foreach(var m in t.Methods.OrderBy(x=>x.Name))
        {
            var problem=ScanMethod(mod.ImportReference(m),out var n);
            report.AppendLine($"{(problem==null?"clean":"BAD  ")}  {(m.IsPublic?"public ":"private")} {Sig(m)}{(problem==null?"":"   <- "+problem.Split('\n')[0])}");
        }
    }
}
foreach(var spec in dumps)
{
    report.AppendLine();report.AppendLine("== Dump "+spec+" ==");
    var m=Find(spec)?.Resolve();
    if(m?.HasBody!=true){report.AppendLine("  (not found / no body)");continue;}
    foreach(var ins in m.Body.Instructions)if(ins.Operand is MemberReference r)report.AppendLine($"  {ins.OpCode} {r.FullName}");else if(ins.Operand is string s)report.AppendLine($"  {ins.OpCode} \"{s}\"");
}
report.AppendLine();
var famText=families.Count==0?"none":string.Join(", ",families.Select(f=>$"{f.Key}={f.Value}"));
// Informational: concrete stripped candidates in an assembly (to pick probes).
foreach(var asmName in findStripped)
{
    report.AppendLine();report.AppendLine("== Stripped candidates in "+asmName+" (informational, first 40) ==");
    var shown=0;
    var asm=AssemblyDefinition.ReadAssembly(Path.Combine(interopDir,asmName+".dll"),readerParams);
    foreach(var t in asm.MainModule.GetTypes())foreach(var m in t.Methods)
    {
        if(shown>=40)break;
        if(!m.IsPublic||m.HasGenericParameters||!m.HasBody)continue;
        var p=NativeProblem(m);
        if(p!=null&&p.StartsWith("stripped")){report.AppendLine("  "+Sig(m)+"   <- "+p);shown++;}
    }
}
report.AppendLine();report.AppendLine($"== Native status not decidable offline ({unverifiedList.Count}; covered by the runtime guard where the mod calls them) ==");
foreach(var u in unverifiedList)report.AppendLine("  "+u);
report.AppendLine($"SUMMARY mod-calls={roots.Count} fail={fails} ({famText}) native-unverified-wrappers={unverifiedList.Count} probe-errors={probeErrors}");
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);File.WriteAllText(reportPath,report.ToString());
Console.WriteLine($"Interop scan: {roots.Count} mod calls, {fails} FAIL ({famText}), probe errors {probeErrors}. Report: {reportPath}");
foreach(var line in report.ToString().Split('\n').Where(l=>l.StartsWith("FAIL")||l.StartsWith("ERROR")||l.Contains("expected")))Console.WriteLine(line.TrimEnd());
return fails==0&&probeErrors==0?0:1;

static int Usage(string msg){Console.Error.WriteLine(msg);return 2;}
