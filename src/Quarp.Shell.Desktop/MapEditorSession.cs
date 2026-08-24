using Quarp.CartKit;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The map and sprite-flag editing session of one cartridge <b>folder</b> — the headless model
/// behind the tilemap tab (M9 stage 3, wave 3a). It owns the two payloads the console reads,
/// and nothing else: no window, no renderer, no mode.
///
/// <para><b>The writer is not an encoder.</b> <c>map.bin</c> is 18 432 flat tile bytes,
/// row-major, cell <c>(x, y)</c> at <c>y * 256 + x</c> (MAP-FORMAT §1–§2); <c>flags.bin</c> is
/// 256 flag bytes, one per sprite (SPEC-8 §6). Neither has a magic, a version or a header, so
/// the payload <em>is</em> the file: this class reads bytes and writes the same bytes back, and
/// there is no new format code anywhere in it. The only thing a payload can be wrong about is
/// its length, and that is checked on the way in and again on the way out, by the one helper
/// <see cref="RequirePayload"/> — break it and the length tests go red together.</para>
///
/// <para><b>Absent file = zeros, and that is not dirty.</b> A brand-new cart has neither file;
/// it opens as 18 432 + 256 zeros, which is exactly what <see cref="CartSource"/> hands the
/// console for the same folder (SPEC-8 §6: missing optional asset = zeros). Files are created
/// only by the first dirty save, so opening the tilemap tab on a cart that has no map cannot
/// leave one behind.</para>
///
/// <para><b>Dirty is content against the disk, per file</b> — the rule
/// <see cref="SpriteEditorSession"/> established, applied to two banks instead of one. Each bank
/// is compared with a snapshot of what the disk holds; painting a cell and painting it back
/// makes the session honestly clean again, because saving then would change nothing. Two 18 KB
/// compares cost microseconds and cannot drift the way an edit counter under an
/// undo/redo/new-stroke braid can. <see cref="Save"/> writes only the banks that differ, so a
/// flag edit never rewrites <c>map.bin</c> and a map edit never creates <c>flags.bin</c>.</para>
///
/// <para><b>A cart with map.csv has a read-only map.</b> The fact "this cartridge's map" has one
/// owner; while the text source lies in the folder, the owner is the Tiled/`quarp map build`
/// path, and a dirty <c>map.bin</c> write would silently stale it — the same class of lie that
/// <c>CartSource.RequireBuiltAsset</c> already refuses in the other direction. So the map bank
/// refuses edits at the door (<see cref="PaintTile"/> throws) and <see cref="MapReadOnly"/> is a
/// visible property the editor is expected to say out loud, the way the library says
/// "read-only: unpack to a folder to edit" for a .quarp8. The <b>flags</b> bank stays editable:
/// <c>map.csv</c> owns the map, not the sprite flags (MAP-FORMAT §10 — flags are a property of
/// the sprite, not of the cell).</para>
///
/// <para><b>Undo is one stack over both banks, and a step is an operation.</b> A whole snapshot
/// is 18 688 bytes — cheaper than one sprite-editor undo entry — so there is no delta encoding
/// and no per-bank bookkeeping: a step restores the map and the flags together, whichever of
/// them the operation moved. A pencil gesture is one step however many cells it crossed
/// (<see cref="BeginStroke"/>/<see cref="EndStroke"/>); a flag write is one step by itself.</para>
/// </summary>
public sealed class MapEditorSession
{
    /// <summary>The binary the console reads. One name owner: the constructor reads it, <see cref="Save"/> writes it, tests point at it.</summary>
    public const string MapFileName = "map.bin";

    /// <summary>The sprite flags binary, same deal.</summary>
    public const string FlagsFileName = "flags.bin";

    /// <summary>The authoring text source whose presence makes the map read-only (MAP-FORMAT §4).</summary>
    public const string MapSourceFileName = "map.csv";

    /// <summary>Map width in cells — 256, from the one owner of map geometry.</summary>
    public const int MapColumns = CartData.MapWidth;

    /// <summary>Map height in cells — 72.</summary>
    public const int MapRows = CartData.MapHeight;

