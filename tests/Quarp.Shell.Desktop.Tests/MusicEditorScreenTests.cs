using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quarp.CartKit;
using Quarp.Shell.Desktop;
using Xunit;

namespace Quarp.Shell.Desktop.Tests;

/// <summary>
/// The music editor screen, everything about it that is not a picture: the geometry's
/// round-trips, the button contract, two-way input parity, mute and solo, the two-digit slot
/// entry, the section flags, the window and the whole-song overview, the playback request, and
/// the MUSIC tab finally being a live button.
///
/// <para>The pixels are pinned next door in <c>MusicEditorScreenGoldenTests</c>. What is here is
/// what a hash cannot say: that a click lands where the picture drew it, that every live button
/// changes something observable, and that every key has a mouse twin and every mouse gesture a
/// key — the input-parity law of M9 stage 2.5, which is two-way.</para>
/// </summary>
public class MusicEditorScreenTests : IDisposable
{
    private readonly string _root;

    public MusicEditorScreenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quarp-music-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // Console pixels, always: this screen was born on the console (ADR-029) and never had a
    // window-sized cut, so unlike its four siblings' suites there is no back-buffer size here.
    private const int ConsoleWidth = 160;
    private const int ConsoleHeight = 90;
    private const double FrameSeconds = 1.0 / 60.0;
    private const int Off = -1000;

    private static readonly Keys[] NoKeys = Array.Empty<Keys>();

    private static readonly EditorButton[] AllButtons = (EditorButton[])Enum.GetValues(typeof(EditorButton));

    // ==================================================================================
    // The window, minus the window.
    // ==================================================================================

    private sealed class Harness
    {
        private readonly ShellCommandReader _keys = new();
        private readonly EditorMouseReader _pointer = new();
        private int _wheelTotal;

        internal Harness(ShellModeMachine modes) => Modes = modes;

        internal ShellModeMachine Modes { get; }

        internal ToolbarFlyout Flyout { get; } = new();

        internal IconHoverTracker Hover { get; } = new();

        internal SheetScroll SheetScroll { get; } = new();

        internal EditorShell Context =>
            new(Modes, Flyout, Hover, SheetScroll, ConsoleWidth, ConsoleHeight);

        internal MusicEditorLayout Layout => MusicEditorLayout.Compute(ConsoleWidth, ConsoleHeight);

        internal MusicEditorSession Session => Modes.MusicEditor!;

        internal MusicEditorView View => Modes.MusicView!;

        internal void Frame(Keys[] down, int mouseX, int mouseY, ButtonState left, ButtonState right)
        {
            ShellCommands commands = _keys.Read(new KeyboardState(down));
            EditorMouse mouse = _pointer.Read(new MouseState(
                mouseX, mouseY, _wheelTotal, left, ButtonState.Released, right,
                ButtonState.Released, ButtonState.Released));
            switch (Modes.Mode)
            {
                case ShellMode.Editor:
                    SpriteEditorInput.Update(Context, commands, mouse, FrameSeconds);
                    break;
                case ShellMode.SfxEditor:
                    SfxEditorInput.Update(Context, commands, mouse, FrameSeconds);
                    break;
                case ShellMode.MusicEditor:
                    MusicEditorInput.Update(Context, commands, mouse, FrameSeconds);
                    break;
            }
        }

        internal void Idle() => Frame(NoKeys, Off, Off, ButtonState.Released, ButtonState.Released);

        internal void Tap(params Keys[] down)
        {
            Frame(down, Off, Off, ButtonState.Released, ButtonState.Released);
            Idle();
        }

        internal void Wheel(int notches, int x, int y)
        {
            _wheelTotal += notches * 120;
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Released);
        }

        internal void LeftDown(int x, int y) =>
            Frame(NoKeys, x, y, ButtonState.Pressed, ButtonState.Released);

