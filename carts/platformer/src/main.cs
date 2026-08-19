using Quarp.Api;

namespace Platformer;

/// <summary>
/// TOWER CLIMB — the vertical platformer of M4 stage 3, and the demo the resolution verdict
/// leans on hardest (M4 Р7): a game whose fairness is a direct function of how far below
/// himself the player can see.
///
/// <para>The tower is 24 tiles wide and fills the whole 72-row map — about seven screens of
/// climbing at 160x90. It is drawn from <c>map.csv</c> through <c>quarp map build</c> (tools/tower.py
/// generates the CSV); the sprite sheet is painted pixel by pixel with <see cref="Sset"/> in
/// <see cref="Init"/>, so the cartridge carries no <c>gfx.png</c>. What each tile *does* comes
/// from sprite flags set in <see cref="Init"/> — solid, one-way platform, deadly, gem, goal —
/// which is the idiom MAP-FORMAT §10 names: <c>Fget(Mget(cx, cy), flag)</c>.</para>
///
/// <para><b>The one number this demo exists to produce.</b> Two chimneys (columns 7-8 and
/// 15-16) run the full height of the tower and no platform ever covers them; missing a jump
/// drops you down one, and the bottom of a chimney is a spike pit. Surviving a fall means
/// steering out of the chimney onto a platform you can see coming — so the honest question is
/// how many ticks of warning the screen gives at terminal speed. With the camera centering the
/// player and nothing else, 160x90 gives 37 visible pixels below the feet and terminal fall is
/// 3 px/tick: <b>12.33 ticks, 206 ms</b> — short of the 15-tick floor Р7 set in advance (the
/// historical 128x72 measurement was worse still, 28 px / 9.33 ticks / 156 ms; the full
/// arithmetic and the recovery-time comparison built on both numbers, the one that decided
/// ADR-021, is worked out in this cartridge's own <c>README.md</c> and preserved as history in
/// <c>docs/milestones/M4-MEASUREMENTS.md</c>, not repeated here). The camera therefore leads the fall (see
/// <see cref="CamLeadTicks"/>), and that lead is measured and reported rather than hidden: the
/// physics below was chosen to be genre-typical and was not moved to meet the threshold (Р19).
/// </para>
///
/// <para>Everything positional is derived from <see cref="ScreenWidth"/> and
/// <see cref="ScreenHeight"/> — the HUD strip, the play field under it, both camera axes and
/// every panel — so the same compiled cartridge lays itself out on whatever screen size the
/// console reports (Р6) without an edit. The literals 8, 16 and 24 that do appear are tile
/// size, sprites per sheet row and the width of this tower; none of them is a screen
/// dimension.</para>
/// </summary>
public sealed class TowerClimb : Cartridge
{
    // --- geometry that belongs to the console, not to the screen ---
    private const int Tile = 8;                 // SPEC-8 §3: sprites and map cells are 8x8
    private const int SheetColumns = 16;        // the 128x128 sheet is a 16x16 grid of sprites
    private const int GlyphW = 4;               // system font advance, 4x6 (API-8 §3)

    // --- the player's collision box; the sprite is 8 wide and hangs 1 px past it each side ---
    private const int BoxW = 6;
    private const int BoxH = 8;

    // --- HUD and play field -----------------------------------------------------------------
    // One tile tall: a 5 px line of text at y=1 and a divider on the last row. The world is
    // drawn under it, clipped, so nothing ever paints over the readouts.
    private const int HudH = 8;

    // --- tiles (sprite numbers; tile 0 is "empty" and Map never draws it, MAP-FORMAT §2) ---
    private const int SprWall = 1;
    private const int SprPlatform = 2;
    private const int SprSpike = 3;
    private const int SprGem = 4;
    private const int SprBanner = 5;
    private const int SprStone = 6;
    private const int SprIdle = 16;
    private const int SprRunA = 17;
    private const int SprRunB = 18;
    private const int SprAir = 19;

    // --- sprite flags: the tile's behaviour, kept off the tile number so two different bricks
    // can both be solid (MAP-FORMAT §10). Set in Init, so no flags.bin is needed. ---
    private const int FlagSolid = 0;
    private const int FlagPlatform = 1;         // one-way: lands you only when falling onto it
    private const int FlagDeadly = 2;
    private const int FlagGem = 3;
    private const int FlagGoal = 4;

