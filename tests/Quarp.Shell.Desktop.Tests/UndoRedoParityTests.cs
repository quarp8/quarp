using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// <b>Ctrl+Z and Ctrl+Y on every editor screen, not just the one they were written on</b>
/// (REFERENCES-EDITORS §8 item 1: "Единая система undo/redo во всех редакторах").
///
/// <para><b>Why this file exists as its own instrument.</b> Undo/redo is implemented five times
/// — one stack per session — and routed five times, one <c>if (commands.EditorUndo)</c> line per
/// router. That shape makes exactly one defect easy: losing the line on one screen. And until
/// this file, the whole suite could not see it. The check that proves it: delete
/// <c>if (commands.EditorUndo) { session.Undo(); }</c> from <c>SfxEditorInput</c> and every test
/// in this solution stays green — the sound screen's own tests call <c>session.Undo()</c>
/// directly and never press the key, and no other test drives that router at all. A stack that
/// works and a key that does not reach it are indistinguishable from the author's chair, and
/// they are indistinguishable to a test suite that never presses the key either.
///
/// <para>So the claim is stated over the whole ring at once, in the shape
/// <see cref="FunctionKeyParityTests"/> already proved works for travel: the real
/// <see cref="ShellCommandReader"/> does the edge detection, the real router runs the frame, and
/// what is asserted is the shell's own key handling rather than a second copy of it.</para>
///
/// <para><b>Each screen must undo in ITS OWN session.</b> Every case below opens all five banks,
/// dirties all five, and then presses the chord on exactly one screen — so a router that reached
/// for the wrong session (or for whatever <c>ShellModeMachine</c> happened to hold) fails just as
/// loudly as one that reached for nothing. That is the second half of §8 item 1 and it cannot be
/// stated on a screen that is the only one open.</para>
///
/// <para><b>Headless.</b> The five routers are static and take an <see cref="EditorShell"/>;
/// none of them needs a graphics device.</para>
/// </summary>
public class UndoRedoParityTests : IDisposable
{
    private const int ConsoleWidth = 160;
    private const int ConsoleHeight = 90;
    private const double FrameSeconds = 1.0 / 60.0;
    private const int Off = -1000;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    private readonly string _root;

