using Quarp.Api;
using Xunit;

namespace Quarp.Core.Tests;

/// <summary>
/// The second, retroactive palette stage — <see cref="DisplayPalette"/>, reached from a cartridge
/// as <c>Pald</c> and <c>Palr</c> — and the second hash that measures it.
///
/// <para><b>What each test is for.</b> The stage's whole claim is that it changes the picture
/// without changing the frame: the index buffer, and therefore <see cref="FrameHash.Of(Framebuffer)"/>,
/// stays where it was, while what reaches the window does not. A claim of that shape is exactly
/// the kind that passes by accident — a stage wired to nothing changes no pixel either — so every
/// test here carries a negative control: the fact it would catch if the code stopped doing its
/// job, spelled out as a "Break recipe" a reviewer can run.</para>
///
/// <para><b>Why the frame hash is asserted so often below.</b> Eight determinism anchors, twelve
/// pinned demo hashes, the cross-architecture CI job and twenty-six shell screen goldens are all
/// quoted from that one number. The default state of this stage is the identity map, so none of
/// them may move — and "may not move" is a thing to check, not to believe.</para>
/// </summary>
public class DisplayPaletteTests
{
    private static VirtualConsole NewConsole() => new(ConsoleProfile.Profile8);

    /// <summary>Paints a scene that uses several colours, a camera and a clip, so a hash of it means something.</summary>
    private static void PaintScene(VirtualConsole console)
    {
        console.Cls(1);
        console.RectFill(10, 10, 40, 20, 7);
        console.Circ(80, 45, 17, 10);
        console.Line(0, 0, 159, 89, 3);
        console.Print("QUARP", 60, 70, 8);
    }

    /// <summary>
    /// The identity state — what a fresh console is in — changes no pixel of the picture and no
    /// existing hash. Both halves are checked: the resolved colour of every (row, colour) pair is
    /// the colour itself, and the frame hash of an untouched console is still the constant
    /// <see cref="FrameHashTests.EmptyProfile8Frame"/> that predates this stage by four milestones.
    ///
    /// <para>Break recipe: make <c>DisplayPalette.Reset</c> fill the sets with anything but
    /// <c>(byte)i</c> — say <c>(byte)(i ^ 1)</c> — and the resolve loop reddens immediately while
    /// the frame hash stays green, which is precisely the failure this pair of assertions is
    /// shaped to separate.</para>
    /// </summary>
    [Fact]
    public void TheDefaultStateIsIdentityAndMovesNothing()
    {
        var console = NewConsole();

        Assert.True(console.Display.IsIdentity);
        Assert.Equal(FrameHashTests.EmptyProfile8Frame, FrameHash.Of(console.Framebuffer));

        for (int y = 0; y < console.ScreenHeight; y++)
        {
            Assert.Equal(0, console.Display.RowSet(y));
            Assert.Equal(0, console.Display.SetOffset(y));
            for (int color = 0; color < Palette.MasterCount; color++)
            {
                Assert.Equal((byte)color, console.Display.Resolve(y, (byte)color));
            }
        }
    }

    /// <summary>
    /// The hashed record of the identity state, pinned: 223 bytes — a 5-byte shape header, four
    /// 32-byte sets, 90 selector bytes — and the digest FNV-1a gives for them.
    ///
    /// <para><b>Where the constant came from.</b> This wave had no .NET SDK, so the value was
    /// derived rather than observed: FNV-1a 64 was run over the byte sequence spelled out in the
    /// assertions below. The same derivation, run over 14 400 zero bytes, reproduces
    /// <see cref="FrameHashTests.EmptyProfile8Frame"/> — the constant this suite has been pinning
    /// since ADR-021 — which is what makes the derivation trustworthy rather than merely
    /// self-consistent. This is <em>not</em> a determinism anchor: it is a fact about a default,
    /// and it moves the day the record's layout deliberately changes, with
    /// <see cref="DisplayPalette.HashVersion"/> moving in the same commit.</para>
    ///
    /// <para>Break recipe: drop the version byte from <c>WriteHashBytes</c>, or write the height
    /// high byte first. The length assertion or the digest reddens, and nothing else in the suite
    /// notices — which is the reason this test states the bytes and not only the digest.</para>
    /// </summary>
    [Fact]
    public void PinsTheIdentityRecordAndItsDigest()
    {
        var display = new DisplayPalette(ConsoleProfile.Profile8);
        Assert.Equal(223, display.HashLength);

        var record = new byte[display.HashLength];
        display.WriteHashBytes(record);

        Assert.Equal(DisplayPalette.HashVersion, record[0]);
        Assert.Equal(DisplayPalette.SetCount, record[1]);
        Assert.Equal(Palette.MasterCount, record[2]);
        Assert.Equal(90, record[3]);
        Assert.Equal(0, record[4]);
        for (int k = 0; k < DisplayPalette.SetCount; k++)
        {
            for (int i = 0; i < Palette.MasterCount; i++)
            {
                Assert.Equal((byte)i, record[DisplayPalette.HashHeaderLength + (k * Palette.MasterCount) + i]);
            }
        }
        for (int y = 0; y < 90; y++)
        {
            Assert.Equal(0, record[DisplayPalette.HashHeaderLength + 128 + y]);
        }

        Assert.Equal("98c930226b19a232", FrameHash.Of(display));
    }

