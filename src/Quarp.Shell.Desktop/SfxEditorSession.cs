using Quarp.CartKit;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The sound editing session of one cartridge <b>folder</b> — the headless model behind the
/// SOUND tab. It owns the one payload the console's APU reads for effects, <c>sfx.bin</c>, and
/// nothing else: no window, no renderer, no mode, no speaker. The fourth member of the family
/// <see cref="SpriteEditorSession"/>, <see cref="MapEditorSession"/> and
/// <see cref="CodeEditorSession"/> started, and it repeats their contract deliberately —
/// dirty-against-disk, a save that writes only what changed, a clean session that writes
/// nothing at all, and a payload whose length is checked on the way in and again on the way out.
///
/// <para><b>The writer is not an encoder.</b> The bit layout of a step word, the four bytes of a
/// slot header, the offsets, the magic, the version and every rule a bank has to obey belong to
/// <see cref="AudioFormat"/> in <c>Quarp.CartKit</c> (docs/AUDIO-FORMAT.md §2, §3, §5) — the one
/// owner of the format. This class holds 4352 bytes and calls into that owner for every read of
/// a field, every write of a field, the parse on load and the wrap on save. There is no second
/// copy of the format here, and the day the format grows a field this file will not have to
/// know.</para>
///
/// <para><b>Absent file = silence, not an error</b> (AUDIO-FORMAT §1). A cart with no
/// <c>sfx.bin</c> opens as an all-zero payload, which the format defines as 64 empty slots —
/// exactly what <see cref="CartSource"/> hands the console for the same folder. The file is
/// created only by the first dirty save, so opening the SOUND tab on a cart that has no sound
/// cannot leave one behind.</para>
///
/// <para><b>A cart with sfx.txt has a read-only bank — the map's verdict, applied to audio.</b>
/// The fact "this cartridge's effects" has one owner; while the text source lies in the folder
/// the owner is the <c>quarp audio build</c> path, and a dirty <c>sfx.bin</c> write would
/// silently stale it — the same class of lie <c>CartSource.RequireBuiltAsset</c> already refuses
/// in the other direction (an <c>sfx.txt</c> with no <c>sfx.bin</c> is a load error, not
/// silence). This is the same fork <c>map.csv</c> put in front of
/// <see cref="MapEditorSession.MapReadOnly"/>, and the M9 work order settled it there: the text
/// source wins, the editor says so out loud <em>before</em> the author starts typing notes
/// rather than surprising them at save time, and removing <c>sfx.txt</c> is the deliberate act
/// that hands the bank back. So this bank refuses edits at the door
/// (<see cref="RequireWritableBank"/>) and <see cref="BankReadOnly"/> is a visible property the
/// screen is expected to print. Note which carts that covers: snake carries an <c>sfx.txt</c>,
/// so its bank is read-only inside Quarp, and the demo goldens gain a second lock.</para>
///
/// <para><b>Undo is one stack over the bank, and a step is an operation.</b> A whole snapshot is
/// 4352 bytes — a quarter of one map-editor undo entry, and the sprite editor's entries are
/// larger still — so there is no delta encoding and no per-slot bookkeeping to get wrong: a step
/// restores the whole bank, which is also the only granularity under which "change the length"
/// (one header byte plus up to 32 zeroed words) is honestly <em>one</em> action. A pointer
/// gesture across the pitch grid is likewise one step however many columns it crossed
/// (<see cref="BeginStroke"/>/<see cref="EndStroke"/>), exactly as a pencil stroke is on the map;
/// a keyboard action that arrives outside a gesture opens and closes its own stroke, so every
/// public verb below costs exactly one Ctrl+Z whichever hand performed it.</para>
///
/// <para><b>The payload is canonical at every instant, not merely at save time.</b>
/// AUDIO-FORMAT §5 forbids a step beyond <c>length</c> that is non-zero, a rest that is anything
/// but the zero word, a speed of 0 on a slot that plays, and a loop outside
/// <c>start &lt; end &lt;= length</c>. Those rules are enforced by the mutators here rather than
/// by a pass before writing, because the very same bytes are handed to a live
/// <c>Quarp.Core.Audio.Apu</c> for the preview: a bank that is only legal at save time would be
/// a bank the author cannot hear.</para>
/// </summary>
public sealed class SfxEditorSession
{
    /// <summary>The binary the console reads. One name owner: the constructor reads it, <see cref="Save"/> writes it, tests point at it.</summary>
    public const string SfxFileName = "sfx.bin";

