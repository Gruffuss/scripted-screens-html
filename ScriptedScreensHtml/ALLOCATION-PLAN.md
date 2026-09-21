# Getting the HTML mod under the allocation budget

Rewritten 2026-09-21 after four independent investigations. Every item names its cost, whether that
cost is **measured** or **inferred**, and the number that must move to call it done. A step is not
done until its number has moved in the stated place.

---

## How to use this file

**This file is the source of truth, not the conversation.** A session that does one item and then
gets compacted must be able to pick the rest up from here alone. So:

1. **Tick the item in the board below the moment it lands**, and write the measured before/after
   into the same row. Not "done" — the number.
2. If an item turns out to be wrong, **strike it and say why** rather than deleting it; a removed
   item looks identical to one nobody has done yet.
3. If a measurement contradicts what an item claims, **change the claim here first**, then act.
4. Nothing is ticked on the strength of a bench number alone where the row says `game`.

### Board

| # | item | status | before | after |
|---|---|---|---|---|
| 0a | bench compiles `HtmlSurface.cs` + `VectorBridge.cs` | ☐ | — | — |
| 0b | measurement window encloses `Diff` + `Capture` | ☐ | — | — |
| 0c | stop labelling bench phases "main thread" | ☐ | — | — |
| 0d | **ClearScript 7.5.1.1 native V8 loads under Unity's Mono** (gates 2-4) | ☐ | — | — |
| 0e | 7.4.5 → 7.5.1.1 crossing costs re-measured independently | ☑ | 3,096 B/frame | **344 B/frame** |
| 1a | boxed `Children()` enumerator ×3 | ☐ | ~12 KB/f | — |
| 1b | `WriteBatch` span scan, no `Split` | ☐ | 5,792 B/f | — |
| 1c | `StyleApplier.Functions` in-place scanner | ☐ | ~3,000 B/f | — |
| 1d | `NeedsMatrix` ordinal `IndexOf` | ☐ | 4.7% of strings | — |
| 1e | cache the `Report` delegate | ☐ | 3.7% of strings | — |
| 1f | `_attrCache.Keys` hoisted out of the loop | ☐ | 8.3% of AtmoDark | — |
| 1g | `Tweens._ended` no longer blocks the cache | ☐ | — | — |
| 1h | SVG/canvas become cacheable | ☐ | ~50 KB/f on a gauge page | — |
| 1i | `EmitText` computes the rect before writing | ☐ | ~300 B/label | — |
| 1j | `AppendNum`/`AppendNodeId` at :218, :592-593, :4199 | ☐ | ~140 B/f | — |
| 1k | `Keep()` doubles instead of exact-sizing | ☐ | ~20 KB spike | — |
| 1l | `Shadows()` cached by declaration | ☐ | 400-600 B/el | — |
| 1m | `Tweens.cs:445` plain loop | ☐ | ~96 B/eval | — |
| 1n | `SceneSlots.Unescaped` `[ThreadStatic]` builder | ☐ | ~1,265 B/f | — |
| 1o | `FinishJob` / `ApplyExternals` / `VectorBridge` / pooled `UiProp[]` | ☐ | 4,792 B/f | — |
| 1✓ | **Step 1 measured in game, 15 consoles, paired** | ☐ | 34.6 MB/s | — |
| 2a | bump ClearScript 7.4.5 → 7.5.1.1 | ☐ | — | — |
| 2b | pages on V8; delete `BindToFixed`/`__fixed` on that path | ☐ | 40,761 B/f | — |
| 2c | cache the `ScriptObject`; never bind `Action<double>` | ☐ | 5,716 B/call | — |
| 2d | V8 fallback to Jint becomes loud; fix `MAX_PATH` staging | ☐ | — | — |
| 2✓ | **Step 2 measured in game** | ☐ | — | — |
| 3a | slot registry + shared `Float64Array` frame protocol | ☐ | 5,930 B/f | — |
| 3b | the eight array-returning bindings reshaped | ☐ | — | — |
| 3c | string path gated on real change, pooled | ☐ | — | — |
| 3✓ | **Step 3 measured in game — the acceptance test** | ☐ | — | target ≤0.8 MB/s |

---

## The target and where we are

**Target:** game + this mod, 15 consoles *actually running a page*, under **3 MB/s** of managed
allocation. Not reached by culling, gating or lowering fidelity.

**Floor:** 2.2 MB/s measured (game + 59 mods + StationeersLua + the whole vector pipeline, consoles
powered, no page). So the mod's budget is ~0.8 MB/s.

**Today:** 15 consoles running `examples/07-game.lua` measured **34.6 MB/s**, which is
**12 collections in 90 s — a 30-44 ms freeze every ~7 s**. Pause length scales with live heap
(~25 ms per GB), so it gets worse as a base grows.

