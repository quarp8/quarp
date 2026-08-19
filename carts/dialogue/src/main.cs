using System;
using System.Collections.Generic;
using Quarp.Api;

namespace Dialogue;

/// <summary>
/// "Two Lights" — a two-hander dialogue scene, and the M4 worst case for text
/// (tasks/open/02-dialogue.md, work order Р7): a harbour pilot and a lighthouse keeper
/// argue about the last drum of lamp oil, the player picks one of two answers, and the
/// scene ends two ways.
///
/// <para>Everything on screen is placed from <see cref="IConsoleApi.ScreenWidth"/> and
/// <see cref="IConsoleApi.ScreenHeight"/>; the numbers 128 and 72 appear nowhere. That is
/// the whole point of the demo for the resolution verdict — the same build has to re-wrap
/// itself when it is run on the 160x90 spike (`quarp run carts/dialogue --profile 8w`),
/// otherwise the comparison would be measuring the source code instead of the screen.</para>
///
/// <para>The two 16x16 portraits are painted pixel by pixel with <see cref="IConsoleApi.Sset"/>
/// in <see cref="Init"/> from the hex-digit art below (Р16: no hand-drawn binaries), so the
/// cartridge carries no gfx.png and the art still diffs as text.</para>
///
/// <para>Text is measured in the system font's own units: a glyph cell is 4x6 px (3x5 ink
/// plus one pixel of air), so a line of N characters is exactly 4N wide. The word wrapper
/// below is written by hand: <see cref="IConsoleApi.Print"/> draws exactly the string it is
/// given, and a dialogue window is the one place where the string is not known until the
/// console has been asked how wide it is. That wrapper is the single biggest thing this
/// demo had to supply for itself.</para>
/// </summary>
public sealed class TwoLights : Cartridge
{
    // --- system font metrics (API-8 §3 "Print") ---
    private const int GlyphW = 4;               // horizontal advance per character
    private const int LineH = 6;                // baseline pitch: 5 px of ink plus 1 px of air
    private const int FirstGlyph = 32;          // the font covers ASCII 32..126 and nothing else
    private const int GlyphCount = 95;

    // --- dialogue window; all of it is thickness, not position (position comes from the API) ---
    private const int BoxBorder = 1;
    private const int BoxPad = 2;
    private const int TextLines = 3;            // the card asks for a three-line window
    private const int PortraitPx = 16;          // 2x2 sprite cells
    private const int PortraitGap = 2;          // air between the portrait and the text column
    private const int NameGap = 1;              // air between the speaker name and the first line

    /// <summary>
    /// Characters typed per tick. Two is a compromise a paper design cannot make for you:
    /// at one the longest page here takes over a second to read itself out, at four the
    /// typewriter stops reading as typing.
    /// </summary>
    private const int RevealPerTick = 2;

    // --- scene above the window ---
    private const int PierH = 6;
    private const int BustH = 11;               // body under the 16x16 head sprite
    private const int StarCount = 22;
    private const int PlankStep = 8;

    // --- speakers ---
    private const int Mara = 0;                 // harbour pilot; the player answers as her
    private const int Osk = 1;                  // lighthouse keeper
    private const int Nobody = -1;

    // --- sound (sfx.txt / music.txt next to this file; rebuild with `quarp audio build`) ---
    // Asked for from Update and Init only: Sfx/Music write chip state, and QRP1004 rejects
    // either of them from Draw outright (API-8 §5).
    private const int SfxPage = 0;
    private const int SfxChoice = 1;
    private const int SfxEnding = 4;
    private const int MusicTheme = 0;

    // --- colors (SPEC-8 §2 palette; 16..31 are the secret twins used for dimming) ---
    private const byte ColInk = 0;
    private const byte ColDim = 1;
    private const byte ColSteel = 2;
    private const byte ColPaper = 3;
    private const byte ColSea = 4;
    private const byte ColSkySlot = 5;          // remapped to master 20 (night) every frame
    private const byte MasterNight = 20;
    private const byte ColMara = 6;
    private const byte ColLamp = 8;
    private const byte ColOsk = 9;
    private const byte ColBand = 10;
    private const byte ColPier = 13;
    private const byte ColPlank = 14;

    private const int AllRevealed = int.MaxValue;

