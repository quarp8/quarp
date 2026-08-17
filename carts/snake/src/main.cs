using Quarp.Api;

namespace Snake;

/// <summary>
/// Classic snake — the first QUARP-8 demo cartridge (milestone M1).
/// Play field is 16x8 cells of 8px (screen rows 1-8); row 0 is the score HUD.
/// The snake speeds up with every apple; the best score persists in Dget/Dset slot 0.
/// Drawn with primitives only — no gfx.png.
/// </summary>
public sealed class SnakeGame : Cartridge
{
    // --- geometry (SPEC-8 §1: 128x72, 16x9 tiles of 8px) ---
    private const int GridW = 16;
    private const int GridH = 8;                // rows 1-8 of the screen; row 0 is the HUD
    private const int CellCount = GridW * GridH;
    private const int CellPx = 8;
    private const int FieldTopPx = 8;
    private const int GlyphW = 4;               // 4x6 system font: 4px advance per glyph

    // --- tuning ---
    private const int StartHeadX = 8;
    private const int StartHeadY = 4;
    private const int StartLength = 3;
    private const int StartStepInterval = 8;    // ticks per snake step at game start
    private const int MinStepInterval = 3;      // fastest speed, reached one apple at a time
    private const int ScoreFlashTicks = 12;
    private const int BestScoreSlot = 0;

    // --- colors (work order: bg 0, snake 7/23, apple 10, HUD 3) ---
    private const byte ColBg = 0;
    private const byte ColHud = 3;
    private const byte ColHudFlash = 8;         // score flashes yellow for a moment after eating
    private const byte ColDivider = 1;
    private const byte ColBody = 7;
    private const byte ColHeadSlot = 6;         // remapped to master 23 every frame; nothing else draws with slot 6
    private const byte MasterForest = 23;       // secret dark twin of green 7 (SPEC-8 §2)
    private const byte ColEyes = 3;
    private const byte ColDeadHead = 10;
    private const byte ColApple = 10;
    private const byte ColAppleStem = 7;
    private const byte ColAppleShine = 3;
    private const byte ColPanel = 3;
    private const byte ColGameOverText = 10;
    private const byte ColWinText = 8;

    // --- directions; the opposite of d is d ^ 1 ---
    private const int DirNone = -1;
    private const int DirLeft = 0;
    private const int DirRight = 1;
    private const int DirUp = 2;
    private const int DirDown = 3;
    private static readonly int[] DirDx = { -1, 1, 0, 0 };
    private static readonly int[] DirDy = { 0, 0, -1, 1 };

    // Cached digit strings so the score can be printed with zero allocations in the tick path.
    private static readonly string[] Digits = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

    private enum GameState
    {
        Playing,
        GameOver,
        Win,
    }

    // The body is a ring buffer of cells; _cellTaken mirrors it for O(1) collision checks
    // and free-cell counting. Allocated once — reset only rewrites contents.
    private readonly int[] _bodyX = new int[CellCount];
    private readonly int[] _bodyY = new int[CellCount];
    private readonly bool[] _cellTaken = new bool[CellCount];

    private GameState _state;
    private int _headSlot;
    private int _length;
    private int _dir;
    private int _pendingDir;                    // one buffered turn (queue depth 1), DirNone when empty
    private int _stepInterval;
    private int _stepTimer;
    private int _appleX;
    private int _appleY;
    private int _score;
    private int _best;
    private int _scoreFlash;

    public override void Init()
    {
        _best = (int)Dget(BestScoreSlot);
        if (_best < 0)
        {
            _best = 0;
        }

        if (_best > 999)
        {
            _best = 999;                        // a legit score never exceeds 125; treat odd saves as garbage
        }

        ResetGame();
    }

    public override void Update()
    {
        if (_state == GameState.Playing)
        {
            UpdatePlaying();
        }
        else if (Btnp(Button.Start))
        {
            ResetGame();                        // own state reset — not a console restart
        }
    }

