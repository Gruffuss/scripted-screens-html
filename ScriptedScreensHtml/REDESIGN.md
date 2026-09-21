# Translate once, then send only what changed

Started 2026-09-16. The HTML mod is a renderer today: every change, however small, lays the
page out, writes the whole scene (about 10 KB) and the vector mod parses and rebuilds all of
it. A page that updates 2.4 times a second made a garbage collection every 6 s and ran at
p99 28-35 ms, against the same console written in Lua for the vector mod at a flat 72 FPS,
p99 16-17 ms. This plan makes it a translation layer, as the vector mod was designed to be used:
structure once, values as data.

## The goal, stated properly (corrected 2026-09-21)

**Nothing of the HTML side runs after the initial translation.** Not a reduced amount — nothing.
The plan below was written as "structure once, values as patches *from the HTML mod*", and that
middle step should not exist.

The compiler turns HTML + CSS into a vector scene whose slots are named by the author's ids
(`<span id="temp">` becomes `$temp`), hands it to the vector element once, and unloads. After that
**Lua writes the values straight to that element**, exactly as the hand-written vector pages already
do. `ColdbenchConsole.lua:2841` is the entire runtime data path:

```lua
if VDATA then VDATA:set_props({ data = v }) end
```

`VectorBridge` uses that same `keep = 1` mechanism today; it simply inserts the whole HTML pipeline
in front of it and recomputes a scene first.

Things previously treated as reasons to stay resident:

| | |
|---|---|
| sending values | Lua's job, directly. The HTML mod was a detour |
| clicks | already reach Lua's `on_click` with the button id; never through the HTML mod |
| page JS | a mockup convenience — production logic is Lua, and clock-driven motion compiles to an expression |
| DOM reads (`getBoundingClientRect`, `children`…) | only page JS needs them, and page JS is not running |
| a genuine structural change (a theme press) | **recompile** — the compiler is *invoked*, not resident. `use_space()` is the same move |

So there is no per-frame HTML work of any kind, and **the engine question disappears with it**: no
JavaScript at runtime means no interpreter at runtime. Step 7 is retained below only as the measured
record of that investigation, not as work to do.

After compiling, the mod drops the DOM, the cascade, the layout tree, the emitter state and the
engine, and holds only what a recompile needs: the source and the id → slot map.

## The plan (2026-09-21) — this supersedes the steps below

**The mod is a compiler.** It runs at page load and unloads. It emits two things, and the game
already runs both:

| output | what it carries |
|---|---|
| a **vector scene** | the structure, slots named by the author's ids, and clock-pure motion as expressions |
| **Lua source** | whatever the page's script does that has memory — as `on_frame` / `tick(dt)` / `on_click` handlers that write slots |

Nothing of the HTML side is resident afterwards. **There is no JavaScript engine at runtime**, so
there is no engine question: no Jint, no V8, no ClearScript, no native dependency, no shared buffer.
Step 7 below is retained only as the measured record of that investigation.

Where each thing a page does ends up:

| the page does | compiles to | runs |
|---|---|---|
| CSS animation / transition | a vector expression | nowhere — it is geometry |
| script motion that is pure in the clock | a vector expression | nowhere |
| script with memory, per frame (a game loop) | Lua `on_frame` | the game's Lua VM, **200,000 instructions/frame** (`FrameCallbackManager.cs:77`) — ample |
| `setInterval` a few Hz | Lua `tick(dt)` | same, 2/s |
| `onclick` | Lua `on_click` | already true today |
| any DOM write | a slot write | `VDATA:set_props{ data = ... }` — `ColdbenchConsole.lua:2841` |

### Steps

**0. Fix the instruments.** Every wrong conclusion on 2026-09-21 came from a measuring tool. The
bench does not compile `HtmlSurface.cs` or `VectorBridge.cs`; its allocation window opens after
`Tweens.Diff` and `OffThread.Capture`; it labels page-thread work "main thread"; and it never calls
`tweens.Expire`, which made item 1g invisible to it. Do these before trusting another number.

**1. Name slots by author id.** `<span id="temp">` compiles to `$temp`, so Lua addresses the scene
without the mod. This is the contract between the compiled page and whoever feeds it.

**2. Make the structure independent of values.** Three bugs, all printed by the mod itself in its
`first difference` lines, and the reason a page that should send 4 structures in 3 minutes sends
1-5 a second:

