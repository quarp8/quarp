namespace Quarp.Shell.Desktop;

/// <summary>
/// The boot screen's wordmark — QUARP and its four game-tile dice — as a tiny indexed
/// bitmap, lifted pixel for pixel from the owner's mockup (quarp.pixelforge, the boot-menu
/// order of 2026-08-24). The mockup was drawn in placeholder neon; each of its five colors
/// maps to the nearest slot of the console's own visible sixteen (SPEC-8 §2), because the
/// palette is the console's identity and the boot screen is the first thing that palette
/// ever gets to say.
///
/// <para>Same discipline as <see cref="EditorIcons"/>' masks: the grid below is <b>data</b>,
/// one char per pixel ('.' transparent, letters naming palette slots), excluded from the
/// chrome count by the markers <c>scripts/count-chrome.sh</c> anchors on. Editing the
/// picture means editing these strings — there is no second copy anywhere.</para>
/// </summary>
public static class MenuArt
{
    /// <summary>What each grid letter means: a visible-palette slot (SPEC-8 §2 numbering).</summary>
    private static int SlotOf(char c) => c switch
    {
        'w' => 3,   // white       — the wordmark (mockup's light gray)
        'c' => 5,   // sky blue    — the plus tile (mockup's cyan)
        'g' => 7,   // green       — the cross tile
        'y' => 8,   // yellow      — the dot tile
        'm' => 11,  // pink        — the diamond tile (mockup's magenta)
        _ => -1,
    };

    private static readonly string[] Rows =
    {
        "..............................................................ccccc......",
        ".wwwww.....ww...ww....wwwww....wwwww.....wwwww................ccwcc.ggggg",
        "wwwwwww....ww...ww...wwwwwww..wwwwwww...wwwwwww...............cwwwc.gwgwg",
        "ww...ww....ww...ww...ww...ww..ww...ww...ww...ww...............ccwcc.ggwgg",
        "ww...ww....ww...ww...ww...ww..ww...ww...ww...ww...............ccccc.gwgwg",
        "ww...ww....ww...ww...wwwwwww..wwwwwww...wwwwwww..........yyyyy......ggggg",
        "ww...ww....ww...ww...wwwwwww..wwwwww....wwwwww...........ywwwy.mmmmm.....",
        "ww...ww....ww...ww...ww...ww..ww..ww....ww...............ywywy.mmwmm.....",
        "wwwwwww....wwwwwww...ww...ww..ww...ww...ww...............ywwwy.mwmwm.....",
        ".wwwwwww....wwwww....ww...ww..ww...ww...ww...............yyyyy.mmwmm.....",
        "......ww.......................................................mmmmm.....",
        ".......ww................................................................",
    };

    /// <summary>Grid width in pixels.</summary>
    public static int Width => Rows[0].Length;

    /// <summary>Grid height in pixels.</summary>
    public static int Height => Rows.Length;

    /// <summary>
    /// Palette slot at (x, y), or -1 for a transparent pixel. Out of range reads as
    /// transparent, so a renderer looping a padded box cannot crash on the art.
    /// </summary>
    public static int SlotAt(int x, int y) =>
        (uint)y < (uint)Rows.Length && (uint)x < (uint)Rows[y].Length ? SlotOf(Rows[y][x]) : -1;
}
