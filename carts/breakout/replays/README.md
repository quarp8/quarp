# carts/breakout — walkthrough replay

| File | What it is |
|---|---|
| `walkthrough.input` | hand-written scripted input track, 660 ticks, ends in GameOver |

The recorded `.qrpr` is deliberately not committed (work order Р20); it regenerates
deterministically with the command at the bottom.

## Hashes (160×90, ADR-021)

| Run | frame | audio |
|---|---|---|
| walkthrough, 660 ticks | `2b3a069484e1102a` | `b2e6f34db9b6024f` |
| idle, 660 ticks | `19726a4a25400139` | `909eec49d2a54e15` |
| idle, 600 ticks | `19726a4a25400139` | `e9a8b28bf47401ba` |
| silence digest, 600 ticks | — | `54738d7161a01b25` |

Frame differs from idle at the same length — the input is not a no-op. Audio differs from
idle — the sfx path fires during play (idle plays music only). Idle audio differs from the
600-tick silence digest — the cart is not mute. The idle audio at 600 ticks is the one
number the move to 160×90 did **not** change (`e9a8b28bf47401ba` before and after): with
nothing pressed the cart never leaves `Serve`, so it plays the theme and nothing else, and
the theme does not know how wide the screen is.

## What the move to 160×90 changed (Р25: the cart's code did not change, the track did)

Every number below is `main.cs`'s own arithmetic read at the new screen size — there is no
128/72/160/90 literal in the cartridge to fix.

| Quantity | Formula | 160×90 | 128×72 (history) |
|---|---|---:|---:|
| brick width | `ScreenWidth / BrickCols(4)` | 40 px | 32 px |
| paddle width | `ScreenWidth / PaddleWidthDiv(4)` | 40 px | 32 px |
| paddle top edge | `ScreenHeight − 4 − 3` | y = 83 | y = 65 |
| paddle travel | `0 .. ScreenWidth − paddleW` | 0…120 | 0…96 |
| paddle start | midpoint of the travel | x = 60 | x = 48 |
| brick rows (top edges) | `HudHeight + 4 + row·5` | 12 / 17 / 22 / 27 | same |
| ball resting on the paddle | `paddleX + (paddleW − 3)/2`, `paddleY − 3` | +18.5, y = 80 | +14.5, y = 62 |
| one round trip of the ball | up to the bottom brick row and back to the miss line | **104 ticks** | 68 ticks |

The brick grid is the only thing that got *easier*: `BrickCols = 4` divides 160 as evenly as
it divided 128, so `_brickW` still carries no truncation remainder. Everything else got
taller, and the round trip is the number that broke the old track: at `BallSpeed` = 1 px/tick
the ball now spends 50 ticks climbing (80 → 30) and 54 falling (30 → 84) instead of 32 and 36,
so the old schedule's launches landed in the middle of the previous ball's flight.

## What the track does now, and what it proves

Five rounds. Each launch goes straight up from wherever the paddle is parked (`ServeAngle`
is fixed), so the shot is aimed by moving the paddle *before* pressing O; the paddle then
steps aside while the ball is airborne, which turns every catch into a miss.

| Round | paddle at launch | ball x | launch (tick) | brick hit (tick) | miss (tick) | lives after |
|---:|---:|---:|---:|---:|---:|---:|
| 0 | 0 | 18.5 | 41 | 91 — row 3, col 0 | 145 | 4 |
| 1 | 40 | 58.5 | 151 | 201 — row 3, col 1 | 255 | 3 |
| 2 | 80 | 98.5 | 261 | 311 — row 3, col 2 | 365 | 2 |
| 3 | 120 | 138.5 | 371 | **421 — row 3, col 3: bottom row complete** | 475 | 1 |
| 4 | 60 | 78.5 | 481 | 536 — row 2, col 1 | **595 — GameOver** | 0 |

Ticks are cartridge-scale (`--every` checkpoint numbering); the entry that causes a tick N
event sits at N−1 in the file, because `Ticks` is incremented before `Update`.

Both of the work order's criteria are in that table: **a full row cleared** (row 3, all four
columns, by tick 421) and **a life lost** (five of them; the fifth ends the run). Unlike the
stage-3 track, this one reaches a terminal `GameState` — `GameOver` at tick 595 — rather than
stopping mid-rally, so "plays through to a final state" (Р20) is literally true here now.

### How the five miss ticks were observed rather than asserted

The stage-3 edition of this file had to prove its claims with temporary instrumentation (a
`throw` at the asserted tick, then reverted), because the cart cannot print its own state.
That is no longer needed and no longer allowed (Р25 forbids touching demo code), so the
proof is read off the frame hash instead, and it happens to be sharper:

**After a miss the cart returns to `Serve`, where the ball rides a motionless paddle and the
frame is byte-identical from tick to tick.** A run of equal consecutive hashes is therefore a
lost life, dated exactly. `--every 1` over the 660-tick recording gives runs of ≥3 identical
frames starting at ticks 1, 35, **145, 255, 365, 475, 595**, and then only the blink of the end
panel. The two early ones are the paddle standing still before and after the opening walk; the
other five are the five misses, each on the predicted tick, none early, none late.

The last of them is the interesting one. Rounds 0–3 take 104 ticks from launch to miss
(50 up + 54 down); round 4 takes 114 (595 − 481), because with row 3 gone its ball climbs
five pixels higher before it meets row 2 and falls five pixels further afterwards. **That
ten-tick difference is the observable proof that the bottom row was cleared** — the ball flew
through the space where four bricks used to be. Round 4's ball straddles brick columns 1 and
2 (x 78…81 against a column boundary at 80), and `HitsABrick` scans columns left to right,
so it is column 1 that loses its row-2 brick.

From tick 595 the recording shows exactly two distinct frame hashes alternating in blocks of
28 and 12 ticks: the GAME OVER panel with its "PRESS START" prompt blinking on `Ticks % 40 <
28`. That is the terminal state, dated and observed.

## Negative controls

| What was broken | What went red |
|---|---|
| empty track, same length | frame `19726a4a25400139`, audio `909eec49d2a54e15` — the idle hashes, bit for bit. "Walkthrough ≠ idle" is carried by the input, not by the recording length |
| the four mid-flight paddle steps deleted (launches kept) | every ball is caught: **no run of identical frames after tick 41 at all**, and 51 distinct frame hashes in ticks 600…660 instead of 2 — no life is ever lost, and the run never reaches a terminal state. This is the control for both criteria at once |
| *(stage 3, 128×72)* the miss branch disabled in `main.cs` | the ball left the screen on tick 116 — the invariant "the ball never leaves the screen" is live and its removal is visible |

**Known weakness of this cart's idle run, named on purpose and still true at 160×90:** the
`Serve` state draws a byte-identical frame every tick (no clock, no blink), so `quarp sim
carts/breakout` prints the same frame hash at any tick count — `19726a4a25400139` at 600 and
at 660 alike. That is the exact signature the CI golden guards against in `carts/snake`, and
it makes "walkthrough differs from idle" a weak proof *for this cart*: any paddle movement
satisfies it. The strong claims are the dated ones above.

## Regenerate

```
quarp replay record carts/breakout -o /tmp/walkthrough.qrpr --ticks 660 \
  --input-file carts/breakout/replays/walkthrough.input
```
