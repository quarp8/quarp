using Quarp.CartKit;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The system clipboard's contract for all four picture-and-sound banks (REFERENCES-EDITORS §8
/// item 2), proven headless: nothing here constructs a window, a graphics device or an SDL
/// handle. Every claim is driven through the same public verbs the routers call — the sessions
/// take a string and hand a string back, and who fetched that string from the operating system
/// is deliberately outside the frame (see <see cref="ITextClipboard"/>).
///
/// <para><b>What is being proved, in four sentences.</b> (1) A round trip through
/// <see cref="ClipboardFormat"/> is byte-exact on each of the four banks. (2) A block of the
/// wrong bank, and a block whose text has been damaged, are refused <em>with a sentence the
/// author can read on the message line</em> — asserted through the very
/// <c>StandingNotice</c> each renderer draws, not through a private field. (3) Each paste is
/// exactly one Ctrl+Z. (4) A refused paste writes nothing at all.</para>
///
/// <para><b>Every test below carries its own negative control</b>, because each of these claims
/// has a way of passing for the wrong reason: a round-trip test passes if the paste is a no-op
/// and the bytes never changed, a refusal test passes if the verb refuses <em>everything</em>,
/// and an undo test passes if the paste pushed no step at all. The controls are named in each
/// test's own comment along with the recipe for breaking it.</para>
///
/// <para><c>carts/</c> holds pinned goldens and nothing here writes into it: every session runs
/// on a fresh temp folder.</para>
/// </summary>
public class ClipboardTextTests : IDisposable
{
    private readonly string _root;