| | |
|---|---|
| 2a | the `G` transform wrapper is dropped at identity — a *value* changes the *shape* of the text |
| 2b | gradient stops are template literals, not slots |
| 2c | def ids come from a page-wide counter and renumber on every re-emit (`d3b9b1d` claims to fix this and is in the tree — find why it does not hold) |

**3. Compile clock-pure motion to expressions.** Generalise what already happens for `@keyframes`
to script statements. A statement qualifies only if its right-hand side reads nothing but the time
parameter, constants and never-written outer values. Per *statement*, not per callback: a loop that
positions twenty elements from the clock and increments one score compiles the twenty and leaves
the one. **It must fail toward "keep running"** — wrongly deciding a callback is pure freezes the
console, which is the bug class fixed on 2026-09-21 (see 1h).

**4. Transpile the remainder to Lua.** The subset a console page uses is small. The mismatches are
a known table, handled once with explicit helpers rather than idiomatic output — verbose Lua nobody
reads is the right trade:

| | JS | Lua |
|---|---|---|
| truthiness | `if (0)`, `if ("")` are false | **both are true** — emit `js_truthy(x)` |
| arrays | 0-based, `.length` | 1-based, `#t` |
| modulo | `-1 % 3 === -1` | `-1 % 3 == 2` |
| concat | `+` | `..` |
| absent | `undefined` and `null` | only `nil` |
| not-equal | `!==` | `~=` |

None of these is subtle in effect: a flipped truthiness is a lamp stuck on, not a half pixel. And
the output is Lua source that can be read, which no interpreter offers.

**5. Unload after compiling.** Drop the DOM, the cascade, the layout tree, the emitter state.
Keep only what a recompile needs: the source and the id → slot map.

**6. Enumerate reachable states rather than recompiling for them.**

> Emit every state the page can reach, gate them with slots, and recompile only for states that
> cannot be enumerated.

That is exactly `JOBROWS = 2` plus `"+N more"`: a chosen maximum, everything emitted, presence
driven by opacity. Applied to themes:

| theme changes | answer |
|---|---|
| colours only | slots. A switch is a data write, no recompile |
| geometry within one design space | emit both, gate with `G o=$theme`. Hidden shapes are walked and evaluated but never tessellated |
| **the design space itself** (726 vs 806) | **recompile** — the viewbox belongs to the scene, and `use_space()` already rejects the alternative: *"scaling would put 52px tabs on 46.8 and every hairline on a half pixel"* |

So recompile means: **the source changed, or the design space changed.** Never per state change.

**Additive vector-side ask** (no behaviour change — a fully transparent group draws nothing today
either): a group-level early-out, `if (alpha <= 0.002f) return;` at `Tessellator.cs:1204`, would
make a hidden subtree genuinely free instead of merely cheap.

### The specification is already written, in Lua

`CoolingUi/ColdbenchConsole.lua` and `ManufacturingUi/ManufacturingConsole.lua` are what the
compiler should produce. Quoted in "Step 5 in full" below.

---

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
   nothing on the HTML side. **See "Step 5 in full" below** — the cache was built and the
   structure still resends, because three things make the structure depend on a *value*.

6. **The remaining leak.** Memory still climbs slowly with a rebuilding page after the
   2026-09-16 pruning; find and remove it (the counters are in the Diagnostics line).

## Status

| Step | State |
|---|---|
| 1 | done, seen in game: 4 structures and 278 patches in 3 min on the Apple page (was 4,421 structures); a structure change uses the other of two slot-name sets, so its values never land on the structure still on screen |
| 2 | done (vector 0.11.26 `snap`), all HTML payloads snap |
| 3 | done, seen in game: 818 in-place updates in 3 min, no elements created per tick |
| 4 | done for looping opacity/transform animations (CSS and script); finite ones keep the runner |
| 5 | **open, and the cause is known (2026-09-21)** — the per-element cache exists, but the structure still resends 1-5 times a second on a script-driven page. Three bugs make the structure depend on a value; see below |
| 7 | **conditional, and probably unnecessary** — see "Step 7". Jint's cost is per *script frame*; once step 5 lands a page writes values instead of rebuilding markup, and a realistic page ticks 2.4 times a second, not 70 |
| 6 | memory flat over 3 min (2,105 -> 2,011 MB); garbage collections 10 per 3 min |

