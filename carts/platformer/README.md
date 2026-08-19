# Tower Climb — the vertical platformer

Climb 30 platforms to the banner at the top. Two chimneys run the whole height of the tower and
no platform ever covers them; the bottom of a chimney is a spike pit. Arrows move, `Z` or `X`
jumps (hold for a full jump, tap for a short one), `Enter` restarts a finished run.

```
quarp build carts/platformer      # compile and check, no window, no tick
quarp run   carts/platformer      # play it
quarp run   carts/platformer --profile 8w    # the same cartridge on 160x90 (M4 spike, Р6)
```

Made of: `map.csv` → `quarp map build` → `map.bin` for the tower; `Sset` in `Init` for every
sprite (no `gfx.png`); `sfx.txt` / `music.txt` → `quarp audio build` for five effects and a
four-pattern theme. Nothing here is shared with another demo — copy-paste between demos is the
point at this stage (Р18), so that the standard library of M4 stage 4 is cut from repetitions
somebody actually observed.

## Rebuilding the assets

```
python carts/platformer/tools/tower.py                  # map.csv (and tools/tower.txt to read)
python carts/platformer/tools/plan-walkthrough.py       # replays/walkthrough.input
quarp map build   carts/platformer
quarp audio build carts/platformer
```

`quarp build` fails if any of the three binaries has drifted from its source, so a forgotten
rebuild is loud rather than silent.

## The measurement this cartridge exists for (M4 Р7)

Everything below is arithmetic on constants in `src/main.cs`, not an impression. The physics was
chosen to be genre-typical and then measured; it was **not** moved to clear the threshold (Р19),
and the first number below is the one that fails.

### Terminal fall speed

`MaxFall` = **3 px/tick** = 180 px/s = 22.5 tiles/s. Half a tile per tick is the readability
ceiling on an 8 px grid, and it is 1.2x the 2.5 px/tick the climber comes down at from a full
jump, so the cap never clips the player's own arc. Free fall reaches it 24 ticks and 37.5 px
after the ground goes away (`3 ÷ 0.125`).

### Visible pixels below the player, and the window they buy

The HUD is one tile tall, so the play field is `ScreenHeight − 8`, and the camera centres the
player's 8 px box in that field:

```
feet on screen  = (py + 8) − (py + 4 − FieldHeight/2) + 8 = FieldHeight/2 + 12
visible below   = ScreenHeight − (FieldHeight/2 + 12)
```

| Screen | Field | Feet at | Visible below | Window at 3 px/tick | Р7 floor 15 ticks |
|---|---:|---:|---:|---:|---|
| 128 x 72 | 64 px | y 44 | **28 px** | **9.33 ticks — 156 ms** | fails, 62 % of it |
| 160 x 90 | 82 px | y 53 | **37 px** | **12.33 ticks — 206 ms** | fails, 82 % of it |

**Neither resolution passes with a camera that only centres the player.** That is the honest
finding, and nothing unusual in the physics produced it: the window is `visible ÷ speed`, so at
any fall speed a screen buys warning in proportion to its height, and 72 lines is a short
screen. Halving the fall speed would pass the threshold and would also make a 576 px tower take
six and a half seconds to fall down, which is a different game, not a fix.

### So yes — a camera compromise was needed, and here is exactly what it cost

The camera leads the fall: its target is where the climber will be `CamLeadTicks = 8` ticks from
now, eased in at one eighth per tick, and it only ever looks **down** (climbing, the next
platform is 16 px up and always on screen). Stated as a time on purpose — lead pixels and fall
pixels are the same pixels, so the lead buys exactly 8 ticks of warning at any resolution and
any fall speed.

Measured in exact Q16.16: the ease converges to 23.9999 px and `(int)` floors it to **23 px**,
but convergence is slower than the speed ramp — on fall tick 24, the tick terminal speed
arrives, the lead is only **17 px**; 23 px is first reached on tick 39, some 85 px into a fall.
(The first edition of this file claimed the two settle together on tick 24. They do not; the
stage-3 adversarial review caught it, and every number below is recomputed from the corrected
trace — the worst *moment* of a fall, not the flattering steady state.)

