using Mono.Cecil;
var dir=@"C:\Program Files (x86)\Steam\steamapps\common\Sprocket\BepInEx\interop";
var wanted=args.Length>0?args:new[]{"VehicleRendererGroup","VehicleObjectModel","VehicleObject","VehicleTransform","Hatch","CommanderCupola","PartDefinition","PartDefinitionDatabase","VehicleResources"};
foreach(var file in Directory.GetFiles(dir,"*.dll")) {
 using var a=AssemblyDefinition.ReadAssembly(file);
 if(args.Contains("--find"))foreach(var t in a.MainModule.Types)foreach(var p in t.Properties.Where(p=>p.PropertyType.Name.Contains("PartDefinition")||p.Name.Contains("Atlas")||p.PropertyType.Name.Contains("Atlaser")))Console.WriteLine(t.FullName+" "+p);
 foreach(var t in a.MainModule.Types.Where(t=>wanted.Contains(t.Name)&&!t.Namespace.StartsWith("Il2CppSprocket"))) {
 Console.WriteLine("TYPE "+t.FullName+" : "+t.BaseType);
 foreach(var f in t.Fields.Where(f=>!f.Name.StartsWith("Native")||args.Contains("--native")))Console.WriteLine("FIELD "+f.FieldType+" "+f.Name);
 foreach(var p in t.Properties)Console.WriteLine("PROP "+p.PropertyType+" "+p.Name);
 foreach(var m in t.Methods.Where(m=>!m.IsConstructor&&!m.IsGetter&&!m.IsSetter))Console.WriteLine("METHOD "+m);
 }
}