    /// <summary>
    /// <b>The test the second hash exists for.</b> Two consoles draw the identical scene; one of
    /// them is then flooded through the display stage into an entirely different colour. The frame
    /// hash — "what did the cartridge draw" — is the same for both, to the digit. The display hash
    /// — "how is this coloured" — is not. Neither number alone describes the frame; the pair does.
    ///
    /// <para>Break recipe: make <c>Pald</c> write into the console's <c>_palMap</c> instead of the
    /// display state (that is, turn it back into a second <c>Pal</c>). The frame hashes diverge —
    /// and every anchor, demo hash and editor golden in the repository diverges with them, which
    /// is the accident this assertion is standing in front of.</para>
    /// </summary>
    [Fact]
    public void TheDisplayStageChangesTheColoursWithoutChangingTheFrame()
    {
        var plain = NewConsole();
        var tinted = NewConsole();
        PaintScene(plain);
        PaintScene(tinted);

        // Flood every colour of set 0 to master 16, then show the whole screen through set 0.
        for (int color = 0; color < Palette.MasterCount; color++)
        {
            tinted.Pald(0, (byte)color, 16);
        }
        tinted.Palr(0, tinted.ScreenHeight, 0);

        Assert.Equal(FrameHash.Of(plain.Framebuffer), FrameHash.Of(tinted.Framebuffer));
        Assert.Equal(plain.Framebuffer.Pixels, tinted.Framebuffer.Pixels);

        Assert.NotEqual(FrameHash.Of(plain.Display), FrameHash.Of(tinted.Display));
        Assert.Equal("98c930226b19a232", FrameHash.Of(plain.Display));

        // And the picture really is a different picture: every pixel is shown as master 16.
        for (int y = 0; y < tinted.ScreenHeight; y += 7)
        {
            for (int x = 0; x < tinted.ScreenWidth; x += 11)
            {
                byte stored = tinted.Framebuffer.Pixels[(y * tinted.ScreenWidth) + x];
                Assert.Equal(16, tinted.Display.Resolve(y, stored));
            }
        }
    }

    /// <summary>
    /// The difference an author is told about in one sentence, checked as two numbers:
    /// <c>Pal</c> changes what colour you <b>draw</b> with — the byte that lands in the buffer —
    /// and <c>Pald</c> changes what colour the buffer is <b>shown</b> in, retroactively, leaving
    /// the byte alone.
    ///
    /// <para>Break recipe: apply the display set inside <c>VirtualConsole.Plot</c> (at write time)
    /// rather than at output time. The <c>Pget</c> assertion on the <c>Pald</c> console flips from
    /// 7 to 23, and the stage stops being retroactive — a cartridge could no longer recolour
    /// anything it had already drawn.</para>
    /// </summary>
    [Fact]
    public void PalChangesWhatIsDrawnAndPaldChangesWhatIsShown()
    {
        var drawStage = NewConsole();
        drawStage.Pal(7, 23);
        drawStage.Cls(7);
        Assert.Equal(23, drawStage.Pget(80, 45));                      // the buffer itself changed
        Assert.Equal(23, drawStage.Display.Resolve(45, 23));           // output stage is identity

        var displayStage = NewConsole();
        displayStage.Cls(7);                                           // drawn BEFORE the call
        displayStage.Pald(0, 7, 23);
        Assert.Equal(7, displayStage.Pget(80, 45));                    // the buffer is untouched
        Assert.Equal(23, displayStage.Display.Resolve(45, 7));         // but it is shown as 23

        // Same picture in the buffer as an untouched console: the stage wrote no pixel at all.
        var untouched = NewConsole();
        untouched.Cls(7);
        Assert.Equal(FrameHash.Of(untouched.Framebuffer), FrameHash.Of(displayStage.Framebuffer));
    }

