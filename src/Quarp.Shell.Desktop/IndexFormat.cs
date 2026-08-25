namespace Quarp.Shell.Desktop;

/// <summary>
/// How the shell spells a <b>bank index</b> — the sprite number over the canvas, the tile number
/// on the map, the sound slot, the pattern number, and the coordinate pairs beside them
/// (REFERENCES-EDITORS §8 item 20: "Переключаемый показ индексов в hex/dec"). One owner for the
/// whole shell, so a value the author toggles once reads the same way on all five screens.
///
/// <para><b>The two spellings are TIC-80's own, verbatim.</b> <c>src/studio/editors/sprite.c</c>
/// prints the sprite number as
/// <code>sprintf(buf, sprite->hexindex ? "0x%02X" : "#%i", index)</code>
/// and flips <c>hexindex</c> when the number itself is clicked (REFERENCES-EDITORS §2.1). So
/// decimal keeps the <c>#</c> sigil this shell already prints and hex takes the <c>0x</c> prefix
/// instead of it. The prefix is not decoration: with 256 sprites <c>#012</c> and <c>0x12</c> are
/// both plausible readings of the same cell, and a reader who cannot tell which base he is
/// looking at has gained nothing from the toggle.</para>
///
/// <para>PICO-8 hangs the same switch on <c>CTRL-H</c>
/// in both graphics editors ("to toggle hex view (shows sprite index in hexadecimal)",
/// REFERENCES-EDITORS §2.3) and that is the key this shell took.</para>
///
/// <para><b>Why this is a value and not a service.</b> It has no dependencies at all — no
/// geometry, no device, no session — which is the layer-1 test <c>scripts/check-modules.sh</c>
/// states in so many words, and the shape <see cref="PaletteColors"/> already has one file over:
/// that type owns "how a colour index becomes a colour", this one owns "how a bank index becomes
/// text". Being a <c>readonly struct</c> buys the thing a mutable holder could not: every
/// renderer can take it <b>by value with a compile-time default</b> (<c>default</c> is decimal),
/// so a caller with no shell behind it — a golden test, a layout probe — prints exactly what it
/// printed before this type existed, and there is no shared mutable fallback for anyone to
/// toggle by accident.</para>
///
/// <para><b>Where the one live copy lives</b> is <see cref="ShellModeMachine.Indexes"/>: the one
/// object every router already has in its hand and every screen's draw call is fed from, and the
/// one that outlives a cart. It is deliberately NOT on a per-screen view — five views would be
/// five answers to one question, which is exactly the defect §8 item 20 is asking us not to
/// ship — and deliberately not on a session, because nothing about hex or decimal is ever
/// written to a cartridge: it is a way of reading, not a fact of the document.</para>
/// </summary>
public readonly struct IndexFormat
{
    /// <summary>True when indexes print in hexadecimal. <c>default</c> is decimal, which is what every screen printed before the toggle existed.</summary>
    public bool Hex { get; private init; }

    /// <summary>The switch itself — <c>Ctrl+H</c> on any editor screen, and the only way this value ever changes.</summary>
    public IndexFormat Toggled() => new() { Hex = !Hex };

    /// <summary>
    /// A sprite or tile number as it stands alone in the status band: <c>#003</c> or
    /// <c>0x03</c>. Three decimal digits and two hex ones, because both are the widest the bank
    /// can produce (255 and 0xFF) — a number that never changes width cannot make the
    /// right-aligned field jump as the author walks the sheet.
    /// </summary>
    public string Sprite(int index) => Hex ? $"0x{index:X2}" : $"#{index:D3}";

    /// <summary>
    /// A labelled slot number — <c>SFX 07</c> or <c>SFX 0x07</c>, <c>PAT 12</c> or
    /// <c>PAT 0x12</c>. The label stays outside the base, so the two sound screens keep the
    /// words they already had and only the digits change.
    /// </summary>
    public string Slot(string label, int index) => Hex ? $"{label} 0x{index:X2}" : $"{label} {index:00}";

    /// <summary>
    /// A coordinate pair for the status band's left field: <c>007,012</c> or <c>0x07,0x0C</c>.
    /// Coordinates travel with the index because they are read together — the author looking up
    /// a sprite in hex is looking up its pixel in hex too — and because a screen that answered
    /// the key half-way would be worse than one that ignored it. Widest case is nine characters
    /// against the forty the line holds, so no field is at risk.
    /// </summary>
    public string Pair(int a, int b) => Hex ? $"0x{a:X2},0x{b:X2}" : $"{a:D3},{b:D3}";
}
