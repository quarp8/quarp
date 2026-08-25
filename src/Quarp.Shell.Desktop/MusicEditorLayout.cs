using Microsoft.Xna.Framework;
using Quarp.Core;

namespace Quarp.Shell.Desktop;

/// <summary>
/// The three section flags a pattern can carry, as the <b>column order the screen shows them
/// in</b> — an enum rather than three rectangles, so <see cref="MusicEditorLayout"/> can answer
/// "which marker, on which pattern" in one call and <see cref="MusicEditorInput"/> can route it
/// in one switch. The exact shape <see cref="SfxField"/> has for the sound screen's steppers and
/// <see cref="EditorPromptVerb"/> for the prompt line.
///
/// <para>The <em>meaning</em> of each flag is not here: the bits, their names and the rule that
/// bits 3-7 stay zero belong to <see cref="Quarp.CartKit.AudioFormat"/> and reach this screen
/// through <see cref="MusicEditorSession.FlagLoopStart"/> and its two neighbours. This enum owns
/// one thing only — that loop-start is drawn left of loop-end, which is drawn left of stop.</para>
/// </summary>
public enum MusicFlagColumn
{
    /// <summary>Playback returns here when it meets <see cref="LoopEnd"/>. Drawn <c>[</c>.</summary>
    LoopStart,

    /// <summary>End of a section: jump back to the nearest start. Drawn <c>]</c>.</summary>
    LoopEnd,

    /// <summary>The song ends after this pattern. Drawn <c>X</c>.</summary>
    Stop,
}

