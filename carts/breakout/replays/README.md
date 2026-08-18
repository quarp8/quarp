# carts/breakout — walkthrough replay

| File | What it is |
|---|---|
| `walkthrough.input` | hand-written scripted input track, 600 ticks |

The recorded `.qrpr` is deliberately not committed (work order R20); it regenerates
deterministically with the command at the bottom.

## What the track proves, and how it was proven

| Run | frame | audio |
|---|---|---|
| walkthrough, 600 ticks | `cd38f90b198669ca` | `1ea33a4f3efc684d` |
| idle, 600 ticks | `3d5c4e3449547f41` | `e9a8b28bf47401ba` |

Frame differs from idle — the input is not a no-op (an empty track reproduces the idle
hash exactly; shown red during stage-3 acceptance). Audio differs from idle — the sfx
path fires during play (idle plays music only). Idle audio differs from the 600-tick
silence digest `54738d7161a01b25` — the cart is not mute.

**Known weakness of this cart's idle run, named on purpose:** the `Serve` state draws a
byte-identical frame every tick (no clock, no blink), so `quarp sim carts/breakout` prints
the same frame hash at any tick count — the exact signature the CI golden guards against
in `carts/snake`. That makes "walkthrough differs from idle" a weak proof *for this cart*:
any paddle movement satisfies it. The stronger claims below therefore came from temporary
instrumentation (a `throw` at the asserted tick, then reverted — byte counts and hashes
returned to the values above), during the stage-3 build brigade's run:

- first life lost on tick **109** (`paddleX=32, ballX=14, livesBefore=5`);
- the bottom brick row (4 of 4) fully cleared by tick **313** (`lives=2, bricksLeft=12`);
- with the miss branch disabled, the ball left the screen on tick 116 — the branch that
  keeps the "ball never leaves the screen" invariant is live, and its removal is visible.

## Regenerate

```
dotnet <path>/quarp.dll replay record carts/breakout -o walkthrough.qrpr --ticks 600 \
  --input-file carts/breakout/replays/walkthrough.input
```
