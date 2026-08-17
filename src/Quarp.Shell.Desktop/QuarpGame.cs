using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Desktop shell: presents the core's indexed framebuffer as one point-sampled texture
/// at the largest integer scale that fits the window (ARCHITECTURE §5).
/// At M0 it shows the palette test pattern; the cart pipeline arrives in M1.
/// </summary>
public sealed class QuarpGame : Game
{
    private readonly Framebuffer _framebuffer;
    private readonly Color[] _colorBuffer;
    private readonly Color[] _palette;

    private SpriteBatch _spriteBatch = null!;
    private Texture2D _screenTexture = null!;

    public QuarpGame()
    {
        var profile = ConsoleProfile.Profile8;
        _framebuffer = new Framebuffer(profile);
        TestPattern.Render(_framebuffer);

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

        Window.Title = profile.Name;
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _screenTexture = new Texture2D(GraphicsDevice, _framebuffer.Width, _framebuffer.Height);
    }

    protected override void Update(GameTime gameTime)
    {
        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
        {
            Exit();
        }
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        byte[] pixels = _framebuffer.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            _colorBuffer[i] = _palette[pixels[i]];
        }
        _screenTexture.SetData(_colorBuffer);

        int windowWidth = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int windowHeight = GraphicsDevice.PresentationParameters.BackBufferHeight;
        int scale = Math.Max(1, Math.Min(windowWidth / _framebuffer.Width, windowHeight / _framebuffer.Height));
        int destWidth = _framebuffer.Width * scale;
        int destHeight = _framebuffer.Height * scale;
        var dest = new Rectangle((windowWidth - destWidth) / 2, (windowHeight - destHeight) / 2, destWidth, destHeight);

        GraphicsDevice.Clear(Color.Black);
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        _spriteBatch.Draw(_screenTexture, dest, Color.White);
        _spriteBatch.End();
        base.Draw(gameTime);
    }
}