    private enum Phase
    {
        Talk,
        Choose,
        Ending,
    }

    // --- the script -------------------------------------------------------------------
    // Nine replies, ~40 words each: this is the worst case on purpose, so nothing here is
    // trimmed to make a number look better (Р19). Words are counted in carts/dialogue/README.md.

    private const int NextChoice = -1;
    private const int NextEnd = -2;

    private static readonly int[] NodeSpeaker = { Osk, Mara, Osk, Mara, Osk, Mara, Osk, Mara, Osk };
    private static readonly int[] NodeNext = { 1, 2, 3, 4, NextChoice, 6, NextEnd, 8, NextEnd };

    private static readonly string[] NodeText =
    {
        // 0-4: the setup, shared by both branches.
        "The glass is clean, Mara, but the oil is not. Two fingers left in the drum, "
            + "and the storm is walking up the sound like it owns the water. One night. "
            + "That is what we have.",
        "One night is enough if the ferry keeps to the schedule. It never does. Tomas "
            + "loads until the rail is wet, then argues with the tide about it. He will "
            + "be late, and he will be low in the water.",
        "Then we are choosing, and I would rather choose with you than alone at the top "
            + "of the stair. Burn the drum tonight and the light stands until dawn. After "
            + "dawn there is nothing, and the yard boat does not come until Thursday.",
        "Or we keep half the drum dark and I take the skiff out to the shoal with a hand "
            + "lantern. Tomas knows my light. He has followed it drunk, twice. The shoal "
            + "is close enough to row and far enough to matter.",
        "Two ways to spend the same oil. The tower is certain and it is blind after "
            + "sunrise. The skiff is clever and it is one woman in a swell. Say it out "
            + "loud, pilot. Whatever you say, I will hold to it.",
        // 5-6: branch A, the tower.
        "Burn it. A pilot who rows past a dark tower is just another wreck with opinions. "
            + "Tomas needs to see the coast, not me. Give him the whole drum and let the "
            + "morning argue with the yard about Thursday.",
        "Then it is spent. The lamp took every drop and gave back a road on the water, "
            + "and the ferry walked it in at four with the rail dry for once. We are dark "
            + "now, and we are dark together. Thursday will come.",
        // 7-8: branch B, the shoal.
        "Half the drum stays in the cellar. I will be at the shoal before the squall "
            + "turns, with the lantern on the mast and my back to the rocks. If he follows "
            + "me the way he follows a bar sign, we all sleep.",
        "He followed. I watched two lights crawl in past the shoal, the little one leading "
            + "and the fat one complaining, and the tower still had oil at dawn. You were "
            + "wet to the collar and grinning. Thursday can take its time.",
    };

    private const string ChoicePrompt = "What do you say, pilot?";

    // 24 characters each: the cursor eats two of the window's columns, and 2 + 24 is exactly
    // the 26 columns the portrait leaves. Longer options would be clipped, not wrapped.
    private static readonly string[] ChoiceOption =
    {
        "Burn it all in the tower",
        "Row out with the lantern",
    };

    private static readonly int[] ChoiceTarget = { 5, 7 };

    private static readonly string[] EndingTitle = { "THE TOWER HELD", "THE SHOAL LIGHT" };

    private static readonly string[] EndingEpilogue =
    {
        "Dawn came with a dry rail and an empty drum.",
        "Two lights crawled in, and the drum kept a finger.",
    };

    private const string RestartHint = "PRESS START TO PLAY AGAIN";

    private static readonly string[] SpeakerName = { "MARA", "OSK" };
    private static readonly byte[] SpeakerColor = { ColMara, ColOsk };

    /// <summary>Top-left sprite of each 2x2 portrait block: cells 0,1,16,17 and 2,3,18,19.</summary>
    private static readonly int[] PortraitSprite = { 0, 2 };

    // --- portrait art ------------------------------------------------------------------
    // One character per pixel: '.' is the transparent color 0, '0'-'9' and 'a'-'f' are
    // palette slots 0-15. Sixteen rows of sixteen characters; ColorOf does the decoding and
    // Sset does the painting, both in Init.

