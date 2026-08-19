using Quarp.Api;

namespace Shmup;

/// <summary>
/// Vertical shoot-'em-up — the sixth QUARP-8 demo cartridge (milestone M4, stage 3).
/// The player's ship sits near the bottom of the field and holds off three waves of
/// stationary enemy formations that open fire once their entrance is done; survive all
/// three waves' clocks and it's a win, run out of lives and it's a loss.
///
/// <para><b>Why the formations don't march</b> (unlike a classic space-invaders sweep):
/// every position in this cartridge — enemy X/Y, bullet spawn point, the tick a shot goes
/// off — has to be predictable by hand from constants alone, because the work order asks
/// for a proven tick at which >=8 bullets and >=4 enemies are alive simultaneously, and
/// that proof is arithmetic on this file's constants, not a screenshot. A marching
/// formation would make the same claim depend on where the bounce happened to be, which
/// is exactly the kind of "worked when I watched it" fact SPEC-8 §7 doesn't allow into
/// a report. Movement stays visual (see <see cref="DrawEnemy"/>'s idle bob), never state.</para>
///
/// <para><b>Layout comes from <see cref="Cartridge.ScreenWidth"/>/<see cref="Cartridge.ScreenHeight"/></b>,
/// never from 128/72 literals (API-8 §3) — see <see cref="Init"/>. The one exception is the
/// HUD height and the enemy grid spacing, which are cartridge tuning (like snake's
/// <c>CellPx</c>), not screen geometry, and stay as plain constants for the same reason
/// snake's are.</para>
///
/// <para>Sound (AUDIO-FORMAT.md): a blip on every shot, a noise thud on every kill or hit,
/// and a two-pattern bass+lead theme that loops on channels 2-3 — see sfx.txt and
/// music.txt next to this file, built the same way carts/snake's are.</para>
/// </summary>
public sealed class ShmupGame : Cartridge
{
    // --- HUD and field geometry ---
    // HudHeight is cartridge layout (how tall the score bar is), not a screen dimension —
    // it stays a literal for the same reason snake's CellPx does; the field it leaves
    // behind is ScreenHeight - HudHeight, computed in Init, never written as "64".
    private const int HudHeight = 8;

    // --- player ship ---
    private const int PlayerSize = 8;                 // ship sprite is one 8x8 cell
    private const int PlayerSpeed = 1;                 // px/tick sideways, held movement
    private const int PlayerBottomMargin = 2;           // px of clearance below the ship
    private const int PlayerFireCooldownTicks = 10;     // 6 shots/sec at most while O is held
    private const int PlayerBulletSpeed = 2;             // px/tick, upward
    private const int PlayerBulletW = 2;
    private const int PlayerBulletH = 4;
    private const int MaxPlayerBullets = 8;
    private const int StartLives = 3;
    private const int InvulnTicks = 45;                 // 0.75s of blink immunity after a hit

    // --- enemy bullets ---
    private const int EnemyBulletSpeed = 1;              // px/tick, downward — the number the
                                                          // dodge-window figure in the report is
                                                          // built on (API-8 doesn't fix a value;
                                                          // this cartridge's own constant is it)
    private const int EnemyBulletW = 2;
    private const int EnemyBulletH = 4;
    private const int MaxEnemyBullets = 16;              // >= the 8 guaranteed by the opening
                                                          // volley (see UpdateIntermission below)
                                                          // plus headroom for late re-fires

    // --- enemy formation ---
    private const int EnemySize = 8;
    private const int EnemyColSpacing = 14;
    private const int EnemyRowSpacing = 10;
    private const int EnemyRowGap = 8;                   // px between the HUD divider and row 0
    private const int MaxEnemies = 8;                    // largest wave (4x2)
    private const int FirstShotDelayTicks = 20;          // ticks after a wave goes active before
                                                          // its first enemy fires
    private const int FireStaggerTicks = 3;              // ticks between one enemy's first shot
                                                          // and the next enemy's, spawn order
    private const int EnemyFireIntervalTicks = 90;       // baseline gap between an enemy's shots
    private const int EnemyFireJitterTicks = 30;         // + RndInt(30) so re-fires don't lock-step

