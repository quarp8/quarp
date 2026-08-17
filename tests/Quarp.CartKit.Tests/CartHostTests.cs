using Quarp.Api;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// CartHost loading rules: exactly one Cartridge subclass per cart, a usable constructor,
/// and best-effort unload semantics.
/// </summary>
public class CartHostTests
{
    private static byte[] CompileOk(string source)
    {
        CartCompileResult result = CartCompiler.Compile(
            new[] { new CartSourceFile("src/main.cs", source) }, "hosttest");
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return result.AssemblyBytes;
    }

    [Fact]
    public void LoadsTheSingleCartridgeSubclass()
    {
        byte[] assembly = CompileOk("""
            using Quarp.Api;

            public sealed class OnlyCart : Cartridge
            {
                public override void Update() { }
            }
            """);
        using var host = CartHost.Load(assembly);
        Assert.NotNull(host.Cartridge);
        Assert.Equal("OnlyCart", host.Cartridge.GetType().Name);
        Assert.IsAssignableFrom<Cartridge>(host.Cartridge);
    }

    [Fact]
    public void TwoCartridgeSubclassesAreRejected()
    {
        byte[] assembly = CompileOk("""
            using Quarp.Api;

            public sealed class FirstCart : Cartridge { }
            public sealed class SecondCart : Cartridge { }
            """);
        var e = Assert.Throws<CartLoadException>(() => CartHost.Load(assembly));
        Assert.Contains("exactly one Cartridge subclass", e.Message);
        Assert.Contains("FirstCart", e.Message);
        Assert.Contains("SecondCart", e.Message);
    }

    [Fact]
    public void NoCartridgeSubclassIsRejected()
    {
        byte[] assembly = CompileOk("public sealed class NotACart { }");
        var e = Assert.Throws<CartLoadException>(() => CartHost.Load(assembly));
        Assert.Contains("no class deriving", e.Message);
    }

    [Fact]
    public void AbstractSubclassDoesNotCount()
    {
        // The abstract intermediate is skipped; its concrete child is the single cart.
        byte[] assembly = CompileOk("""
            using Quarp.Api;

            public abstract class CartBase : Cartridge { }
            public sealed class RealCart : CartBase { }
            """);
        using var host = CartHost.Load(assembly);
        Assert.Equal("RealCart", host.Cartridge.GetType().Name);
    }

    [Fact]
    public void MissingParameterlessConstructorIsRejected()
    {
        byte[] assembly = CompileOk("""
            using Quarp.Api;

            public sealed class NeedyCart : Cartridge
            {
                public NeedyCart(int value) { _ = value; }
            }
            """);
        var e = Assert.Throws<CartLoadException>(() => CartHost.Load(assembly));
        Assert.Contains("parameterless constructor", e.Message);
    }

    [Fact]
    public void ThrowingConstructorIsReportedWithTheInnerError()
    {
        byte[] assembly = CompileOk("""
            using Quarp.Api;

            public sealed class ExplodingCart : Cartridge
            {
                public ExplodingCart() => throw new System.InvalidOperationException("boom at construction");
            }
            """);
        var e = Assert.Throws<CartLoadException>(() => CartHost.Load(assembly));
        Assert.Contains("boom at construction", e.Message);
        Assert.Contains("InvalidOperationException", e.Message);
    }

    [Fact]
    public void CartridgeIsUnusableAfterUnload()
    {
        byte[] assembly = CompileOk("""
            using Quarp.Api;

            public sealed class ShortLivedCart : Cartridge { public override void Update() { } }
            """);
        var host = CartHost.Load(assembly);
        host.Unload();
        Assert.Throws<InvalidOperationException>(() => host.Cartridge);
        host.Dispose();     // double-dispose is harmless
    }

    [Fact]
    public void CartsShareTheHostsCartridgeTypeIdentity()
    {
        // The load context has no Load override, so Quarp.Api resolves to the host's copy
        // and the loaded instance is a Cartridge of the very same runtime type.
        byte[] assembly = CompileOk("""
            using Quarp.Api;

            public sealed class SharedTypeCart : Cartridge { }
            """);
        using var host = CartHost.Load(assembly);
        Assert.Same(typeof(Cartridge), host.Cartridge.GetType().BaseType);
    }
}
