#!/usr/bin/env python3
"""Writes carts/platformer/replays/walkthrough.input -- a scripted clearing of the tower.

Run from the repository root:   python carts/platformer/tools/plan-walkthrough.py

Why a planner and not a hand-written track. M4 Р20 asks every demo to carry a scripted run
from the start to the end state, and thirty-one jumps across mostly-two-tile chasms cannot be
timed by eye: a track written by guessing would be tuned by watching the game, and a brigade
that may not open a window has no way to watch. So the physics of src/main.cs is mirrored
here in exact Q16.16 integer arithmetic -- the same operations in the same order, floors and
truncations included -- and each jump is found by trying every take-off tick until one lands.
The result is not a recording; it is a proof that the plan works, and the cartridge then
either reproduces it tick for tick or the mirror is wrong, which is itself worth knowing.

Everything below that looks like a magic number is a copy of a constant in src/main.cs. That
duplication is the honest cost of a planner outside the console; it is checked the only way
it can be, by running the produced track through `quarp replay record` and reading the
checkpoint hashes (replays/README.md explains how a headless run is read).
"""

import os
import sys

# No __pycache__ next to the cartridge: importing tower.py would drop a .pyc into the cart
# folder, and a cart folder is a thing this project packs, hashes and diffs.
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tower  # noqa: E402  -- the level is described once, in tower.py

# --- Q16.16, exactly as src/Quarp.Api/Fix.cs implements it -------------------------------
ONE = 1 << 16


def fix(n, d=1):
    """Fix.Ratio: truncates toward zero, like C# long division."""
    v = (n << 16)
    q = abs(v) // abs(d)
    return q if (v < 0) == (d < 0) else -q


def mul(a, b):
    """Fix * Fix: a 64-bit product shifted down 16, arithmetic (floor) shift."""
    return (a * b) >> 16


def to_int(a):
    """(int)Fix: floor toward negative infinity, an arithmetic shift."""
    return a >> 16


def cdiv(a, b):
    """C# int division: truncates toward zero."""
    q = abs(a) // abs(b)
    return q if (a < 0) == (b < 0) else -q


# --- constants copied from src/main.cs ---------------------------------------------------
TILE, BOXW, BOXH = 8, 6, 8
GRAVITY = fix(1, 8)
JUMP_SPEED = fix(-5, 2)
MAX_FALL = fix(3)
RUN_SPEED = fix(5, 4)
GROUND_ACCEL = fix(1, 4)
AIR_ACCEL = fix(3, 16)
GROUND_FRICTION = fix(1, 2)
AIR_FRICTION = fix(1, 16)
JUMP_CUT = fix(-3, 2)
COYOTE_TICKS = 6
BUFFER_TICKS = 6
START_ROW, START_COL, PIT_ROW = 68, 2, 70

SOLID = {tower.WALL, tower.FLOOR}
PLATFORM = {tower.LEDGE}
DEADLY = {tower.SPIKE}
GEM = {tower.GEM}
GOAL = {tower.GOAL}

# Button bits, from src/Quarp.Api/Button.cs and the mask InputScript builds.
LEFT, RIGHT, O = 1 << 0, 1 << 1, 1 << 4
LETTER = {LEFT: "L", RIGHT: "R", O: "O"}


class Player:
    __slots__ = ("x", "y", "vx", "vy", "coyote", "buffer", "state", "gems")

    def __init__(self):
        self.x = START_COL * TILE << 16
        self.y = (START_ROW * TILE - BOXH) << 16
        self.vx = 0
        self.vy = 0
        self.coyote = 0
        self.buffer = 0
        self.state = "climbing"
        self.gems = 0

    def clone(self):
        p = Player.__new__(Player)
        for name in Player.__slots__:
            setattr(p, name, getattr(self, name))
        return p


class World:
    def __init__(self, grid):
        self.grid = [row[:] for row in grid]

    def clone(self):
        return World(self.grid)

    def at(self, col, row):
        if col < 0 or row < 0 or row >= tower.MAP_H or col >= tower.MAP_W:
            return 0
        return self.grid[row][col]


def cols_of(px):
    return range(cdiv(px, TILE), cdiv(px + BOXW - 1, TILE) + 1)


def grounded(p, w):
    py = to_int(p.y)
    if (py + BOXH) % TILE != 0:
        return False
    row = cdiv(py + BOXH, TILE)
    for col in cols_of(to_int(p.x)):
        t = w.at(col, row)
        if t in SOLID or t in PLATFORM:
            return True
    return False


def support_row(p):
    return cdiv(to_int(p.y) + BOXH, TILE)


