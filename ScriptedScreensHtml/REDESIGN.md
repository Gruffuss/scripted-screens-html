# Translate once, then send only what changed

Started 2026-09-16. The HTML mod is a renderer today: every change, however small, lays the
page out, writes the whole scene (about 10 KB) and the vector mod parses and rebuilds all of
it. A page that updates 2.4 times a second made a garbage collection every 6 s and ran at
p99 28-35 ms, against the same console written in Lua for the vector mod at a flat 72 FPS,
p99 16-17 ms. This plan makes it a translation layer, as the vector mod was designed to be used:
structure once, values as data.

## The bar

A page is done only when, beside the Lua regulator (AtmoUi/AtmoRegulator.lua) on the same
console, over 3 minutes, it matches within noise on: frame mean, p99 and worst; garbage
collections per minute; memory at start and end; bytes sent per update. The only allowed
difference is the fixed memory of the layout tree and the script engine.

## Steps

1. **Values are data slots.** After the emitter writes a scene, every changing value in it
   (position, size, text, colour, opacity, and the numbers inside expressions) is replaced by
   a named slot `$L<line>_<key>`; the values go in a data map. The slotted text is the
   *template*. Same template as last time: send only the slots whose value changed, as a
   `keep = 1` data patch through the page's own element (no re-parse, no scene restart).
   Different template: send the full data, then the template as the new structure.
   Tween expressions are written relative to the time the structure was last sent (the vector
   clock only restarts on a structure), so a live animation no longer forces a resend.
   *Measure:* bytes per update, structure sends per minute, GCs per minute.

2. **Values do not glide unless the page says so** (vector-side ask 19, `snap = 1` on a data
   payload). A browser snaps; only CSS transitions animate.

3. **innerHTML updates in place.** New markup is compared with the current tree; matching
   elements keep their VisualElement and only changed text and attributes are written. A page
   that re-renders every tick becomes a few value writes, which step 1 turns into a patch.
   *Measure:* elements created per second (target 0 on an unchanged structure).

4. **Keyframe animations are compiled once.** A running animation becomes one periodic
   expression in the scene, not a runner that writes styles at every keyframe boundary
   (12 redraws a second for one pulsing lamp today).

5. **Emit only what changed on our side too.** The emitter's per-element output is cached and
   rebuilt only for elements whose layout or record changed, so an update allocates next to
   nothing on the HTML side.

6. **The remaining leak.** Memory still climbs slowly with a rebuilding page after the
   2026-09-16 pruning; find and remove it (the counters are in the Diagnostics line).

## Status

| Step | State |
|---|---|
| 1 | done, seen in game: 4 structures and 278 patches in 3 min on the Apple page (was 4,421 structures); a structure change uses the other of two slot-name sets, so its values never land on the structure still on screen |
| 2 | done (vector 0.11.26 `snap`), all HTML payloads snap |
| 3 | done, seen in game: 818 in-place updates in 3 min, no elements created per tick |
| 4 | done for looping opacity/transform animations (CSS and script); finite ones keep the runner |
| 5 | open: the emitter still writes the whole scene text on every change (6.8 emits/s, 0.47 ms/frame on the main thread) |
| 6 | memory flat over 3 min (2,105 -> 2,011 MB); garbage collections 10 per 3 min |

Also fixed on the way: `overflow` no longer clips the element's own background, border and shadow
(the panel's shadow was tessellated and then clipped away: 8.3 ms -> 3.4 ms per rebuild); a pending
timer no longer keeps the layout panel awake (it re-rendered the page off-screen every frame);
the serializer kept dropping worker-assigned ids; internal attributes survive in-place updates;
a script setting the same animation value no longer restarts it.

Measured 2026-09-17, same restart, consoles 561 and 563 blank, 586 running each in turn:

| | Lua regulator | HTML Apple page |
|---|---|---|
| frame mean | 13.77 ms (72.6 FPS) | 13.93 ms (71.8 FPS) |
| p99 | 14.9 ms | 15.0 ms |
| vector rebuild (off-thread) | 0.85 ms | 3.26 ms |
| structures / patches in 3 min | - | 4 / 278 |

A box whose declared background turns transparent keeps its node (the structure no longer
changes with a lamp's colour); a box that shrinks to zero keeps its node too.

Diagnostics line additions: `main N ms/frame, awake N frames, sent: N structures M patches (K values), J in-place`,
a `frames over 25 ms` line, and `new structure ... first difference` lines explaining each structure send.