    /// <summary>Exactly 18 432 bytes (MAP-FORMAT §2). Not a coincidence to be re-derived elsewhere: this is the file size.</summary>
    public const int MapPayloadSize = MapColumns * MapRows;

    /// <summary>
    /// 256 sprites in the sheet (SPEC-8 §3) and one flag byte each, which is why the flags
    /// payload and the tile range are the same 256 and why no byte value is ever an illegal tile.
    /// </summary>
    public const int SpriteCount = VirtualConsole.SpriteCount;

    /// <summary>Exactly 256 bytes (SPEC-8 §6).</summary>
    public const int FlagsPayloadSize = CartData.FlagCount;

    /// <summary>Eight flags per sprite — the width of the byte, and of <c>Fget</c>'s bit index.</summary>
    public const int FlagBits = 8;

    private readonly string _mapPath;
    private readonly string _flagsPath;

    /// <summary>What the disk holds, per bank: the dirty comparison's baseline, replaced bank by bank on save. Never aliases the live arrays.</summary>
    private byte[] _savedMap;
    private byte[] _savedFlags;

    // The live banks. Mutated in place by the tools and replaced wholesale by undo/redo, so
    // nothing may cache a reference to either across a step — every access goes through the field.
    private byte[] _map;
    private byte[] _flags;

    // One stack for both banks. Each entry is a full copy of both (18 688 bytes): a snapshot
    // that shared an untouched bank's array would be corrupted the moment a later operation
    // mutated that array in place, and at this size sharing buys nothing worth that risk.
    private readonly List<Banks> _undo = new();
    private readonly List<Banks> _redo = new();

    /// <summary>Pre-stroke banks while the paint button is down; null between strokes.</summary>
    private Banks? _strokeBackup;
    private bool _strokeChanged;

    /// <summary>Both banks as one undo entry — see the type comment for why a snapshot is whole.</summary>
    private readonly record struct Banks(byte[] Map, byte[] Flags);

    /// <summary>
    /// Opens the map and flags of a cartridge folder (.quarp8 files never get here — the mode
    /// machine refuses them with the read-only line, exactly as for the sprite editor). Both
    /// files are optional: absent means zeros and a clean session. A file of the wrong length
    /// is refused here with <see cref="CartLoadException"/>, the same failure and the same
    /// wording <see cref="CartSource"/> produces for the same file, so the library reports it
    /// the way it reports a broken launch.
    /// </summary>
    public MapEditorSession(string cartFolder)
    {
        ArgumentNullException.ThrowIfNull(cartFolder);
        CartName = Path.GetFileName(Path.TrimEndingDirectorySeparator(cartFolder));
        _mapPath = Path.Combine(cartFolder, MapFileName);
        _flagsPath = Path.Combine(cartFolder, FlagsFileName);
        MapReadOnly = File.Exists(Path.Combine(cartFolder, MapSourceFileName));
        _savedMap = ReadPayload(_mapPath, MapPayloadSize, MapFileName);
        _savedFlags = ReadPayload(_flagsPath, FlagsPayloadSize, FlagsFileName);
        _map = (byte[])_savedMap.Clone();
        _flags = (byte[])_savedFlags.Clone();
    }

    /// <summary>Folder name, for the header — the manifest is deliberately not read, same call as <see cref="SpriteEditorSession"/>.</summary>
    public string CartName { get; }

    /// <summary>
    /// True when <c>map.csv</c> lies beside the map: the map bank is then read-only and the
    /// editor must say so before the author starts drawing. Observable on purpose — a surprise
    /// at save time is the thing this property exists to prevent.
    /// </summary>
    public bool MapReadOnly { get; }

    /// <summary>The live map, row-major 256x72 tile bytes — what the canvas draws and what <see cref="Save"/> writes verbatim.</summary>
    public ReadOnlySpan<byte> Map => _map;

    /// <summary>The live flags, one byte per sprite index.</summary>
    public ReadOnlySpan<byte> Flags => _flags;

