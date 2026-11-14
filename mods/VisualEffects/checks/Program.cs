using System.Numerics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json.Nodes;
using SprocketVisualEffects;

static void Check(bool condition,string description){if(!condition)throw new Exception(description);}
static bool Near(float a,float b)=>Math.Abs(a-b)<1e-4;
// Usage: Checks <assets dir> <game dir (read only)> <built SprocketVisualEffects.dll> [S1 atlas dir]
if(args.Length<3){Console.Error.WriteLine("Usage: Checks <assets dir> <game dir> <SprocketVisualEffects.dll> [S1 atlas dir]");Environment.Exit(2);}
var assets=Path.GetFullPath(args[0]);
AssetContract.Validate(assets);
for(var i=0;i<64;i++)
{
    var time=i<24?i*.01f:.24f+(i-24)*.06f;
    Check(EffectMath.Frame(time)==i,"Frame boundary "+i);
    if(i>0)Check(EffectMath.Frame(time-.0001f)==i-1,"Pre-boundary "+i);
    var top=EffectMath.Uv(i,0,0);var bottom=EffectMath.Uv(i,1,1);
    Check(top.X<bottom.X&&top.Y>bottom.Y,"UV orientation "+i);
    Check(top.X>=(i%8)/8f&&bottom.X<(i%8+1)/8f,"U tile bounds");
    Check(bottom.Y>1-(i/8+1)/8f&&top.Y<1-(i/8)/8f,"V row bounds");
}
Check(EffectMath.Frame(-.1f)==-1&&EffectMath.Frame(float.NaN)==-1&&EffectMath.Frame(2.64f)==-1&&EffectMath.Frame(2.639f)==63,"Lifetime gates");
Console.WriteLine("PASS actual 64-frame nonuniform timing, expiry, UV top/bottom and tile bleed bounds");
Check(Near(EffectMath.Scale(120,1),1)&&Near(EffectMath.Scale(50,1),50f/120)&&Near(EffectMath.Scale(1000,1),2.5f)&&Near(EffectMath.Scale(1,1),.35f),"Calibre bounds");
Check(Near(EffectMath.Scale(120,float.NaN),1)&&Near(EffectMath.Scale(120,99),3)&&Near(EffectMath.Safe(float.PositiveInfinity,1500,50,5000),1500),"Config sanitization");
var births=new float[12];var active=new bool[12];
for(var i=0;i<12;i++){Check(EffectMath.SelectSlot(births,active)==i,"Inactive pool selection");births[i]=i;active[i]=true;}
Check(EffectMath.SelectSlot(births,active)==0,"Oldest recycling");births[0]=30;Check(EffectMath.SelectSlot(births,active)==1,"Second recycle");active[8]=false;Check(EffectMath.SelectSlot(births,active)==8,"Expired slot reuse");
Console.WriteLine("PASS calibre/config finite clamps and deterministic capped-pool reuse/recycle");
var f=Vector3.UnitZ;var up=Vector3.UnitY;
foreach(var dir in new[]{Vector3.UnitX,-Vector3.UnitX,Vector3.UnitZ,-Vector3.UnitZ,Vector3.Normalize(new Vector3(1,1,1)),Vector3.Normalize(new Vector3(-1,-1,1))})
{
    var vertical=EffectMath.SideVertical(f,up,dir);
    Check(Near(vertical.Length(),1)&&Near(Vector3.Dot(f,vertical),0),"Side orthogonal basis");
    foreach(var front in new[]{false,true})
    {
        var axis=front?Vector3.UnitX:f;
        var anchor=EffectMath.Corner(axis,vertical,front?.5f:.1f,.5f,front,1);
        Check(anchor.Length()<1e-5,"World muzzle invariant on view change");
        var a=EffectMath.Corner(axis,vertical,0,0,front,1);var b=EffectMath.Corner(axis,vertical,1,0,front,1);var c=EffectMath.Corner(axis,vertical,0,1,front,1);
        Check(Near((b-a).Length(),7)&&Near((c-a).Length(),3.5f),"Preserved source extent/aspect");
    }
}
Check(Near(EffectMath.FrontWeight(0),0)&&Near(EffectMath.FrontWeight(1),1)&&Near(EffectMath.FrontWeight(-1),1)&&Near(EffectMath.FrontWeight(.7f),.5f),"Axial visibility and transition");
var previous=0f;for(var i=0;i<=1000;i++){var blend=EffectMath.FrontWeight(i/1000f);Check(blend>=previous&&Near(blend+(1-blend),1),"Smooth complementary blend");previous=blend;}
Console.WriteLine("PASS front/side basis, +/- axial visibility, smooth blend, invariant world anchor and full 2:1 bounds");
var temp=Path.Combine(Path.GetTempPath(),"sprocketvfx-manifest-negative-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
try
{
    var json=JsonNode.Parse(File.ReadAllText(Path.Combine(assets,"effect-manifest.json")))!;
    json["frameTimes"]![24]=.241;File.WriteAllText(Path.Combine(temp,"effect-manifest.json"),json.ToJsonString());
    try{AssetContract.Validate(temp);throw new Exception("Retimed asset was accepted");}catch(InvalidDataException){}
}
finally{Directory.Delete(temp,true);}
Console.WriteLine("PASS source manifest contract and rejection of incompatible frame schedule");
if(args.Length>3)
{
    // 0.3.0: the generated frame tables must belong to the S1 atlas set they were baked from.
    // The atlas set is a bake input and is not part of this source tree: pass its folder as the 4th argument.
    var atlasDir=Path.GetFullPath(args[3]);
    using var sha=System.Security.Cryptography.SHA256.Create();
    foreach(var n in FlipbookTables.Names)foreach(var s in new[]{"A","B"}){var bytes=File.ReadAllBytes(Path.Combine(atlasDir,n+"_"+s+".png"));sha.TransformBlock(bytes,0,bytes.Length,null,0);}
    sha.TransformFinalBlock(Array.Empty<byte>(),0,0);
    Check(Convert.ToHexString(sha.Hash!).ToLowerInvariant()==FlipbookTables.AtlasSetSha256,"FlipbookTables out of date: re-run bake/make_tables.py");
    Console.WriteLine("PASS FlipbookTables match the S1 atlas set (SHA256)");
}
else Console.WriteLine("SKIP FlipbookTables vs S1 atlas set (no atlas folder given as 4th argument; the atlas set is a bake input outside this source tree)");
var game=args[1];
using var file=File.OpenRead(Path.Combine(game,"BepInEx","interop","Sprocket.Vehicles.Weapons.dll"));
using var pe=new PEReader(file);var md=pe.GetMetadataReader();
var t=md.TypeDefinitions.Select(h=>md.GetTypeDefinition(h)).Single(t=>md.GetString(t.Name)=="MuzzleFlashEffect"&&md.GetString(t.Namespace)=="Sprocket.Vehicles.Fires");
var setup=t.GetMethods().Select(h=>md.GetMethodDefinition(h)).Single(m=>md.GetString(m.Name)=="Setup");
var signature=setup.DecodeSignature(new Names(),null);
Check(signature.ReturnType=="Void"&&string.Join(",",signature.ParameterTypes)=="Sprocket.WeaponFramework.CannonMuzzleFlashEffectInfo,UnityEngine.Transform,UnityEngine.Vector3,UnityEngine.Quaternion,Boolean","Installed interop signature changed");
Check(t.GetFields().Select(h=>md.GetString(md.GetFieldDefinition(h).Name)).Count(n=>n.StartsWith("NativeMethodInfoPtr_Setup_"))==1,"Native pointer field ambiguity");
Console.WriteLine("PASS installed interop Setup return/parameters and unambiguous native pointer field");

{
    // 0.4.5: VE never writes native part state. Every member the built DLL references on a Sprocket type
    // must be a read-only getter, a native inspector drawer call or RequestRedraw; any native mesh,
    // material, dynamic or collision setter (MarkDynamic, SetMesh, SetShadowProxy, SetInteractionMesh,
    // SetMaterials, set_Materials, SetCollisionMesh, ...) or VehicleMaterial wrapper fails the build.
    var dll=Path.GetFullPath(args[2]);
    using var modFile=File.OpenRead(dll);using var modPe=new PEReader(modFile);var mod=modPe.GetMetadataReader();
    string Parent(EntityHandle h){if(h.Kind!=HandleKind.TypeReference)return h.Kind.ToString();var r=mod.GetTypeReference((TypeReferenceHandle)h);return mod.GetString(r.Namespace)+"."+mod.GetString(r.Name);}
    var used=mod.MemberReferences.Select(h=>mod.GetMemberReference(h)).Select(m=>Parent(m.Parent)+"::"+mod.GetString(m.Name)).Where(n=>n.StartsWith("Sprocket")).ToHashSet();
    var allowed=new HashSet<string>{
        "Sprocket.UI.IGUIElementDrawer::Dropdown","Sprocket.UI.IGUIElementDrawer::InfoField","Sprocket.UI.IGUIElementDrawer::InputField","Sprocket.UI.IGUIElementDrawer::Slider",
        "Sprocket.UI.IGUILayout::BeginDropdown","Sprocket.UI.IGUILayout::EndAllDropdowns","Sprocket.VehicleDesigner.DesignerEditor::RequestRedraw",
        "Sprocket.VehicleDesigner.Modules.VehicleObjectEditor::get_target","Sprocket.Vehicles.IVehicleObjectReader::get_Items",
        "Sprocket.Vehicles.Vehicle::get_ObjectReader","Sprocket.Vehicles.Vehicle::get_Released","Sprocket.Vehicles.VehicleComponent::get_ComponentID",
        "Sprocket.Vehicles.VehicleComponent::get_IsInstalled","Sprocket.Vehicles.VehicleComponent::get_Vehicle","Sprocket.Vehicles.VehicleComponent::get_VehicleObject",
        "Sprocket.Vehicles.VehicleObject::get_Components","Sprocket.Vehicles.VehicleObject::get_Flags","Sprocket.Vehicles.VehicleObject::get_GUID",
        "Sprocket.Vehicles.VehicleObject::get_Released","Sprocket.Vehicles.VehicleObject::get_Vehicle","Sprocket.Vehicles.VehicleObjectModel::get_HasModel",
        "Sprocket.Vehicles.VehicleObjectModel::get_ModelPosition","Sprocket.Vehicles.VehicleObjectModel::get_ModelRotation","Sprocket.Vehicles.VehicleObjectModel::GetModelScale",
        "Sprocket.Vehicles.VehiclePart::get_VehicleTransform","Sprocket.Vehicles.VehicleTransform::get_Mirror","Sprocket.Vehicles.VehicleTransform::get_Parent",
        "Sprocket.Vehicles.VehicleTransform::get_VehicleObject","Sprocket.WeaponFramework.CannonMuzzleFlashEffectInfo::Calibre"};
    var extra=used.Except(allowed).OrderBy(x=>x).ToArray();
    Check(extra.Length==0,"Sprocket member outside the read-only allowlist: "+string.Join(", ",extra));
    foreach(var bad in new[]{"MarkDynamic","SetMesh","SetShadowProxy","SetInteractionMesh","SetMaterials","set_Materials","set_Material","SetCollisionMesh","set_Mesh"})
        Check(!used.Any(n=>n.EndsWith("::"+bad)),"forbidden native setter referenced: "+bad);
    var types=mod.TypeReferences.Select(h=>mod.GetTypeReference(h)).Select(t=>mod.GetString(t.Namespace)+"."+mod.GetString(t.Name)).ToHashSet();
    Check(!types.Contains("Sprocket.Vehicles.AssetManagement.VehicleMaterial")&&!types.Contains("Sprocket.Vehicles.AssetManagement.VehicleMaterialFlags"),"no native VehicleMaterial wrapper");
    Check(used.Contains("Sprocket.Vehicles.VehicleObjectModel::GetModelScale")&&used.Contains("Sprocket.Vehicles.VehicleObjectModel::get_ModelPosition"),"lens pose reads the native model transform");
    Console.WriteLine($"PASS built DLL references {used.Count} Sprocket members, all read-only getters/inspector drawer calls; no MarkDynamic/SetMesh/SetShadowProxy/SetInteractionMesh/SetMaterials/SetCollisionMesh/VehicleMaterial");
}

sealed class Names:ISignatureTypeProvider<string,object?>
{
    public string GetArrayType(string e,ArrayShape s)=>e+"[]";
    public string GetByReferenceType(string e)=>e+"&";
    public string GetFunctionPointerType(MethodSignature<string> s)=>"fn";
    public string GetGenericInstantiation(string t,System.Collections.Immutable.ImmutableArray<string> a)=>t;
    public string GetGenericMethodParameter(object? c,int i)=>"M"+i;
    public string GetGenericTypeParameter(object? c,int i)=>"T"+i;
    public string GetModifiedType(string m,string u,bool r)=>u;
    public string GetPinnedType(string e)=>e;
    public string GetPointerType(string e)=>e+"*";
    public string GetPrimitiveType(PrimitiveTypeCode c)=>c.ToString();
    public string GetSZArrayType(string e)=>e+"[]";
    public string GetTypeFromDefinition(MetadataReader r,TypeDefinitionHandle h,byte k){var t=r.GetTypeDefinition(h);return r.GetString(t.Namespace)+"."+r.GetString(t.Name);}
    public string GetTypeFromReference(MetadataReader r,TypeReferenceHandle h,byte k){var t=r.GetTypeReference(h);return r.GetString(t.Namespace)+"."+r.GetString(t.Name);}
    public string GetTypeFromSpecification(MetadataReader r,object? c,TypeSpecificationHandle h,byte k)=>r.GetTypeSpecification(h).DecodeSignature(this,c);
}
