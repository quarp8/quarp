using Quarp.Api;

namespace Digger;

/// <summary>
/// Digger — a Boulder-Dash-lite cave for QUARP-8 (M4 stage 3 demo).
/// Dig through the dirt, collect gems, wake up boulders and do not stand under them: the
/// exit unlocks once enough gems are in the pouch, and walking into it clears the level.
///
/// <para><b>The level is a real map</b> (docs/MAP-FORMAT.md): <c>map.csv</c> is the source,
/// <c>quarp map build carts/digger</c> makes <c>map.bin</c>, and the cartridge reads it with
/// <see cref="Cartridge.Mget"/> / writes it with <see cref="Cartridge.Mset"/>. The cave is
/// 40x24 cells against a 16x8 window, so the camera follows the player across it.</para>
///
/// <para><b>The map owns the level, the code owns the rules.</b> Nothing about the cave is
/// spelled out twice: <see cref="Init"/> scans the map once and derives the level bounds, the
/// number of gems, where the player starts (a marker tile that is erased on the spot) and
/// where the exit is. Redrawing the cave in Tiled therefore cannot silently disagree with a
/// constant in here — the only number this file owns about gems is how many you are allowed
/// to miss (<see cref="GemsSpare"/>).</para>
///
/// <para><b>Sprites are painted in code</b> with <see cref="Cartridge.Sset"/> (M4 Р16: no
/// hand-drawn binaries in the demos), and the tile properties the rules ask about — solid,
/// diggable, round, gem — live in sprite flags via <see cref="Cartridge.Fset"/>, which is
/// where map semantics belong (MAP-FORMAT §10).</para>
///
/// <para><b>Time is a grid step, not a tick.</b> Every <see cref="StepTicks"/> ticks the world
/// advances once: the player moves, then everything unsupported falls. Between steps the
/// pressed direction is latched, so a tap made off-beat is not swallowed — and a press pays for
/// exactly one of those steps, however long the key stays down, until the hold gate opens
/// <see cref="HoldRepeatTicks"/> ticks later (<see cref="LatchDirection"/>).</para>
/// </summary>
public sealed class DiggerGame : Cartridge
{
    // --- tiles: the legend of map.csv, and the sprite index each cell draws with ---
    private const byte TileEmpty = 0;           // dug out; tile 0 is never drawn (API-8 §3)
    private const byte TileDirt = 1;
    private const byte TileWall = 2;
    private const byte TileBoulder = 3;         // at rest
    private const byte TileGem = 4;
    private const byte TileExitClosed = 5;
    private const byte TileExitOpen = 6;
    private const byte TileStart = 7;           // player marker, erased by Init — never drawn
    private const byte TileBoulderFalling = 8;  // same art as TileBoulder, different rules

    private const int SprPlayer = 9;

    // --- sprite flags (Fset in Init; Fget is how the rules ask a cell what it is) ---
    private const int FlagSolid = 0;            // the player cannot walk into it
    private const int FlagDiggable = 1;
    private const int FlagRound = 2;            // a boulder rolls off it
    private const int FlagGem = 3;

    // --- geometry ---
    // TileSize, the glyph box and the sheet stride are properties of the sprite sheet and the
    // system font (SPEC-8 §3, API-8 §3), not of the screen: the screen is asked for its size.
    private const int TileSize = 8;
    private const int SheetColumns = 16;        // 128px sheet / 8px sprite
    private const int GlyphW = 4;
    private const int GlyphH = 6;
    private const int HudRows = 1;
    private const int HudH = HudRows * GlyphH + 2;

    // The map is 256x72 cells on every profile (SPEC-8 §3). These two numbers describe the
    // map, not the display — the 72 here is cells of map, and it would not move if the screen
    // did. Init walks all of it once to find out how much of it the author actually drew.
    private const int MapW = 256;
    private const int MapH = 72;