    public override void Draw()
    {
        // The head draws with a dedicated slot pointed at the secret forest green.
        // Re-applied every frame; safe to leave active since only the head uses slot 6.
        Pal(ColHeadSlot, MasterForest);
        Cls(ColBg);
        DrawHud();
        DrawField();
        if (_state != GameState.Playing)
        {
            DrawEndPanel();
        }
    }

    // --- simulation ---

    private void ResetGame()
    {
        for (int i = 0; i < CellCount; i++)
        {
            _cellTaken[i] = false;
        }

        _state = GameState.Playing;
        _headSlot = -1;
        _length = 0;
        for (int i = StartLength - 1; i >= 0; i--)
        {
            PushHead(StartHeadX - i, StartHeadY);
        }

        _dir = DirRight;
        _pendingDir = DirNone;
        _stepInterval = StartStepInterval;
        _stepTimer = StartStepInterval;
        _score = 0;
        _scoreFlash = 0;
        SpawnApple();
    }

    private void UpdatePlaying()
    {
        BufferTurn();

        if (_scoreFlash > 0)
        {
            _scoreFlash--;
        }

        _stepTimer--;
        if (_stepTimer <= 0)
        {
            Step();
            _stepTimer = _stepInterval;         // set after Step so a fresh speed-up applies immediately
        }
    }

    private void BufferTurn()
    {
        if (_pendingDir != DirNone)
        {
            return;                             // queue depth is 1: keep the first buffered turn
        }

        // Validity is part of the chain so an invalid press (same/opposite direction)
        // does not swallow a valid one made on the same tick.
        if (Btnp(Button.Left) && CanTurn(DirLeft))
        {
            _pendingDir = DirLeft;
        }
        else if (Btnp(Button.Right) && CanTurn(DirRight))
        {
            _pendingDir = DirRight;
        }
        else if (Btnp(Button.Up) && CanTurn(DirUp))
        {
            _pendingDir = DirUp;
        }
        else if (Btnp(Button.Down) && CanTurn(DirDown))
        {
            _pendingDir = DirDown;
        }
    }

    // Same direction is a no-op; reversing into yourself would be instant death.
    private bool CanTurn(int dir) => dir != _dir && dir != (_dir ^ 1);

    private void Step()
    {
        if (_pendingDir != DirNone)
        {
            _dir = _pendingDir;
            _pendingDir = DirNone;
        }

        int nx = _bodyX[_headSlot] + DirDx[_dir];
        int ny = _bodyY[_headSlot] + DirDy[_dir];
        if (nx < 0 || nx >= GridW || ny < 0 || ny >= GridH)
        {
            EndRun(GameState.GameOver);
            return;
        }

        bool eating = nx == _appleX && ny == _appleY;

        // Moving into the tail tip is legal when the tail vacates that cell this very step.
        int tailSlot = (_headSlot - _length + 1) & (CellCount - 1);
        bool tailVacates = !eating && nx == _bodyX[tailSlot] && ny == _bodyY[tailSlot];
        if (_cellTaken[ny * GridW + nx] && !tailVacates)
        {
            EndRun(GameState.GameOver);
            return;
        }

        if (!eating)
        {
            RemoveTail();
        }

        PushHead(nx, ny);

        if (eating)
        {
            _score++;
            _scoreFlash = ScoreFlashTicks;
            if (_stepInterval > MinStepInterval)
            {
                _stepInterval--;
            }

            if (_length == CellCount)
            {
                EndRun(GameState.Win);          // the snake fills the field — nowhere left for an apple
                return;
            }

            SpawnApple();
        }
    }

    private void PushHead(int x, int y)
    {
        _headSlot = (_headSlot + 1) & (CellCount - 1);
        _bodyX[_headSlot] = x;
        _bodyY[_headSlot] = y;
        _cellTaken[y * GridW + x] = true;
        _length++;
    }

    private void RemoveTail()
    {
        int tailSlot = (_headSlot - _length + 1) & (CellCount - 1);
        _cellTaken[_bodyY[tailSlot] * GridW + _bodyX[tailSlot]] = false;
        _length--;
    }

