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
/// it, and since wave R1 so does the library, which now has a console grid to point at.
/// Polling always keeps the reader's previous-state true across mode switches, so a button held
/// into another screen produces no phantom press.
///
/// <para><b>One resolution, since wave R1.</b> The owner's law of 2026-08-25 — "the console
/// is the same for everyone and in everything" — ended the second machine this class used to
/// run. A running cart is presented as the core's indexed framebuffer scaled by whole integers
/// (ARCHITECTURE §5); the library is now drawn <em>into a framebuffer of its own</em>
/// (<see cref="ShellScreen"/>) with the same core calls a cartridge uses, and both go to the
/// window through the one <see cref="ConsolePresenter"/>. Waves R2 and R3 brought the sprite
/// and map editors onto the same road; the two remaining editor screens (code, sound) and the
/// boot menu still paint at the window's native resolution and are scheduled to follow, and
/// while they do, this class is the one place where both roads are visible.</para>
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
    private CodeEditorRenderer _codeUi = null!;
    private SfxEditorRenderer _sfxUi = null!;
    private MainMenuRenderer _menuUi = null!;
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
            directSession);

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
        _codeUi = new CodeEditorRenderer(GraphicsDevice);
        _sfxUi = new SfxEditorRenderer(GraphicsDevice);
        _menuUi = new MainMenuRenderer(GraphicsDevice);

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
        // Read every frame in every mode (see the type comment), consumed by the editor only.
        EditorMouse mouse = _mouse.Read(Mouse.GetState());

        switch (_modes.Mode)
        {
            case ShellMode.Game:
                UpdateGame(commands, keyboard, gameTime);
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
                // The one screen that also needs the CHARACTER stream, not just the key frame:
                // typing wants the author's keyboard layout, dead keys and auto-repeat, which
                // only Window.TextInput knows. The buffer is handed over as a plain list and
                // cleared below with the frame, so the router sees exactly what arrived since
                // the previous frame — in order, once. See CodeEditorInput's type comment.
                CodeEditorInput.Update(
                    EditorContext(), commands, mouse, _typedChars, gameTime.ElapsedGameTime.TotalSeconds);
                break;
            case ShellMode.SfxEditor:
                SfxEditorInput.Update(EditorContext(), commands, mouse, gameTime.ElapsedGameTime.TotalSeconds);
                // The one editor frame with a second half: the router decided whether the slot
                // should sound, and this drives the chip that makes it so. Kept out of the
                // router on purpose — a router that owned a speaker could not run in a headless
                // test, which is the property wave 3c bought and this wave keeps.
                UpdateSfxPreview(gameTime);
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
        _typedChars.Clear();
        _droppedFile = null;

        if (_modes.ExitRequested)
        {
            Exit();
            return;
        }
        base.Update(gameTime);
    }

    /// <summary>One frame of a running cart: time control, ticks, audio — unchanged from M2-M4 except for where Esc goes.</summary>
    private void UpdateGame(in ShellCommands commands, KeyboardState keyboard, GameTime gameTime)
    {
        if (commands.Quit)
        {
            // Direct launch: exit request, picked up by Update. Library launch: the machine
            // drains the speaker, disposes the session (save.dat's forced flush lives in that
            // Dispose) and lands back on a rescanned library — see ShellModeMachine.
            _modes.HandleEscape();
            if (!_modes.ExitRequested)
            {
                UpdateWindowTitle();
                _accumulator.Reset();
            }
            return;
        }

        CartSession session = _modes.Session!;
        session.ApplyCommands(commands);

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

        session.Update(ticks, InputMapper.Read(keyboard), rewinding);

        // Once a frame, whatever the simulation did: tops the device queue up with silence so
        // a pause, a rewind or a stalled machine is quiet instead of a source running dry.
        // Only in game mode — the library's silence comes from Drain having stopped the
        // source, and topping it up would just count Padded blocks nobody can hear.
        _audio?.EndFrame();
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
    /// The whole of what the editor input routers (wave 3c) are allowed to see of this window:
    /// the shell state they steer, plus the back buffer as two numbers. Built per call because
    /// a resize changes the size mid-session — and built here, in the one class that owns a
    /// graphics device, so that <see cref="SpriteEditorInput"/> and <see cref="MapEditorInput"/>
    /// can name no MonoGame type at all and therefore run in a headless test.
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
    /// The same context for the one screen that is drawn on the console (wave R2): the surface
    /// it measures itself against is the shell's own framebuffer, not the back buffer, so the
    /// two numbers it receives are 160 and 90. The router cannot tell the difference and must
    /// not — it lays out and hit-tests in the surface it is given, which is exactly why the same
    /// type serves both the console screen and the three that have not moved yet.
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
                // Three screens on one road since wave R3: all are drawn into the shell's own
                // console and presented by the same presenter the cartridge's frame goes
                // through. The draw clock feeds the sprite screen's marching ants — chrome
                // animating in host time, like the tooltip delay; no simulation or hash sees it.
                RenderShellScreen(gameTime.TotalGameTime.TotalSeconds);
                break;
            case ShellMode.CodeEditor:
                // Same draw clock the sprite editor's marching ants ride: host chrome animating
                // in host time (here, the caret's blink); no simulation or hash can see it.
                _codeUi.Draw(
                    _spriteBatch,
                    GraphicsDevice.PresentationParameters.BackBufferWidth,
                    GraphicsDevice.PresentationParameters.BackBufferHeight,
                    _modes.CodeEditor!,
                    _modes.CodeView!,
                    _hover.Target,
                    _hover.TooltipVisible,
                    gameTime.TotalGameTime.TotalSeconds);
                break;
            case ShellMode.SfxEditor:
                _sfxUi.Draw(
                    _spriteBatch,
                    GraphicsDevice.PresentationParameters.BackBufferWidth,
                    GraphicsDevice.PresentationParameters.BackBufferHeight,
                    _modes.SfxEditor!,
                    _modes.SfxView!,
                    _hover.Target,
                    _hover.TooltipVisible);
                break;
            case ShellMode.Menu:
                _menuUi.Draw(
                    _spriteBatch,
                    GraphicsDevice.PresentationParameters.BackBufferWidth,
                    GraphicsDevice.PresentationParameters.BackBufferHeight,
                    _modes.Menu);
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
        if (_modes.Session is not CartSession session || _spriteBatch is null || _presenter is null)
        {
            return;     // No cart on screen, or called before LoadContent (a crash during the very first reload).
        }
        _overlay.Show(session.Status, session.StatusPercent);

        _presenter.ClearLetterbox();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        Rectangle dest = _presenter.Draw(
            _spriteBatch,
            session.Framebuffer,
            GraphicsDevice.PresentationParameters.BackBufferWidth,
            GraphicsDevice.PresentationParameters.BackBufferHeight);
        // The overlay goes over the same rectangle, so its pixels line up with console
        // pixels — and it is a texture of its own, so the framebuffer stays untouched.
        _overlay.Draw(_spriteBatch, dest);
        _spriteBatch.End();
    }

    /// <summary>
    /// One frame of a tool screen: draw it into the shell's console, then hand that framebuffer
    /// to the same presenter the cartridge's frame goes through. The whole of what "the editor
    /// runs on the same virtual hardware" means at this layer — the only difference between
    /// this method and <see cref="RenderFrame"/> is which framebuffer is presented and whether
    /// the pause indicator has anything to say.
    ///
    /// <para>Three screens live here as of wave R3: the library (wave R1), the sprite editor
    /// (R2) and the map editor (R3). Which one is drawn is the mode's business and nothing else
    /// changes — same console, same presenter, same whole-integer scale. The two remaining
    /// editors (code and sound) still paint themselves at the window's resolution and are
    /// dispatched separately in <see cref="Draw"/>; they join this method one wave at a time.</para>
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
                _flyout.OpenSlot, _sheetScroll, timeSeconds);
        }
        else if (_modes.Mode == ShellMode.MapEditor)
        {
            MapEditorRenderer.Draw(
                _shellScreen, _modes.MapEditor!, _modes.Editor!, _modes.MapView!,
                _hover.Target, _hover.TooltipVisible);
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
            _codeUi?.Dispose();
            _menuUi?.Dispose();
            _sfxUi?.Dispose();
            _audio?.Dispose();
        }
        base.Dispose(disposing);
    }
}
