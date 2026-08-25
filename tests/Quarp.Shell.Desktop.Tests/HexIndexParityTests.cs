using System;
using System.IO;
using Microsoft.Xna.Framework.Input;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <b>Hex or decimal, chosen once for the whole shell</b> — REFERENCES-EDITORS §8 item 20
/// ("Переключаемый показ индексов в hex/dec"), on PICO-8's own key: "CTRL-H to toggle hex view
/// (shows sprite index in hexadecimal)", offered there in <em>both</em> graphics editors.
///
/// <para><b>The claim that needed an instrument.</b> The obvious way to build this feature is
/// the wrong one: a <c>bool hexIndex</c> per screen, flipped by that screen's key. TIC-80 itself
/// does that (<c>sprite->hexindex</c> is a field of the sprite editor) and it is why its map
/// screen and its sprite screen can disagree about what base they are printing in. The order for
/// this wave says one owner for the whole shell, so the test is not "does the sprite screen
/// print hex" but "<b>does the toggle thrown on ANY screen show up on ALL of them</b>" — a claim
/// no per-screen test can state, and one that goes red the moment a second copy of the fact
/// appears anywhere.</para>
///
/// <para>The rule itself is <see cref="IndexFormat"/>, a dependency-free value with the two
/// spellings TIC-80 uses verbatim (<c>"0x%02X"</c> and <c>"#%i"</c>); the single live copy is
/// <see cref="ShellModeMachine.Indexes"/>.</para>
/// </summary>
public class HexIndexParityTests : IDisposable
{
    private const int ConsoleWidth = 160;
    private const int ConsoleHeight = 90;
    private const double FrameSeconds = 1.0 / 60.0;
    private const int Off = -1000;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    private readonly string _root;

