using Quarp.Api;

namespace Breakout;

/// <summary>
/// Breakout-like — one of the six M4 stage-3 demos (work order Р8/K). A paddle deflects a
/// <see cref="Fix"/>-space ball into a grid of bricks; the bounce angle off the paddle depends
/// on where the ball lands on it, computed through <see cref="SMath"/> exactly as API-8 §10
/// prescribes ("no floats anywhere, angles are turns"). Win is an empty field, loss is running
/// out of lives — both are terminal <see cref="GameState"/> values, same shape as
/// <c>carts/snake</c>'s <c>GameState.Win</c>/<c>GameOver</c>.
///
/// <para><b>Every geometric constant below is either a fixed pixel design choice (HUD height,
/// brick thickness, ball size — numbers that do not scale with the screen) or derived from
/// <see cref="ScreenWidth"/>/<see cref="ScreenHeight"/> at <see cref="Init"/> time. There is no
/// 128, 72, 160 or 90 anywhere in this file</b> — the work order's non-goal (M4 stage 3, Р19)
/// and the whole reason the resolution spike (<c>--profile 8w</c>) is worth running at all: a
/// cartridge that baked in one screen size would look identical on both and prove nothing.</para>
///
/// <para>Sprites: none — every shape is a primitive (<see cref="RectFill"/>/<see cref="CircFill"/>),
/// same choice <c>carts/snake</c> made, for the same reason (Р16: "Sset в Init() или примитивы";
/// there is nothing here that benefits from a sprite sheet over rectangles and a circle).</para>
/// </summary>
public sealed class BreakoutGame : Cartridge
{
    // --- HUD (a fixed pixel band, not tied to screen size — same convention as carts/snake) ---
    private const int HudHeight = 8;                  // row 0: score left, lives right
    private const byte ColBg = 0;
    private const byte ColDivider = 1;
    private const byte ColHud = 3;
    private const byte ColHudLow = 8;                 // lives readout flashes this color at 1 life
    private const int GlyphW = 4;                     // system font advance (API-8 §3), not a screen literal

    // --- bricks: a fixed-size grid whose pixel geometry is derived from ScreenWidth/Height ---
    private const int BrickCols = 4;
    private const int BrickRows = 4;
    private const int BrickTopMargin = 4;              // gap between the HUD divider and the first row
    private const int BrickH = 4;
    private const int BrickRowGap = 1;
    private const int BrickRowPitch = BrickH + BrickRowGap;
    // Row colors cycle through four palette slots so the grid reads as stacked bands, the same
    // "meaning through color" trick the HUD flash and the snake's forest-green head use.
    private static readonly byte[] BrickRowColor = { 8, 9, 10, 11 };

    // --- paddle ---
    private const int PaddleWidthDiv = 4;              // paddle width = ScreenWidth / 4
    private const int PaddleHeight = 3;
    private const int PaddleBottomMargin = 4;
    private const int PaddleSpeed = 2;                 // px/tick while held (int; Fix conversion is implicit)
    private const byte ColPaddle = 7;

    // --- ball ---
    private const int BallSize = 3;
    private const byte ColBall = 10;
    // Speed is a design constant, not derived from the screen: a faster or slower ball changes
    // difficulty, not layout, so it stays a plain Fix regardless of profile. One pixel per tick
    // (60 px/s) keeps the worst-case reaction window (Р7: gap-to-paddle in ticks) a clean
    // whole number and the trajectory hand-computable for the scripted walkthrough.
    private static readonly Fix BallSpeed = Fix.Ratio(1, 1);
    // The serve always launches straight up (SMath.Sin(3/4 turn) = -1, SMath.Cos = 0) — a fixed
    // convention independent of paddle position, same as most Breakout clones' "always serves
    // up" rule. Every *subsequent* paddle bounce uses the offset-dependent formula in
    // BouncePaddleAngle, which is where "угол отскока зависит от точки удара по ракетке"
    // (work order K) actually lives.
    private static readonly Fix ServeAngle = Fix.Ratio(3, 4);
    // How far the bounce angle can swing away from straight-up per unit of paddle-relative hit
    // offset (offset in [-1, 1]). Kept well inside (0, 1/2) turn of straight-up so the reflected
    // angle can never reach 1/2 or 1.0 turns exactly — those would be a ball moving perfectly
    // sideways, using SMath.Sin(a) = 0 for its vertical speed, which would leave the paddle and
    // never come back down (SPEC-8 §7 forbids nothing here directly, but an eternally sideways
    // ball would never let the round end, which is its own kind of broken).
    private static readonly Fix MaxBounceDeviation = Fix.Ratio(1, 5);

