using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Metadata-only comparison: never load Unity/IL2CPP wrappers into a managed test process.
internal sealed class ApiSurface : ISignatureTypeProvider<string, object?>
{
    internal static HashSet<string> Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var provider = new ApiSurface();
        var result = new HashSet<string>(StringComparer.Ordinal);
        string Constant(ConstantHandle handle)
        {
            if (handle.IsNil) return "";
            var constant = reader.GetConstant(handle);
            return constant.TypeCode + ":" + Convert.ToHexString(reader.GetBlobBytes(constant.Value));
        }
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if ((type.Attributes & TypeAttributes.VisibilityMask) is not (TypeAttributes.Public or TypeAttributes.NestedPublic)) continue;
            string name = provider.GetTypeFromDefinition(reader, handle, 0);
            result.Add("type " + name);
            foreach (var fieldHandle in type.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public) continue;
                var fieldName = reader.GetString(field.Name);
                // Release metadata changes on every release; retain its name/type/flags in the ABI audit.
                // All other field constants and every optional parameter default remain exact comparisons.
                // The plugin GUID is normalized the same way: its value is checked separately (see PluginGuid below).
                var value = name == "SprocketKeybinds.Plugin" && fieldName == "PluginVersion" ? "<release-version>"
                    : name == "SprocketKeybinds.Keybinds" && fieldName == "PluginGuid" ? "<plugin-guid>" : Constant(field.GetDefaultValue());
                result.Add(name + " field " + fieldName + " " + field.DecodeSignature(provider, null) + " " + field.Attributes + " " + value);
            }
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public) continue;
                var signature = method.DecodeSignature(provider, null);
                var parameters = method.GetParameters().Select(p => reader.GetParameter(p)).Where(p => p.SequenceNumber > 0)
                    .OrderBy(p => p.SequenceNumber).Select(p => reader.GetString(p.Name) + ":" + p.Attributes + ":" + Constant(p.GetDefaultValue()));
                result.Add(name + " method " + reader.GetString(method.Name) + " " + method.Attributes + " " + signature.ReturnType + "(" + string.Join(",", signature.ParameterTypes)
                    + ") params=" + string.Join(",", parameters));
            }
        }
        return result;
    }
    internal static string ReleaseVersion(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var plugin = reader.TypeDefinitions.Select(reader.GetTypeDefinition).Single(t => reader.GetString(t.Namespace) == "SprocketKeybinds" && reader.GetString(t.Name) == "Plugin");
        var field = plugin.GetFields().Select(reader.GetFieldDefinition).Single(f => reader.GetString(f.Name) == "PluginVersion");
        return System.Text.Encoding.Unicode.GetString(reader.GetBlobBytes(reader.GetConstant(field.GetDefaultValue()).Value));
    }
    internal static string PluginGuid(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var keybinds = reader.TypeDefinitions.Select(reader.GetTypeDefinition).Single(t => reader.GetString(t.Namespace) == "SprocketKeybinds" && reader.GetString(t.Name) == "Keybinds");
        var field = keybinds.GetFields().Select(reader.GetFieldDefinition).Single(f => reader.GetString(f.Name) == "PluginGuid");
        return System.Text.Encoding.Unicode.GetString(reader.GetBlobBytes(reader.GetConstant(field.GetDefaultValue()).Value));
    }
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fn(" + string.Join(",", signature.ParameterTypes) + ")->" + signature.ReturnType;
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType + " mod(" + modifier + "," + isRequired + ")";
    public string GetPinnedType(string elementType) => elementType + " pinned";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeDefinition(handle);
        return type.GetDeclaringType().IsNil ? reader.GetString(type.Namespace) + "." + reader.GetString(type.Name)
            : GetTypeFromDefinition(reader, type.GetDeclaringType(), rawTypeKind) + "+" + reader.GetString(type.Name);
    }
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        return type.ResolutionScope.Kind == HandleKind.TypeReference ? GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind) + "+" + reader.GetString(type.Name)
            : reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
    }
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}
