using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// F1..F5 on <b>every</b> editor screen, not just the one they were written on
/// (REFERENCES-EDITORS §8 item 16, TIC-80's own five keys).
///
/// <para><b>Why this file exists as its own instrument.</b> This shell routes travel one line
/// per screen: Alt+Left/Right is handled separately inside each of the five routers, and so is
/// the function-key block beside it. That shape makes exactly one defect easy — wiring the key
/// on the screen you happen to be editing and leaving the other four deaf — and it is a defect
/// no per-screen test would catch, because each per-screen test only ever asks its own screen.
/// The key was in fact born that way: it landed wired on the CODE screen alone, and the four
/// missing blocks were added afterwards. A key that works on one editor and not on its
/// neighbour is worse than a key that does not exist, because the author stops trusting it
/// everywhere. So the claim here is stated over the whole ring at once, and it fails the moment
/// any single router loses its block.</para>
///
/// <para><b>Headless.</b> The five routers are static and take an <see cref="EditorShell"/>;
/// none of them needs a graphics device. The real <see cref="ShellCommandReader"/> does the
/// edge detection, so what is asserted is the shell's own key handling and not a second copy
/// of it.</para>
/// </summary>
public class FunctionKeyParityTests : IDisposable
{
    private const int ConsoleWidth = 160;
    private const int ConsoleHeight = 90;
    private const double FrameSeconds = 1.0 / 60.0;
    private const int Off = -1000;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    private readonly string _root;

    public FunctionKeyParityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-fkeys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A cart with every bank an editor might want to open, and nothing else.</summary>
    private ShellModeMachine OpenCart()
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        string cart = Path.Combine(root, "cart");
        Directory.CreateDirectory(Path.Combine(cart, CodeEditorSession.SourceDirectoryName));
        File.WriteAllText(
            Path.Combine(cart, "manifest.json"), "{\"name\":\"fkeys\",\"author\":\"\",\"profile\":8}");
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
        return machine;
    }

    /// <summary>
    /// One frame of the router that belongs to whichever screen the machine is on. The dispatch
    /// mirrors <c>QuarpGame.Update</c>'s switch, which is the one thing in this shell that cannot
    /// be driven headless — every verb it calls is the router's own public entry point.
    /// </summary>
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

    private static void Tap(ShellModeMachine modes, ShellCommandReader keys, EditorMouseReader pointer, Keys key)
    {
        Frame(modes, keys, pointer, new[] { key });
        Frame(modes, keys, pointer, NoKeys);
    }

    /// <summary>
    /// Every screen answers every function key: five starting points times five destinations,
    /// twenty-five presses, and the destination must be the one
    /// <see cref="EditorIcons.EditorTabForNumber"/> names — including the press that asks for the
    /// screen already on, which is the honest no-op the tab strip promises.
    ///
    /// <para>Break recipe: delete the <c>EditorTabJump</c> block from any ONE of the five
    /// routers — say <c>MusicEditorInput</c> — and five of these twenty-five presses go red while
    /// every other test in the suite stays green. That is the whole reason the claim is written
    /// over the ring instead of per screen.</para>
    /// </summary>
    [Fact]
    public void EveryEditorScreenAnswersEveryFunctionKey()
    {
        int tabs = EditorIcons.LiveEditorTabs.Count;
        Assert.Equal(5, tabs);

        for (int from = 0; from < tabs; from++)
        {
            for (int to = 0; to < tabs; to++)
            {
                var modes = OpenCart();
                var keys = new ShellCommandReader();
                var pointer = new EditorMouseReader();

                modes.SwitchEditorTab(EditorIcons.LiveEditorTabs[from]);
                Assert.Equal(EditorIcons.LiveEditorTabs[from], modes.Mode);

                Tap(modes, keys, pointer, Keys.F1 + to);

                Assert.Equal(EditorIcons.LiveEditorTabs[to], modes.Mode);
            }
        }
    }

    /// <summary>
    /// The negative control the sweep above cannot give itself: a key that is NOT one of the
    /// five must move nothing, from any screen. Without this, a router that answered every
    /// function key with "go to tab 1" would pass the sweep for five of its twenty-five cells and
    /// look like a wiring problem rather than the lie it is.
    ///
    /// <para>Break recipe: make <see cref="EditorIcons.EditorTabForNumber"/> clamp instead of
    /// answering null and every row here goes red.</para>
    /// </summary>
    [Fact]
    public void NoScreenTreatsASixthFunctionKeyAsASixthEditor()
    {
        foreach (ShellMode start in EditorIcons.LiveEditorTabs)
        {
            var modes = OpenCart();
            var keys = new ShellCommandReader();
            var pointer = new EditorMouseReader();

            modes.SwitchEditorTab(start);
            Tap(modes, keys, pointer, Keys.F6);
            Assert.Equal(start, modes.Mode);

            Tap(modes, keys, pointer, Keys.F9);
            Assert.Equal(start, modes.Mode);
        }
    }
}