    /// <summary>
    /// Rows shown through different sets come out in different colours — the whole point of the
    /// selector — and the band form assigns exactly the rows it names and no others.
    ///
    /// <para>Break recipe: have <c>SetOffset</c> ignore its argument and always return 0. Every
    /// row resolves through set 0, the horizon disappears, and this test names the first row that
    /// stopped moving.</para>
    /// </summary>
    [Fact]
    public void RowsWithDifferentSetsAreColouredDifferently()
    {
        var console = NewConsole();
        console.Pald(1, 7, 23);          // set 1: green shown as forest
        console.Pald(2, 7, 3);           // set 2: green shown as white
        console.Palr(0, 30, 1);          // sky band
        console.Palr(30, 30, 2);         // middle band
        // rows 60..89 keep set 0

        Assert.Equal(23, console.Display.Resolve(0, 7));
        Assert.Equal(23, console.Display.Resolve(29, 7));
        Assert.Equal(3, console.Display.Resolve(30, 7));
        Assert.Equal(3, console.Display.Resolve(59, 7));
        Assert.Equal(7, console.Display.Resolve(60, 7));
        Assert.Equal(7, console.Display.Resolve(89, 7));

        Assert.Equal(1, console.Display.RowSet(29));
        Assert.Equal(2, console.Display.RowSet(30));
        Assert.Equal(0, console.Display.RowSet(60));

        // The offsets a presenter takes once per row are the same three numbers, scaled.
        Assert.Equal(1 * Palette.MasterCount, console.Display.SetOffset(0));
        Assert.Equal(2 * Palette.MasterCount, console.Display.SetOffset(30));
        Assert.Equal(0, console.Display.SetOffset(60));

        // A colour nobody remapped is unchanged in every set: a set is a map, not a filter.
        Assert.Equal(10, console.Display.Resolve(0, 10));
        Assert.Equal(10, console.Display.Resolve(30, 10));
    }

    /// <summary>
    /// The selector is never read or written outside the screen: rows below 0 and at or past
    /// <c>ScreenHeight</c> are dropped on write and answer with set 0 on read, and a band that
    /// hangs off an edge paints only the rows that exist.
    ///
    /// <para>Break recipe: drop the range check from <c>AssignRow</c> — an
    /// <c>IndexOutOfRangeException</c> reaches the cartridge from a call the API promises is soft
    /// (API-8 §1.1) — or change <c>RowSet</c>'s guard to <c>y &lt; Height</c> only, and a negative
    /// row starts reading memory in front of the array.</para>
    /// </summary>
    [Fact]
    public void TheSelectorIsNotReadOrWrittenOutsideTheScreen()
    {
        var console = NewConsole();
        string before = FrameHash.Of(console.Display);

        console.Palr(-1, 2);
        console.Palr(console.ScreenHeight, 2);
        console.Palr(int.MaxValue, 2);
        console.Palr(int.MinValue, 2);
        Assert.Equal(before, FrameHash.Of(console.Display));
        Assert.True(console.Display.IsIdentity);

        Assert.Equal(0, console.Display.RowSet(-1));
        Assert.Equal(0, console.Display.RowSet(console.ScreenHeight));
        Assert.Equal(0, console.Display.RowSet(int.MinValue));
        Assert.Equal(0, console.Display.SetOffset(int.MaxValue));

        // A band clipped at the top: rows 0..4 only.
        console.Palr(-5, 10, 1);
        Assert.Equal(1, console.Display.RowSet(0));
        Assert.Equal(1, console.Display.RowSet(4));
        Assert.Equal(0, console.Display.RowSet(5));

        // A band clipped at the bottom: rows 88..89 only, and nothing past the array.
        console.Palr(88, 1000, 2);
        Assert.Equal(2, console.Display.RowSet(88));
        Assert.Equal(2, console.Display.RowSet(89));
        Assert.Equal(0, console.Display.RowSet(87));

        // A band of no height is a no-op, exactly as a Clip of no width is.
        string beforeEmpty = FrameHash.Of(console.Display);
        console.Palr(10, 0, 3);
        console.Palr(10, -4, 3);
        Assert.Equal(beforeEmpty, FrameHash.Of(console.Display));
    }