    private static readonly string[] MaraArt =
    {
        "................",
        "....dddddddd....",
        "...dddddddddd...",
        "..dddffffffddd..",
        "..ddffffffffdd..",
        "..ddffffffffdd..",
        "..dff0ffff0ffd..",
        "..dffffffffffd..",
        "..dfffefffffd...",
        "..dffffaaffffd..",
        "..ddffffffffdd..",
        "...ddffffffdd...",
        "......ffff......",
        "..66663ff36666..",
        ".66666633666666.",
        "6666666336666666",
    };

    private static readonly string[] OskArt =
    {
        "................",
        "...4444444444...",
        "..444444444444..",
        ".44444444444444.",
        "..2eeeeeeeeee2..",
        "..2eeeeeeeeee2..",
        "..2ee0eeee0ee2..",
        "..2eeeeeeeeee2..",
        "..2eeee99eeee2..",
        "..22eeeeeeee22..",
        "..2222aa222222..",
        "...2222222222...",
        "....22222222....",
        ".....222222.....",
        // Rows 14-15 sit at y=22-23, at or below the horizon (DrawSea starts at
        // _horizonY=20 for the 128x72 layout): '4' there is ColSea, the exact slot the
        // sea itself is filled with, so whenever Osk is undimmed (he is speaking, or
        // Draw is in the ending, where CurrentSpeaker is Nobody and nobody is dimmed)
        // that collar trim renders in the sea's own master color and vanishes into it —
        // tasks/open/bug-dialogue-portrait-flicker.md, hat/body flicker. '2' (steel,
        // already this collar's dominant color two rows up) reads against the sea in
        // both palette states and costs nothing else in the silhouette.
        "..222222222222..",
        "2222222222222222",
    };

    private static readonly string[][] Portraits = { MaraArt, OskArt };

    // --- layout, all of it computed in Init from the console's screen size ---
    private int _boxY;
    private int _boxH;
    private int _portraitX;
    private int _portraitY;
    private int _nameY;
    private int _textX;
    private int _textY;
    private int _cols;                          // columns left of the portrait: the narrow case
    private int _wideX;
    private int _wideCols;                      // columns with no portrait and no name
    private int _horizonY;
    private int _pierY;
    private int _headY;
    private int _leftBustX;
    private int _rightBustX;

    // --- wrapped text, built once in Init because the wrapping depends on the screen ---
    private string[] _lines = Array.Empty<string>();
    private int[] _pageFirstLine = Array.Empty<int>();
    private int[] _pageLineCount = Array.Empty<int>();
    private int[] _pageChars = Array.Empty<int>();
    private int[] _nodeFirstPage = Array.Empty<int>();
    private int[] _nodePageCount = Array.Empty<int>();
    private readonly int[] _endFirstLine = new int[2];
    private readonly int[] _endLineCount = new int[2];

    // One string per printable character, so the typewriter costs no allocation per frame.
    private readonly string[] _glyph = new string[GlyphCount];

    private readonly int[] _starX = new int[StarCount];
    private readonly int[] _starY = new int[StarCount];

    // --- simulation state ---
    private Phase _phase;
    private int _node;
    private int _pageInNode;
    private int _revealed;                      // characters of the current page already typed
    private int _choice;                        // selected option, and afterwards the branch taken
    private int _ending;

    public override void Init()
    {
        for (int i = 0; i < GlyphCount; i++)
        {
            _glyph[i] = new string((char)(FirstGlyph + i), 1);
        }

        PaintPortraits();
        ComputeLayout();
        BuildPages();
        PlaceStars();
        ResetScene();
    }

    public override void Update()
    {
        // One phase per tick: the press that leaves Talk must not also answer the choice it
        // just opened, so the branch is taken once, at the top, on the phase as it was.
        switch (_phase)
        {
            case Phase.Talk:
                UpdateTalk();
                break;
            case Phase.Choose:
                UpdateChoose();
                break;
            default:
                UpdateEnding();
                break;
        }
    }

    public override void Draw()
    {
        ApplyNightPalette();
        Cls(ColInk);
        DrawSky();
        DrawSea();
        DrawLighthouse();
        DrawPier();
        DrawBusts();
        Pal();
        DrawWindow();
    }

    // --- simulation --------------------------------------------------------------------

    private void ResetScene()
    {
        _phase = Phase.Talk;
        _node = 0;
        _pageInNode = 0;
        _revealed = 0;
        _choice = 0;
        _ending = 0;
        Music(MusicTheme);
    }