    // --- the tower, mirroring tools/tower.py; see tools/tower.txt for the ASCII of it ---
    private const int TowerCols = 24;
    private const int PitRow = 70;              // spikes across the base
    private const int StartRow = 68;            // the stone ledge the run starts on
    private const int StartCol = 2;
    private const int MaxGems = 32;

    // --- physics, in pixels and ticks -------------------------------------------------------
    // Chosen the way a platformer is normally tuned and then measured, not the other way round
    // (Р19). Gravity 1/8 px/tick^2 with a 2.5 px/tick launch gives a 26.25 px jump that peaks in
    // 20 ticks (0.33 s) — a hair over three tiles, which is the classic run-and-jump arc. The
    // platforms sit two tiles (16 px) apart, so the arc clears them with 10 px to spare and the
    // skill is horizontal, not vertical.
    private static readonly Fix Gravity = Fix.Ratio(1, 8);
    private static readonly Fix JumpSpeed = Fix.Ratio(-5, 2);

    // Terminal fall. Half a tile per tick is the readability ceiling for an 8 px grid, and it is
    // 1.2x the speed you come down from your own full jump, so the arc is never clipped by its
    // own cap. It is also, deliberately, not a number picked to pass Р7 — it fails Р7 with a
    // centering camera, and saying so is the point of this cartridge.
    private static readonly Fix MaxFall = 3;

    private static readonly Fix RunSpeed = Fix.Ratio(5, 4);         // 1.25 px/tick = 9.4 tiles/s
    private static readonly Fix GroundAccel = Fix.Ratio(1, 4);      // full speed in 5 ticks
    private static readonly Fix AirAccel = Fix.Ratio(3, 16);
    private static readonly Fix GroundFriction = Fix.Ratio(1, 2);
    private static readonly Fix AirFriction = Fix.Ratio(1, 16);

    /// <summary>
    /// Releasing the jump button caps the remaining rise here: a tap climbs about 9 px (just
    /// over a tile), a held button the full 26. Variable jump height is what makes the two-tile
    /// spacing playable without feeling floaty.
    /// </summary>
    private static readonly Fix JumpCutSpeed = Fix.Ratio(-3, 2);

    private const int CoyoteTicks = 6;          // you may still jump 6 ticks after walking off
    private const int BufferTicks = 6;          // a jump pressed 6 ticks early still fires

    /// <summary>
    /// How far ahead of a fall the camera looks, in ticks of the current fall speed. This is the
    /// compromise the screen height forced, and it is stated as a time on purpose: at terminal
    /// speed it buys exactly <see cref="CamLeadTicks"/> extra ticks of warning, whatever the
    /// resolution and whatever the fall speed, because lead pixels and fall pixels are the same
    /// pixels. It only ever looks *down* — climbing, the next platform is 16 px up and always on
    /// screen; falling, everything you need is below you.
    /// </summary>
    private const int CamLeadTicks = 8;

    /// <summary>How fast the lead eases in: one eighth of the remaining distance per tick.</summary>
    private static readonly Fix CamLeadEase = Fix.Ratio(1, 8);

    // --- colors (SPEC-8 §2 palette) ---
    private const byte ColVoid = 0;
    private const byte ColBackdrop = 4;         // the tower's inside wall
    private const byte ColMortar = 0;           // courses of stone behind everything, every 16 px
    private const byte ColHudBg = 0;
    private const byte ColHudLine = 1;
    private const byte ColHudText = 3;
    private const byte ColHudDim = 1;
    private const byte ColGem = 6;
    private const byte ColPanel = 3;
    private const byte ColWin = 8;
    private const byte ColLose = 10;

    private const int MortarSpacing = 16;

    private const int SfxJump = 0;
    private const int SfxLand = 1;
    private const int SfxGem = 2;
    private const int SfxDeath = 3;
    private const int SfxWin = 4;
    private const int MusicTheme = 0;

    private const int BestTimeSlot = 0;

    private enum RunState
    {
        Climbing,
        Fallen,
        Cleared,
    }

    // The sprite sheet, painted in Init. Eight rows of eight characters per sprite: '.' leaves
    // the pixel at color 0 (transparent to Spr and Map), any hex digit is a palette slot.
    private static readonly int[] ArtSprites =
    {
        SprWall, SprPlatform, SprSpike, SprGem, SprBanner, SprStone,
        SprIdle, SprRunA, SprRunB, SprAir,
    };