    /// <summary>
    /// Every index is masked rather than rejected, the rule the whole surface follows
    /// (API-8 §1.4): set &amp; 3, colours &amp; 31.
    ///
    /// <para>Break recipe: replace the masks in <c>Remap</c> with a range check that returns
    /// early. <c>Pald(4, 7, 23)</c> silently does nothing instead of writing set 0, and a
    /// cartridge ported from a console with more sets fails in a way nothing reports.</para>
    /// </summary>
    [Fact]
    public void IndicesAreMaskedNotRejected()
    {
        var masked = NewConsole();
        masked.Pald(6, 39, 48);          // set 6 & 3 = 2, colour 39 & 31 = 7, shown 48 & 31 = 16
        var plain = NewConsole();
        plain.Pald(2, 7, 16);

        Assert.Equal(FrameHash.Of(plain.Display), FrameHash.Of(masked.Display));

        var rows = NewConsole();
        rows.Palr(10, 7);                // set 7 & 3 = 3
        Assert.Equal(3, rows.Display.RowSet(10));
    }

    /// <summary>
    /// Reset works, at all three grains: one set, all sets, the selector — and the resets are
    /// independent of each other, which is what makes <c>Pald()</c> safe to call from a screen
    /// that is using the selector for something else.
    ///
    /// <para>Break recipe: make <c>ResetSets</c> also clear the selector. The last two assertions
    /// redden — and a cartridge that resets its palettes between scenes silently loses its
    /// horizon.</para>
    /// </summary>
    [Fact]
    public void ResetPutsTheStageBackAtEveryGrain()
    {
        var console = NewConsole();
        string identity = FrameHash.Of(console.Display);

        console.Pald(0, 7, 23);
        console.Pald(1, 7, 3);
        console.Palr(0, 45, 1);
        Assert.NotEqual(identity, FrameHash.Of(console.Display));

        console.Pald(0);                                     // one set back to identity
        Assert.Equal(7, console.Display.Resolve(60, 7));     // row 60 is set 0 again
        Assert.Equal(3, console.Display.Resolve(0, 7));      // set 1 is untouched

        console.Pald();                                      // all sets
        Assert.Equal(7, console.Display.Resolve(0, 7));
        Assert.Equal(1, console.Display.RowSet(0));          // selector survived, on purpose
        Assert.NotEqual(identity, FrameHash.Of(console.Display));

        console.Palr();                                      // and the selector
        Assert.Equal(identity, FrameHash.Of(console.Display));
        Assert.True(console.Display.IsIdentity);
    }

    /// <summary>Sets one band and one map, so a test can watch them survive something.</summary>
    private static void TintLowerHalf(VirtualConsole console)
    {
        console.Pald(1, 7, 23);
        console.Palr(45, 45, 1);
    }

    /// <summary>
    /// <b>The reset decision, pinned.</b> The output state lives until it is changed — it is not
    /// cleared by <c>Cls</c>, not cleared by drawing, not cleared at a frame boundary — and it
    /// <em>is</em> cleared when a cartridge is attached, together with camera, clip, <c>Pal</c>
    /// and <c>Palt</c>. That is the same rule the other four drawing states follow (API-8 §1,
    /// ADR-017: "<c>Cls</c> does not reset the clip — state lives until explicitly changed, one
    /// rule instead of an exception to it").
    ///
    /// <para>Break recipe: clear the display state from <c>Cls</c>, the way PICO-8's <c>cls</c>
    /// resets the clip. The first two assertions redden — and, worse than reddening, a night wash
    /// would have to be re-applied after every screen clear, which is 128 writes per frame to hold
    /// a picture still.</para>
    /// </summary>
    [Fact]
    public void TheStageLivesUntilItIsChangedAndIsResetWhenACartIsAttached()
    {
        var console = NewConsole();
        TintLowerHalf(console);
        string tinted = FrameHash.Of(console.Display);

        console.Cls(3);
        console.RectFill(0, 0, 160, 90, 5);
        Assert.Equal(tinted, FrameHash.Of(console.Display));      // drawing does not touch it
        Assert.Equal(23, console.Display.Resolve(89, 7));

        console.AttachCart(new PaintingCart());
        Assert.True(console.Display.IsIdentity);                  // a new run starts clean
        Assert.Equal("98c930226b19a232", FrameHash.Of(console.Display));

        // And a cart's own calls survive its ticks: this is drawing state, not simulation state.
        console.Tick(default);
        console.Tick(default);
        Assert.Equal(1, console.Display.RowSet(50));
        Assert.Equal(23, console.Display.Resolve(50, 7));
    }

