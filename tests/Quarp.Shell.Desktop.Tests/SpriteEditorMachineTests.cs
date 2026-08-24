using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The library ↔ sprite editor transitions (M9 stage 2, wave 2b), driven through
/// <see cref="ShellModeMachine"/> without a window. The work order's named claims live here:
/// X over a cartridge <b>folder</b> opens the editor on that cart's own sheet; X over a
/// .quarp8 refuses with the honest read-only line instead of surprising at save time; and a
/// dirty session can only leave through the prompt's explicit Z (save) or X (discard).
///
/// <para>No cartridge is ever launched in these tests, so the folders need only the
/// <c>manifest.json</c> marker the library scan looks for — no sources, no Roslyn, which is
/// what keeps this file fast.</para>
/// </summary>
public class SpriteEditorMachineTests : IDisposable
{
    private readonly string _root;

    public SpriteEditorMachineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-spred-m-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ShellModeMachine Machine()
    {
        var machine = new ShellModeMachine(
            new CartLibrary(_root),
            static path => CartSession.Start(path),
            static () => { });
        machine.Menu.SkipIntro();           // the real road since ADR-028: born on the menu,
        machine.OpenLibrary();              // intro skipped, through door 1 into the library
        return machine;
    }

    private string CartFolder(string name, byte[]? sheet = null)
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.json"), $"{{\"name\":\"{name}\",\"author\":\"\",\"profile\":8}}");
        if (sheet is not null)
        {
            File.WriteAllBytes(
                Path.Combine(folder, "gfx.png"),
                PngEncoder.EncodeFromPaletteIndices(sheet, CartData.GfxWidth, CartData.GfxHeight));
        }
        return folder;
    }

    /// <summary>Walks the selection bar to the named entry — by name, because rescans sort.</summary>
    private static void Select(ShellModeMachine machine, string name)
    {
        for (int i = 0; i < machine.Library.Entries.Count; i++)
        {
            if (machine.Library.Selected!.Value.Name == name)
            {
                return;
            }
            machine.Library.MoveSelection(+1);
        }
        Assert.Fail($"'{name}' is not in the library");
    }

    /// <summary>One dirty pixel, the shortest honest way.</summary>
    private static void MakeDirty(SpriteEditorSession editor)
    {
        editor.SelectColor(7);
        editor.BeginStroke();
        editor.Paint(0, 0);
        editor.EndStroke();
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void XOverAFolderCartOpensItsOwnSheet()
    {
        var sheet = new byte[CartData.GfxWidth * CartData.GfxHeight];
        sheet[0] = 13;
        CartFolder("painted", sheet);
        var machine = Machine();
        Select(machine, "painted");

        machine.OpenEditor();

        Assert.Equal(ShellMode.Editor, machine.Mode);
        Assert.Equal("painted", machine.Editor!.CartName);
        Assert.Equal(13, machine.Editor.Pixels[0]);         // ITS sheet, not an empty one
        Assert.Null(machine.LibraryMessage);
    }

    /// <summary>
    /// The read-only refusal (work order: .quarp8 is not editable in v1, and the honest place
    /// to say so is the library line, before any pixels are drawn into a sheet that cannot be
    /// saved). Negative control (г): let OpenEditor skip the folder check and this goes red
    /// on the mode and the message both.
    /// </summary>
    [Fact]
    public void XOverAQuarp8StaysInTheLibraryWithTheReadOnlyLine()
    {
        File.WriteAllBytes(Path.Combine(_root, "sealed.quarp8"), new byte[] { 0x50, 0x4B });
        var machine = Machine();
        Select(machine, "sealed");

        machine.OpenEditor();

        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.Null(machine.Editor);
        Assert.Equal("read-only: unpack to a folder to edit", machine.LibraryMessage);
    }

    [Fact]
    public void XWithAnEmptyLibraryDoesNothing()
    {
        var machine = Machine();

        machine.OpenEditor();

        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.Null(machine.Editor);
    }

    [Fact]
    public void ACorruptGfxPngReportsOnTheLibraryScreenInsteadOfCrashing()
    {
        string folder = CartFolder("mangled");
        File.WriteAllText(Path.Combine(folder, "gfx.png"), "these are not the bytes you are looking for");
        var machine = Machine();
        Select(machine, "mangled");

        machine.OpenEditor();

        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.Null(machine.Editor);
        Assert.StartsWith("mangled:", machine.LibraryMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void EscapeFromADirtyEditorRaisesThePromptAndStays()
    {
        CartFolder("wip");
        var machine = Machine();
        machine.OpenEditor();
        MakeDirty(machine.Editor!);

        machine.HandleEscape();

        Assert.Equal(ShellMode.Editor, machine.Mode);       // unsaved pixels never fall silently
        Assert.True(machine.Editor!.ExitPromptShown);
        Assert.False(machine.ExitRequested);
    }

    [Fact]
    public void PromptZSavesTheSheetAndReturnsToTheLibrary()
    {
        string folder = CartFolder("keeper");
        var machine = Machine();
        machine.OpenEditor();
        MakeDirty(machine.Editor!);
        machine.HandleEscape();

        machine.SaveEditorAndClose();

        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.Null(machine.Editor);
        byte[] decoded = PngDecoder.DecodeToPaletteIndices(
            File.ReadAllBytes(Path.Combine(folder, "gfx.png")), CartData.GfxWidth, CartData.GfxHeight, "gfx.png");
        Assert.Equal(7, decoded[0]);        // the dirty pixel reached the disk
    }

    [Fact]
    public void PromptXLeavesWithoutWritingAnything()
    {
        string folder = CartFolder("dropout");
        var machine = Machine();
        machine.OpenEditor();
        MakeDirty(machine.Editor!);
        machine.HandleEscape();

        machine.DiscardEditorAndClose();

        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.Null(machine.Editor);
        Assert.False(File.Exists(Path.Combine(folder, "gfx.png")));    // discard means the disk never noticed
    }

    [Fact]
    public void PromptEscapeStaysInTheEditorStillDirty()
    {
        CartFolder("undecided");
        var machine = Machine();
        machine.OpenEditor();
        MakeDirty(machine.Editor!);
        machine.HandleEscape();                             // prompt up

        machine.HandleEscape();                             // Esc again: stay

        Assert.Equal(ShellMode.Editor, machine.Mode);
        Assert.False(machine.Editor!.ExitPromptShown);
        Assert.True(machine.Editor.IsDirty);
    }

    /// <summary>
    /// The prompt's exit verbs are prompt-only: a bare Z or X during normal editing must not
    /// close anything (Z is not even routed to the machine outside the prompt — this pins the
    /// machine-side guard for whatever routes to it).
    /// </summary>
    [Fact]
    public void TheExitVerbsAreDeafWithoutThePrompt()
    {
        string folder = CartFolder("guarded");
        var machine = Machine();
        machine.OpenEditor();
        MakeDirty(machine.Editor!);

        machine.SaveEditorAndClose();
        machine.DiscardEditorAndClose();

        Assert.Equal(ShellMode.Editor, machine.Mode);
        Assert.NotNull(machine.Editor);
        Assert.False(File.Exists(Path.Combine(folder, "gfx.png")));
    }

    [Fact]
    public void ClosingTheEditorLandsTheBarOnTheCartJustEdited()
    {
        CartFolder("alpha");
        CartFolder("omega");
        var machine = Machine();
        Select(machine, "omega");
        machine.OpenEditor();

        machine.HandleEscape();                             // clean close rescans, like leaving a game

        Assert.Equal(ShellMode.Library, machine.Mode);
        Assert.Equal("omega", machine.Library.Selected!.Value.Name);
    }
}