    // --- sound (sfx.txt / music.txt in this folder; rebuild with `quarp audio build`) ---
    private const int SfxBounce = 0;                   // wall or paddle
    private const int SfxBrick = 1;                    // brick destroyed
    private const int SfxMiss = 2;                     // life lost
    private const int MusicTheme = 0;
    private const int StartingLives = 5;

    private enum GameState
    {
        Serve,      // ball rides the paddle, waiting for O
        Playing,
        GameOver,
        Win,
    }

    // --- cached screen geometry (API-8 §3: reading once and caching is legal, values are
    // constant for the whole run — the framebuffer is allocated once at console creation) ---
    private int _screenW;
    private int _screenH;
    private int _fieldTop;         // = HudHeight, named for readability at call sites
    private int _brickW;
    private int _brickTop;
    private int _paddleW;
    private int _paddleY;          // top edge of the paddle — Р7 "gap" measurement point
    private int _paddleMinX;
    private int _paddleMaxX;

    private readonly bool[] _brickAlive = new bool[BrickCols * BrickRows];
    private int _bricksLeft;

    private GameState _state;
    private Fix _paddleX;
    private Fix _ballX;
    private Fix _ballY;
    private Fix _ballVX;
    private Fix _ballVY;
    private int _lives;
    private int _score;

    // Cached digit strings so the HUD prints without allocating in the tick path (same trick
    // carts/snake uses for its score/best readout).
    private static readonly string[] Digits = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

    public override void Init()
    {
        _screenW = ScreenWidth;
        _screenH = ScreenHeight;
        _fieldTop = HudHeight;

        // BrickCols is chosen (4) specifically because it divides both 128 and 160 evenly —
        // ScreenWidth is always a multiple of it on both profiles this cartridge is asked to
        // run on, so _brickW carries no truncation remainder on either screen.
        _brickW = _screenW / BrickCols;
        _brickTop = _fieldTop + BrickTopMargin;

        _paddleW = _screenW / PaddleWidthDiv;
        _paddleY = _screenH - PaddleBottomMargin - PaddleHeight;
        _paddleMinX = 0;
        _paddleMaxX = _screenW - _paddleW;

        ResetGame();
    }

    /// <summary>Either terminal state — the run is over, win or lose alike.</summary>
    private bool Ended => _state == GameState.GameOver || _state == GameState.Win;

    public override void Update()
    {
        if (Ended)
        {
            if (Btnp(Button.Start))
            {
                ResetGame();                    // own state reset — not a console restart
            }
            return;
        }

        MovePaddle();

        if (_state == GameState.Serve)
        {
            // The ball rides the paddle until launch, so moving the paddle before pressing O
            // aims the very first shot — this is how the walkthrough steers which brick column
            // gets hit without needing any paddle-bounce geometry at all.
            TrackBallOnPaddle();
            if (Btnp(Button.O))
            {
                Launch();
            }
        }
        else
        {
            StepBall();
        }
    }

    public override void Draw()
    {
        Cls(ColBg);
        DrawHud();
        DrawBricks();
        RectFill((int)_paddleX, _paddleY, _paddleW, PaddleHeight, ColPaddle);
        if (_state == GameState.Serve || _state == GameState.Playing)
        {
            DrawBall();
        }
        if (Ended)
        {
            DrawEndPanel();
        }
    }

    // --- simulation ---

    private void ResetGame()
    {
        for (int i = 0; i < _brickAlive.Length; i++)
        {
            _brickAlive[i] = true;
        }
        _bricksLeft = _brickAlive.Length;

        _lives = StartingLives;
        _score = 0;
        _paddleX = Fix.Ratio(_paddleMinX + _paddleMaxX, 2);   // start centered
        _state = GameState.Serve;
        TrackBallOnPaddle();
        Music(MusicTheme);
    }

    private void MovePaddle()
    {
        if (Btn(Button.Left))
        {
            _paddleX -= PaddleSpeed;
        }
        if (Btn(Button.Right))
        {
            _paddleX += PaddleSpeed;
        }
        if (_paddleX < _paddleMinX)
        {
            _paddleX = _paddleMinX;
        }
        else if (_paddleX > _paddleMaxX)
        {
            _paddleX = _paddleMaxX;
        }
    }

    /// <summary>Keeps the ball centered on the paddle while it waits to be launched.</summary>
    private void TrackBallOnPaddle()
    {
        _ballX = _paddleX + Fix.Ratio(_paddleW - BallSize, 2);
        _ballY = _paddleY - BallSize;
    }

    private void Launch()
    {
        _state = GameState.Playing;
        _ballVX = SMath.Cos(ServeAngle) * BallSpeed;
        _ballVY = SMath.Sin(ServeAngle) * BallSpeed;
        Sfx(SfxBounce);
    }

