#!/usr/bin/env python3
"""Writes carts/platformer/map.csv — the tower this cartridge is played in.

Why a generator and not a hand-typed file. `map.csv` is a fixed 72 x 256 grid
(docs/MAP-FORMAT.md §4): every row carries 256 values whether or not the world is
256 tiles wide, and this tower is 24. Hand-typing 18 432 cells of which 16 704 are
the same zero is not authoring, it is transcription — and a miscount is exactly the
error the format refuses to pad over. Tiled would emit the same file from a drawing;
this script emits it from the design constants below, so the constants stay the one
place the level is described. It also writes tower.txt, the same tower as ASCII, which
is the artifact a human actually reads in a diff.

Run from the repository root:   python carts/platformer/tools/tower.py

Nothing here is part of the cartridge: the loader globs src/**/*.cs plus the named
assets, so tools/ is invisible to it (the same reason carts/snake/replays/plan-golden.cs
can sit next to a cartridge without being one).
"""

import os

# --- tile indices; these are sprite numbers, and sprite 0 is "empty" (MAP-FORMAT §2) ---
EMPTY = 0
WALL = 1        # brick, solid          (flag 0)
LEDGE = 2       # one-way platform      (flag 1)
SPIKE = 3       # pit floor, deadly     (flag 2)
GEM = 4         # collectible           (flag 3)
GOAL = 5        # tower banner          (flag 4)
FLOOR = 6       # cut stone, solid      (flag 0)

MAP_W, MAP_H = 256, 72          # SPEC-8 §3: the map is always this size
TOWER_W = 24                    # columns 0..23; 0 and 23 are the walls

ROOF_ROW = 0
BASE_ROW = MAP_H - 1            # 71 — solid stone under the spikes
PIT_ROW = MAP_H - 2             # 70 — the spikes you die on
START_ROW = MAP_H - 4           # 68 — the ledge the climb starts from
BOTTOM_LEDGE_ROW = MAP_H - 6    # 66 — first one-way platform
# 32 platforms at the original uniform spacing put the top two (rows 4 and 2) directly under
# the roof: a full held jump rises 26.25 px = 3.28 tiles (apex on tick 20, src/main.cs), and
# from row 4 or row 2 that arc runs out of tower before it runs out of rise -- the climber's
# head is stopped by the roof (row 0) mid-arc, measured clearance 0 px on both (bug-platformer-
# ceiling.md). BOTTOM_LEDGE_ROW and LEDGE_STEP are the lower tower's business and stay fixed
# (Р19 non-goal), so the only lever that buys headroom without moving a single lower platform
# is how many of them there are: two fewer (32 -> 30) leaves the new top platform (formerly
# index 29) at row 8, already the safe one this cap was measured against.
LEDGE_COUNT = 30
LEDGE_STEP = 2                  # rows between platforms: 2 tiles = 16 px of climb

# The summit sits one row above the new top platform (row 8) rather than the old two-row step:
# row 7 gives 13 px of clearance to the roof for a full held jump, comfortably clear of the
# 26.25 px arc (bug-platformer-ceiling.md re-measured this after the LEDGE_COUNT cut above).
GOAL_LEDGE_ROW = 7
GOAL_ROW = 6
# The summit sits over the LEFT landing zone, and that is a fix rather than a taste. A jump
# lifts the climber's head four tiles (26 px of rise plus the 8 px box), so a banner directly
# above the last platform was also within reach of the one two rows below it -- the top of the
# tower could be skipped in a single leap. Moving the summit to the far side of a chasm puts it
# 100 px away horizontally from anything but the last platform, and an arc buys only ~50.
GOAL_COLS = (3, 4)
GOAL_LEDGE = (1, 6)

# The three landing zones and, between them, the two chimneys (columns 7-8 and 15-16)
# that no platform ever covers. Missing a jump means falling down one of them, and a
# chimney runs the whole height of the tower straight onto the spikes: that is where the
# game's only real threat lives, and it is why "how far below yourself can you see"
# decides whether the game is fair (M4 Р7).
LEFT = (1, 6)
MID = (9, 14)
RIGHT = (17, 22)
CYCLE = [LEFT, MID, RIGHT, MID]     # L -> M -> R -> M -> L ... a pendulum, one chasm per jump