    // --- tuning ---
    private const int StepTicks = 6;            // 10 grid steps a second
    // How long a held direction waits before it repeats. Three grid steps rather than a round
    // number of milliseconds on purpose: a gate that is a whole number of steps long makes the
    // wait exactly this many ticks whichever tick of the step cycle the press landed on (see
    // LatchDirection). 18 ticks is 0.3 s — a keyboard's own repeat delay, and enough that a
    // human tap can never reach it.
    private const int HoldRepeatTicks = StepTicks * 3;
    private const int GemsSpare = 2;            // gems you may leave in the cave
    private const int PanelBlinkTicks = 40;

    // --- sound (sfx.txt / music.txt next to this file; rebuild with `quarp audio build`) ---
    // Every one of these is asked for from Update, never from Draw: they write chip state,
    // and QRP1004 rejects that in Draw because a rewind would replay the sound a different
    // number of times (API-8 §5).
    private const int SfxDig = 0;
    private const int SfxGem = 1;
    private const int SfxThud = 2;
    private const int SfxPush = 3;
    private const int SfxCrush = 4;
    private const int SfxExit = 5;
    private const int SfxClear = 6;
    private const int MusicTheme = 0;

    // --- colors (SPEC-8 §2 slots) ---
    private const byte ColVoid = 0;
    private const byte ColHud = 3;
    private const byte ColHudWarn = 8;
    private const byte ColHudOpen = 7;
    private const byte ColDivider = 1;
    private const byte ColPanel = 3;
    private const byte ColClear = 7;
    private const byte ColDead = 10;

    // --- directions; DirDx/DirDy are indexed by these ---
    private const int DirNone = -1;
    private const int DirLeft = 0;
    private const int DirRight = 1;
    private const int DirUp = 2;
    private const int DirDown = 3;
    private static readonly int[] DirDx = { -1, 1, 0, 0 };
    private static readonly int[] DirDy = { 0, 0, -1, 1 };

    // Indexed by direction like DirDx/DirDy, so the order of this array *is* the priority that
    // settles a press of two directions at once — and it is the only place that order is
    // written down (both the "went down" and the "still held" reads walk this one array).
    private static readonly Button[] DirButtons = { Button.Left, Button.Right, Button.Up, Button.Down };

    private static readonly string[] Digits = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

    /// <summary>
    /// Sprite art, one string per pixel row, one hex digit per palette slot 0-15 (SPEC-8 §2).
    /// Kept as text because a diff of a redrawn rock should look like a redrawn rock. Index
    /// into this array is the sprite number, so it is also the tile number — a cell drawn by
    /// <see cref="Cartridge.Map"/> is the sprite with the same index (MAP-FORMAT §10).
    /// Slot 0 is transparent by default (API-8 §3 <c>Palt</c>), which is what rounds off the
    /// boulder and the gem against whatever is behind them.
    /// </summary>
    private static readonly string[][] SpriteArt =
    {
        null!,                                  // 0 — empty: never drawn, never painted
        new[]                                   // 1 — dirt: brown with tan grit
        {
            "dddedddd",
            "ddddddde",
            "eddddddd",
            "dddddedd",
            "ddedddde",
            "dddddddd",
            "ddddeddd",
            "dedddddd",
        },
        new[]                                   // 2 — wall: offset brickwork
        {
            "11111111",
            "12222222",
            "12222222",
            "12222222",
            "11111111",
            "22221222",
            "22221222",
            "22221222",
        },
        new[]                                   // 3 — boulder at rest
        {
            "00111100",
            "01332210",
            "13322221",
            "13222221",
            "12222221",
            "12222211",
            "01222110",
            "00111100",
        },
        new[]                                   // 4 — gem
        {
            "00033000",
            "00355300",
            "03555530",
            "35555553",
            "35555553",
            "03555530",
            "00355300",
            "00033000",
        },
        new[]                                   // 5 — exit, barred
        {
            "11111111",
            "1a0000a1",
            "10a00a01",
            "100aa001",
            "100aa001",
            "10a00a01",
            "1a0000a1",
            "11111111",
        },
        new[]                                   // 6 — exit, open and lit
        {
            "11111111",
            "18888881",
            "18333381",
            "18333381",
            "18333381",
            "18333381",
            "18888881",
            "11111111",
        },
        null!,                                  // 7 — start marker: erased by Init, never drawn
        null!,                                  // 8 — falling boulder: copied from 3 (see Init)
        new[]                                   // 9 — the digger
        {
            "00999900",
            "09999990",
            "0ffffff0",
            "0f4ff4f0",
            "00ffff00",
            "06666660",
            "06666660",
            "01100110",
        },
    };