    // --- waves ---
    // Three formations, widest last. Every enemy is worth its wave's point value; a wave's
    // clock (WaveDurationTicks) runs regardless of how many of its enemies are still alive —
    // "survive the wave" is a time budget, not a kill quota (tasks/open/05-shmup.md: "Победа —
    // пережить все волны"). Wave 0 (the smallest) is the one walkthrough.input fully clears,
    // because a 4-enemy single row is the easiest one to prove by hand (see replays/README.md).
    private static readonly int[] WaveCols = { 4, 3, 4 };
    private static readonly int[] WaveRows = { 1, 2, 2 };
    private static readonly int[] WavePoints = { 10, 15, 20 };
    private const int WaveCount = 3;
    private const int IntermissionTicks = 90;             // "WAVE n" banner, enemies visible,
                                                          // already shootable, not yet shooting
    private const int WaveDurationTicks = 200;            // active combat window per wave

    private const int BestScoreSlot = 0;

    // --- sprites (built with Sset in Init — M4 work order Р16, "assets from code") ---
    private const int SprPlayer = 1;
    private const int SprEnemyA = 2;                     // row 0 of every formation
    private const int SprEnemyB = 3;                     // row 1 (only 2-row waves use it)

    // --- sound (sfx.txt / music.txt in this folder; rebuild with `quarp audio build`) ---
    private const int SfxShoot = 0;
    private const int SfxBoom = 1;
    private const int MusicTheme = 0;

    // --- colors (palette slots 0-15, SPEC-8 §2) ---
    private const byte ColBg = 0;
    private const byte ColHud = 7;
    private const byte ColHudFlash = 10;
    private const byte ColDivider = 1;
    private const byte ColPlayerBullet = 10;
    private const byte ColEnemyBullet = 14;
    private const byte ColPanel = 6;
    private const byte ColWinText = 11;
    private const byte ColLoseText = 8;

    private const int GlyphW = 4;                        // system font advance (API-8 §3 Print)

