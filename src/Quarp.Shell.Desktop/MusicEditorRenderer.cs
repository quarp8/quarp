using Microsoft.Xna.Framework;
using Quarp.Core;
using static Quarp.Shell.Desktop.ConsoleChromeRenderer;

namespace Quarp.Shell.Desktop;

/// <summary>
/// Draws the music editor <b>into the console's own framebuffer</b> (ADR-029): the top band with
/// the exit button, the tooltip field and the five editor tabs; the tool column with play/stop,
/// save, undo and redo; the channel header with its four mute and four solo toggles; ten tracker
/// rows of the song — pattern number, three section markers, four channel cells; the whole-song
/// overview down the right edge; the status line and the one message line.
///
/// <para><b>What this screen is, and why it is not a note grid.</b> <c>music.bin</c> is 64
/// patterns x 4 channels, and a channel byte holds a <b>reference to an SFX slot</b> and one
/// active bit — six bits and one (AUDIO-FORMAT §4). There is no note in this bank, no octave, no
/// volume and no effect; those live one tab over in <c>sfx.bin</c>. So this editor is PICO-8's
/// pattern navigator rather than TIC-80's note tracker (REFERENCES-EDITORS §6.3 against §6.1),
/// and a cell says one thing: which of the 64 sounds this voice plays in this bar, or nothing.
/// A screen that drew a piano roll here would be drawing a field the cartridge does not
/// have.</para>
///
/// <para><b>Nothing was dropped, and here is the roll call</b> (the wave's law: if a control went
/// under a key, it gets named). The pattern grid, the three section markers per pattern, mute and
/// solo per channel, the transport, save/undo/redo, the whole-song overview and the playhead are
/// <b>all on screen at once</b>, with no overlay and no mode. What is not on screen is
/// fifty-four of the sixty-four patterns — the grid holds ten — and that is answered rather than
/// hidden: the overview shows every one of the sixty-four at all times, and clicking it brings
/// any of them into the grid. The arithmetic is in <see cref="MusicEditorLayout"/>'s type
/// comment.</para>
///
/// <para><b>Mute and solo are drawn, never saved.</b> A channel the author has silenced for
/// listening keeps its numbers on screen and loses only its brightness — dim instead of text ink,
/// with its M or S toggle lit. That is the visible half of the promise
/// <see cref="MusicEditorView"/> makes in prose: the cartridge's bytes do not know this screen
/// exists.</para>
///
/// <para><b>Not the cartridge's console.</b> The framebuffer written here belongs to the shell
/// (<see cref="ShellScreen"/>); the golden master the CI compares between architectures is
/// <see cref="CartSession"/>'s, and no call in this file can reach it.</para>
///
/// <para><b>Cost, measured rather than waved away.</b> The heaviest loop on this frame is the
/// overview's 64 rows x 4 channels of a 3x1 fill — 256 spans of three pixels — against the 14400
/// pixels the <c>Cls</c> on the same frame writes. This is drawing, not simulation: it happens
/// once per rendered frame, never inside a tick, and no rewind replays it.</para>
/// </summary>
public static class MusicEditorRenderer
{
    /// <summary>What the tooltip field says when no control is hovered — TIC-80's <c>Names[mode]</c>.</summary>
    public const string ScreenName = "MUSIC";

    /// <summary>What a silent channel's cell reads. Two characters, so a rest and a slot number are the same width.</summary>
    public const string RestText = "--";

    /// <summary>The one-character faces of the three section markers, in <see cref="MusicFlagColumn"/> order.</summary>
    private static readonly string[] _flagFaces = { "[", "]", "X" };

    /// <summary>The layout this screen is drawn with; the router asks for the same one, so picture and clicks cannot disagree.</summary>
    public static MusicEditorLayout LayoutFor(ShellScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return MusicEditorLayout.Compute(screen.Width, screen.Height);
    }

