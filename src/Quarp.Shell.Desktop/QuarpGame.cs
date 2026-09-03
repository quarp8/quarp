using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quarp.CartKit;
using Quarp.Core;
using Quarp.Core.Audio;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Desktop shell: one window, six modes (M9, ADR-026; the boot menu — ADR-028) — the boot
/// menu, the game library, a running cartridge, and the sprite/map/code editors. Without a cart
/// path it opens on the boot screen: a short intro in the console's palette with its jingle
/// (skippable by any key), then QUARP's main menu, whose first door is the library (the
/// console's face; the old windowed test pattern died with M9 — the palette is proven by
/// <c>quarp pattern</c> and by the boot screens themselves, drawn on Master32); with a path it
/// runs that cartridge directly, hot reload and all, no intro, and Esc quits the process — the
/// developer's F5 loop, which neither the menu nor the library interrupts. Mode policy lives in
/// <see cref="ShellModeMachine"/>, editor policy in <see cref="SpriteEditorSession"/>; this
/// class owns only what needs a graphics device, plus the routing of raw input to whichever
/// mode is on screen. The mouse (new in M9 stage 2) is polled every frame; the editors act on
/// it, and since waves R1 and R6 so do the library and the boot menu, which now have a console
/// grid to point at.
/// Polling always keeps the reader's previous-state true across mode switches, so a button held
/// into another screen produces no phantom press.
///
/// <para><b>One resolution, since wave R1.</b> The owner's law of 2026-08-25 — "the console
/// is the same for everyone and in everything" — ended the second machine this class used to
/// run. A running cart is presented as the core's indexed framebuffer scaled by whole integers
/// (ARCHITECTURE §5); the library is now drawn <em>into a framebuffer of its own</em>
/// (<see cref="ShellScreen"/>) with the same core calls a cartridge uses, and both go to the
/// window through the one <see cref="ConsolePresenter"/>. Waves R2-R5 brought the five editor
/// screens onto the same road and wave R6 brought the boot menu, which was the last tenant of
/// the host font path; the host frame, its renderer and both host atlases left the tree with it.
/// There is no second road for this class to be the junction of any more: every mode either
/// presents the cartridge's framebuffer or the shell's, and both go through one presenter at one
/// whole-integer scale.</para>
///
/// <para><b>Time (M2).</b> MonoGame's <c>IsFixedTimeStep</c> is off and replaced by
/// <see cref="TickAccumulator"/>: real time is banked, whole ticks come out, at most five
/// catch-up ticks run in one frame, and <see cref="Draw"/> happens exactly once per frame
/// whatever the tick count was. <c>SynchronizeWithVerticalRetrace</c> stays on for the
/// picture, but no timing decision depends on it (ARCHITECTURE §4).</para>
///
/// <para><b>Two layers, on purpose.</b> The console texture carries the cartridge's frame and
/// nothing else — that frame is the golden master the CI hashes. The pause and speed
/// indicators live in <see cref="ShellOverlay"/>, a second texture blended on top at the same
/// scale, so nothing the shell says about time can ever reach the framebuffer.</para>
///
/// <para><b>Sound (M3).</b> Each tick's 800 samples go straight from the console to
/// <see cref="AudioOutput"/>, which keeps two or three blocks queued on the device. The
/// direction of that arrow is the whole design: ticks produce audio, audio never asks for a
/// tick, and a machine with no sound card changes nothing about what the simulation
/// computes.</para>
/// </summary>
/// <summary>
/// What one frame of the <b>game</b> screen is made of, once the graphics device is subtracted:
/// the surface the window presents, the output state it is presented through, and — only while
/// the pause menu is up — the band laid over it and the surface that band was painted on.
///
/// <para>It exists so the two surfaces can be compared. The cartridge's framebuffer is the golden
/// master this whole project is about; the band and the menu ride an overlay texture precisely so
/// that pausing cannot change a pixel of it, and "cannot" is a claim about which object was
/// handed to which painter. <see cref="QuarpGame.ComposeGameScreen"/> makes that choice and this
/// type reports it, so a headless test can hold both and ask whether they are the same array.</para>
/// </summary>
/// <param name="Presented">The framebuffer the window shows: the running cartridge's own, or the shell's when none is running.</param>
/// <param name="Display">The output state that framebuffer is shown through — its console's, never a mixture.</param>
/// <param name="Band">The paused game's top band, measured and painted; null while the game runs.</param>
/// <param name="BandSurface">The surface <paramref name="Band"/> was painted on, whose first rows the overlay lifts; null while the game runs.</param>
public readonly record struct GameScreenLayers(
    Framebuffer Presented, DisplayPalette Display, GameTabBar? Band, Framebuffer? BandSurface);

public sealed class QuarpGame : Game
{
    private readonly TickAccumulator _accumulator = new();
    private readonly ShellCommandReader _commands = new();
    private readonly EditorMouseReader _mouse = new();
    private readonly IconHoverTracker _hover = new();
    private readonly ToolbarFlyout _flyout = new();
    private readonly SheetScroll _sheetScroll = new();
    private readonly ConsoleProfile _profile;
    private readonly ShellModeMachine _modes;

    /// <summary>
    /// The shell's own console — a second <see cref="VirtualConsole"/> of the same profile,
    /// which the library screen (and, as they follow, the editors) is drawn into. Built here,
    /// in the constructor, because it needs no graphics device: it is virtual hardware, not a
    /// texture. Its framebuffer is a different object from the running cart's, so a tool screen
    /// drawn over a paused game cannot touch the frame that game left behind.
    /// </summary>
    private readonly ShellScreen _shellScreen;

    private SpriteBatch _spriteBatch = null!;
    private ConsolePresenter _presenter = null!;
    private ShellOverlay _overlay = null!;
    private AudioOutput? _audio;

    private TimeSpeed _lastSpeed = TimeSpeed.At(TimeSpeed.NormalIndex);
    private bool _lastPaused;

    /// <summary>The intro's voice: a bare APU with <see cref="BootJingle"/> loaded, alive only while the intro plays.</summary>
    private Apu? _bootApu;

    /// <summary>Banks real time into the jingle's 60 Hz ticks — the game's accumulator discipline, menu-sized.</summary>
    private readonly TickAccumulator _menuTicks = new();

    /// <summary>
    /// The sound editor's voice: a bare <see cref="Apu"/> loaded with the edited bank, alive
    /// only while a slot is being auditioned. The <b>same class the cartridge speaks through</b>
    /// and the same one <see cref="BootJingle"/> already borrows — there is no second
    /// synthesizer in this shell, and the audition therefore cannot sound different from the
    /// game. See <see cref="UpdateSfxPreview"/> for the whole of the arrangement.
    /// </summary>
    private Apu? _sfxApu;

    /// <summary>Banks real time into the audition's 60 Hz ticks — a third accumulator, because a third clock owner would be a bug.</summary>
    private readonly TickAccumulator _sfxTicks = new();