| Screen | Worst tick of the fall | Window at that tick | Steady state (long falls) | Verdict |
|---|---|---:|---:|---|
| 128 x 72 | tick 24: 28 + 17 = 45 px at 3 px/tick | **15.00 ticks — 250 ms** | 17.0 ticks | passes the "no less than 15" threshold **with zero margin** |
| 160 x 90 | tick 24: 37 + 17 = 54 px at 3 px/tick | **18.00 ticks — 300 ms** | 20.0 ticks | passes, 3 ticks to spare |

No per-room camera and no "look down" button: a tower is one room, and a button that shows you
what you are falling towards is a button the player is pressing during the two seconds they have
least attention to spare.

### What the window is actually spent on

A chimney is 2 tiles wide (16 px) and the climber is 6 px, so escaping one costs at worst
5.5 px of sideways travel — and the escape happens **in the air**, where acceleration is
`AirAccel = 3/16` px/tick², not the ground's 1/4: **8 ticks** from a standing start, not the
7 the first edition claimed. Subtract that from the worst-tick window and what is left is the
time to *decide*:

| Screen and camera | Window (worst tick) | Manoeuvre | Left to react |
|---|---:|---:|---:|
| 128 x 72, centring | 9.33 | 8 | **1.3 ticks — 22 ms** |
| 128 x 72, shipped | 15.00 | 8 | **7.0 ticks — 117 ms** |
| 160 x 90, shipped | 18.00 | 8 | 10.0 ticks — 167 ms |

Human reaction to a visual cue is around 200-250 ms. On 128x72 with a centring camera the
recovery is not hard, it is impossible; with the lead it is genuinely tight — under one
reaction-time budget; on 160x90 it is workable. The authoritative cross-checked tables for
the stage-4 verdict live in `docs/milestones/M4-MEASUREMENTS.md`, derived independently of
this file.

### Jump

| | |
|---|---|
| launch | 2.5 px/tick, gravity 0.125 px/tick² |
| rise | **26.25 px = 3.28 tiles**, apex on tick 20 (0.33 s) |
| share of the play field | **41 %** of 64 px at 128x72, **32 %** of 82 px at 160x90 |
| platform spacing | 2 tiles (16 px) — the arc clears it with 10 px to spare |

A jump eating two fifths of the visible field is the other half of the same crowding: at 128x72
the apex of an ordinary jump is most of the way to the top of the screen, so the camera is
moving during almost every jump.

The same 26.25 px arc is why the tower has 30 platforms and not 32: the owner's playtest found
the roof jammed against the top two (`bug-platformer-ceiling.md`), and a full held jump measured
0 px of clearance to the roof from both the old top platform (row 4) and the old summit ledge
(row 2) — the arc was stopped mid-rise, not clipped by a hair. `tools/tower.py` now stops the
regular staircase two platforms short (`LEDGE_COUNT = 30`, top platform at row 8, 21 px of
clearance) and moves the summit ledge to row 7, 13 px of clearance to the roof for the same full
jump. Nothing below the new top platform moved.

### Rows of play field left after the HUD

| Screen | HUD | Field | In tiles | In text lines (6 px) |
|---|---:|---:|---|---|
| 128 x 72 | 8 px | 64 px | **8 of 9 rows** | 10 of 12 |
| 160 x 90 | 8 px | 82 px | **10.25 of 11.25 rows** | 13 of 15 |

The HUD carries three readouts (gems, height climbed, clock) across `ScreenWidth`; at 128 px
they occupy 33, 16 and 20 px with 23 and 35 px of gap between them, so the strip is comfortable
rather than tight, and one tile row of the nine is the whole price.

## Proof that it can be finished

`replays/README.md` — a scripted clear, an idle run of the same length that hashes differently,
and a scripted death as the negative control that makes the first two mean something.
