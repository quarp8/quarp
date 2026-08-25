using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The one road from an indexed <see cref="Framebuffer"/> to the window: unpack through
/// <see cref="Palette.Master32"/>, upload to one texture, blit at
/// <see cref="FramePlacement"/>'s whole-integer scale, centred, point-sampled.
///
/// <para><b>Why it is a type now.</b> This code was <c>QuarpGame.RenderFrame</c>'s middle
/// section and served exactly one picture — the running cartridge's. Wave R1 gave the shell a
/// framebuffer of its own (<see cref="ShellScreen"/>), and the question immediately became
/// whether tool screens get their own presentation path. They do not, and this file is the
/// reason: there is one texture, one palette unpack, one scale rule, one destination
/// rectangle, and both framebuffers go through it. A second path would mean the library could
/// look non-native — a different scale, a different filter, a different palette unpack — while
/// every commit message claimed it was the same console.</para>
///
/// <para><b>One texture for both pictures.</b> The cartridge's console and the shell's are
/// built from the same <see cref="ConsoleProfile"/>, so their framebuffers are the same size
/// and one texture serves both; a framebuffer of another size is refused rather than silently
/// stretched. The pixels of the two never mix: whichever framebuffer is passed in is uploaded
/// whole, and the mode switch that changes which one is passed also redraws it.</para>
///
/// <para>Nothing here can write to a framebuffer. It reads <c>Pixels</c> and produces window
/// pixels, which is what keeps the golden master (the cartridge's frame) out of reach of
/// anything the shell decides to show.</para>
/// </summary>
public sealed class ConsolePresenter : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Texture2D _texture;
    private readonly Color[] _colorBuffer;
    private readonly Color[] _palette;
    private readonly int _width;
    private readonly int _height;

    public ConsolePresenter(GraphicsDevice device, ConsoleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(profile);
        _device = device;
        _width = profile.Width;
        _height = profile.Height;
        _texture = new Texture2D(device, _width, _height);
        _colorBuffer = new Color[_width * _height];
        _palette = new Color[Palette.MasterCount];
        for (int i = 0; i < Palette.MasterCount; i++)
        {
            _palette[i] = PaletteColors.Opaque(i);
        }
    }

    /// <summary>
    /// Paints the letterbox — everything outside the picture — black. Separate from
    /// <see cref="Draw"/> because it must happen before <c>SpriteBatch.Begin</c>, and the
    /// caller owns the batch so that a second layer (the pause indicator) can share it.
    /// </summary>
    public void ClearLetterbox() => _device.Clear(Color.Black);

    /// <summary>
    /// Uploads the framebuffer and blits it, inside an already-begun batch (the shell begins
    /// it with <see cref="SamplerState.PointClamp"/>). Returns the rectangle it landed in, so
    /// a caller that has something to draw over the same picture — <see cref="ShellOverlay"/>
    /// — lines its pixels up with console pixels instead of guessing the placement again.
    /// </summary>
    public Rectangle Draw(SpriteBatch batch, Framebuffer framebuffer, int windowWidth, int windowHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(framebuffer);
        if (framebuffer.Width != _width || framebuffer.Height != _height)
        {
            throw new ArgumentException(
                $"framebuffer is {framebuffer.Width}x{framebuffer.Height}, presenter is {_width}x{_height}",
                nameof(framebuffer));
        }

        byte[] pixels = framebuffer.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            _colorBuffer[i] = _palette[pixels[i]];
        }
        _texture.SetData(_colorBuffer);

        Rectangle dest = FramePlacement.Compute(windowWidth, windowHeight, _width, _height).Destination;
        batch.Draw(_texture, dest, Color.White);
        return dest;
    }

    public void Dispose() => _texture.Dispose();
}