    /// <summary>Which <see cref="SfxEditorView.PlayEpoch"/> <see cref="_sfxApu"/> was started for; a newer one restarts the slot.</summary>
    private int _sfxEpoch;

    /// <summary>
    /// The music editor's voice: a bare <see cref="Apu"/> loaded with <b>both</b> edited banks —
    /// the song and the sounds it names — alive only while a song is being auditioned. The same
    /// class the cartridge speaks through and the same one <see cref="BootJingle"/> and the sfx
    /// audition already borrow; there is no second synthesizer in this shell. See
    /// <see cref="UpdateMusicPreview"/> for the whole of the arrangement.
    /// </summary>
    private Apu? _musicApu;

    /// <summary>Banks real time into the song's 60 Hz ticks — a fourth accumulator, because a fourth clock owner would be a bug.</summary>
    private readonly TickAccumulator _musicTicks = new();

    /// <summary>Which <see cref="MusicEditorView.PlayEpoch"/> <see cref="_musicApu"/> was started for; a newer one restarts the song.</summary>
    private int _musicEpoch;

    /// <summary>Characters from <c>Window.TextInput</c> since the last frame; consumed by the name field only.</summary>
    private readonly List<char> _typedChars = new();

    /// <summary>The last file dropped on the window, until the menu or the library consumes it.</summary>
    private string? _droppedFile;

    /// <summary>
    /// Library mode when <paramref name="cartPath"/> is null; direct-launch cart mode
    /// otherwise. A direct launch's load errors throw out of here so the CLI can turn them
    /// into exit codes; a library launch's errors stay on the library screen instead.
    /// <paramref name="breakAtTick"/> is <c>--break-at N</c> and only means anything with a
    /// cart to stop — the CLI rejects the flag without a path before it gets here.
    /// </summary>
    /// <param name="profile">
    /// Which console to build. Null means <see cref="ConsoleProfile.Profile8"/> — 160x90, the
    /// spec, and the only console the CLI opens today. It stays a parameter rather than a read
    /// of the static because the profile decides every pixel and every frame hash: QUARP-16
    /// (M6) and any test that builds a screen of its own have to be able to say which console
    /// they mean at the one place a console is created.
    /// </param>
    public QuarpGame(string? cartPath = null, int? breakAtTick = null, ConsoleProfile? profile = null)
    {
        StartCompilerWarmUp();

        _profile = profile ?? ConsoleProfile.Profile8;
        CartSession? directSession = null;
        if (cartPath is not null)
        {
            directSession = CartSession.Start(cartPath, _profile);
            directSession.BreakAt = breakAtTick;
            // Lets a long resimulation repaint the window from inside its progress callback
            // instead of freezing it (ARCHITECTURE §4). Cached once — it is called in a loop.
            directSession.PresentFrame = PresentCurrentFrame;
        }
        _modes = new ShellModeMachine(
            new CartLibrary(CartLibrary.DefaultRoots()),
            StartSessionFromLibrary,
            DrainAudio,
            directSession,
            // The machine's own clipboard, for the Ctrl+X/C/V of all FIVE editor screens —
            // code, sprites, map, sound and music (REFERENCES-EDITORS §8 item 2). ONE instance,
            // constructed HERE and nowhere else: it is a host device (SDL2), host devices belong
            // to the window exactly as the keyboard and mouse readers do, and a second one would
            // mean a second answer to "what is on the clipboard" — which is precisely what would
            // break copying a block out of one screen and into another. Everything above it sees
            // only ITextClipboard, which is what keeps all five screens headless-testable.
            textClipboard: new SystemTextClipboard());

        // The shell's drawing surface. Palette unpacking and the frame's placement moved with
        // it into ConsolePresenter, which now owns both for the cart's frame and for this one.
        _shellScreen = new ShellScreen(_profile);

        // x8 of 160x90 is 1280x720 exactly: a whole-pixel scale that is also a standard display
        // mode, so the window fills a 720p screen with nothing left over and needs no letterbox.
        // The scale is chosen for the target hardware of M5 — the uConsole's panel is 1280x720 —
        // and larger desktops get their multiple by resizing, which the presenter already picks
        // per frame (ARCHITECTURE §5). Only integer scales are ever used: a fractional one
        // resamples a pixel-art frame into blur.
        const int WindowScale = 8;
        var graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = _profile.Width * WindowScale,
            PreferredBackBufferHeight = _profile.Height * WindowScale,
            SynchronizeWithVerticalRetrace = true,            // for the picture, not for the clock
        };
        graphics.ApplyChanges();

        // The strict accumulator owns time from here (M2 work order). MonoGame's own fixed
        // step chases an unbounded backlog, which on slow hardware is a death spiral: this
        // way an overloaded machine runs slowly instead of locking up.
        IsFixedTimeStep = false;

