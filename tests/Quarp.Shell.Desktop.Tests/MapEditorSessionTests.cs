using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The map editor's model contract, proven headless (M9 stage 3, wave 3a; the flag bank moved
/// out to <see cref="SpriteEditorSession"/> in wave 3b-1): the one payload, the dirty rule, the
/// save contract, the read-only map of a cart that still has <c>map.csv</c>, and one undo stack
/// over it — driven through <see cref="MapEditorSession"/> alone, the way
/// <see cref="SpriteEditorSessionTests"/> drives the sprite editor.
///
/// <para>The stage's named negative-control targets live here: (a) a clean session writes
/// nothing — proven twice, by an empty directory listing and by a read-only file whose write
/// <em>attempt</em> would surface in <see cref="MapEditorSession.SaveError"/>; (b) the writer
/// is the payload and nothing else — a dirty save of a real demo map differs from the original
/// in exactly the one byte that was painted; (c) the length check refuses a truncated file.</para>
///
/// <para>Every test works on a copy in a temp folder. <c>carts/</c> holds pinned goldens
/// (carts/demo-goldens.tsv) and nothing here may write into it — the demo carts are only ever
/// <em>read</em>, to get real bytes to copy.</para>
/// </summary>
public class MapEditorSessionTests : IDisposable
{
    private readonly string _root;

