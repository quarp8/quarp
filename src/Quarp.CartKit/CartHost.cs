using System.Reflection;
using System.Runtime.Loader;
using Quarp.Api;

namespace Quarp.CartKit;

/// <summary>
/// Owns one loaded cartridge: a collectible AssemblyLoadContext holding the compiled
/// assembly plus the single <see cref="Quarp.Api.Cartridge"/> instance found inside it.
/// The context deliberately has no Load override: Quarp.Api then resolves through the
/// default context, so the cart's Cartridge base class is the very same runtime type the
/// host uses. <see cref="Unload"/> is best-effort (ARCHITECTURE §3) — the context dies
/// only once nothing references it; <see cref="LoadContextWeakReference"/> lets a dev
/// HUD watch whether it actually did.
/// </summary>
public sealed class CartHost : IDisposable
{
    private AssemblyLoadContext? _context;
    private Cartridge? _cartridge;

    private CartHost(AssemblyLoadContext context, Cartridge cartridge)
    {
        _context = context;
        _cartridge = cartridge;
        LoadContextWeakReference = new WeakReference(context);
    }

    /// <summary>Dead target = the collectible context was actually collected after <see cref="Unload"/>.</summary>
    public WeakReference LoadContextWeakReference { get; }

    /// <summary>The cartridge instance; invalid after <see cref="Unload"/>.</summary>
    public Cartridge Cartridge => _cartridge
        ?? throw new InvalidOperationException("Cartridge host has been unloaded.");

    /// <summary>
    /// Loads a compiled cartridge assembly (bytes from <see cref="CartCompiler"/>, embedded
    /// PDB inside) and instantiates its single Cartridge subclass. Zero or more than one
    /// subclass is a <see cref="CartLoadException"/>.
    ///
    /// <para>One stream, no symbol stream, on purpose. The PDB is <em>inside</em> these bytes
    /// (<c>DebugInformationFormat.Embedded</c>), so the runtime finds the symbols in the image it
    /// was handed and cart stack traces keep their file and line — measured. The trap to remember
    /// before "improving" this: emit a separate portable PDB instead, and this same call loses
    /// line numbers silently, with nothing failing to say that the M1 milestone criterion just
    /// went away.</para>
    /// </summary>
    public static CartHost Load(byte[] assemblyBytes)
    {
        var context = new AssemblyLoadContext("quarp-cart", isCollectible: true);
        try
        {
            Assembly assembly;
            using (var stream = new MemoryStream(assemblyBytes, writable: false))
            {
                assembly = context.LoadFromStream(stream);
            }
            Type cartType = FindCartridgeType(assembly);
            Cartridge cartridge = Instantiate(cartType);
            return new CartHost(context, cartridge);
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    /// <summary>Best-effort unload: drops all strong references and asks the context to collect.</summary>
    public void Unload()
    {
        _cartridge = null;
        AssemblyLoadContext? context = _context;
        _context = null;
        context?.Unload();
    }

    public void Dispose() => Unload();

    private static Type FindCartridgeType(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            throw new CartLoadException("cartridge assembly contains types that failed to load.", e);
        }
        Type? found = null;
        foreach (Type type in types)
        {
            if (type.IsAbstract || !typeof(Cartridge).IsAssignableFrom(type))
            {
                continue;
            }
            if (found is not null)
            {
                throw new CartLoadException(
                    $"cartridge must contain exactly one Cartridge subclass, found at least two: "
                    + $"{found.FullName} and {type.FullName}.");
            }
            found = type;
        }
        return found ?? throw new CartLoadException("cartridge contains no class deriving from Quarp.Api.Cartridge.");
    }

    private static Cartridge Instantiate(Type cartType)
    {
        try
        {
            return (Cartridge)Activator.CreateInstance(cartType)!;
        }
        catch (MissingMethodException e)
        {
            throw new CartLoadException($"{cartType.FullName} needs a public parameterless constructor.", e);
        }
        catch (TargetInvocationException e)
        {
            throw new CartLoadException(
                $"{cartType.FullName} constructor threw {e.InnerException?.GetType().Name}: "
                + $"{e.InnerException?.Message}", e);
        }
    }
}