**Four harnesses independently reconciled the bench with the game** (29.1 / 33.4 / 34.3 / 40.8 KB
per console-frame against 32 MB/s measured). The bench can therefore price a change in seconds, and
only the engine's share needs the game to settle.

**A page with no `<script>` already costs ~130-290 B/frame.** The non-script pipeline is solved; the
32 MB/s is entirely script-driven pages.

---

## Step 0 — Fix the instruments first

Every wrong conclusion on 2026-09-21 came from a measuring tool, not from the code. Do these before
trusting another bench number.

| | what | why |
|---|---|---|
| 0a | Add `HtmlSurface.cs` and `VectorBridge.cs` to the bench's compile set behind Unity stubs | **They have never been compiled by it.** Every pipeline total ever quoted from the bench excluded the bridge. |
| 0b | Move the `Allocated()` window in `HtmlSurface.cs:997-999` to enclose `_tweens.Diff` and `OffThread.Capture` | They run *before* it opens and cost ~12 KB/frame between them — twice the measured cost of `Emit`. |
| 0c | Stop labelling bench phases "main thread" | The bench is single-threaded; in game that work is the page thread. |
| 0d | **Verify ClearScript 7.5.1.1's native V8 loads under Unity's Mono** | Untested. **Gates steps 2-4 entirely.** 30 minutes, one restart. |

---

## Step 1 — The C# work. Engine-independent, no new dependency, nothing thrown away.

Measured: four of these alone took the main thread from 13,623 → ~5,500 B/frame (−60%), and whole-
process from 34.3 → 23.4 KB/frame, with **byte-identical scenes on all seven examples**.

### 1a. The boxed enumerator, three places, ~12 KB/frame

`VisualElement.Children()` is declared `IEnumerable<VisualElement>` but returns a `List<>`, so
`foreach` boxes the enumerator (~40 B a call, 150 elements a walk).

- `OffThread.cs:227` (`Walk`) — ~6 KB per capture
- `Tweens.cs:322` (`Walk`) — ~6 KB per diff
- `VectorEmitter.cs:786` (`ByZIndex`), `VectorEmitter.cs:617`

Fix: `for (var i = 0; i < ve.childCount; i++) { var child = ve[i]; … }`. The indexer exists.

### 1b. `ScriptHost.WriteBatch` — 5,792 B/frame, and it is mine from this morning

`ScriptHost.cs` — `batch.Split('')` makes ~121 substrings plus an array every frame. Measured:
`Split` 5,792 B against **0 B** for a span scan of the same batch. Needs `StyleApplier.Apply` to take
spans rather than a `CssDeclaration` of two strings — that is the real work and it is mechanical.

### 1c. `StyleApplier.Functions` — ~3,000 B/frame on a transform-heavy page

A `yield return` iterator doing `Substring` + `ToLowerInvariant` + `Substring` + `Split(',')` +
`Trim` per argument. Replace with an in-place scanner. (Measured per write: `transform:
translateY(-14.2px)` = 328 B, a length = 56 B, a colour = 40 B, `opacity` = 0 B.)

### 1d. `StyleApplier.NeedsMatrix` — 4.7% of all string allocation

`transform.ToLowerInvariant()` on the whole value then 7 `Contains`, **called from `Apply` and again
from `VectorEmitter.EmitElement` once per element per emit**. Ordinal `IndexOf`, no lowercasing.

### 1e. `ScriptHost.ApplyStyle` — a fresh `Action<string>` per call

The `Report` method group is converted to a delegate on every call. Cache it in a field.

### 1f. `ScriptHost.cs:897` — `.Keys` inside a loop

```csharp
foreach (var old in replaced)
    foreach (var key in _attrCache.Keys)   // snapshots EVERY key into a new string[], per replaced id
```
**16.2 MB of a 194 MB AtmoDark trace — 8.3% of everything that page allocates**, the single largest
site in it. `foreach (var kv in _attrCache)` does not snapshot; hoist the scan out of the loop and
test membership against a set. Same `.Keys` pattern at `ScriptHost.cs:405` (not in a loop, cheaper).

### 1g. `Tweens.cs:330` — an element that ever transitioned is permanently uncacheable

`Of()` falls back to `_ended`, which is only cleared when a new tween starts or the element is
forgotten. `tw == null` is required by both the cache hit (`VectorEmitter.cs:290`) and `CacheUsable`
(`:707`), so such an element re-allocates its expression strings every frame forever. Drop the
`_ended` fallback once the tween's end frame has been emitted.