    /// <summary>
    /// One tick of ball physics. The ball's stored position is only ever assigned one of four
    /// values: a wall-clamped coordinate, a paddle-bounce reposition, an unmodified in-flight
    /// step, or <see cref="TrackBallOnPaddle"/>'s paddle-relative coordinate after a miss —
    /// every one of which is provably inside [0, ScreenWidth) x [FieldTop, ScreenHeight). A
    /// tentative position that would leave the screen (the miss case) is computed and then
    /// discarded rather than committed, which is the "clamp/bounce" invariant the work order
    /// (K, criterion 3) asks this file to carry: the ball never leaves the screen because the
    /// one motion that would send it past the bottom edge is never written to _ballX/_ballY at
    /// all — the state machine falls back to <see cref="Miss"/> instead.
    /// </summary>
    private void StepBall()
    {
        Fix nx = _ballX + _ballVX;
        Fix ny = _ballY + _ballVY;

        if (nx < Fix.Zero)
        {
            nx = Fix.Zero;
            _ballVX = -_ballVX;
            Sfx(SfxBounce);
        }
        else if (nx > _screenW - BallSize)
        {
            nx = _screenW - BallSize;
            _ballVX = -_ballVX;
            Sfx(SfxBounce);
        }

        if (ny < _fieldTop)
        {
            ny = _fieldTop;
            _ballVY = -_ballVY;
            Sfx(SfxBounce);
        }

        if (HitsABrick(nx, ny))
        {
            _ballX = nx;
            _ballY = ny;
            return;
        }

        if (OverlapsPaddle(nx, ny) && _ballVY > Fix.Zero)
        {
            BouncePaddle(nx);
            return;
        }

        // Past the paddle's row without a catch: a miss. The tentative (nx, ny) is dropped —
        // the comment on this method explains why that is exactly what keeps the ball on screen.
        if (ny > _paddleY)
        {
            Miss();
            return;
        }

        _ballX = nx;
        _ballY = ny;
    }

    /// <summary>
    /// Finds the first alive brick overlapping the ball's tentative rectangle, destroys it and
    /// reflects the axis with the smaller penetration (the standard minimum-translation-vector
    /// rule): a shallow vertical overlap bounces vertically, a shallow horizontal one bounces
    /// horizontally. Returns false — and touches nothing — when no brick overlaps.
    /// </summary>
    private bool HitsABrick(Fix nx, Fix ny)
    {
        int ballLeft = (int)nx;
        int ballTop = (int)ny;
        int ballRight = ballLeft + BallSize;
        int ballBottom = ballTop + BallSize;

        for (int row = 0; row < BrickRows; row++)
        {
            int brickTop = _brickTop + row * BrickRowPitch;
            int brickBottom = brickTop + BrickH;
            if (ballBottom <= brickTop || ballTop >= brickBottom)
            {
                continue;
            }
            for (int col = 0; col < BrickCols; col++)
            {
                int index = row * BrickCols + col;
                if (!_brickAlive[index])
                {
                    continue;
                }
                int brickLeft = col * _brickW;
                int brickRight = brickLeft + _brickW;
                if (ballRight <= brickLeft || ballLeft >= brickRight)
                {
                    continue;
                }

                _brickAlive[index] = false;
                _bricksLeft--;
                _score += 10;
                Sfx(SfxBrick);

                // System.Math is banned in cartridge code (SPEC-8 §7, QRP1002), so the
                // penetration depths are min/max'd by hand rather than through it.
                int overlapX = MinInt(ballRight, brickRight) - MaxInt(ballLeft, brickLeft);
                int overlapY = MinInt(ballBottom, brickBottom) - MaxInt(ballTop, brickTop);
                if (overlapX < overlapY)
                {
                    _ballVX = -_ballVX;
                }
                else
                {
                    _ballVY = -_ballVY;
                }

                if (_bricksLeft == 0)
                {
                    Win();
                }
                return true;
            }
        }
        return false;
    }

    private bool OverlapsPaddle(Fix nx, Fix ny)
    {
        int ballLeft = (int)nx;
        int ballTop = (int)ny;
        int ballRight = ballLeft + BallSize;
        int ballBottom = ballTop + BallSize;
        int paddleLeft = (int)_paddleX;
        int paddleRight = paddleLeft + _paddleW;
        int paddleTop = _paddleY;
        int paddleBottom = _paddleY + PaddleHeight;
        return ballRight > paddleLeft && ballLeft < paddleRight
            && ballBottom > paddleTop && ballTop < paddleBottom;
    }

