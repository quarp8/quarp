using System.Security.Cryptography;
using Quarp.CartKit;
using Quarp.Core;

namespace Quarp.Cli;

/// <summary>
/// <c>quarp gfx dump &lt;cart&gt; [-o &lt;file.png&gt;] [--force]</c> — boots a cartridge headless,
/// runs its <c>Init</c> and writes the sprite sheet the console is left holding as a
/// <c>gfx.png</c> (M9 wave A1).
///
/// <para><b>Why this exists.</b> Not one demo cartridge in this repository has a <c>gfx.png</c>:
/// every sheet is painted in <c>Init</c> with <c>Sset</c> from hex strings, four of the five
/// carts carrying their own copy of the same string parser. That makes the sheet real only while
/// a console is running, which leaves the M9 sprite editor with nothing to open and reduces the
/// ADR-026 criterion "the demo art round-trips byte for byte" to a test over empty sheets. This
/// command is the extractor half of moving that art out of code and into files; the carts
/// themselves are not touched here.</para>
///
/// <para><b>The sheet comes from the console, never from the source text.</b> The cartridge is
/// loaded, compiled and attached exactly the way <c>quarp sim</c> does it, and the bytes written
/// are read back out of the running console through <see cref="VirtualConsole.Sget"/> — the same
/// array <c>Spr</c> and <c>Map</c> blit from. A second reader of the hex strings in
/// <c>carts/*/src/main.cs</c> would be a second owner of what the art <em>is</em>, and the two
/// owners would disagree the first time a cart computed a pixel instead of spelling it: digger
/// copies sprite cells around with <c>Sget</c>/<c>Sset</c>, and no parser of its source could
/// ever see that.</para>
///
/// <para><b>Init runs once; no tick ever does.</b> <see cref="VirtualConsole.AttachCart"/> is
/// Init, as tick 0 (SPEC-8 §7), and nothing here calls <see cref="VirtualConsole.Tick"/>. That
/// is the definition of the sheet being dumped: the cartridge's art as authored, before any
/// animation, damage flash or <c>Sset</c> an <c>Update</c> would have made — a sheet dumped one
/// tick later is a screenshot of a game in progress, not the artwork the editor should open.</para>
///
/// <para><b>The encoder is the one in <see cref="PngEncoder"/></b>, called exactly as
/// <c>SpriteEditorSession.Save</c> calls it — same function, same 128x128, same visible-index
/// input. That is deliberate to the point of being the reason this command is not allowed to
/// format a PNG itself: the moment this file and the editor both knew how to spell a
/// <c>gfx.png</c>, the format would have two owners and the round-trip criterion would be
/// measuring them against each other rather than against the art.</para>
///
/// <para><b>What proves the bytes are the sheet.</b> Before anything reaches the disk, the
/// encoded file is handed straight back to <see cref="PngDecoder"/> and the indices that come out
/// are compared with the indices that went in. The encoder is a pure function pinned to filter 0
/// and stored deflate — its IDAT literally contains the index bytes — so this is cheap, and it
/// turns any future disagreement between the two halves of the format into a refusal to write
/// instead of a cartridge whose art quietly changed on the way out.</para>
/// </summary>
public static class GfxDumpCommand
{
    private const string Usage = "usage: quarp gfx dump <cart> [-o <file.png>] [--force]";

    /// <summary>
    /// The sheet's dimensions as the <em>console</em> states them — this is the end of the pipe
    /// the pixels are read from, so the read loop counts in the console's numbers.
    /// <see cref="CartData.GfxWidth"/> states the same pair for the <em>file</em> end, and that
    /// is the pair handed to the encoder, exactly as <c>SpriteEditorSession.Save</c> hands it.
    /// They are the same 128x128 today; should they ever stop being, the encoder's own
    /// length check refuses the call and this command reports it without writing anything,
    /// rather than producing a gfx.png the loader would reject.
    /// </summary>
    private const int SheetWidth = VirtualConsole.SheetWidth;

    private const int SheetHeight = VirtualConsole.SheetHeight;

    /// <summary>
    /// Entry point for the <c>gfx</c> command group; <paramref name="args"/> starts at the
    /// subcommand, the shape <see cref="MapBuildCommand.Invoke"/> and
    /// <see cref="AudioBuildCommand.Invoke"/> already have, so the dispatcher in
    /// <c>Program.cs</c> stays one line and every argument error belongs to the command that
    /// understands it.
    /// </summary>
    public static int Invoke(string[] args)
    {
        string? sub = args.Length > 0 ? args[0] : null;
        if (sub != "dump")
        {
            Console.Error.WriteLine(sub is null
                ? "usage: quarp gfx <dump> ..."
                : $"quarp gfx: unknown subcommand '{sub}'");
            Console.Error.WriteLine("  " + Usage);
            return 1;
        }
        return Dump(args[1..]);
    }