    /// <summary>The authoring text source whose presence makes the bank read-only (AUDIO-FORMAT §6).</summary>
    public const string SfxSourceFileName = "sfx.txt";

    /// <summary>Slots in the bank — 64, from the one owner of the format.</summary>
    public const int SlotCount = AudioFormat.SfxSlotCount;

    /// <summary>Steps in a slot — 32.</summary>
    public const int StepCount = AudioFormat.SfxStepCount;

    /// <summary>Exactly 4352 bytes (AUDIO-FORMAT §2), borrowed rather than re-derived.</summary>
    public const int PayloadSize = AudioFormat.SfxPayloadSize;

    /// <summary>Highest note index: 63, i.e. D#7. Six bits, and not one more.</summary>
    public const int MaxNote = AudioFormat.MaxNote;

    /// <summary>Waveforms profile 8 defines: 6.</summary>
    public const int WaveCount = AudioFormat.WaveCount;

    /// <summary>Loudest step volume: 7. Volume 0 is a rest and has exactly one spelling, the zero word.</summary>
    public const int MaxVolume = AudioFormat.MaxVolume;

    /// <summary>Note effects profile 8 defines, "none" included: 7.</summary>
    public const int EffectCount = AudioFormat.EffectCount;

    /// <summary>Fastest slot: 1 console tick per step.</summary>
    public const int MinSpeed = 1;

    /// <summary>Slowest slot: 255 ticks per step — the field is one byte.</summary>
    public const int MaxSpeed = 255;

    /// <summary>
    /// Ticks per step a slot gets the moment it stops being empty — 8, borrowed from
    /// <see cref="AudioTextCompiler.DefaultSpeed"/> so a sound typed here and a sound written in
    /// <c>sfx.txt</c> open at the same tempo. Not a second default: that constant is itself the
    /// core's <c>SfxSlot.DefaultSpeed</c>, and the chain has one end.
    /// </summary>
    public const int DefaultSpeed = AudioTextCompiler.DefaultSpeed;

    private readonly string _sfxPath;

    /// <summary>What the disk holds: the dirty comparison's baseline, replaced on save. Never aliases <see cref="_bank"/>.</summary>
    private byte[] _savedBank;

    // The live bank. Mutated in place by the verbs and replaced wholesale by undo/redo, so
    // nothing may cache a reference to it across a step — every access goes through the field.
    private byte[] _bank;

    private readonly List<byte[]> _undo = new();
    private readonly List<byte[]> _redo = new();

    /// <summary>Pre-gesture bank while a pointer gesture is open; null between gestures.</summary>
    private byte[]? _strokeBackup;
    private bool _strokeChanged;

    /// <summary>
    /// Opens the effects bank of a cartridge folder (.quarp8 files never get here — the mode
    /// machine refuses them with the read-only line, exactly as for the other three screens).
    /// The file is optional: absent means 64 empty slots and a clean session. A file that is not
    /// a bank, is the wrong length, or breaks any rule of AUDIO-FORMAT §5 is refused here by
    /// <see cref="AudioFormat.ParseSfxFile"/> with <see cref="CartLoadException"/> — the very
    /// same failure and the very same wording <see cref="CartSource"/> produces for the same
    /// file, so the library reports it the way it reports a broken launch.
    /// </summary>
    public SfxEditorSession(string cartFolder)
    {
        ArgumentNullException.ThrowIfNull(cartFolder);
        CartName = Path.GetFileName(Path.TrimEndingDirectorySeparator(cartFolder));
        _sfxPath = Path.Combine(cartFolder, SfxFileName);
        BankReadOnly = File.Exists(Path.Combine(cartFolder, SfxSourceFileName));
        _savedBank = ReadPayload(_sfxPath);
        _bank = (byte[])_savedBank.Clone();
    }