# Gems sit at the middle of a platform, one row above it, so a climber crossing from the
# landing end to the take-off end walks through them without a detour.
GEM_LEDGES = [2, 5, 8, 11, 14, 17, 20, 23, 26, 29]


def span_of(index):
    return CYCLE[index % len(CYCLE)]


def ledge_row(index):
    return BOTTOM_LEDGE_ROW - index * LEDGE_STEP


def build():
    grid = [[EMPTY] * MAP_W for _ in range(MAP_H)]
    notes = [""] * MAP_H

    for col in range(TOWER_W):
        grid[ROOF_ROW][col] = WALL
    notes[ROOF_ROW] = "roof"

    for row in range(ROOF_ROW + 1, MAP_H):
        grid[row][0] = WALL
        grid[row][TOWER_W - 1] = WALL

    for col in range(1, TOWER_W - 1):
        grid[BASE_ROW][col] = FLOOR
        grid[PIT_ROW][col] = SPIKE
    notes[PIT_ROW] = "the pit: falling here is the loss condition"

    for col in range(LEFT[0], LEFT[1] + 1):
        grid[START_ROW][col] = FLOOR
    notes[START_ROW] = "start ledge (player spawns here)"

    for i in range(LEDGE_COUNT):
        row = ledge_row(i)
        lo, hi = span_of(i)
        for col in range(lo, hi + 1):
            grid[row][col] = LEDGE
        notes[row] = f"platform {i:02d}, columns {lo}-{hi}"

    for i in GEM_LEDGES:
        lo, hi = span_of(i)
        grid[ledge_row(i) - 1][(lo + hi) // 2] = GEM

    for col in range(GOAL_LEDGE[0], GOAL_LEDGE[1] + 1):
        grid[GOAL_LEDGE_ROW][col] = LEDGE
    notes[GOAL_LEDGE_ROW] = "summit platform"
    for col in GOAL_COLS:
        grid[GOAL_ROW][col] = GOAL
    notes[GOAL_ROW] = "the banner -- touching it wins the run"

    return grid, notes


CSV_HEADER = """\
# carts/platformer: the tower. GENERATED by tools/tower.py; do not hand-edit.
#
#     python carts/platformer/tools/tower.py && quarp map build carts/platformer
#
# 72 rows of 256 values, top row first (docs/MAP-FORMAT.md, section 4). The tower occupies
# columns 0..23; everything to the right of it is empty and always will be, because the
# map is 256 wide by specification and this game is not. Tile 0 is the empty cell, not
# sprite 0, so the sprite numbers below start at 1:
#   1 brick wall (solid)   2 platform (one-way, jump up through it)   3 spikes (deadly)
#   4 gem (collect)        5 summit banner (win)                      6 stone (solid)
# What each tile *does* is decided by the sprite flags the cartridge sets in Init(),
# not by the number itself -- see src/main.cs.
"""


def main():
    grid, notes = build()
    here = os.path.dirname(os.path.abspath(__file__))
    cart = os.path.dirname(here)

    lines = [CSV_HEADER]
    for row in range(MAP_H):
        text = ",".join(str(v) for v in grid[row])
        if notes[row]:
            text += f"  # row {row}: {notes[row]}"
        lines.append(text)
    # Trailing newline, LF: Tiled's export ends the last row with one too, and the
    # compiler normalises CRLF anyway, so LF keeps the diff quiet on every machine.
    with open(os.path.join(cart, "map.csv"), "w", newline="\n", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")

    glyph = {EMPTY: " ", WALL: "#", LEDGE: "=", SPIKE: "^", GEM: "*", GOAL: "!", FLOOR: "_"}
    art = ["# The same tower as ASCII -- generated, for reading in a diff. See tower.py.", ""]
    for row in range(MAP_H):
        body = "".join(glyph[v] for v in grid[row][:TOWER_W])
        art.append(f"{row:2d} |{body}|" + (f"  {notes[row]}" if notes[row] else ""))
    with open(os.path.join(here, "tower.txt"), "w", newline="\n", encoding="utf-8") as f:
        f.write("\n".join(art) + "\n")

    filled = sum(1 for row in grid for v in row if v != EMPTY)
    print(f"map.csv: {MAP_H} rows x {MAP_W} values, {filled} non-empty cells")


if __name__ == "__main__":
    main()