    public HexIndexParityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-hexindex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A cart with every bank an editor might want to open — <see cref="FunctionKeyParityTests"/>' fixture.</summary>
    private ShellModeMachine OpenCart()
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        string cart = Path.Combine(root, "cart");
        Directory.CreateDirectory(Path.Combine(cart, CodeEditorSession.SourceDirectoryName));
        File.WriteAllText(
            Path.Combine(cart, "manifest.json"), "{\"name\":\"hex\",\"author\":\"\",\"profile\":8}");
        File.WriteAllText(
            Path.Combine(cart, CodeEditorSession.SourceDirectoryName, CodeEditorSession.SourceFileName),
            """
            using Quarp.Api;

            public sealed class Blank : Cartridge
            {
                public override void Draw()
                {
                    Cls(0);
                }
            }
            """);
        var machine = new ShellModeMachine(
            new CartLibrary(root), static path => CartSession.Start(path), static () => { });
        machine.Menu.SkipIntro();
        machine.OpenLibrary();
        machine.OpenEditor();
        foreach (ShellMode tab in EditorIcons.LiveEditorTabs)
        {
            machine.SwitchEditorTab(tab);
        }
        return machine;
    }

    private static void Frame(ShellModeMachine modes, ShellCommandReader keys, EditorMouseReader pointer, Keys[] down)
    {
        ShellCommands commands = keys.Read(new KeyboardState(down));
        EditorMouse mouse = pointer.Read(new MouseState(
            Off, Off, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released,
            ButtonState.Released, ButtonState.Released));
        var shell = new EditorShell(
            modes, new ToolbarFlyout(), new IconHoverTracker(), new SheetScroll(),
            ConsoleWidth, ConsoleHeight);
        switch (modes.Mode)
        {
            case ShellMode.Editor:
                SpriteEditorInput.Update(shell, commands, mouse, FrameSeconds);
                break;
            case ShellMode.MapEditor:
                MapEditorInput.Update(shell, commands, mouse, FrameSeconds);
                break;
            case ShellMode.CodeEditor:
                CodeEditorInput.Update(shell, commands, mouse, Array.Empty<char>(), FrameSeconds);
                break;
            case ShellMode.SfxEditor:
                SfxEditorInput.Update(shell, commands, mouse, FrameSeconds);
                break;
            case ShellMode.MusicEditor:
                MusicEditorInput.Update(shell, commands, mouse, FrameSeconds);
                break;
        }
    }

    private static void Chord(ShellModeMachine modes, ShellCommandReader keys, EditorMouseReader pointer, params Keys[] chord)
    {
        Frame(modes, keys, pointer, chord);
        Frame(modes, keys, pointer, NoKeys);
    }

    /// <summary>
    /// The rule itself, with no shell around it: TIC-80's two spellings, and the widths that keep
    /// the right-aligned status field from jumping as the author walks the sheet.
    ///
    /// <para>Break recipe: drop the <c>0x</c> prefix from <see cref="IndexFormat.Sprite"/> and
    /// this goes red — the prefix is not decoration, it is the only thing that tells
    /// <c>#012</c> from <c>0x12</c> for a reader who did not press the key himself.</para>
    /// </summary>
    [Fact]
    public void TheRuleIsTicEightysTwoSpellingsAndNothingElse()
    {
        IndexFormat dec = default;
        IndexFormat hex = dec.Toggled();

        Assert.False(dec.Hex);
        Assert.True(hex.Hex);
        Assert.False(hex.Toggled().Hex);

        Assert.Equal("#003", dec.Sprite(3));
        Assert.Equal("#255", dec.Sprite(255));
        Assert.Equal("0x03", hex.Sprite(3));
        Assert.Equal("0xFF", hex.Sprite(255));

        Assert.Equal("SFX 07", dec.Slot("SFX", 7));
        Assert.Equal("SFX 0x07", hex.Slot("SFX", 7));
        Assert.Equal("PAT 12", dec.Slot("PAT", 12));
        Assert.Equal("PAT 0x0C", hex.Slot("PAT", 12));

        Assert.Equal("007,012", dec.Pair(7, 12));
        Assert.Equal("0x07,0x0C", hex.Pair(7, 12));
    }

    /// <summary>
    /// <b>The sweep.</b> Ctrl+H pressed on any one of the five screens flips the one shell-wide
    /// value, and the other four screens read the very same value — five starting points, and
    /// after each press every screen agrees. A second press puts it back, so the key is a toggle
    /// and not a latch.
    ///
    /// <para>Break recipe: delete the <c>EditorHexToggle</c> block from any ONE of the five
    /// routers and exactly that starting point goes red. Give any screen a private copy of the
    /// flag — a <c>bool</c> on its view, say — and the agreement assertion goes red for every
    /// starting point but that screen's own, which is the TIC-80 defect this file names in its
    /// header.</para>
    /// </summary>
    [Fact]
    public void CtrlHFromAnyScreenFlipsTheOneShellWideFormat()
    {
        foreach (ShellMode start in EditorIcons.LiveEditorTabs)
        {
            ShellModeMachine modes = OpenCart();
            var keys = new ShellCommandReader();
            var pointer = new EditorMouseReader();

            modes.SwitchEditorTab(start);
            Assert.Equal(start, modes.Mode);
            Assert.False(modes.Indexes.Hex);

            Chord(modes, keys, pointer, Keys.LeftControl, Keys.H);
            Assert.True(modes.Indexes.Hex);

            // The value the OTHER four screens will print with is this same value: walking the
            // ring must not find a screen that kept its own answer.
            foreach (ShellMode other in EditorIcons.LiveEditorTabs)
            {
                modes.SwitchEditorTab(other);
                Assert.True(modes.Indexes.Hex);
            }

            modes.SwitchEditorTab(start);
            Chord(modes, keys, pointer, Keys.LeftControl, Keys.H);
            Assert.False(modes.Indexes.Hex);
        }
    }

    /// <summary>
    /// The four screens that print a bank index actually consult the value. Two are asserted on
    /// the text itself (the sound and music screens hand their status field out as a string) and
    /// two on the framebuffer, because the sprite and map screens print theirs straight into it —
    /// a hash that did not move would mean a renderer that took the argument and ignored it.
    ///
    /// <para>Break recipe: put <c>$"SFX {view.SelectedSlot:00}"</c> back in
    /// <c>SfxEditorRenderer.Summary</c> and its line goes red; hard-code <c>$"#{...:D3}"</c> in
    /// either graphics renderer and that screen's hash stops moving.</para>
    /// </summary>
    [Fact]
    public void EveryScreenThatPrintsAnIndexPrintsItInTheChosenBase()
    {
        ShellModeMachine modes = OpenCart();
        IndexFormat hex = default(IndexFormat).Toggled();

        // Both screens open on slot / pattern 0, which is the one value whose two spellings
        // differ only in the prefix — the hardest case for the assertion and therefore the right
        // one to make it on.
        Assert.Equal(0, modes.SfxView!.SelectedSlot);
        Assert.Equal("SFX 00", SfxEditorRenderer.Summary(modes.SfxView!));
        Assert.Equal("SFX 0x00", SfxEditorRenderer.Summary(modes.SfxView!, hex));

        Assert.Equal("PAT 00", MusicEditorRenderer.Summary(modes.MusicEditor!));
        Assert.Equal("PAT 0x00", MusicEditorRenderer.Summary(modes.MusicEditor!, hex));

        var screen = new ShellScreen();

        modes.Editor!.SelectRegionCell(3, 0);       // sprite 3 — "#003" and "0x03" differ in every glyph
        SpriteEditorRenderer.Draw(screen, modes.Editor, null, false, null, new SheetScroll(), 0.0);
        string spritesDec = FrameHash.Of(screen.Framebuffer);
        SpriteEditorRenderer.Draw(
            screen, modes.Editor, null, false, null, new SheetScroll(), 0.0, null, hex);
        Assert.NotEqual(spritesDec, FrameHash.Of(screen.Framebuffer));

        modes.MapEditor!.SelectSprite(3);
        MapEditorRenderer.Draw(
            screen, modes.MapEditor, modes.Editor, modes.MapView!, null, false);
        string mapDec = FrameHash.Of(screen.Framebuffer);
        MapEditorRenderer.Draw(
            screen, modes.MapEditor, modes.Editor, modes.MapView!, null, false, hex);
        Assert.NotEqual(mapDec, FrameHash.Of(screen.Framebuffer));
    }

    /// <summary>
    /// The negative controls. Two chords that are <em>almost</em> this one must leave the format
    /// alone, and each is chosen because it is a real key of this shell rather than an invented
    /// one:
    ///
    /// <list type="number">
    ///   <item><b>Bare H</b> is the piano's B natural on the sound screen
    ///   (<c>zsxdcvgbhnjm</c>, REFERENCES-EDITORS §8 item 17). A chord must not double as its
    ///   bare key, and the traffic goes both ways: pressing the letter must not flip a display
    ///   setting. Break recipe: drop the <c>ctrl &amp;&amp;</c> from the reader's
    ///   <c>EditorHexToggle</c> line.</item>
    ///   <item><b>Ctrl+G</b> is PICO-8's own key for the OTHER switch in the same paragraph — the
    ///   grid — and this shell could not take it, because Ctrl+G is already the code screen's
    ///   find-next. Asserting that it does not flip the format is what keeps a future reader from
    ///   "fixing" the divergence by moving the hex switch onto it. Break recipe: add
    ///   <c>|| Pressed(keyboard, Keys.G)</c> to that same line.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void NeitherBareHNorCtrlGFlipsTheFormat()
    {
        foreach (ShellMode start in EditorIcons.LiveEditorTabs)
        {
            ShellModeMachine modes = OpenCart();
            var keys = new ShellCommandReader();
            var pointer = new EditorMouseReader();

            modes.SwitchEditorTab(start);

            Chord(modes, keys, pointer, Keys.H);
            Assert.False(modes.Indexes.Hex);

            Chord(modes, keys, pointer, Keys.LeftControl, Keys.G);
            Assert.False(modes.Indexes.Hex);
        }
    }
}