    private static int Dump(string[] args)
    {
        string? cartPath = null;
        string? outPath = null;
        bool force = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-o")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine($"quarp gfx dump: -o needs a file path ({Usage})");
                    return 1;
                }
                outPath = args[i + 1];
                i++;
            }
            else if (args[i] == "--force")
            {
                force = true;
            }
            else if (cartPath is null && !args[i].StartsWith('-'))
            {
                cartPath = args[i];
            }
            else
            {
                Console.Error.WriteLine($"quarp gfx dump: unknown argument '{args[i]}' ({Usage})");
                return 1;
            }
        }
        if (cartPath is null)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        string root = Path.GetFullPath(cartPath);
        bool folder = Directory.Exists(root);
        if (!folder && !File.Exists(root))
        {
            Console.Error.WriteLine(
                $"quarp: cartridge not found: {root} (expected a cart folder or a .quarp8 file).");
            return 1;
        }

        string? target = ResolveOutput(cartPath, root, folder, outPath);
        if (target is null)
        {
            return 1;
        }

        // Before the cartridge is loaded, let alone run: the answer to "may I write here" does
        // not depend on anything inside the cart, and an author who mistyped the destination
        // deserves that sentence rather than half a minute of Roslyn followed by it. It also
        // keeps the refusal honest when the file in the way is not a readable sheet at all —
        // loading first would report *that* file's decode error and never mention the overwrite.
        if (Directory.Exists(target))
        {
            Console.Error.WriteLine($"quarp: {target} is a directory, not a file to write the sheet into.");
            return 1;
        }
        if (File.Exists(target) && !force)
        {
            Console.Error.WriteLine(
                $"quarp: {target} already exists; pass --force to overwrite it. Refusing to replace a "
                + "cartridge's art with a dump of its running console unless that is what was asked for.");
            return 1;
        }

        byte[] sheet;
        try
        {
            sheet = ReadSheetAfterInit(root);
        }
        catch (CartLoadException e)
        {
            Console.Error.WriteLine($"quarp: {e.Message}");
            return 1;
        }
        catch (CartCompileFailure e)
        {
            foreach (string diagnostic in e.Diagnostics)
            {
                Console.Error.WriteLine(diagnostic);
            }
            Console.Error.WriteLine($"quarp gfx dump: {cartPath} does not compile; no sheet was written.");
            return 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"quarp: cannot read {root}: {e.Message}");
            return 1;
        }
        catch (Exception e)
        {
            // The cartridge's own Init threw. The embedded PDB puts file and line in the trace,
            // and nothing has been written: a half-painted sheet is not this cart's art.
            Console.Error.WriteLine("quarp: cartridge crashed during Init:");
            Console.Error.WriteLine(e.ToString());
            return 1;
        }

        byte[] png;
        try
        {
            png = PngEncoder.EncodeFromPaletteIndices(sheet, CartData.GfxWidth, CartData.GfxHeight);
        }
        catch (ArgumentException e)
        {
            // Every path into the sheet masks to 0-15 (Sset does, the decoder does), so this is
            // an internal contradiction rather than an author's mistake — say so, and write
            // nothing.
            Console.Error.WriteLine(
                $"quarp: internal error — the console's sheet is not encodable as gfx.png: {e.Message}");
            return 1;
        }
        if (!Roundtrips(png, sheet, out string mismatch))
        {
            Console.Error.WriteLine(
                "quarp: internal error — the encoded sheet does not read back as the sheet that was "
                + $"encoded ({mismatch}). Nothing was written.");
            return 1;
        }

        try
        {
            File.WriteAllBytes(target, png);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"quarp: cannot write {target}: {e.Message}");
            return 1;
        }

        // Two lines, both meant to be read by a script. The first says what was dumped from where
        // and how much of the sheet is not the empty index; the second is the digest an organiser
        // compares across runs and machines to claim the art has not moved.
        Console.WriteLine(
            $"{root} -> {target}  ({png.Length} bytes, {SheetWidth}x{SheetHeight}, {Describe(sheet)})");
        Console.WriteLine($"sha256 {Convert.ToHexStringLower(SHA256.HashData(png))}");
        WarnAboutLayers(target);
        return 0;
    }

    /// <summary>
    /// Where the sheet goes. <c>-o</c> wins; without it the destination is the cart folder's own
    /// <c>gfx.png</c>, which is the whole point of the default — the file lands exactly where the
    /// loader looks for it. A packed cartridge has no folder to put it in, so it must be told:
    /// inventing a path next to a <c>.quarp8</c> would write art into a place nothing reads.
    /// </summary>
    private static string? ResolveOutput(string cartPath, string root, bool folder, string? outPath)
    {
        if (outPath is not null)
        {
            return Path.GetFullPath(outPath);
        }
        if (!folder)
        {
            Console.Error.WriteLine(
                $"quarp: {cartPath} is a packed cartridge, which has no folder to write gfx.png into; "
                + $"pass -o <file.png> ({Usage})");
            return null;
        }
        return Path.Combine(root, "gfx.png");
    }

    /// <summary>
    /// Loads, compiles and attaches the cartridge — the sequence <c>quarp sim</c> runs, minus
    /// every tick — and hands back the 128x128 sheet the console holds once <c>Init</c> has
    /// returned. Read through <see cref="VirtualConsole.Sget"/>, the console's own door onto the
    /// live sheet, so what is dumped is what <c>Spr</c> would have drawn.
    /// </summary>
    private static byte[] ReadSheetAfterInit(string root)
    {
        CartData data = CartSource.Load(root);
        CartCompileResult result = CartCompiler.Compile(data);
        foreach (string warning in result.Warnings)
        {
            Console.Error.WriteLine(warning);
        }
        if (!result.Success)
        {
            throw new CartCompileFailure(result.Diagnostics);
        }
        using var host = CartHost.Load(result.AssemblyBytes);
        // Persistent memory stays zeroed and save.dat is neither read nor written, for the same
        // reason sim gives: the dump must depend on the cartridge alone, not on this machine's
        // saves — an Init that reads a save slot must not paint two different sheets on two
        // developers' laptops.
        var console = new VirtualConsole(
            ConsoleProfile.Profile8, data.Gfx, data.Map, data.Flags, data.Sfx, data.Music);

        // Init, as tick 0, exactly once. No Tick call anywhere below.
        console.AttachCart(host.Cartridge);

        var sheet = new byte[SheetWidth * SheetHeight];
        for (int y = 0; y < SheetHeight; y++)
        {
            for (int x = 0; x < SheetWidth; x++)
            {
                sheet[(y * SheetWidth) + x] = console.Sget(x, y);
            }
        }
        return sheet;
    }

    /// <summary>
    /// Decodes what was just encoded and compares it with what went in — the proof, carried by
    /// the command itself rather than only by its tests, that the file about to be written is the
    /// sheet the console was showing. Returns false with the first differing pixel named.
    /// </summary>
    private static bool Roundtrips(byte[] png, byte[] sheet, out string mismatch)
    {
        byte[] back;
        try
        {
            back = PngDecoder.DecodeToPaletteIndices(png, CartData.GfxWidth, CartData.GfxHeight, "gfx.png");
        }
        catch (CartLoadException e)
        {
            mismatch = e.Message;
            return false;
        }
        if (back.Length != sheet.Length)
        {
            mismatch = $"{back.Length} pixels back, {sheet.Length} in";
            return false;
        }
        for (int i = 0; i < sheet.Length; i++)
        {
            if (back[i] != sheet[i])
            {
                mismatch = $"pixel ({i % SheetWidth},{i / SheetWidth}) went in as {sheet[i]}, came back as {back[i]}";
                return false;
            }
        }
        mismatch = string.Empty;
        return true;
    }

    /// <summary>
    /// A dumped <c>gfx.png</c> beside an existing <c>gfx-layers.png</c> is a trap worth naming:
    /// by ADR-027 the layer stack is the authoring source and <c>gfx.png</c> the flattened
    /// artifact, so the sprite editor opens the stack, notices the mismatch, and the next save
    /// overwrites this dump with the stack's composite. The dump is still written — the author
    /// asked for it — but silently losing it later is worse than a line on stderr now.
    /// </summary>
    private static void WarnAboutLayers(string target)
    {
        string? directory = Path.GetDirectoryName(target);
        if (directory is null
            || !string.Equals(Path.GetFileName(target), "gfx.png", StringComparison.Ordinal)
            || !File.Exists(Path.Combine(directory, "gfx-layers.png")))
        {
            return;
        }
        Console.Error.WriteLine(
            "quarp: note — gfx-layers.png is next to this file. The sprite editor treats the layer stack "
            + "as the source (ADR-027) and its next save will replace this gfx.png with the stack's "
            + "composite.");
    }

    /// <summary>
    /// How much of the sheet is actually painted, in the terms the console reads it in: index 0
    /// is the empty pixel every sheet starts as (and the one <c>Palt</c> makes transparent by
    /// default), so a count of non-zero bytes is the one number that tells at a glance whether a
    /// cartridge really painted anything in <c>Init</c> — a dump of 16384 zeros is exactly the
    /// symptom this command was built to make visible.
    /// </summary>
    private static string Describe(ReadOnlySpan<byte> sheet)
    {
        int painted = 0;
        for (int i = 0; i < sheet.Length; i++)
        {
            if (sheet[i] != 0)
            {
                painted++;
            }
        }
        return $"{painted} of {sheet.Length} pixels painted";
    }

    /// <summary>
    /// Carries Roslyn's diagnostics out of the load-and-run helper without the helper printing
    /// them: cartridge compiler errors go to stderr verbatim, in the form the VS Code problem
    /// matcher reads (<see cref="BuildCommand"/> explains why that shape is load-bearing), and
    /// they are the one failure that is neither a load error nor a crash.
    /// </summary>
    private sealed class CartCompileFailure : Exception
    {
        public CartCompileFailure(IReadOnlyList<string> diagnostics)
            : base("cartridge does not compile") => Diagnostics = diagnostics;

        public IReadOnlyList<string> Diagnostics { get; }
    }
}