    // One string[] per sprite in ArtSprites, same index — passed straight to Std.PaintPattern
    // (M4 Р28), which is this exact loop-and-Sset shape lifted out to Quarp.Api (this file was
    // its canonical source, per Std.PaintPattern's doc comment).
    private static readonly string[][] Art =
    {
        // SprWall — offset brick courses; the mortar is color 0, so the backdrop shows through it
        new[]
        {
            "ddd0dddd",
            "ddd0dddd",
            "ddd0dddd",
            "00000000",
            "ddddddd0",
            "ddddddd0",
            "ddddddd0",
            "00000000",
        },
        // SprPlatform — three pixels of lit stone and nothing under it: a one-way ledge has to
        // *look* like something you can pass through, or the rule feels like a bug
        new[]
        {
            "22222222",
            "11111111",
            "10111101",
            "........",
            "........",
            "........",
            "........",
            "........",
        },
        // SprSpike
        new[]
        {
            "........",
            ".2.2.2.2",
            ".2.2.2.2",
            "22222222",
            "22222222",
            "11111111",
            "11111111",
            "11111111",
        },
        // SprGem
        new[]
        {
            "........",
            "...66...",
            "..6556..",
            ".653556.",
            ".655556.",
            "..6556..",
            "...66...",
            "........",
        },
        // SprBanner
        new[]
        {
            "..8888..",
            ".888888.",
            ".8a88a8.",
            ".888888.",
            ".888888.",
            "..8888..",
            "...88...",
            "..9999..",
        },
        // SprStone
        new[]
        {
            "22222222",
            "11111111",
            "11011111",
            "11111111",
            "11111101",
            "11111111",
            "11011111",
            "11111111",
        },
        // SprIdle
        new[]
        {
            "..dddd..",
            "..dfff..",
            "..d4f4..",
            "...fff..",
            "..aaaa..",
            ".aaaaaa.",
            "..4..4..",
            "..4..4..",
        },
        // SprRunA
        new[]
        {
            "..dddd..",
            "..dfff..",
            "..d4f4..",
            "...fff..",
            "..aaaa..",
            ".aaaaaa.",
            ".44..4..",
            ".4...44.",
        },
        // SprRunB
        new[]
        {
            "..dddd..",
            "..dfff..",
            "..d4f4..",
            "...fff..",
            "..aaaa..",
            ".aaaaaa.",
            "..4.44..",
            ".44...4.",
        },
        // SprAir
        new[]
        {
            "..dddd..",
            "..dfff..",
            "..d4f4..",
            "...fff..",
            ".aaaaaa.",
            "..aaaa..",
            ".4....4.",
            "4......4",
        },
    };

    private readonly int[] _gemX = new int[MaxGems];
    private readonly int[] _gemY = new int[MaxGems];
    private int _gemTotal;

    private RunState _state;
    private Fix _x;
    private Fix _y;
    private Fix _vx;
    private Fix _vy;
    private Fix _camLead;
    private bool _faceLeft;
    private bool _wasAirborne;
    private int _coyote;
    private int _jumpBuffer;
    private int _animTimer;
    private int _gems;
    private int _timerTicks;
    private int _bestSeconds;

    public override void Init()
    {
        PaintSheet();
        TagTiles();
        FindGems();

        // Seconds, not ticks: a Fix holds up to 32767, and a slow climb can outlast that many
        // ticks (9 minutes) while it can never outlast that many seconds. Headless runs read 0
        // here — `quarp sim` never opens save.dat (API-8 §7) — which is exactly what makes the
        // frame hash a property of the cartridge instead of of this machine.
        _bestSeconds = (int)Dget(BestTimeSlot);
        if (_bestSeconds < 0 || _bestSeconds > 999)
        {
            _bestSeconds = 0;
        }

        ResetRun();
    }

    public override void Update()
    {
        if (_state != RunState.Climbing)
        {
            if (Btnp(Button.Start))
            {
                ResetRun();
            }
            return;
        }

        _timerTicks++;
        _animTimer++;
        StepPlayer();
        StepCameraLead();
    }