    private void UpdateTalk()
    {
        int total = _pageChars[CurrentPage];
        if (_revealed < total)
        {
            _revealed += RevealPerTick;
            if (_revealed > total)
            {
                _revealed = total;
            }
        }

        if (!AdvancePressed())
        {
            return;
        }

        if (_revealed < total)
        {
            _revealed = total;                  // first press finishes the page instead of skipping it
            Sfx(SfxPage);
            return;
        }

        if (_pageInNode + 1 < _nodePageCount[_node])
        {
            _pageInNode++;
            _revealed = 0;
            Sfx(SfxPage);
            return;
        }

        GoTo(NodeNext[_node]);
    }

    private void UpdateChoose()
    {
        // Clamped, not wrapping: a scripted walkthrough can then tap Down as often as it
        // likes and still land on the same option (carts/dialogue/replays/README.md).
        if (Btnp(Button.Up) && _choice > 0)
        {
            _choice--;
            Sfx(SfxPage);
        }
        else if (Btnp(Button.Down) && _choice < ChoiceOption.Length - 1)
        {
            _choice++;
            Sfx(SfxPage);
        }

        if (AdvancePressed())
        {
            Sfx(SfxChoice);
            GoTo(ChoiceTarget[_choice]);
        }
    }

    private void UpdateEnding()
    {
        if (Btnp(Button.Start))
        {
            ResetScene();
        }
    }

    private void GoTo(int next)
    {
        if (next == NextChoice)
        {
            _phase = Phase.Choose;
            return;
        }

        if (next == NextEnd)
        {
            _phase = Phase.Ending;
            _ending = _choice;
            Music();                            // the theme stops so the sting lands in silence
            Sfx(SfxEnding);
            return;
        }

        // Back to Talk explicitly: this is also the return path out of the choice menu, and
        // leaving the phase alone here froze the scene on the menu forever — the walkthrough
        // caught it as a frame hash that stopped changing 300 ticks early.
        _phase = Phase.Talk;
        _node = next;
        _pageInNode = 0;
        _revealed = 0;
        Sfx(SfxPage);
    }

    private bool AdvancePressed() => Btnp(Button.X) || Btnp(Button.O);

    private int CurrentPage => _nodeFirstPage[_node] + _pageInNode;

    private int CurrentSpeaker => _phase switch
    {
        Phase.Talk => NodeSpeaker[_node],
        Phase.Choose => Mara,                   // the options are the pilot's own words
        _ => Nobody,
    };

    // --- text preparation --------------------------------------------------------------

    private void ComputeLayout()
    {
        int inset = BoxBorder + BoxPad;

        _boxH = 2 * inset + LineH + NameGap + TextLines * LineH;
        _boxY = ScreenHeight - _boxH;
        _portraitX = inset;
        _nameY = _boxY + inset;
        _textX = _portraitX + PortraitPx + PortraitGap;
        _textY = _nameY + LineH + NameGap;
        _cols = (ScreenWidth - inset - _textX) / GlyphW;
        _wideX = inset;
        _wideCols = (ScreenWidth - 2 * inset) / GlyphW;

        // The portrait is centred against the name plus the three lines, not against the
        // box: the box also holds the border and the padding, and centring against those
        // would push the face down against the bottom rule.
        _portraitY = _nameY + (LineH + NameGap + TextLines * LineH - PortraitPx) / 2;

        _horizonY = _boxY / 2;
        _pierY = _boxY - PierH;
        _headY = _pierY - BustH - PortraitPx;
        _leftBustX = ScreenWidth / 6;
        _rightBustX = ScreenWidth - ScreenWidth / 6 - PortraitPx;
    }