def step(p, w, mask, prev):
    # Update returns early once the run is over, so the world stops dead: the climber does not
    # slide, the clock does not advance, and the drawn frame stops changing. Mirroring that here
    # is what makes the tick this planner reports the same tick the console freezes on.
    if p.state != "climbing":
        return

    on_ground = grounded(p, w)
    if on_ground:
        p.coyote = COYOTE_TICKS
        p.vy = 0
    elif p.coyote > 0:
        p.coyote -= 1

    jump_held = mask & O
    if (mask & ~prev) & O:
        p.buffer = BUFFER_TICKS
    elif p.buffer > 0:
        p.buffer -= 1

    direction = 0
    if mask & LEFT:
        direction -= 1
    if mask & RIGHT:
        direction += 1

    accel = GROUND_ACCEL if on_ground else AIR_ACCEL
    if direction > 0:
        p.vx = min(p.vx + accel, RUN_SPEED)
    elif direction < 0:
        p.vx = max(p.vx - accel, -RUN_SPEED)
    else:
        drag = GROUND_FRICTION if on_ground else AIR_FRICTION
        if p.vx > drag:
            p.vx -= drag
        elif p.vx < -drag:
            p.vx += drag
        else:
            p.vx = 0

    launched = False
    if p.buffer > 0 and p.coyote > 0:
        p.vy = JUMP_SPEED
        p.buffer = 0
        p.coyote = 0
        on_ground = False
        launched = True

    if not on_ground and not launched:
        if p.vy < JUMP_CUT and not jump_held:
            p.vy = JUMP_CUT
        p.vy = min(p.vy + GRAVITY, MAX_FALL)

    move_x(p, w)
    move_y(p, w)
    touch(p, w)


def move_x(p, w):
    p.x += p.vx
    px, py = to_int(p.x), to_int(p.y)
    if p.vx > 0:
        col = cdiv(px + BOXW - 1, TILE)
        if solid_column(w, col, py):
            p.x = (col * TILE - BOXW) << 16
            p.vx = 0
    elif p.vx < 0:
        col = cdiv(px, TILE)
        if solid_column(w, col, py):
            p.x = ((col + 1) * TILE) << 16
            p.vx = 0


def move_y(p, w):
    was_bottom = p.y + (BOXH << 16)
    p.y += p.vy
    px, py = to_int(p.x), to_int(p.y)
    if p.vy > 0:
        row = cdiv(py + BOXH - 1, TILE)
        if catches(w, row, px, was_bottom):
            p.y = (row * TILE - BOXH) << 16
            p.vy = 0
    elif p.vy < 0:
        row = cdiv(py, TILE)
        if solid_row(w, row, px):
            p.y = ((row + 1) * TILE) << 16
            p.vy = 0


def solid_column(w, col, py):
    for row in range(cdiv(py, TILE), cdiv(py + BOXH - 1, TILE) + 1):
        if w.at(col, row) in SOLID:
            return True
    return False


def solid_row(w, row, px):
    return any(w.at(col, row) in SOLID for col in cols_of(px))


def catches(w, row, px, was_bottom):
    top = (row * TILE) << 16
    for col in cols_of(px):
        t = w.at(col, row)
        if t in SOLID:
            return True
        if t in PLATFORM and was_bottom <= top:
            return True
    return False


def touch(p, w):
    px, py = to_int(p.x), to_int(p.y)
    for row in range(cdiv(py, TILE), cdiv(py + BOXH - 1, TILE) + 1):
        for col in cols_of(px):
            t = w.at(col, row)
            if t in DEADLY:
                p.state = "fallen"
                return
            if t in GOAL:
                p.state = "cleared"
                return
            if t in GEM:
                w.grid[row][col] = 0
                p.gems += 1
    if py > (PIT_ROW + 1) * TILE:
        p.state = "fallen"


# --- planning -----------------------------------------------------------------------------

JUMP_HOLD = 24          # long enough that the release never cuts the rise
SEARCH_LIMIT = 200      # ticks a single hop may take before the attempt is abandoned
SETTLE_LIMIT = 40       # ticks allowed for friction to bring the climber to a stop


def landed_on(p, w, row, span):
    if not grounded(p, w) or support_row(p) != row:
        return False
    lo, hi = span
    return any(lo <= col <= hi for col in cols_of(to_int(p.x)))


def try_hop(p0, w0, prev0, masks, done):
    """Runs a candidate button track; returns (track, arrival, player, world, prev) or None."""
    p, w, prev = p0.clone(), w0.clone(), prev0
    track = []
    for tick in range(SEARCH_LIMIT):
        mask = masks(tick)
        step(p, w, mask, prev)
        prev = mask
        track.append(mask)
        if p.state == "fallen":
            return None
        if done(p, w):
            break
    else:
        return None
    arrival = len(track)

    # Let go and wait for the climber to come to a complete stop, so the next hop is planned
    # from a state with no inherited speed in it. Every segment then begins the same way and a
    # mistake cannot compound across the climb.
    for _ in range(SETTLE_LIMIT):
        if p.state != "climbing":
            break                       # the run is over; nothing left to settle
        if p.vx == 0 and grounded(p, w):
            break
        step(p, w, 0, prev)
        prev = 0
        track.append(0)
        if p.state == "fallen":
            return None
    else:
        return None
    return track, arrival, p, w, prev