    /// <summary>
    /// The sprite the pencil stamps and the flag panel edits — session state, not panel state,
    /// so the tile picker and the flag panel cannot disagree about which sprite is meant
    /// (the stage-3 contract). Tile 0 is emptiness rather than sprite 0 (MAP-FORMAT §2), and
    /// selecting it is how the author erases.
    /// </summary>
    public int SelectedSprite { get; private set; }

    /// <summary>True while the paint button is down — the map half of one undo step is open.</summary>
    public bool StrokeActive => _strokeBackup is not null;

    /// <summary>True when the live map differs from what the disk holds. Per-file: this one never speaks for the flags.</summary>
    public bool IsMapDirty => !_map.AsSpan().SequenceEqual(_savedMap);

    /// <summary>True when the live flags differ from what the disk holds.</summary>
    public bool IsFlagsDirty => !_flags.AsSpan().SequenceEqual(_savedFlags);

    /// <summary>True when either bank differs from the disk — what the exit prompt asks.</summary>
    public bool IsDirty => IsMapDirty || IsFlagsDirty;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Bumped on every change to either bank (paint, flag write, undo, redo) so a renderer can re-upload only when something moved.</summary>
    public int Version { get; private set; }

    /// <summary>Why the last save failed, or null. A save the author believes happened but did not is data loss, so it has to be sayable.</summary>
    public string? SaveError { get; private set; }

    /// <summary>Tile byte at a map cell. Out-of-map coordinates throw: the viewport clamps its own loops, and a silent 0 here would hide a windowing bug.</summary>
    public byte TileAt(int cellX, int cellY)
    {
        ValidateCell(cellX, cellY);
        return _map[cellY * MapColumns + cellX];
    }

    /// <summary>The whole flag byte of one sprite — the flag panel's read door (it takes bytes, it does not own them).</summary>
    public byte FlagsAt(int sprite)
    {
        ValidateSprite(sprite);
        return _flags[sprite];
    }

    /// <summary>One flag bit of one sprite, the shape <c>Fget</c> has (API-8 §3).</summary>
    public bool IsFlagSet(int sprite, int bit)
    {
        ValidateSprite(sprite);
        ValidateBit(bit);
        return (_flags[sprite] & (1 << bit)) != 0;
    }

    /// <summary>
    /// Tile picker click. Throws rather than masks: a value outside 0-255 is a caller bug, and
    /// masking it would stamp a sprite the author never chose. Legal even on a read-only map —
    /// choosing a tile writes nothing.
    /// </summary>
    public void SelectSprite(int sprite)
    {
        ValidateSprite(sprite);
        SelectedSprite = sprite;
    }

    /// <summary>
    /// Right button on the canvas: the pencil takes the tile under the cursor (the sprite
    /// editor's eyedropper, one bank over). Reads only, so a read-only map allows it — being
    /// unable to copy a tile out of digger's map would be a pointless second punishment.
    /// </summary>
    public void PickTile(int cellX, int cellY)
    {
        ValidateCell(cellX, cellY);
        SelectedSprite = _map[cellY * MapColumns + cellX];
    }

    /// <summary>
    /// Paint button pressed on the canvas. The pre-operation banks are snapshotted here and
    /// become the undo entry when the stroke ends — the whole "one gesture = one step"
    /// mechanism: nothing inside the stroke touches the undo stack.
    /// </summary>
    public void BeginStroke()
    {
        if (StrokeActive)
        {
            return;     // A second press without a release (focus-loss glitches) folds into the open stroke.
        }
        _strokeBackup = Snapshot();
        _strokeChanged = false;
    }

    /// <summary>
    /// One pencil sample at a map cell: the selected sprite becomes that cell's tile. The cast
    /// to byte cannot truncate — <see cref="SelectSprite"/> is the only door into
    /// <see cref="SelectedSprite"/> and it refuses anything outside 0-255.
    /// </summary>
    public void PaintTile(int cellX, int cellY)
    {
        if (!StrokeActive)
        {
            throw new InvalidOperationException(
                "PaintTile outside a stroke — the shell must call BeginStroke on the press (SpriteEditorSession.Paint's contract).");
        }
        RequireWritableMap();
        ValidateCell(cellX, cellY);
        int offset = cellY * MapColumns + cellX;
        byte tile = (byte)SelectedSprite;
        if (_map[offset] == tile)
        {
            return;     // Re-stamping the same tile is not a change; see EndStroke for why that matters.
        }
        _map[offset] = tile;
        _strokeChanged = true;
        Version++;
    }

