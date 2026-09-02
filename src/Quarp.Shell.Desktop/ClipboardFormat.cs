using System.Globalization;
using System.Text;
using Quarp.CartKit;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Which of the four banks a piece of clipboard text came out of. The whole reason the format
/// carries a tag at all: without one, a block of map cells and a block of sprite pixels are both
/// "some hex", and pasting the first into the sprite editor would paint plausible-looking
/// garbage instead of saying no (REFERENCES-EDITORS §8 item 2).
/// </summary>
public enum ClipboardKind
{
    /// <summary>A rectangle of sheet pixels, 4bpp — the sprite editor's region or its selection.</summary>
    Sprites,

    /// <summary>A rectangle of map cells, one byte of <c>map.bin</c> each.</summary>
    Map,

    /// <summary>One whole SFX slot: its four header bytes and all 32 step words (AUDIO-FORMAT §2).</summary>
    Sfx,

    /// <summary>A rectangle of the pattern list: patterns by channels, one <c>music.bin</c> channel byte each.</summary>
    Music,
}

/// <summary>
/// One decoded piece of clipboard text: what bank it came from, how big it is, and its bytes.
/// Deliberately dumb — every rule about what the bytes may say lives in
/// <see cref="ClipboardFormat"/> (shape) and in the session that takes it (content), so this type
/// can be passed around without anyone wondering which half of it has been checked.
/// </summary>
public sealed class ClipboardBlock
{
    private readonly byte[] _bytes;

    internal ClipboardBlock(ClipboardKind kind, int width, int height, byte[] bytes)
    {
        Kind = kind;
        Width = width;
        Height = height;
        _bytes = bytes;
    }

    /// <summary>Which bank wrote it.</summary>
    public ClipboardKind Kind { get; }

    /// <summary>
    /// Width in the kind's own unit: sprite <b>pixels</b>, map cells, music channels; always 1
    /// for an SFX slot, which has no rectangle.
    /// </summary>
    public int Width { get; }

    /// <summary>Height in the same unit: sprite pixels, map cells, music patterns; 1 for SFX.</summary>
    public int Height { get; }

    /// <summary>
    /// The payload, one byte per unit — a colour 0-15 for sprites, a tile for the map, a channel
    /// byte for music — except SFX, where it is the slot record's
    /// <see cref="ClipboardFormat.SfxRecordSize"/> bytes.
    /// </summary>
    public ReadOnlySpan<byte> Bytes => _bytes;
}

/// <summary>
/// The one owner of the clipboard's <b>text</b> format — the thing §8 item 2 of
/// REFERENCES-EDITORS asks for and the thing four editors would otherwise each invent a corner
/// of. It encodes a piece of any of the four banks into one short hex line and parses one back.
/// Nothing here reads a session, a screen or a device: it is a pure function on strings and
/// bytes, which is why it sits in layer 1 with the other pure values.
///
/// <para><b>Why hex text and not a binary blob.</b> Straight from TIC-80: <c>toClipboard</c> /
/// <c>fromClipboard</c> put <em>hex strings</em> into the operating system's clipboard
/// (REFERENCES-EDITORS §1), and that is what makes "copy a piece of a level into a forum post"
/// work at all. A block that survives an email, a chat window and a text file is worth more than
/// a block that is two bytes shorter. LIKO-12 takes the same road (§2.2: its sprite copy is
/// "только hex-пиксели") and, notably, can also <em>import PICO-8's</em> <c>[gfx]…[/gfx]</c>,
/// which is only possible because both are text.</para>
///
/// <para><b>What is ours and not theirs: the header.</b> TIC-80's sprite and SFX clipboard is
/// bare hex with no header at all, and it tells one blob from another by <em>length</em>
/// (<c>sameSize=true</c>, §1); its map clipboard prepends two bytes of <c>[w][h]</c> and checks
/// <c>data[0]*data[1] == size-2</c> (§3.1). Length is a weak witness — a 64-pixel sprite and a
/// 32-cell map block are both 64 hex digits — and the failure it produces is silent garbage. So
/// every block here starts with a readable word: <c>quarp0 gfx 8 8 …</c>. That is the one line
/// of this format that exists for the sake of the <em>refusal</em>: a map block pasted into the
/// sprite editor is answered with a sentence, not with pixels.</para>
///
/// <para><b>Whitespace is not data.</b> Any run of whitespace separates the header's four words,
/// and whitespace <em>inside</em> the payload is ignored entirely — TIC-80's
/// <c>remove_white_spaces</c> flag, made unconditional. A mail client that wraps the line at
/// column 76 must not break the paste, and that is exactly what the format is for.</para>
///
/// <para><b>Case is not data either.</b> The tag, the kind and the hex digits all parse in either
/// case, because a chat client that capitalises the first letter of a line is not a corruption.
/// Encoding always writes lower case.</para>
///
/// <para><b>Nothing here throws on bad input.</b> Every parse failure comes back as
/// <c>false</c> and a short reason meant for an editor's message line: clipboard text arrives
/// from outside the process, and an exception on the paste path would reach a renderer and take
/// the console down with it.</para>
/// </summary>
public static class ClipboardFormat
{
    /// <summary>
    /// The first word of every block. Carries the format version in it rather than beside it, so
    /// the next version of the format is a word this parser simply does not recognise — and the
    /// author is told "not a Quarp block" instead of being handed a misread one. The digit is 0,
    /// like every other format number in this prototype (ADR-041).
    /// </summary>
    public const string Tag = "quarp0";