    /// <summary>Folder name, for the header — the manifest is deliberately not read, same call as its three siblings.</summary>
    public string CartName { get; }

    /// <summary>
    /// True when <c>sfx.txt</c> lies beside the bank: the bank is then read-only and the screen
    /// must say so before the author enters a note. Observable on purpose — a surprise at save
    /// time is the thing this property exists to prevent.
    /// </summary>
    public bool BankReadOnly { get; }

    /// <summary>
    /// The live payload, 4352 bytes — what the grids draw, what the preview APU loads, and what
    /// <see cref="Save"/> wraps into <c>sfx.bin</c>. Canonical at every instant (see the type
    /// note), so handing it straight to <c>Apu.LoadSfxPayload</c> is safe on any frame.
    /// </summary>
    public ReadOnlySpan<byte> Payload => _bank;

    /// <summary>True while a pointer gesture is open — the current undo step is still growing.</summary>
    public bool StrokeActive => _strokeBackup is not null;

    /// <summary>True when the live bank differs from what the disk holds.</summary>
    public bool IsDirty => !_bank.AsSpan().SequenceEqual(_savedBank);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Bumped on every change to the bank (edit, undo, redo) so a renderer or the preview can notice.</summary>
    public int Version { get; private set; }

    /// <summary>Why the last save failed, or null. A save the author believes happened but did not is data loss, so it has to be sayable.</summary>
    public string? SaveError { get; private set; }

    // ---- reads: every one of them through the format's owner ----

    /// <summary>Console ticks per step of a slot, 1-255; 0 exactly when the slot is empty.</summary>
    public int SlotSpeed(int slot)
    {
        ValidateSlot(slot);
        return AudioFormat.SlotSpeed(_bank, slot);
    }

    /// <summary>Steps the slot plays, 0-32. Zero is the single mark of an empty slot (AUDIO-FORMAT §2).</summary>
    public int SlotLength(int slot)
    {
        ValidateSlot(slot);
        return AudioFormat.SlotLength(_bank, slot);
    }

    /// <summary>First step of the loop; meaningful only when <see cref="SlotLoopEnd"/> is non-zero.</summary>
    public int SlotLoopStart(int slot)
    {
        ValidateSlot(slot);
        return AudioFormat.SlotLoopStart(_bank, slot);
    }

    /// <summary>One past the last step of the loop (half-open); 0 means the slot does not loop.</summary>
    public int SlotLoopEnd(int slot)
    {
        ValidateSlot(slot);
        return AudioFormat.SlotLoopEnd(_bank, slot);
    }

    /// <summary>True when the slot holds nothing at all — the zero record, which is what <c>Sfx(id)</c> ignores.</summary>
    public bool SlotIsEmpty(int slot) => SlotLength(slot) == 0;

    /// <summary>
    /// The speed a screen should <em>show</em> for a slot: its stored speed, or
    /// <see cref="DefaultSpeed"/> while the slot is still empty. An empty slot stores 0 in every
    /// header byte and may not store anything else (AUDIO-FORMAT §5), so this is a display
    /// default rather than a second owner of stored state — the number the slot will get the
    /// moment its first note lands.
    /// </summary>
    public int EffectiveSpeed(int slot) => SlotIsEmpty(slot) ? DefaultSpeed : SlotSpeed(slot);

    /// <summary>One raw step word — for a renderer that wants all four fields at once.</summary>
    public ushort Step(int slot, int step)
    {
        ValidateSlot(slot);
        ValidateStep(step);
        return AudioFormat.Step(_bank, slot, step);
    }

    /// <summary>Semitone of a step, 0-63. Meaningless on a rest, whose word is zero throughout.</summary>
    public int StepNote(int slot, int step) => AudioFormat.Note(Step(slot, step));