        internal void LeftUp(int x, int y) =>
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Released);

        internal void Click(int x, int y)
        {
            LeftDown(x, y);
            LeftUp(x, y);
        }

        internal void RightClick(int x, int y)
        {
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Pressed);
            Frame(NoKeys, x, y, ButtonState.Released, ButtonState.Released);
        }

        internal void ClickRect(Rectangle rect) => Click(rect.Center.X, rect.Center.Y);

        internal void ClickButton(EditorButton button) => ClickRect(Layout.ButtonRect(button));
    }

    // ==================================================================================
    // Fixtures — the road the shell really takes, menu → library → editor → MUSIC tab.
    // ==================================================================================

    private string NewCartFolder(bool musicSource = false)
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
        string folder = Path.Combine(root, "cart");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"), "{\"name\":\"music\",\"author\":\"\",\"profile\":8}");
        if (musicSource)
        {
            File.WriteAllText(
                Path.Combine(folder, MusicEditorSession.MusicSourceFileName), "# hand-written\n");
        }
        return folder;
    }

    private static ShellModeMachine MachineOnTheMusicTab(string cartFolder)
    {
        var machine = new ShellModeMachine(
            new CartLibrary(Path.GetDirectoryName(cartFolder)!),
            static path => CartSession.Start(path),
            static () => { });
        machine.Menu.SkipIntro();           // the real road since ADR-028: menu → library → editor
        machine.OpenLibrary();
        machine.OpenEditor();
        Assert.Equal(ShellMode.Editor, machine.Mode);
        machine.SwitchEditorTab(ShellMode.MusicEditor);
        Assert.Equal(ShellMode.MusicEditor, machine.Mode);
        return machine;
    }

    private Harness OpenMusicEditor(out string cartFolder, bool musicSource = false)
    {
        cartFolder = NewCartFolder(musicSource);
        return new Harness(MachineOnTheMusicTab(cartFolder));
    }

    private static string MusicPath(string folder) =>
        Path.Combine(folder, MusicEditorSession.MusicFileName);

    // ==================================================================================
    // 1. The MUSIC tab is alive, and the stub list is empty.
    // ==================================================================================

    /// <summary>
    /// The one-line version of this whole wave: the MUSIC tab is no longer drawn-but-dead. Its
    /// stub flag is gone — with it, the <em>whole</em> stub list, because it was the last name on
    /// it — it routes to a real mode, its tooltip promises a key instead of a later portion, and
    /// it joins the ring Alt+Left/Right walks.
    ///
    /// <para>Break recipe: put <see cref="EditorButton.MusicTab"/> back into
    /// <see cref="EditorIcons.IsStub"/> — every assertion here goes red, and so does the contract
    /// sweep below, because the router refuses stubs before any verb.</para>
    /// </summary>
    [Fact]
    public void TheMusicTabIsNoLongerADeadButtonAndNothingElseIsEither()
    {
        Assert.False(EditorIcons.IsStub(EditorButton.MusicTab));
        Assert.Equal(ShellMode.MusicEditor, EditorIcons.TabTarget(EditorButton.MusicTab));
        Assert.DoesNotContain(
            "LATER PORTION", EditorIcons.Tooltip(EditorButton.MusicTab), StringComparison.Ordinal);
        Assert.Contains("ALT+", EditorIcons.Tooltip(EditorButton.MusicTab), StringComparison.Ordinal);
        Assert.Contains(ShellMode.MusicEditor, EditorIcons.LiveEditorTabs);
        // Six since M9 stage 5 put the running GAME at the head of the strip; music is still
        // its last stop, which is what F6 means.
        Assert.Equal(6, EditorIcons.LiveEditorTabs.Count);
        Assert.Equal(ShellMode.MusicEditor, EditorIcons.LiveEditorTabs[^1]);
        // The list is empty now, and that is the sentence this wave exists to be able to write.
        foreach (EditorButton button in AllButtons)
        {
            Assert.False(EditorIcons.IsStub(button), $"{button} is still called a stub");
        }
    }

    /// <summary>
    /// Alt+Right from the sound screen arrives at the music screen — the ring has five stops now,
    /// and the fifth is reachable by the only key the strip has.
    /// </summary>
    [Fact]
    public void AltRightFromTheSoundScreenArrivesAtTheMusicScreen()
    {
        Harness harness = OpenMusicEditor(out _);
        harness.Modes.SwitchEditorTab(ShellMode.SfxEditor);
        Assert.Equal(ShellMode.SfxEditor, harness.Modes.Mode);

        harness.Tap(Keys.LeftAlt, Keys.Right);

        Assert.Equal(ShellMode.MusicEditor, harness.Modes.Mode);
    }

    // ==================================================================================
    // 2. Absent music.bin is silence, and a clean session writes nothing.
    // ==================================================================================

    /// <summary>
    /// AUDIO-FORMAT §1's headline rule, in the editor: a cart with no <c>music.bin</c> opens as 64
    /// empty patterns — not an error, not a message — and closing it again leaves the folder
    /// exactly as it was found. The file is created only by a dirty save, so visiting the MUSIC
    /// tab cannot leave one behind.
    /// </summary>
    [Fact]
    public void AnAbsentBankIsSixtyFourEmptyPatternsAndACleanSessionWritesNothing()
    {
        Harness harness = OpenMusicEditor(out string folder);
        MusicEditorSession session = harness.Session;

        Assert.Equal(64, MusicEditorSession.PatternCount);
        for (int pattern = 0; pattern < MusicEditorSession.PatternCount; pattern++)
        {
            Assert.True(session.PatternIsEmpty(pattern));
            Assert.Equal(0, session.PatternFlags(pattern));
        }
        Assert.False(session.IsDirty);
        Assert.False(File.Exists(MusicPath(folder)));

        Assert.True(session.Save());
        Assert.False(File.Exists(MusicPath(folder)));
    }

    /// <summary>
    /// A cart whose song is never opened must not get a session it never asked for — the lazy
    /// birth every tab but the sheet's carries. The music session appears on the first visit to
    /// the MUSIC tab and not one frame earlier.
    /// </summary>
    [Fact]
    public void TheSongSessionIsBornOnTheFirstVisitToTheTabAndNotBefore()
    {
        string folder = NewCartFolder();
        var machine = new ShellModeMachine(
            new CartLibrary(Path.GetDirectoryName(folder)!),
            static path => CartSession.Start(path),
            static () => { });
        machine.Menu.SkipIntro();
        machine.OpenLibrary();
        machine.OpenEditor();

        Assert.Null(machine.MusicEditor);
        Assert.Null(machine.MusicView);

        machine.SwitchEditorTab(ShellMode.MusicEditor);

        Assert.NotNull(machine.MusicEditor);
        Assert.NotNull(machine.MusicView);
    }

    // ==================================================================================
    // 3. Geometry: every clickable rectangle answers its own centre.
    // ==================================================================================

    /// <summary>
    /// The round-trip discipline every screen here carries, applied to this one's pieces: a cell
    /// drawn in one place and hit-tested in another is the defect this closes. The window's first
    /// pattern travels through every call, because a row's meaning depends on where the window
    /// stands.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(54)]
    public void EveryCellRoundTripsThroughItsRectangle(int firstPattern)
    {
        var layout = MusicEditorLayout.Compute(ConsoleWidth, ConsoleHeight);

        for (int row = 0; row < layout.VisibleRows; row++)
        {
            int pattern = firstPattern + row;
            for (int channel = 0; channel < MusicEditorLayout.ChannelCount; channel++)
            {
                Point at = layout.ChannelCellRect(pattern, channel, firstPattern).Center;
                Assert.True(layout.TryChannelCell(at.X, at.Y, firstPattern, out int hitPattern, out int hitChannel));
                Assert.Equal(pattern, hitPattern);
                Assert.Equal(channel, hitChannel);
                Assert.Equal(MusicRegion.Song, layout.RegionAt(at.X, at.Y));
            }
            foreach (MusicFlagColumn flag in Enum.GetValues<MusicFlagColumn>())
            {
                Point at = layout.FlagCellRect(pattern, flag, firstPattern).Center;
                Assert.True(layout.TryFlagCell(at.X, at.Y, firstPattern, out int hitPattern, out MusicFlagColumn hitFlag));
                Assert.Equal(pattern, hitPattern);
                Assert.Equal(flag, hitFlag);
                Assert.Equal(MusicRegion.Flags, layout.RegionAt(at.X, at.Y));
            }
        }
        for (int channel = 0; channel < MusicEditorLayout.ChannelCount; channel++)
        {
            Point mute = layout.MuteRect(channel).Center;
            Assert.True(layout.TryChannelToggle(mute.X, mute.Y, out int hitChannel, out bool solo));
            Assert.Equal(channel, hitChannel);
            Assert.False(solo);
            Point soloAt = layout.SoloRect(channel).Center;
            Assert.True(layout.TryChannelToggle(soloAt.X, soloAt.Y, out hitChannel, out solo));
            Assert.Equal(channel, hitChannel);
            Assert.True(solo);
            Assert.Equal(MusicRegion.Channels, layout.RegionAt(mute.X, mute.Y));
        }
        for (int pattern = 0; pattern < MusicEditorLayout.PatternCount; pattern++)
        {
            Point at = layout.OverviewRowRect(pattern).Center;
            Assert.True(layout.TryOverviewPattern(at.X, at.Y, out int hitPattern));
            Assert.Equal(pattern, hitPattern);
            Assert.Equal(MusicRegion.Overview, layout.RegionAt(at.X, at.Y));
        }
    }

    /// <summary>
    /// The frame is the shared one, not a second copy: this screen's tab strip, status band,
    /// prompt line and six chrome buttons are the very rectangles the sound screen's are. Two
    /// frames existing would be the drift <see cref="ConsoleChrome"/> was written to prevent.
    /// </summary>
    [Fact]
    public void TheFrameIsTheOneTheOtherConsoleScreensStandIn()
    {
        var music = MusicEditorLayout.Compute(ConsoleWidth, ConsoleHeight);
        var sfx = SfxEditorLayout.Compute(ConsoleWidth, ConsoleHeight);

        Assert.Equal(sfx.TabStrip, music.TabStrip);
        Assert.Equal(sfx.StatusBar, music.StatusBar);
        Assert.Equal(sfx.PromptY, music.PromptY);
        Assert.Equal(sfx.ButtonSize, music.ButtonSize);
        foreach (EditorPromptVerb verb in Enum.GetValues<EditorPromptVerb>())
        {
            Assert.Equal(sfx.PromptVerbRect(verb), music.PromptVerbRect(verb));
        }
        EditorButton[] shared =
        {
            EditorButton.ExitTab, EditorButton.CodeTab, EditorButton.SpritesTab,
            EditorButton.TilemapTab, EditorButton.SoundTab, EditorButton.MusicTab,
        };
        foreach (EditorButton button in shared)
        {
            Assert.Equal(sfx.ButtonRect(button), music.ButtonRect(button));
        }
    }

    /// <summary>
    /// The layout places exactly the buttons <see cref="EditorIcons.BelongsToMusicEditor"/> names,
    /// and no others: a placed button that nobody claims, or a claimed button nobody places, is
    /// the defect class the button contract closed in wave 2g.
    /// </summary>
    [Fact]
    public void TheLayoutPlacesExactlyTheButtonsThisScreenClaims()
    {
        var layout = MusicEditorLayout.Compute(ConsoleWidth, ConsoleHeight);
        var placed = layout.Buttons.Select(place => place.Id).ToHashSet();

        foreach (EditorButton button in AllButtons)
        {
            Assert.Equal(EditorIcons.BelongsToMusicEditor(button), placed.Contains(button));
        }
        Assert.Equal(11, placed.Count);      // ten, plus the GAME tab of M9 stage 5
    }

    // ==================================================================================
    // 4. The button contract.
    // ==================================================================================

    /// <summary>
    /// Everything a music button click may legally touch, in one comparable value.
    /// <c>SaveReported</c> is in it because since ADR-041 a dirty save cannot reach the disk —
    /// what SAVE changes is the message the author is shown, and a button whose only effect is a
    /// message is still a button that did something.
    /// </summary>
    private sealed record Snapshot(
        ShellMode Mode, int Version, bool Dirty, bool CanUndo, bool CanRedo, bool PromptShown,
        bool PlayWanted, int Cursor, bool SaveReported);

    private static Snapshot Observe(ShellModeMachine machine)
    {
        MusicEditorSession music = machine.MusicEditor!;
        MusicEditorView view = machine.MusicView!;
        return new Snapshot(
            machine.Mode, music.Version, music.IsDirty, music.CanUndo, music.CanRedo,
            view.ExitPromptShown, view.PlayWanted, music.CursorPattern, music.SaveError is not null);
    }

    /// <summary>The shell's press dispatch over the real router pieces — the same two-line mirror its four siblings use.</summary>
    private static void RouteClick(ShellModeMachine machine, EditorButton button)
    {
        if (EditorIcons.IsStub(button))
        {
            return;                                     // the router refuses stubs before any verb
        }
        if (EditorIcons.TabTarget(button) is ShellMode tab)
        {
            machine.SwitchEditorTab(tab);               // travel is the mode machine's verb
            return;
        }
        if (EditorIcons.ClickMusicButton(machine.MusicEditor!, machine.MusicView!, button))
        {
            machine.HandleEscape();                     // the exit tab's verb belongs to the machine
        }
    }

    /// <summary>A session where every live button has work to do: dirt, an undo step and a redo step.</summary>
    private static void Prepare(MusicEditorSession session)
    {
        session.SetChannelSlot(0, 0, 3);
        session.SetChannelSlot(1, 0, 4);
        session.Undo();
    }

    /// <summary>
    /// The sweep. Live buttons must change the snapshot; the music tab (it names the screen
    /// already on show) must change exactly nothing.
    ///
    /// <para>Break recipe: delete any <c>case</c> from
    /// <see cref="EditorIcons.ClickMusicButton"/> — that one button's assertion goes red by name.
    /// Add a button to <see cref="MusicEditorLayout"/> without wiring it and the same line names
    /// the new one.</para>
    /// </summary>
    [Fact]
    public void EveryPlacedLiveMusicButtonChangesSomethingObservable()
    {
        foreach (EditorButtonPlace place in MusicEditorLayout.Compute(ConsoleWidth, ConsoleHeight).Buttons)
        {
            string folder = NewCartFolder();
            ShellModeMachine machine = MachineOnTheMusicTab(folder);
            Prepare(machine.MusicEditor!);
            Snapshot before = Observe(machine);

            RouteClick(machine, place.Id);

            Snapshot after = Observe(machine);
            if (place.Id == EditorButton.MusicTab)
            {
                Assert.True(before == after, $"{place.Id} is a no-op by contract but changed state");
            }
            else
            {
                Assert.True(
                    before != after,
                    $"{place.Id} is placed and live but its click changed nothing — unwired?");
            }
        }
    }

    /// <summary>
    /// Every buttonless control of this screen has a label, and the label names its keys — the
    /// discoverability half of the input-parity law. <see cref="MusicRegion.None"/> is not a
    /// control and answers with a throw rather than with a lie.
    /// </summary>
    [Fact]
    public void EveryButtonlessControlNamesItsKeys()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EditorIcons.MusicRegionTooltip(MusicRegion.None));

        Assert.Contains("0-9", EditorIcons.MusicRegionTooltip(MusicRegion.Song), StringComparison.Ordinal);
        Assert.Contains("DEL", EditorIcons.MusicRegionTooltip(MusicRegion.Song), StringComparison.Ordinal);
        Assert.Contains("TAB", EditorIcons.MusicRegionTooltip(MusicRegion.Flags), StringComparison.Ordinal);
        Assert.Contains("SHIFT+1-4", EditorIcons.MusicRegionTooltip(MusicRegion.Channels), StringComparison.Ordinal);
        Assert.Contains("SHIFT+5-8", EditorIcons.MusicRegionTooltip(MusicRegion.Channels), StringComparison.Ordinal);
        Assert.Contains("PGUP", EditorIcons.MusicRegionTooltip(MusicRegion.Overview), StringComparison.Ordinal);
        // The mute row says outright that it saves nothing — a control that looks like an edit
        // and is not has to say so where it is.
        Assert.Contains(
            "SAVES NOTHING", EditorIcons.MusicRegionTooltip(MusicRegion.Channels), StringComparison.Ordinal);
    }

    // ==================================================================================
    // 5. Two-way input parity: the key and the click write the same byte.
    // ==================================================================================

    /// <summary>
    /// Typing a slot number: two digits, and the second one writes and steps the cursor on. The
    /// half-typed state is visible rather than secret, and a pair above 63 is refused rather than
    /// clamped — clamping would play a sound the author did not ask for.
    /// </summary>
    [Fact]
    public void TwoDigitsWriteASlotAndAPairAboveSixtyThreeIsRefused()
    {
        Harness harness = OpenMusicEditor(out _);

        harness.Tap(Keys.D0);
        Assert.Equal(0, harness.View.PendingDigit);
        Assert.True(harness.Session.ChannelIsSilent(0, 0));     // nothing written yet

        harness.Tap(Keys.D7);
        Assert.Equal(MusicEditorView.NoDigit, harness.View.PendingDigit);
        Assert.Equal(7, harness.Session.ChannelSlot(0, 0));
        Assert.Equal(1, harness.Session.CursorPattern);         // the tracker step

        harness.Tap(Keys.D9);
        harness.Tap(Keys.D9);
        Assert.True(harness.Session.ChannelIsSilent(1, 0));     // 99 is not a slot
        Assert.Equal(9, harness.View.PendingDigit);             // ...and the 9 is the new first digit
        Assert.Equal(1, harness.Session.CursorPattern);
    }

    /// <summary>
    /// The wheel over a cell is the mouse's whole answer to typing a number, and it reaches every
    /// state the keys reach: silence steps up to slot 0, and slot 0 steps back down to silence.
    /// Without it the most-used action of this screen would be keyboard-only, which the parity
    /// law forbids.
    /// </summary>
    [Fact]
    public void TheWheelOverACellStepsItsSlotBothWaysThroughSilence()
    {
        Harness harness = OpenMusicEditor(out _);
        Point at = harness.Layout.ChannelCellRect(2, 1, harness.View.FirstPattern).Center;

        harness.Wheel(1, at.X, at.Y);
        Assert.Equal(0, harness.Session.ChannelSlot(2, 1));

        harness.Wheel(1, at.X, at.Y);
        Assert.Equal(1, harness.Session.ChannelSlot(2, 1));

        harness.Wheel(-1, at.X, at.Y);
        harness.Wheel(-1, at.X, at.Y);
        Assert.True(harness.Session.ChannelIsSilent(2, 1));
    }

    /// <summary>
    /// Del and the right click are the same verb: the cell falls silent. The keyboard one steps
    /// the cursor on (it is the twin of a completed digit pair), the pointer one does not — it
    /// has already said where it means.
    /// </summary>
    [Fact]
    public void DelAndARightClickBothSilenceACell()
    {
        Harness harness = OpenMusicEditor(out _);
        harness.Session.SetChannelSlot(0, 0, 5);
        harness.Session.SetChannelSlot(4, 2, 6);

        harness.Tap(Keys.Delete);
        Assert.True(harness.Session.ChannelIsSilent(0, 0));
        Assert.Equal(1, harness.Session.CursorPattern);

        Point at = harness.Layout.ChannelCellRect(4, 2, harness.View.FirstPattern).Center;
        harness.RightClick(at.X, at.Y);
        Assert.True(harness.Session.ChannelIsSilent(4, 2));
        Assert.Equal(4, harness.Session.CursorPattern);
        Assert.Equal(2, harness.Session.CursorChannel);
    }

    /// <summary>
    /// The three section flags, from both hands: the key on the cursor's pattern, the click on the
    /// marker it landed on. Pressing either again clears the flag, which is what makes three keys
    /// enough for all eight states.
    /// </summary>
    [Theory]
    [InlineData(MusicFlagColumn.LoopStart, MusicEditorSession.FlagLoopStart)]
    [InlineData(MusicFlagColumn.LoopEnd, MusicEditorSession.FlagLoopEnd)]
    [InlineData(MusicFlagColumn.Stop, MusicEditorSession.FlagStop)]
    public void EverySectionFlagHasAKeyAndAClickThatMeanTheSameThing(MusicFlagColumn column, byte bit)
    {
        Keys key = column switch
        {
            MusicFlagColumn.LoopStart => Keys.OemTilde,
            MusicFlagColumn.LoopEnd => Keys.Tab,
            _ => Keys.X,
        };
        Harness byKey = OpenMusicEditor(out _);

        byKey.Tap(key);
        Assert.True(byKey.Session.HasFlag(0, bit));
        byKey.Tap(key);
        Assert.False(byKey.Session.HasFlag(0, bit));

        Harness byMouse = OpenMusicEditor(out _);
        Rectangle marker = byMouse.Layout.FlagCellRect(3, column, byMouse.View.FirstPattern);

        byMouse.ClickRect(marker);
        Assert.True(byMouse.Session.HasFlag(3, bit));
        Assert.Equal(3, byMouse.Session.CursorPattern);
        byMouse.ClickRect(marker);
        Assert.False(byMouse.Session.HasFlag(3, bit));
    }

    /// <summary>
    /// Mute and solo, from both hands, and the fact that neither touches the cartridge. Shift+1..4
    /// mute, Shift+5..8 solo, and the header's eight cells do the same — the sprite screen's flag
    /// row keys read by the screen where they mean something else, which is the standing
    /// resolution for a shared key.
    /// </summary>
    [Fact]
    public void MuteAndSoloAnswerToBothHandsAndChangeNoByte()
    {
        Harness harness = OpenMusicEditor(out _);
        harness.Session.SetChannelSlot(0, 0, 1);
        byte[] before = harness.Session.Payload.ToArray();

        harness.Tap(Keys.LeftShift, Keys.D2);
        Assert.True(harness.View.ChannelMuted(1));
        harness.Tap(Keys.LeftShift, Keys.D2);
        Assert.False(harness.View.ChannelMuted(1));

        harness.Tap(Keys.LeftShift, Keys.D7);
        Assert.True(harness.View.ChannelSoloed(2));

        harness.ClickRect(harness.Layout.MuteRect(3));
        Assert.True(harness.View.ChannelMuted(3));
        harness.ClickRect(harness.Layout.SoloRect(0));
        Assert.True(harness.View.ChannelSoloed(0));

        // Solo wins over mute: with two channels soloed, only those two are audible, and the
        // muted one keeps its flag for when the solos are dropped.
        Assert.True(harness.View.ChannelAudible(0));
        Assert.False(harness.View.ChannelAudible(1));
        Assert.True(harness.View.ChannelAudible(2));
        Assert.False(harness.View.ChannelAudible(3));
        Assert.True(harness.View.ChannelMuted(3));

        Assert.Equal(before, harness.Session.Payload.ToArray());
        // One write happened in this test, and it was the SetChannelSlot above: six toggles later
        // the bank's version has not moved, which is the machine-checkable half of "saves nothing".
        Assert.Equal(1, harness.Session.Version);
    }

    /// <summary>
    /// The window: a click on the overview brings any of the 64 patterns into the ten-row grid,
    /// PgUp/PgDn walk it a screenful at a time, and the wheel over the overview scrolls it. All
    /// three are the same fact — where the editable window stands — and it has one owner.
    /// </summary>
    [Fact]
    public void TheOverviewIsTheScrollControlAndPageKeysAreItsTwin()
    {
        Harness harness = OpenMusicEditor(out _);
        Assert.Equal(0, harness.View.FirstPattern);

        harness.ClickRect(harness.Layout.OverviewRowRect(40));
        Assert.Equal(40, harness.Session.CursorPattern);
        Assert.InRange(harness.View.FirstPattern, 31, 40);
        Assert.True(harness.Layout.RowVisible(40, harness.View.FirstPattern));

        harness.Tap(Keys.PageUp);
        Assert.Equal(40 - harness.Layout.VisibleRows, harness.Session.CursorPattern);
        Assert.True(harness.Layout.RowVisible(harness.Session.CursorPattern, harness.View.FirstPattern));

        int before = harness.View.FirstPattern;
        harness.Wheel(-1, harness.Layout.Overview.Center.X, harness.Layout.Overview.Center.Y);
        Assert.True(harness.View.FirstPattern > before);
    }

    /// <summary>
    /// A drag across the grid marks a rectangle, Shift+arrows do the same from the keyboard, and
    /// the clipboard chords act on what is marked. The session owns the rectangle; the view owns
    /// only the corner the gesture started from.
    /// </summary>
    [Fact]
    public void ADragAndShiftArrowsBothMarkTheSameRectangle()
    {
        Harness harness = OpenMusicEditor(out _);
        Point from = harness.Layout.ChannelCellRect(1, 1, 0).Center;
        Point to = harness.Layout.ChannelCellRect(3, 3, 0).Center;

        harness.LeftDown(from.X, from.Y);
        harness.Frame(NoKeys, to.X, to.Y, ButtonState.Pressed, ButtonState.Released);
        harness.LeftUp(to.X, to.Y);

        Assert.True(harness.Session.HasSelection);
        Assert.Equal(1, harness.Session.SelectionPattern);
        Assert.Equal(1, harness.Session.SelectionChannel);
        Assert.Equal(3, harness.Session.SelectionPatterns);
        Assert.Equal(3, harness.Session.SelectionChannels);

        Harness keys = OpenMusicEditor(out _);
        keys.Tap(Keys.LeftShift, Keys.Down);
        keys.Tap(Keys.LeftShift, Keys.Right);
        Assert.True(keys.Session.HasSelection);
        Assert.Equal(0, keys.Session.SelectionPattern);
        Assert.Equal(2, keys.Session.SelectionPatterns);
        Assert.Equal(2, keys.Session.SelectionChannels);
    }

    // ==================================================================================
    // 6. Playback is asked for here and performed by the wiring.
    // ==================================================================================

    /// <summary>
    /// Space and the play button ask for the same thing, and neither of them makes a sound: the
    /// request is a view fact, the chip belongs to <c>QuarpGame</c>. A second ask restarts the
    /// song from the cursor rather than being swallowed, which is the whole gesture when an author
    /// is auditioning one edit at a time.
    /// </summary>
    [Fact]
    public void SpaceAndThePlayButtonAskForTheSongAndTheWiringAnswers()
    {
        Harness harness = OpenMusicEditor(out _);
        harness.Session.SetCursor(6, 0);

        harness.Tap(Keys.Space);
        Assert.True(harness.View.PlayWanted);
        Assert.Equal(6, harness.View.PlayFrom);
        int epoch = harness.View.PlayEpoch;
        Assert.False(harness.View.Playing);          // nothing has reported yet: no chip in this test

        // The wiring's report is what lights the button and the playhead.
        harness.View.ReportPlaying(true, 7);
        Assert.True(harness.View.Playing);
        Assert.Equal(7, harness.View.PlayingPattern);

        harness.Tap(Keys.Space);
        Assert.False(harness.View.PlayWanted);
        Assert.Equal(MusicEditorView.NoPattern, harness.View.PlayingPattern);

        harness.ClickButton(EditorButton.ToolPlay);
        Assert.True(harness.View.PlayWanted);
        Assert.True(harness.View.PlayEpoch > epoch);

        // A song that ran off its end stops being wanted, so the next Space starts it again
        // instead of having to stop it first.
        harness.View.ReportPlaying(false, MusicEditorView.NoPattern);
        Assert.False(harness.View.PlayWanted);
        Assert.False(harness.View.Playing);
    }

    /// <summary>
    /// What the preview chip is handed: the session's bytes with the inaudible channels silenced,
    /// as a <b>copy</b>. This is the one place mute could have reached the cartridge, so it is the
    /// one place that gets an assertion about it.
    /// </summary>
    [Fact]
    public void TheAudiblePayloadSilencesMutedChannelsAndLeavesTheSongAlone()
    {
        Harness harness = OpenMusicEditor(out _);
        MusicEditorSession session = harness.Session;
        session.SetChannelSlot(0, 0, 1);
        session.SetChannelSlot(0, 1, 2);
        byte[] before = session.Payload.ToArray();

        harness.View.ToggleMute(0);
        byte[] audible = harness.View.AudiblePayload(session);

        Assert.Equal(MusicEditorSession.PayloadSize, audible.Length);
        Assert.Equal(-1, MusicPatternList.PatternChannel(audible, 0, 0));     // muted
        Assert.Equal(2, MusicPatternList.PatternChannel(audible, 0, 1));      // heard
        Assert.Equal(before, session.Payload.ToArray());                 // and the song is untouched
        Assert.NotSame(before, audible);

        // Solo silences everything it does not name, whatever the mutes say.
        harness.View.ToggleSolo(1);
        byte[] soloed = harness.View.AudiblePayload(session);
        Assert.Equal(-1, MusicPatternList.PatternChannel(soloed, 0, 0));
        Assert.Equal(2, MusicPatternList.PatternChannel(soloed, 0, 1));
    }

    /// <summary>
    /// The bank the preview needs beside the song: <c>sfx.bin</c>, opened on demand through the
    /// same lazy door the SOUND tab uses. Asking twice returns the same session — a second one
    /// would be a second owner of the cart's sounds, and the author's unsaved notes would be
    /// inaudible in the song.
    /// </summary>
    [Fact]
    public void ThePreviewsSoundBankIsTheOneTheSoundTabEdits()
    {
        Harness harness = OpenMusicEditor(out _);
        Assert.Null(harness.Modes.SfxEditor);

        SfxEditorSession? first = harness.Modes.EnsureSfxBank();

        Assert.NotNull(first);
        Assert.Same(first, harness.Modes.SfxEditor);
        Assert.Same(first, harness.Modes.EnsureSfxBank());
        Assert.Equal(ShellMode.MusicEditor, harness.Modes.Mode);    // opening a bank is not travel

        // ...and it is the very session the SOUND tab shows, unsaved notes and all.
        first!.SetStep(0, 0, 24, 0, 5, 0);
        harness.Modes.SwitchEditorTab(ShellMode.SfxEditor);
        Assert.Same(first, harness.Modes.SfxEditor);
        Assert.True(harness.Modes.SfxEditor!.IsDirty);
    }

    // ==================================================================================
    // 7. The read-only bank, and the exit question.
    // ==================================================================================

    /// <summary>
    /// A cart with <c>music.txt</c> beside its bank is read-only inside Quarp — the verdict
    /// <c>map.csv</c> set and <c>sfx.txt</c> repeated. The screen says so on its standing line
    /// before the author types, and every writing key is inert rather than throwing.
    /// </summary>
    [Fact]
    public void ACartWithMusicTextIsReadOnlyAndSaysSoBeforeTheAuthorTypes()
    {
        Harness harness = OpenMusicEditor(out _, musicSource: true);

        Assert.True(harness.Session.BankReadOnly);
        Assert.Equal(
            "READ-ONLY: MUSIC.TXT OWNS THIS SONG", MusicEditorRenderer.StandingNotice(harness.Session));

        harness.Tap(Keys.D0);
        harness.Tap(Keys.D5);
        harness.Tap(Keys.OemTilde);
        harness.Tap(Keys.Delete);
        Point at = harness.Layout.ChannelCellRect(0, 0, 0).Center;
        harness.Wheel(1, at.X, at.Y);

        Assert.False(harness.Session.IsDirty);
        Assert.True(harness.Session.ChannelIsSilent(0, 0));
        Assert.Equal(0, harness.Session.PatternFlags(0));
    }

    /// <summary>
    /// Esc on a dirty song raises the question rather than dropping the work, and X on it leaves
    /// with <c>music.bin</c> byte-for-byte untouched — a cart that never had one still does not.
    /// </summary>
    [Fact]
    public void EscapeOnADirtySongAsksAndDiscardWritesNothing()
    {
        Harness harness = OpenMusicEditor(out string folder);
        harness.Session.SetChannelSlot(0, 0, 9);

        harness.Tap(Keys.Escape);
        Assert.True(harness.View.ExitPromptShown);
        Assert.Equal(ShellMode.MusicEditor, harness.Modes.Mode);

        // While the prompt is up the digits are deaf: a stray keystroke must not change the bank
        // the author is being asked about.
        harness.Tap(Keys.D3);
        Assert.Equal(9, harness.Session.ChannelSlot(0, 0));

        harness.Tap(Keys.X);
        Assert.Equal(ShellMode.Library, harness.Modes.Mode);
        Assert.False(File.Exists(MusicPath(folder)));
    }

    /// <summary>
    /// Z on the prompt still tries to save, and since ADR-041 the try comes back with a sentence
    /// instead of a file: the pattern navigator has no format to write into, so nothing lands on
    /// the disk. The shell's existing rule for a save that failed then applies unchanged — the
    /// editor stays open with the message and the author's work, exactly as it does for a full
    /// disk — and X on the prompt is still the way out (see the test above).
    /// </summary>
    [Fact]
    public void SaveAndExitReportsThatThereIsNothingToSaveInto()
    {
        Harness harness = OpenMusicEditor(out string folder);
        harness.Session.SetChannelSlot(2, 1, 11);
        harness.Session.SetPatternFlags(2, MusicEditorSession.FlagStop);

        harness.Tap(Keys.Escape);
        harness.Tap(Keys.Z);

        Assert.Equal(ShellMode.MusicEditor, harness.Modes.Mode);
        Assert.False(File.Exists(MusicPath(folder)));
        Assert.True(harness.Session.IsDirty);
        Assert.NotNull(harness.Session.SaveError);
        Assert.Contains("quarp audio build", harness.Session.SaveError!);

        // The prompt is still up — a failed save does not close it — so the way out is X, which
        // discards and leaves, writing nothing.
        Assert.True(harness.Modes.MusicView!.ExitPromptShown);
        harness.Tap(Keys.X);
        Assert.Equal(ShellMode.Library, harness.Modes.Mode);
        Assert.False(File.Exists(MusicPath(folder)));
    }

    /// <summary>
    /// The shared exit rule, with five banks now: answering for the song does not close the editor
    /// while another open bank is still dirty — the author is brought to that tab and asked there.
    /// One rule, one method, five arms.
    /// </summary>
    [Fact]
    public void LeavingTheSongAsksTheOtherOpenBanksToo()
    {
        Harness harness = OpenMusicEditor(out _);
        harness.Modes.Editor!.SelectColor(8);
        harness.Modes.Editor.BeginStroke();
        harness.Modes.Editor.Paint(0, 0);
        harness.Modes.Editor.EndStroke();
        Assert.True(harness.Modes.Editor.IsDirty);

        // The song itself is clean, so Esc would close the editor outright if the rule were
        // per-bank instead of per-cartridge.
        harness.Tap(Keys.Escape);

        Assert.Equal(ShellMode.Editor, harness.Modes.Mode);
        Assert.True(harness.Modes.Editor.ExitPromptShown);
    }

    /// <summary>
    /// The same rule from the other side, which is the arm this wave added: leaving from
    /// <em>another</em> tab must not drop a dirty song the author is not looking at. The editor
    /// comes to the front of the music tab and asks there.
    ///
    /// <para>Break recipe: delete the <see cref="ShellMode.MusicEditor"/> arm from
    /// <c>ShellModeMachine.CloseUnlessAnotherBankIsDirty</c> and this goes red — the editor closes
    /// and four unsaved patterns go with it.</para>
    /// </summary>
    [Fact]
    public void ADirtySongIsAskedAboutEvenFromAnotherTab()
    {
        Harness harness = OpenMusicEditor(out _);
        harness.Session.SetChannelSlot(0, 0, 7);
        harness.Modes.SwitchEditorTab(ShellMode.Editor);
        Assert.Equal(ShellMode.Editor, harness.Modes.Mode);
        Assert.False(harness.Modes.Editor!.IsDirty);        // the sheet has nothing to answer for

        harness.Tap(Keys.Escape);

        Assert.Equal(ShellMode.MusicEditor, harness.Modes.Mode);
        Assert.True(harness.Modes.MusicView!.ExitPromptShown);
    }

    /// <summary>
    /// Flipping tabs keeps unsaved patterns: the session is born once and lives until the whole
    /// editor closes. That is the stage-3 promise "there and back without losing unsaved work",
    /// now with the fifth tab in it.
    /// </summary>
    [Fact]
    public void TravelKeepsUnsavedPatterns()
    {
        Harness harness = OpenMusicEditor(out _);
        harness.Session.SetChannelSlot(5, 3, 12);
        MusicEditorSession before = harness.Session;

        harness.Modes.SwitchEditorTab(ShellMode.Editor);
        harness.Modes.SwitchEditorTab(ShellMode.MusicEditor);

        Assert.Same(before, harness.Modes.MusicEditor);
        Assert.Equal(12, harness.Session.ChannelSlot(5, 3));
        Assert.True(harness.Session.IsDirty);
    }
}
