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
/// with one it runs the cartridge at a fixed 60 Hz step with hot reload and save.dat
/// persistence (the strict accumulator arrives in M2 — MonoGame's fixed step is enough
/// for M1). Escape closes the window in both modes.
/// </summary>
public sealed class QuarpGame : Game
{
    private readonly Framebuffer? _patternFramebuffer;
    private readonly CartSession? _session;
    private readonly Color[] _colorBuffer;
    private readonly Color[] _palette;

    private SpriteBatch _spriteBatch = null!;
    private Texture2D _screenTexture = null!;

    /// <summary>Pattern mode when <paramref name="cartPath"/> is null; cart mode otherwise.</summary>
    public QuarpGame(string? cartPath = null)
    {
        StartCompilerWarmUp();

        var profile = ConsoleProfile.Profile8;
        if (cartPath is null)
        {
            _patternFramebuffer = new Framebuffer(profile);
            TestPattern.Render(_patternFramebuffer);
        }
        else
        {
            _session = CartSession.Start(cartPath);
        }

        _colorBuffer = new Color[profile.Width * profile.Height];
        _palette = new Color[Palette.MasterCount];
        for (int i = 0; i < Palette.MasterCount; i++)
        {
            uint rgb = Palette.Master32[i];
            _palette[i] = new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        var graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = profile.Width * 10,   // 1280x720 — pixel-perfect x10
            PreferredBackBufferHeight = profile.Height * 10,
        };
        graphics.ApplyChanges();

        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60); // 60 ticks per game second

        Window.Title = _session is null ? profile.Name : $"{profile.Name} — {_session.Name}";
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
        Framebuffer framebuffer = CurrentFramebuffer;
        _screenTexture = new Texture2D(GraphicsDevice, framebuffer.Width, framebuffer.Height);
    }

    protected override void Update(GameTime gameTime)
    {
        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
        {
            Exit();
        }
        _session?.Update(InputMapper.Read());
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
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
        _spriteBatch.End();
        base.Draw(gameTime);
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        _session?.SaveNow();
        base.OnExiting(sender, args);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _session?.Dispose();
        }
        base.Dispose(disposing);
    }
}
