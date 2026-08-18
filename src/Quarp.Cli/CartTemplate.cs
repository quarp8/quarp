namespace Quarp.Cli;

/// <summary>The files <c>quarp new</c> writes: the smallest playable cartridge, plus the dev-only IDE project.</summary>
public static class CartTemplate
{
    /// <summary>
    /// Folder holding the dev-only project. Deliberately dotted and deliberately outside
    /// <c>src/</c>: the loader reads only <c>src/**/*.cs</c> plus the named assets, the packer
    /// writes only that same list, the watcher ignores everything else, and the code budget
    /// counts only <c>src/</c>. So this folder is invisible to the cartridge in all four
    /// places that matter — it exists for the editor and nothing else.
    /// </summary>
    public const string DevFolder = ".quarp";

    /// <summary>Name of the dev-only project inside <see cref="DevFolder"/>.</summary>
    public const string DevProjectFile = "cart.csproj";

    /// <summary>
    /// The dev-only project that gives an author red squiggles while they type
    /// (M2 work order; API-8 §12). Opening the cartridge folder in VS Code or Rider loads
    /// this, which references <c>Quarp.Api</c> for IntelliSense and <c>Quarp.Analyzers</c>
    /// for QRP1001-QRP1003 — the very rules <c>CartCompiler</c> enforces at build time.
    ///
    /// <para><c>{0}</c> is replaced with the absolute path of the folder holding the Quarp
    /// tools, separator included. Absolute rather than relative on purpose: a relative path
    /// from an arbitrary cartridge folder to wherever <c>quarp</c> happens to be installed
    /// breaks the first time the cart is copied somewhere else, and the failure is silent —
    /// a missing analyzer produces no diagnostics rather than an error.</para>
    ///
    /// <para><c>EnableDefaultCompileItems=false</c> plus the explicit glob is load-bearing.
    /// The analyzers only wake up for a compilation that declares a <c>Cartridge</c>
    /// subclass, so if the glob ever failed to reach <c>main.cs</c>, all three rules would
    /// switch themselves off without a word.</para>
    /// </summary>
    public const string DevProjectFormat = """
        <Project Sdk="Microsoft.NET.Sdk">

          <!-- Dev-only project: it exists so VS Code and Rider can show the Quarp determinism
               diagnostics (QRP1001-QRP1003) while you type. It is NOT how the cartridge is
               built - `quarp run` compiles src/**/*.cs itself - and it is NOT part of the
               .quarp8 package. The tool paths below are absolute and belong to this machine;
               after moving the cartridge or updating Quarp, regenerate this file. -->

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
          </PropertyGroup>

          <ItemGroup>
            <Compile Include="..\src\**\*.cs" />
            <Reference Include="Quarp.Api">
              <HintPath>{0}Quarp.Api.dll</HintPath>
            </Reference>
            <Analyzer Include="{0}Quarp.Analyzers.dll" />
          </ItemGroup>

        </Project>

        """;

    public const string MainCs = """
        using Quarp.Api;

        namespace MyCart;

        public sealed class MyCart : Cartridge
        {
            private int _x = 60;
            private int _y = 40;

            public override void Update()
            {
                // 60 times per game second. Only int and Fix here — no float (SPEC-8 §7).
                if (Btn(Button.Left))
                {
                    _x--;
                }
                if (Btn(Button.Right))
                {
                    _x++;
                }
                if (Btn(Button.Up))
                {
                    _y--;
                }
                if (Btn(Button.Down))
                {
                    _y++;
                }
            }

            public override void Draw()
            {
                Cls(0);
                Print("HELLO QUARP-8", 38, 8, 3);
                RectFill(_x, _y, 8, 8, 7);
            }
        }
        """;
}