*(I attempted a change here this morning, measured a regression and reverted it — this is the
mechanism I was missing. `examples/09-transition.lua` is the repro.)*

### 1h. `VectorEmitter.cs:707` — SVG and canvas are never cacheable

`CacheUsable` is false for `SvgElement`/`CanvasElement`, so every shape re-emits every frame
regardless of change: **~800-1,200 B per shape per frame. A 50-shape gauge page is ~50 KB/frame from
this alone.** Invisible in `07-game` (no SVG); this is the Atmo/gauge cliff.

### 1i. `VectorEmitter.cs:1765-1766` — `EmitText`'s rect rewrite

`sb.Replace(" y=" + F(y) + " w=", …)` twice: 4 `F()` strings + 4 concats ≈ 300 B per clipped
single-line label, which on a dashboard is most of them. Compute the adjusted `y`/`h` *before*
writing the `T` line.

### 1j. Allocation-free helpers that already exist, beside code that does not use them

- `VectorEmitter.cs:218` — two `ToString("0.##")` on the `SCENE` line → `AppendNum`
- `VectorEmitter.cs:592-593` — `F(w - bw)` / `F(h - bw)` on the border path → `AppendNum`
- `VectorEmitter.cs:4199` — `" id=" + ve.name + …` → `AppendNodeId`, three lines below it

### 1k. `VectorEmitter.cs:724` — `Keep()` sizes exactly, with no headroom

The root's buffer is the whole scene, so a page whose text grows one character ("9" → "10")
reallocates ~20 KB plus one buffer per ancestor. `Output.Take` doubles; match it.

### 1l. `VectorEmitter.cs:399` — `Shadows()` re-parsed every rebuild

~400-600 B per shadowed element. A pure function of the declaration text; cache by it, exactly as
`ParseGradient` and `Lowered` already are.

### 1m. `Tweens.cs:445` — `Array.Exists(LayoutProps, p => prop.StartsWith(p, …))`

The lambda captures `prop`, so it is a display class **and** a fresh delegate per evaluation (~96 B).
A plain loop.

### 1n. `SceneSlots.Unescaped` — essentially the whole 1,265 B of Split

`SceneSlots.cs:319`, `:329` — a `StringBuilder` + its `char[]` + the result string per *changed* text
slot. `[ThreadStatic]` builder, as `Memory.Builder` already is; only the result string remains.

### 1o. The game-side path the bench never compiled

- `HtmlSurface.FinishJob` builds `"html:" + ElementId` on **every send**, three call sites — cache it
- `HtmlSurface.ApplyExternals` allocates a `HashSet<string>` every frame *before* checking whether
  there are any externals — early-return, hoist to a field
- `VectorBridge` per send: a `List<UiProp>` + `ToArray()` + a `UiElement` + `MethodInfo.Invoke(null,
  new object[6])`. Cache the array, `Delegate.CreateDelegate` the postfix once. **Measured 4,792 B
  today, 0 B pooled.**
- Pool the `UiProp[]` patch array. **This needs an addition to the vector mod**: `UiValue.Map` takes
  a `UiProp[]` whose length *is* the count, so a pooled array needs a count alongside it. `UiProp`
  and `UiValue` are structs, so nothing else on that boundary allocates.

