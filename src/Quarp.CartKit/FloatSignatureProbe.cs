using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Quarp.CartKit;

/// <summary>
/// Signature type provider that answers exactly one question about a decoded metadata
/// type: does floating point appear anywhere inside it? Arrays, byrefs, pointers, pinned
/// and modified types propagate the answer from their element type, generic
/// instantiations from the generic type or any argument, function pointers from their
/// own signature — so <c>List&lt;double&gt;[]</c> and <c>ref float</c> are just as
/// detectable as a bare <c>float64</c>.
///
/// Used by <see cref="CartCompiler"/>'s float post-pass, the ban that cannot be worded
/// around: the C# keyword ban is a syntax filter and identifier spellings
/// (<c>System.Double</c>, <c>using D = System.Double;</c>) route around it, but every
/// one of them lands in the emitted signatures as ELEMENT_TYPE_R4/R8 or a
/// <c>System.Single</c>/<c>Double</c>/<c>Decimal</c>/<c>Half</c> type reference.
/// </summary>
internal sealed class FloatSignatureProbe : ISignatureTypeProvider<bool, object?>
{
    public static readonly FloatSignatureProbe Instance = new();

    private FloatSignatureProbe()
    {
    }

    /// <summary>
    /// The banned real-number type names as they appear in metadata. <c>Single</c> and
    /// <c>Double</c> normally encode as primitive element types, but they are legal as
    /// type references too; <c>Decimal</c> and <c>Half</c> are ordinary value types and
    /// have no element type at all, so this name check is the only way to see them.
    /// </summary>
    public static bool IsFloatTypeName(string ns, string name)
    {
        if (ns != "System")
        {
            return false;
        }
        return name is "Single" or "Double" or "Decimal" or "Half";
    }

    /// <summary>True when the return type or any parameter of a decoded signature is floating point.</summary>
    public static bool MentionsFloat(MethodSignature<bool> signature)
    {
        if (signature.ReturnType)
        {
            return true;
        }
        foreach (bool parameter in signature.ParameterTypes)
        {
            if (parameter)
            {
                return true;
            }
        }
        return false;
    }

    public bool GetPrimitiveType(PrimitiveTypeCode typeCode) =>
        typeCode is PrimitiveTypeCode.Single or PrimitiveTypeCode.Double;

    // A cart's own types cannot be System.Double; their fields and methods are scanned
    // in their own right, so recursing into them here would only duplicate diagnostics.
    public bool GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => false;

    public bool GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        TypeReference typeRef = reader.GetTypeReference(handle);
        return IsFloatTypeName(reader.GetString(typeRef.Namespace), reader.GetString(typeRef.Name));
    }

    public bool GetTypeFromSpecification(
        MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public bool GetSZArrayType(bool elementType) => elementType;

    public bool GetArrayType(bool elementType, ArrayShape shape) => elementType;

    public bool GetByReferenceType(bool elementType) => elementType;

    public bool GetPointerType(bool elementType) => elementType;

    public bool GetPinnedType(bool elementType) => elementType;

    public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired) => modifier || unmodifiedType;

    public bool GetFunctionPointerType(MethodSignature<bool> signature) => MentionsFloat(signature);

    public bool GetGenericInstantiation(bool genericType, ImmutableArray<bool> typeArguments)
    {
        if (genericType)
        {
            return true;
        }
        foreach (bool argument in typeArguments)
        {
            if (argument)
            {
                return true;
            }
        }
        return false;
    }

    // Open generic parameters carry no type: the closed instantiation is what gets scanned.
    public bool GetGenericMethodParameter(object? genericContext, int index) => false;

    public bool GetGenericTypeParameter(object? genericContext, int index) => false;
}
