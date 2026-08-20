using System.Security.Cryptography;
using Quarp.CartKit;
using Quarp.Core;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The real layers of wave 2h (ADR-027), proven headless like every session contract: five
/// fixed sheets, tools write the active one, the composite is what shows and what gfx.png
/// gets, gfx-layers.png carries the stack, and the whole undo/dirty/save discipline of waves
/// 2b-2g holds over the stack instead of one sheet.
///
/// <para>Four of the wave's named negative controls live here: break the clean-save guard
/// and <see cref="ACleanSessionCreatesNeitherFile"/> goes red (control а); make the
/// composite ignore upper layers and <see cref="TheCompositeShowsTheTopNonZeroLayer"/> goes
/// red (б); save the active layer instead of the composite into gfx.png and
/// <see cref="SaveWritesTheCompositeToGfxAndTheStackToLayers"/> goes red (в); and the
/// direct question of the wave — what survives closing the editor — is pinned by
/// <see cref="ReopeningShowsTheSavedCompositeAndForgetsUndoHistory"/>.</para>
/// </summary>
public class SpriteEditorLayerTests : IDisposable
{
    private const int SheetSize = CartData.GfxWidth * CartData.GfxHeight;

    private readonly string _root;

    public SpriteEditorLayerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-spred-lyr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string CartFolder(byte[]? gfx = null)
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        if (gfx is not null)
        {
            File.WriteAllBytes(
                Path.Combine(folder, "gfx.png"),
                PngEncoder.EncodeFromPaletteIndices(gfx, CartData.GfxWidth, CartData.GfxHeight));
        }
        return folder;
    }

    private static void Stroke(SpriteEditorSession session, int x, int y)
    {
        session.BeginStroke();
        session.Paint(x, y);
        session.EndStroke();
    }

    private static byte CompositeAt(SpriteEditorSession session, int x, int y) =>
        session.Pixels[y * CartData.GfxWidth + x];

    private static byte LayerAt(SpriteEditorSession session, int layer, int x, int y) =>
        session.LayerPixels(layer)[y * CartData.GfxWidth + x];

    // ---- writing and reading layers ----

    [Fact]
    public void ToolsWriteTheActiveLayerAndOnlyIt()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectLayer(1);
        session.SelectColor(7);

        Stroke(session, 2, 3);

        Assert.Equal(7, LayerAt(session, 1, 2, 3));
        Assert.Equal(0, LayerAt(session, 0, 2, 3));     // the base layer never felt the pencil
        Assert.Equal(7, CompositeAt(session, 2, 3));    // and the composite shows the stroke
    }

    /// <summary>The flatten law (ADR-027): higher covers, 0 is transparent — and erasing the cover reveals the base again.</summary>
    [Fact]
    public void TheCompositeShowsTheTopNonZeroLayer()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(11);                        // "green" on the base
        Stroke(session, 0, 0);
        session.SelectLayer(2);
        session.SelectColor(8);                         // "red" on layer 3
        Stroke(session, 0, 0);

        Assert.Equal(8, CompositeAt(session, 0, 0));    // the cover wins
        Assert.Equal(11, LayerAt(session, 0, 0, 0));    // the base still holds its pixel

        session.SelectColor(0);                         // the eraser is the pencil with 0
        Stroke(session, 0, 0);
        Assert.Equal(11, CompositeAt(session, 0, 0));   // 0 is transparent — the base shows through
    }

    /// <summary>The card's letter: the eyedropper (and the wand's flood, which shares the read) answers the ACTIVE layer, not the composite.</summary>
    [Fact]
    public void TheEyedropperReadsTheActiveLayerNotTheComposite()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(5);
        Stroke(session, 0, 0);                          // base holds 5
        session.SelectLayer(1);                         // layer 2 is empty here

        session.PickColor(0, 0);

        Assert.Equal(0, session.CurrentColor);          // what I drew HERE is nothing
    }

    // ---- undo over the stack ----

    /// <summary>
    /// One snapshot = the whole stack: undo rolls back the last operation wherever it landed,
    /// regardless of which layer the author is looking at now.
    /// </summary>
    [Fact]
    public void UndoRestoresTheWholeStackAcrossLayers()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(3);
        Stroke(session, 0, 0);                          // step 1 on the base
        session.SelectLayer(1);
        session.SelectColor(4);
        Stroke(session, 1, 1);                          // step 2 on layer 2
        session.SelectLayer(4);                         // looking somewhere else entirely

        session.Undo();
        Assert.Equal(0, LayerAt(session, 1, 1, 1));     // step 2 gone
        Assert.Equal(3, LayerAt(session, 0, 0, 0));     // step 1 stands

        session.Undo();
        Assert.Equal(0, LayerAt(session, 0, 0, 0));     // step 1 gone too
        Assert.False(session.IsDirty);

        session.Redo();
        session.Redo();
        Assert.Equal(3, LayerAt(session, 0, 0, 0));
        Assert.Equal(4, LayerAt(session, 1, 1, 1));
    }

    /// <summary>Switching layers is not an operation: no undo step, no dirt — and it clamps like every navigation verb.</summary>
    [Fact]
    public void SelectingALayerIsFreeAndClamped()
    {
        var session = new SpriteEditorSession(CartFolder());

        session.SelectLayer(3);
        Assert.Equal(3, session.ActiveLayerIndex);
        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);

        session.SelectLayer(99);
        Assert.Equal(SpriteEditorSession.LayerCount - 1, session.ActiveLayerIndex);
        session.SelectLayer(-5);
        Assert.Equal(0, session.ActiveLayerIndex);
    }

    /// <summary>
    /// A committed selection survives a layer switch (Photoshop's convention — the mask marks
    /// positions and stays visible under the select tool), while an open float parks on the
    /// layer it grabbed from, exactly like a tool switch parks it.
    /// </summary>
    [Fact]
    public void ACommittedSelectionSurvivesTheLayerSwitchAndAFloatParksFirst()
    {
        var session = new SpriteEditorSession(CartFolder());
        session.SelectColor(6);
        Stroke(session, 1, 1);
        session.SelectTool(SpriteEditorTool.Select);
        session.BeginSelect(1, 1);
        session.CommitSelect();
        Assert.True(session.HasSelection);

        session.SelectLayer(2);
        Assert.True(session.HasSelection);              // the mask outlives the switch

        session.SelectLayer(0);                         // back to the pixel's home
        session.BeginSelect(1, 1);                      // grab...
        session.UpdateSelect(3, 3);                     // ...drag the float
        Assert.True(session.MoveActive);
        session.SelectLayer(2);                         // switch mid-float
        Assert.False(session.MoveActive);               // parked, not floating
        Assert.Equal(6, LayerAt(session, 0, 3, 3));     // and parked on the OLD layer
    }

    // ---- files on disk ----

    /// <summary>Control (а): open-and-save on an untouched cart creates neither gfx.png nor gfx-layers.png.</summary>
    [Fact]
    public void ACleanSessionCreatesNeitherFile()
    {
        string folder = CartFolder();
        var session = new SpriteEditorSession(folder);

        Assert.True(session.Save());

        Assert.False(File.Exists(Path.Combine(folder, "gfx.png")));
        Assert.False(File.Exists(Path.Combine(folder, SpriteEditorSession.LayersFileName)));
    }

    /// <summary>Control (в)'s home: gfx.png gets the COMPOSITE, gfx-layers.png the stack with the base as the top strip.</summary>
    [Fact]
    public void SaveWritesTheCompositeToGfxAndTheStackToLayers()
    {
        string folder = CartFolder();
        var session = new SpriteEditorSession(folder);
        session.SelectColor(11);
        Stroke(session, 0, 0);                          // base: green at (0,0)
        Stroke(session, 5, 5);                          // base: green at (5,5) — no cover here
        session.SelectLayer(2);
        session.SelectColor(8);
        Stroke(session, 0, 0);                          // layer 3: red over the green

        Assert.True(session.Save());

        byte[] gfx = PngDecoder.DecodeToPaletteIndices(
            File.ReadAllBytes(Path.Combine(folder, "gfx.png")),
            CartData.GfxWidth, CartData.GfxHeight, "gfx.png");
        Assert.Equal(8, gfx[0]);                                    // the composite, not the active layer:
        Assert.Equal(11, gfx[5 * CartData.GfxWidth + 5]);           // the uncovered base pixel is here too

        byte[] stacked = PngDecoder.DecodeToPaletteIndices(
            File.ReadAllBytes(Path.Combine(folder, SpriteEditorSession.LayersFileName)),
            CartData.GfxWidth, CartData.GfxHeight * SpriteEditorSession.LayerCount,
            SpriteEditorSession.LayersFileName);
        Assert.Equal(11, stacked[0]);                               // strip 0 = the base layer
        Assert.Equal(8, stacked[2 * SheetSize]);                    // strip 2 = layer 3
        Assert.Equal(0, stacked[1 * SheetSize]);                    // untouched layers are zeros
        Assert.False(session.IsDirty);
    }

    /// <summary>Determinism through the session: the same stack state saves to byte-identical files, every time.</summary>
    [Fact]
    public void SavingTheSameStateAgainProducesTheSameBytes()
    {
        string folder = CartFolder();
        string gfxPath = Path.Combine(folder, "gfx.png");
        string layersPath = Path.Combine(folder, SpriteEditorSession.LayersFileName);
        var session = new SpriteEditorSession(folder);
        session.SelectColor(9);
        Stroke(session, 4, 4);
        Assert.True(session.Save());
        byte[] gfxFirst = SHA256.HashData(File.ReadAllBytes(gfxPath));
        byte[] layersFirst = SHA256.HashData(File.ReadAllBytes(layersPath));

        session.Undo();
        Assert.True(session.Save());                    // a genuinely different state hits the disk...
        Assert.False(gfxFirst.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(gfxPath))));

        session.Redo();
        Assert.True(session.Save());                    // ...and the original state returns byte-for-byte

        Assert.True(gfxFirst.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(gfxPath))));
        Assert.True(layersFirst.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(layersPath))));
    }

    /// <summary>An edit buried under an opaque cover is honestly dirty: gfx.png comes out identical, but the layers file must change.</summary>
    [Fact]
    public void AChangeHiddenByACoverIsStillDirtyAndStillSaved()
    {
        string folder = CartFolder();
        var session = new SpriteEditorSession(folder);
        session.SelectLayer(1);
        session.SelectColor(5);
        Stroke(session, 0, 0);                          // the cover
        Assert.True(session.Save());
        string gfxPath = Path.Combine(folder, "gfx.png");
        byte[] gfxBefore = SHA256.HashData(File.ReadAllBytes(gfxPath));

        session.SelectLayer(0);
        session.SelectColor(7);
        Stroke(session, 0, 0);                          // buried under the cover
        Assert.True(session.IsDirty);                   // composite unchanged — the stack is not
        Assert.True(session.Save());

        Assert.True(gfxBefore.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(gfxPath))));
        var reopened = new SpriteEditorSession(folder);
        Assert.Equal(7, LayerAt(reopened, 0, 0, 0));    // the buried pixel round-tripped through the layers file
    }

    // ---- loading ----

    [Fact]
    public void WithoutALayersFileTheBaseLoadsFromGfxAndTheRestAreEmpty()
    {
        var gfx = new byte[SheetSize];
        gfx[7] = 13;
        var session = new SpriteEditorSession(CartFolder(gfx));

        Assert.Equal(13, session.LayerPixels(0)[7]);
        for (int i = 1; i < SpriteEditorSession.LayerCount; i++)
        {
            Assert.True(session.LayerPixels(i).IndexOfAnyExcept((byte)0) < 0);
        }
        Assert.False(session.GfxOutOfSyncOnDisk);
        Assert.False(session.IsDirty);
    }

    /// <summary>
    /// The wave's direct question, pinned. Red on layer 3 over green on layer 1, saved,
    /// closed, reopened: gfx.png holds the red composite, the canvas shows red again (the
    /// stack reloads from gfx-layers.png — the green survives underneath), and Ctrl+Z does
    /// NOTHING, because undo history is session memory and dies with the editor — replaying
    /// stale history against a disk someone else may have touched would be worse.
    /// </summary>
    [Fact]
    public void ReopeningShowsTheSavedCompositeAndForgetsUndoHistory()
    {
        string folder = CartFolder();
        var first = new SpriteEditorSession(folder);
        first.SelectColor(11);
        Stroke(first, 0, 0);                            // green on the base
        first.SelectLayer(2);
        first.SelectColor(8);
        Stroke(first, 0, 0);                            // red on layer 3, same pixel
        Assert.True(first.Save());

        var reopened = new SpriteEditorSession(folder);
        Assert.Equal(8, CompositeAt(reopened, 0, 0));   // the canvas shows the red composite
        Assert.Equal(11, LayerAt(reopened, 0, 0, 0));   // the green is still under it
        Assert.False(reopened.CanUndo);                 // history did not survive the reopen

        int version = reopened.Version;
        reopened.Undo();                                // the trap's Ctrl+Z

        Assert.Equal(version, reopened.Version);        // a visible no-op:
        Assert.Equal(8, CompositeAt(reopened, 0, 0));   // nothing moved,
        Assert.False(reopened.IsDirty);                 // nothing got dirty
    }

    /// <summary>
    /// gfx.png edited outside while gfx-layers.png stands (Aseprite is a first-class path):
    /// the stack wins (ADR-027 — the layers file is the source), the session opens clean,
    /// and the divergence is flagged so the renderer can announce the coming overwrite.
    /// Saving reconciles the disk and clears the flag.
    /// </summary>
    [Fact]
    public void AForeignGfxEditIsFlaggedAndTheStackWins()
    {
        string folder = CartFolder();
        var author = new SpriteEditorSession(folder);
        author.SelectColor(5);
        Stroke(author, 0, 0);
        Assert.True(author.Save());

        var foreign = new byte[SheetSize];              // "Aseprite" rewrites gfx.png wholesale
        foreign[0] = 9;
        File.WriteAllBytes(
            Path.Combine(folder, "gfx.png"),
            PngEncoder.EncodeFromPaletteIndices(foreign, CartData.GfxWidth, CartData.GfxHeight));

        var session = new SpriteEditorSession(folder);
        Assert.True(session.GfxOutOfSyncOnDisk);
        Assert.Equal(5, CompositeAt(session, 0, 0));    // the stack's pixel, not the foreign one
        Assert.False(session.IsDirty);                  // clean: closing now touches nothing

        session.SelectColor(2);
        Stroke(session, 3, 3);
        Assert.True(session.Save());
        Assert.False(session.GfxOutOfSyncOnDisk);       // the save reconciled both files
        byte[] gfx = PngDecoder.DecodeToPaletteIndices(
            File.ReadAllBytes(Path.Combine(folder, "gfx.png")),
            CartData.GfxWidth, CartData.GfxHeight, "gfx.png");
        Assert.Equal(5, gfx[0]);                        // the announced overwrite happened
    }

    [Fact]
    public void ACorruptLayersFileThrowsTheSameExceptionALoadWould()
    {
        string folder = CartFolder();
        File.WriteAllText(Path.Combine(folder, SpriteEditorSession.LayersFileName), "not a png");

        Assert.Throws<CartLoadException>(() => new SpriteEditorSession(folder));
    }
}