Also fixed on the way: `overflow` no longer clips the element's own background, border and shadow
(the panel's shadow was tessellated and then clipped away: 8.3 ms -> 3.4 ms per rebuild); a pending
timer no longer keeps the layout panel awake (it re-rendered the page off-screen every frame);
the serializer kept dropping worker-assigned ids; internal attributes survive in-place updates;
a script setting the same animation value no longer restarts it.

Measured 2026-09-17, same restart, the 2x2 and 1x1 consoles blank, the 3x3 console running each in turn:

| | Lua regulator | HTML Apple page |
|---|---|---|
| frame mean | 13.77 ms (72.6 FPS) | 13.93 ms (71.8 FPS) |
| p99 | 14.9 ms | 15.0 ms |
| vector rebuild (off-thread) | 0.85 ms | 3.26 ms |
| structures / patches in 3 min | - | 4 / 278 |

A box whose declared background turns transparent keeps its node (the structure no longer
changes with a lamp's colour); a box that shrinks to zero keeps its node too.

## Off the game thread (2026-09-17)

UI Toolkit keeps its layout in native code only the game thread may read, so that is the one
reason the page needed the game thread for more than handing a scene to the vector mod. The game
thread now lays out and copies what the translator reads (`OffThread.Box` per element); a worker
translates the copy, splits it into template and values and decides the send; the game thread
sends it on a later frame. Font questions a worker cannot answer are answered after the job and
the page emits again. While a job runs nothing changes the page (Update waits; pointer, click,
data and source changes finish the job first).

Measured on the Apple page, game focused, 90 s runs: translation on the game thread 0.26-0.29 ->
0.036 ms/frame (copy 0.15 ms per emit; translation 2.5 ms per emit on a worker); all page code on
the game thread 0.47 -> 0.25 ms/frame, now including Lua data application, which was not counted
before. UI Toolkit's own update: 0.03 ms/frame. Remaining game-thread work: applying the page
script's results, keyframe runners and data binding (UI Toolkit writes).

Frame time cannot show differences this size: identical runs differ by up to 1 ms. Compare the
per-part timings.

## Our own layout engine (started 2026-09-17)

The page's remaining game-thread work (0.25 ms per console per frame, and consoles come by the
ten) is UI Toolkit's: its layout is native and game-thread only, so script results, animation
steps and data binding all had to be written into it there. The plan replaces it:

1. **Engine** (done): Yoga.Net vendored under `Yoga/` for netstandard2.1 with its per-layout
   garbage removed (48 KB per incremental layout of a 200-node tree -> 0.1 KB; its own layout
   tests pass against the copy). `TextMeasure` ports TextMeshPro's width and line-break
   arithmetic over face data copied once per face on the game thread (`FaceCopy`), so text is
   measured with the data the vector mod draws with; allocation-free, tested headless.
2. **Drop-in elements** (built): `Dom.cs`/`DomStyle.cs` give the part of UI Toolkit's API the mod
   uses (`style`, `resolvedStyle`, `layout`, `Children()`, the geometry callback,
   `MeasureTextSize`) over Yoga, with UI Toolkit's initial values and inheritance. The layout
   pass runs the geometry callbacks (grid, line boxes, baselines, mixed calc) until they stop
   writing, inside one call. UI Toolkit's document, panel settings and texture are gone.
3. **Side by side**: every test page's layout dump from the UI Toolkit build (the reference)
   against the same page with our engine; boxes matched by tree position.
4. **Everything on the page's worker**: script results, Lua data, clicks, hover, animations,
   transitions, layout and translation in one job per page per frame; the game thread passes in
   time and input and hands the scene to the vector mod.
5. **Measure**: three HTML consoles, focused, minutes; the target is ~0.02 ms per console per
   frame on the game thread.

Diagnostics line additions: `main N ms/frame, awake N frames, sent: N structures M patches (K values), J in-place`,
a `frames over 25 ms` line, and `new structure ... first difference` lines explaining each structure send.


---

## Step 5 in full (2026-09-21)

`ALLOCATION-PLAN.md` was folded in here, because it was step 5 under another name and two
documents would drift.

### Why the structure still resends

The Apple page sends 4 structures in 3 minutes. `examples/07-game.lua` sends **1-5 a second**, and
the mod's own `new structure ... first difference` lines name all three causes:

```
"G a=[$M221_a_0,...] t=[$M221_t_0,...] {"   ->  "R x=$M221_x ... id=pb10"
"box ... stops=[[0,#D8DDE3],[1,#8A949F]]"  ->  "box ... stops=[[0,#F1D36B],[1,#B08A1C]]"
"GL id=grad113_1 units=bbox ..."           ->  "GL id=grad121_1 units=bbox ..."
```

| # | cause | fix |
|---|---|---|
| 5a | the `G` transform wrapper is dropped when `transform` reaches identity, so a **value** changes the **shape** of the text | emit the wrapper unconditionally for anything that can transform, identity as slot values |
| 5b | gradient stops are template literals | stops become slots |
| 5c | def ids come from a page-wide running counter, so a re-emit renumbers them | stable per element — `d3b9b1d` claims this and is in the tree, so find why it does not hold here |

Until these land, the patch path built in step 1 only works for pages that happen not to trip them.

### What the target looks like, from the Lua pages

`CoolingUi/ColdbenchConsole.lua` and `ManufacturingUi/ManufacturingConsole.lua` already are what
the emitter should produce. They are the specification:

| | how the Lua does it |
|---|---|
| column layout | `cols(x, w, n, gap)` — arithmetic, **once, at build** |
| a 20-minute history chart | one `YS` + one `LS`, `x==X+i*step`, `y==base-clamp($hevap[i],0,1)*h`. A new sample shifts the array; no new nodes |
| a variable-length job list | `JOBROWS = 2` fixed slots; overflow becomes `"+N more ... see JOBS"` |
| rows appearing/disappearing | `fo = "=clamp($jb2_3,0,1)*0.06"` — presence is opacity from a slot, never node insertion |
| a real shape change | `lsig = concat{ #list, #MISSING, S.dim }` — a signature; rebuild only when it changes |
| a theme | `use_space(vb)` re-points the design space and **the caller rebuilds**: "every rect on screen is in the old space". Skins share token names, resolved at build |

So there is nothing HTML provides after the initial translation that the vector side cannot do.
Layout is answered **once**, at compile, and is arithmetic thereafter. A CSS theme switch is a
recompile, not a per-frame cascade.

### Compile-time cost — done 2026-09-21, measured, scenes byte-identical

These are now paid once per compile rather than per frame. Board kept so a half-finished pass is
recoverable; **tick with the number, not the word**.

| # | item | status | before | after |
|---|---|---|---|---|
| 1b | `WriteBatch` span scan, no `Split` | ☑ | claimed 5,792 | −2,530 script/f |
| 1c | `StyleApplier.Functions` + `Unit` on spans | ☑ | claimed ~3,000 | **−5,501** script/f |
| 1d | `NeedsMatrix` ordinal `IndexOf` | ☑ | 4.7% of strings | −1,332 script/f |
| 1e | cached `Report` delegate | ☑ | 3.7% of strings | −1,510 script/f |
| 1f | `_attrCache.Keys` hoisted out of its loop | ☑ | claimed 8.3% | −1,036 = **8.9%** of AtmoDark |
| 1g | `_ended` grace period (not removal — removal costs 2 structure sends per repeat) | ☑ | 756 B/f emit | **277 B/f** |
| 1h | SVG/canvas cacheable **+ frozen-gauge bug** | ☑ | 7,448 B/f | **32 B/f** |
| 1i | `EmitText` computes its rect before writing | ☑ | 64 B/f | 0 |
| 1j | `AppendNum`/`AppendNodeId` where they already existed | ☑ | 64-67 B/f | 0 |
| 1k | `Keep()` doubles instead of exact-sizing | ☑ | **1,675 B/f** | 0 |
| 1l | `Shadows()` cached by declaration | ☑ | 64 B/f | 0 |
| 1m | `Tweens.cs:445` plain loop | ☑ | 881 B/f | 793 B/f |
| 1n | `SceneSlots.Unescaped` grow-only `char[]` (a `StringBuilder` measured **worse**) | ☑ | 191 B/slot | **71 B** |
| 1a | ~~boxed `Children()` enumerator~~ | struck | claimed ~12 KB/f | **0 — this mod has its own DOM (`Dom.cs:128`)** |
| 1o | `FinishJob` / `ApplyExternals` / `VectorBridge` / pooled `UiProp[]` | ☐ | 4,792 B/f | — |
| 1p | `_attrCache` keys composed at 4 call sites | ☐ | — | — |
| 1q | `HtmlParser.Intern` is private, forcing ~12 duplicated lines | ☐ | — | — |
| 1r | `EmitText` counts newlines after escaping them — `lines` is always 1 | ☐ | rendering defect | — |
| 1s | `Tweens.Any` counts `_live` only → a finished transition replays once | ☐ | — | — |

**Bench blind spots found on the way — fix before trusting another bench number:**

| # | | |
|---|---|---|
| 0a | the bench does not compile `HtmlSurface.cs` or `VectorBridge.cs` | every pipeline total ever quoted from it excluded the bridge |
| 0b | the allocation window opens *after* `Tweens.Diff` and `OffThread.Capture` | ~12 KB/frame invisible |
| 0c | phases are labelled "main thread" in a single-threaded harness | page-thread work reported as game-thread |
| 0f | the bench never calls `tweens.Expire(now)` | item 1g was invisible to it entirely |

---

## Step 7 — the page's own JavaScript (2026-09-21)

**Do this last, and only if it is still needed.** Jint's cost is per *script frame* and
proportional to what the script does. The 16 MB/s measured on AtmoDark is not Jint being slow — it
is Jint building a 21 KB markup string that we then parse, morph, re-cascade and re-emit, which is
exactly what step 5 deletes. A page that writes 25 doubles into slots 2.4 times a second costs
almost nothing, whatever interprets it. **Measure a value-binding script after step 5 before
building any of this.**

The case that would still need it is a genuine per-frame game loop across many consoles
(`examples/07-game.lua`, 70 script frames a second) — the stress page, not the product. And even
there, motion that is a function of the clock should be compiled to an expression so the script
does not run at all; only real game logic needs a tick.

Measured over 15 consoles before step 5: a page with
its `<script>` stripped costs the same as no page at all (2.1 vs 2.2 MB/s); the same page with its
script costs 34.6.

**The version was the whole story.** `Microsoft.ClearScript.V8` was pinned at **7.4.5** on
2026-09-21, and every "V8 is too expensive" conclusion came from it. Re-measured on the same
machine:

| | 7.4.5 | **7.5.1.1** |
|---|---:|---:|
| host → script, 0 args | 1,520.1 B | **160.0 B** |
| host → script, 1 arg | 1,872.1 B | 296.3 B |
| `ITypedArray<double>.Read(64)` | 1,224.0 B | **48.0 B** |
| a frame: call + read 25 doubles | 3,096.1 B | **344.3 B** |
| JS writing a double into a shared `Float64Array` | — | **0.4 B** |

At 15 consoles that last-but-one row is **0.27 MB/s against a 2.2 MB/s floor**, so:

| | |
|---|---|
| 7a | bump ClearScript 7.4.5 → 7.5.1.1 (still `netstandard2.1`) |
| 7b | pages on V8; **delete `BindToFixed` and the `__fixed` shim on that path** (Jint workarounds, ~69,000 B/frame under V8) |
| 7c | the frame clock goes **in the buffer**, not as an argument (160 B vs 296 B) |
| 7d | **never bind `Action<double>`** — 5,716 B per call, the worst shape measured |
| 7e | `V8Engine.TryCreate` falls back to Jint with only a `LogWarning`, and stages the native DLL into `Path.GetTempPath()` — a long temp path exceeds `MAX_PATH` and the fallback is silent. An investigator measured a whole "V8" run before noticing it was Jint |
| 7f | **verify 7.5.1.1's native V8 loads under Unity's Mono** — untested, gates all of step 7 |

~~A hand-written P/Invoke layer~~ — struck. It measures 0.000 B and is the only route to literal
zero, but over 7.5.1.1 it buys **0.16 MB/s across 15 consoles** for ~37 bindings rewritten against a
C API and a failure mode where our own marshalling bug kills the game. It does not even avoid a
native dependency; ClearScript ships native V8 regardless. Revisit only if step 7 lands materially
worse than measured.

**Not doing:** reducing emits/s (masking); out-of-process (needs a shipped `.exe`, refused);
manual GC (a process-wide setting); another pure-C# engine (same heap).
