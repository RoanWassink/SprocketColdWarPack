// Read-only probe: prints the members of the named types from the generated interop assemblies.
// usage: dotnet run -- TypeName [TypeName...]    (interop folder: env SPROCKET_INTEROP, default: the game's BepInEx\interop)
using Mono.Cecil;
var dir=Environment.GetEnvironmentVariable("SPROCKET_INTEROP")??@"C:\Program Files (x86)\Steam\steamapps\common\Sprocket\BepInEx\interop";
foreach(var f in Directory.GetFiles(dir,"*.dll")) {
 using var m=ModuleDefinition.ReadModule(f);
 foreach(var t in m.GetTypes())if(args.Contains(t.Name)) {
 Console.WriteLine(t.FullName+" : "+t.BaseType);
 foreach(var p in t.Properties)Console.WriteLine("PROPERTY "+p.PropertyType+" "+p.Name);
 foreach(var p in t.Methods.Where(x=>!x.Name.StartsWith("get_")&&!x.Name.StartsWith("set_")&&!x.Name.StartsWith(".cctor")))Console.WriteLine(p.FullName);
 }
}