    /// <summary>
    /// One frame of the music editor. Owns the whole surface: it resets the console's drawing
    /// state and clears, so nothing another screen left behind can bend these pixels.
    /// <paramref name="view"/> is the very state the router's hit tests read, so the picture and
    /// the clicks cannot disagree; <paramref name="hover"/> and <paramref name="tooltipVisible"/>
    /// come from the shell's <see cref="IconHoverTracker"/> — the hovered control's frame lights
    /// up immediately, the text label only after the tracker's three seconds, and the label lands
    /// in the top band rather than under the pointer
    /// (<see cref="ConsoleChrome.TooltipChars"/> explains why).
    /// </summary>
    /// <returns>The layout used, so a test can assert against exactly what was drawn.</returns>
    public static MusicEditorLayout Draw(
        ShellScreen screen, MusicEditorSession session, MusicEditorView view,
        HoverTarget? hover, bool tooltipVisible, IndexFormat indexes = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        MusicEditorLayout layout = LayoutFor(screen);
        VirtualConsole console = screen.Console;
        screen.Begin();
        console.Cls(Ink);

        DrawBands(console, layout.Chrome);
        DrawHeader(console, layout, view);
        DrawRows(console, layout, session, view);
        DrawOverview(console, layout, session, view);
        DrawButtons(console, layout, session, view, hover);

        DrawStatusText(console, layout.Chrome, Coordinates(session, view), Summary(session, indexes));
        DrawMessageLine(
            console, layout.Chrome, view.ExitPromptShown, session.SaveError, StandingNotice(session));
        DrawTooltipField(
            console, layout.Chrome,
            tooltipVisible && hover is HoverTarget target ? TooltipText(target) : null,
            ScreenName);
        return layout;
    }