    private static readonly string[] Digits =
        { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

    // Row-major 8x8 pixel patterns for Std.PaintPattern (M4 Р28: sprites are code, not a PNG
    // this cartridge doesn't own). '.' skips the pixel (the sheet's default 0, transparent under
    // the default Palt); every other character is a hex palette slot written straight into the
    // rows below, one fixed digit per sprite (Std.PaintPattern's canonical dialect is a color
    // per pixel; this cartridge only ever needs one color per sprite): 'c' (12) is the ship,
    // '8' and '9' (the same numbers) are the two enemy rows — reformatted from the original
    // '#'-per-pixel dialect, same shapes, same colors, same Sset sequence. No ColShip/ColEnemyA/
    // ColEnemyB constant backs these digits any more (adversary review, M4 stage 4.1 fix wave,
    // card В1): once BuildSprites moved to Std.PaintPattern and stopped taking a color argument,
    // nothing else in the file read them, and the only place left to spell the color is the hex
    // digit next to the pixel it colors.
    private static readonly string[] PlayerPattern =
    {
        "...cc...",
        "...cc...",
        "..cccc..",
        "..cccc..",
        ".cccccc.",
        "cccccccc",
        "c.cccc.c",
        "c......c",
    };

    private static readonly string[] EnemyAPattern =
    {
        "..8888..",
        ".888888.",
        "88.88.88",
        "88888888",
        ".888888.",
        "..8..8..",
        ".8....8.",
        "8......8",
    };

    private static readonly string[] EnemyBPattern =
    {
        "99999999",
        "9.9999.9",
        "99999999",
        "..9999..",
        ".999999.",
        "99....99",
        ".9.99.9.",
        "..9..9..",
    };

    private enum RunState { Playing, Win, GameOver }
    private enum WavePhase { Intermission, Active }

    // --- runtime geometry, resolved from ScreenWidth/ScreenHeight in Init (API-8 §3) ---
    private int _fieldTop;
    private int _playerY;
    private int _playerMinX;
    private int _playerMaxX;

    // --- player state ---
    private int _playerX;
    private int _lives;
    private int _score;
    private int _best;
    private int _fireCooldown;
    private int _invuln;

    // --- player bullet pool (parallel arrays — no List/Dictionary in the tick path) ---
    private readonly bool[] _pbAlive = new bool[MaxPlayerBullets];
    private readonly int[] _pbX = new int[MaxPlayerBullets];
    private readonly int[] _pbY = new int[MaxPlayerBullets];

    // --- enemy bullet pool ---
    private readonly bool[] _ebAlive = new bool[MaxEnemyBullets];
    private readonly int[] _ebX = new int[MaxEnemyBullets];
    private readonly int[] _ebY = new int[MaxEnemyBullets];

    // --- enemies (reused every wave; only the first _enemyCount slots are meaningful) ---
    private readonly bool[] _enAlive = new bool[MaxEnemies];
    private readonly int[] _enX = new int[MaxEnemies];
    private readonly int[] _enY = new int[MaxEnemies];
    private readonly int[] _enSprite = new int[MaxEnemies];
    private readonly int[] _enFireAt = new int[MaxEnemies];   // absolute Ticks of next shot
    private int _enemyCount;
    private int _waveStartTick;      // Ticks at the moment this wave's phase became Active

    private RunState _state;
    private WavePhase _phase;
    private int _wave;
    private int _waveCountdown;      // ticks left in the current phase (intermission or active)

    public override void Init()
    {
        // Read the console's actual size once; everything below is derived, never literal
        // (API-8 §3), so the cartridge lays out correctly on whatever screen size QUARP-8
        // reports — not just the one it happened to be built against.
        _fieldTop = HudHeight;
        _playerY = ScreenHeight - PlayerSize - PlayerBottomMargin;
        _playerMinX = 0;
        _playerMaxX = ScreenWidth - PlayerSize;

        BuildSprites();

        // Srand is called explicitly (API-8 §6) even though the default seed is already 0 and
        // already deterministic — the point is to name the seed as this cartridge's input
        // rather than lean on an implicit default an unrelated change could alter. Only used
        // for enemy re-fire jitter (see UpdateEnemyFiring); the tick proven in the report
        // (the opening volley) never touches Rnd/RndInt, on purpose — see UpdateIntermission
        // below.
        Srand(20260818);

        _best = (int)Dget(BestScoreSlot);
        if (_best < 0 || _best > 9999)
        {
            _best = 0;                  // an implausible save is treated as garbage, not trusted
        }

        ResetGame();
    }

    public override void Update()
    {
        if (_state == RunState.Playing)
        {
            UpdatePlayerControls();
            UpdatePlayerBullets();
            UpdateEnemyBullets();

            if (_phase == WavePhase.Intermission)
            {
                UpdateIntermission();
            }
            else
            {
                UpdateEnemyFiring();
                UpdateWaveClock();
            }
        }
        else if (Btnp(Button.Start))
        {
            ResetGame();
        }
    }

    public override void Draw()
    {
        Cls(ColBg);
        DrawHud();
        DrawField();
        if (_phase == WavePhase.Intermission && _state == RunState.Playing)
        {
            DrawWaveBanner();
        }
        if (_state != RunState.Playing)
        {
            DrawEndPanel();
        }
    }

    // ================= simulation =================

    private void ResetGame()
    {
        _lives = StartLives;
        _score = 0;
        _playerX = (_playerMinX + _playerMaxX) / 2;
        _fireCooldown = 0;
        _invuln = 0;
        _state = RunState.Playing;

        for (int i = 0; i < MaxPlayerBullets; i++)
        {
            _pbAlive[i] = false;
        }

        for (int i = 0; i < MaxEnemyBullets; i++)
        {
            _ebAlive[i] = false;
        }

        StartWave(0);
        Music(MusicTheme);
    }

    private void StartWave(int waveIndex)
    {
        _wave = waveIndex;
        _phase = WavePhase.Intermission;
        _waveCountdown = IntermissionTicks;
        SpawnWave(waveIndex);
    }

    /// <summary>
    /// Lays out one wave's formation. Row-major spawn order (row 0 left-to-right, then row 1)
    /// is load-bearing: <see cref="UpdateEnemyFiring"/> schedules enemy <c>i</c>'s first shot
    /// at <c>FirstShotDelayTicks + i * FireStaggerTicks</c> after the wave goes active, and the
    /// report's "&gt;=8 bullets, &gt;=4 enemies" tick is computed against this exact ordering —
    /// change it here and that arithmetic (replays/README.md) stops matching the code.
    /// </summary>
    private void SpawnWave(int waveIndex)
    {
        int cols = WaveCols[waveIndex];
        int rows = WaveRows[waveIndex];
        int width = (cols - 1) * EnemyColSpacing + EnemySize;
        int startX = (ScreenWidth - width) / 2;
        int topY = _fieldTop + EnemyRowGap;

        int idx = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                _enAlive[idx] = true;
                _enX[idx] = startX + c * EnemyColSpacing;
                _enY[idx] = topY + r * EnemyRowSpacing;
                _enSprite[idx] = r == 0 ? SprEnemyA : SprEnemyB;
                _enFireAt[idx] = 0;      // real schedule is set when the phase goes Active
                idx++;
            }
        }