    public override void Draw()
    {
        Camera();
        Clip();
        Cls(ColVoid);

        int camX = CameraX();
        int camY = CameraY();

        // The world lives under the HUD strip and is clipped to it, so a tall sprite at the top
        // of the field cannot scribble on the readouts. Clip is in screen space and the camera
        // never moves it (API-8 §3), which is the whole reason this pair works.
        Clip(0, HudH, ScreenWidth, FieldHeight);
        Camera(camX, camY - HudH);
        DrawBackdrop(camY);
        DrawTiles(camX, camY);
        DrawPlayer();

        Camera();
        Clip();
        DrawHud();
        if (_state != RunState.Climbing)
        {
            DrawPanel();
        }
    }

    // --- simulation -------------------------------------------------------------------------

    private void ResetRun()
    {
        for (int i = 0; i < _gemTotal; i++)
        {
            Mset(_gemX[i], _gemY[i], SprGem);    // put back everything the last run picked up
        }

        _state = RunState.Climbing;
        _x = StartCol * Tile;
        _y = StartRow * Tile - BoxH;
        _vx = Fix.Zero;
        _vy = Fix.Zero;
        _camLead = Fix.Zero;
        _faceLeft = false;
        _wasAirborne = false;
        _coyote = 0;
        _jumpBuffer = 0;
        _animTimer = 0;
        _gems = 0;
        _timerTicks = 0;
        Music(MusicTheme);
    }

    private void StepPlayer()
    {
        bool grounded = Grounded();
        if (grounded)
        {
            _coyote = CoyoteTicks;
            _vy = Fix.Zero;                      // standing still means standing still: without
                                                 // this, gravity would trickle in and the ground
                                                 // check would flicker every eighth tick
            if (_wasAirborne)
            {
                Sfx(SfxLand);
            }
        }
        else if (_coyote > 0)
        {
            _coyote--;
        }
        _wasAirborne = !grounded;

        bool jumpHeld = Btn(Button.O) || Btn(Button.X);
        if (Btnp(Button.O) || Btnp(Button.X))
        {
            _jumpBuffer = BufferTicks;
        }
        else if (_jumpBuffer > 0)
        {
            _jumpBuffer--;
        }

        int dir = 0;
        if (Btn(Button.Left))
        {
            dir--;
        }
        if (Btn(Button.Right))
        {
            dir++;
        }

        Fix accel = grounded ? GroundAccel : AirAccel;
        if (dir > 0)
        {
            _vx += accel;
            if (_vx > RunSpeed)
            {
                _vx = RunSpeed;
            }
            _faceLeft = false;
        }
        else if (dir < 0)
        {
            _vx -= accel;
            if (_vx < -RunSpeed)
            {
                _vx = -RunSpeed;
            }
            _faceLeft = true;
        }
        else
        {
            Fix drag = grounded ? GroundFriction : AirFriction;
            if (_vx > drag)
            {
                _vx -= drag;
            }
            else if (_vx < -drag)
            {
                _vx += drag;
            }
            else
            {
                _vx = Fix.Zero;
            }
        }

        // Coyote time and the buffer meet here: either one alone is a fairness patch, the pair is
        // what makes a two-tile gap feel like a two-tile gap.
        bool launched = false;
        if (_jumpBuffer > 0 && _coyote > 0)
        {
            _vy = JumpSpeed;
            _jumpBuffer = 0;
            _coyote = 0;
            grounded = false;
            launched = true;
            _wasAirborne = true;
            Sfx(SfxJump);
        }

        if (!grounded && !launched)
        {
            if (_vy < JumpCutSpeed && !jumpHeld)
            {
                _vy = JumpCutSpeed;
            }
            _vy += Gravity;
            if (_vy > MaxFall)
            {
                _vy = MaxFall;
            }
        }

        MoveX();
        MoveY();
        TouchTiles();
    }

    /// <summary>
    /// Horizontal move and the wall it runs into. One resolve pass is enough because
    /// <see cref="RunSpeed"/> is far below a tile: the box can enter at most one new column per
    /// tick, so there is no column it can pass through unnoticed.
    /// </summary>
    private void MoveX()
    {
        _x += _vx;
        int px = (int)_x;
        int py = (int)_y;
        if (_vx > Fix.Zero)
        {
            int col = (px + BoxW - 1) / Tile;
            if (SolidColumn(col, py))
            {
                _x = col * Tile - BoxW;
                _vx = Fix.Zero;
            }
        }
        else if (_vx < Fix.Zero)
        {
            int col = px / Tile;
            if (SolidColumn(col, py))
            {
                _x = (col + 1) * Tile;
                _vx = Fix.Zero;
            }
        }
    }

