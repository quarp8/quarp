using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quarp.CartKit;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Desktop shell: presents the core's indexed framebuffer as one point-sampled texture
/// at the largest integer scale that fits the window (ARCHITECTURE §5).
/// Two modes (M1 work order): without a cart path it shows the palette test pattern;
/// with one it runs the cartridge with hot reload and save.dat persistence.
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
    private readonly Framebuffer? _patternFramebuffer;
    private readonly CartSession? _session;
    private readonly Color[] _colorBuffer;
    private readonly Color[] _palette;
    private readonly TickAccumulator _accumulator = new();
    private readonly ShellCommandReader _commands = new();
    private readonly ConsoleProfile _profile;

    private SpriteBatch _spriteBatch = null!;
    private Texture2D _screenTexture = null!;
    private ShellOverlay _overlay = null!;
    private AudioOutput? _audio;

    private TimeSpeed _lastSpeed = TimeSpeed.At(TimeSpeed.NormalIndex);
    private bool _lastPaused;

    /// <summary>Pattern mode when <paramref name="cartPath"/> is null; cart mode otherwise.</summary>
    public QuarpGame(string? cartPath = null)
    {
        StartCompilerWarmUp();

        _profile = ConsoleProfile.Profile8;
        if (cartPath is null)
        {
            _patternFramebuffer = new Framebuffer(_profile);
            TestPattern.Render(_patternFramebuffer);
        }
        else
        {
            _session = CartSession.Start(cartPath);
            // Lets a long resimulation repaint the window from inside its progress callback
            // instead of freezing it (ARCHITECTURE §4). Cached once — it is called in a loop.
            _session.PresentFrame = PresentCurrentFrame;
        }

        _colorBuffer = new Color[_profile.Width * _profile.Height];
        _palette = new Color[Palette.MasterCount];
        for (int i = 0; i < Palette.MasterCount; i++)
        {
            uint rgb = Palette.Master32[i];
            _palette[i] = new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        var graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = _profile.Width * 10,   // 1280x720 — pixel-perfect x10
            PreferredBackBufferHeight = _profile.Height * 10,
            SynchronizeWithVerticalRetrace = true,            // for the picture, not for the clock
        };
        graphics.ApplyChanges();

        // The strict accumulator owns time from here (M2 work order). MonoGame's own fixed
        // step chases an unbounded backlog, which on slow hardware is a death spiral: this
        // way an overloaded machine runs slowly instead of locking up.
        IsFixedTimeStep = false;

        Window.Title = _session is null ? _profile.Name : $"{_profile.Name} — {_session.Name}";
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
    }

    private Framebuffer CurrentFramebuffer => _session?.Framebuffer ?? _patternFramebuffer!;

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

        // Opened here rather than in the constructor: the audio device belongs to a running
        // Game, and an unavailable one is reported by AudioOutput rather than thrown.
        _audio = new AudioOutput();
        if (_session is not null)
        {
            // Cached once — this delegate is invoked on every tick, including eight times a
            // frame at x8.
            _session.AudioSink = _audio.Submit;
        }
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState keyboard = Keyboard.GetState();
        ShellCommands commands = _commands.Read(keyboard);
        if (commands.Quit)
        {
            Exit();
            return;
        }

        if (_session is null)
        {
            base.Update(gameTime);
            return;
        }

        _session.ApplyCommands(commands);

        // A speed change or a pause invalidates the banked remainder: it was measured in the
        // old rung's units, and carrying it across would spit out a burst of ticks nobody
        // asked for on the frame the player pressed the key.
        TimeSpeed speed = _session.Speed;
        bool paused = _session.IsPaused;
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

        _session.Update(ticks, InputMapper.Read(keyboard), rewinding);

        // Once a frame, whatever the simulation did: tops the device queue up with silence so
        // a pause, a rewind or a stalled machine is quiet instead of a source running dry.
        _audio?.EndFrame();
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        RenderFrame();
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
        if (_spriteBatch is null || _screenTexture is null)
        {
            return;     // Called before LoadContent (a crash during the very first reload).
        }
        _overlay.Show(_session?.Status, _session?.StatusPercent ?? -1);

        Framebuffer framebuffer = CurrentFramebuffer;
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
        _session?.SaveNow();
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
            _session?.Dispose();
            _overlay?.Dispose();
            _audio?.Dispose();
        }
        base.Dispose(disposing);
    }
}