/// <summary>
/// Where everything on the <b>music</b> editor screen sits, in <b>console pixels</b> — 160x90 on
/// profile 8, the fifth and last screen to be measured this way (ADR-029). It is the geometry's
/// <b>single owner</b>: <see cref="MusicEditorRenderer"/> draws these rectangles and
/// <see cref="MusicEditorInput"/> hit-tests the pointer against the same ones, so a pattern can
/// never be painted in one place and clicked in another.
///
/// <para>The shared frame — the top band with the exit button, the tooltip field and the five
/// editor tabs, the three rules, the message line and the status line — is measured by
/// <see cref="ConsoleChrome"/> and only forwarded here. There is no second chrome on this
/// screen.</para>
///
/// <para><b>THE ARITHMETIC, in full, because on this screen it decided the whole design.</b>
/// <see cref="ConsoleChrome"/> leaves rows 11..74 for content and rows 75..77 for a horizontal
/// slider. This screen has no horizontal slider — a song scrolls <em>down</em> — so, exactly as
/// the code screen does and for the reason <see cref="ConsoleChrome.SliderBottom"/> spells out, it
/// runs its content to row 77 and gets <b>67 rows</b> instead of 64.</para>
///
/// <para><b>Why ten patterns and not sixty-four.</b> A cell has to say <em>which SFX slot</em>,
/// and a slot number is two digits: 8 px wide and 5 px tall in the 4x6 system font. So a tracker
/// row cannot be shorter than the font's own cell — <see cref="RowHeight"/> = 6 — and 64 patterns
/// would need 384 rows where the console has 67. Sixty-seven rows buy: 6 for the channel header,
/// 1 for its rule, and 60 for the grid — <b>ten patterns of the sixty-four</b>. That is the loss,
/// and it is named rather than hidden.</para>
///
/// <para><b>What answers it: the whole song is on screen anyway.</b> Beside the grid stands the
/// <see cref="Overview"/> — one pixel row per pattern, 64 of them, four 3-px channel marks and a
/// 2-px flag column each. Sixty-four rows plus a frame is 66 px, which fits the 67 the frame
/// leaves with one to spare, so <b>every pattern of the song is visible at all times</b> even
/// though only ten are editable at once. It is the map screen's minimap answer to the same
/// problem (TIC-80's <c>world.c</c>, REFERENCES-EDITORS §3.1) applied to a song, and it is also
/// the scroll control: a click on it jumps the window, a wheel over it scrolls.</para>
///
/// <para><b>The width, left to right, and it comes out exact.</b> 10 px of tool column (one
/// icon-button, since this screen has four buttons and no tools) + 2 px of gutter = the grid at
/// x 12. The overview is 16 px wide against the right edge (x 144), with 2 px of gutter, so the
/// grid ends at 142 and is <b>130 px</b> wide. Inside it: <see cref="NumberWidth"/> = 14 for the
/// pattern number (two digits, 8 px, centred), <see cref="FlagsWidth"/> = 12 for the three
/// section markers (three cells of 4 px, one character each), and the remaining 104 px divided by
/// four channels gives <b>26 px a channel</b> — which holds six characters of the system font
/// where a cell needs two, so the number sits in the middle of a wide, plainly clickable
/// plate.</para>
///
/// <para><b>What did NOT fit, named rather than hidden.</b> (1) Fifty-four of the sixty-four
/// patterns are off the editable grid at any moment — the overview shows them, the scroll reaches
/// them. (2) There is no room for the <em>name</em> of the sound a slot holds, only its number:
/// names live in <c>sfx.txt</c> and on the sound screen, one tab over. (3) The three section
/// markers are 4 px wide, which is one character — <c>[</c>, <c>]</c> and <c>X</c> — where the
/// sound screen could afford five-character labels; their words are in the tooltip. (4) Save,
/// undo and redo left the status band for the tool column, for the reason the other four screens'
/// did: the console's status line is five pixels tall and an icon-button is ten.</para>
///
/// <para><b>Every scale is one.</b> There is no fractional scale on this screen and no path that
/// can produce one (ARCHITECTURE §5); the window's only say is the whole-integer factor
/// <see cref="FramePlacement"/> presents the finished frame at.</para>
/// </summary>
public readonly struct MusicEditorLayout
{
    /// <summary>Patterns in the song — 64, borrowed from the model rather than re-derived.</summary>
    public const int PatternCount = MusicEditorSession.PatternCount;

    /// <summary>Channels in a pattern — 4.</summary>
    public const int ChannelCount = MusicEditorSession.ChannelCount;

    /// <summary>Section markers per pattern: loop start, loop end, stop.</summary>
    public const int FlagColumns = 3;

    /// <summary>Height of one tracker row: the system font's own cell, which is the floor for a row that must print two digits.</summary>
    public const int RowHeight = SystemFont.CellHeight;

    /// <summary>Height of the channel header band — one text cell, the same 6 px a row gets.</summary>
    public const int HeaderHeight = SystemFont.CellHeight;

    /// <summary>Width of the pattern-number field: two digits (8 px) centred with 3 px of air each side.</summary>
    public const int NumberWidth = 14;

    /// <summary>Width of one section-marker cell — four pixels, which is exactly one character of the 4x6 font.</summary>
    public const int FlagCellWidth = 4;

    /// <summary>Width of all three markers together.</summary>
    public const int FlagsWidth = FlagColumns * FlagCellWidth;

    /// <summary>Width of the mute and solo cells in a channel's header — two characters' worth of cell for a one-character face.</summary>
    public const int ToggleWidth = 8;

    /// <summary>Clear pixels between the tool column and the grid, and between the grid and the overview.</summary>
    public const int Gutter = 2;

    /// <summary>Width of the whole-song overview, frame included: 4 channels x 3 px + a 2 px flag column + 1 px of frame each side.</summary>
    public const int OverviewWidth = 4 * OverviewChannelWidth + OverviewFlagWidth + 2;

    /// <summary>Console pixels one channel takes in the overview.</summary>
    public const int OverviewChannelWidth = 3;

    /// <summary>Console pixels the overview's flag column takes.</summary>
    public const int OverviewFlagWidth = 2;

    /// <summary>
    /// Patterns in one group of the overview's ruler — four, the same beat grouping the sound
    /// screen counts steps in (<see cref="SfxEditorLayout.BeatSteps"/>) and TIC-80's own
    /// <c>NOTES_PER_BEAT</c> (§6.1). Sixty-four unmarked rows cannot be counted; sixteen groups can.
    /// </summary>
    public const int OverviewGroup = 4;

    /// <summary>
    /// The tool column, top to bottom: play/stop, then the three the host frame kept in its
    /// status bar. Those three moved for the reason the other four screens' did — the console's
    /// status line is five pixels tall and an icon-button is ten.
    /// </summary>
    private static readonly EditorButton[] _toolColumn =
    {
        EditorButton.ToolPlay, EditorButton.Save, EditorButton.Undo, EditorButton.Redo,
    };

    /// <summary>The frame this screen stands in. See <see cref="ConsoleChrome"/>.</summary>
    public ConsoleChrome Chrome { get; private init; }

    // Forwarded, not recomputed — ConsoleChrome is the only place these exist.

    /// <summary>Screen width in console pixels.</summary>
    public int ScreenWidth => Chrome.ScreenWidth;

    /// <summary>Screen height in console pixels.</summary>
    public int ScreenHeight => Chrome.ScreenHeight;

    /// <summary>Side of every icon-button — ten console pixels, an 8x8 mask plus its frame.</summary>
    public int ButtonSize => ConsoleChrome.ButtonSize;

    /// <summary>Screen-edge inset for text — one pixel, because forty columns is the whole line.</summary>
    public int Margin => ConsoleChrome.Margin;

    /// <summary>The top band that carries the exit button, the tooltip field and the five editor tabs.</summary>
    public Rectangle TabStrip => Chrome.TopBar;

    /// <summary>The status band: the channel and slot at the left, the pattern number at the right.</summary>
    public Rectangle StatusBar => Chrome.StatusBar;

    /// <summary>Glyph top of the single message line — the exit prompt, the save error or the standing notice.</summary>
    public int PromptY => Chrome.MessageY;

    /// <summary>The ten placed buttons — the frame's six and the tool column's four.</summary>
    public IReadOnlyList<EditorButtonPlace> Buttons { get; private init; }

    /// <summary>The channel header band: four cells, each a channel number and its mute and solo toggles.</summary>
    public Rectangle Header { get; private init; }

    /// <summary>The rule between the header and the first tracker row.</summary>
    public int HeaderRuleY => Header.Bottom;

    /// <summary>The tracker rows — <see cref="VisibleRows"/> patterns of the sixty-four.</summary>
    public Rectangle Rows { get; private init; }

    /// <summary>
    /// The ring around the pattern grid — the channel header and the ten tracker rows together,
    /// because they are one panel: the header names the columns the rows are read in, and a rule
    /// already separates them.
    ///
    /// <para>TIC-80 grounds each tracker channel on a rectangle of its own before writing a row
    /// into it (<c>drawTrackerChannel</c>, REFERENCES-EDITORS §6.1), and every panel on the
    /// sprite screen has carried a ring since 2026-08-25
    /// (<see cref="SpriteEditorLayout.CanvasFrame"/>). This one had none: its top and bottom were
    /// the frame's own rules, there with or without a grid, and its sides were nothing. All four
    /// are drawn here, so the panel has one owner rather than two accidents.</para>
    /// </summary>
    public Rectangle GridFrame =>
        new(Rows.X - 1, Header.Y - 1, Rows.Width + 2, Rows.Bottom - Header.Y + 2);

    /// <summary>Left edge of the first channel column; the number and the markers stand left of it.</summary>
    public int ChannelsX { get; private init; }

    /// <summary>Width of one channel column — 26 px, see the type comment.</summary>
    public int ChannelWidth { get; private init; }

    /// <summary>The whole-song overview: 64 one-pixel rows and a frame.</summary>
    public Rectangle Overview { get; private init; }

    /// <summary>How many patterns the grid shows at once — ten on a 90-row console.</summary>
    public int VisibleRows => Rows.Height / RowHeight;

    /// <summary>The highest first-pattern the window can stand at, so the last row is pattern 63.</summary>
    public int MaxFirstPattern => Math.Max(0, PatternCount - VisibleRows);

    /// <summary>
    /// The screen's geometry for a console of the given size. The two numbers are <b>console</b>
    /// pixels — 160x90 on profile 8 — and never a window size: the window's only say in this
    /// screen is the whole-integer scale <see cref="FramePlacement"/> presents it at.
    /// </summary>
    public static MusicEditorLayout Compute(int screenWidth, int screenHeight)
    {
        var buttons = new EditorButtonPlace[10];
        int placed = 0;
        ConsoleChrome chrome = ConsoleChrome.Compute(screenWidth, screenHeight, buttons, ref placed);

        int button = ConsoleChrome.ButtonSize;
        int top = chrome.ContentTop;

        // One column, top to bottom — four buttons is 40 px of the 67 the band has.
        for (int i = 0; i < _toolColumn.Length; i++)
        {
            buttons[placed++] = new EditorButtonPlace
            {
                Id = _toolColumn[i],
                Rect = new Rectangle(0, top + i * button, button, button),
            };
        }

        // Measured from both edges inward, so nothing here is an absolute column on a console
        // that might not be 160 wide.
        var overview = new Rectangle(
            screenWidth - OverviewWidth, top, OverviewWidth, PatternCount + 2);
        int gridX = button + Gutter;
        int gridWidth = Math.Max(1, overview.X - Gutter - gridX);
        var header = new Rectangle(gridX, top, gridWidth, HeaderHeight);

        int rowsTop = header.Bottom + 1;
        // The floor is the slider band's bottom, not the content band's: this screen places no
        // horizontal slider, and ConsoleChrome.SliderBottom is where the frame says such a
        // screen may run to. Three pixels is half a tracker row.
        int visible = Math.Max(1, (chrome.SliderBottom - rowsTop) / RowHeight);
        var rows = new Rectangle(gridX, rowsTop, gridWidth, visible * RowHeight);

        int channelsX = gridX + NumberWidth + FlagsWidth;
        int channelWidth = Math.Max(1, (gridX + gridWidth - channelsX) / ChannelCount);

        return new MusicEditorLayout
        {
            Chrome = chrome,
            Buttons = buttons,
            Header = header,
            Rows = rows,
            ChannelsX = channelsX,
            ChannelWidth = channelWidth,
            Overview = overview,
        };
    }

    /// <summary>The placed rectangle of one button — the hover frame anchors to it.</summary>
    public Rectangle ButtonRect(EditorButton id) => ConsoleChrome.ButtonRect(Buttons, id);

    /// <summary>The 8x8 mask's destination inside a button.</summary>
    public Rectangle ButtonIconRect(Rectangle buttonRect) => ConsoleChrome.ButtonIconRect(buttonRect);

    /// <summary>Console point to the button under it, stubs included (hover needs the dead ones too).</summary>
    public bool TryButton(int x, int y, out EditorButton id) =>
        ConsoleChrome.TryButton(Buttons, x, y, out id);

    /// <summary>Clickable area of one prompt verb — <see cref="ConsoleChrome"/> owns the message line.</summary>
    public Rectangle PromptVerbRect(EditorPromptVerb verb) => Chrome.PromptVerbRect(verb);

    /// <summary>Console point to a prompt verb, or false. Checked only while the prompt is up.</summary>
    public bool TryPromptVerb(int x, int y, out EditorPromptVerb verb) =>
        Chrome.TryPromptVerb(x, y, out verb);

    // ---- the grid ----

    /// <summary>Top of the row that shows <paramref name="pattern"/> while the window starts at <paramref name="firstPattern"/>.</summary>
    public int RowY(int pattern, int firstPattern) => Rows.Y + (pattern - firstPattern) * RowHeight;

    /// <summary>True when the pattern is one of the <see cref="VisibleRows"/> the window shows.</summary>
    public bool RowVisible(int pattern, int firstPattern) =>
        pattern >= firstPattern && pattern < firstPattern + VisibleRows;

    /// <summary>The whole row of one pattern, number through last channel — the band the cursor's row wears.</summary>
    public Rectangle RowRect(int pattern, int firstPattern) =>
        new(Rows.X, RowY(pattern, firstPattern), Rows.Width, RowHeight);

    /// <summary>The pattern-number field of one row.</summary>
    public Rectangle NumberRect(int pattern, int firstPattern) =>
        new(Rows.X, RowY(pattern, firstPattern), NumberWidth, RowHeight);

    /// <summary>One section marker's cell.</summary>
    public Rectangle FlagCellRect(int pattern, MusicFlagColumn flag, int firstPattern) =>
        new(Rows.X + NumberWidth + (int)flag * FlagCellWidth,
            RowY(pattern, firstPattern), FlagCellWidth, RowHeight);

    /// <summary>One channel cell — the rectangle a slot number is printed in and a click is tested against.</summary>
    public Rectangle ChannelCellRect(int pattern, int channel, int firstPattern) =>
        new(ChannelsX + channel * ChannelWidth, RowY(pattern, firstPattern), ChannelWidth, RowHeight);

    /// <summary>
    /// Console point to (pattern, channel), or false off the channel columns. The window's first
    /// pattern travels in because a row's meaning depends on where the window stands — the same
    /// shape <see cref="CodeEditorLayout.TryTextCell"/> has for a scrolled buffer.
    /// </summary>
    public bool TryChannelCell(int x, int y, int firstPattern, out int pattern, out int channel)
    {
        pattern = 0;
        channel = 0;
        if (!Rows.Contains(x, y) || x < ChannelsX)
        {
            return false;
        }
        channel = (x - ChannelsX) / ChannelWidth;
        if (channel >= ChannelCount)
        {
            return false;       // the rounding tail at the right edge belongs to no channel
        }
        pattern = firstPattern + (y - Rows.Y) / RowHeight;
        return pattern < PatternCount;
    }

    /// <summary>
    /// Console point to the nearest <em>visible</em> cell, for drags: a gesture whose pointer
    /// leaves the grid keeps marking along its edge instead of tearing, exactly as
    /// <see cref="MapEditorLayout.ClampMapCell"/> does for the map canvas and
    /// <see cref="SfxEditorLayout.ClampPitchCell"/> for the pitch grid.
    /// </summary>
    public void ClampCell(int x, int y, int firstPattern, out int pattern, out int channel)
    {
        int lastVisible = Math.Min(PatternCount, firstPattern + VisibleRows) - 1;
        pattern = Math.Clamp(firstPattern + FloorDiv(y - Rows.Y, RowHeight), firstPattern, lastVisible);
        channel = Math.Clamp(FloorDiv(x - ChannelsX, ChannelWidth), 0, ChannelCount - 1);
    }

    /// <summary>Console point to a section marker, or false off them.</summary>
    public bool TryFlagCell(int x, int y, int firstPattern, out int pattern, out MusicFlagColumn flag)
    {
        pattern = 0;
        flag = MusicFlagColumn.LoopStart;
        if (!Rows.Contains(x, y) || x < Rows.X + NumberWidth || x >= ChannelsX)
        {
            return false;
        }
        flag = (MusicFlagColumn)((x - Rows.X - NumberWidth) / FlagCellWidth);
        pattern = firstPattern + (y - Rows.Y) / RowHeight;
        return pattern < PatternCount;
    }

    // ---- the channel header ----

    /// <summary>The header cell of one channel: its number and its two toggles.</summary>
    public Rectangle ChannelHeaderRect(int channel) =>
        new(ChannelsX + channel * ChannelWidth, Header.Y, ChannelWidth, Header.Height);

    /// <summary>The mute toggle of one channel — the middle of its header cell.</summary>
    public Rectangle MuteRect(int channel)
    {
        Rectangle cell = ChannelHeaderRect(channel);
        return new Rectangle(cell.Right - 2 * ToggleWidth, cell.Y, ToggleWidth, cell.Height);
    }

    /// <summary>The solo toggle of one channel — the right end of its header cell.</summary>
    public Rectangle SoloRect(int channel)
    {
        Rectangle cell = ChannelHeaderRect(channel);
        return new Rectangle(cell.Right - ToggleWidth, cell.Y, ToggleWidth, cell.Height);
    }

    /// <summary>
    /// Console point to which channel's toggle was hit and which of the two it is, or false. One
    /// call rather than eight rectangles at the call site, so the router cannot wire a channel's
    /// mute to another channel's solo in one place and rightly in another — the same shape
    /// <see cref="SfxEditorLayout.TryFieldStepper"/> carries for the sound screen's steppers.
    /// </summary>
    public bool TryChannelToggle(int x, int y, out int channel, out bool solo)
    {
        for (int i = 0; i < ChannelCount; i++)
        {
            if (MuteRect(i).Contains(x, y))
            {
                channel = i;
                solo = false;
                return true;
            }
            if (SoloRect(i).Contains(x, y))
            {
                channel = i;
                solo = true;
                return true;
            }
        }
        channel = 0;
        solo = false;
        return false;
    }

    // ---- the whole-song overview ----

    /// <summary>The overview's interior — 64 rows of one pixel, inside the frame.</summary>
    public Rectangle OverviewInterior =>
        new(Overview.X + 1, Overview.Y + 1, Overview.Width - 2, PatternCount);

    /// <summary>The one-pixel row of one pattern in the overview.</summary>
    public Rectangle OverviewRowRect(int pattern) =>
        new(OverviewInterior.X, OverviewInterior.Y + pattern, OverviewInterior.Width, 1);

    /// <summary>The mark one channel of one pattern leaves in the overview.</summary>
    public Rectangle OverviewChannelRect(int pattern, int channel) =>
        new(OverviewInterior.X + channel * OverviewChannelWidth,
            OverviewInterior.Y + pattern, OverviewChannelWidth, 1);

    /// <summary>
    /// The one column of a channel's overview lane that no mark can paint, and the column the
    /// overview's ruler lives in.
    ///
    /// <para>A channel's mark is drawn a pixel narrower than <see cref="OverviewChannelWidth"/> —
    /// the same trick the sound screen's grids use (<see cref="SfxEditorLayout.StepGapX"/>) — so
    /// this column is free at every pattern, for every song. The lane rules drawn down it stop
    /// the overview from being an empty box on an empty song and tell channel 2's marks from
    /// channel 3's on a full one; the group ticks and the cursor's and playhead's rows go in the
    /// same column, so <b>nothing the overview draws about itself can cover the song</b>.</para>
    /// </summary>
    public int OverviewLaneX(int channel) =>
        OverviewInterior.X + channel * OverviewChannelWidth + OverviewChannelWidth - 1;

    /// <summary>The overview's flag column for one pattern — two pixels: loop marks, then stop.</summary>
    public Rectangle OverviewFlagRect(int pattern) =>
        new(OverviewInterior.X + ChannelCount * OverviewChannelWidth,
            OverviewInterior.Y + pattern, OverviewFlagWidth, 1);

    /// <summary>
    /// The frame that shows which slice of the song the grid is editing — the minimap's viewport
    /// rectangle, one screen over. It brackets the window's rows rather than covering them, which
    /// is why it is two pixels taller than the window and starts a pixel above it.
    /// </summary>
    public Rectangle OverviewWindowRect(int firstPattern) =>
        new(Overview.X, OverviewInterior.Y + firstPattern - 1, Overview.Width, VisibleRows + 2);

    /// <summary>Console point to a pattern in the overview, or false off it.</summary>
    public bool TryOverviewPattern(int x, int y, out int pattern)
    {
        pattern = 0;
        if (!Overview.Contains(x, y))
        {
            return false;
        }
        pattern = Math.Clamp(y - OverviewInterior.Y, 0, PatternCount - 1);
        return true;
    }

    // ---- one hit test for the hover clock ----

    /// <summary>
    /// Console point to which buttonless control of this screen is under it, or
    /// <see cref="MusicRegion.None"/>. One hit test for the hover clock, so the tooltip and the
    /// click cannot disagree about what the pointer is on: the click chain in
    /// <see cref="MusicEditorInput"/> tests exactly these rectangles, in this order.
    /// </summary>
    public MusicRegion RegionAt(int x, int y)
    {
        if (Overview.Contains(x, y))
        {
            return MusicRegion.Overview;
        }
        if (Header.Contains(x, y))
        {
            return MusicRegion.Channels;
        }
        if (!Rows.Contains(x, y))
        {
            return MusicRegion.None;
        }
        return x >= ChannelsX ? MusicRegion.Song
            : x >= Rows.X + NumberWidth ? MusicRegion.Flags
            : MusicRegion.Song;
    }

    /// <summary>The rectangle a region's tooltip names — the same box <see cref="RegionAt"/> answered from.</summary>
    public Rectangle RegionRect(MusicRegion region) => region switch
    {
        MusicRegion.Song => Rows,
        MusicRegion.Flags => new Rectangle(Rows.X + NumberWidth, Rows.Y, FlagsWidth, Rows.Height),
        MusicRegion.Channels => Header,
        MusicRegion.Overview => Overview,
        _ => Rectangle.Empty,
    };

    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
}