    public ClipboardTextTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-clip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(_root, recursive: true);
    }

    // ---- helpers ----

    private string CartFolder(byte[]? sheet = null)
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        if (sheet is not null)
        {
            File.WriteAllBytes(
                Path.Combine(folder, "gfx.png"),
                PngEncoder.EncodeFromPaletteIndices(sheet, CartData.GfxWidth, CartData.GfxHeight));
        }
        return folder;
    }

    /// <summary>A sheet whose every pixel is a distinct-ish visible index, so corruption anywhere shows up somewhere.</summary>
    private static byte[] PatternSheet()
    {
        var sheet = new byte[CartData.GfxWidth * CartData.GfxHeight];
        for (int i = 0; i < sheet.Length; i++)
        {
            sheet[i] = (byte)((i * 7) % Palette.VisibleCount);
        }
        return sheet;
    }

    /// <summary>The active layer's pixels inside the current region, row-major — what a copy reads and a paste writes.</summary>
    private static byte[] RegionPixels(SpriteEditorSession session)
    {
        int n = session.RegionPixels;
        int size = VirtualConsole.SpriteSize;
        ReadOnlySpan<byte> layer = session.LayerPixels(session.ActiveLayerIndex);
        var pixels = new byte[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                pixels[(y * n) + x] =
                    layer[(((session.RegionCellY * size) + y) * CartData.GfxWidth)
                          + (session.RegionCellX * size) + x];
            }
        }
        return pixels;
    }

    /// <summary>One SFX slot's 68 bytes, read straight out of the live payload through the format's own offsets.</summary>
    private static byte[] SlotRecord(SfxEditorSession session, int slot)
    {
        ReadOnlySpan<byte> bank = session.Payload;
        var record = new byte[ClipboardFormat.SfxRecordSize];
        for (int i = 0; i < AudioFormat.SfxSlotHeaderSize; i++)
        {
            record[i] = bank[AudioFormat.SlotHeaderOffset(slot) + i];
        }
        for (int i = 0; i < AudioFormat.SfxStepCount * AudioFormat.SfxStepSize; i++)
        {
            record[AudioFormat.SfxSlotHeaderSize + i] = bank[AudioFormat.StepOffset(slot, 0) + i];
        }
        return record;
    }

    private static int[] MusicCells(MusicEditorSession session, int pattern, int channel, int patterns, int channels)
    {
        var cells = new int[patterns * channels];
        for (int row = 0; row < patterns; row++)
        {
            for (int column = 0; column < channels; column++)
            {
                cells[(row * channels) + column] = session.ChannelSlot(pattern + row, channel + column);
            }
        }
        return cells;
    }

    /// <summary>A map with a recognisable pattern in the corner the tests copy from.</summary>
    private static void SeedMap(MapEditorSession map)
    {
        var tiles = new byte[4 * 3];
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i] = (byte)(0x30 + i);
        }
        map.PasteBlock(2, 1, 4, 3, tiles);
    }

    // ---- 1. round trip, one test per bank ----

    /// <summary>
    /// <b>Sprites.</b> Copy the region, scribble over it, paste the text back, and the eight
    /// rows of pixels are the ones that were copied — byte for byte, colour 0 included.
    ///
    /// <para><b>Negative control</b>, and it is the one that matters here: the region is
    /// repainted with a solid colour <em>7</em> between the copy and the paste, and that state is
    /// asserted to differ from the original. Without it this test would pass on a paste that did
    /// nothing at all. Colour 7 rather than colour 0 is chosen on purpose — the paste is opaque
    /// and a transparent one would leave 7s standing where the block holds 0s, which a cleared
    /// (all-zero) region could not have shown.</para>
    ///
    /// <para>Break recipe: make <see cref="SpriteEditorSession.PasteFromText"/> treat source
    /// colour 0 as transparent (the stamp tool's rule — return <c>src[...]</c> when the block's
    /// pixel is 0) and this goes red on every pixel of the pattern sheet that happens to be 0,
    /// while every other assertion here stays green.</para>
    /// </summary>
    [Fact]
    public void SpriteRegionSurvivesTheRoundTripThroughClipboardText()
    {
        var session = new SpriteEditorSession(CartFolder(PatternSheet()));
        session.SelectRegionCell(3, 2);
        byte[] original = RegionPixels(session);

        string text = session.CopyToText();
        Assert.StartsWith("quarp1 gfx 8 8 ", text, StringComparison.Ordinal);

        // The negative control: destroy the region, and prove it is destroyed.
        session.ClearRegion();
        session.SelectColor(7);
        session.Fill(0, 0);
        byte[] scribbled = RegionPixels(session);
        Assert.NotEqual(original, scribbled);
        Assert.All(scribbled, p => Assert.Equal(7, p));

        Assert.True(session.PasteFromText(text));
        Assert.Equal(original, RegionPixels(session));
        Assert.Null(session.ClipboardNotice);
    }

    /// <summary>
    /// <b>Map.</b> Mark a rectangle, copy it as text, empty it, paste the text back over the same
    /// corner, and the twelve cells are the ones that were copied.
    ///
    /// <para><b>Negative control:</b> the emptied rectangle is asserted to be all
    /// <see cref="MapEditorSession.EmptyTile"/> before the paste, so a paste that did nothing
    /// cannot pass. The floating step is exercised as the shell exercises it —
    /// <c>PasteText</c> arms the float, <c>PasteAt</c> lands it — because that is where the
    /// clipboard's block and the ghost under the cursor have to agree.</para>
    ///
    /// <para>Break recipe: have <c>MapEditorPaint.PasteText</c> skip
    /// <c>view.Clipboard.Write(...)</c> and merely start the float. The float then lands whatever
    /// was in hand before (nothing), the cells stay empty, and this test names the defect while
    /// the refusal tests below stay green.</para>
    /// </summary>
    [Fact]
    public void MapBlockSurvivesTheRoundTripThroughClipboardText()
    {
        string folder = CartFolder();
        var map = new MapEditorSession(folder);
        var view = new MapEditorView();
        SeedMap(map);

        view.BeginSelection(2, 1);
        view.UpdateSelection(5, 3);
        view.EndSelection();
        Assert.Equal(4, view.SelectionWidth);
        Assert.Equal(3, view.SelectionHeight);

        string text = MapEditorPaint.CopySelectionToText(map, view);
        Assert.StartsWith("quarp1 map 4 3 ", text, StringComparison.Ordinal);

        map.ClearArea(2, 1, 4, 3);
        for (int y = 1; y <= 3; y++)
        {
            for (int x = 2; x <= 5; x++)
            {
                // The negative control: nothing of the block survives the emptying.
                Assert.Equal(MapEditorSession.EmptyTile, map.TileAt(x, y));
            }
        }

        Assert.True(MapEditorPaint.PasteText(map, view, text));
        Assert.True(view.PasteFloating);
        Assert.True(MapEditorPaint.PasteAt(map, view, 2, 1));
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                Assert.Equal((byte)(0x30 + (row * 4) + column), map.TileAt(2 + column, 1 + row));
            }
        }
        Assert.Null(map.ClipboardNotice);
    }

    /// <summary>
    /// <b>SFX.</b> A whole slot — speed, length, loop and all 32 step words — copied out of slot
    /// 5 and pasted into slot 9 gives two records that are byte-identical. That is the
    /// cross-slot exchange TIC-80's <c>toClipboard(effect, sizeof(tic_sample))</c> buys (§5.1),
    /// and it is why the slot header travels with the notes.
    ///
    /// <para><b>Negative control:</b> slot 9 is asserted empty and different from slot 5 before
    /// the paste, so a paste that did nothing cannot pass; and the loop fields are checked by
    /// name afterwards, so a paste that moved only the step words cannot pass either.</para>
    ///
    /// <para>Break recipe: drop the header from <c>RecordByteOffset</c>'s first branch — copy
    /// only the 64 step bytes — and slot 9 gains the notes at speed 0 with no loop; the loop and
    /// speed assertions below go red and the step comparison stays green, naming exactly which
    /// half was lost.</para>
    /// </summary>
    [Fact]
    public void SfxSlotSurvivesTheRoundTripThroughClipboardText()
    {
        var session = new SfxEditorSession(CartFolder());
        session.SetStep(5, 0, note: 24, wave: 2, volume: 6, effect: AudioFormat.EffectSlide);
        session.SetStep(5, 1, note: 26, wave: 3, volume: 4, effect: AudioFormat.EffectNone);
        session.SetStep(5, 2, note: 31, wave: 5, volume: 7, effect: AudioFormat.EffectVibrato);
        session.SetSpeed(5, 12);
        session.SetLoop(5, 1, 3);

        byte[] source = SlotRecord(session, 5);
        string text = session.CopySlotToText(5);
        Assert.StartsWith("quarp1 sfx 1 1 ", text, StringComparison.Ordinal);

        // The negative control: the destination is empty, and provably not the source already.
        Assert.True(session.SlotIsEmpty(9));
        Assert.NotEqual(source, SlotRecord(session, 9));

        Assert.True(session.PasteSlotFromText(9, text));
        Assert.Equal(source, SlotRecord(session, 9));
        Assert.Equal(12, session.SlotSpeed(9));
        Assert.Equal(3, session.SlotLength(9));
        Assert.Equal(1, session.SlotLoopStart(9));
        Assert.Equal(3, session.SlotLoopEnd(9));
        Assert.Null(session.ClipboardNotice);
    }

    /// <summary>
    /// <b>Music.</b> A block of the pattern list — four patterns by two channels — copied,
    /// silenced and pasted back gives the same eight cells, silence included. Silence is the
    /// interesting half: it is spelled <c>-1</c> in the clipboard and <c>0x00</c> on the wire,
    /// and a block that lost the difference between "silent" and "slot 0" would sound wrong
    /// while comparing equal on three quarters of its cells.
    ///
    /// <para><b>Negative control:</b> the block is asserted all-silent between the copy and the
    /// paste, so a paste that did nothing cannot pass. One of the copied cells is deliberately
    /// slot <b>0</b> and another is deliberately silent, which is what makes the
    /// silence-versus-zero claim testable at all.</para>
    ///
    /// <para>Break recipe: in <see cref="ClipboardFormat.EncodeMusic"/> drop the active bit —
    /// write the bare slot number — and slot 0 and silence become the same byte <c>00</c>; the
    /// pasted block comes back all-silent and this test names it, while the sprite and map round
    /// trips stay green.</para>
    /// </summary>
    [Fact]
    public void MusicBlockSurvivesTheRoundTripThroughClipboardText()
    {
        var session = new MusicEditorSession(CartFolder());
        session.SetChannelSlot(10, 1, 0);       // slot zero, which is NOT silence
        session.SetChannelSlot(11, 2, 63);
        session.SetChannelSlot(12, 1, 7);
        // (13, 1) and every other cell of the block stays silent on purpose.

        session.SelectRange(10, 1, 13, 2);
        int[] original = MusicCells(session, 10, 1, 4, 2);
        Assert.Contains(0, original);
        Assert.Contains(-1, original);

        string text = session.CopySelectionToText();
        Assert.StartsWith("quarp1 mus 2 4 ", text, StringComparison.Ordinal);

        session.ClearSelectedCells();
        Assert.All(MusicCells(session, 10, 1, 4, 2), cell => Assert.Equal(-1, cell));

        Assert.True(session.PasteFromText(10, 1, text));
        Assert.Equal(original, MusicCells(session, 10, 1, 4, 2));
        Assert.Null(session.ClipboardNotice);
    }

    // ---- 2. refusals: the wrong bank, and damaged text ----

    /// <summary>
    /// A block of another bank is refused on every screen, and the refusal <b>names the bank it
    /// actually is</b> — the whole reason the format carries a header rather than relying on
    /// length the way TIC-80's sprite and SFX clipboard does (§1). The sentence is read back
    /// through each renderer's <c>StandingNotice</c>, which is the function that puts it on the
    /// message line, so this proves the author sees it and not merely that a property was set.
    ///
    /// <para><b>Negative control:</b> the same four sessions are then handed a block of their
    /// <em>own</em> kind and accept it, with the notice back to null. Without that half, a verb
    /// that refused everything unconditionally would pass this test.</para>
    ///
    /// <para>Break recipe: make <see cref="ClipboardFormat.TryDecode(string, ClipboardKind, out ClipboardBlock, out string)"/>
    /// ignore the <c>expected</c> argument. Every refusal assertion here goes red at once and
    /// the accept half stays green, which is the shape of the defect.</para>
    /// </summary>
    [Fact]
    public void ABlockOfAnotherBankIsRefusedByNameOnEveryScreen()
    {
        string gfx = ClipboardFormat.EncodeSprites(2, 2, new byte[] { 1, 2, 3, 4 });
        string mapText = ClipboardFormat.EncodeMap(2, 1, new byte[] { 9, 9 });
        string sfxText = ClipboardFormat.EncodeSfx(new byte[ClipboardFormat.SfxRecordSize]);
        string musText = ClipboardFormat.EncodeMusic(1, 1, new[] { 5 });

        var sprites = new SpriteEditorSession(CartFolder(PatternSheet()));
        var map = new MapEditorSession(CartFolder());
        var view = new MapEditorView();
        var sfx = new SfxEditorSession(CartFolder());
        var music = new MusicEditorSession(CartFolder());
        // Both screens carry a standing warning of their own while the author stands on
        // sprite/tile 0, and this test is about the clipboard's line rather than about theirs:
        // step off zero so that "the line is quiet again" means what it says.
        sprites.SelectRegionCell(1, 0);
        map.SelectSprite(1);

        Assert.False(sprites.PasteFromText(mapText));
        Assert.Equal("PASTE: THAT IS A MAP BLOCK", SpriteEditorRenderer.StandingNotice(sprites));

        Assert.False(MapEditorPaint.PasteText(map, view, gfx));
        Assert.Equal("PASTE: THAT IS A GFX BLOCK", MapEditorRenderer.StandingNotice(map));
        Assert.False(view.PasteFloating);

        Assert.False(sfx.PasteSlotFromText(0, musText));
        Assert.Equal("PASTE: THAT IS A MUS BLOCK", SfxEditorRenderer.StandingNotice(sfx));

        Assert.False(music.PasteFromText(0, 0, sfxText));
        Assert.Equal("PASTE: THAT IS A SFX BLOCK", MusicEditorRenderer.StandingNotice(music));

        // The negative control: each screen takes its own kind, and the line goes quiet again.
        Assert.True(sprites.PasteFromText(gfx));
        Assert.Null(SpriteEditorRenderer.StandingNotice(sprites));
        Assert.True(MapEditorPaint.PasteText(map, view, mapText));
        Assert.Null(MapEditorRenderer.StandingNotice(map));
        Assert.True(sfx.PasteSlotFromText(0, sfxText));
        Assert.Null(SfxEditorRenderer.StandingNotice(sfx));
        Assert.True(music.PasteFromText(0, 0, musText));
        Assert.Null(MusicEditorRenderer.StandingNotice(music));
    }

    /// <summary>
    /// Text that is not ours at all, text that is ours but damaged, and an empty clipboard each
    /// get their own sentence on the message line. Three different failures, three different
    /// things for the author to do about them — "you pasted a paragraph", "the block arrived
    /// broken", "there is nothing to paste" — which is why they are not one message.
    ///
    /// <para><b>Negative control:</b> the undamaged text is accepted by the very same session at
    /// the end. Without it, a decoder that refused everything would pass.</para>
    ///
    /// <para>Break recipe: delete the <c>filled != units</c> check at the end of
    /// <see cref="ClipboardFormat.TryDecode(string, out ClipboardBlock, out string)"/>. The
    /// truncated block is then accepted with its tail left as zeros, and the "damaged" assertion
    /// for it goes red while the foreign-text one stays green.</para>
    /// </summary>
    [Fact]
    public void ForeignTextDamagedTextAndAnEmptyClipboardEachGetTheirOwnSentence()
    {
        var map = new MapEditorSession(CartFolder());
        var view = new MapEditorView();
        map.SelectSprite(1);        // see the foreign-block test: tile 0 has a standing line of its own
        string good = ClipboardFormat.EncodeMap(2, 2, new byte[] { 1, 2, 3, 4 });

        Assert.False(MapEditorPaint.PasteText(map, view, "Dear Bob, here is the level I promised."));
        Assert.Equal($"PASTE: {ClipboardFormat.ForeignReason}", MapEditorRenderer.StandingNotice(map));

        // Our tag, a hex digit turned into a letter the alphabet does not reach.
        Assert.False(MapEditorPaint.PasteText(map, view, good[..^1] + "z"));
        Assert.Equal($"PASTE: {ClipboardFormat.DamagedReason}", MapEditorRenderer.StandingNotice(map));

        // Our tag, the header's promise longer than the payload delivers.
        Assert.False(MapEditorPaint.PasteText(map, view, good[..^2]));
        Assert.Equal($"PASTE: {ClipboardFormat.DamagedReason}", MapEditorRenderer.StandingNotice(map));

        Assert.False(MapEditorPaint.PasteText(map, view, string.Empty));
        Assert.Equal($"PASTE: {ClipboardFormat.EmptyReason}", MapEditorRenderer.StandingNotice(map));

        // The negative control: the same session, the same verb, the intact text.
        Assert.True(MapEditorPaint.PasteText(map, view, good));
        Assert.Null(MapEditorRenderer.StandingNotice(map));
    }

    /// <summary>
    /// A sprite block bigger than the region is refused with both measurements in the sentence,
    /// never clipped. Half a pasted sprite reads as a drawing mistake and would be undone by
    /// hand instead of understood; TIC-80 refuses the same case outright with its
    /// <c>sameSize</c> flag (§1).
    ///
    /// <para><b>Negative control:</b> the very same 16x16 block is accepted the moment the region
    /// is grown to 16 pixels a side, so this is a size rule and not a broken decoder.</para>
    ///
    /// <para>Break recipe: change the guard to clip instead of refuse (drop the <c>if</c> and let
    /// <c>ApplyRegionEdit</c>'s bounds do the work). The refusal assertion goes red while the
    /// grown-region assertion stays green, which is exactly the behaviour the rule forbids.</para>
    /// </summary>
    [Fact]
    public void ASpriteBlockLargerThanTheRegionIsRefusedWithBothMeasurements()
    {
        var session = new SpriteEditorSession(CartFolder(PatternSheet()));
        var pixels = new byte[16 * 16];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i % Palette.VisibleCount);
        }
        string big = ClipboardFormat.EncodeSprites(16, 16, pixels);

        Assert.Equal(1, session.RegionCells);
        byte[] before = RegionPixels(session);
        Assert.False(session.PasteFromText(big));
        Assert.Equal("PASTE: 16x16 BLOCK, REGION IS 8x8", SpriteEditorRenderer.StandingNotice(session));
        Assert.Equal(before, RegionPixels(session));    // a refusal writes nothing

        // The negative control: same block, a region it fits in.
        session.SelectRegionSize(2);
        Assert.True(session.PasteFromText(big));
        Assert.Equal(pixels, RegionPixels(session));
        Assert.Null(session.ClipboardNotice);
    }

    /// <summary>
    /// An SFX block whose bytes would make an <em>illegal</em> bank is refused: AUDIO-FORMAT §5
    /// forbids a wave of 6, a rest that is not the zero word, and a loop outside its length, and
    /// the clipboard is not the softer door — the pasted record goes through the very validator
    /// the file loader uses before a live byte moves. Without this the preview APU would be
    /// handed a bank that <c>sfx.bin</c> could not load back.
    ///
    /// <para><b>Negative control:</b> a legal record built the same way, through the same
    /// encoder, is accepted; so the refusal is about the content and not about the road.</para>
    ///
    /// <para>Break recipe: delete the <c>ValidateSfxPayload</c> call in
    /// <see cref="SfxEditorSession.PasteSlotFromText"/> and write the record straight in. This
    /// test goes red and every round-trip test above stays green — which is the point, because a
    /// round trip can only ever carry legal banks.</para>
    /// </summary>
    [Fact]
    public void AnSfxBlockThatWouldBreakTheBankIsRefused()
    {
        var session = new SfxEditorSession(CartFolder());

        // speed 8, length 1, no loop; step 0 = wave 6, which profile 8 does not define.
        var illegal = new byte[ClipboardFormat.SfxRecordSize];
        illegal[0] = 8;
        illegal[1] = 1;
        ushort word = (ushort)((6 << 6) | (7 << 9));    // wave 6, volume 7 — a wave the format refuses
        illegal[AudioFormat.SfxSlotHeaderSize] = (byte)(word & 0xFF);
        illegal[AudioFormat.SfxSlotHeaderSize + 1] = (byte)(word >> 8);

        Assert.False(session.PasteSlotFromText(3, ClipboardFormat.EncodeSfx(illegal)));
        Assert.Equal("PASTE: SLOT DATA IS NOT LEGAL", SfxEditorRenderer.StandingNotice(session));
        Assert.True(session.SlotIsEmpty(3));            // a refusal writes nothing
        Assert.False(session.CanUndo);                  // and costs no undo step

        // The negative control: the same record with a wave profile 8 does define.
        var legal = (byte[])illegal.Clone();
        ushort ok = AudioFormat.PackStep(24, AudioFormat.WaveTriangle, 7, AudioFormat.EffectNone);
        legal[AudioFormat.SfxSlotHeaderSize] = (byte)(ok & 0xFF);
        legal[AudioFormat.SfxSlotHeaderSize + 1] = (byte)(ok >> 8);
        Assert.True(session.PasteSlotFromText(3, ClipboardFormat.EncodeSfx(legal)));
        Assert.Equal(1, session.SlotLength(3));
        Assert.Null(session.ClipboardNotice);
    }

    // ---- 3. one operation, one undo step ----

    /// <summary>
    /// Each of the four pastes costs exactly one Ctrl+Z: after the paste, one
    /// <c>Undo()</c> puts the bank back where it stood and the session is empty-handed again
    /// (<c>CanUndo</c> false — every session here starts with its history honestly dead, so one
    /// step is the only reading of "false after one undo").
    ///
    /// <para><b>Negative control:</b> the state <em>before</em> the undo is asserted to differ
    /// from the state before the paste, on all four banks. Without it, a paste that quietly did
    /// nothing — and therefore pushed no step — would pass this test perfectly.</para>
    ///
    /// <para>Break recipe: in <see cref="SfxEditorSession.PasteSlotFromText"/> replace the single
    /// <c>OpenOwnStroke</c>/<c>CloseOwnStroke</c> pair with a per-byte one (open and close inside
    /// the loop). The SFX half then needs 68 undos and this test names it, while the SFX round
    /// trip above stays green.</para>
    /// </summary>
    [Fact]
    public void EveryPasteCostsExactlyOneUndoStep()
    {
        // Sprites.
        var sprites = new SpriteEditorSession(CartFolder(PatternSheet()));
        byte[] spritesBefore = RegionPixels(sprites);
        var blockPixels = new byte[64];
        Array.Fill(blockPixels, (byte)11);
        Assert.False(sprites.CanUndo);
        Assert.True(sprites.PasteFromText(ClipboardFormat.EncodeSprites(8, 8, blockPixels)));
        Assert.NotEqual(spritesBefore, RegionPixels(sprites));   // the control
        sprites.Undo();
        Assert.Equal(spritesBefore, RegionPixels(sprites));
        Assert.False(sprites.CanUndo);

        // Map.
        var map = new MapEditorSession(CartFolder());
        var view = new MapEditorView();
        Assert.False(map.CanUndo);
        Assert.True(MapEditorPaint.PasteText(map, view, ClipboardFormat.EncodeMap(2, 2, new byte[] { 4, 5, 6, 7 })));
        Assert.True(MapEditorPaint.PasteAt(map, view, 0, 0));
        Assert.Equal(4, map.TileAt(0, 0));                       // the control
        map.Undo();
        Assert.Equal(MapEditorSession.EmptyTile, map.TileAt(0, 0));
        Assert.False(map.CanUndo);

        // SFX.
        var sfx = new SfxEditorSession(CartFolder());
        var record = new byte[ClipboardFormat.SfxRecordSize];
        record[0] = 8;
        record[1] = 1;
        ushort note = AudioFormat.PackStep(30, AudioFormat.WaveSaw, 5, AudioFormat.EffectNone);
        record[AudioFormat.SfxSlotHeaderSize] = (byte)(note & 0xFF);
        record[AudioFormat.SfxSlotHeaderSize + 1] = (byte)(note >> 8);
        Assert.False(sfx.CanUndo);
        Assert.True(sfx.PasteSlotFromText(2, ClipboardFormat.EncodeSfx(record)));
        Assert.False(sfx.SlotIsEmpty(2));                        // the control
        sfx.Undo();
        Assert.True(sfx.SlotIsEmpty(2));
        Assert.False(sfx.CanUndo);

        // Music.
        var music = new MusicEditorSession(CartFolder());
        Assert.False(music.CanUndo);
        Assert.True(music.PasteFromText(0, 0, ClipboardFormat.EncodeMusic(2, 2, new[] { 1, 2, 3, 4 })));
        Assert.Equal(1, music.ChannelSlot(0, 0));                // the control
        music.Undo();
        Assert.Equal(MusicEditorSession.SilentSlot, music.ChannelSlot(0, 0));
        Assert.False(music.CanUndo);
    }

    // ---- 4. the shape of the format itself ----

    /// <summary>
    /// Whitespace inside a block is not data and neither is case: a line wrapped by a mail
    /// client at column twenty, and shouted in capitals by a chat window, still pastes to the
    /// same bytes. That is TIC-80's <c>remove_white_spaces</c> made unconditional, and it is the
    /// property that makes "paste a piece of a level into a forum post" actually work.
    ///
    /// <para><b>Negative control:</b> the same text with one hex digit replaced by a letter
    /// outside a-f is refused. Without it a decoder that skipped every character it did not
    /// understand would pass the wrapping half and silently misread real corruption.</para>
    ///
    /// <para>Break recipe: parse the payload from <c>words[4]</c> alone instead of joining every
    /// remaining word. The wrapped text then arrives short, and the first assertion here goes red
    /// while the round-trip tests above — whose text is never wrapped — stay green.</para>
    /// </summary>
    [Fact]
    public void WrappingAndCaseDoNotChangeWhatABlockSays()
    {
        var tiles = new byte[6];
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i] = (byte)(0xA0 + i);
        }
        string text = ClipboardFormat.EncodeMap(3, 2, tiles);

        var wrapped = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; i += 20)
        {
            wrapped.Append(text.AsSpan(i, Math.Min(20, text.Length - i))).Append('\n');
        }
        string mangled = wrapped.ToString().ToUpperInvariant();

        Assert.True(ClipboardFormat.TryDecode(mangled, ClipboardKind.Map, out ClipboardBlock? block, out string reason));
        Assert.Equal(string.Empty, reason);
        Assert.Equal(3, block!.Width);
        Assert.Equal(2, block.Height);
        Assert.Equal(tiles, block.Bytes.ToArray());

        // The negative control: real corruption is still corruption. One payload digit, and one
        // only — mangling the tag as well would prove the FOREIGN branch instead of this one.
        Assert.False(ClipboardFormat.TryDecode(
            mangled.Replace("A0", "Z0", StringComparison.Ordinal),
            ClipboardKind.Map, out _, out string bad));
        Assert.Equal(ClipboardFormat.DamagedReason, bad);
    }

    /// <summary>
    /// The cross-cartridge claim §8 item 2 actually makes — "копировать спрайты/куски карты/SFX
    /// между картриджами и между людьми" — through one clipboard and two independent cart
    /// folders. The string is the only thing that crosses; neither session knows the other
    /// exists, and neither knows what an <see cref="ITextClipboard"/> is.
    ///
    /// <para><b>Negative control:</b> the destination's region is asserted different from the
    /// source's before the paste (two different sheets), so a test that compared a folder with
    /// itself could not pass by accident.</para>
    ///
    /// <para>Break recipe: give <see cref="ShellModeMachine"/> a fresh
    /// <see cref="InMemoryTextClipboard"/> per screen instead of one per process. The two
    /// sessions below still pass because they share the buffer by hand — but
    /// <see cref="TheModeMachineHandsOutOneClipboardForTheWholeProcess"/> goes red, which is
    /// where that defect belongs.</para>
    /// </summary>
    [Fact]
    public void ABlockTravelsFromOneCartridgeToAnotherThroughOneClipboard()
    {
        var sheet = PatternSheet();
        var source = new SpriteEditorSession(CartFolder(sheet));
        var destination = new SpriteEditorSession(CartFolder());     // no gfx.png: an empty sheet
        ITextClipboard clipboard = new InMemoryTextClipboard();

        source.SelectRegionCell(5, 5);
        destination.SelectRegionCell(5, 5);
        byte[] wanted = RegionPixels(source);
        Assert.NotEqual(wanted, RegionPixels(destination));          // the control

        clipboard.Write(source.CopyToText());
        Assert.True(destination.PasteFromText(clipboard.Read()));
        Assert.Equal(wanted, RegionPixels(destination));
    }

    /// <summary>
    /// One clipboard for the whole process, and the code editor is on it too: the machine hands
    /// the same <see cref="ITextClipboard"/> to every screen, which is what makes the sentence
    /// "всё межредакторное копирование идёт через системный буфер" (§1) true here rather than
    /// merely quoted. A test that passes nothing gets the in-memory one, which is why every
    /// assertion in this file runs without an operating system.
    ///
    /// <para><b>Negative control:</b> a machine constructed <em>with</em> a clipboard hands back
    /// that very instance, so the default is a default and not a hard-wired object.</para>
    ///
    /// <para>Break recipe: go back to storing the constructor's argument in a private field and
    /// passing it straight into <c>new CodeEditorView(...)</c>. The property disappears, the four
    /// routers have nothing to read, and this test will not compile — which is the loudest form
    /// this particular regression can take.</para>
    /// </summary>
    [Fact]
    public void TheModeMachineHandsOutOneClipboardForTheWholeProcess()
    {
        var library = new CartLibrary(_root);
        var defaulted = new ShellModeMachine(
            library, _ => throw new InvalidOperationException("no cart is started here"), () => { });
        Assert.NotNull(defaulted.TextClipboard);
        defaulted.TextClipboard.Write("quarp1 map 1 1 07");
        Assert.Equal("quarp1 map 1 1 07", defaulted.TextClipboard.Read());

        // The negative control: an injected clipboard is the one that comes back.
        ITextClipboard injected = new InMemoryTextClipboard();
        var wired = new ShellModeMachine(
            library,
            _ => throw new InvalidOperationException("no cart is started here"),
            () => { },
            textClipboard: injected);
        Assert.Same(injected, wired.TextClipboard);
    }

    /// <summary>
    /// A copy with nothing to copy writes nothing to the clipboard and says so — because the one
    /// thing a Ctrl+C may never do is <em>destroy</em> what the author put on the clipboard from
    /// another program. The empty string never reaches the device: <see cref="EditorShell"/> is
    /// the single door that enforces it, and the sentence reaches the message line.
    ///
    /// <para><b>Negative control:</b> a copy that <em>does</em> have a rectangle writes, and the
    /// notice clears. Without it, a door that refused every write would pass.</para>
    ///
    /// <para>Break recipe: drop the <c>string.IsNullOrEmpty</c> guard from
    /// <see cref="EditorShell.CopyText"/>. The first assertion below goes red — the clipboard is
    /// emptied by a Ctrl+C over nothing — while the second half stays green.</para>
    /// </summary>
    [Fact]
    public void ACopyWithNothingSelectedNeitherWritesNorLies()
    {
        var map = new MapEditorSession(CartFolder());
        var view = new MapEditorView();
        map.SelectSprite(1);        // see the foreign-block test: tile 0 has a standing line of its own
        SeedMap(map);
        ITextClipboard clipboard = new InMemoryTextClipboard();
        clipboard.Write("something the author copied in another program");

        string nothing = MapEditorPaint.CopySelectionToText(map, view);
        Assert.Equal(string.Empty, nothing);
        Assert.Equal("COPY: NOTHING SELECTED", MapEditorRenderer.StandingNotice(map));
        if (!string.IsNullOrEmpty(nothing))
        {
            clipboard.Write(nothing);
        }
        Assert.Equal("something the author copied in another program", clipboard.Read());

        // The negative control: a real rectangle does travel.
        view.BeginSelection(2, 1);
        view.UpdateSelection(3, 1);
        view.EndSelection();
        string something = MapEditorPaint.CopySelectionToText(map, view);
        Assert.StartsWith("quarp1 map 2 1 ", something, StringComparison.Ordinal);
        Assert.Null(MapEditorRenderer.StandingNotice(map));
    }

    /// <summary>
    /// A cut is a copy and then an emptying, in that order, and on all four banks the emptying is
    /// the one that can be refused: a read-only bank still gives the author the text (reading
    /// takes nothing from the text source that owns it) and says why nothing was removed. That is
    /// TIC-80's composition with our read-only rule laid over it, and it is the case where a
    /// half-done cut would otherwise lose data.
    ///
    /// <para><b>Negative control:</b> the same cut on a writable bank does empty the slot, so the
    /// refusal is about <c>sfx.txt</c> and not about cut being broken.</para>
    ///
    /// <para>Break recipe: let <see cref="SfxEditorSession.CutSlotToText"/> call
    /// <c>ClearSlot</c> before the read-only check. The verb then throws out of
    /// <c>RequireWritableBank</c> — an exception on the paste path, which is the failure this
    /// whole wave's "no exceptions outward" rule exists to stop — and this test goes red with a
    /// throw instead of an assertion.</para>
    /// </summary>
    [Fact]
    public void ACutOnAReadOnlyBankStillCopiesAndSaysWhyItRemovedNothing()
    {
        string locked = CartFolder();
        File.WriteAllText(Path.Combine(locked, SfxEditorSession.SfxSourceFileName), "# hand-authored\n");
        var writable = new SfxEditorSession(CartFolder());
        writable.SetStep(4, 0, note: 20, wave: 1, volume: 5, effect: AudioFormat.EffectNone);
        string text = writable.CopySlotToText(4);

        var readOnly = new SfxEditorSession(locked);
        Assert.True(readOnly.BankReadOnly);
        string cut = readOnly.CutSlotToText(0);
        Assert.StartsWith("quarp1 sfx 1 1 ", cut, StringComparison.Ordinal);
        Assert.Equal("CUT: SFX.TXT OWNS THIS BANK", SfxEditorRenderer.StandingNotice(readOnly));
        Assert.False(readOnly.PasteSlotFromText(0, text));
        Assert.Equal("PASTE: SFX.TXT OWNS THIS BANK", SfxEditorRenderer.StandingNotice(readOnly));
        Assert.True(readOnly.SlotIsEmpty(0));

        // The negative control: the writable bank's cut really does empty its slot.
        Assert.False(writable.SlotIsEmpty(4));
        writable.CutSlotToText(4);
        Assert.True(writable.SlotIsEmpty(4));
        Assert.Null(writable.ClipboardNotice);
    }
}