    /// <summary>
    /// Vertical move. The bottom edge *before* the move is what decides a one-way platform: you
    /// land on it only if you were entirely above it a tick ago, which is what lets a jump pass
    /// up through the same tile it will later land on.
    /// </summary>
    private void MoveY()
    {
        Fix wasBottom = _y + BoxH;
        _y += _vy;
        int px = (int)_x;
        int py = (int)_y;
        if (_vy > Fix.Zero)
        {
            int row = (py + BoxH - 1) / Tile;
            if (Catches(row, px, wasBottom))
            {
                _y = row * Tile - BoxH;
                _vy = Fix.Zero;
            }
        }
        else if (_vy < Fix.Zero)
        {
            int row = py / Tile;
            if (SolidRow(row, px))
            {
                _y = (row + 1) * Tile;
                _vy = Fix.Zero;
            }
        }
    }

    /// <summary>
    /// Spikes, gems and the banner, all read off the map under the box. The pit test at the end
    /// is a net, not the rule: the spikes kill first, and this only catches a fall that somehow
    /// left the tower.
    /// </summary>
    private void TouchTiles()
    {
        int px = (int)_x;
        int py = (int)_y;
        int col0 = px / Tile;
        int col1 = (px + BoxW - 1) / Tile;
        int row0 = py / Tile;
        int row1 = (py + BoxH - 1) / Tile;
        for (int row = row0; row <= row1; row++)
        {
            for (int col = col0; col <= col1; col++)
            {
                byte tile = Mget(col, row);
                if (Fget(tile, FlagDeadly))
                {
                    EndRun(RunState.Fallen);
                    return;
                }
                if (Fget(tile, FlagGoal))
                {
                    EndRun(RunState.Cleared);
                    return;
                }
                if (Fget(tile, FlagGem))
                {
                    Mset(col, row, 0);
                    _gems++;
                    Sfx(SfxGem);
                }
            }
        }

        if (py > (PitRow + 1) * Tile)
        {
            EndRun(RunState.Fallen);
        }
    }

    private void EndRun(RunState state)
    {
        _state = state;
        _vx = Fix.Zero;
        _vy = Fix.Zero;
        // The song stops first so the last sound of a run lands in silence instead of on top of
        // a bass note — the same order carts/snake uses.
        Music();
        if (state == RunState.Cleared)
        {
            Sfx(SfxWin);
            int seconds = _timerTicks / 60;
            if (_bestSeconds == 0 || seconds < _bestSeconds)
            {
                _bestSeconds = seconds;
                Dset(BestTimeSlot, _bestSeconds);
            }
        }
        else
        {
            Sfx(SfxDeath);
        }
    }

    /// <summary>
    /// Standing on something. Only true when the feet are flush with a tile boundary, which is
    /// the only state a resolved landing can leave them in — and it means walking off a ledge
    /// turns this false on the very tick the last pixel of support goes away.
    /// </summary>
    private bool Grounded()
    {
        int py = (int)_y;
        if ((py + BoxH) % Tile != 0)
        {
            return false;
        }
        int row = (py + BoxH) / Tile;
        int px = (int)_x;
        for (int col = px / Tile; col <= (px + BoxW - 1) / Tile; col++)
        {
            byte tile = Mget(col, row);
            if (Fget(tile, FlagSolid) || Fget(tile, FlagPlatform))
            {
                return true;
            }
        }
        return false;
    }

    private bool SolidColumn(int col, int py)
    {
        for (int row = py / Tile; row <= (py + BoxH - 1) / Tile; row++)
        {
            if (Fget(Mget(col, row), FlagSolid))
            {
                return true;
            }
        }
        return false;
    }

    private bool SolidRow(int row, int px)
    {
        for (int col = px / Tile; col <= (px + BoxW - 1) / Tile; col++)
        {
            if (Fget(Mget(col, row), FlagSolid))
            {
                return true;
            }
        }
        return false;
    }