    /// <summary>An SFX slot record: four header bytes plus 32 step words (AUDIO-FORMAT §2), 68 bytes.</summary>
    public const int SfxRecordSize =
        AudioFormat.SfxSlotHeaderSize + (AudioFormat.SfxStepCount * AudioFormat.SfxStepSize);

    /// <summary>
    /// The longest clipboard text this parser will look at. The largest legal block is a whole
    /// map — 256x72 cells at two hex digits each, about 36 KB — so 128 KB leaves room for any
    /// amount of wrapping whitespace and still refuses a clipboard someone filled with a novel
    /// before the parser has allocated anything from it.
    /// </summary>
    public const int MaxTextLength = 128 * 1024;

    /// <summary>Nothing on the clipboard at all. Not a corruption, so it gets its own sentence.</summary>
    public const string EmptyReason = "CLIPBOARD IS EMPTY";

    /// <summary>The text is somebody else's — code, prose, a PNG pasted as base64. Not our tag.</summary>
    public const string ForeignReason = "NOT A QUARP BLOCK";

    /// <summary>Our tag, but the rest does not parse: a non-hex digit, a truncated payload, a bad number.</summary>
    public const string DamagedReason = "BLOCK TEXT IS DAMAGED";

    /// <summary>Our tag on a payload nobody would send: refused before any array is sized from it.</summary>
    public const string TooLongReason = "BLOCK TEXT IS TOO LONG";

    /// <summary>The three-letter word each kind writes into the header — and reads back in a refusal.</summary>
    public static string TagOf(ClipboardKind kind) => kind switch
    {
        ClipboardKind.Sprites => "gfx",
        ClipboardKind.Map => "map",
        ClipboardKind.Sfx => "sfx",
        _ => "mus",
    };

    // ---- encoding ----

    /// <summary>
    /// A rectangle of sheet pixels. <b>One hex digit per pixel</b>, row-major — the 4bpp
    /// spelling TIC-80's <c>tic_tool_buf2str</c> produces and the one LIKO-12 reads out of
    /// PICO-8's <c>[gfx]</c> block (§2.2), so a Quarp sprite line looks like the thing an author
    /// of either of those has seen before. Dimensions are in <em>pixels</em> and not in 8x8
    /// cells, because the sprite editor's existing selection is a pixel mask inside a region and
    /// a cell count could not say what it copied; a whole-region copy is a multiple of 8 on both
    /// axes and therefore is a block of cells, which is the ordinary case.
    /// </summary>
    public static string EncodeSprites(int width, int height, ReadOnlySpan<byte> pixels) =>
        Encode(ClipboardKind.Sprites, width, height, pixels);

    /// <summary>A rectangle of map cells, two hex digits each — TIC-80's <c>copySelectionToClipboard</c> payload with our header in front of it (§3.1).</summary>
    public static string EncodeMap(int width, int height, ReadOnlySpan<byte> tiles) =>
        Encode(ClipboardKind.Map, width, height, tiles);

    /// <summary>
    /// One whole slot — TIC-80's <c>toClipboard(effect, sizeof(tic_sample), true)</c>, which is
    /// "весь сэмпл целиком" (§5.1): the header (speed, length, both loop fields) travels with
    /// the notes, because a sound pasted without its tempo is not the sound that was copied.
    /// </summary>
    public static string EncodeSfx(ReadOnlySpan<byte> record) =>
        Encode(ClipboardKind.Sfx, 1, 1, record);

