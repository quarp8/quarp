# carts/platformer — reading a headless run

Two input tracks live here, and neither is a golden: M4 stage 3 pins no demo hashes until the
resolution verdict is in (Р20). They are proofs of a different kind — that the game can be
finished, and that the way we tell it was finished is not self-fulfilling.

| File | What it does | Written by |
|---|---|---|
| `walkthrough.input` | clears the tower: 30 platforms, the summit and the banner | `tools/plan-walkthrough.py` |
| `fall.input` | walks off the start ledge into the spikes | by hand, one entry |

## The two commands

```
quarp replay record carts/platformer -o walkthrough.qrpr --ticks 1274 \
    --input-file carts/platformer/replays/walkthrough.input --every 1

quarp sim carts/platformer --ticks 1274
```

The recording must run to the end without an exception, and its final framebuffer hash must
differ from the idle run of the same length — otherwise the script is not playing the game.
The `.qrpr` the recorder writes is deliberately **not** committed: it is the one generated file
in this cartridge that nothing would check (`quarp build` checks `map.bin`, `sfx.bin` and
`music.bin` against their sources), so it would be the one free to rot.

## How you can tell a headless run apart from a still picture

A frame hash is sixteen hex digits; it cannot be looked at. But this cartridge has exactly three
drawing behaviours, and they are distinguishable from a column of hashes alone:

| The run is | What the frames do | What `--every 1` shows |
|---|---|---|
| still climbing | the clock in the HUD advances every tick (a tick is 1.67 hundredths, so the last digit never repeats) | every hash different |
| cleared | the clock stops and the result card holds perfectly still | one hash, repeated to the end |
| fallen | the clock stops and `PRESS START` blinks on a 40-tick cycle | two hashes alternating, in runs of 28 and 12 |

This is why the failure screen blinks and the cleared screen does not. The design reason came
first — a result card is meant to be read, a failure screen is asking you for something — but
the measurement reason is the one that makes it load-bearing here, and pretending otherwise
would be dishonest.

**Measured, 2026-08-18, re-verified on 160×90 the same day** (`quarp replay record ...
--every 1`, 1274 ticks; the 30-platform tower and the tick-1184 finish are unchanged from the
128×72 measurement below them — `ScreenWidth`/`ScreenHeight` only ever reach the camera and the
HUD, never the physics, so the frame that first touches the banner does not move when the screen
does):

- ticks 1..1183: 1183 distinct frame hashes, no repeat anywhere;
- ticks 1184..1274: `4c59928f9725624b`, ninety-one times — the banner was touched on tick 1184,
  exactly as on 128×72 (where the same tick carried a different hash, `a8ccfa944d798dbb`, before
  ADR-021 — different pixels, same tick, which is the point);
- idle run of the same length: `5ffc7631272b19db` frame / `6b34f2e0f67fe441` audio — a different
  picture and a different sound, as they must be (the walkthrough's own audio hash for the same
  1274 ticks is `980ef0c50aa1cee7`, unchanged from 128×72 — audio never reads `ScreenWidth`
  either, so this hash is one of the few numbers on this page ADR-021 did not touch).

**The negative control**, and the reason the freeze above means anything
(`--input-file fall.input --ticks 300`): the death panel is already on screen at checkpoint 50
of an `--every 1` run (the track's tick scale sits one below the cartridge's — an `N:` entry is
seen by `Update` on cartridge tick N+1), and from then on the
hashes alternate `a833e175603e2186` / `c12e217722a14731` in runs of 28 and 12 for the rest of
the recording. A constant and a square wave are not the same signal, so "the tower was cleared"
and "the climber died early and sat on a screen" cannot be confused. If the walkthrough ever
starts producing the square wave, it stopped clearing the tower.

Before ADR-021 this same negative control alternated `1eae30067314d734` / `d25e79917caf4d93` in
the same runs of 28 and 12 — the run length and the run *shape* are the load-bearing part of
this proof, not the hash values, and both are exactly reproduced at 160×90.

## Regenerating `walkthrough.input`

```
python carts/platformer/tools/plan-walkthrough.py
```

The planner mirrors the cartridge's physics in exact Q16.16 integer arithmetic and searches for
the earliest take-off tick that lands each jump; it prints the tick the banner is reached and
the `--ticks` the recorder needs. Editing any constant in `src/main.cs` invalidates the track,
and the mirror in `tools/plan-walkthrough.py` has to move with it — the copy is the honest cost
of planning a run from outside the console, and the check on it is exactly the freeze tick
above: the planner and the console agree on 1184 or the mirror is wrong.