    private bool Catches(int row, int px, Fix wasBottom)
    {
        Fix top = row * Tile;
        for (int col = px / Tile; col <= (px + BoxW - 1) / Tile; col++)
        {
            byte tile = Mget(col, row);
            if (Fget(tile, FlagSolid))
            {
                return true;
            }
            if (Fget(tile, FlagPlatform) && wasBottom <= top)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Eases the camera lead toward "where the fall will be in <see cref="CamLeadTicks"/> ticks".
    /// Easing rather than snapping because a landing takes the fall speed to zero in one tick,
    /// and a camera that answered that instantly would jump 24 px on every touchdown. One eighth
    /// per tick settles in about the same twenty ticks a fall needs to reach terminal speed, so
    /// the extra runway arrives exactly when it starts to matter.
    /// </summary>
    private void StepCameraLead()
    {
        Fix target = _vy > Fix.Zero ? _vy * CamLeadTicks : Fix.Zero;
        _camLead += (target - _camLead) * CamLeadEase;
    }

    // --- layout, all of it derived from the console's screen size ----------------------------

    private int FieldHeight => ScreenHeight - HudH;

    private int TowerPixelWidth => TowerCols * Tile;

    private int TowerPixelHeight => (PitRow + 2) * Tile;

    private int CameraX()
    {
        int target = (int)_x + BoxW / 2 - ScreenWidth / 2;
        int limit = TowerPixelWidth - ScreenWidth;
        return limit <= 0 ? 0 : Std.Clamp(target, 0, limit);
    }

    private int CameraY()
    {
        int field = FieldHeight;
        int target = (int)_y + BoxH / 2 - field / 2 + (int)_camLead;
        int limit = TowerPixelHeight - field;
        return limit <= 0 ? 0 : Std.Clamp(target, 0, limit);
    }

    // --- drawing ------------------------------------------------------------------------------

    private void DrawBackdrop(int camY)
    {
        RectFill(0, camY, TowerPixelWidth, FieldHeight, ColBackdrop);
        // Courses of stone every two tiles. They are the only thing on screen that says how fast
        // you are falling when nothing else is nearby, which in a chimney is most of the time.
        for (int y = camY - (camY % MortarSpacing); y < camY + FieldHeight; y += MortarSpacing)
        {
            Line(0, y, TowerPixelWidth - 1, y, ColMortar);
        }
    }

    private void DrawTiles(int camX, int camY)
    {
        // Only the visible window, plus one cell of margin on each side for the partial tiles at
        // the edges. Drawing the whole 256x72 map would work and cost nothing visible, but the
        // window is what keeps the cost proportional to the screen rather than to the map.
        int col = camX / Tile;
        int row = camY / Tile;
        Map(col, row, col * Tile, row * Tile, ScreenWidth / Tile + 2, FieldHeight / Tile + 2);
    }

    private void DrawPlayer()
    {
        int sprite;
        if (_state == RunState.Fallen || !Grounded())
        {
            sprite = SprAir;
        }
        else if (_vx != Fix.Zero)
        {
            sprite = (_animTimer / 6) % 2 == 0 ? SprRunA : SprRunB;
        }
        else
        {
            sprite = SprIdle;
        }

        Spr(sprite, (int)_x - 1, (int)_y, 1, 1, _faceLeft);
    }

    private void DrawHud()
    {
        RectFill(0, 0, ScreenWidth, HudH - 1, ColHudBg);
        Line(0, HudH - 1, ScreenWidth - 1, HudH - 1, ColHudLine);

        int x = Print("GEM ", 1, 1, ColGem);
        x = Q.PrintInt(_gems, x, 1, ColHudText);
        x = Print("/", x, 1, ColHudDim);
        Q.PrintInt(_gemTotal, x, 1, ColHudDim);

        // Height climbed, centered: 4 glyphs of "H nn", so the box is 4 * GlyphW wide.
        // Std.IntWidth (M4 Р28) already returns pixels, unlike this file's old digit-count
        // IntWidth — see the arithmetic note on Std.IntWidth's doc comment.
        int climbed = ClimbedTiles();
        int center = (ScreenWidth - 2 * GlyphW - Std.IntWidth(climbed)) / 2;
        center = Print("H ", center, 1, ColHudDim);
        Q.PrintInt(climbed, center, 1, ColHudText);

        DrawClock(ScreenWidth - 1 - ClockWidth(_timerTicks), 1);
    }

    private int ClimbedTiles()
    {
        int climbed = (StartRow * Tile - BoxH - (int)_y) / Tile;
        return climbed < 0 ? 0 : climbed;
    }

    /// <summary>
    /// A speed-runner's clock, seconds and hundredths. It is also the reason a headless run can
    /// be read: while the climb is live this text changes on every single tick (a tick is 1.67
    /// hundredths, so the last digit never repeats), the clock stops dead when the run ends, and
    /// only the failure screen blinks after that. So a frame hash that stops changing means the
    /// tower was cleared, one that alternates means the climber fell, and one that keeps moving
    /// means the run is still going — see replays/README.md.
    /// </summary>
    private void DrawClock(int x, int y)
    {
        int hundredths = ClockHundredths(_timerTicks);
        x = Q.PrintInt(hundredths / 100, x, y, ColHudText);
        x = Print(".", x, y, ColHudText);
        x = Q.PrintInt(hundredths / 10 % 10, x, y, ColHudText);
        Q.PrintInt(hundredths % 10, x, y, ColHudText);
    }

    /// <summary>
    /// Hundredths of a second in N ticks — 100 per 60 — capped at the widest clock the HUD has
    /// room for. The single owner of the conversion: the width of the text and the text itself
    /// both read it, and two copies of "how long is this run" is exactly the kind of duplicate
    /// that ends up disagreeing by one digit.
    /// </summary>
    private static int ClockHundredths(int ticks)
    {
        int hundredths = ticks * 5 / 3;
        return hundredths > 99999 ? 99999 : hundredths;
    }

    private static int ClockWidth(int ticks) =>
        Std.IntWidth(ClockHundredths(ticks) / 100) + 3 * GlyphW;

    private void DrawPanel()
    {
        bool won = _state == RunState.Cleared;
        int panelW = 25 * GlyphW;
        int panelH = 34;
        int panelX = (ScreenWidth - panelW) / 2;
        int panelY = HudH + (FieldHeight - panelH) / 2;

        RectFill(panelX, panelY, panelW, panelH, ColVoid);
        Rect(panelX, panelY, panelW, panelH, ColPanel);

        string title = won ? "TOWER CLEARED" : "THE TOWER WINS";
        Print(title, panelX + (panelW - title.Length * GlyphW) / 2, panelY + 4, won ? ColWin : ColLose);

        int x = Print("GEMS ", panelX + 5, panelY + 13, ColGem);
        x = Q.PrintInt(_gems, x, panelY + 13, ColHudText);
        x = Print("/", x, panelY + 13, ColHudDim);
        Q.PrintInt(_gemTotal, x, panelY + 13, ColHudDim);

        x = Print("TIME ", panelX + 5, panelY + 20, ColHudDim);
        DrawClock(x, panelY + 20);

        // The cleared screen holds absolutely still: it is a result card, meant to be read and
        // photographed, and standing still is also what makes it legible to a headless run. The
        // failure screen blinks, because a screen that wants something from you should say so.
        if (won || Ticks % 40 < 28)
        {
            const string prompt = "PRESS START";
            Print(prompt, panelX + (panelW - prompt.Length * GlyphW) / 2, panelY + 27, ColPanel);
        }
    }

    // --- one-time setup -----------------------------------------------------------------------

    /// <summary>
    /// Paints the sheet from <see cref="Art"/>. Generative graphics rather than a gfx.png: it
    /// diffs, it needs no image editor, and Init is part of the simulation so the sheet is the
    /// same on every machine (M4 Р16).
    /// </summary>
    private void PaintSheet()
    {
        for (int i = 0; i < ArtSprites.Length; i++)
        {
            int sprite = ArtSprites[i];
            int originX = (sprite % SheetColumns) * Tile;
            int originY = (sprite / SheetColumns) * Tile;
            Q.PaintPattern(originX, originY, Art[i]);
        }
    }

    private void TagTiles()
    {
        Fset(SprWall, FlagSolid, true);
        Fset(SprStone, FlagSolid, true);
        Fset(SprPlatform, FlagPlatform, true);
        Fset(SprSpike, FlagDeadly, true);
        Fset(SprGem, FlagGem, true);
        Fset(SprBanner, FlagGoal, true);
    }

    /// <summary>
    /// Remembers where the gems started so a retry can put them back. The map is the level's
    /// only description, so collecting one is <c>Mset(col, row, 0)</c> and a restart is the
    /// reverse — no second copy of the level anywhere.
    /// </summary>
    private void FindGems()
    {
        _gemTotal = 0;
        for (int row = 0; row <= PitRow + 1; row++)
        {
            for (int col = 0; col < TowerCols; col++)
            {
                if (Fget(Mget(col, row), FlagGem) && _gemTotal < MaxGems)
                {
                    _gemX[_gemTotal] = col;
                    _gemY[_gemTotal] = row;
                    _gemTotal++;
                }
            }
        }
    }
}