    /// <summary>
    /// A rectangle of the pattern list. The cells arrive the way
    /// <see cref="IMusicClipboard"/> carries them — a slot 0-63, or -1 for silence — and go out
    /// as the <em>channel byte</em> <c>music.bin</c> itself uses (AUDIO-FORMAT §4): the active
    /// bit set over a six-bit slot, or the plain zero a silent channel is required to store.
    /// Using the on-disk spelling rather than a second one of our own means the canonicity rule
    /// ("a silent channel stores 0x00, and 0x03 is refused") is inherited instead of restated.
    /// </summary>
    public static string EncodeMusic(int patterns, int channels, ReadOnlySpan<int> cells)
    {
        if (patterns < 1 || channels < 1 || cells.Length < patterns * channels)
        {
            return string.Empty;
        }
        var bytes = new byte[patterns * channels];
        for (int i = 0; i < bytes.Length; i++)
        {
            int cell = cells[i];
            bytes[i] = cell < 0
                ? (byte)0
                : (byte)(MusicPatternList.ChannelActiveBit | (cell & MusicPatternList.ChannelSlotMask));
        }
        // Width is channels and height is patterns, so the header reads left-to-right the way the
        // grid does: "mus 4 8" is four channels wide and eight patterns tall.
        return Encode(ClipboardKind.Music, channels, patterns, bytes);
    }

    // ---- decoding ----