        for (; idx < MaxEnemies; idx++)
        {
            _enAlive[idx] = false;
        }

        _enemyCount = cols * rows;
    }

    private void UpdatePlayerControls()
    {
        if (_invuln > 0)
        {
            _invuln--;
        }

        // Btn, not Btnp: holding a direction is continuous movement (API-8 §4).
        if (Btn(Button.Left))
        {
            _playerX -= PlayerSpeed;
            if (_playerX < _playerMinX)
            {
                _playerX = _playerMinX;
            }
        }

        if (Btn(Button.Right))
        {
            _playerX += PlayerSpeed;
            if (_playerX > _playerMaxX)
            {
                _playerX = _playerMaxX;
            }
        }

        if (_fireCooldown > 0)
        {
            _fireCooldown--;
        }

        // Held O autofires at the cooldown's rate rather than one shot per press: a shmup
        // where mashing beats holding would just teach the player to mash.
        if (Btn(Button.O) && _fireCooldown <= 0 && TryFirePlayerBullet())
        {
            _fireCooldown = PlayerFireCooldownTicks;
            Sfx(SfxShoot);
        }
    }

    private bool TryFirePlayerBullet()
    {
        for (int i = 0; i < MaxPlayerBullets; i++)
        {
            if (_pbAlive[i])
            {
                continue;
            }

            _pbAlive[i] = true;
            _pbX[i] = _playerX + (PlayerSize - PlayerBulletW) / 2;
            _pbY[i] = _playerY;
            return true;
        }

        return false;   // pool full — a held trigger just waits for a slot, no error (API-8 §1)
    }

    /// <summary>
    /// Moves every live player bullet and resolves it against the current wave's enemies.
    /// Runs in both wave phases: a formation is visible and shootable through its whole
    /// intermission (see the class doc and replays/README.md — this is how wave 0 gets fully
    /// cleared before it ever fires back).
    /// </summary>
    private void UpdatePlayerBullets()
    {
        for (int i = 0; i < MaxPlayerBullets; i++)
        {
            if (!_pbAlive[i])
            {
                continue;
            }

            _pbY[i] -= PlayerBulletSpeed;
            if (_pbY[i] + PlayerBulletH <= 0)
            {
                _pbAlive[i] = false;
                continue;
            }

            for (int e = 0; e < _enemyCount; e++)
            {
                if (!_enAlive[e])
                {
                    continue;
                }

                if (!Overlap(_pbX[i], _pbY[i], PlayerBulletW, PlayerBulletH,
                        _enX[e], _enY[e], EnemySize, EnemySize))
                {
                    continue;
                }

                _enAlive[e] = false;
                _pbAlive[i] = false;
                _score += WavePoints[_wave];
                Sfx(SfxBoom);
                break;
            }
        }
    }

    private void UpdateEnemyBullets()
    {
        for (int i = 0; i < MaxEnemyBullets; i++)
        {
            if (!_ebAlive[i])
            {
                continue;
            }

            _ebY[i] += EnemyBulletSpeed;
            if (_ebY[i] >= ScreenHeight)
            {
                _ebAlive[i] = false;
                continue;
            }

            if (_invuln <= 0
                && Overlap(_ebX[i], _ebY[i], EnemyBulletW, EnemyBulletH,
                    _playerX, _playerY, PlayerSize, PlayerSize))
            {
                _ebAlive[i] = false;
                _lives--;
                _invuln = InvulnTicks;
                Sfx(SfxBoom);
                if (_lives <= 0)
                {
                    EndRun(RunState.GameOver);
                }
            }
        }
    }

    private void UpdateIntermission()
    {
        _waveCountdown--;
        if (_waveCountdown > 0)
        {
            return;
        }

        _phase = WavePhase.Active;
        _waveCountdown = WaveDurationTicks;
        _waveStartTick = Ticks;

        // The opening volley's schedule: enemy i's first shot lands at _waveStartTick +
        // FirstShotDelayTicks + i * FireStaggerTicks, spawn order from SpawnWave. This one
        // line is the entire basis for the "8 bullets, 8 enemies" tick in replays/README.md —
        // no RNG involved yet, so it is exact, not observed.
        for (int i = 0; i < _enemyCount; i++)
        {
            if (_enAlive[i])
            {
                _enFireAt[i] = _waveStartTick + FirstShotDelayTicks + i * FireStaggerTicks;
            }
        }
    }

    private void UpdateEnemyFiring()
    {
        for (int i = 0; i < _enemyCount; i++)
        {
            if (!_enAlive[i] || Ticks < _enFireAt[i])
            {
                continue;
            }

            TryFireEnemyBullet(_enX[i], _enY[i]);
            // Re-fires are jittered (console RNG, API-8 §6) so a formation's later volleys
            // don't stay in lock-step forever — the opening volley above deliberately isn't.
            _enFireAt[i] = Ticks + EnemyFireIntervalTicks + RndInt(EnemyFireJitterTicks);
        }
    }

    private void TryFireEnemyBullet(int enemyX, int enemyY)
    {
        for (int i = 0; i < MaxEnemyBullets; i++)
        {
            if (_ebAlive[i])
            {
                continue;
            }

            _ebAlive[i] = true;
            _ebX[i] = enemyX + (EnemySize - EnemyBulletW) / 2;
            _ebY[i] = enemyY + EnemySize;
            return;
        }
        // Pool exhausted: the shot is skipped rather than dropping an older bullet, matching
        // the player pool's "wait, don't misbehave" rule (API-8 §1).
    }

    private void UpdateWaveClock()
    {
        _waveCountdown--;
        if (_waveCountdown > 0)
        {
            return;
        }

        if (_wave + 1 < WaveCount)
        {
            StartWave(_wave + 1);
        }
        else
        {
            EndRun(RunState.Win);
        }
    }

    private void EndRun(RunState state)
    {
        _state = state;
        Music();                 // stop the theme before anything else, same order as snake
        if (_score > _best)
        {
            _best = _score;
            Dset(BestScoreSlot, _best);
        }
    }

    private static bool Overlap(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh) =>
        ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;

    // ================= drawing =================

    private void DrawHud()
    {
        Line(0, HudHeight - 1, ScreenWidth - 1, HudHeight - 1, ColDivider);

        byte scoreColor = _invuln > 0 ? ColHudFlash : ColHud;
        int x = Print("SCORE ", 1, 1, scoreColor);
        Q.PrintInt(_score, x, 1, scoreColor);

        int livesW = GlyphW * 6 + GlyphW;                 // "LIVES " + one digit
        Print("LIVES ", ScreenWidth - livesW, 1, ColHud);
        Q.PrintInt(_lives, ScreenWidth - GlyphW, 1, ColHud);
    }

    private void DrawField()
    {
        for (int i = 0; i < _enemyCount; i++)
        {
            if (_enAlive[i])
            {
                DrawEnemy(i);
            }
        }

        for (int i = 0; i < MaxEnemyBullets; i++)
        {
            if (_ebAlive[i])
            {
                RectFill(_ebX[i], _ebY[i], EnemyBulletW, EnemyBulletH, ColEnemyBullet);
            }
        }

        for (int i = 0; i < MaxPlayerBullets; i++)
        {
            if (_pbAlive[i])
            {
                RectFill(_pbX[i], _pbY[i], PlayerBulletW, PlayerBulletH, ColPlayerBullet);
            }
        }

        DrawPlayer();
    }

    /// <summary>
    /// Draws enemy <paramref name="i"/> with a small cosmetic vertical bob. The bob is a pure
    /// function of <see cref="Cartridge.Ticks"/> read here, in <c>Draw</c> — it never touches
    /// <c>_enY</c>, so the collision math in <see cref="UpdatePlayerBullets"/> and the fire
    /// schedule in <see cref="UpdateEnemyFiring"/> stay exactly on the stationary grid the
    /// report's arithmetic assumes (see the class doc's "why the formations don't march").
    /// </summary>
    private void DrawEnemy(int i)
    {
        int bob = (Ticks / 8 + i) % 4 < 2 ? 0 : 1;
        Spr(_enSprite[i], _enX[i], _enY[i] + bob);
    }

    private void DrawPlayer()
    {
        // Blink out every other 4-tick chunk while invulnerable, rather than draw solid — a
        // ship that is still fully opaque after being hit reads as "nothing happened".
        if (_invuln > 0 && Ticks % 8 >= 4)
        {
            return;
        }

        Spr(SprPlayer, _playerX, _playerY);
    }

    private void DrawWaveBanner()
    {
        string label = "WAVE " + Digits[_wave + 1];
        int bannerY = _fieldTop + 24;
        Q.PrintCentered(label, bannerY, ColHud);
        if (Ticks % 40 < 28)
        {
            const string ready = "GET READY";
            Q.PrintCentered(ready, bannerY + 10, ColHudFlash);
        }
    }

    private void DrawEndPanel()
    {
        int panelW = 76;
        int panelH = 32;
        int panelX = (ScreenWidth - panelW) / 2;
        int panelY = _fieldTop + (ScreenHeight - _fieldTop - panelH) / 2;

        RectFill(panelX, panelY, panelW, panelH, ColBg);
        Rect(panelX, panelY, panelW, panelH, ColPanel);

        if (_state == RunState.Win)
        {
            const string label = "ALL WAVES CLEAR";
            Q.PrintCentered(label, panelY + 5, ColWinText);
        }
        else
        {
            const string label = "SHIP LOST";
            Q.PrintCentered(label, panelY + 5, ColLoseText);
        }

        int scoreW = GlyphW * 6 + Std.IntWidth(_score);
        int sx = Print("SCORE ", (ScreenWidth - scoreW) / 2, panelY + 14, ColHud);
        Q.PrintInt(_score, sx, panelY + 14, ColHud);

        if (Ticks % 40 < 28)
        {
            const string restart = "PRESS START";
            Q.PrintCentered(restart, panelY + 23, ColHud);
        }
    }

    // ================= sprites =================

    private void BuildSprites()
    {
        Q.PaintPattern(SprPlayer % 16 * 8, SprPlayer / 16 * 8, PlayerPattern);
        Q.PaintPattern(SprEnemyA % 16 * 8, SprEnemyA / 16 * 8, EnemyAPattern);
        Q.PaintPattern(SprEnemyB % 16 * 8, SprEnemyB / 16 * 8, EnemyBPattern);
    }
}