    private void BuildPages()
    {
        var lines = new List<string>();
        var pageFirstLine = new List<int>();
        var pageLineCount = new List<int>();
        var pageChars = new List<int>();
        _nodeFirstPage = new int[NodeText.Length];
        _nodePageCount = new int[NodeText.Length];

        for (int node = 0; node < NodeText.Length; node++)
        {
            int firstLine = lines.Count;
            Wrap(NodeText[node], _cols, lines);
            int lineCount = lines.Count - firstLine;

            _nodeFirstPage[node] = pageFirstLine.Count;
            for (int i = 0; i < lineCount; i += TextLines)
            {
                int count = lineCount - i < TextLines ? lineCount - i : TextLines;
                int chars = 0;
                for (int k = 0; k < count; k++)
                {
                    chars += lines[firstLine + i + k].Length;
                }

                pageFirstLine.Add(firstLine + i);
                pageLineCount.Add(count);
                pageChars.Add(chars);
            }

            _nodePageCount[node] = pageFirstLine.Count - _nodeFirstPage[node];
        }

        // The endings drop the portrait and the name, so they get the wide column count —
        // the same window, measured both ways, which is what the Р7 table compares.
        for (int e = 0; e < EndingEpilogue.Length; e++)
        {
            _endFirstLine[e] = lines.Count;
            Wrap(EndingEpilogue[e], _wideCols, lines);
            _endLineCount[e] = lines.Count - _endFirstLine[e];
        }

        _lines = lines.ToArray();
        _pageFirstLine = pageFirstLine.ToArray();
        _pageLineCount = pageLineCount.ToArray();
        _pageChars = pageChars.ToArray();
    }

    /// <summary>
    /// Greedy word wrap into lines of at most <paramref name="cols"/> characters. The console
    /// has no wrapping call: Print takes one string and advances 4 px per character, so where
    /// the line ends is the cartridge's problem. (The core does honour a '\n' inside the
    /// string as a hard break, which API-8 §3 denies — but an authored break is not a wrap,
    /// and a scene that re-lays itself out for the screen it is given needs this either way.)
    /// A word longer than the whole line is cut at the column rather than left to run off the
    /// window — silently clipped text is worse than a visible break.
    /// </summary>
    private static void Wrap(string text, int cols, List<string> lines)
    {
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && text[i] == ' ')
            {
                i++;                            // spaces at a break are eaten, not printed
            }

            if (i >= text.Length)
            {
                return;
            }

            int start = i;
            int lastSpace = -1;
            int j = i;
            while (j < text.Length && j - start < cols)
            {
                if (text[j] == ' ')
                {
                    lastSpace = j;
                }

                j++;
            }

            int end;
            if (j >= text.Length || text[j] == ' ')
            {
                end = j;                        // the rest fits, or the break falls on a space
            }
            else if (lastSpace > start)
            {
                end = lastSpace;
            }
            else
            {
                end = j;                        // one word wider than the window: hard cut
            }