    /// <summary>
    /// Parses clipboard text into a block of whatever kind it claims to be. False with a reason
    /// on anything that is not one of ours; never throws.
    /// </summary>
    public static bool TryDecode(string? text, out ClipboardBlock? block, out string reason)
    {
        block = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            reason = EmptyReason;
            return false;
        }
        if (text.Length > MaxTextLength)
        {
            reason = TooLongReason;
            return false;
        }
        string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 5 || !string.Equals(words[0], Tag, StringComparison.OrdinalIgnoreCase))
        {
            // Fewer than five words cannot be a block of ours either, and the honest answer for
            // "someone copied a word" is the same as for "someone copied a paragraph": not ours.
            reason = ForeignReason;
            return false;
        }
        if (!TryKind(words[1], out ClipboardKind kind))
        {
            reason = ForeignReason;
            return false;
        }
        if (!TryNumber(words[2], out int width) || !TryNumber(words[3], out int height))
        {
            reason = DamagedReason;
            return false;
        }
        if (!InRange(kind, width, height))
        {
            reason = $"{TagOf(kind).ToUpperInvariant()} BLOCK {width}x{height} IS OUT OF RANGE";
            return false;
        }
        int units = kind == ClipboardKind.Sfx ? SfxRecordSize : width * height;
        int digits = DigitsPerUnit(kind);
        var bytes = new byte[units];
        int filled = 0;
        int nibble = 0;
        int value = 0;
        for (int word = 4; word < words.Length; word++)
        {
            string chunk = words[word];
            for (int i = 0; i < chunk.Length; i++)
            {
                if (!TryHexDigit(chunk[i], out int digit))
                {
                    reason = DamagedReason;
                    return false;
                }
                if (filled == units)
                {
                    reason = DamagedReason;      // more payload than the header promised
                    return false;
                }
                value = (value << 4) | digit;
                if (++nibble == digits)
                {
                    bytes[filled++] = (byte)value;
                    nibble = 0;
                    value = 0;
                }
            }
        }
        if (filled != units || nibble != 0)
        {
            reason = DamagedReason;              // less payload than the header promised
            return false;
        }
        block = new ClipboardBlock(kind, width, height, bytes);
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// The same parse, with the kind the caller can actually use. A well-formed block of another
    /// bank comes back false with <b>its own name in the sentence</b> — "THAT IS A MAP BLOCK" —
    /// which is the entire point of the header: the author learns what they pasted, not merely
    /// that it did not work.
    /// </summary>
    public static bool TryDecode(string? text, ClipboardKind expected, out ClipboardBlock? block, out string reason)
    {
        if (!TryDecode(text, out block, out reason))
        {
            return false;
        }
        if (block!.Kind != expected)
        {
            reason = $"THAT IS A {TagOf(block.Kind).ToUpperInvariant()} BLOCK";
            block = null;
            return false;
        }
        return true;
    }

    /// <summary>
    /// A music block's cells in the spelling <see cref="IMusicClipboard"/> wants — slot 0-63, or
    /// -1 for silence. Returns false when a byte is not a canonical channel byte: bit 7 set, or
    /// an inactive channel that still remembers a slot. AUDIO-FORMAT §4 refuses both in a file
    /// and there is no reason the clipboard should be the softer door.
    /// </summary>
    public static bool TryMusicCells(ClipboardBlock block, Span<int> destination)
    {
        ArgumentNullException.ThrowIfNull(block);
        ReadOnlySpan<byte> bytes = block.Bytes;
        if (block.Kind != ClipboardKind.Music || destination.Length < bytes.Length)
        {
            return false;
        }
        for (int i = 0; i < bytes.Length; i++)
        {
            byte cell = bytes[i];
            if (cell == 0)
            {
                destination[i] = -1;
                continue;
            }
            if ((cell & ~MusicPatternList.ChannelSlotMask) != MusicPatternList.ChannelActiveBit)
            {
                return false;
            }
            destination[i] = cell & MusicPatternList.ChannelSlotMask;
        }
        return true;
    }

    // ---- the private half ----

    /// <summary>
    /// Sprites spend one hex digit per unit and everyone else spends two, and that is not an
    /// inconsistency: a sprite pixel is four bits wide (16 colours), so a second digit would be
    /// four zero bits per pixel — a sprite block half again as long as it needs to be, in the
    /// one kind where blocks get longest.
    /// </summary>
    private static int DigitsPerUnit(ClipboardKind kind) => kind == ClipboardKind.Sprites ? 1 : 2;

    /// <summary>
    /// What each kind's rectangle may say, taken from the profile rather than from a taste for
    /// round numbers. Checked <b>before</b> the payload array is allocated, so a header claiming
    /// 60000x60000 costs nothing.
    /// </summary>
    private static bool InRange(ClipboardKind kind, int width, int height) => kind switch
    {
        ClipboardKind.Sprites =>
            width >= 1 && width <= CartData.GfxWidth && height >= 1 && height <= CartData.GfxHeight,
        ClipboardKind.Map =>
            width >= 1 && width <= CartData.MapWidth && height >= 1 && height <= CartData.MapHeight,
        // A slot is a slot: it has no rectangle, and the two ones are written only so that every
        // block has the same four-word header and one parser reads them all.
        ClipboardKind.Sfx => width == 1 && height == 1,
        _ => width >= 1 && width <= MusicPatternList.ChannelCount
             && height >= 1 && height <= MusicPatternList.PatternCount,
    };

    private static bool TryKind(string word, out ClipboardKind kind)
    {
        foreach (ClipboardKind candidate in new[]
                 {
                     ClipboardKind.Sprites, ClipboardKind.Map, ClipboardKind.Sfx, ClipboardKind.Music,
                 })
        {
            if (string.Equals(word, TagOf(candidate), StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }
        kind = ClipboardKind.Sprites;
        return false;
    }

    /// <summary>A plain decimal count. Hand-rolled rather than <c>int.TryParse</c> so that "+8", "8," and a culture's digits are all refused the same way.</summary>
    private static bool TryNumber(string word, out int value)
    {
        value = 0;
        if (word.Length is 0 or > 6)
        {
            return false;
        }
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            if (c is < '0' or > '9')
            {
                return false;
            }
            value = (value * 10) + (c - '0');
        }
        return true;
    }

    private static bool TryHexDigit(char c, out int value)
    {
        if (c is >= '0' and <= '9')
        {
            value = c - '0';
            return true;
        }
        if (c is >= 'a' and <= 'f')
        {
            value = c - 'a' + 10;
            return true;
        }
        if (c is >= 'A' and <= 'F')
        {
            value = c - 'A' + 10;
            return true;
        }
        value = 0;
        return false;
    }

    /// <summary>The one writer of a block, so the header cannot drift between the four kinds.</summary>
    private static string Encode(ClipboardKind kind, int width, int height, ReadOnlySpan<byte> payload)
    {
        int units = kind == ClipboardKind.Sfx ? SfxRecordSize : width * height;
        if (!InRange(kind, width, height) || payload.Length < units)
        {
            // Callers hand their own bank's bytes, so this is a caller bug; it is a returned
            // empty string rather than a throw because the caller is a copy verb on the input
            // path, and an editor that crashes on Ctrl+C is worse than one that copies nothing.
            return string.Empty;
        }
        int digits = DigitsPerUnit(kind);
        var text = new StringBuilder(Tag.Length + 16 + (units * digits));
        // Invariant, not the machine's culture: a block copied on one desktop is pasted on
        // another, and the two numbers in its header have to mean the same thing on both.
        text.Append(Tag).Append(' ').Append(TagOf(kind)).Append(' ')
            .Append(width.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(height.ToString(CultureInfo.InvariantCulture)).Append(' ');
        for (int i = 0; i < units; i++)
        {
            byte value = payload[i];
            if (digits == 2)
            {
                text.Append(HexDigits[value >> 4]);
            }
            text.Append(HexDigits[value & 0x0F]);
        }
        return text.ToString();
    }

    private const string HexDigits = "0123456789abcdef";
}