**Step 1 verification:** bench per-phase totals on `07-game` **and** `AtmoDark` (different hot paths —
one agent's four patches moved `07-game` 32% and AtmoDark ~1%), plus `BENCH_VERIFY=1` proving the
emitted scene is byte-identical. An allocation win with no correctness gate looks exactly like a
page that stopped animating — one agent hit precisely that and its allocation "improved" to 261 B.

**Expected after Step 1:** ~19 KB/console-frame, ~17 MB/s at 15 consoles, a freeze every ~13 s.
**That does not meet the target on its own** and is not claimed to.

---

## Step 2 — ClearScript 7.5.1.1 and V8

The version bump is the single highest-ratio change available and everything after it depends on it.

- Bump `Microsoft.ClearScript.V8` and `.Native.win-x64` from **7.4.5 → 7.5.1.1** (still
  `netstandard2.1`).

  **Re-measured here independently of the investigation that found it, on .NET 8, and it agrees to
  the byte:**

  | | 7.4.5 | 7.5.1.1 |
  |---|---:|---:|
  | host → script, 0 args | 1,520.1 B | **160.0 B** |
  | host → script, 1 arg | 1,872.1 B | 296.3 B |
  | `ITypedArray<double>.Read(64)` | 1,224.0 B | **48.0 B** |
  | a whole frame: call + read 25 doubles | 3,096.1 B | **344.3 B** |

  At 15 consoles and 52 fps that last row is **0.27 MB/s, 12% of the floor** — the engine side
  alone clears the target. 7.4.5 was pinned arbitrarily on 2026-09-21 and is the sole reason V8
  looked like a dead end; three of the four investigations measured only that version.
- Switch pages to V8 with today's bindings. Measured end to end: `07-game` 40,761 → **~15,600
  B/frame**; `AtmoDark` 15,012 → **~5,580** (engine side 11,595 → 2,464).
- ~~Cache a `ScriptObject` instead of `MethodInfo.Invoke`~~ — **struck: worth nothing on 7.5.1.1.**
  Re-measured: `engine.Invoke(name, args)` and a cached `ScriptObject.InvokeAsFunction()` both cost
  exactly 160 B there. It was worth 312 B on 7.4.5, which is where that advice came from.
- **Pass the clock through the buffer, not as an argument.** Measured on 7.5.1.1: a 0-argument call
  is 160 B, a 1-argument call is 296 B. Writing the frame time into slot 0 and calling with no
  arguments makes a frame 160 + 48 = **208 B**.
- **Delete `BindToFixed` and the `__fixed` shim on the V8 path.** They are Jint workarounds and cost
  ~69,000 B/frame under V8; V8's own `toFixed` allocates nothing managed.
- **Never bind `Action<double>`** — measured **5,716 B per call**, the worst shape in the table.

### Two defects in my own V8 code, found by an agent

- `V8Engine.TryCreate` falls back to Jint with only a `LogWarning`. An agent measured an entire "V8"
  run before noticing it had been Jint the whole time, and the numbers read as plausible. Make the
  fallback loud.
- The native library is staged into `Path.GetTempPath()`; on a machine with a long temp path that
  exceeds `MAX_PATH` and is exactly how the silent fallback above triggered.

---

## Step 3 — The shared-buffer frame protocol

Measured in isolation, one realistic frame of 25 animated style writes:

| | B/frame |
|---|---:|
| script builds a `` batch string and hands it over | 1,905 |
| …plus `WriteBatch` splitting it into fields (today) | **5,930** |
| **script writes 25 doubles into a shared `Float64Array`; host makes one call and one `Read`** | **265** |

22× cheaper and 2× faster. The design that implies:

- **Register once, write by slot.** Every `(element, animatable property)` pair gets an integer slot
  at first use; the prelude keeps element→slot, the host the inverse. A frame is `__buf[slot] = v`.
- **One crossing in, one out, per frame** — `InvokeAsFunction` (160 B) + `ITypedArray.Read` (48 B).
- **Strings only when they change**, gated on actual change and pooled. Text, class names and
  colours keep a string path.
- **The eight array-returning bindings** (`__query __children __attrs __rect __size __viewport
  __scrollOf __children_rects`) each need reshaping — numeric ones into the buffer, the rest packed
  into one string. **This is the bulk of the porting work, not the frame loop.**

**Expected:** ~400-800 B/console-frame → **0.3-0.6 MB/s at 15 consoles, 15-30% of the floor.**
*(Protocol measured in isolation; end-to-end inferred, medium-high confidence.)*

---

## Not doing: hand-written P/Invoke

Raw `DllImport` with blittable arguments measures **0.000 B**, and it is the only route to literal
zero. It buys the last ~208 B/console-frame over Step 3 — **0.16 MB/s across 15 consoles** — for
~37 bindings rewritten against a C API and a failure mode where our own marshalling bug kills the
game. **Not worth it.** Revisit only if Step 3 lands materially worse than inferred.

Note this does not avoid a native dependency: ClearScript ships native V8 either way. It avoids
*hand-writing the binding layer*, which is where the risk actually lives.

## Also not doing

- **Reducing emits/s** — masking, ruled out.
- **Out-of-process** — needs a shipped `.exe`; refused, and Windows would flag it. Dead, not deferred.
- **Manual GC mode** — a process-wide setting; spending the rest of the game's frame budget on our
  consoles deserves the blame it would get.
- **Another pure-C# engine** — its objects land in the same heap.

---

## On "zero"

The vector mod reaches zero because it has no interpreter, no strings and a fixed-shape output. This
mod runs arbitrary user JavaScript and emits text. **Zero for a frame in which no text changed is
reachable; zero unconditionally is not, and the residue is proportional to changed characters, not
to frame rate.** For a numeric dashboard that is a handful of bytes a second.

Correction to a premise used earlier, including in the briefs given to the agents: the vector mod's
zero is its *fast path's* zero. It still allocates a `Stack<Frame>` per `EmitCore`, a closure and a
`Task` per dispatch, and per-group lists for filters, masks, clips and concave paths. "Small enough
to vanish into the floor" is what it actually achieves, and it is the right bar.