    /// <summary>
    /// Button released: the gesture commits as one undo step — unless it changed nothing, in
    /// which case it never happened (an idle click must not push a no-op snapshot that makes
    /// Ctrl+Z look dead). Safe to call without an open stroke: releases arrive when the press
    /// landed outside the canvas.
    /// </summary>
    public void EndStroke()
    {
        if (_strokeBackup is not Banks backup)
        {
            return;
        }
        _strokeBackup = null;
        if (!_strokeChanged)
        {
            return;
        }
        _undo.Add(backup);
        _redo.Clear();      // The redone future described a map that no longer exists.
    }

    /// <summary>
    /// The flag panel's write door: a whole flag byte of one sprite, one operation, one undo
    /// step. An open map stroke is closed first — otherwise the flag step would be pushed
    /// <em>under</em> the pre-stroke snapshot and undo would replay the two operations in the
    /// wrong order. Writing the value the sprite already has changes nothing and is therefore
    /// not a step and not dirt. Untouched by <see cref="MapReadOnly"/>: <c>map.csv</c> owns the
    /// map, not the flags.
    /// </summary>
    public void SetFlags(int sprite, byte value)
    {
        ValidateSprite(sprite);
        EndStroke();
        if (_flags[sprite] == value)
        {
            return;
        }
        _undo.Add(Snapshot());
        _redo.Clear();
        _flags[sprite] = value;
        Version++;
    }

    /// <summary>One checkbox in the flag panel: flips a single bit of one sprite, through <see cref="SetFlags"/> so there is one write door.</summary>
    public void ToggleFlag(int sprite, int bit)
    {
        ValidateSprite(sprite);
        ValidateBit(bit);
        SetFlags(sprite, (byte)(_flags[sprite] ^ (1 << bit)));
    }

    /// <summary>
    /// Ctrl+Z. Ends an open stroke first (committing it), so an undo mid-drag rolls back a whole
    /// gesture instead of tearing one in half. Whole-bank swaps, no copying: the snapshots
    /// already exist and nothing else holds them. Both banks move together — the step is the
    /// operation, whichever bank it landed in. History lives in the session only: closing the
    /// tab forgets it, and a fresh session opens with Ctrl+Z honestly dead.
    /// </summary>
    public void Undo()
    {
        EndStroke();
        if (_undo.Count == 0)
        {
            return;
        }
        _redo.Add(new Banks(_map, _flags));
        Banks previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _map = previous.Map;
        _flags = previous.Flags;
        Version++;
    }

    /// <summary>Ctrl+Y — the exact mirror of <see cref="Undo"/>.</summary>
    public void Redo()
    {
        EndStroke();
        if (_redo.Count == 0)
        {
            return;
        }
        _undo.Add(new Banks(_map, _flags));
        Banks next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _map = next.Map;
        _flags = next.Flags;
        Version++;
    }