    /// <summary>Waveform index of a step, 0-5.</summary>
    public int StepWave(int slot, int step) => AudioFormat.Wave(Step(slot, step));

    /// <summary>Volume of a step, 0-7; 0 means the step is a rest and the whole word is zero.</summary>
    public int StepVolume(int slot, int step) => AudioFormat.Volume(Step(slot, step));

    /// <summary>Note effect of a step, 0-6.</summary>
    public int StepEffect(int slot, int step) => AudioFormat.Effect(Step(slot, step));

    /// <summary>True when the step makes no sound — the zero word, and a slot's steps past its length are all of these.</summary>
    public bool StepIsRest(int slot, int step) => Step(slot, step) == 0;

    // ---- gestures ----

    /// <summary>
    /// A pointer went down on a grid. The pre-gesture bank is snapshotted here and becomes the
    /// undo entry when the gesture ends — the whole "one drag = one step" mechanism, borrowed
    /// unchanged from <see cref="MapEditorSession.BeginStroke"/>: nothing inside the gesture
    /// touches the undo stack.
    /// </summary>
    public void BeginStroke()
    {
        if (StrokeActive)
        {
            return;     // a second press without a release (focus-loss glitches) folds into the open gesture
        }
        _strokeBackup = (byte[])_bank.Clone();
        _strokeChanged = false;
    }

    /// <summary>
    /// The pointer came up: the gesture commits as one undo step — unless it changed nothing, in
    /// which case it never happened (an idle click must not push a no-op snapshot that makes
    /// Ctrl+Z look dead). Safe to call without an open gesture.
    /// </summary>
    public void EndStroke()
    {
        if (_strokeBackup is not byte[] backup)
        {
            return;
        }
        _strokeBackup = null;
        if (!_strokeChanged)
        {
            return;
        }
        _undo.Add(backup);
        _redo.Clear();      // the redone future described a bank that no longer exists
    }

    // ---- writes ----

    /// <summary>
    /// Writes one step: the note, the waveform, the volume and the effect the author's pen is
    /// holding. The two canonicity rules of AUDIO-FORMAT §3 and §5 are applied right here, and
    /// they are the reason this method exists instead of a raw word setter:
    ///
    /// <list type="bullet">
    ///   <item><b>Volume 0 is a rest and a rest is the zero word.</b> Whatever note, wave or
    ///     effect the caller passed is dropped, because none of it can be heard and a field
    ///     nobody can hear must not be able to change the bytes of the cartridge.</item>
    ///   <item><b>A step the slot does not play must be zero</b>, so writing past
    ///     <see cref="SlotLength"/> <em>grows the slot</em> rather than laying a note where the
    ///     sequencer will never look. A slot that was empty also gets
    ///     <see cref="DefaultSpeed"/> in the same breath, because a played slot may not hold
    ///     speed 0.</item>
    /// </list>
    ///
    /// <para>Out-of-range arguments throw rather than clamp: a note of 64 or a wave of 6 is a
    /// caller bug, and silently substituting one is precisely the "somebody else's game playing
    /// differently from how it was written" the format's §3 refuses.</para>
    /// </summary>
    public void SetStep(int slot, int step, int note, int wave, int volume, int effect)
    {
        ValidateSlot(slot);
        ValidateStep(step);
        ValidateNote(note);
        ValidateWave(wave);
        ValidateVolume(volume);
        ValidateEffect(effect);
        RequireWritableBank();
        bool own = OpenOwnStroke();
        if (volume == 0)
        {
            WriteWord(slot, step, 0);
        }
        else
        {
            GrowToInclude(slot, step);
            WriteWord(slot, step, AudioFormat.PackStep(note, wave, volume, effect));
        }
        CloseOwnStroke(own);
    }

    /// <summary>
    /// Silences one step — the zero word, the only spelling a rest has. Del's verb, and the
    /// volume grid's bottom row. The slot's <see cref="SlotLength"/> is deliberately <b>not</b>
    /// shortened: a rest inside a sound is a rest, and a trailing one is a beat of silence the
    /// author asked for.
    /// </summary>
    public void ClearStep(int slot, int step)
    {
        ValidateSlot(slot);
        ValidateStep(step);
        RequireWritableBank();
        bool own = OpenOwnStroke();
        WriteWord(slot, step, 0);
        CloseOwnStroke(own);
    }

