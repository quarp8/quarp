using Microsoft.CodeAnalysis;

namespace Quarp.Analyzers;

/// <summary>
/// Decides whether a compilation is cartridge code, which is the only code these rules apply
/// to — the engine itself may use float, DateTime and dictionaries freely.
///
/// The test is a property of the compilation: <em>this assembly declares a type that derives
/// from <c>Quarp.Api.Cartridge</c></em>. That was chosen over an MSBuild property (a
/// <c>build_property.*</c> entry read through <c>AnalyzerOptions</c>) because the analyzer
/// has to behave identically along both delivery paths, and only one of them has MSBuild in
/// it: the generated <c>.quarp/cart.csproj</c> is a real project, but CartCompiler builds a
/// <c>CSharpCompilation</c> by hand and would have to fake an
/// <c>AnalyzerConfigOptionsProvider</c> to carry a property across. A missing property also
/// fails in the worst possible direction — silently, with the safety rules switched off —
/// whereas a missing cartridge class means there is nothing to police anyway.
///
/// This is a gate, not a sandbox. The engine's own projects never reference the analyzer in
/// the first place; the gate is what keeps it inert if one ever does, and what keeps it from
/// firing on, say, a shared library the author compiles alongside the cartridge sources.
/// </summary>
internal static class CartridgeScope
{
    /// <summary>Metadata name of the base class every cartridge derives from.</summary>
    public const string CartridgeMetadataName = "Quarp.Api.Cartridge";

    /// <summary>
    /// True when the compilation's own source declares a <c>Quarp.Api.Cartridge</c> subclass.
    /// Walks source-declared types only (<see cref="IAssemblySymbol.GlobalNamespace"/> of the
    /// compilation's assembly), never the referenced assemblies, and is computed once per
    /// compilation from a compilation-start action.
    /// </summary>
    public static bool IsCartridgeCompilation(Compilation compilation)
    {
        INamedTypeSymbol? cartridge = compilation.GetTypeByMetadataName(CartridgeMetadataName);
        if (cartridge is null)
        {
            // Quarp.Api is not referenced at all: whatever this is, it is not a cartridge.
            return false;
        }
        return DeclaresSubclass(compilation.Assembly.GlobalNamespace, cartridge);
    }

    private static bool DeclaresSubclass(INamespaceSymbol container, INamedTypeSymbol cartridge)
    {
        foreach (INamedTypeSymbol type in container.GetTypeMembers())
        {
            if (DeclaresSubclass(type, cartridge))
            {
                return true;
            }
        }
        foreach (INamespaceSymbol nested in container.GetNamespaceMembers())
        {
            if (DeclaresSubclass(nested, cartridge))
            {
                return true;
            }
        }
        return false;
    }

    private static bool DeclaresSubclass(INamedTypeSymbol type, INamedTypeSymbol cartridge)
    {
        for (INamedTypeSymbol? baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType, cartridge))
            {
                return true;
            }
        }
        // A cartridge nested inside another type is unusual but legal, and CartHost finds it.
        foreach (INamedTypeSymbol nested in type.GetTypeMembers())
        {
            if (DeclaresSubclass(nested, cartridge))
            {
                return true;
            }
        }
        return false;
    }
}