    public MapEditorSessionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-maped-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // Several tests deliberately leave a payload read-only (the no-write proof); Delete
        // would throw on it, so attributes are normalized first — the sprite editor's pattern.
        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(_root, recursive: true);
    }

    // ---- helpers ----

    /// <summary>An empty cart folder, optionally seeded with a map payload and/or a map.csv.</summary>
    private string CartFolder(byte[]? map = null, bool mapSource = false)
    {
        string folder = Path.Combine(_root, "cart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        if (map is not null)
        {
            File.WriteAllBytes(Path.Combine(folder, MapEditorSession.MapFileName), map);
        }
        if (mapSource)
        {
            File.WriteAllText(Path.Combine(folder, MapEditorSession.MapSourceFileName), "# a hand-authored map\n");
        }
        return folder;
    }

    /// <summary>Every tile value appears and no row equals another — corruption anywhere shows up somewhere.</summary>
    private static byte[] PatternMap()
    {
        var map = new byte[MapEditorSession.MapPayloadSize];
        for (int i = 0; i < map.Length; i++)
        {
            map[i] = (byte)(i * 7 + i / MapEditorSession.MapColumns);
        }
        return map;
    }

    /// <summary>Walks up from the test bin folder to the repo root, same as PngEncoderTests/SnakeCartTests.</summary>
    private static string CartsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "carts");
            if (File.Exists(Path.Combine(candidate, "snake", "manifest.json")))
            {
                return candidate;
            }
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("carts/ not found above the test directory");
    }

    /// <summary>
    /// A copy of a demo cart's root files in the temp tree. Only the root files matter to this
    /// session (map.bin, map.csv), and copying only them keeps the demo's replays and sources —
    /// the pinned goldens — where they belong: read, never written.
    /// </summary>
    private string CopyDemoCart(string name)
    {
        string source = Path.Combine(CartsRoot(), name);
        string folder = Path.Combine(_root, name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
            File.SetAttributes(Path.Combine(folder, Path.GetFileName(file)), FileAttributes.Normal);
        }
        return folder;
    }

    /// <summary>One complete pencil gesture: press, samples, release.</summary>
    private static void Stroke(MapEditorSession session, params (int X, int Y)[] cells)
    {
        session.BeginStroke();
        foreach ((int x, int y) in cells)
        {
            session.PaintTile(x, y);
        }
        session.EndStroke();
    }

    // ---- the demo maps: opened, never written ----

    /// <summary>
    /// The stage's headline guarantee on the two carts that actually have a map. Both ship
    /// map.csv, so the map bank is read-only and the proof is "the session does not write",
    /// not "the session wrote the same bytes": the file is marked read-only before the session
    /// opens, so any write attempt would fail loudly and land in SaveError.
    /// </summary>
    [Theory]
    [InlineData("digger")]
    [InlineData("platformer")]
    public void ADemoCartMapIsUntouchedByAnOpenAndSave(string cart)
    {
        byte[] original = File.ReadAllBytes(Path.Combine(CartsRoot(), cart, MapEditorSession.MapFileName));
        Assert.Equal(MapEditorSession.MapPayloadSize, original.Length);
        string folder = CopyDemoCart(cart);
        string mapPath = Path.Combine(folder, MapEditorSession.MapFileName);
        DateTime before = File.GetLastWriteTimeUtc(mapPath);
        File.SetAttributes(mapPath, FileAttributes.ReadOnly);

        var session = new MapEditorSession(folder);
        Assert.True(session.Map.SequenceEqual(original));    // loaded byte for byte
        Assert.True(session.MapReadOnly);                    // ...and honestly labelled before any editing
        Assert.False(session.IsDirty);

        Assert.True(session.Save());

        Assert.Null(session.SaveError);                      // "no error" means "no write was attempted"
        Assert.True(File.ReadAllBytes(mapPath).SequenceEqual(original));
        Assert.Equal(before, File.GetLastWriteTimeUtc(mapPath));
    }

    /// <summary>
    /// The writer is not an encoder: what lands on disk is the payload, offset for offset. Run
    /// on a real demo map (copied without its map.csv, so the map is editable) — a dirty save
    /// must differ from the original in exactly the one byte the pencil moved, at exactly
    /// <c>y * 256 + x</c> (MAP-FORMAT §2).
    /// </summary>
    [Fact]
    public void ADirtySaveWritesThePayloadAndNothingElse()
    {
        byte[] original = File.ReadAllBytes(Path.Combine(CartsRoot(), "digger", MapEditorSession.MapFileName));
        string folder = CartFolder(map: original);           // no map.csv: this copy is editable
        string mapPath = Path.Combine(folder, MapEditorSession.MapFileName);
        var session = new MapEditorSession(folder);
        Assert.False(session.MapReadOnly);
        const int cellX = 130;
        const int cellY = 41;
        int offset = cellY * MapEditorSession.MapColumns + cellX;
        byte painted = (byte)(original[offset] ^ 0xFF);      // guaranteed different from what is there
        session.SelectSprite(painted);

        Stroke(session, (cellX, cellY));
        Assert.True(session.Save());

        byte[] written = File.ReadAllBytes(mapPath);
        Assert.Equal(MapEditorSession.MapPayloadSize, written.Length);   // no header, no magic, no version
        Assert.True(session.Map.SequenceEqual(written));
        byte[] expected = (byte[])original.Clone();
        expected[offset] = painted;
        Assert.True(written.AsSpan().SequenceEqual(expected));           // exactly one byte moved, at y * 256 + x
        Assert.False(session.IsDirty);
    }

    // ---- read-only map (map.csv present) ----

    [Fact]
    public void AMapCsvBesideTheMapMakesTheMapReadOnlyAtTheDoor()
    {
        string folder = CartFolder(map: PatternMap(), mapSource: true);
        var session = new MapEditorSession(folder);
        session.SelectSprite(9);
        session.BeginStroke();

        var e = Assert.Throws<InvalidOperationException>(() => session.PaintTile(0, 0));

        Assert.Contains(MapEditorSession.MapSourceFileName, e.Message, StringComparison.Ordinal);
        Assert.False(session.IsMapDirty);       // a refused edit must not half-apply
        Assert.False(session.CanUndo);
    }

    // ---- absent file, clean session ----

    [Fact]
    public void ACartWithoutMapOpensAsZerosAndIsClean()
    {
        var session = new MapEditorSession(CartFolder());

        Assert.Equal(MapEditorSession.MapPayloadSize, session.Map.Length);
        Assert.True(session.Map.IndexOfAnyExcept((byte)0) < 0);
        Assert.False(session.IsDirty);
        Assert.False(session.IsMapDirty);
        Assert.False(session.MapReadOnly);
        Assert.False(session.CanUndo);
    }

    /// <summary>The clean-session guarantee, absent-file half: opening and saving an untouched cart creates nothing at all.</summary>
    [Fact]
    public void ACleanSessionCreatesNoFiles()
    {
        string folder = CartFolder();
        var session = new MapEditorSession(folder);

        Assert.True(session.Save());

        Assert.Empty(Directory.GetFileSystemEntries(folder));
    }

    /// <summary>
    /// The clean-session guarantee, existing-file half — the map payload present and marked
    /// read-only: if the guard were gone, the write would fail against the attribute and show up
    /// in SaveError, so "no error and same mtime" means "nothing was written", not "the same
    /// bytes were written".
    /// </summary>
    [Fact]
    public void ACleanSessionNeverTouchesAnExistingMapFile()
    {
        string folder = CartFolder(map: PatternMap());
        string mapPath = Path.Combine(folder, MapEditorSession.MapFileName);
        DateTime mapBefore = File.GetLastWriteTimeUtc(mapPath);
        File.SetAttributes(mapPath, FileAttributes.ReadOnly);
        var session = new MapEditorSession(folder);

        Assert.True(session.Save());

        Assert.Null(session.SaveError);
        Assert.Equal(mapBefore, File.GetLastWriteTimeUtc(mapPath));
    }

    /// <summary>Second Ctrl+S without new edits: the bank equals the disk again, so nothing is attempted.</summary>
    [Fact]
    public void ARepeatedSaveWithoutNewEditsIsANoOp()
    {
        string folder = CartFolder();
        var session = new MapEditorSession(folder);
        session.SelectSprite(4);
        Stroke(session, (2, 5));
        Assert.True(session.Save());
        File.SetAttributes(Path.Combine(folder, MapEditorSession.MapFileName), FileAttributes.ReadOnly);

        Assert.True(session.Save());

        Assert.Null(session.SaveError);
    }

    [Fact]
    public void ADirtyMapWritesOnlyMapBinAndExactlyItsLength()
    {
        string folder = CartFolder();
        var session = new MapEditorSession(folder);
        session.SelectSprite(17);

        Stroke(session, (0, 0));
        Assert.True(session.IsMapDirty);
        Assert.True(session.Save());

        string[] written = Directory.GetFiles(folder);
        Assert.Single(written);
        Assert.Equal(MapEditorSession.MapFileName, Path.GetFileName(written[0]));
        Assert.Equal(MapEditorSession.MapPayloadSize, new FileInfo(written[0]).Length);
        Assert.False(session.IsDirty);
    }

    /// <summary>Dirty is content against the disk, not a history of edits.</summary>
    [Fact]
    public void PaintingACellBackToItsLoadedTileIsCleanAgain()
    {
        byte[] map = PatternMap();
        var session = new MapEditorSession(CartFolder(map: map));
        byte loaded = session.TileAt(5, 5);
        session.SelectSprite((byte)(loaded ^ 0x3C));

        Stroke(session, (5, 5));
        Assert.True(session.IsMapDirty);

        session.SelectSprite(loaded);
        Stroke(session, (5, 5));

        Assert.Equal(loaded, session.TileAt(5, 5));
        Assert.False(session.IsMapDirty);       // saving now would change nothing, so the session is honestly clean
        Assert.True(session.CanUndo);           // ...even though two real operations happened
    }

    // ---- undo / redo: one stack, step = operation ----

    [Fact]
    public void OneStrokeIsOneUndoStepHoweverManyCellsItPainted()
    {
        var session = new MapEditorSession(CartFolder());
        session.SelectSprite(6);

        Stroke(session, (0, 0), (1, 0), (2, 0), (2, 1));     // four cells, one gesture

        session.Undo();
        Assert.True(session.Map.IndexOfAnyExcept((byte)0) < 0);   // fully back in ONE step
        Assert.False(session.CanUndo);                            // and there is no second step
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void UndoAndRedoWalkTwoStrokesOneOperationAtATime()
    {
        var session = new MapEditorSession(CartFolder());
        session.SelectSprite(3);
        Stroke(session, (7, 7));                 // step 1
        session.SelectSprite(9);
        Stroke(session, (2, 2));                 // step 2

        session.Undo();
        Assert.Equal(0, session.TileAt(2, 2));
        Assert.Equal(3, session.TileAt(7, 7));   // the first step is untouched by the second step's undo

        session.Undo();
        Assert.Equal(0, session.TileAt(7, 7));
        Assert.False(session.CanUndo);

        session.Redo();
        Assert.Equal(3, session.TileAt(7, 7));
        Assert.Equal(0, session.TileAt(2, 2));

        session.Redo();
        Assert.Equal(9, session.TileAt(2, 2));
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void ANewOperationClearsTheRedoFuture()
    {
        var session = new MapEditorSession(CartFolder());
        session.SelectSprite(2);
        Stroke(session, (0, 0));
        session.Undo();
        Assert.True(session.CanRedo);

        session.SelectSprite(1);
        Stroke(session, (4, 4));                // history branched; the old future is gone

        Assert.False(session.CanRedo);
    }

    [Fact]
    public void AStrokeThatChangesNothingIsInvisibleToUndoAndDirt()
    {
        var session = new MapEditorSession(CartFolder());
        session.SelectSprite(0);                // stamping tile 0 over tile 0

        Stroke(session, (0, 0), (4, 4));

        Assert.False(session.CanUndo);          // an idle click must not make Ctrl+Z look dead
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void UndoMidStrokeCommitsTheGestureAndRollsItBackWhole()
    {
        var session = new MapEditorSession(CartFolder());
        session.SelectSprite(8);
        session.BeginStroke();
        session.PaintTile(0, 0);
        session.PaintTile(3, 3);

        session.Undo();                          // no EndStroke — Ctrl+Z arrived mid-drag

        Assert.True(session.Map.IndexOfAnyExcept((byte)0) < 0);
        Assert.False(session.StrokeActive);
        Assert.True(session.CanRedo);
    }

    // ---- the length check (the stage's third negative control) ----

    [Fact]
    public void AMapBinOfTheWrongLengthIsRefused()
    {
        string folder = CartFolder(map: new byte[MapEditorSession.MapPayloadSize - 1]);

        var e = Assert.Throws<CartLoadException>(() => new MapEditorSession(folder));

        Assert.Contains(MapEditorSession.MapFileName, e.Message, StringComparison.Ordinal);
        Assert.Contains("18432", e.Message, StringComparison.Ordinal);
    }

    // ---- tile picker, eyedropper, coordinate guards ----

    [Fact]
    public void TheEyedropperTakesTheTileUnderTheCursorEvenOnAReadOnlyMap()
    {
        byte[] map = PatternMap();
        string folder = CartFolder(map: map, mapSource: true);
        var session = new MapEditorSession(folder);

        session.PickTile(9, 2);

        Assert.Equal(map[2 * MapEditorSession.MapColumns + 9], session.SelectedSprite);
    }

    [Fact]
    public void SpriteNumbersOutsideTheSheetAreRejectedAtTheDoor()
    {
        var session = new MapEditorSession(CartFolder());
        session.SelectSprite(MapEditorSession.SpriteCount - 1);      // the last legal one

        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectSprite(MapEditorSession.SpriteCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectSprite(-1));
        Assert.Equal(MapEditorSession.SpriteCount - 1, session.SelectedSprite);   // a rejected value must not half-apply
    }

    [Fact]
    public void CellsOutsideTheMapAreRejected()
    {
        var session = new MapEditorSession(CartFolder());

        Assert.Throws<ArgumentOutOfRangeException>(() => session.TileAt(MapEditorSession.MapColumns, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.TileAt(0, MapEditorSession.MapRows));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.PickTile(-1, 0));
        session.BeginStroke();
        Assert.Throws<ArgumentOutOfRangeException>(() => session.PaintTile(0, -1));
    }

    [Fact]
    public void PaintWithoutAStrokeThrows()
    {
        var session = new MapEditorSession(CartFolder());

        Assert.Throws<InvalidOperationException>(() => session.PaintTile(0, 0));
    }

    // ---- a failed save keeps the work ----

    [Fact]
    public void AFailedSaveReportsAndKeepsTheEdits()
    {
        string folder = CartFolder(map: PatternMap());
        string mapPath = Path.Combine(folder, MapEditorSession.MapFileName);
        File.SetAttributes(mapPath, FileAttributes.ReadOnly);
        var session = new MapEditorSession(folder);
        session.SelectSprite((byte)(session.TileAt(0, 0) ^ 0xFF));
        Stroke(session, (0, 0));

        Assert.False(session.Save());

        Assert.NotNull(session.SaveError);
        Assert.True(session.IsMapDirty);        // the author's work is still here, still saveable

        File.SetAttributes(mapPath, FileAttributes.Normal);
        Assert.True(session.Save());
        Assert.Null(session.SaveError);
        Assert.False(session.IsDirty);
    }
}