def plan_hop(p, w, prev, target_row, span, want_clear=False):
    """Finds the earliest take-off that clears the gap; returns the tick track."""
    px = to_int(p.x)
    lo, hi = span
    natural = 0
    if px + BOXW - 1 < lo * TILE:
        natural = RIGHT
    elif px > (hi + 1) * TILE - 1:
        natural = LEFT
    order = [natural] + [d for d in (RIGHT, LEFT, 0) if d != natural]

    if want_clear:
        def done(pp, ww):
            return pp.state == "cleared"
    else:
        def done(pp, ww):
            return landed_on(pp, ww, target_row, span)

    for jump_tick in range(0, 121):
        for direction in order:
            def masks(tick, d=direction, j=jump_tick):
                mask = d
                if j <= tick < j + JUMP_HOLD:
                    mask |= O
                return mask
            attempt = try_hop(p, w, prev, masks, done)
            if attempt is not None:
                return attempt
    return None


def main():
    grid, _ = tower.build()
    world = World(grid)
    player = Player()
    prev = 0
    track = []

    targets = []
    for i in range(tower.LEDGE_COUNT):
        targets.append((tower.ledge_row(i), tower.span_of(i), f"platform {i:02d}", False))
    targets.append((tower.GOAL_LEDGE_ROW, tower.GOAL_LEDGE, "the summit", False))
    targets.append((tower.GOAL_ROW, tower.GOAL_COLS, "the banner", True))

    marks = []
    for index, (row, span, label, want_clear) in enumerate(targets):
        result = plan_hop(player, world, prev, row, span, want_clear)
        if result is None:
            raise SystemExit(f"no route onto target {index} (row {row}, columns {span})")
        piece, arrival, player, world, prev = result
        marks.append((len(track), label, row))
        win_tick = len(track) + arrival
        track.extend(piece)

    # A tail of stillness after the banner. It is not padding: the cleared screen never
    # animates, so a run of identical frame hashes to the end of the recording is what says
    # the tower was cleared rather than merely survived (replays/README.md).
    track.extend([0] * 90)

    entries = []
    last = None
    for tick, mask in enumerate(track):
        if mask != last:
            entries.append((tick, mask))
            last = mask
    if entries and entries[0] == (0, 0):
        entries.pop(0)

    mark_at = {tick: (label, row) for tick, label, row in marks}
    lines = [
        "# carts/platformer -- scripted clearing of the tower.",
        "# GENERATED by tools/plan-walkthrough.py; do not hand-edit.",
        "#",
        "#     python carts/platformer/tools/plan-walkthrough.py",
        "#",
        "# Grammar (quarp replay record --input-file): tick:buttons, entries separated by",
        "# commas or newlines, '#' starts a comment. Each entry sets what player 0 holds from",
        "# that tick until the next one.",
        "#",
        f"# The banner is reached on tick {win_tick}; the run collects {player.gems} of the tower's gems. Every",
        "# hop is the same shape: hold a direction, hold the jump button across the arc, then",
        "# release everything and let friction stop the climber before the next hop is planned.",
        "# Jumps are held for 24 ticks because a release under 8 ticks would cut the rise",
        "# (JumpCutSpeed in src/main.cs) and the two-tile step would stop clearing.",
        "#",
        f"# Reproduce:  quarp replay record carts/platformer -o carts/platformer/replays/walkthrough.qrpr \\",
        f"#                 --ticks {len(track)} --input-file carts/platformer/replays/walkthrough.input",
        "#",
    ]
    row = []
    for tick, mask in entries:
        if tick in mark_at:
            if row:
                lines.append(",".join(row) + ",")
                row = []
            label, target_row = mark_at[tick]
            lines.append(f"# tick {tick}: setting off for {label} (map row {target_row})")
        letters = "".join(LETTER[bit] for bit in (LEFT, RIGHT, O) if mask & bit)
        row.append(f"{tick}:{letters}")
        if len(row) == 8:
            lines.append(",".join(row) + ",")
            row = []
    if row:
        lines.append(",".join(row))
    text = "\n".join(lines) + "\n"
    if text.rstrip().endswith(","):
        text = text.rstrip()[:-1] + "\n"

    here = os.path.dirname(os.path.abspath(__file__))
    out = os.path.join(os.path.dirname(here), "replays")
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "walkthrough.input"), "w", newline="\n", encoding="utf-8") as f:
        f.write(text)

    print(f"cleared at tick {win_tick}, gems {player.gems}/{len(tower.GEM_LEDGES)}, "
          f"{len(entries)} entries, record --ticks {len(track)}")


if __name__ == "__main__":
    main()