        UpdateWindowTitle();
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
    }

    /// <summary>
    /// The mode machine's session factory: what "launch this library entry" means when a
    /// window exists. Load and compile failures throw and the machine turns them into a
    /// library message; wiring happens here because the machine has no business knowing about
    /// textures or sound cards.
    /// </summary>
    private CartSession StartSessionFromLibrary(string path)
    {
        CartSession session = CartSession.Start(path, _profile);
        session.PresentFrame = PresentCurrentFrame;
        if (_audio is not null)
        {
            // LoadContent has run by the time the library can accept a keypress, so the sink
            // is available; the null check is for the audio-less machine, not for ordering.
            session.AudioSink = _audio.Submit;
        }
        return session;
    }

    private void DrainAudio() => _audio?.Drain();

    /// <summary>Session name in the title while a cart runs; the bare console name otherwise.</summary>
    private void UpdateWindowTitle() =>
        Window.Title = _modes.Session is CartSession session ? $"{_profile.Name} — {session.Name}" : _profile.Name;

    /// <summary>
    /// Runs one throwaway compile on a background thread so Roslyn's cold cost (1-3 s of its
    /// own JIT) is paid while the window comes up instead of on the author's first save —
    /// the start-up warm-up required by ARCHITECTURE §3 and the M1 work order. Deliberately
    /// fire-and-forget: it touches neither MonoGame nor console state, and a failure only
    /// means the first real compile is the slow one, so it must never escape this thread.
    /// </summary>
    private static void StartCompilerWarmUp()
    {
        var thread = new Thread(static () =>
        {
            try
            {
                CartCompiler.WarmUp();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[quarp] compiler warm-up failed: {e.Message}");
            }
        })
        {
            IsBackground = true, // Never delays process exit.
            Name = "quarp-warmup",
        };
        thread.Start();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _presenter = new ConsolePresenter(GraphicsDevice, _profile);
        _overlay = new ShellOverlay(GraphicsDevice, _profile.Width, _profile.Height);

        // The two window events the boot screens live on. Characters buffer here and are
        // consumed by the menu's name field once per frame (edge-ordering with the same
        // frame's ShellCommands is then explicit); a dropped file parks until the menu or
        // the library — the two screens where no session owns the window — picks it up.
        Window.TextInput += (_, e) => _typedChars.Add(e.Character);
        Window.FileDrop += (_, e) =>
        {
            if (e.Files is { Length: > 0 })
            {
                _droppedFile = e.Files[0];
            }
        };

        // Opened here rather than in the constructor: the audio device belongs to a running
        // Game, and an unavailable one is reported by AudioOutput rather than thrown.
        _audio = new AudioOutput();
        if (_modes.Session is CartSession session)
        {
            // The direct-launch session predates the device; library launches are wired in
            // the factory. Cached once — this delegate is invoked on every tick, including
            // eight times a frame at x8.
            session.AudioSink = _audio.Submit;
        }
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState keyboard = Keyboard.GetState();
        ShellCommands commands = _commands.Read(keyboard);
        // Read every frame in every mode (see the type comment): the editors and the library
        // consume it as host UI, and since ADR-030 the game mode folds it into the cartridge's
        // input snapshot.
        EditorMouse mouse = _mouse.Read(Mouse.GetState());

        // Which screen this frame's pointer is being measured against. Remembered BEFORE the
        // switch below, because the switch is where a tab key can move the shell to another
        // screen, and the target this frame wrote would then be read by that other screen's
        // Draw — the crash IconHoverTracker.Clear documents in full. Comparing after is the
        // only place that sees both halves of that frame.
        ShellMode screenOnEntry = _modes.Mode;

        // M9 stage 5, Р2 — the one save rule: while the author stands on an editor tab the
        // paused session runs no ticks, so nothing would poll the cartridge folder and a Ctrl+S
        // would reach the running cartridge never. The machine owns the guard (it is a no-op on
        // the game screen, whose own Update polls) so the rule is testable without a window;
        // this line is the whole of the window's part in it.
        _modes.PollSessionReload();

        switch (_modes.Mode)
        {
            case ShellMode.Game:
                // The pointer finally crosses the cartridge boundary here (ADR-030): the same
                // frame of mouse the editors get, folded into the input snapshot inside
                // UpdateGame — through FramePlacement, the same single owner of
                // window-to-console coordinates the tool screens convert through.
                UpdateGame(commands, keyboard, mouse, gameTime);
                break;
            case ShellMode.Library:
                UpdateLibrary(commands, mouse);
                break;
            case ShellMode.Editor:
                // The one screen that has left the window's coordinate system (wave R2): its
                // layout is measured in console pixels, so it is handed the console's size and
                // a pointer already translated into console space. Both conversions happen
                // HERE, in the class that owns the window, and both go through
                // FramePlacement — the single owner of window-to-console coordinates. The
                // router does no scale arithmetic of its own, and neither may any other reader.
                SpriteEditorInput.Update(
                    ConsoleEditorContext(),
                    commands,
                    mouse.ToConsole(_shellScreen.Placement(
                        GraphicsDevice.PresentationParameters.BackBufferWidth,
                        GraphicsDevice.PresentationParameters.BackBufferHeight)),
                    gameTime.ElapsedGameTime.TotalSeconds);
                break;
            case ShellMode.MapEditor:
                // The second screen off the window's coordinate system (wave R3): same two
                // conversions the sprite screen gets, through the same single owner. The router
                // does no scale arithmetic of its own, and neither may any other reader.
                MapEditorInput.Update(
                    ConsoleEditorContext(),
                    commands,
                    mouse.ToConsole(_shellScreen.Placement(
                        GraphicsDevice.PresentationParameters.BackBufferWidth,
                        GraphicsDevice.PresentationParameters.BackBufferHeight)),
                    gameTime.ElapsedGameTime.TotalSeconds);
                break;
            case ShellMode.CodeEditor:
                // The third screen off the window's coordinate system (wave R4): same two
                // conversions its two neighbours get, through the same single owner. What only
                // this screen needs on top is the CHARACTER stream, not just the key frame:
                // typing wants the author's keyboard layout, dead keys and auto-repeat, which
                // only Window.TextInput knows. The buffer is handed over as a plain list and
                // cleared below with the frame, so the router sees exactly what arrived since
                // the previous frame — in order, once. See CodeEditorInput's type comment.
                CodeEditorInput.Update(
                    ConsoleEditorContext(),
                    commands,
                    mouse.ToConsole(_shellScreen.Placement(
                        GraphicsDevice.PresentationParameters.BackBufferWidth,
                        GraphicsDevice.PresentationParameters.BackBufferHeight)),
                    _typedChars,
                    gameTime.ElapsedGameTime.TotalSeconds);
                break;
            case ShellMode.SfxEditor:
                // The third screen off the window's coordinate system (wave R5): same two
                // conversions the sprite and map screens get, through the same single owner.
                // The router does no scale arithmetic of its own, and neither may any other
                // reader.
                SfxEditorInput.Update(
                    ConsoleEditorContext(),
                    commands,
                    mouse.ToConsole(_shellScreen.Placement(
                        GraphicsDevice.PresentationParameters.BackBufferWidth,
                        GraphicsDevice.PresentationParameters.BackBufferHeight)),
                    gameTime.ElapsedGameTime.TotalSeconds);
                // The one editor frame with a second half: the router decided whether the slot
                // should sound, and this drives the chip that makes it so. Kept out of the
                // router on purpose — a router that owned a speaker could not run in a headless
                // test, which is the property wave 3c bought and this wave keeps.
                UpdateSfxPreview(gameTime);
                break;
            case ShellMode.MusicEditor:
                // The fifth screen off the window's coordinate system: same two conversions its
                // four siblings get, through the same single owner.
                MusicEditorInput.Update(
                    ConsoleEditorContext(),
                    commands,
                    mouse.ToConsole(_shellScreen.Placement(
                        GraphicsDevice.PresentationParameters.BackBufferWidth,
                        GraphicsDevice.PresentationParameters.BackBufferHeight)),
                    gameTime.ElapsedGameTime.TotalSeconds);
                // The second editor frame with a second half, for the reason the sound screen's
                // has one: the router decided whether the song should sound, and this drives the
                // chip that makes it so. Kept out of the router on purpose — a router that owned a
                // speaker could not run in a headless test.
                UpdateMusicPreview(gameTime);
                break;
            case ShellMode.Menu:
                UpdateMenu(commands, keyboard, mouse, gameTime);
                break;
        }
        // Typed characters not consumed by the name field or the code editor this frame are
        // stale by the next,
        // and a file dropped on a screen that does not take drops (a running game, an open
        // editor) is discarded rather than parked — surfacing it minutes later on some other
        // screen would be a launch nobody just asked for.
        if (_modes.Mode != screenOnEntry)
        {
            // The screen moved under the pointer during this very frame. What was under it
            // belonged to the screen that has just left, and a target measured on one layout
            // means nothing on another — see IconHoverTracker.Clear for the crash this line
            // fixes. The flyout goes with it: an open list belongs to the button that opened
            // it, and that button is on the screen we just walked off.
            _hover.Clear();
            _flyout.Close();
        }
        _typedChars.Clear();
        _droppedFile = null;
        if (_modes.Mode != ShellMode.Game)
        {
            // Sub-notch wheel movement belongs to the frame's screen. Whatever fraction of a
            // notch was left over when the player last scrolled in a game must not surface as
            // a phantom scroll step minutes later in a different cartridge.
            _wheelDetentRemainder = 0;
        }

        if (_modes.ExitRequested)
        {
            Exit();
            return;
        }
        base.Update(gameTime);
    }

    /// <summary>
    /// One frame of the game screen: the router's verdict first, then ticks, audio and — since
    /// ADR-030 — the cartridge's pointer.
    ///
    /// <para><b>Every key of this screen moved into <see cref="GameScreenInput"/> in M9 stage 5</b>,
    /// which is the sixth router and the first one this screen has ever had. What is left here is
    /// what genuinely needs a window: the frame clock, the speaker and the input snapshot. The
    /// screen also stopped being a screen that always has a cartridge behind it — F1 from an
    /// editor opened out of the library lands here with nothing running and the pause menu
    /// offering START (Р7), which is why the session is read as a nullable now.</para>
    /// </summary>
    private void UpdateGame(in ShellCommands commands, KeyboardState keyboard, in EditorMouse mouse, GameTime gameTime)
    {
        CartSession? before = _modes.Session;
        GameScreenInput.Update(
            ConsoleEditorContext(),
            commands,
            mouse.ToConsole(_shellScreen.Placement(
                GraphicsDevice.PresentationParameters.BackBufferWidth,
                GraphicsDevice.PresentationParameters.BackBufferHeight)),
            gameTime.ElapsedGameTime.TotalSeconds);
        if (_modes.ExitRequested)
        {
            return;             // Update picks this up and calls Exit
        }
        if (_modes.Mode != ShellMode.Game)
        {
            // A tab key or a tab click took us to an editor, or Exit went back to the library.
            UpdateWindowTitle();
            _accumulator.Reset();
            return;
        }
        if (_modes.Session is not CartSession session)
        {
            return;             // the pause menu on a blank screen: nothing to tick
        }
        if (!ReferenceEquals(before, session))
        {
            OnSessionStarted();     // the menu's START just launched one — same wiring as every other road
        }

        // A speed change or a pause invalidates the banked remainder: it was measured in the
        // old rung's units, and carrying it across would spit out a burst of ticks nobody
        // asked for on the frame the player pressed the key.
        TimeSpeed speed = session.Speed;
        bool paused = session.IsPaused;
        if (speed.Numerator != _lastSpeed.Numerator
            || speed.Denominator != _lastSpeed.Denominator
            || paused != _lastPaused)
        {
            _accumulator.Reset();
            _lastSpeed = speed;
            _lastPaused = paused;
        }

        // Backspace rewinds in real time at the selected speed, so the same budget of ticks
        // is spent going backwards. Pause does not stop it: rewinding out of a pause (or out
        // of a crash) is exactly when it is most wanted.
        bool rewinding = commands.Rewinding;
        int ticks = paused && !rewinding
            ? 0
            : _accumulator.Advance(gameTime.ElapsedGameTime.Ticks, speed);

        // The full ADR-030 snapshot: the keyboard-and-gamepad button masks exactly as before,
        // plus the pointer in console pixels. The window-to-console translation goes through
        // the same FramePlacement the game's frame is presented with (ConsolePresenter computes
        // the identical placement from the identical inputs), so the pixel under the cursor is
        // the pixel the cartridge reads. The console draws no cursor over a running cart —
        // ADR-030 hands the cartridge numbers, and the cursor sprite is the cartridge's to
        // draw, exactly as it is in the originals the library ports.
        InputState input = InputMapper.Read(
            keyboard,
            mouse,
            _shellScreen.Placement(
                GraphicsDevice.PresentationParameters.BackBufferWidth,
                GraphicsDevice.PresentationParameters.BackBufferHeight),
            ConsumeWheelSteps(mouse.WheelDelta));
        session.Update(ticks, input, rewinding);

        // Once a frame, whatever the simulation did: tops the device queue up with silence so
        // a pause, a rewind or a stalled machine is quiet instead of a source running dry.
        // Only in game mode — the library's silence comes from Drain having stopped the
        // source, and topping it up would just count Padded blocks nobody can hear.
        _audio?.EndFrame();
    }

    /// <summary>
    /// MonoGame wheel detents accumulated toward whole notches between game frames. Editors
    /// read the raw detents themselves; the cartridge's <c>MouseWheel</c> is whole steps
    /// (API-8 §4), and a trackpad delivers fractions of a notch per frame that must add up
    /// rather than truncate to an eternal zero. Cleared whenever the shell is not on the game
    /// screen — see the end of <see cref="Update"/>.
    /// </summary>
    private int _wheelDetentRemainder;

    /// <summary>One MonoGame wheel notch, the unit <c>ScrollWheelValue</c> counts in.</summary>
    private const int WheelDetentsPerStep = 120;

    /// <summary>Whole wheel steps this frame's detents add up to; the sub-notch remainder is banked.</summary>
    private int ConsumeWheelSteps(int detents)
    {
        int total = _wheelDetentRemainder + detents;
        int steps = total / WheelDetentsPerStep;    // Truncation toward zero: a direction change spends the bank first.
        _wheelDetentRemainder = total - (steps * WheelDetentsPerStep);
        return steps;
    }

    /// <summary>
    /// One frame of the library: selection, launch, opening the sprite editor, or leaving for
    /// the menu — by key, and since wave R1 by pointer too. The library was keyboard-only for
    /// as long as it was host UI; now that it is drawn on the console there is a grid to point
    /// at, and the pointer reaches it the one legal way — through
    /// <see cref="ShellScreen.Placement"/>, which is the single owner of window-to-console
    /// coordinates. This method does no scale arithmetic of its own, and neither may any other.
    /// </summary>
    private void UpdateLibrary(in ShellCommands commands, in EditorMouse mouse)
    {
        if (ConsumeDroppedFile())
        {
            return;
        }
        if (commands.Quit)
        {
            _modes.HandleEscape();      // Library Esc = back to the boot menu (ADR-028).
            return;
        }
        if (commands.MenuUp)
        {
            _modes.Library.MoveSelection(-1);
        }
        if (commands.MenuDown)
        {
            _modes.Library.MoveSelection(+1);
        }
        if (commands.MenuEditor)
        {
            _modes.OpenEditor();
            return;
        }
        if (commands.MenuConfirm && _modes.LaunchSelected() is not null)
        {
            OnSessionStarted();
        }
        else if (mouse.LeftPressed)
        {
            ClickLibrary(mouse);
        }
    }

    /// <summary>
    /// A click on the list. Two-step on purpose, the way a desktop list behaves: the first
    /// press moves the bar, a press on the row that already has the bar launches it. A single
    /// press that both selected and launched would make a misplaced click start a cartridge,
    /// and this screen's rows are seven console pixels tall.
    ///
    /// <para>The selection is moved through <see cref="CartLibrary.MoveSelection"/> rather
    /// than by assigning an index: that method owns the clamping rule, and a second way to set
    /// the bar would be a second place for it to go out of range.</para>
    /// </summary>
    private void ClickLibrary(in EditorMouse mouse)
    {
        FramePlacement placement = _shellScreen.Placement(
            GraphicsDevice.PresentationParameters.BackBufferWidth,
            GraphicsDevice.PresentationParameters.BackBufferHeight);
        if (!placement.TryToCanvas(mouse.X, mouse.Y, out int consoleX, out int consoleY))
        {
            return;     // The letterbox. Not a click on the nearest row — see FramePlacement.
        }
        LibraryLayout layout = LibraryRenderer.LayoutFor(_shellScreen, _modes.Library, _modes.LibraryMessage);
        if (layout.HitRow(consoleX, consoleY) is not int index)
        {
            return;
        }
        if (index != _modes.Library.SelectedIndex)
        {
            _modes.Library.MoveSelection(index - _modes.Library.SelectedIndex);
            return;
        }
        if (_modes.LaunchSelected() is not null)
        {
            OnSessionStarted();
        }
    }

    /// <summary>
    /// One frame of the boot screen (M9 stage 4, ADR-028). The intro is its own little
    /// world — the clock advances, the jingle ticks, any fresh key or click cuts to the menu
    /// — and the menu itself is three doors: arrows or the 1-2-3 hotkeys the mockup prints,
    /// Z/Enter to walk through, Esc to leave the console. The name field, while up, owns the
    /// keyboard the way the editor's exit prompt does: characters land in it, Enter creates,
    /// Esc cancels, and the rows underneath are deliberately deaf.
    /// </summary>
    private void UpdateMenu(in ShellCommands commands, KeyboardState keyboard, in EditorMouse mouse, GameTime gameTime)
    {
        MainMenuSession menu = _modes.Menu;
        if (menu.Phase == MenuPhase.Intro)
        {
            bool anyInput = keyboard.GetPressedKeys().Length > 0 || mouse.LeftDown;
            bool left = menu.AdvanceIntro(gameTime.ElapsedGameTime.TotalSeconds, anyInput);
            PlayBootJingle(gameTime, stopNow: left);
            return;
        }
        if (ConsumeDroppedFile())
        {
            return;
        }
        if (menu.Phase == MenuPhase.NameEntry)
        {
            foreach (char c in _typedChars)
            {
                if (c == '\b')
                {
                    menu.EraseChar();
                }
                else if (!char.IsControl(c))
                {
                    menu.TypeChar(c);   // the field folds case and drops what a folder cannot hold
                }
            }
            if (commands.Quit)
            {
                _modes.HandleEscape();          // cancels the field, stays on the menu
            }
            else if (commands.MenuConfirm)
            {
                _modes.ConfirmCreateGame();     // straight into the editor on success
            }
            return;
        }
        if (commands.Quit)
        {
            _modes.HandleEscape();              // the menu is the root: Esc leaves the process
            return;
        }
        if (commands.MenuUp)
        {
            menu.MoveSelection(-1);
        }
        if (commands.MenuDown)
        {
            menu.MoveSelection(+1);
        }
        // The reader's digit field is named for the editor's toolbar, but it reports plain
        // D1..D6 edges and the menu's rows are numbered 1-3 on screen — a digit is select
        // and go in one press.
        bool digitGo = menu.ActivateDigit(commands.EditorToolDigit);
        if (digitGo || commands.MenuConfirm)
        {
            ActivateMenuItem(menu.Selected);
        }
        else if (mouse.LeftPressed)
        {
            ClickMenu(menu, mouse);
        }
    }

    /// <summary>
    /// A click on a door. Two-step exactly like <see cref="ClickLibrary"/>, and for the same
    /// reason: the first press moves the bar, a press on the door that already has the bar walks
    /// through it. These rows are seven console pixels tall, and door 2 opens an OS dialog while
    /// door 3 writes a folder — a misplaced click must not do either.
    ///
    /// <para><b>Why the menu can be clicked at all now.</b> It was keyboard-only for as long as
    /// it was host UI, exactly as the library was before wave R1: there was no grid to point at,
    /// only a <c>SpriteBatch</c>. Wave R6 gave it the console's grid, so the pointer reaches it
    /// the one legal way — through <see cref="ShellScreen.Placement"/>, the single owner of
    /// window-to-console coordinates. This method does no scale arithmetic of its own, and
    /// neither may any other.</para>
    /// </summary>
    private void ClickMenu(MainMenuSession menu, in EditorMouse mouse)
    {
        FramePlacement placement = _shellScreen.Placement(
            GraphicsDevice.PresentationParameters.BackBufferWidth,
            GraphicsDevice.PresentationParameters.BackBufferHeight);
        if (!placement.TryToCanvas(mouse.X, mouse.Y, out int consoleX, out int consoleY))
        {
            return;     // The letterbox. Not a click on the nearest door — see FramePlacement.
        }
        if (MainMenuRenderer.LayoutFor(_shellScreen).HitRow(consoleX, consoleY) is not int index)
        {
            return;
        }
        if (index != menu.SelectedIndex)
        {
            menu.MoveSelection(index - menu.SelectedIndex);
            return;
        }
        ActivateMenuItem(menu.Selected);
    }

    /// <summary>The three doors. LOAD CART tries the OS picker and reports its refusal, if any, on the message line.</summary>
    private void ActivateMenuItem(MenuItem item)
    {
        switch (item)
        {
            case MenuItem.Library:
                _modes.OpenLibrary();
                break;
            case MenuItem.LoadCart:
                if (FilePicker.TryPick(out string path, out string? refusal))
                {
                    LaunchFromPath(path);
                }
                else if (refusal is not null)
                {
                    _modes.Menu.Message = refusal;
                }
                break;
            case MenuItem.CreateGame:
                _modes.BeginCreateGame();
                break;
        }
    }

    /// <summary>A picked or dropped cart, launched with the same bookkeeping as a library launch.</summary>
    private void LaunchFromPath(string path)
    {
        if (_modes.LoadCartFromPath(path) is not null)
        {
            OnSessionStarted();
        }
    }

    /// <summary>
    /// True when a file dropped on the window was just consumed — the menu and the library
    /// both call this first, so a drop outranks whatever key landed the same frame. The
    /// machine turns a bad drop into a message on the screen the author is looking at.
    /// </summary>
    private bool ConsumeDroppedFile()
    {
        if (_droppedFile is not string path)
        {
            return false;
        }
        _droppedFile = null;
        LaunchFromPath(path);
        return true;
    }

    /// <summary>
    /// A fresh session starts at normal speed and unpaused; the banked remainder of however
    /// long the player browsed must not become a burst of catch-up ticks. One owner for the
    /// three launch roads (library row, OS picker, dropped file).
    /// </summary>
    private void OnSessionStarted()
    {
        _accumulator.Reset();
        _lastSpeed = TimeSpeed.At(TimeSpeed.NormalIndex);
        _lastPaused = false;
        UpdateWindowTitle();
    }

    /// <summary>
    /// The intro's sound, rendered by a bare APU at the same 60 Hz the accumulator gives a
    /// cartridge, fed to the same speaker. Born on the intro's first audible frame; drained
    /// and dropped the moment the intro ends, by clock or by skip — the menu is silent, and
    /// a skipped jingle stopping mid-note is the honest cut (TIC-80's --skip skips the sound
    /// too). No sound card, no jingle, nothing else changes — the audio arrow keeps pointing
    /// one way (M3).
    /// </summary>
    private void PlayBootJingle(GameTime gameTime, bool stopNow)
    {
        if (_audio is not AudioOutput audio || !audio.IsAvailable)
        {
            return;
        }
        if (stopNow)
        {
            if (_bootApu is not null)
            {
                audio.Drain();
                _bootApu = null;
            }
            return;
        }
        if (_bootApu is null)
        {
            _bootApu = new Apu();
            BootJingle.Start(_bootApu);
            _menuTicks.Reset();
        }
        int ticks = _menuTicks.Advance(gameTime.ElapsedGameTime.Ticks, TimeSpeed.At(TimeSpeed.NormalIndex));
        for (int i = 0; i < ticks; i++)
        {
            _bootApu.RenderTick();
            audio.Submit(_bootApu.Block);
        }
        audio.EndFrame();
    }

    /// <summary>
    /// The sound editor's audition, and the answer to "who synthesizes it": the <b>same</b>
    /// <see cref="Apu"/> a cartridge runs on, fed the session's own payload and rendered at the
    /// same 60 Hz the accumulator gives a game, into the same <see cref="AudioOutput"/>. The
    /// arrangement is <see cref="PlayBootJingle"/>'s, one screen over, and it exists for the
    /// reason that one does: the shell may not grow a second synthesizer, so when it needs a
    /// sound of its own it borrows the console's chip instead of imitating it.
    ///
    /// <para><b>Why a bare APU rather than the cartridge's.</b> There is no cartridge here —
    /// <see cref="ShellModeMachine.Session"/> is null in every editor mode, and starting one to
    /// hear a beep would run somebody's game. A bare <see cref="Apu"/> is the same class, the
    /// same integer arithmetic and the same 800 samples a tick; what it is missing is a
    /// <c>VirtualConsole</c> around it, which an audition does not need.</para>
    ///
    /// <para><b>What the author hears is what the cartridge will play</b>, because
    /// <see cref="Apu.LoadSfxPayload"/> takes the very bytes <see cref="SfxEditorSession.Save"/>
    /// writes — not a copy the editor keeps in another shape. The payload is reloaded on every
    /// fresh ask, so an edit made between two presses of Space is heard on the second.</para>
    ///
    /// <para>No sound card, no audition, and nothing else changes: the view is told the slot is
    /// not sounding and the play button goes dark, which is the honest answer rather than a
    /// button that pretends. Leaving the tab, changing the slot, or the slot running out of
    /// steps all end it through the one door — <see cref="SfxEditorView.ReportPlaying"/>.</para>
    /// </summary>
    private void UpdateSfxPreview(GameTime gameTime)
    {
        if (_modes.SfxEditor is not SfxEditorSession session || _modes.SfxView is not SfxEditorView view)
        {
            return;
        }
        if (_audio is not AudioOutput audio || !audio.IsAvailable)
        {
            view.ReportPlaying(false);
            return;
        }
        if (!view.PlayWanted)
        {
            StopSfxPreview(audio, view);
            return;
        }
        if (_sfxApu is null || _sfxEpoch != view.PlayEpoch)
        {
            _sfxApu = new Apu();
            _sfxApu.LoadSfxPayload(session.Payload);
            _sfxApu.PlaySfx(view.SelectedSlot, 0);
            _sfxEpoch = view.PlayEpoch;
            _sfxTicks.Reset();
        }
        int ticks = _sfxTicks.Advance(gameTime.ElapsedGameTime.Ticks, TimeSpeed.At(TimeSpeed.NormalIndex));
        for (int i = 0; i < ticks; i++)
        {
            _sfxApu.RenderTick();
            audio.Submit(_sfxApu.Block);
        }
        audio.EndFrame();
        if (!_sfxApu.IsChannelBusy(0))
        {
            StopSfxPreview(audio, view);        // a one-shot slot ended: the button goes dark by itself
        }
        else
        {
            view.ReportPlaying(true);
        }
    }

    /// <summary>Drops the audition's chip and the tail it left on the device — the game-to-library drain, slot-sized.</summary>
    private void StopSfxPreview(AudioOutput audio, SfxEditorView view)
    {
        if (_sfxApu is not null)
        {
            audio.Drain();
            _sfxApu = null;
        }
        view.ReportPlaying(false);
    }

    /// <summary>
    /// The music editor's audition, and the answer to "who synthesizes it": the <b>same</b>
    /// <see cref="Apu"/> a cartridge runs on, rendered at the same 60 Hz the accumulator gives a
    /// game, into the same <see cref="AudioOutput"/>. The arrangement is
    /// <see cref="UpdateSfxPreview"/>'s, one tab over, and it exists for the reason that one does:
    /// the shell may not grow a second synthesizer, so when it needs a sound of its own it borrows
    /// the console's chip instead of imitating it.
    ///
    /// <para><b>Two banks, not one, and that is the whole difference from the sound screen.</b> A
    /// pattern of <c>music.bin</c> holds <em>references</em> to SFX slots — there is not one note
    /// in the file (AUDIO-FORMAT §4) — so a preview loaded with the song alone would run the
    /// sequencer in silence. The effects bank comes from
    /// <see cref="ShellModeMachine.EnsureSfxBank"/>, i.e. from the very session the SOUND tab
    /// edits, so what the author hears is the sounds as they stand right now, saved or not. A bank
    /// that will not load leaves the sequencer running and silent rather than taking the screen
    /// down: the message has already been put on the library line by the machine.</para>
    ///
    /// <para><b>What mute and solo do here, and what they do not.</b> The payload handed to the
    /// chip is <see cref="MusicEditorView.AudiblePayload"/> — a <b>copy</b> of the session's bytes
    /// with the inaudible channels silenced. The cartridge's own 320 bytes are never touched by a
    /// listening decision, which is the promise <see cref="MusicEditorView"/> makes and this is
    /// the one place that could have broken it.</para>
    ///
    /// <para>No sound card, no audition, and nothing else changes: the view is told the song is
    /// not sounding, the play button goes dark and the playhead disappears — the honest answer
    /// rather than a button that pretends. A stop flag, running off pattern 63, leaving the tab or
    /// pressing Space again all end it through the one door,
    /// <see cref="MusicEditorView.ReportPlaying"/>.</para>
    /// </summary>
    private void UpdateMusicPreview(GameTime gameTime)
    {
        if (_modes.MusicEditor is not MusicEditorSession session
            || _modes.MusicView is not MusicEditorView view)
        {
            return;
        }
        if (_audio is not AudioOutput audio || !audio.IsAvailable)
        {
            view.ReportPlaying(false, MusicEditorView.NoPattern);
            return;
        }
        if (!view.PlayWanted)
        {
            StopMusicPreview(audio, view);
            return;
        }
        if (_musicApu is null || _musicEpoch != view.PlayEpoch)
        {
            _musicApu = new Apu();
            if (_modes.EnsureSfxBank() is SfxEditorSession sfx)
            {
                _musicApu.LoadSfxPayload(sfx.Payload);
            }
            _musicApu.LoadMusicPayload(view.AudiblePayload(session));
            _musicApu.PlayMusic(view.PlayFrom);
            _musicEpoch = view.PlayEpoch;
            _musicTicks.Reset();
        }
        int ticks = _musicTicks.Advance(gameTime.ElapsedGameTime.Ticks, TimeSpeed.At(TimeSpeed.NormalIndex));
        for (int i = 0; i < ticks; i++)
        {
            _musicApu.RenderTick();
            audio.Submit(_musicApu.Block);
        }
        audio.EndFrame();
        if (!_musicApu.IsMusicPlaying)
        {
            StopMusicPreview(audio, view);      // a stop flag or the end of the song: the button goes dark by itself
        }
        else
        {
            view.ReportPlaying(true, _musicApu.CurrentPattern);
        }
    }

    /// <summary>Drops the song's chip and the tail it left on the device — the sfx audition's stop, song-sized.</summary>
    private void StopMusicPreview(AudioOutput audio, MusicEditorView view)
    {
        if (_musicApu is not null)
        {
            audio.Drain();
            _musicApu = null;
        }
        view.ReportPlaying(false, MusicEditorView.NoPattern);
    }

    /// <summary>
    /// The whole of what the editor input routers (wave 3c) are allowed to see of this window:
    /// the shell state they steer, plus the back buffer as two numbers. Built per call because
    /// a resize changes the size mid-session — and built here, in the one class that owns a
    /// graphics device, so that the editor routers can name no MonoGame type at all and
    /// therefore run in a headless test. Since wave R4 only the sound screen's router reads this
    /// one; the other three take <see cref="ConsoleEditorContext"/>.
    /// </summary>
    private EditorShell EditorContext() =>
        new(
            _modes,
            _flyout,
            _hover,
            _sheetScroll,
            GraphicsDevice.PresentationParameters.BackBufferWidth,
            GraphicsDevice.PresentationParameters.BackBufferHeight);

    /// <summary>
    /// The same context for the three screens that are drawn on the console (waves R2-R4): the
    /// surface they measure themselves against is the shell's own framebuffer, not the back
    /// buffer, so the two numbers they receive are 160 and 90. A router cannot tell the
    /// difference and must not — it lays out and hit-tests in the surface it is given, which is
    /// exactly why the same type serves both the console screens and the one that has not moved
    /// yet.
    /// </summary>
    private EditorShell ConsoleEditorContext() =>
        new(_modes, _flyout, _hover, _sheetScroll, _shellScreen.Width, _shellScreen.Height);

    protected override void Draw(GameTime gameTime)
    {
        switch (_modes.Mode)
        {
            case ShellMode.Game:
                RenderFrame();
                break;
            case ShellMode.Library:
            case ShellMode.Editor:
            case ShellMode.MapEditor:
            case ShellMode.CodeEditor:
            case ShellMode.SfxEditor:
            case ShellMode.MusicEditor:
            case ShellMode.Menu:
                // Every screen on one road since wave R6 — the boot menu included: all are
                // drawn into the shell's own console and presented by the same presenter the
                // cartridge's frame goes through. The draw clock feeds the sprite screen's
                // marching ants and the code screen's caret blink — chrome animating in host
                // time, like the tooltip delay; no simulation or hash sees it. The menu's own
                // animation is not on that clock: the intro is a pure function of the session's
                // own, which is what lets a test pin a frame of it.
                RenderShellScreen(gameTime.TotalGameTime.TotalSeconds);
                break;
        }
        base.Draw(gameTime);        // the game loop presents for us
    }

    /// <summary>
    /// Renders and <b>presents</b> one frame outside the game loop. Called straight from
    /// <see cref="CartSession"/>'s progress callback during a long resimulation: the
    /// simulation is blocking the main thread there, so nothing else can keep the window from
    /// going dark. It touches no game state, only textures, and swallows device errors —
    /// a repaint that fails must not turn a slow rebuild into a crash.
    /// </summary>
    private void PresentCurrentFrame()
    {
        try
        {
            RenderFrame();
            GraphicsDevice.Present();
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
            // Device lost, resizing, or shutting down mid-rebuild. Nothing to do but skip
            // this repaint.
        }
    }

    private void RenderFrame()
    {
        if (_spriteBatch is null || _presenter is null || _overlay is null)
        {
            return;     // Called before LoadContent — a crash during the very first reload.
        }
        CartSession? session = _modes.Session;
        GameScreenLayers layers = ComposeGameScreen(_modes, _shellScreen, _hover.Target, _hover.TooltipVisible);
        _overlay.Show(session?.Status, session?.StatusPercent ?? -1);
        if (layers.Band is GameTabBar band)
        {
            _overlay.ShowMenu(
                _modes.PauseMenu.Text(_modes.MenuTick),
                _modes.PauseMenu.Box(_shellScreen.Width, _shellScreen.Height),
                _modes.PauseMenu.TextOrigin(_shellScreen.Width, _shellScreen.Height));
            _overlay.ShowBand(layers.BandSurface!.Pixels, band.Rows);
        }
        else
        {
            _overlay.HideMenu();
            _overlay.HideBand();
        }

        _presenter.ClearLetterbox();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        Rectangle dest = _presenter.Draw(
            _spriteBatch,
            layers.Presented,
            layers.Display,
            GraphicsDevice.PresentationParameters.BackBufferWidth,
            GraphicsDevice.PresentationParameters.BackBufferHeight);
        // The overlay goes over the same rectangle, so its pixels line up with console
        // pixels — and it is a texture of its own, so the framebuffer stays untouched.
        _overlay.Draw(_spriteBatch, dest);
        _spriteBatch.End();
    }

    /// <summary>
    /// Everything <see cref="RenderFrame"/> decides that does not need a graphics device: which
    /// surface the window presents on the game screen, and — while the pause menu is up — the
    /// band painted over it (M9 stage 5a).
    ///
    /// <para><b>Why this is a static method and not four lines inside the render.</b> The stage's
    /// headline promise is that <em>the frame on screen at the moment of the pause is the frame
    /// the player was playing</em>: the cartridge's framebuffer is the project's golden master
    /// (<c>quarp sim</c> hashes it, CI compares those hashes across architectures), so the band
    /// must neither write a pixel of it nor push it down by its eleven rows. That promise lives
    /// in the choice of two surfaces, and while the choice sat inside a device-bound method the
    /// only test that could reach it was one that made both surfaces itself and then compared the
    /// cartridge's frame with a copy of itself — a check that stayed green with the band's draw
    /// commented out. Here the choice is the thing under test: hand this a real session and it
    /// answers with the two framebuffers it picked, and they are either the same object or they
    /// are not.</para>
    ///
    /// <para>With no cartridge running (Р7's START menu) the game screen <em>is</em> the shell's
    /// own console, so that is what comes back as <see cref="GameScreenLayers.Presented"/> — and
    /// it is cleared here when the band is not going to clear it. That is also the one state in
    /// which the two surfaces are deliberately the <b>same</b> object: with no cartridge there is
    /// no golden master to keep off, and the band is simply part of the picture.</para>
    /// </summary>
    public static GameScreenLayers ComposeGameScreen(
        ShellModeMachine modes, ShellScreen shell, HoverTarget? hover, bool tooltipVisible)
    {
        ArgumentNullException.ThrowIfNull(modes);
        ArgumentNullException.ThrowIfNull(shell);
        CartSession? session = modes.Session;
        // The cartridge's own surfaces while one is running: never the shell's, or the paused
        // picture would be the tool screen the author last drew instead of the game.
        Framebuffer presented = session?.Framebuffer ?? shell.Framebuffer;
        DisplayPalette display = session?.Display ?? shell.Display;
        if (!modes.PauseMenu.Shown)
        {
            // Playing: no band at all (160x90 is too small to spend eleven rows on a player who
            // is not editing anything — see GameTabBar). With nothing running the screen still
            // has to be a picture, so the shell's console is cleared rather than left holding
            // whatever the last mode drew.
            if (session is null)
            {
                shell.Begin();
                shell.Console.Cls(0);
            }
            return new GameScreenLayers(presented, display, Band: null, BandSurface: null);
        }
        // Paused. The band goes on the SHELL's console — the one surface that is never presented
        // while a cartridge is on screen — and ShellOverlay.ShowBand lifts its finished rows into
        // the RGBA layer the pause menu and the PAUSE indicator already ride on. See GameTabBar
        // for why finished pixels travel rather than a description of them.
        GameTabBar bar = GameTabBar.Compute(shell.Width, shell.Height);
        bar.Draw(shell, modes.GameTitle, hover, tooltipVisible);
        return new GameScreenLayers(presented, display, bar, shell.Framebuffer);
    }

    /// <summary>
    /// One frame of a tool screen: draw it into the shell's console, then hand that framebuffer
    /// to the same presenter the cartridge's frame goes through. The whole of what "the editor
    /// runs on the same virtual hardware" means at this layer — the only difference between
    /// this method and <see cref="RenderFrame"/> is which framebuffer is presented and whether
    /// the pause indicator has anything to say.
    ///
    /// <para>As of wave R6 <b>every</b> screen lives here, with nothing left over: the boot
    /// menu (R6), the library (R1), the sprite editor (R2), the map editor (R3), the code
    /// editor (R4), the sound editor (R5) and the music editor. Which one is drawn is the
    /// mode's business and nothing else changes — same console, same presenter, same
    /// whole-integer scale. The host frame and the host font path left the tree with the menu:
    /// there is no second resolution in this shell any more, which is the whole of ADR-029.</para>
    /// </summary>
    private void RenderShellScreen(double timeSeconds)
    {
        if (_spriteBatch is null || _presenter is null)
        {
            return;
        }
        if (_modes.Mode == ShellMode.Editor)
        {
            SpriteEditorRenderer.Draw(
                _shellScreen, _modes.Editor!, _hover.Target, _hover.TooltipVisible,
                _flyout.OpenSlot, _sheetScroll, timeSeconds, _modes.SpriteView, _modes.Indexes);
        }
        else if (_modes.Mode == ShellMode.MapEditor)
        {
            MapEditorRenderer.Draw(
                _shellScreen, _modes.MapEditor!, _modes.Editor!, _modes.MapView!,
                _hover.Target, _hover.TooltipVisible, _modes.Indexes);
        }
        else if (_modes.Mode == ShellMode.CodeEditor)
        {
            CodeEditorRenderer.Draw(
                _shellScreen, _modes.CodeEditor!, _modes.CodeView!,
                _hover.Target, _hover.TooltipVisible, timeSeconds);
        }
        else if (_modes.Mode == ShellMode.SfxEditor)
        {
            SfxEditorRenderer.Draw(
                _shellScreen, _modes.SfxEditor!, _modes.SfxView!,
                _hover.Target, _hover.TooltipVisible, _modes.Indexes);
        }
        else if (_modes.Mode == ShellMode.MusicEditor)
        {
            MusicEditorRenderer.Draw(
                _shellScreen, _modes.MusicEditor!, _modes.MusicView!,
                _hover.Target, _hover.TooltipVisible, _modes.Indexes);
        }
        else if (_modes.Mode == ShellMode.Menu)
        {
            MainMenuRenderer.Draw(_shellScreen, _modes.Menu);
        }
        else
        {
            LibraryRenderer.Draw(_shellScreen, _modes.Library, _modes.LibraryMessage);
        }
        _presenter.ClearLetterbox();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        _presenter.Draw(
            _spriteBatch,
            _shellScreen.Framebuffer,
            _shellScreen.Display,
            GraphicsDevice.PresentationParameters.BackBufferWidth,
            GraphicsDevice.PresentationParameters.BackBufferHeight);
        _spriteBatch.End();
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        _modes.Session?.SaveNow();
        ReportAudio();
        base.OnExiting(sender, args);
    }

    /// <summary>
    /// Prints what the session's sound actually cost, so the latency figure in
    /// ARCHITECTURE §2 is something anyone can reproduce by playing for a minute and
    /// quitting, rather than a number someone once wrote down. Depth at submit is the
    /// measurement; the wait it implies is that depth in 16.667 ms blocks, minus however much
    /// of the head block has already played — which no MonoGame API reports, so it is given
    /// as a range and not as a false decimal.
    /// </summary>
    private void ReportAudio()
    {
        if (_audio is null || !_audio.IsAvailable || _audio.MeanDepthAtSubmit < 0)
        {
            return;
        }
        double depth = _audio.MeanDepthAtSubmit;
        Console.WriteLine(
            $"[quarp] audio: queue {depth:F2} blocks at submit "
            + $"({(depth - 1) * AudioQueue.BlockMilliseconds:F1}-{depth * AudioQueue.BlockMilliseconds:F1} ms "
            + $"before the device starts it, plus the driver's own buffer), "
            + $"{_audio.Submitted} blocks submitted, {_audio.Dropped} dropped, "
            + $"{_audio.Padded} padded with silence.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _modes.Session?.Dispose();
            _overlay?.Dispose();
            _presenter?.Dispose();
            _audio?.Dispose();
        }
        base.Dispose(disposing);
    }
}