    /// <summary>
    /// Ticks per step, 1-255 (AUDIO-FORMAT §2: the unit is a console tick, not a millisecond,
    /// because a step that lasted "1/120 s" would drift away from the frames a rewind replays).
    /// An <b>empty</b> slot is the zero record and may hold no speed at all, so this is an
    /// honest no-op there — the slot takes <see cref="DefaultSpeed"/> when its first note lands,
    /// and <see cref="EffectiveSpeed"/> is what a screen shows in the meantime.
    /// </summary>
    public void SetSpeed(int slot, int speed)
    {
        ValidateSlot(slot);
        if (speed is < MinSpeed or > MaxSpeed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speed), speed, $"a slot's speed is {MinSpeed}-{MaxSpeed} console ticks per step (AUDIO-FORMAT §2).");
        }
        RequireWritableBank();
        if (SlotIsEmpty(slot))
        {
            return;
        }
        bool own = OpenOwnStroke();
        WriteHeaderByte(slot, 0, (byte)speed);
        CloseOwnStroke(own);
    }

    /// <summary>
    /// How many steps the slot plays, 0-32. Shortening zeroes every step it drops, because
    /// AUDIO-FORMAT §2 requires a step past the length to be the zero word — otherwise two banks
    /// that sound identical would compare as different and a cartridge's identity would move on
    /// an edit nobody can hear. A loop left hanging outside the new length is pulled back in, or
    /// dropped when nothing of it survives.
    ///
    /// <para>Length 0 is how a slot is emptied, and it empties the whole record: speed and both
    /// loop fields go to zero too, which is the one spelling an unused slot has.</para>
    /// </summary>
    public void SetLength(int slot, int length)
    {
        ValidateSlot(slot);
        if (length is < 0 or > StepCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length), length, $"a slot plays 0-{StepCount} steps (AUDIO-FORMAT §2).");
        }
        RequireWritableBank();
        bool own = OpenOwnStroke();
        if (length == 0)
        {
            ClearSlotRecord(slot);
        }
        else
        {
            if (SlotIsEmpty(slot))
            {
                WriteHeaderByte(slot, 0, DefaultSpeed);
            }
            WriteHeaderByte(slot, 1, (byte)length);
            for (int step = length; step < StepCount; step++)
            {
                WriteWord(slot, step, 0);
            }
            ClampLoop(slot, length);
        }
        CloseOwnStroke(own);
    }

    /// <summary>
    /// The loop, as the half-open interval AUDIO-FORMAT §2 defines: <c>loop 2 6</c> repeats
    /// steps 2, 3, 4 and 5. <paramref name="end"/> of 0 turns the loop off, and then
    /// <paramref name="start"/> must be 0 as well — "a slot that does not loop stores 0 in both
    /// loop fields" is the format's own sentence, and the alternative (a remembered start with
    /// no end) is a byte nobody can hear deciding what the cartridge hashes to.
    /// </summary>
    public void SetLoop(int slot, int start, int end)
    {
        ValidateSlot(slot);
        RequireWritableBank();
        int length = SlotLength(slot);
        if (end == 0)
        {
            if (start != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(start), (start, end), "a slot that does not loop stores 0 in both loop fields (AUDIO-FORMAT §5).");
            }
        }
        else if (start < 0 || start >= end || end > length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end), (start, end),
                $"a loop is a half-open range inside 0..{length}: start < end <= length (AUDIO-FORMAT §5).");
        }
        bool own = OpenOwnStroke();
        WriteHeaderByte(slot, 2, (byte)start);
        WriteHeaderByte(slot, 3, (byte)end);
        CloseOwnStroke(own);
    }

    /// <summary>Turns the loop off — <see cref="SetLoop"/> with the one pair that means "no loop".</summary>
    public void ClearLoop(int slot) => SetLoop(slot, 0, 0);

    /// <summary>Empties a slot back to the zero record, header and all 32 steps — <see cref="SetLength"/> with 0, named for what it is.</summary>
    public void ClearSlot(int slot) => SetLength(slot, 0);

    // ---- undo / redo / save ----

    /// <summary>
    /// Ctrl+Z. Ends an open gesture first (committing it), so an undo mid-drag rolls back a whole
    /// gesture instead of tearing one in half. Whole-bank swaps, no copying. History lives in the
    /// session only: closing the tab forgets it, and a fresh session opens with Ctrl+Z honestly
    /// dead.
    /// </summary>
    public void Undo()
    {
        EndStroke();
        if (_undo.Count == 0)
        {
            return;
        }
        _redo.Add(_bank);
        byte[] previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _bank = previous;
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
        _undo.Add(_bank);
        byte[] next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _bank = next;
        Version++;
    }

    /// <summary>
    /// Ctrl+S. The clean guard is the save contract's heart: a session whose bank equals the disk
    /// writes <b>nothing</b> — open-and-close leaves the file untouched and, for a cart that
    /// never had one, uncreated, and a repeated Ctrl+S is a no-op. That is what keeps the pinned
    /// demo banks byte-identical after the editor has opened them, and the read-only rule is the
    /// second lock on the same door.
    ///
    /// <para>The bytes go out through <see cref="AudioFormat.WriteSfxFile"/>, which re-validates
    /// the whole payload — length first — before it prepends a header. So the length is checked
    /// on the way in (<see cref="AudioFormat.ParseSfxFile"/>) and again on the way out, by the
    /// one owner, and this class cannot write a bank it could not read back.</para>
    ///
    /// <para>Disk failures land in <see cref="SaveError"/> instead of throwing, because a full
    /// disk must leave the author their work and a message. A read-only bank that is somehow
    /// dirty is a contract violation rather than an accident: that throws.</para>
    /// </summary>
    /// <returns>True when the disk now matches the bank (including "already did"), false when a write failed.</returns>
    public bool Save()
    {
        EndStroke();
        if (!IsDirty)
        {
            SaveError = null;
            return true;
        }
        if (BankReadOnly)
        {
            // Unreachable while RequireWritableBank guards every mutator — that door slams
            // first. Kept as the second gate, because "sfx.bin is owned by sfx.txt" is a
            // save-time promise and the next wave's tools will be new writers.
            throw new InvalidOperationException(
                $"{CartName}: {SfxFileName} is read-only while {SfxSourceFileName} is present — "
                + $"the text source owns the bank. Remove {SfxSourceFileName} to edit the sound inside Quarp.");
        }
        try
        {
            byte[] file = AudioFormat.WriteSfxFile(_bank);
            File.WriteAllBytes(_sfxPath, file);
            _savedBank = (byte[])_bank.Clone();
            SaveError = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SaveError = e.Message;
            return false;
        }
    }

    // ---- the private half ----

    /// <summary>Absent file = the zero bank (AUDIO-FORMAT §1); present file = its payload, validated by the format's owner.</summary>
    private static byte[] ReadPayload(string path)
    {
        if (!File.Exists(path))
        {
            return AudioFormat.EmptySfxPayload();
        }
        return AudioFormat.ParseSfxFile(File.ReadAllBytes(path), SfxFileName);
    }

    /// <summary>A verb that arrived outside a pointer gesture gets a gesture of its own, so it costs exactly one Ctrl+Z.</summary>
    private bool OpenOwnStroke()
    {
        if (StrokeActive)
        {
            return false;
        }
        BeginStroke();
        return true;
    }

    private void CloseOwnStroke(bool own)
    {
        if (own)
        {
            EndStroke();
        }
    }

    /// <summary>
    /// The one hand that writes a bank byte. Every verb goes through here, so "the bank changed"
    /// means exactly one thing: the dirt, the <see cref="Version"/> the preview watches and the
    /// gesture's changed-flag move together or not at all. Re-writing the same value is not a
    /// change, which is what keeps an idle click out of the undo stack.
    /// </summary>
    private void WriteByte(int offset, byte value)
    {
        if (_bank[offset] == value)
        {
            return;
        }
        _bank[offset] = value;
        _strokeChanged = true;
        Version++;
    }

    private void WriteHeaderByte(int slot, int field, byte value) =>
        WriteByte(AudioFormat.SlotHeaderOffset(slot) + field, value);

    /// <summary>One step word, little-endian, through the byte writer so both halves are one change.</summary>
    private void WriteWord(int slot, int step, ushort word)
    {
        int offset = AudioFormat.StepOffset(slot, step);
        WriteByte(offset, (byte)(word & 0xFF));
        WriteByte(offset + 1, (byte)(word >> 8));
    }

    /// <summary>A note landing on or past the slot's end extends the slot; an empty slot also gains its tempo.</summary>
    private void GrowToInclude(int slot, int step)
    {
        if (SlotIsEmpty(slot))
        {
            WriteHeaderByte(slot, 0, DefaultSpeed);
        }
        if (step >= SlotLength(slot))
        {
            WriteHeaderByte(slot, 1, (byte)(step + 1));
        }
    }

    /// <summary>Pulls a loop back inside a shortened slot, or drops it when nothing of it is left.</summary>
    private void ClampLoop(int slot, int length)
    {
        int start = SlotLoopStart(slot);
        int end = SlotLoopEnd(slot);
        if (end == 0)
        {
            return;
        }
        if (start >= length)
        {
            WriteHeaderByte(slot, 2, 0);
            WriteHeaderByte(slot, 3, 0);
            return;
        }
        if (end > length)
        {
            WriteHeaderByte(slot, 3, (byte)length);
        }
    }

    /// <summary>The zero record: four header bytes and 32 zero words, which is what an unused slot is.</summary>
    private void ClearSlotRecord(int slot)
    {
        for (int field = 0; field < AudioFormat.SfxSlotHeaderSize; field++)
        {
            WriteHeaderByte(slot, field, 0);
        }
        for (int step = 0; step < StepCount; step++)
        {
            WriteWord(slot, step, 0);
        }
    }

    private void RequireWritableBank()
    {
        if (BankReadOnly)
        {
            throw new InvalidOperationException(
                $"{CartName}: the effects bank is read-only while {SfxSourceFileName} is present — "
                + $"the text source owns it (AUDIO-FORMAT §6). Remove {SfxSourceFileName} to edit the sound inside Quarp.");
        }
    }

    private static void ValidateSlot(int slot)
    {
        if (slot is < 0 or >= SlotCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slot), slot, $"the bank holds slots 0-{SlotCount - 1} (SPEC-8 §4).");
        }
    }

    private static void ValidateStep(int step)
    {
        if (step is < 0 or >= StepCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(step), step, $"a slot holds steps 0-{StepCount - 1} (SPEC-8 §4).");
        }
    }

    private static void ValidateNote(int note)
    {
        if (note is < 0 or > MaxNote)
        {
            throw new ArgumentOutOfRangeException(
                nameof(note), note, $"notes are 0-{MaxNote} semitones above C-2 (AUDIO-FORMAT §3).");
        }
    }

    private static void ValidateWave(int wave)
    {
        if (wave is < 0 or >= WaveCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wave), wave, $"profile 8 defines waves 0-{WaveCount - 1} (AUDIO-FORMAT §3).");
        }
    }

    private static void ValidateVolume(int volume)
    {
        if (volume is < 0 or > MaxVolume)
        {
            throw new ArgumentOutOfRangeException(
                nameof(volume), volume, $"volumes are 0-{MaxVolume}, and 0 is a rest (AUDIO-FORMAT §3).");
        }
    }

    private static void ValidateEffect(int effect)
    {
        if (effect is < 0 or >= EffectCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effect), effect, $"profile 8 defines effects 0-{EffectCount - 1} (AUDIO-FORMAT §3).");
        }
    }
}