    public UndoRedoParityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-undokeys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A cart with every bank an editor might want to open, and nothing else. Copied from <see cref="FunctionKeyParityTests"/> deliberately: the two sweeps must not start from different carts.</summary>
    private ShellModeMachine OpenCart()
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        string cart = Path.Combine(root, "cart");
        Directory.CreateDirectory(Path.Combine(cart, CodeEditorSession.SourceDirectoryName));
        File.WriteAllText(
            Path.Combine(cart, "manifest.json"), "{\"name\":\"undo\",\"author\":\"\",\"profile\":8}");
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
        // Every EDITOR tab is visited once so that every bank exists: the four lazy sessions are
        // born on arrival, and a bank that is not open cannot prove anything about the key that
        // edits it.
        foreach (ShellMode tab in EditorScreens)
        {
            machine.SwitchEditorTab(tab);
            Assert.Equal(tab, machine.Mode);
        }
        return machine;
    }

    /// <summary>
    /// The strip's five <b>editor</b> stops — <see cref="EditorIcons.LiveEditorTabs"/> minus the
    /// GAME tab of M9 stage 5. Derived from that one list, so a seventh tab joins these sweeps by
    /// existing; the game is subtracted because it edits no bank and has no undo stack to prove
    /// anything about, which is a fact about that screen and not an exception to this file's rule.
    /// </summary>
    private static IReadOnlyList<ShellMode> EditorScreens { get; } =
        EditorIcons.LiveEditorTabs.Where(tab => tab != ShellMode.Game).ToArray();

    /// <summary>One frame of the router that belongs to whichever screen the machine is on — <see cref="FunctionKeyParityTests"/>'s dispatch, which mirrors <c>QuarpGame.Update</c>'s switch.</summary>
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

    /// <summary>A chord: everything down for one frame, everything up on the next, so the reader sees exactly one edge.</summary>
    private static void Chord(ShellModeMachine modes, ShellCommandReader keys, EditorMouseReader pointer, params Keys[] chord)
    {
        Frame(modes, keys, pointer, chord);
        Frame(modes, keys, pointer, NoKeys);
    }

    /// <summary>
    /// The five banks as five strings, in <see cref="EditorScreens"/>'s own order —
    /// the bytes each session would save, not its <c>Version</c> or its <c>CanUndo</c>. A counter
    /// can be bumped by an undo that restored the wrong thing; the payload cannot.
    /// </summary>
    private static string[] Payloads(ShellModeMachine modes)
    {
        var payloads = new string[EditorScreens.Count];
        for (int i = 0; i < payloads.Length; i++)
        {
            payloads[i] = PayloadOf(modes, EditorScreens[i]);
        }
        return payloads;
    }

    /// <summary>
    /// One bank as a string, chosen BY SCREEN rather than by position in a hand-written list.
    ///
    /// <para><b>This is not ceremony — the hand-written list was wrong.</b> The first draft of
    /// this file listed the five payloads in the order a person would say them out loud
    /// (sprites, map, code, sound, music) while the sweep above indexes them with
    /// <see cref="EditorIcons.LiveEditorTabs"/>, whose order is the order of the strip on screen
    /// (code, sprites, map, sound, music). Three of the five rows therefore compared the wrong
    /// bank, and the sweep reported "Ctrl+Z did not undo on the code screen" about a sprite
    /// bank that no key had been pressed near. A test that names the wrong owner is worse than
    /// no test: it sends whoever reads it to fix a file that was never broken. Asking the mode
    /// directly makes the two orders one order, and a sixth tab cannot silently misalign it —
    /// it lands in the switch's default and stops the run.</para>
    /// </summary>
    private static string PayloadOf(ShellModeMachine modes, ShellMode screen) => screen switch
    {
        ShellMode.Editor => Convert.ToHexString(modes.Editor!.Pixels),
        ShellMode.MapEditor => Convert.ToHexString(modes.MapEditor!.Map),
        ShellMode.CodeEditor => modes.CodeEditor!.Text,
        ShellMode.SfxEditor => Convert.ToHexString(modes.SfxEditor!.Payload),
        ShellMode.MusicEditor => Convert.ToHexString(modes.MusicEditor!.Payload),
        _ => throw new ArgumentOutOfRangeException(
            nameof(screen), screen, "not one of the five editor screens"),
    };

    /// <summary>
    /// One undoable edit in each of the five banks, made through the sessions' own public verbs
    /// (this file is about the KEY, so the edit itself must not be in doubt). Each is a single
    /// closed undo step.
    /// </summary>
    private static void DirtyEveryBank(ShellModeMachine modes)
    {
        SpriteEditorSession sprites = modes.Editor!;
        sprites.SelectColor(7, SpriteEditorInk.Primary);
        sprites.BeginStroke();
        sprites.Paint(0, 0);
        sprites.EndStroke();

        MapEditorSession map = modes.MapEditor!;
        map.SelectSprite(5);
        map.BeginStroke();
        map.PaintTile(0, 0);
        map.EndStroke();

        // Multi-line, so the code session closes the step instead of leaving a typing run open:
        // this file asks what Ctrl+Z does to a finished edit, not how runs coalesce.
        modes.CodeEditor!.Insert("// mark\n");

        modes.SfxEditor!.SetStep(0, 0, 24, 0, 1, 0);
        modes.MusicEditor!.SetChannelSlot(0, 0, 5);
    }

    /// <summary>
    /// The sweep: on each of the five screens, Ctrl+Z rolls that screen's own bank back and
    /// Ctrl+Y brings it forward again — and the other four banks do not move either way.
    ///
    /// <para><b>Break recipe.</b> Delete the <c>if (commands.EditorUndo)</c> block from any ONE
    /// of the five routers — <c>SfxEditorInput</c> is the one this test was written for — and
    /// exactly that screen's row goes red while every other test in the solution stays green.
    /// Delete the <c>EditorRedo</c> block instead and the same row fails one assertion later.
    /// Point a router at the wrong session (say <c>MusicEditorInput</c> calling
    /// <c>shell.Modes.SfxEditor!.Undo()</c>) and both the rolled-back row and the untouched-row
    /// assertion fail at once, which no per-screen test could have told apart.</para>
    /// </summary>
    [Fact]
    public void EveryEditorScreenUndoesAndRedoesInItsOwnSession()
    {
        int tabs = EditorScreens.Count;
        Assert.Equal(5, tabs);      // six stops on the strip, five of them banks with an undo stack

        for (int screen = 0; screen < tabs; screen++)
        {
            ShellModeMachine modes = OpenCart();
            var keys = new ShellCommandReader();
            var pointer = new EditorMouseReader();

            string[] clean = Payloads(modes);
            DirtyEveryBank(modes);
            string[] dirty = Payloads(modes);
            for (int i = 0; i < tabs; i++)
            {
                Assert.NotEqual(clean[i], dirty[i]);     // the edit itself must be real, on all five
            }

            modes.SwitchEditorTab(EditorScreens[screen]);
            Assert.Equal(EditorScreens[screen], modes.Mode);

            Chord(modes, keys, pointer, Keys.LeftControl, Keys.Z);

            string[] undone = Payloads(modes);
            for (int i = 0; i < tabs; i++)
            {
                Assert.Equal(i == screen ? clean[i] : dirty[i], undone[i]);
            }

            Chord(modes, keys, pointer, Keys.LeftControl, Keys.Y);

            string[] redone = Payloads(modes);
            for (int i = 0; i < tabs; i++)
            {
                Assert.Equal(dirty[i], redone[i]);
            }
        }
    }

    /// <summary>
    /// The negative control the sweep cannot give itself: a Ctrl chord the shell does not bind
    /// must undo nothing, on any screen. Without it a reader that answered <c>EditorUndo</c> to
    /// "Ctrl plus anything" would pass every row above and still be wrong, and so would a router
    /// that undid once per frame regardless of the keyboard.
    ///
    /// <para>Ctrl+Q is the chord chosen because it is provably free: <c>Keys.Q</c> occurs in
    /// <see cref="ShellCommandReader"/> only inside the piano row, which is read as
    /// <c>ctrl || shift ? 0</c> and therefore cannot see a chord at all. Bare Z is deliberately
    /// NOT the negative control here — on the two graphics screens it is the paint key, so it
    /// changes a bank on purpose, which is a different claim tested elsewhere.</para>
    ///
    /// <para>Break recipe: change <see cref="ShellCommandReader"/>'s undo line to
    /// <c>ctrl &amp;&amp; (Pressed(keyboard, Keys.Z) || Pressed(keyboard, Keys.Q))</c> and every
    /// row here goes red while the sweep above stays green.</para>
    /// </summary>
    [Fact]
    public void NoScreenUndoesOnAChordTheShellDoesNotBind()
    {
        foreach (ShellMode start in EditorScreens)
        {
            ShellModeMachine modes = OpenCart();
            var keys = new ShellCommandReader();
            var pointer = new EditorMouseReader();

            DirtyEveryBank(modes);
            string[] dirty = Payloads(modes);

            modes.SwitchEditorTab(start);
            Chord(modes, keys, pointer, Keys.LeftControl, Keys.Q);

            Assert.Equal(dirty, Payloads(modes));
        }
    }
}
