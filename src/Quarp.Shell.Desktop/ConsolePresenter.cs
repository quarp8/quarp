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
/// <para><b>The display stage is applied here, and only here.</b> A frame reaching the window is
/// a pair: the index buffer, and the output state it is shown through
/// (<see cref="DisplayPalette"/> — four 32-to-32 sets and a row selector). This is the single
/// place that pair is resolved into RGB, for the same reason there is a single texture and a
/// single scale rule: a second resolver could show the cartridge's frame in colours the shell's
/// frame would never get, while both commits claimed it was one console. Cost is paid once per
/// frame (128 lookups compose the sets into <c>_table</c>) and once per row (the row's 32-entry
/// base); the per-pixel loop is byte-for-byte the indexed load it was before the stage
/// existed.</para>
///
/// <para>Nothing here can write to a framebuffer, and nothing here can write to the display
/// state either. It reads <c>Pixels</c> and a read-only view of that state and produces window
/// pixels, which is what keeps the golden master (the cartridge's frame) out of reach of
/// anything the shell decides to show.</para>
/// </summary>
public sealed class ConsolePresenter : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Texture2D _texture;
    private readonly Color[] _colorBuffer;
    private readonly Color[] _palette;

    // The display stage composed into RGB: the four sets, set-major, 32 colours each, in the same
    // order DisplayPalette.SetOffset indexes. Rebuilt at most once per frame (see EnsureTable).
    private readonly Color[] _table;

    private DisplayPalette? _tableSource;
    private int _tableRevision;
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
        _table = new Color[DisplayPalette.SetCount * Palette.MasterCount];
        ComposeIdentity();
        _tableSource = null;
        _tableRevision = 0;
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
    ///
    /// <para><paramref name="display"/> is the output state that framebuffer is shown through;
    /// its owner is the console the framebuffer came from. A <c>null</c> means the identity
    /// stage — the picture as the index buffer has it — and is exactly what every caller got
    /// before this parameter existed.</para>
    /// </summary>
    public Rectangle Draw(
        SpriteBatch batch,
        Framebuffer framebuffer,
        DisplayPalette? display,
        int windowWidth,
        int windowHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(framebuffer);
        if (framebuffer.Width != _width || framebuffer.Height != _height)
        {
            throw new ArgumentException(
                $"framebuffer is {framebuffer.Width}x{framebuffer.Height}, presenter is {_width}x{_height}",
                nameof(framebuffer));
        }
        if (display is not null && display.Height != _height)
        {
            throw new ArgumentException(
                $"display state is {display.Height} rows, presenter is {_height}",
                nameof(display));
        }

        EnsureTable(display);
        ReadOnlySpan<byte> pixels = framebuffer.Pixels;
        Span<Color> destination = _colorBuffer;
        for (int y = 0; y < _height; y++)
        {
            // One indirection per ROW: the row's 32-entry table is taken here, and the inner loop
            // stays the single indexed load it was before the display stage existed.
            ReadOnlySpan<Color> table = _table.AsSpan(
                display is null ? 0 : display.SetOffset(y),
                Palette.MasterCount);
            int start = y * _width;
            ReadOnlySpan<byte> source = pixels.Slice(start, _width);
            Span<Color> line = destination.Slice(start, _width);
            for (int x = 0; x < _width; x++)
            {
                line[x] = table[source[x]];
            }
        }
        _texture.SetData(_colorBuffer);

        Rectangle dest = FramePlacement.Compute(windowWidth, windowHeight, _width, _height).Destination;
        batch.Draw(_texture, dest, Color.White);
        return dest;
    }

    /// <summary>
    /// Composes the display sets into RGB — 128 lookups — at most once per frame, and only when
    /// the state actually changed. The cache key is the state's identity <em>and</em> its
    /// revision: the presenter serves two consoles (the cartridge's and the shell's), so
    /// "unchanged since last time" has to mean "same object, same revision", not one of the two.
    /// </summary>
    private void EnsureTable(DisplayPalette? display)
    {
        if (ReferenceEquals(display, _tableSource) && (display is null || display.Revision == _tableRevision))
        {
            return;
        }
        if (display is null)
        {
            ComposeIdentity();
        }
        else
        {
            ReadOnlySpan<byte> sets = display.Sets;
            for (int i = 0; i < _table.Length; i++)
            {
                _table[i] = _palette[sets[i]];
            }
        }
        _tableSource = display;
        _tableRevision = display?.Revision ?? 0;
    }

    private void ComposeIdentity()
    {
        for (int k = 0; k < DisplayPalette.SetCount; k++)
        {
            int baseIndex = k * Palette.MasterCount;
            for (int i = 0; i < Palette.MasterCount; i++)
            {
                _table[baseIndex + i] = _palette[i];
            }
        }
    }

    public void Dispose() => _texture.Dispose();
}