    /// <summary>
    /// Ctrl+S. The clean guard is the save contract's heart: a session whose banks equal the
    /// disk writes <b>nothing</b> — open-and-close leaves both files untouched and, for a cart
    /// that never had them, uncreated, and a repeated Ctrl+S is a no-op. That is what keeps the
    /// pinned demo maps (carts/digger, carts/platformer) byte-identical after the editor has
    /// opened them, and the read-only rule above is the second lock on the same door.
    ///
    /// <para>The two banks are independent files with no ordering constraint between them
    /// (unlike gfx-layers.png/gfx.png, where one is derived from the other), so each is written
    /// only if it is dirty and each updates its own baseline. A disk failure on the second write
    /// therefore leaves the first honestly saved and the second honestly still dirty.</para>
    ///
    /// <para>Disk failures land in <see cref="SaveError"/> instead of throwing, because a full
    /// disk must leave the author their work and a message. A read-only map that is somehow
    /// dirty, or a payload of the wrong length, are contract violations rather than accidents:
    /// those throw.</para>
    /// </summary>
    /// <returns>True when the disk now matches both banks (including "already did"), false when a write failed.</returns>
    public bool Save()
    {
        EndStroke();
        if (!IsDirty)
        {
            SaveError = null;
            return true;
        }
        if (MapReadOnly && IsMapDirty)
        {
            // Unreachable while PaintTile is the only writer of the map bank — that door slams
            // first. Kept as the second gate, because "the map file is owned by map.csv" is a
            // save-time promise and the next wave's tools will be new writers.
            throw new InvalidOperationException(
                $"{CartName}: {MapFileName} is read-only while {MapSourceFileName} is present — "
                + $"the text source owns the map. Remove {MapSourceFileName} to edit the map inside Quarp.");
        }
        try
        {
            if (IsMapDirty)
            {
                RequirePayload(_map, MapPayloadSize, MapFileName);
                File.WriteAllBytes(_mapPath, _map);
                _savedMap = (byte[])_map.Clone();
            }
            if (IsFlagsDirty)
            {
                RequirePayload(_flags, FlagsPayloadSize, FlagsFileName);
                File.WriteAllBytes(_flagsPath, _flags);
                _savedFlags = (byte[])_flags.Clone();
            }
            SaveError = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SaveError = e.Message;
            return false;
        }
    }

    /// <summary>A copy of both banks — the undo entry and nothing else holds these arrays.</summary>
    private Banks Snapshot() => new((byte[])_map.Clone(), (byte[])_flags.Clone());

    private void RequireWritableMap()
    {
        if (MapReadOnly)
        {
            throw new InvalidOperationException(
                $"{CartName}: the map is read-only while {MapSourceFileName} is present — "
                + $"the text source owns it (MAP-FORMAT §4). Remove {MapSourceFileName} to edit the map inside Quarp.");
        }
    }

    private static void ValidateCell(int cellX, int cellY)
    {
        if (cellX is < 0 or >= MapColumns)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cellX), cellX, $"map columns are 0-{MapColumns - 1} (MAP-FORMAT §2); the viewport clamps before calling in.");
        }
        if (cellY is < 0 or >= MapRows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cellY), cellY, $"map rows are 0-{MapRows - 1} (MAP-FORMAT §2); the viewport clamps before calling in.");
        }
    }

    private static void ValidateSprite(int sprite)
    {
        if (sprite is < 0 or >= SpriteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sprite), sprite, $"the sheet holds sprites 0-{SpriteCount - 1} (SPEC-8 §3).");
        }
    }

    private static void ValidateBit(int bit)
    {
        if (bit is < 0 or >= FlagBits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bit), bit, $"a sprite has flags 0-{FlagBits - 1} (SPEC-8 §6).");
        }
    }

    /// <summary>Absent file = zeros (SPEC-8 §6); present file = its bytes, length-checked by the one helper below.</summary>
    private static byte[] ReadPayload(string path, int expectedLength, string name)
    {
        if (!File.Exists(path))
        {
            return new byte[expectedLength];
        }
        byte[] bytes = File.ReadAllBytes(path);
        RequirePayload(bytes, expectedLength, name);
        return bytes;
    }

    /// <summary>
    /// The only thing a flat payload can be wrong about (MAP-FORMAT §3: there are no illegal
    /// tile values, so length is the whole validator). One owner for both directions — the load
    /// and the save — so the check cannot pass on the way in and be missing on the way out. The
    /// message matches <c>CartSource</c>'s for the same file, because the author reads it in
    /// both places.
    /// </summary>
    private static void RequirePayload(byte[] payload, int expectedLength, string name)
    {
        if (payload.Length != expectedLength)
        {
            throw new CartLoadException($"{name}: {payload.Length} bytes, must be exactly {expectedLength}.");
        }
    }
}
