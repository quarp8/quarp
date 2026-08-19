# Tower Climb — the vertical platformer

Climb 30 platforms to the banner at the top. Two chimneys run the whole height of the tower and
no platform ever covers them; the bottom of a chimney is a spike pit. Arrows move, `Z` or `X`
jumps (hold for a full jump, tap for a short one), `Enter` restarts a finished run.

```
quarp build carts/platformer      # compile and check, no window, no tick
quarp run   carts/platformer      # play it
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

After ADR-021 the console has one resolution; the 128×72 column that used to sit next to the
one below made the M4 stage-4 resolution verdict and is preserved as history in
`docs/milestones/M4-MEASUREMENTS.md`, not repeated here.

| Field | Feet at | Visible below | Window at 3 px/tick | Р7 floor 15 ticks |
|---:|---:|---:|---:|---|
| 82 px | y 53 | **37 px** | **12.33 ticks — 206 ms** | fails, 82 % of it |

**160x90 does not pass with a camera that only centres the player, either.** That is the honest
finding, and nothing unusual in the physics produced it: the window is `visible ÷ speed`, so at
any fall speed a screen buys warning in proportion to its height, and 90 lines alone is not
enough. Halving the fall speed would pass the threshold and would also make a 576 px tower take
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

| Worst tick of the fall | Window at that tick | Steady state (long falls) | Verdict |
|---|---:|---:|---|
| tick 24: 37 + 17 = 54 px at 3 px/tick | **18.00 ticks — 300 ms** | 20.0 ticks | passes, 3 ticks to spare |

No per-room camera and no "look down" button: a tower is one room, and a button that shows you
what you are falling towards is a button the player is pressing during the two seconds they have
least attention to spare.

### What the window is actually spent on

A chimney is 2 tiles wide (16 px) and the climber is 6 px, so escaping one costs at worst
5.5 px of sideways travel — and the escape happens **in the air**, where acceleration is
`AirAccel = 3/16` px/tick², not the ground's 1/4: **8 ticks** from a standing start, not the
7 the first edition claimed. Subtract that from the worst-tick window and what is left is the
time to *decide*:

| Window (worst tick) | Manoeuvre | Left to react |
|---:|---:|---:|
| 18.00 | 8 | **10.0 ticks — 167 ms** |

Human reaction to a visual cue is around 200-250 ms, so 10.0 ticks (167 ms) of recovery time
after an 8-tick manoeuvre is workable — with margin the 128x72 alternative never had. That
comparison decided ADR-021 and is preserved as history in `docs/milestones/M4-MEASUREMENTS.md`,
not repeated here.

### Jump

| | |
|---|---|
| launch | 2.5 px/tick, gravity 0.125 px/tick² |
| rise | **26.25 px = 3.28 tiles**, apex on tick 20 (0.33 s) |
| share of the play field | **32 %** of 82 px at 160x90 |
| platform spacing | 2 tiles (16 px) — the arc clears it with 10 px to spare |

A jump eating roughly a third of the visible field is the other half of the same crowding: the
apex of an ordinary jump reaches well into the upper half of the screen, so the camera is moving
during almost every jump.

The same 26.25 px arc is why the tower has 30 platforms and not 32: the owner's playtest found
the roof jammed against the top two (`bug-platformer-ceiling.md`), and a full held jump measured
0 px of clearance to the roof from both the old top platform (row 4) and the old summit ledge
(row 2) — the arc was stopped mid-rise, not clipped by a hair. `tools/tower.py` now stops the
regular staircase two platforms short (`LEDGE_COUNT = 30`, top platform at row 8, 21 px of
clearance) and moves the summit ledge to row 7, 13 px of clearance to the roof for the same full
jump. Nothing below the new top platform moved.

### Rows of play field left after the HUD

| HUD | Field | In tiles | In text lines (6 px) |
|---:|---:|---|---|
| 8 px | 82 px | **10.25 of 11.25 rows** | 13 of 15 |

The HUD carries three readouts (gems, height climbed, clock) across `ScreenWidth`; the two side
readouts are pinned to its edges and the centred one grows its margins by exactly the amount the
screen did (`(ScreenWidth - boxWidth) / 2`, `DrawHud` in `src/main.cs`), so the strip is
comfortable rather than tight at 160x90 too, and one tile row of the field is the whole price.

## Proof that it can be finished

`replays/README.md` — a scripted clear, an idle run of the same length that hashes differently,
and a scripted death as the negative control that makes the first two mean something.