    private void SpawnApple()
    {
        // One RndInt over the number of free cells, then walk the grid to the k-th free cell.
        int k = RndInt(CellCount - _length);
        for (int i = 0; i < CellCount; i++)
        {
            if (_cellTaken[i])
            {
                continue;
            }

            if (k == 0)
            {
                _appleX = i % GridW;
                _appleY = i / GridW;
                return;
            }

            k--;
        }
    }

    private void EndRun(GameState state)
    {
        _state = state;
        _scoreFlash = 0;
        if (_score > _best)
        {
            _best = _score;
            Dset(BestScoreSlot, _best);
        }
    }

    // --- drawing ---

    private void DrawHud()
    {
        Line(0, FieldTopPx - 1, 127, FieldTopPx - 1, ColDivider);

        byte scoreColor = _scoreFlash > 0 ? ColHudFlash : ColHud;
        int x = Print("SCORE ", 1, 1, scoreColor);
        PrintInt(_score, x, 1, scoreColor);

        int bestX = 127 - GlyphW * 5 - IntWidth(_best);     // right-aligned "BEST n"
        bestX = Print("BEST ", bestX, 1, ColHud);
        PrintInt(_best, bestX, 1, ColHud);
    }

    private void DrawField()
    {
        for (int i = 1; i < _length; i++)       // i = 0 is the head, drawn separately
        {
            int slot = (_headSlot - i) & (CellCount - 1);
            RectFill(_bodyX[slot] * CellPx, FieldTopPx + _bodyY[slot] * CellPx, CellPx, CellPx, ColBody);
        }

        if (_state != GameState.Win)            // on a win there is no free cell for an apple
        {
            DrawApple();
        }

        DrawHead();
    }

    private void DrawHead()
    {
        int px = _bodyX[_headSlot] * CellPx;
        int py = FieldTopPx + _bodyY[_headSlot] * CellPx;
        byte color = _state == GameState.GameOver ? ColDeadHead : ColHeadSlot;
        RectFill(px, py, CellPx, CellPx, color);

        // Two eye pixels on the leading edge show where the snake is headed.
        switch (_dir)
        {
            case DirLeft:
                Pset(px + 2, py + 2, ColEyes);
                Pset(px + 2, py + 5, ColEyes);
                break;
            case DirRight:
                Pset(px + 5, py + 2, ColEyes);
                Pset(px + 5, py + 5, ColEyes);
                break;
            case DirUp:
                Pset(px + 2, py + 2, ColEyes);
                Pset(px + 5, py + 2, ColEyes);
                break;
            default:
                Pset(px + 2, py + 5, ColEyes);
                Pset(px + 5, py + 5, ColEyes);
                break;
        }
    }

    private void DrawApple()
    {
        int px = _appleX * CellPx;
        int py = FieldTopPx + _appleY * CellPx;
        CircFill(px + 3, py + 4, 3, ColApple);
        Pset(px + 4, py, ColAppleStem);
        Pset(px + 2, py + 3, ColAppleShine);
    }

    private void DrawEndPanel()
    {
        const int panelW = 72;
        const int panelH = 36;
        const int panelX = (128 - panelW) / 2;
        const int panelY = FieldTopPx + (72 - FieldTopPx - panelH) / 2;

        RectFill(panelX, panelY, panelW, panelH, ColBg);
        Rect(panelX, panelY, panelW, panelH, ColPanel);

        if (_state == GameState.Win)
        {
            Print("YOU WIN!", 64 - GlyphW * 8 / 2, panelY + 5, ColWinText);
        }
        else
        {
            Print("GAME OVER", 64 - GlyphW * 9 / 2, panelY + 5, ColGameOverText);
        }

        int scoreW = GlyphW * 6 + IntWidth(_score);
        int x = Print("SCORE ", (128 - scoreW) / 2, panelY + 14, ColHud);
        PrintInt(_score, x, panelY + 14, ColHud);

        if (Ticks % 40 < 28)                    // blink
        {
            Print("PRESS START", 64 - GlyphW * 11 / 2, panelY + 25, ColHud);
        }
    }

    /// <summary>Prints 0-999 without allocating; returns the x after the last digit.</summary>
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
}