    /// <summary>Draws through both palette stages, so a tick exercises the API a cartridge sees.</summary>
    private sealed class PaintingCart : Cartridge
    {
        public override void Init()
        {
        }

        public override void Update()
        {
        }

        public override void Draw()
        {
            Cls(7);
            Pald(1, 7, 23);
            Palr(45, 45, 1);
        }
    }

    /// <summary>
    /// The revision counter the presenter caches its composed RGB table against: it moves on every
    /// write and only on a write. If it stopped moving, the window would keep showing last frame's
    /// colours — a defect no framebuffer test could ever see.
    ///
    /// <para>Break recipe: delete the <c>_revision++</c> from <c>Remap</c>. This test names it
    /// immediately; without it the same bug shows up as "the fade only works when I also move a
    /// row", found by eye, weeks later.</para>
    /// </summary>
    [Fact]
    public void EveryWriteMovesTheRevisionAndNothingElseDoes()
    {
        var display = new DisplayPalette(ConsoleProfile.Profile8);
        int start = display.Revision;

        display.Remap(0, 7, 23);
        int afterRemap = display.Revision;
        Assert.True(afterRemap > start);

        display.AssignRow(10, 1);
        int afterRow = display.Revision;
        Assert.True(afterRow > afterRemap);

        display.AssignRows(20, 10, 2);
        Assert.True(display.Revision > afterRow);

        int settled = display.Revision;
        _ = display.Resolve(10, 7);
        _ = display.RowSet(10);
        _ = display.SetOffset(10);
        _ = display.IsIdentity;
        _ = FrameHash.Of(display);
        Assert.Equal(settled, display.Revision);                 // reads never move it

        display.AssignRow(-1, 3);                                // a dropped write is not a write
        Assert.Equal(settled, display.Revision);
    }

    /// <summary>
    /// The record is fixed-size and complete: two states that differ anywhere — in a set byte, in
    /// a selector byte — hash differently, and two states that agree everywhere hash the same
    /// however they were reached.
    ///
    /// <para>Break recipe: leave the selector out of <c>WriteHashBytes</c> and hash only the 128
    /// set bytes. The first <c>NotEqual</c> reddens; a per-row effect would become invisible to
    /// the only instrument that can see it.</para>
    /// </summary>
    [Fact]
    public void TheHashSeesEveryByteOfTheState()
    {
        var a = new DisplayPalette(ConsoleProfile.Profile8);
        var b = new DisplayPalette(ConsoleProfile.Profile8);
        Assert.Equal(FrameHash.Of(a), FrameHash.Of(b));

        a.Remap(3, 31, 30);
        Assert.NotEqual(FrameHash.Of(a), FrameHash.Of(b));
        b.Remap(3, 31, 30);
        Assert.Equal(FrameHash.Of(a), FrameHash.Of(b));

        a.AssignRow(89, 3);
        Assert.NotEqual(FrameHash.Of(a), FrameHash.Of(b));
        b.AssignRows(89, 1, 3);                                  // same bytes, other door
        Assert.Equal(FrameHash.Of(a), FrameHash.Of(b));

        // Text form: the same 16 lowercase hex digits every other hash in the project wears.
        string text = FrameHash.Of(a);
        Assert.Equal(FrameHash.HexLength, text.Length);
        Assert.All(text, c => Assert.True((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'), text));
    }

    /// <summary>
    /// A wrong-sized span is refused rather than half-filled, and the hash of a taller console's
    /// state is a different length — the reason the height is stamped into the header.
    ///
    /// <para>Break recipe: let <c>WriteHashBytes</c> write as much as fits. A caller with a
    /// 218-byte buffer would silently hash a truncated selector, and two different states would
    /// start agreeing.</para>
    /// </summary>
    [Fact]
    public void AWrongSizedRecordIsRefused()
    {
        var display = new DisplayPalette(ConsoleProfile.Profile8);
        Assert.Throws<ArgumentException>(() => display.WriteHashBytes(new byte[display.HashLength - 1]));
        Assert.Throws<ArgumentException>(() => display.WriteHashBytes(new byte[display.HashLength + 1]));

        var tall = new DisplayPalette(new ConsoleProfile { Name = "TALL", Width = 320, Height = 180 });
        Assert.Equal(DisplayPalette.HashHeaderLength + 128 + 180, tall.HashLength);
        Assert.NotEqual(FrameHash.Of(display), FrameHash.Of(tall));
    }
}
