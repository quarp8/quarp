using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quarp.CartKit;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Desktop shell: one window, three modes (M9, ADR-026) — the game library, a running
/// cartridge, and the sprite editor. Without a cart path it opens on the library (the console's
/// face; the old windowed test pattern died with M9 — the palette is proven by
/// <c>quarp pattern</c> and by the library itself, which is drawn on Master32); with a path it
/// runs that cartridge directly, hot reload and all, and Esc quits the process — the
/// developer's F5 loop, which the library never interrupts. Mode policy lives in
/// <see cref="ShellModeMachine"/>, editor policy in <see cref="SpriteEditorSession"/>; this
/// class owns only what needs a graphics device, plus the routing of raw input to whichever
/// mode is on screen. The mouse (new in M9 stage 2) is polled every frame but <b>acted on
/// only in the editor</b> — the library stays keyboard-driven by decision; polling always
/// keeps the reader's previous-state true across mode switches, so a button held into the
/// editor produces no phantom press.
///
/// <para><b>Two resolutions, on purpose.</b> A running cart is presented as the core's
/// indexed framebuffer scaled by whole integers (ARCHITECTURE §5). The library and the stub
/// are host UI and draw at the window's native resolution via <see cref="LibraryRenderer"/> —
/// still on the master palette and the system font, so the console keeps its face.</para>
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
    private readonly Color[] _colorBuffer;
    private readonly Color[] _palette;
    private readonly TickAccumulator _accumulator = new();
    private readonly ShellCommandReader _commands = new();
    private readonly EditorMouseReader _mouse = new();
    private readonly IconHoverTracker _hover = new();
    private readonly ConsoleProfile _profile;
    private readonly ShellModeMachine _modes;

    private SpriteBatch _spriteBatch = null!;
    private Texture2D _screenTexture = null!;
    private ShellOverlay _overlay = null!;
    private LibraryRenderer _hostUi = null!;
    private SpriteEditorRenderer _editorUi = null!;
    private AudioOutput? _audio;

    private TimeSpeed _lastSpeed = TimeSpeed.At(TimeSpeed.NormalIndex);
    private bool _lastPaused;

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

        _colorBuffer = new Color[_profile.Width * _profile.Height];
        _palette = new Color[Palette.MasterCount];
        for (int i = 0; i < Palette.MasterCount; i++)
        {
            _palette[i] = PaletteColors.Opaque(i);
        }

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
        _screenTexture = new Texture2D(GraphicsDevice, _profile.Width, _profile.Height);
        _overlay = new ShellOverlay(GraphicsDevice, _profile.Width, _profile.Height);
        _hostUi = new LibraryRenderer(GraphicsDevice);
        _editorUi = new SpriteEditorRenderer(GraphicsDevice);

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
                UpdateLibrary(commands);
                break;
            case ShellMode.Editor:
                UpdateEditor(commands, mouse, gameTime.ElapsedGameTime.TotalSeconds);
                break;
        }

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

    /// <summary>One frame of the library: selection, launch, opening the sprite editor, or leaving.</summary>
    private void UpdateLibrary(in ShellCommands commands)
    {
        if (commands.Quit)
        {
            _modes.HandleEscape();      // Library Esc = leave the process; Update picks it up.
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
            // A fresh session starts at normal speed and unpaused; the banked remainder of
            // however long the player browsed must not become a burst of catch-up ticks.
            _accumulator.Reset();
            _lastSpeed = TimeSpeed.At(TimeSpeed.NormalIndex);
            _lastPaused = false;
            UpdateWindowTitle();
        }
    }

    /// <summary>
    /// One frame of the sprite editor: routes keys and mouse hits into the session, whose
    /// policy the headless tests own. Input parity is the law of this frame (M9 stage 2.5) —
    /// every live action has a key path and a click path, and both funnel into the same
    /// session method so neither can drift. While the exit prompt is up it owns the input —
    /// Z saves and leaves, X discards and leaves, Esc stays, and the same three verbs are
    /// clickable on the prompt line — and everything else (including the pencil) is
    /// deliberately deaf, so a stray click cannot change the sheet mid-decision.
    /// </summary>
    private void UpdateEditor(in ShellCommands commands, in EditorMouse mouse, double elapsedSeconds)
    {
        SpriteEditorSession editor = _modes.Editor!;
        // The same layout the renderer will draw this frame — geometry has one owner.
        var layout = SpriteEditorLayout.Compute(
            GraphicsDevice.PresentationParameters.BackBufferWidth,
            GraphicsDevice.PresentationParameters.BackBufferHeight,
            editor.RegionCells);

        if (editor.ExitPromptShown)
        {
            _hover.Update(null, elapsedSeconds);    // dead buttons must not grow tooltips
            if (commands.Quit)
            {
                _modes.HandleEscape();          // Esc lowers the prompt: "stay" — see SpriteEditorSession.RequestClose
            }
            else if (commands.MenuConfirm)
            {
                _modes.SaveEditorAndClose();
            }
            else if (commands.MenuEditor)
            {
                _modes.DiscardEditorAndClose();
            }
            else if (mouse.LeftPressed && layout.TryPromptVerb(mouse.X, mouse.Y, out EditorPromptVerb verb))
            {
                switch (verb)
                {
                    case EditorPromptVerb.SaveAndExit:
                        _modes.SaveEditorAndClose();
                        break;
                    case EditorPromptVerb.Discard:
                        _modes.DiscardEditorAndClose();
                        break;
                    default:
                        _modes.HandleEscape();  // Stay — lowers the prompt, exactly like Esc
                        break;
                }
            }
            return;
        }
        if (commands.Quit)
        {
            _modes.HandleEscape();              // clean → library; dirty → the prompt above
            return;
        }
        if (commands.EditorUndo)
        {
            editor.Undo();
        }
        if (commands.EditorRedo)
        {
            editor.Redo();
        }
        if (commands.EditorSave)
        {
            editor.Save();                       // failure lands in SaveError; the prompt line shows it
        }
        if (commands.EditorToolToggle)
        {
            editor.ToggleTool();
        }
        if (EditorIcons.ToolForDigit(commands.EditorToolDigit) is SpriteEditorTool digitTool)
        {
            SetTool(editor, digitTool);          // stub digits return null and switch nothing
        }
        if (commands.EditorRegionCycle)
        {
            editor.CycleRegionSize();
            // The canvas must resize this same frame, so the mouse hits below test against
            // the geometry the renderer is about to draw.
            layout = SpriteEditorLayout.Compute(
                GraphicsDevice.PresentationParameters.BackBufferWidth,
                GraphicsDevice.PresentationParameters.BackBufferHeight,
                editor.RegionCells);
        }
        if (commands.EditorFlipH)
        {
            editor.FlipHorizontal();
        }
        if (commands.EditorFlipV)
        {
            editor.FlipVertical();
        }
        if (commands.EditorRotate)
        {
            editor.RotateClockwise();
        }
        if (commands.EditorClear)
        {
            editor.ClearRegion();
        }
        if (commands.EditorColorPrev)
        {
            editor.SelectColor((editor.CurrentColor + Palette.VisibleCount - 1) % Palette.VisibleCount);
        }
        if (commands.EditorColorNext)
        {
            editor.SelectColor((editor.CurrentColor + 1) % Palette.VisibleCount);
        }

        // Keyboard drawing: arrows steer the canvas cursor, Z/Space is the pencil's button,
        // X the eyedropper — the whole mouse vocabulary without a mouse. The session clamps
        // the cursor, so painting at it is in-range by construction.
        int dx = (commands.MenuRight ? 1 : 0) - (commands.MenuLeft ? 1 : 0);
        int dy = (commands.MenuDown ? 1 : 0) - (commands.MenuUp ? 1 : 0);
        if (dx != 0 || dy != 0)
        {
            editor.MoveCursor(dx, dy);
            if (editor.StrokeActive && commands.EditorPaintDown)
            {
                editor.Paint(editor.CursorX, editor.CursorY);   // held pencil + arrows = a dragged stroke
            }
        }
        if (commands.EditorPaintPressed)
        {
            if (editor.Tool == SpriteEditorTool.Fill)
            {
                editor.Fill(editor.CursorX, editor.CursorY);
            }
            else
            {
                editor.BeginStroke();
                editor.Paint(editor.CursorX, editor.CursorY);
            }
        }
        if (commands.EditorPaintReleased)
        {
            editor.EndStroke();                  // the keyboard stroke commits as one undo step, like the mouse's
        }
        if (commands.MenuEditor)
        {
            editor.PickColor(editor.CursorX, editor.CursorY);
        }

        // Hover: buttons first (they overlap nothing), then swatches — the only two targets
        // with tooltips. The tracker shows the frame highlight immediately and holds the
        // label back for its three seconds.
        HoverTarget? hover = null;
        if (layout.TryButton(mouse.X, mouse.Y, out EditorButton hoveredButton))
        {
            hover = HoverTarget.OfButton(hoveredButton);
        }
        else if (layout.TrySwatch(mouse.X, mouse.Y, out int hoveredSwatch))
        {
            hover = HoverTarget.OfSwatch(hoveredSwatch);
        }
        _hover.Update(hover, elapsedSeconds);

        // The one cursor: mouse hover parks it where the mouse is, so the status bar's
        // coordinates read the pointer and a following keyboard stroke starts there.
        if (layout.TryCanvasPixel(mouse.X, mouse.Y, out int overX, out int overY))
        {
            editor.SetCursor(overX, overY);
        }

        if (mouse.LeftPressed)
        {
            if (layout.TryButton(mouse.X, mouse.Y, out EditorButton pressed))
            {
                if (!EditorIcons.IsStub(pressed) && HandleEditorButton(editor, pressed))
                {
                    return;                      // the exit tab may have left the mode
                }
            }
            else if (layout.TrySwatch(mouse.X, mouse.Y, out int color))
            {
                editor.SelectColor(color);
            }
            else if (layout.TrySheetCell(mouse.X, mouse.Y, out int cellX, out int cellY))
            {
                editor.SelectRegionCell(cellX, cellY);
            }
            else if (layout.TryCanvasPixel(mouse.X, mouse.Y, out int pressX, out int pressY))
            {
                if (editor.Tool == SpriteEditorTool.Fill)
                {
                    // The bucket is a click, not a gesture: no stroke opens, so the drag
                    // branch below stays naturally dead until the tool is the pencil again.
                    editor.Fill(pressX, pressY);
                }
                else
                {
                    editor.BeginStroke();
                    editor.Paint(pressX, pressY);
                }
            }
        }
        else if (mouse.LeftDown && editor.StrokeActive)
        {
            // Drags are clamped to the canvas: a stroke that wanders off the edge keeps
            // painting along it instead of tearing, and the clamp is what upholds Paint's
            // in-range contract. The cursor follows so the readout stays truthful mid-drag.
            layout.ClampCanvasPixel(mouse.X, mouse.Y, out int dragX, out int dragY);
            editor.SetCursor(dragX, dragY);
            editor.Paint(dragX, dragY);
        }
        if (mouse.LeftReleased)
        {
            editor.EndStroke();                  // one stroke commits as one undo step
        }
        if (mouse.RightPressed && layout.TryCanvasPixel(mouse.X, mouse.Y, out int pickX, out int pickY))
        {
            editor.PickColor(pickX, pickY);
        }
    }

    /// <summary>
    /// A click on a live icon-button, routed to the same session calls the keys use — parity
    /// by construction. Returns true when the button may have changed the shell mode (the
    /// exit tab), telling the caller to stop touching the editor this frame.
    /// </summary>
    private bool HandleEditorButton(SpriteEditorSession editor, EditorButton button)
    {
        switch (button)
        {
            case EditorButton.ExitTab:
                _modes.HandleEscape();           // clean → library; dirty → the prompt, same as Esc
                return true;
            case EditorButton.ToolPencil:
                SetTool(editor, SpriteEditorTool.Pencil);
                return false;
            case EditorButton.ToolFill:
                SetTool(editor, SpriteEditorTool.Fill);
                return false;
            case EditorButton.FlipH:
                editor.FlipHorizontal();
                return false;
            case EditorButton.FlipV:
                editor.FlipVertical();
                return false;
            case EditorButton.Rotate:
                editor.RotateClockwise();
                return false;
            case EditorButton.Clear:
                editor.ClearRegion();
                return false;
            case EditorButton.Save:
                editor.Save();                   // the modified/saved icon IS this button — click = Ctrl+S
                return false;
            case EditorButton.Undo:
                editor.Undo();
                return false;
            case EditorButton.Redo:
                editor.Redo();
                return false;
            default:
                return false;                    // SpritesTab: already the mode on screen
        }
    }

    /// <summary>
    /// Direct tool selection on top of the session's toggle. <see cref="SpriteEditorSession.ToggleTool"/>
    /// stays the one door (its tests pin the commit-open-stroke discipline); with exactly two
    /// live tools, "set" is "toggle unless already there". Wave 2e's new tools will need a real
    /// select in the session — that wave owns the change.
    /// </summary>
    private static void SetTool(SpriteEditorSession editor, SpriteEditorTool tool)
    {
        if (editor.Tool != tool)
        {
            editor.ToggleTool();
        }
    }

    protected override void Draw(GameTime gameTime)
    {
        switch (_modes.Mode)
        {
            case ShellMode.Game:
                RenderFrame();
                break;
            case ShellMode.Library:
                _hostUi.DrawLibrary(
                    _spriteBatch,
                    GraphicsDevice.PresentationParameters.BackBufferWidth,
                    GraphicsDevice.PresentationParameters.BackBufferHeight,
                    _modes.Library,
                    _modes.LibraryMessage);
                break;
            case ShellMode.Editor:
                _editorUi.Draw(
                    _spriteBatch,
                    GraphicsDevice.PresentationParameters.BackBufferWidth,
                    GraphicsDevice.PresentationParameters.BackBufferHeight,
                    _modes.Editor!,
                    _hover.Target,
                    _hover.TooltipVisible);
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
        if (_modes.Session is not CartSession session || _spriteBatch is null || _screenTexture is null)
        {
            return;     // No cart on screen, or called before LoadContent (a crash during the very first reload).
        }
        _overlay.Show(session.Status, session.StatusPercent);

        Framebuffer framebuffer = session.Framebuffer;
        byte[] pixels = framebuffer.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            _colorBuffer[i] = _palette[pixels[i]];
        }
        _screenTexture.SetData(_colorBuffer);

        int windowWidth = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int windowHeight = GraphicsDevice.PresentationParameters.BackBufferHeight;
        int scale = Math.Max(1, Math.Min(windowWidth / framebuffer.Width, windowHeight / framebuffer.Height));
        int destWidth = framebuffer.Width * scale;
        int destHeight = framebuffer.Height * scale;
        var dest = new Rectangle((windowWidth - destWidth) / 2, (windowHeight - destHeight) / 2, destWidth, destHeight);

        GraphicsDevice.Clear(Color.Black);
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        _spriteBatch.Draw(_screenTexture, dest, Color.White);
        // The overlay goes over the same rectangle, so its pixels line up with console
        // pixels — and it is a texture of its own, so the framebuffer stays untouched.
        _overlay.Draw(_spriteBatch, dest);
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
            _hostUi?.Dispose();
            _editorUi?.Dispose();
            _audio?.Dispose();
        }
        base.Dispose(disposing);
    }
}