    /// <summary>
    /// The status band's left field: which channel the cursor stands in, what that cell plays,
    /// and — while the song sounds — where the playhead is. The three facts nothing else on the
    /// screen can say in words: the grid says them in two digits and the overview in single
    /// pixels.
    /// </summary>
    public static string Coordinates(MusicEditorSession session, MusicEditorView view)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        int slot = session.ChannelSlot(session.CursorPattern, session.CursorChannel);
        string under = slot < 0 ? RestText : $"{slot:00}";
        string line = $"CH {session.CursorChannel + 1}  SLOT {under}";
        return view.Playing && view.PlayingPattern >= 0
            ? $"{line}  PLAY {view.PlayingPattern:00}"
            : line;
    }

    /// <summary>
    /// The status band's right field: which of the 64 patterns the cursor is on. Right-aligned to
    /// the screen's edge by <see cref="ConsoleChromeRenderer.DrawStatusText"/>, so it stops
    /// jumping when it gains a digit — the same shape the sprite screen's <c>#003</c>, the map's
    /// and the sound screen's <c>SFX 00</c> have.
    /// </summary>
    public static string Summary(MusicEditorSession session, IndexFormat indexes = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        // The shell's base, not this screen's — see SfxEditorRenderer.Summary for the argument.
        return indexes.Slot("PAT", session.CursorPattern);
    }

    /// <summary>
    /// The screen's standing line: the read-only notice, and nothing else. It is the one fact that
    /// changes what typing does, and it has to be said before the author types rather than at save
    /// time — that is the whole reason <see cref="MusicEditorSession.BankReadOnly"/> is a public
    /// property. Cut for forty columns here, at the one place that knows what it says, rather than
    /// truncated at the one place that knows how wide the line is: a truncated sentence ends
    /// mid-word. Same trade as its four siblings.
    /// </summary>
    public static string? StandingNotice(MusicEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        // The clipboard's refusal wins, on all four screens — see SfxEditorRenderer.StandingNotice.
        return session.ClipboardNotice
            ?? (session.BankReadOnly
                ? $"READ-ONLY: {MusicEditorSession.MusicSourceFileName.ToUpperInvariant()} OWNS THIS SONG"
                : null);
    }

    /// <summary>
    /// The hover label for whichever kind of target is under the pointer: a button gets
    /// <see cref="EditorIcons.MusicTooltip"/>, and every control that is <em>not</em> a button —
    /// the grid, the section markers, the channel header, the overview — gets
    /// <see cref="EditorIcons.MusicRegionTooltip"/>, which is where this screen's keys are
    /// announced. The cut to the field's width belongs to <see cref="ConsoleChrome.FitTooltip"/>,
    /// the only thing that knows how wide the field is.
    ///
    /// <para><b>A target this screen does not recognise means "no label", never an exception.</b>
    /// The tracker is cleared the moment the screen changes (<see cref="IconHoverTracker.Clear"/>
    /// carries the crash this rule answers), so a foreign target should not arrive here at all —
    /// this is the second lock on the same door. It is worth having because the caller is
    /// <c>Draw</c>: an exception thrown while painting a frame reaches nothing that can recover,
    /// and the console dies with the author's unsaved work still on screen. No tooltip is worth
    /// that. Returning null is what the field already does when nothing is hovered, so the
    /// degraded picture is one the eye has seen before.</para>
    /// </summary>
    public static string? TooltipText(in HoverTarget target)
    {
        if (target.Button is EditorButton button)
        {
            return EditorIcons.MusicTooltip(button);
        }
        return target.Music is MusicRegion.None ? null : EditorIcons.MusicRegionTooltip(target.Music);
    }

    /// <summary>
    /// The channel header: a number and two toggles per channel, and the rule under the band. A
    /// lit toggle wears the library's blue plate and a bright face, an idle one is dim ink — fill
    /// and brightness carry the signal, never hue alone, the same rule the sprite screen's flag
    /// toggles follow.
    /// </summary>
    private static void DrawHeader(VirtualConsole console, in MusicEditorLayout layout, MusicEditorView view)
    {
        for (int channel = 0; channel < MusicEditorLayout.ChannelCount; channel++)
        {
            Rectangle cell = layout.ChannelHeaderRect(channel);
            string number = (channel + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            console.Print(
                number, cell.X + 1, ConsoleChrome.ButtonTextY(cell),
                view.ChannelAudible(channel) ? Text : Dim);
            DrawToggle(console, layout.MuteRect(channel), "M", view.ChannelMuted(channel));
            DrawToggle(console, layout.SoloRect(channel), "S", view.ChannelSoloed(channel));
        }
        console.RectFill(layout.Rows.X, layout.HeaderRuleY, layout.Rows.Width, 1, Dim);
    }

    /// <summary>One mute or solo toggle.</summary>
    private static void DrawToggle(VirtualConsole console, Rectangle rect, string face, bool on)
    {
        if (on)
        {
            console.RectFill(rect.X, rect.Y, rect.Width, rect.Height, ActiveBg);
        }
        console.Print(
            face, ConsoleChrome.ButtonTextX(rect, face), ConsoleChrome.ButtonTextY(rect),
            on ? Bright : Dim);
    }

    /// <summary>
    /// The ten tracker rows on screen. The cursor's row gets a dim band under everything, so the
    /// eye can follow one bar across the number, the markers and the four voices — the same trick
    /// the sound screen's step column plays across its three grids. The playhead's row is marked
    /// in warn yellow instead, because while the song plays that is the row the ear is on.
    /// </summary>
    private static void DrawRows(
        VirtualConsole console, in MusicEditorLayout layout, MusicEditorSession session, MusicEditorView view)
    {
        int first = view.FirstPattern;
        for (int row = 0; row < layout.VisibleRows; row++)
        {
            int pattern = first + row;
            if (pattern >= MusicEditorLayout.PatternCount)
            {
                break;
            }
            bool playing = view.Playing && view.PlayingPattern == pattern;
            if (pattern == session.CursorPattern)
            {
                Fill(console, layout.RowRect(pattern, first), Dim);
            }
            Rectangle number = layout.NumberRect(pattern, first);
            if (playing)
            {
                console.RectFill(number.X, number.Y, 1, number.Height, Warn);
            }
            string label = $"{pattern:00}";
            console.Print(
                label, ConsoleChrome.ButtonTextX(number, label), ConsoleChrome.ButtonTextY(number),
                playing ? Warn : Text);

            for (int i = 0; i < MusicEditorLayout.FlagColumns; i++)
            {
                var column = (MusicFlagColumn)i;
                DrawFlagCell(
                    console, layout.FlagCellRect(pattern, column, first), _flagFaces[i],
                    session.HasFlag(pattern, MusicEditorView.FlagBit(column)));
            }

            for (int channel = 0; channel < MusicEditorLayout.ChannelCount; channel++)
            {
                DrawChannelCell(console, layout, session, view, pattern, channel);
            }
        }
    }

    /// <summary>One section marker: a lit one on the blue plate with a bright face, an idle one dim on ink.</summary>
    private static void DrawFlagCell(VirtualConsole console, Rectangle rect, string face, bool set)
    {
        if (set)
        {
            console.RectFill(rect.X, rect.Y, rect.Width, rect.Height, ActiveBg);
        }
        console.Print(
            face, ConsoleChrome.ButtonTextX(rect, face), ConsoleChrome.ButtonTextY(rect),
            set ? Bright : Dim);
    }

    /// <summary>
    /// One channel cell: the slot it plays, or <see cref="RestText"/>. A cell inside the marked
    /// rectangle wears the library's blue; a cell of a channel the author has silenced for
    /// listening keeps its number and loses its brightness; the cursor's cell wears a bright
    /// frame, drawn last so it stays readable over either.
    ///
    /// <para>A half-typed slot number is shown in the cursor's cell as the digit and an
    /// underscore — see <see cref="MusicEditorView.PendingDigit"/> for why the state is visible
    /// rather than secret.</para>
    /// </summary>
    private static void DrawChannelCell(
        VirtualConsole console, in MusicEditorLayout layout, MusicEditorSession session,
        MusicEditorView view, int pattern, int channel)
    {
        Rectangle cell = layout.ChannelCellRect(pattern, channel, view.FirstPattern);
        bool cursor = pattern == session.CursorPattern && channel == session.CursorChannel;
        if (InSelection(session, pattern, channel))
        {
            console.RectFill(cell.X, cell.Y, cell.Width, cell.Height, ActiveBg);
        }
        int slot = session.ChannelSlot(pattern, channel);
        string text = cursor && view.PendingDigit != MusicEditorView.NoDigit
            ? $"{view.PendingDigit}_"
            : slot < 0 ? RestText : $"{slot:00}";
        byte ink = slot < 0 ? Dim : view.ChannelAudible(channel) ? Text : Dim;
        console.Print(
            text, ConsoleChrome.ButtonTextX(cell, text), ConsoleChrome.ButtonTextY(cell),
            cursor ? Bright : ink);
        if (cursor)
        {
            console.Rect(cell.X, cell.Y, cell.Width, cell.Height, Bright);
        }
    }

    /// <summary>True when a cell lies inside the session's marked rectangle.</summary>
    private static bool InSelection(MusicEditorSession session, int pattern, int channel) =>
        session.HasSelection
        && pattern >= session.SelectionPattern
        && pattern < session.SelectionPattern + session.SelectionPatterns
        && channel >= session.SelectionChannel
        && channel < session.SelectionChannel + session.SelectionChannels;

    /// <summary>
    /// The whole song, one pixel row per pattern — the answer to the ten rows the grid can hold
    /// (see <see cref="MusicEditorLayout"/>'s arithmetic). A sounding channel leaves a mark, a
    /// silent one leaves ink, a section flag lights the two-pixel column at the right, the frame
    /// shows which slice the grid is editing, and the playhead is a single warn pixel on the box's
    /// own left edge. It is the map screen's minimap, applied to a song.
    /// </summary>
    private static void DrawOverview(
        VirtualConsole console, in MusicEditorLayout layout, MusicEditorSession session, MusicEditorView view)
    {
        Rectangle box = layout.Overview;
        console.Rect(box.X, box.Y, box.Width, box.Height, Dim);
        for (int pattern = 0; pattern < MusicEditorLayout.PatternCount; pattern++)
        {
            for (int channel = 0; channel < MusicEditorLayout.ChannelCount; channel++)
            {
                if (session.ChannelIsSilent(pattern, channel))
                {
                    continue;
                }
                Rectangle mark = layout.OverviewChannelRect(pattern, channel);
                console.RectFill(mark.X, mark.Y, mark.Width - 1, mark.Height,
                    view.ChannelAudible(channel) ? Text : Dim);
            }
            byte flags = session.PatternFlags(pattern);
            Rectangle flagCell = layout.OverviewFlagRect(pattern);
            if ((flags & (MusicEditorSession.FlagLoopStart | MusicEditorSession.FlagLoopEnd)) != 0)
            {
                console.Pset(flagCell.X, flagCell.Y, Bright);
            }
            if ((flags & MusicEditorSession.FlagStop) != 0)
            {
                console.Pset(flagCell.X + 1, flagCell.Y, Error);
            }
        }
        Rectangle window = layout.OverviewWindowRect(view.FirstPattern);
        console.Rect(window.X, window.Y, window.Width, window.Height, Bright);
        if (view.Playing && view.PlayingPattern >= 0)
        {
            console.Pset(box.X, layout.OverviewRowRect(view.PlayingPattern).Y, Warn);
        }
    }

    /// <summary>
    /// Every icon-button through the one mechanism <see cref="ConsoleChromeRenderer.DrawButton"/>
    /// owns. The only decisions this screen makes are its own tab's highlight and the play
    /// button's two faces — a triangle while the song is silent, a square while it sounds, the
    /// same "one identity, two faces" rule Save has carried since wave 2e and the sound screen's
    /// transport since wave R5.
    /// </summary>
    private static void DrawButtons(
        VirtualConsole console, in MusicEditorLayout layout, MusicEditorSession session,
        MusicEditorView view, HoverTarget? hover)
    {
        foreach (EditorButtonPlace place in layout.Buttons)
        {
            var state = new EditorButtonState(
                Active: place.Id == EditorButton.MusicTab
                    || (place.Id == EditorButton.ToolPlay && view.Playing),
                Hovered: hover is HoverTarget target && target.Button == place.Id,
                Dirty: session.IsDirty,
                CanUndo: session.CanUndo,
                CanRedo: session.CanRedo);
            EditorIcon icon = place.Id == EditorButton.ToolPlay && view.Playing
                ? EditorIcon.Stop
                : EditorIcons.IconFor(place.Id);
            DrawButton(console, place, state, icon, text: null);
        }
    }

    /// <summary>One filled rectangle, a layout rectangle unpacked into the console's call.</summary>
    private static void Fill(VirtualConsole console, Rectangle rect, byte color) =>
        console.RectFill(rect.X, rect.Y, rect.Width, rect.Height, color);
}