    private enum GameState
    {
        Playing,
        Crushed,
        Cleared,
    }

    // --- level, learned from the map in Init and never written again ---
    private byte[] _initialTiles = System.Array.Empty<byte>();
    private int _levelW;
    private int _levelH;
    private int _gemTotal;
    private int _gemsNeeded;
    private int _startX;
    private int _startY;
    private int _exitX = -1;
    private int _exitY = -1;

    // --- run state ---
    private GameState _state;
    private int _px;
    private int _py;
    private int _gems;
    private int _wantDir = DirNone;
    private int _stepTimer;
    private int _repeatDelay;                   // ticks left before a held direction may repeat

    public override void Init()
    {
        PaintSprites();
        SetTileFlags();
        ScanLevel();
        SnapshotLevel();
        ResetRun();
    }

    public override void Update()
    {
        if (_state != GameState.Playing)
        {
            if (Btnp(Button.Start))
            {
                ResetRun();                     // own state reset, map included — not a console restart
            }

            return;
        }

        LatchDirection();
        _stepTimer--;
        if (_stepTimer <= 0)
        {
            Step();
            _stepTimer = StepTicks;
        }
    }

    public override void Draw()
    {
        Camera();
        Clip();
        Cls(ColVoid);
        DrawHud();
        DrawCave();
        if (_state != GameState.Playing)
        {
            DrawPanel();
        }
    }

    // --- setup -------------------------------------------------------------------------

    /// <summary>
    /// Paints every sprite from <see cref="SpriteArt"/> into the sheet, then copies the resting
    /// boulder over the falling one. The copy is the point: two tiles that must look identical
    /// have one drawing, so a redrawn rock cannot end up half redrawn.
    /// </summary>
    private void PaintSprites()
    {
        for (int sprite = 0; sprite < SpriteArt.Length; sprite++)
        {
            string[] art = SpriteArt[sprite];
            if (art is null)
            {
                continue;
            }

            int originX = sprite % SheetColumns * TileSize;
            int originY = sprite / SheetColumns * TileSize;
            for (int y = 0; y < TileSize; y++)
            {
                string row = art[y];
                for (int x = 0; x < TileSize; x++)
                {
                    Sset(originX + x, originY + y, ColorOf(row[x]));
                }
            }
        }

        int fromX = TileBoulder % SheetColumns * TileSize;
        int fromY = TileBoulder / SheetColumns * TileSize;
        int toX = TileBoulderFalling % SheetColumns * TileSize;
        int toY = TileBoulderFalling / SheetColumns * TileSize;
        for (int y = 0; y < TileSize; y++)
        {
            for (int x = 0; x < TileSize; x++)
            {
                Sset(toX + x, toY + y, Sget(fromX + x, fromY + y));
            }
        }
    }

    /// <summary>Hex digit to palette slot; the art is written with 0-9 and a-f.</summary>
    private static byte ColorOf(char c) => (byte)(c <= '9' ? c - '0' : c - 'a' + 10);

    /// <summary>
    /// Tile properties live in sprite flags, not in an <c>if</c> chain, because that is where
    /// the map format puts them (MAP-FORMAT §10) — <c>Fget(Mget(x, y), flag)</c> is the
    /// question the rules actually ask.
    /// </summary>
    private void SetTileFlags()
    {
        Fset(TileWall, FlagSolid, true);
        Fset(TileBoulder, FlagSolid, true);
        Fset(TileBoulderFalling, FlagSolid, true);
        Fset(TileExitClosed, FlagSolid, true);
        Fset(TileDirt, FlagDiggable, true);
        Fset(TileBoulder, FlagRound, true);
        Fset(TileBoulderFalling, FlagRound, true);
        Fset(TileGem, FlagRound, true);
        Fset(TileGem, FlagGem, true);
    }