    /// <summary>
    /// Reflects the ball off the paddle. The bounce angle is <see cref="ServeAngle"/> (straight
    /// up) plus an offset proportional to where the ball's center landed relative to the
    /// paddle's center — a hit at the paddle's own edge swings the angle by
    /// <see cref="MaxBounceDeviation"/>, a center hit swings it by nothing. This is the one
    /// place in the file the "bounce angle depends on where it hit the paddle" requirement
    /// (work order K) actually happens; the initial serve deliberately does not use it (see
    /// <see cref="ServeAngle"/>'s remark).
    /// </summary>
    private void BouncePaddle(Fix nx)
    {
        Fix ballCenterX = nx + Fix.Ratio(BallSize, 2);
        Fix paddleCenterX = _paddleX + Fix.Ratio(_paddleW, 2);
        Fix halfPaddle = Fix.Ratio(_paddleW, 2);
        Fix offset = (ballCenterX - paddleCenterX) / halfPaddle;
        if (offset < -Fix.One)
        {
            offset = -Fix.One;
        }
        else if (offset > Fix.One)
        {
            offset = Fix.One;
        }

        Fix angle = ServeAngle + offset * MaxBounceDeviation;
        _ballVX = SMath.Cos(angle) * BallSpeed;
        _ballVY = SMath.Sin(angle) * BallSpeed;
        _ballX = nx;
        _ballY = _paddleY - BallSize;
        Sfx(SfxBounce);
    }

    private void Miss()
    {
        _lives--;
        Sfx(SfxMiss);
        if (_lives <= 0)
        {
            GameOver();
        }
        else
        {
            _state = GameState.Serve;
            TrackBallOnPaddle();
        }
    }

    private void GameOver()
    {
        _state = GameState.GameOver;
        Music();
    }

    private void Win()
    {
        _state = GameState.Win;
        Music();
    }

    // --- drawing ---

    private void DrawHud()
    {
        Line(0, _fieldTop - 1, _screenW - 1, _fieldTop - 1, ColDivider);

        int x = Print("SCORE ", 1, 1, ColHud);
        PrintInt(_score, x, 1, ColHud);

        byte livesColor = _lives <= 1 ? ColHudLow : ColHud;
        int livesW = GlyphW * 6 + IntWidth(_lives);          // "LIVES n" right-aligned
        int livesX = _screenW - 1 - livesW;
        livesX = Print("LIVES ", livesX, 1, livesColor);
        PrintInt(_lives, livesX, 1, livesColor);
    }

    private void DrawBricks()
    {
        for (int row = 0; row < BrickRows; row++)
        {
            int y = _brickTop + row * BrickRowPitch;
            byte color = BrickRowColor[row % BrickRowColor.Length];
            for (int col = 0; col < BrickCols; col++)
            {
                if (!_brickAlive[row * BrickCols + col])
                {
                    continue;
                }
                RectFill(col * _brickW, y, _brickW - 1, BrickH, color);
            }
        }
    }

    private void DrawBall()
    {
        int cx = (int)_ballX + BallSize / 2;
        int cy = (int)_ballY + BallSize / 2;
        CircFill(cx, cy, BallSize / 2, ColBall);
    }

    private void DrawEndPanel()
    {
        int panelW = _screenW - _screenW / 4;
        int panelH = _screenH / 3;
        int panelX = (_screenW - panelW) / 2;
        int panelY = _fieldTop + (_screenH - _fieldTop - panelH) / 2;

        RectFill(panelX, panelY, panelW, panelH, ColBg);
        Rect(panelX, panelY, panelW, panelH, ColHud);

        string headline = _state == GameState.Win ? "YOU WIN!" : "GAME OVER";
        Print(headline, (_screenW - headline.Length * GlyphW) / 2, panelY + 5, ColHud);

        int scoreW = GlyphW * 6 + IntWidth(_score);
        int x = Print("SCORE ", (_screenW - scoreW) / 2, panelY + 14, ColHud);
        PrintInt(_score, x, panelY + 14, ColHud);

        if (Ticks % 40 < 28)                                  // blink, same period as carts/snake
        {
            const string Prompt = "PRESS START";
            Print(Prompt, (_screenW - Prompt.Length * GlyphW) / 2, panelY + 25, ColHud);
        }
    }

    /// <summary>Prints a non-negative int without allocating; returns the x after the last digit.</summary>
    private int PrintInt(int value, int x, int y, byte color)
    {
        if (value >= 100)
        {
            x = Print(Digits[value / 100 % 10], x, y, color);
        }
        if (value >= 10)
        {
            x = Print(Digits[value / 10 % 10], x, y, color);
        }
        return Print(Digits[value % 10], x, y, color);
    }

    private static int IntWidth(int value) => value >= 100 ? GlyphW * 3 : value >= 10 ? GlyphW * 2 : GlyphW;

    private static int MinInt(int a, int b) => a < b ? a : b;

    private static int MaxInt(int a, int b) => a > b ? a : b;
}