            lines.Add(text.Substring(start, end - start));
            i = end;
        }
    }

    private void PaintPortraits()
    {
        // Portrait p occupies sheet columns 16p..16p+15 of the top two sprite rows, which is
        // exactly the 2x2 block that Spr(PortraitSprite[p], x, y, 2, 2) draws.
        for (int p = 0; p < Portraits.Length; p++)
        {
            string[] art = Portraits[p];
            int originX = p * PortraitPx;
            for (int row = 0; row < art.Length; row++)
            {
                string source = art[row];
                for (int col = 0; col < source.Length; col++)
                {
                    byte color = ColorOf(source[col]);
                    if (color != 0)
                    {
                        Sset(originX + col, row, color);
                    }
                }
            }
        }
    }

    private static byte ColorOf(char c)
    {
        if (c >= '0' && c <= '9')
        {
            return (byte)(c - '0');
        }

        if (c >= 'a' && c <= 'f')
        {
            return (byte)(c - 'a' + 10);
        }

        return 0;                               // '.' and anything else: transparent
    }

    private void PlaceStars()
    {
        // Default seed 0 (API-8 §6): the sky is the same on every machine and every run, and
        // it is placed once — a restart must not shuffle it, or a replay of a restart would
        // draw a different sky.
        for (int i = 0; i < StarCount; i++)
        {
            _starX[i] = RndInt(ScreenWidth);
            _starY[i] = RndInt(_horizonY);
        }
    }

    // --- drawing -----------------------------------------------------------------------

    private void ApplyNightPalette()
    {
        Pal();
        Pal(ColSkySlot, MasterNight);           // the sky is a secret color; nothing else uses slot 5
    }

    /// <summary>
    /// Points every slot the scene draws with at its darker master twin, so the listener can
    /// be pushed back without a second set of sprites. Slot 15 is the exception worth knowing:
    /// its twin 31 is *lighter* skin, so the dim version of light skin is plain tan (14).
    ///
    /// Slot 4 is the second exception, found on the same playtest as slot 15's (this bug's
    /// cousin — tasks/open/idea-palette-dark-twins.md): its literal twin is 20, which is also
    /// <see cref="MasterNight"/> — the exact master color <see cref="ApplyNightPalette"/>
    /// paints the whole sky with, every frame, regardless of who is dimmed. Osk's hood
    /// (rows 1-3 of <see cref="OskArt"/>) sits entirely inside the sky rectangle, so a
    /// dimmed Osk had a hood-shaped hole where the "dark twin" of his own hat color was
    /// pixel-identical to the sky behind it — the hat half of the flicker in
    /// tasks/open/bug-dialogue-portrait-flicker.md, the coat/collar half is in DrawBusts.
    /// 21 ("steel", still a cold master tone) has no other claim in this table.
    /// </summary>
    private void ApplyDimPalette()
    {
        Pal(2, 17);
        Pal(3, 18);
        Pal(4, 21);
        Pal(6, 22);
        Pal(9, 25);
        Pal(10, 26);
        Pal(13, 29);
        Pal(14, 30);
        Pal(15, 14);
    }

    private void DrawSky()
    {
        RectFill(0, 0, ScreenWidth, _horizonY, ColSkySlot);
        for (int i = 0; i < StarCount; i++)
        {
            Pset(_starX[i], _starY[i], (i & 1) == 0 ? ColPaper : ColSteel);
        }
    }

    private void DrawSea()
    {
        RectFill(0, _horizonY, ScreenWidth, _pierY - _horizonY, ColSea);
        for (int y = _horizonY + 3; y < _pierY; y += 4)
        {
            for (int x = (y & 7); x < ScreenWidth; x += 11)
            {
                Line(x, y, x + 2, y, ColDim);
            }
        }

        if (_phase == Phase.Ending && _ending == 1)
        {
            // The skiff that was rowed out to the shoal, still burning at dawn.
            int lx = ScreenWidth * 3 / 4;
            Pset(lx, _pierY - 5, ColLamp);
            Line(lx - 2, _pierY - 3, lx + 2, _pierY - 3, ColSteel);
        }
    }

    private void DrawLighthouse()
    {
        const int towerW = 10;
        int x = (ScreenWidth - towerW) / 2;
        int roomY = _horizonY - 14;
        int shaftY = roomY + 4;

        RectFill(x + 2, shaftY, towerW - 4, _pierY - shaftY, ColSteel);
        RectFill(x + 2, shaftY + 4, towerW - 4, 3, ColBand);
        RectFill(x, roomY, towerW, 4, ColDim);

        bool lit = _phase == Phase.Ending && _ending == 0;
        RectFill(x + 3, roomY + 1, 4, 2, lit ? ColLamp : ColSteel);
        if (lit)
        {
            Line(x - 4, roomY + 2, x - 1, roomY + 2, ColLamp);
            Line(x + towerW, roomY + 2, x + towerW + 3, roomY + 2, ColLamp);
        }
    }

    private void DrawPier()
    {
        RectFill(0, _pierY, ScreenWidth, _boxY - _pierY, ColPier);
        Line(0, _pierY, ScreenWidth - 1, _pierY, ColPlank);
        for (int x = 0; x < ScreenWidth; x += PlankStep)
        {
            Line(x, _pierY + 1, x, _boxY - 1, ColInk);
        }
    }

    private void DrawBusts()
    {
        int speaker = CurrentSpeaker;
        DrawBust(_leftBustX, Mara, ColMara, speaker != Mara && speaker != Nobody);
        // Coat was ColSea: undimmed (Osk speaking, or nobody dimmed in the Ending) that
        // RectFill drew the sea's own slot over the sea, and the body vanished — the other
        // half of the flicker fixed alongside the sprite rows above. ColSteel already owns
        // most of his collar, so the coat now reads as one gray oilskin instead of two.
        DrawBust(_rightBustX, Osk, ColSteel, speaker != Osk && speaker != Nobody);
    }

    private void DrawBust(int x, int who, byte coat, bool dim)
    {
        if (dim)
        {
            ApplyDimPalette();
        }
        else
        {
            Pal();
        }

        Spr(PortraitSprite[who], x, _headY, 2, 2);
        int bodyY = _headY + PortraitPx;
        RectFill(x + 1, bodyY, PortraitPx - 2, _pierY - bodyY, coat);
        RectFill(x + 5, bodyY, PortraitPx - 10, 3, ColPaper);
    }

    private void DrawWindow()
    {
        RectFill(0, _boxY, ScreenWidth, _boxH, ColInk);
        Rect(0, _boxY, ScreenWidth, _boxH, ColSteel);

        if (_phase == Phase.Ending)
        {
            DrawEnding();
            return;
        }

        int speaker = CurrentSpeaker;
        Spr(PortraitSprite[speaker], _portraitX, _portraitY, 2, 2);
        Print(SpeakerName[speaker], _textX, _nameY, SpeakerColor[speaker]);

        if (_phase == Phase.Choose)
        {
            DrawChoice();
            return;
        }

        int page = CurrentPage;
        DrawLines(_pageFirstLine[page], _pageLineCount[page], _textX, _revealed);
        if (_revealed >= _pageChars[page])
        {
            DrawMoreArrow();
        }
    }

    private void DrawChoice()
    {
        Print(ChoicePrompt, _textX, _textY, ColDim);
        for (int i = 0; i < ChoiceOption.Length; i++)
        {
            int y = _textY + (i + 1) * LineH;
            bool picked = i == _choice;
            if (picked)
            {
                Print(">", _textX, y, ColLamp);
            }

            Print(ChoiceOption[i], _textX + 2 * GlyphW, y, picked ? ColPaper : ColSteel);
        }
    }

    private void DrawEnding()
    {
        // The bottom line is spoken for by the restart hint, so the epilogue gets the two
        // above it — clamped here rather than trusted, because the line count comes out of
        // the wrapper and the wrapper answers to the screen width, not to this method.
        int count = _endLineCount[_ending];
        if (count > TextLines - 1)
        {
            count = TextLines - 1;
        }

        Print(EndingTitle[_ending], Centered(EndingTitle[_ending]), _nameY, ColLamp);
        DrawLines(_endFirstLine[_ending], count, _wideX, AllRevealed);
        Print(RestartHint, Centered(RestartHint), _textY + (TextLines - 1) * LineH, ColDim);
    }

    private int Centered(string text) => _wideX + (_wideCols - text.Length) * GlyphW / 2;

    /// <summary>
    /// Draws up to <paramref name="count"/> wrapped lines, revealing only the first
    /// <paramref name="revealed"/> characters of the block. A fully revealed line goes out in
    /// one Print call; only the line the typewriter is in the middle of is spelled out glyph
    /// by glyph, so a finished page costs three calls instead of seventy-eight.
    /// </summary>
    private void DrawLines(int firstLine, int count, int x, int revealed)
    {
        for (int i = 0; i < count && i < TextLines; i++)
        {
            string line = _lines[firstLine + i];
            if (revealed <= 0)
            {
                return;
            }

            if (revealed >= line.Length)
            {
                Print(line, x, _textY + i * LineH, ColPaper);
            }
            else
            {
                DrawPartialLine(line, revealed, x, _textY + i * LineH, ColPaper);
                return;
            }

            revealed -= line.Length;
        }
    }

    private void DrawPartialLine(string line, int count, int x, int y, byte color)
    {
        for (int i = 0; i < count; i++)
        {
            char c = line[i];
            if (c > ' ' && c < FirstGlyph + GlyphCount)
            {
                Print(_glyph[c - FirstGlyph], x, y, color);
            }

            x += GlyphW;
        }
    }

    /// <summary>The "there is more" triangle, parked under the portrait: the text column is
    /// full to its last pixel on a long line, and an arrow in the corner would sit on top of
    /// a word.</summary>
    private void DrawMoreArrow()
    {
        if (Ticks % 40 >= 24)
        {
            return;
        }

        int x = _portraitX + PortraitPx / 2 - 2;
        int y = _portraitY + PortraitPx + 1;
        Line(x, y, x + 4, y, ColLamp);
        Line(x + 1, y + 1, x + 3, y + 1, ColLamp);
        Pset(x + 2, y + 2, ColLamp);
    }
}