    /// <summary>
    /// Reads the cave out of the map: how far it extends, how many gems it holds, where the
    /// player starts and where the exit is. One pass over all 256x72 cells, once, in Init —
    /// cheaper than asking the author to keep four constants in step with a picture.
    /// </summary>
    private void ScanLevel()
    {
        int maxX = 0;
        int maxY = 0;
        _gemTotal = 0;
        for (int y = 0; y < MapH; y++)
        {
            for (int x = 0; x < MapW; x++)
            {
                byte tile = Mget(x, y);
                if (tile == TileEmpty)
                {
                    continue;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (y > maxY)
                {
                    maxY = y;
                }

                if (tile == TileGem)
                {
                    _gemTotal++;
                }
                else if (tile == TileExitClosed)
                {
                    _exitX = x;
                    _exitY = y;
                }
                else if (tile == TileStart)
                {
                    // The marker is scenery for the author and nothing to the game: erase it
                    // now, before the snapshot, so a restart cannot bring it back.
                    _startX = x;
                    _startY = y;
                    Mset(x, y, TileEmpty);
                }
            }
        }

        _levelW = maxX + 1;
        _levelH = maxY + 1;
        _gemsNeeded = _gemTotal - GemsSpare;
        if (_gemsNeeded < 1)
        {
            _gemsNeeded = 1;
        }
    }

    /// <summary>
    /// Keeps a copy of the untouched cave. The map is console memory: digging a cell is gone
    /// for good, so without this a second run would start in the rubble of the first one.
    /// </summary>
    private void SnapshotLevel()
    {
        _initialTiles = new byte[_levelW * _levelH];
        for (int y = 0; y < _levelH; y++)
        {
            for (int x = 0; x < _levelW; x++)
            {
                _initialTiles[(y * _levelW) + x] = Mget(x, y);
            }
        }
    }

    private void ResetRun()
    {
        for (int y = 0; y < _levelH; y++)
        {
            for (int x = 0; x < _levelW; x++)
            {
                Mset(x, y, _initialTiles[(y * _levelW) + x]);
            }
        }

        _state = GameState.Playing;
        _px = _startX;
        _py = _startY;
        _gems = 0;
        _wantDir = DirNone;
        _stepTimer = StepTicks;
        // A key still down from the previous run is not a press, so it serves the whole gate
        // before it may run: pressing Start with a thumb on the stick must not launch the digger.
        _repeatDelay = HoldRepeatTicks;
        Music(MusicTheme);                      // restarts the song on every new run
    }

    // --- simulation --------------------------------------------------------------------

    /// <summary>
    /// Turns buttons into the direction the next grid step will use, under two promises a hand
    /// can predict: <b>one press is exactly one cell</b>, and <b>a hold repeats only after
    /// <see cref="HoldRepeatTicks"/> ticks</b>, evenly from then on.
    ///
    /// <para>The latch itself is what keeps an off-beat tap alive: with a step six ticks long,
    /// most taps happen between two steps, and a direction sampled only at the step would drop
    /// them. What made the cave twitchy was latching from <see cref="Cartridge.Btn"/> alone —
    /// a key that is merely still down re-arms the latch every tick, so a press that spans a
    /// step boundary buys the next step as well. A 100 ms tap did that in five phases of the
    /// six-tick cycle out of six (two cells), and a 133 ms one could reach three, the last of
    /// them landing 83 ms after the key was already up.</para>
    ///
    /// <para>So a press is read as an edge (<see cref="Cartridge.Btnp"/>), and it shuts the
    /// repeat gate behind itself: however long the key stays down afterwards, that press pays
    /// for one step and no more. Because the gate is a whole number of steps long, the first
    /// repeat lands exactly <see cref="HoldRepeatTicks"/> ticks after the step the press bought
    /// — not "somewhere between 13 and 23" — whichever tick of the cycle the press happened on.
    /// Past the gate the level read takes over and the run goes at the full step rate, and
    /// letting go clears the latch so the run stops at the next step rather than one cell
    /// later.</para>
    /// </summary>
    private void LatchDirection()
    {
        // Counted down before the reads, so the gate opens on the tick exactly HoldRepeatTicks
        // after the press — which is a step tick, since the gate is a multiple of StepTicks.
        if (_repeatDelay > 0)
        {
            _repeatDelay--;
        }

        int pressed = DirectionDown(freshOnly: true);
        if (pressed != DirNone)
        {
            _wantDir = pressed;
            _repeatDelay = HoldRepeatTicks;
            return;
        }

        if (_repeatDelay == 0)
        {
            _wantDir = DirectionDown(freshOnly: false);
        }
    }

    /// <summary>
    /// First direction whose button reads down, in the fixed priority order of
    /// <see cref="DirButtons"/>, or <see cref="DirNone"/>. <paramref name="freshOnly"/> asks
    /// "went down on this tick" instead of "is down"; both questions share this one walk so the
    /// priority that settles a diagonal press cannot drift apart between them. The order has to
    /// be decided by something, and something arbitrary and written down beats something that
    /// depends on which key the shell polled first.
    /// </summary>
    private int DirectionDown(bool freshOnly)
    {
        for (int dir = 0; dir < DirButtons.Length; dir++)
        {
            if (freshOnly ? Btnp(DirButtons[dir]) : Btn(DirButtons[dir]))
            {
                return dir;
            }
        }

        return DirNone;
    }

    /// <summary>
    /// One grid step: the player acts, then the world settles. That order is what makes
    /// "keep moving and you live" true — a boulder that came loose because you dug its floor
    /// out arrives in your cell one step later, and by then you have already left.
    /// </summary>
    private void Step()
    {
        MovePlayer(_wantDir);
        _wantDir = DirNone;
        if (_state != GameState.Playing)
        {
            return;
        }

        SettleWorld();
    }

    private void MovePlayer(int dir)
    {
        if (dir == DirNone)
        {
            return;
        }

        int nx = _px + DirDx[dir];
        int ny = _py + DirDy[dir];
        if (nx < 0 || ny < 0 || nx >= _levelW || ny >= _levelH)
        {
            return;                             // the cave is walled anyway; this is the belt
        }

        byte tile = Mget(nx, ny);
        if (tile == TileEmpty)
        {
            _px = nx;
            _py = ny;
        }
        else if (Fget(tile, FlagDiggable))
        {
            Mset(nx, ny, TileEmpty);
            _px = nx;
            _py = ny;
            Sfx(SfxDig);
        }
        else if (Fget(tile, FlagGem))
        {
            Mset(nx, ny, TileEmpty);
            _px = nx;
            _py = ny;
            _gems++;
            Sfx(SfxGem);
            if (_gems == _gemsNeeded)
            {
                OpenExit();
            }
        }
        else if (tile == TileExitOpen)
        {
            _px = nx;
            _py = ny;
            _state = GameState.Cleared;
            Music();                            // the song ends first so the fanfare lands in silence
            Sfx(SfxClear);
        }
        else if (tile == TileBoulder && DirDy[dir] == 0)
        {
            Push(nx, ny, DirDx[dir]);
        }

        // Anything else — wall, barred exit, a boulder in mid-air — is simply not walked into.
    }

    /// <summary>
    /// Shoves a resting boulder one cell sideways and steps into the cell it left. Only a
    /// resting one: a boulder in mid-air has no floor to slide along, and letting the player
    /// bat it out of the air would turn every falling rock from a threat into a toy.
    /// </summary>
    private void Push(int boulderX, int boulderY, int dx)
    {
        int beyondX = boulderX + dx;
        if (beyondX < 0 || beyondX >= _levelW || Mget(beyondX, boulderY) != TileEmpty)
        {
            return;
        }

        Mset(beyondX, boulderY, TileBoulder);
        Mset(boulderX, boulderY, TileEmpty);
        _px = boulderX;
        _py = boulderY;
        Sfx(SfxPush);
    }

    private void OpenExit()
    {
        if (_exitX < 0)
        {
            return;                             // a cave drawn without an exit still plays
        }

        Mset(_exitX, _exitY, TileExitOpen);
        Sfx(SfxExit);
    }

    /// <summary>
    /// Everything that is not held up moves, once, in a fixed order: <b>rows from the bottom
    /// up, cells left to right within a row.</b> The order is the mechanic, not a detail.
    ///
    /// <para>Bottom-up means a boulder that moves down lands in a row this sweep has already
    /// passed, so nothing falls twice in one step and no "already moved" bookkeeping is needed.
    /// Left-to-right leaves exactly one hole — a boulder that rolls <em>right</em> lands in a
    /// cell the sweep has not reached yet — and that one is closed by skipping the cell it
    /// rolled into. A rock that rolled left needs no such care: that cell is behind us.</para>
    ///
    /// <para>A resting boulder with nothing under it does not move on the step it comes loose;
    /// it only becomes <see cref="TileBoulderFalling"/> and moves on the next one. That single
    /// step of grace is the whole difference between "dig under a rock and walk on" and "dig
    /// under a rock and die", and it is what makes the cave fair at 10 steps a second.</para>
    /// </summary>
    private void SettleWorld()
    {
        for (int y = _levelH - 1; y >= 0; y--)
        {
            for (int x = 0; x < _levelW; x++)
            {
                byte tile = Mget(x, y);
                if (tile == TileBoulderFalling)
                {
                    if (IsOpenBelow(x, y))
                    {
                        Mset(x, y, TileEmpty);
                        Mset(x, y + 1, TileBoulderFalling);
                        if (_px == x && _py == y + 1)
                        {
                            Crush();
                        }
                    }
                    else
                    {
                        Mset(x, y, TileBoulder);
                        Sfx(SfxThud);
                    }
                }
                else if (tile == TileBoulder)
                {
                    if (IsOpenBelow(x, y))
                    {
                        Mset(x, y, TileBoulderFalling);
                    }
                    else if (Fget(Mget(x, y + 1), FlagRound) && Roll(x, y) > 0)
                    {
                        x++;                    // rolled right, into a cell this sweep has not seen yet
                    }
                }
            }
        }
    }

    private bool IsOpenBelow(int x, int y) => y + 1 < _levelH && Mget(x, y + 1) == TileEmpty;

    /// <summary>
    /// Rolls a boulder off the round thing it is sitting on, left first, then right; returns
    /// the direction it took, or 0. Left first for the same reason the direction priority is
    /// fixed: the choice has to be written down somewhere, and a rule beats a coincidence of
    /// loop order. A roll never enters the player's cell — being nudged sideways by a rock you
    /// are standing next to is not a death anyone would accept.
    /// </summary>
    private int Roll(int x, int y)
    {
        for (int dx = -1; dx <= 1; dx += 2)
        {
            int sideX = x + dx;
            if (sideX < 0 || sideX >= _levelW)
            {
                continue;
            }

            if (Mget(sideX, y) != TileEmpty || !IsOpenBelow(sideX, y))
            {
                continue;
            }

            if (_px == sideX && _py == y)
            {
                continue;
            }

            Mset(x, y, TileEmpty);
            Mset(sideX, y, TileBoulderFalling);
            return dx;
        }

        return 0;
    }

    private void Crush()
    {
        _state = GameState.Crushed;
        Music();
        Sfx(SfxCrush);
    }

    // --- drawing -----------------------------------------------------------------------

    private void DrawHud()
    {
        int x = Print("GEMS ", 1, 1, ColHud);
        x = PrintInt(_gems, x, 1, _gems >= _gemsNeeded ? ColHudOpen : ColHudWarn);
        x = Print("/", x, 1, ColHud);
        PrintInt(_gemsNeeded, x, 1, ColHud);

        string status = _gems >= _gemsNeeded ? "EXIT OPEN" : "EXIT SHUT";
        Print(status, ScreenWidth - 1 - (status.Length * GlyphW), 1,
            _gems >= _gemsNeeded ? ColHudOpen : ColDivider);
        Line(0, HudH - 1, ScreenWidth - 1, HudH - 1, ColDivider);
    }

    /// <summary>
    /// Draws the visible window of the cave. The window is whatever the console has left under
    /// the HUD — every number here comes from <see cref="Cartridge.ScreenWidth"/> and
    /// <see cref="Cartridge.ScreenHeight"/>, so the same build lays itself out on 128x72 and on
    /// the 160x90 spike (M4 Р5/Р6) without an edit.
    ///
    /// <para>The camera keeps the player on a fixed cell of that window — half the visible rows
    /// above him, half the visible columns to his left — and clamps at the edges of the cave.
    /// Half the rows above rather than a third is deliberate: on eight visible rows that buys
    /// four cells of warning about what is coming down, and everything in this game comes
    /// down.</para>
    /// </summary>
    private void DrawCave()
    {
        int fieldH = ScreenHeight - HudH;
        int visibleCols = ScreenWidth / TileSize;
        int visibleRows = fieldH / TileSize;
        int camX = Clamp((_px - (visibleCols / 2)) * TileSize, (_levelW * TileSize) - ScreenWidth);
        int camY = Clamp((_py - (visibleRows / 2)) * TileSize, (_levelH * TileSize) - fieldH);

        Clip(0, HudH, ScreenWidth, fieldH);
        Camera(camX, camY - HudH);              // world y = camY lands on the first row under the HUD
        int cellX = camX / TileSize;
        int cellY = camY / TileSize;
        // One extra cell each way: on a screen whose field is not a whole number of tiles
        // (160x90 leaves 82px) the clamped camera stops mid-cell, and the last row must still
        // be drawn.
        Map(cellX, cellY, cellX * TileSize, cellY * TileSize, visibleCols + 2, visibleRows + 2);
        Spr(SprPlayer, _px * TileSize, _py * TileSize);
        Camera();
        Clip();
    }

    /// <summary>Clamps a camera coordinate into [0, max]; a cave smaller than the screen pins to 0.</summary>
    private static int Clamp(int value, int max)
    {
        if (value > max)
        {
            value = max;
        }

        return value < 0 ? 0 : value;
    }

    private void DrawPanel()
    {
        string title = _state == GameState.Cleared ? "LEVEL CLEAR" : "CRUSHED";
        const string prompt = "PRESS START";
        int panelW = (prompt.Length * GlyphW) + (GlyphW * 2);
        int panelH = (GlyphH * 3) + 6;
        int panelX = (ScreenWidth - panelW) / 2;
        int panelY = HudH + ((ScreenHeight - HudH - panelH) / 2);

        RectFill(panelX, panelY, panelW, panelH, ColVoid);
        Rect(panelX, panelY, panelW, panelH, ColPanel);
        Print(title, panelX + ((panelW - (title.Length * GlyphW)) / 2), panelY + 4,
            _state == GameState.Cleared ? ColClear : ColDead);
        if (Ticks % PanelBlinkTicks < PanelBlinkTicks * 2 / 3)
        {
            Print(prompt, panelX + ((panelW - (prompt.Length * GlyphW)) / 2), panelY + 4 + (GlyphH * 2), ColHud);
        }
    }

    /// <summary>Prints 0-99 without allocating; returns the x after the last digit.</summary>
    private int PrintInt(int value, int x, int y, byte color)
    {
        if (value >= 10)
        {
            x = Print(Digits[value / 10 % 10], x, y, color);
        }

        return Print(Digits[value % 10], x, y, color);
    }
}
