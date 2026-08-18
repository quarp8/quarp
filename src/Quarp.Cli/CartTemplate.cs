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
    /// for QRP1001-QRP1004 — the very rules <c>CartCompiler</c> enforces at build time.
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
               diagnostics (QRP1001-QRP1004) while you type. It is NOT how the cartridge is
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

    /// <summary>
    /// Folder holding the VS Code launch configuration. Dev-only for exactly the same four
    /// reasons <see cref="DevFolder"/> is: the loader globs <c>src/**/*.cs</c> plus the named
    /// assets and nothing else, the packer writes only that same list, the watcher's relevance
    /// filter is an allow-list, and the code budget counts only what the loader returned.
    /// </summary>
    public const string VsCodeFolder = ".vscode";

    /// <summary>Debugger launch configuration inside <see cref="VsCodeFolder"/>.</summary>
    public const string LaunchFile = "launch.json";

    /// <summary>Task definitions inside <see cref="VsCodeFolder"/>; <c>launch.json</c> references one by label.</summary>
    public const string TasksFile = "tasks.json";

    /// <summary>
    /// The placeholder <see cref="LaunchJson"/> and <see cref="TasksJson"/> carry where the
    /// absolute path of the <c>quarp</c> executable goes — <b>quotes included</b>. The writer
    /// replaces the whole token, quotes and all, with a JSON-encoded string, because a Windows
    /// path is full of backslashes and every one of them has to be escaped for JSON.
    /// </summary>
    public const string ToolPathToken = "\"__QUARP_EXE__\"";

    /// <summary>
    /// What F5 runs after <c>quarp new</c> (ADR-019; M4 work order, stage 1). VS Code parses
    /// <c>launch.json</c> as JSON-with-comments, which is why the file explains itself.
    ///
    /// <para>The keys are not decoration, and three of them are load-bearing:</para>
    /// <list type="bullet">
    ///   <item><description><c>justMyCode: false</c> — the cartridge is an assembly loaded from
    ///   a byte array into a collectible context, which is exactly the shape of thing a debugger
    ///   is entitled to call "not my code". Setting it false costs nothing and removes the
    ///   question. Honest caveat: the C# extension documents "My Code" as excluding code that is
    ///   optimized <em>or</em> has no symbols, and a cartridge is neither, so this is insurance
    ///   rather than a proven requirement.</description></item>
    ///   <item><description><c>requireExactSource: true</c> — the extension's own default, and
    ///   the switch that makes the debugger compare the file on disk against the checksum in the
    ///   PDB. Turning it off would hide a broken checksum instead of fixing it: breakpoints
    ///   would bind to the wrong lines and nothing would say why.</description></item>
    ///   <item><description><c>program</c> — absolute, same reasoning and same caveat as
    ///   <see cref="DevProjectFormat"/>: after moving the cartridge or updating Quarp, this file
    ///   has to be regenerated.</description></item>
    /// </list>
    /// </summary>
    public const string LaunchJson = """
        {
            // Written by `quarp new`. F5 in this folder runs the cartridge under the .NET
            // debugger; breakpoints go in src/*.cs. See docs/DEBUGGING.md.
            //
            // The `program` path below is absolute and belongs to the machine that generated
            // this file. After moving the cartridge or updating Quarp, regenerate it (run
            // `quarp new` into an empty folder and copy .vscode across) or fix the path by hand.
            "version": "0.2.0",
            "configurations": [
                {
                    "name": "Quarp: run this cart",
                    "type": "coreclr",
                    "request": "launch",
                    "preLaunchTask": "quarp-build",
                    "program": "__QUARP_EXE__",
                    "args": ["run", "${workspaceFolder}"],
                    "cwd": "${workspaceFolder}",
                    "console": "internalConsole",
                    "stopAtEntry": false,
                    // The cart is loaded from memory into a collectible context - never let the
                    // debugger file it under "not my code".
                    "justMyCode": false,
                    // Compare the source on disk with the checksum in the PDB. Leave it on: it
                    // is what makes a bound breakpoint mean what it says.
                    "requireExactSource": true,
                    "enableStepFiltering": true
                }
            ]
        }

        """;

    /// <summary>
    /// The task <see cref="LaunchJson"/> runs before launching: <c>quarp build</c>, which loads
    /// the cartridge, compiles it with the very compiler <c>quarp run</c> uses, checks the code
    /// budget and the generated banks, and stops — so every C# error and every QRP1001-QRP1004
    /// diagnostic lands in the Problems panel before the window opens.
    ///
    /// <para>It replaces <c>quarp sim --ticks 0</c>, which stood in for a build command that did
    /// not exist. That stand-in attached the cart to a console, and attaching runs <c>Init</c>:
    /// the task that ran before every F5 executed the author's code and reported a startup crash
    /// as a compilation failure. It also printed two hashes the task had no use for.</para>
    ///
    /// <para>The problem matcher is spelled out rather than borrowed from <c>$msCompile</c>:
    /// the exact line shape is ours (Roslyn's <c>Diagnostic.ToString()</c>,
    /// <c>src/main.cs(7,20): error CS1525: ...</c>) and the paths in it are relative to the cart
    /// folder, which is the workspace folder. Naming the base explicitly means the Problems
    /// entry links to the right file without depending on how a built-in matcher resolves.</para>
    ///
    /// <para>The cost is honest and worth knowing: F5 now compiles the cart twice, once here and
    /// once inside <c>quarp run</c>. Delete the <c>preLaunchTask</c> line from
    /// <c>launch.json</c> if a faster F5 matters more than errors in the Problems panel.</para>
    /// </summary>
    public const string TasksJson = """
        {
            // Written by `quarp new`. The `command` path is absolute and belongs to the machine
            // that generated this file - regenerate after moving the cartridge or updating Quarp.
            "version": "2.0.0",
            "tasks": [
                {
                    // `quarp build` loads the cart, compiles src/**/*.cs with the same compiler
                    // `quarp run` uses, checks the 64 KB code budget and the generated banks
                    // (sfx.bin, music.bin, map.bin) against the text they came from, and stops.
                    // No window, no save.dat, no hashes on stdout - and, unlike the
                    // `sim --ticks 0` this replaced, not a single line of your cartridge's
                    // Init or Update. A cart that crashes on startup is something you meet in
                    // the debugger, not something that reports itself as a failed build.
                    "label": "quarp-build",
                    "type": "process",
                    "command": "__QUARP_EXE__",
                    "args": ["build", "${workspaceFolder}"],
                    "group": "build",
                    "presentation": {
                        "reveal": "silent",
                        "panel": "shared",
                        "clear": true
                    },
                    // Matches what CartCompiler prints, e.g.
                    //   src/main.cs(7,20): error CS1525: Invalid expression term ';'
                    //   src/main.cs(12,9): error QRP1001: double is banned in cartridge code
                    // The location group takes 1 to 4 numbers because a diagnostic spanning
                    // several lines is printed as (line,col,endLine,endCol).
                    "problemMatcher": {
                        "owner": "quarp",
                        "fileLocation": ["relative", "${workspaceFolder}"],
                        "pattern": {
                            "regexp": "^(.+)\\((\\d+(?:,\\d+)*)\\):\\s+(error|warning|info)\\s+([A-Za-z]+\\d+):\\s+(.*)$",
                            "file": 1,
                            "location": 2,
                            "severity": 3,
                            "code": 4,
                            "message": 5
                        }
                    }
                }
            ]
        }

        """;

    public const string MainCs = """
        using Quarp.Api;

        namespace MyCart;

        public sealed class MyCart : Cartridge
        {
            private const int Size = 8;

            private int _x;
            private int _y;

            public override void Init()
            {
                // Ask the console how big it is instead of writing 128 and 72 here. The numbers
                // are properties, not constants, so the same cartridge fills whatever screen it
                // is given — which is what lets a game be looked at on two resolutions without
                // being edited (API-8, "ScreenWidth / ScreenHeight").
                _x = (ScreenWidth - Size) / 2;
                _y = (ScreenHeight - Size) / 2;
            }

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
                // The built-in font is fixed-width 4x6, so a line is 4 * its length wide.
                Print(Title, (ScreenWidth - Title.Length * 4) / 2, 8, 3);
                RectFill(_x, _y, Size, Size, 7);
            }

            private const string Title = "HELLO QUARP-8";
        }
        """;
}
