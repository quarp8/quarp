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

**Measured, 2026-08-18** (`quarp replay record ... --every 1`, 1274 ticks, re-measured after
`bug-platformer-ceiling.md` cut the tower to 30 platforms and moved the summit ledge — see
`tools/tower.py` and the README's Jump section):

- ticks 1..1183: 1183 distinct frame hashes, no repeat anywhere;
- ticks 1184..1274: `a8ccfa944d798dbb`, ninety-one times — the banner was touched on tick 1184;
- idle run of the same length: `8d9b7053b88d4c02` frame / `6b34f2e0f67fe441` audio — a different
  picture and a different sound, as they must be (the walkthrough's own audio hash for the same
  1274 ticks is `980ef0c50aa1cee7`).

**The negative control**, and the reason the freeze above means anything
(`--input-file fall.input --ticks 300`): the death panel is already on screen at checkpoint 50
of an `--every 1` run (the track's tick scale sits one below the cartridge's — an `N:` entry is
seen by `Update` on cartridge tick N+1), and from then on the
hashes alternate `1eae30067314d734` / `d25e79917caf4d93` in runs of 28 and 12 for the rest of
the recording. A constant and a square wave are not the same signal, so "the tower was cleared"
and "the climber died early and sat on a screen" cannot be confused. If the walkthrough ever
starts producing the square wave, it stopped clearing the tower.

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
