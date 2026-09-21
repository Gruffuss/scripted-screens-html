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

### Vector 0.11.31 — the three blockers are gone (2026-09-21)

Everything that could not be a runtime value now can be. Built and deployed by the vector session,
offline-tested, **not yet seen in game**.

| | now |
|---|---|
| gradient geometry `x1 y1 x2 y2 cx cy r fx fy a` | expressions |
| gradient **stop positions and stop colours** | expressions; a stop colour written `$name` reads a colour from the payload |
| **clip geometry** — `CP id=track { R x=20 w=$w h=16 }` | live, re-cut once per rebuild |
| `G o=0` | returns at the group |

All re-read once per rebuild before the tree walk, so sampling, banding and refinement are
unchanged, and a gradient of plain numbers keeps the old path at no cost.

**Three behaviours to rely on, and one to design around:**

- **A live clip that evaluates to nothing hides its group** rather than falling back to unclipped.
  A bar at 0% disappears instead of flooding its track — the CSS behaviour, and the safe failure
  direction.
- A radial focus that was never stated follows a live centre rather than a stale copy; live stop
  positions that cross between ticks are re-sorted with their slots.
- **`G o=0` still walks a subtree containing a click region, an `SC` or an `IMG`**, deliberately: a
  browser sends clicks to an `opacity: 0` element. **This matters for step 6's "emit both themes and
  gate the inactive one": the hidden skin's buttons would still be clickable.** A switch was
  offered; asked for.
- A scene that emits nothing at all still draws the magenta "emitted nothing" marker. Old
  behaviour, easier to hit once groups are gated. Any page with a background rect never sees it.

**And a rendering bug they found in ours while testing, which predates all of this:** a two-stop
linear ramp whose stops do not span the shape kept ramping past its last stop instead of holding
that colour, so a fade to transparent never finished fading. `NeedsRefinement` treated every
two-stop linear gradient as exact, which only holds while the stops span 0..1. Fixed on their side.
**AtmoDark's fade will now reach fully transparent and may look different from what it was tuned
against** — check it on a console before assuming the page regressed. Ramps that span 0..1 are
untouched; where it applies a rounded box went 40 → 134 vertices.

---

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

**3. ~~Compile clock-pure motion to expressions.~~ Split update from draw.** *Rewritten 2026-09-21
after surveying every page in the repo; the original item is struck because it would have found
nothing.*

**A purity analyser over page JavaScript has nothing to harvest: 0 of 35 statements in AtmoDark's
`step()`, 0 of 27 in `07-game`'s `update()`, 0 of 109 bindings in its `values()`, 0 of 15 in
HtmlTest8's `spin()`.** The callbacks do not reference a clock at all — `frame(t)` converts `t` to a
delta and discards the absolute value; `step()` takes no time argument.

The reason is worth keeping: **every clock-pure animation on these pages is already in CSS**, which
`Tweens.cs` already compiles to `=from+(to-from)*ease(...)`. AtmoDark's five `@keyframes` map one
for one onto the hand-written console's `t`-expressions — `@keyframes cb-march{to{background-position:26px 0}}`
is `=mod(t*22.6,26)` — and the JS only *gates* them (`animation: state==='trip' ? 'cb-trip ...' : 'none'`),
exactly as `clamp($trip,0,1)*` gates the Lua one. CSS took that job first, so nothing is left in the
script for a purity pass to find.

**The seam that does exist is update/draw, and the pages are already cut along it.** `07-game`:

```js
update(dt);   // 27 statements, all memory, writes nothing visible
draw();       // 27 statements, no memory, writes 31 visible properties
```

`draw()` reads ~20 scalars and nothing else (`g.y g.step g.state g.clock g.score g.flashUntil g.far`
`g.near g.domes g.ground`, the duck flags, and a 4-slot obstacle pool of `x y kind`). Promote those
to `$data` and **25 of `draw()`'s 27 statements become vector expressions verbatim**; two are already
written in the target dialect (`Math.sin(g.clock*8 + o.x*0.01)*3`, `Math.floor(g.clock*6)%2`). The
two that resist are `className` writes, which are structural.

This is exactly the hand-written consoles' decomposition: `sample()`/`inner_loop()`/`history()`
accumulate inside Lua's `tick(dt)`; `page_trend()` declares the structure once with `$slot`
expressions; `up_trend(v)` writes the slots. There is **no accumulation anywhere in either scene**.

So the pass to build finds the scalars a callback writes that its DOM writes read, promotes them to
`$data`, emits the draw half as expressions at load, and leaves the update half for step 4.

**Two constraints from the survey:**

- **`T.text` takes `$name`, never `=expr`** (`ScriptedScreensVector/REFERENCE.md`). Every
  `textContent = fmt(x)` is a slot write however pure `x` is. This removes the commonest shape on
  these pages from the expression path entirely.
- `el.animate([...], { iterations: Infinity })` is the JS spelling of `@keyframes` — declarative and
  clock-pure. Route it through `Tweens.cs` with the CSS keyframes, not through any analysis.
  `HtmlTest6` probes it.

#### Step 3 worked by hand on `07-game`'s `draw()` (2026-09-21)

Done before writing any analyser, because the survey's "25 of 27" was a count and this is the
check. It holds, and it produces four requirements the plan did not have.

| statement | becomes | needs |
|---|---|---|
| `player.style.transform = translate(PLAYER_X, GROUND-80-g.y)` | `G t=[189,"=430-$y"]` | `$y` |
| `la = air ? 18 : (state==='running' ? (phase?24:16) : 24)` | `"=if(gt($y,0),18,if($running,if($phase,24,16),24))"` | `$running` |
| `phase = Math.floor(g.step) % 2` | `=mod(floor($step),2)` | `$step` |
| 14 pads, `x = (i*57+11-g.ground+1600) % 800 - 20` | `RP n=14 x="=mod(i*57+11-$ground+1600,800)-20"` | — |
| obstacles, `o.kind==='drone' ? sin(g.clock*8 + o.x*0.01)*3 : 0` | `RP n=4`, `"=$odrone[i]*sin($clock*8+$ox[i]*0.01)*3"` | `$ox[] $oy[] $odrone[]` |
| `scoreEl.textContent = pad(g.score)` | `text=$score` | a slot, never an expression |
| `player.className = cls` | depends | see below |

**The four requirements:**

1. **String state becomes a 0/1 slot.** `g.state === 'running'` has no expression equivalent — the
   language is scalar. The compiler must find comparisons of a state variable against string
   literals and emit one boolean slot per compared value (`$running`, `$over`). The driver writes
   0 or 1. Without this, `la` and `lb` are not expressible and the survey's count is wrong.
2. **A loop index maps to `RP`'s `i` directly.** `for (let i = 0; i < 14; i++)` over elements that
   differ only by `i` is a repeat, and `(i*57+11-$ground+1600) % 800 - 20` is the body verbatim.
   This is the largest single win on the page: fourteen elements become one node.
3. **A pool of objects becomes parallel arrays plus a repeat.** `obs[]` of `{x, y, kind}` becomes
   `$ox[i]`, `$oy[i]`, `$odrone[i]`. The language indexes arrays, so a fixed-size pool — which is
   how a game writes one anyway — needs nothing new.
4. **A `className` write is expressible if and only if the classes it switches between only
   declare properties that are themselves slottable.** Decidable at compile time by reading the
   stylesheet: if `.duck` only changes heights and offsets, it folds into the same expressions; if
   it changes `display` or adds a border, it is structural and the page needs both states emitted
   and gated (step 6). **This is the check that decides whether a page compiles cleanly, and it is
   a stylesheet question, not a JavaScript one.**

What remains for the driver, in Lua, per frame: about twenty scalars and three short arrays. That
is `up_trend(v)` in `ColdbenchConsole.lua`, which is the shape this is converging on.

**4. Transpile the remainder to Lua.**

**The mechanism exists and was checked against the shipped assemblies (2026-09-21), not assumed:**

| | |
|---|---|
| compile source into the chip's own state | `LuaState.Load(ReadOnlySpan<char> chunk, string chunkName, LuaTable? environment = null)` → a `LuaClosure`, which is a `LuaFunction` (`Lua.dll`, workshop item 3659911735) |
| isolate it | that `environment` argument. Generated code gets its own `_ENV` and cannot touch the author's globals |
| run it | `LuaState.RunAsync(LuaFunction, …)` |
| per-frame hook | `FrameCallbackManager.Register(chip, state, callback)` — `internal static`, reachable through the publicised ScriptedScreens reference we already use |
| budget | `MaxInstructionsPerFrame = 200000` (`FrameCallbackManager.cs:77`), four times the per-tick budget |

**Two constraints that decide the shape, both read from the decompile:**

- **One frame callback per chip, and registering replaces it.** `Register` ends with
  `instance._callbacks[referenceId] = callbackData;`. A compiled page that registers `on_frame`
  would silently evict the one the author's Lua registered. **So chain, do not register**: read the
  existing entry and install a wrapper that calls both, ours first. Alternatively emit the frame
  work as a named function in the page's environment and let the author's own `on_frame` call it —
  decide when the first page needs it, but never replace theirs.
- **Generated Lua must never yield.** `tick()` and `on_frame()` share the chip's root `LuaState`,
  and the file's own warning is explicit: *"running on_frame while tick is suspended races shared
  globals and closure upvalues"*. No `coroutine.yield`, no `ic.yield`, no `sleep` in anything the
  transpiler emits.

The subset a console page uses is small. The mismatches are
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

#### Step 4, the language half: built and diffed against the original (2026-09-21)

`JsToLua.cs` (AST to Lua source) and `JsPrelude.lua` (the runtime it sits on). **All six pages in
this repository transpile**, and `examples/07-game.lua` — the worst case, an endless runner with jump
physics, a spawn timer, collision, scoring, game-over and restart — produces **byte-identical DOM
writes** to the original JavaScript over **3,000 frames**, fifty seconds of play: 49 distinct writes, 0 differ.

**All six pass, and the check is permanent**: `ScriptedScreensHtml.Tests` runs it (`dotnet run`)
against the game's own `Lua.dll`, so a page is checked on the interpreter that will actually run
it rather than on a lookalike.

**The front end cost nothing, which was the first thing worth checking.** Jint already ships with
**Acornima**, a complete JavaScript parser, and `Acornima.dll` is already in the mod's deploy list.
So there is no new dependency and nothing to hand-write — and because the transpiler runs once at
load and unloads, the parser is compile-time only. Nothing of it is on the runtime path.

**Measured before writing a line of it: the language these pages use is 35 AST node types.** No
classes, generators, async, destructuring, template literals or `switch`. That is what makes this
tractable, and it is why the transpiler *reports* anything outside that set rather than
approximating it — a page that refuses to compile is a fixable problem, a page that compiles and
behaves subtly differently is not.

**The differential harness is the only reason any of this can be believed.** `scratchpad/luaprobe`
runs a page's script **both ways** — the original JavaScript under Jint, the transpiled Lua under the
game's own `Lua.dll` — drives the same frames into each, and diffs every DOM write. "It compiles" and
"it is correct" are different claims and only the harness separates them. Both sides get the same
deterministic `Math.random`, or the obstacle course diverges for a reason that is not a bug.

**Six things that were wrong, each found by running it rather than by reading it:**

| | |
|---|---|
| an immediately-invoked function | `(function(){...})()` — Lua calls only a name, a field or another call, so the parentheses are load bearing |
| a function closing over a later `const` | JavaScript's `const` is in scope for a function written *above* it; a Lua `local` is not, so the function read a global nothing ever set. Every top-level binding is forward-declared |
| `&&` and `||` in value position | were eager. `typeof location !== 'undefined' && location.hash` faults on the spot that way, and every Atmo page does exactly that. The right side is a thunk now |
| printing a number | JavaScript prints the **shortest** string that reads back as the same double; `%.17g` prints `933.33333333333337` where the page shows `933.3333333333334`. Ask for 15, then 16, then 17 digits and take the first that round-trips |
| a getter | `get el() { return $(this.id) }` becomes Lua's `__index`, which is the same idea — but the receiver has to be named, since an arrow function never has one |
| `$` in an identifier | legal in JavaScript, not in Lua. The page's own `const $ = id => ...` shorthand |

**A seventh, and the one worth generalising: every JavaScript construct that does not evaluate all
of its operands has to be emitted lazily, and a Lua function call evaluates all of its arguments.**
`js_if(test, yes, no)` looked like an honest ternary and ran *both* branches. On this page one
branch calls `Math.random`, so the obstacle sequence shifted by one draw and the two runs diverged
at frame 79 out of 180 — far enough in to look like a physics difference rather than a translation
one. `&&` and `||` had already been caught the same way; the ternary is the third member of that
family and there are no others. A ternary is now Lua's own `a and b or c` where the consequent
provably cannot be `false` or `nil`, which costs nothing and short-circuits for free, and an inline
closure otherwise.

**How it was found is the reusable part.** The frame-by-frame trace of both runs, diffed, named the
exact frame and the exact field: `nextSpawn` 788.75 against 665.63, with every other number in the
game identical. The difference was 123.12, which is `380 *` the gap between two consecutive draws of
the generator — so the answer was "one side drew an extra random number", not "the physics differs",
before any code was read. Guessing has a poor record on this project; a differential trace does not
need to guess.

**A seventh, which is a standing hazard rather than a fixed bug: Lua 5.4 has an integer subtype and
JavaScript does not.** Any product above 2^53 is exact in Lua and lossy in JavaScript, so the two
give different answers. It surfaced in the test's own random generator — the multiplier was large
enough to overflow — and the fix was to pick one that stays under 2^53 in both. A page doing wide
integer arithmetic would diverge the same way, silently. Not currently detected.

**Method calls go through a dispatcher, not Lua's `obj:name()`.** Giving JavaScript's string methods
to Lua's string metatable would work and that metatable is shared with the whole VM — the generated
chunk runs in its own `_ENV` precisely so it cannot reach the author's Lua, and mutating a global
metatable walks straight past that. A function the *page* put on an object is called with no
receiver, which is not a shortcut: measured, every function stored on an object across these pages
is an arrow function, and an arrow function has no `this`.

`LuaState.Load(ReadOnlySpan<char>, string, LuaTable)` and `RunAsync` were re-confirmed against the
shipped `Lua.dll` rather than trusted from the note above.

**The one divergence left in the repository is a language difference, not a translation bug, and it
is pinned in the test rather than hidden.** `AtmoApple` saves a scroll position with
`kept[id] = el.scrollTop` and restores it by walking `Object.keys(kept)`. In JavaScript, assigning
`undefined` still **creates** the property, so the key exists and the restore runs; in Lua,
assigning `nil` creates nothing, so there is no key. Preserving that would mean an `undefined`
sentinel threaded through every comparison, coercion and truthiness test in the prelude — a large,
risky change to keep a scroll position a compiled console does not have. It is listed as an allowed
difference for that page alone, so a *new* difference there still fails.

**The prelude is an embedded resource, not a file beside the DLL.** A page cannot be compiled
without it, and a file in a mod folder can be deleted, edited or lost in a Workshop update with
nothing saying so until a console goes blank — which is the failure this project has already had
three times over.

**"It reports rather than approximates" is a claim, so it is tested.** Seventeen constructs outside
the supported subset — a class, a template literal, destructuring, `switch`, `async`, a generator, a
real regular expression, a spread argument, an undeclared name, `do`/`while`, a labelled break,
optional chaining, `new`, `throw`, a setter — are compiled and must each be turned down with a line
number. **Sixteen were. The seventeenth was `??`, and it was compiling to `||`** — so `0 ?? 1` would
have given 1 where JavaScript gives 0, in a page that looked like it compiled fine. Exactly the
silent-mistranslation failure the whole reporting rule exists to prevent, and nothing but an
explicit test for it would have found it. It is now implemented properly rather than refused: Lua’s
`nil` covers both `null` and `undefined`, so the translation is exact.

---

#### What two agents found that a differential test could not (2026-09-21)

The six pages passed, which says the transpiler handles *what those pages happen to do*. Two agents
went at it from outside — one auditing the translation against JavaScript semantics, one completing
the prelude until the Atmo pages ran — and between them found **thirty-odd defects the passing test
could not see.** Worth stating plainly: a green differential suite over a fixed corpus is evidence
about the corpus, not about the language.

**The ones that were silently wrong — all now fixed, and each pinned by a snippet run both ways:**

| | what it did |
|---|---|
| an inner `let total = 0` sharing a name with a top-level one | **assigned the outer binding**, corrupting state in a part of the page that never mentions it |
| a nested `function draw()` | overwrote and destroyed the outer `draw` for the rest of the page |
| `var` inside an `if` or a `for` | died at the block's end, where JavaScript scopes it to the function |
| `??` | compiled to `||`, so `0 ?? 5` gave 5 |
| loose `==` | compiled as strict |
| `this` outside a getter | emitted a nil global |
| an assignment to a computed index used as a value | evaluated the index twice |

The first three are one fix: **every scope declares its own hoisted names up front** — function
declarations and `var`s at any depth below it, plus the `let`/`const` in its own statement list —
and a declaration assigns only when *that* scope hoisted it. That is simultaneously JavaScript's
hoisting, its function-scoped `var`, and correct shadowing.

**Loose equality is the interesting one.** It has no short Lua equivalent and coerces across types.
But every loose comparison across all six pages is `x == null`, meaning "null or undefined" — and
Lua's `nil` is exactly both. So that form compiles exactly and every other loose comparison is
reported, which is a one-character fix for whoever wrote it.

**And the ones that compiled and then failed at run time**, which is worse than refusing: a
`continue` followed by a `let` (Lua will not jump into a local's scope), a `continue` after a
sibling loop (the label counter was a high-water mark, not a stack), `arr.length = 0`, a literal as
the object of a member access, and `Date`/`Array` promised by the compiler and defined by nothing.

**The biggest single restoration of the contract was the method manifest.** `js_m` dispatched by
name at run time, so a page calling `splice`, `shift`, `charCodeAt`, `Math.log2` or `Object.values`
compiled cleanly and died on a console with nothing in its own source to point at. The method name
is known statically, so it is now checked against what the prelude defines — and it earned itself
immediately: **`05-script.lua` uses `appendChild`, `getContext('2d')` and `clearRect`**, so it is
refused by name instead of failing in game. A test compares the manifest against `JsPrelude.lua`,
since the two drifting apart would hand back the failure it was added to remove.

**A VM bug worth knowing before anything emits varargs: in this Lua, `...` does not survive a
numeric `for` in the same function.** After one runs, `select("#", ...)` over-counts and
`select(i, ...)` returns values nobody passed — and `table.pack(...)` after a loop is wrong too. It
depends on how many locals are live before the loop, which points at the loop's control registers
overlapping the vararg area. Every prelude function that reads `...` now reads it **before** any
loop. The transpiler emits no varargs today; this is the note for the day it does.

**Still open, all in the prelude and none reached by these pages:** `js_num(nil)` gives 0 where
`Number(undefined)` is `NaN`, so a typo becomes a plausible number rather than a visible one;
`parseInt('12px')` gives NaN; `replaceAll` is not always global; a `fromIndex` argument is ignored;
`toString(16)` ignores its radix; `Object.assign` returns a new table instead of mutating its first
argument; `NaN` does not poison `Math.min`. Each is listed with its demonstration in the audit.

---

#### What a six-agent survey found that the mapping had wrong (2026-09-21)

Six agents read the emitter, the slotter, the surface, the layout, the tween path and the
structural writes; a seventh asked what they had missed. **Three of their findings were things this
mapping had wrong, and every one would have drawn the wrong thing without saying so.** Recorded in
full because the remainder is the specification for what is left.

**Fixed, with the evidence:**

| | what it was | what it is |
|---|---|---|
| the scene is in **absolute** coordinates (`x = parentPos.x + layout.x`, `VectorEmitter.cs:339`) and CSS is not | `style.top` → `y` | `style.top` → `y` **plus the containing block's origin**. Measured: `legA` is at scene `y=141` with a CSS top of 56, inside a player whose own top is 85 — the raw value would have drawn it 85 units high |
| **moving an element does not move what is inside it**, because each descendant's box carries its own absolute position | one write, one slot | one write, **a set of slots with arithmetic**: `legA.style.top` writes `legA_y` *and* `bootA_y` |
| the transform group is **dropped at identity** unless the element has already moved or its CSS declares a transform (`VectorEmitter.cs:1250`) | fine, because the page had already animated | both tests are retrospective and a compile-once page never gets the frame that makes them true. `CanMove`/`CanFade` now also ask whether a script drives the element |
| two lines can carry the same id — a Label with a background emits `R` then `T` — and `<id>_f` goes to whichever comes first | `color` → `<id>_f` | refused, because that slot is the **box's** fill and the write would repaint the background instead of the text |

**The critic's headline, and it is the right frame: one DOM write is not one slot.** Four ways it
is not — a parent's move takes its descendants with it; an element with a border emits a second,
id-less geometry line; the transform anchor is `a=[x+w/2, y+h/2]` so a `w` write leaves a stale
rotation centre; and a Label with a background loses its text-colour slot to its own box. The rule
is **(element, property) → a set of (line, key) slots plus the arithmetic relating them**, written
in one payload.

**Still open, each with its demonstration:**

- **`textContent` is not a plain string.** Seven emitter paths shape it: escapes, `<mspace=0.6em>`
  around digit runs under `tabular-nums`, `<noparse>` around a purely numeric string, `<u>`/`<s>`,
  `BreakAll`, and a `<font=..>` span added after the fact. `<span id="score">00000</span>` emits
  `text="<mspace=0.6em>00000</mspace>"`. Compiled Lua writing the raw string loses digit alignment,
  or the text vanishes. The shaping has to travel with the slot.
- **`snap` is missing from the compiled write.** The surface passes `snap: true` on every patch
  today; the `VDATA:set_props{ scene=.., keep=1, data=.. }` shape has no `snap`, so every value
  would glide over the tick gap — a visible change on every page at once that reads as a
  mistranslation of the motion.
- **`rx` disappears when it clamps to zero.** When every clamped radius falls to <= 0.01 no `rx=`
  token is emitted at all, so a bar animating up from 0% has no radius slot to come back into and
  stays square for ever.
- **A pair slot has no first-come demotion.** The scalar case is handled, but a pair inserts only
  `X_t_0`/`X_t_1` and never the bare `X_t`, so two named wrapper lines on one element **silently
  share** them and the last write wins. Only the transform group is named today, which is why this
  is latent rather than live — but it bounds how many wrappers may ever carry an id.
- **A button without an author id has an unstable click id.** The synthetic name is
  `"__" + tag + (++_autoId)` from a process-wide static that is never reset, so it counts every
  element the process has built rather than the document. A compiled page's Lua handler would stop
  matching after a restart. Every clickable element a compiled page handles needs an author id, and
  the compiler should say so.
- **`color` is inherited**, so the write lands on descendants' `f`, not the element's own line.
- **`maskImage` is not a slot at all** — the only write changes the number of gradient stops, and
  `stops=` is literal by construction.
- **`animation` is slottable after all**, contrary to the note above: an infinite unpaused animation
  is emitted as `G o="=if(lt(p,..),..,..)"`, and every literal in that expression slots.
- **The emitted structure depends on which branch the setup code took**, so “decidable at compile
  time from the stylesheet” is weaker than it reads: an element can get a box only because setup
  gave it one, and a state setup never reaches has no node to gate.

**A live bug found in passing and fixed**: a label whose text begins with `=` was handed to the
renderer as an *expression*, so it drew nothing. The numeric guard covered `"3"` and not `"= 5 kPa"`.

**Read the whole critique before the next step** — it is the most useful artefact this work has
produced, and none of it came from the passing tests.

---

#### Step 3 built: the mapping, and what it turned out to hinge on (2026-09-21)

**Confirmed in game first.** The compiler had been checked exhaustively *outside* the game and none
of that said it runs *in* it. `Diagnostics.CompileProbe` compiles each page's script at build time
and logs the outcome, changing nothing a console shows. Every console:

```
html compile probe: ".../main/run" compiles - 11708 chars of JavaScript into 14875 chars / 444 lines of Lua, prelude 23182 chars
html compile probe: ".../main/web" compiles - 58682 chars of JavaScript into 75161 chars / 443 lines of Lua, prelude 23182 chars
```

Zero failures, zero exceptions. The embedded prelude loads and Acornima parses under Mono.

**`DomWrites` answers what the mapping must serve**, and the answer reorders the work. The compiler
runs *after* the page's setup code, so a setup-only write is already in the geometry and needs
nothing. Setup-versus-runtime is **call-graph reachability, not source position** — `draw()` is
written at the top level and only ever runs from a frame callback.

| page | runtime writes | shape |
|---|---|---|
| `07-game` | 22 | **values.** Every `innerHTML` on this page is setup only |
| `AtmoDark` / `AtmoLight` | **1 each** — `frame.innerHTML` / `panel.innerHTML` | the page rebuilds itself wholesale |
| `AtmoApple` | 8, two of them `innerHTML` | same |
| `05-script` | **0** | nothing runs after setup |

So **`07-game` is the page to make work first**, and the Atmo pages are step 6's problem, not step
3's. That was not obvious before counting.

**`DomSlots` is the mapping**, and the interesting half is not which key but **whether it is safe**.
A write is a slot write only when changing it cannot move anything else: an element out of flow owns
its box, while an element in normal flow decides where its siblings go and only the layout engine
knows that. Both halves report rather than guess.

**The acceptance test spans both halves of the compiler** — every runtime write `07-game` makes,
against **the scene the emitter really produced**, captured from the running game and checked in
beside the test. Result: **10 map directly, 4 through a group**, none unmapped. The ten land on
boxes the emitter already names (`legA_h`, `legA_y`, `score`).

**Two conditions checked against the page's own CSS rather than assumed:**

- `#player div { position: absolute }` puts every element `draw()` moves out of flow, so `top` and
  `height` map one-to-one onto `y` and `h`.
- **Every class the page switches changes only slottable things** — `.duck` moves descendants'
  `left/top/width/height`, `.hurt` and `.flash` change a colour, `.hidden` sets `opacity`. So
  `player.className = 'duck'` folds into slot writes rather than a rebuild. That is step 3's fourth
  requirement answered concretely, and it is a fact about the **stylesheet**, not the script.

**The one emitter change, and the reason it is narrow.** A transform lives on a wrapping `G` that
carries no `id=`, so its numbers get positional names nothing outside can address. The obvious fix
— name every wrapper — was **rejected after reading the renderer**: `SceneModel.cs:663` registers an
identified node in `scene.Identified` and retains its whole prop array, so that would retain hundreds
per page, which is the cost this redesign exists to remove. Instead the surface asks `DomWrites`
which elements the script actually drives and the emitter names only those — four on the game page.

**Still open for `07-game`:** the computed-id families (`$('pb' + i)` over fourteen pebbles, `o.el`
over a four-slot obstacle pool). Both are repeats, which the format already has, and the plan's step
3 requirements 2 and 3 describe them; neither is mapped yet.

---

#### What the DOM-write mapping actually has to cover, counted (2026-09-21)

Measured across every page in the repository before designing it, because the size of the problem
decides the shape of the answer. **Every write a page makes, by property:**

| style property | writes | becomes |
|---|---|---|
| `transform` | 6 | the `G t=[..]` pair's two slots |
| `top` | 5 | the box's `y` — **only when the element is absolutely positioned** |
| `height` | 5 | the box's `h`, same condition |
| `bottom` `color` `background` | 1 each | `y`, the label's `f`, the box's `f` |
| `maskImage` `animation` | 1 each | `animation` is `@keyframes`, which `Tweens.cs` already compiles; `maskImage` wants a look |

| element property | writes | becomes |
|---|---|---|
| `textContent` | 10 | `$id` — a slot, never an expression (`T.text` takes no `=expr`) |
| `innerHTML` | 10 | **structural.** Recompile, or enumerate the reachable states and gate them |
| `className` | 8 | decidable at compile time by reading the stylesheet — see step 3's fourth requirement |
| `scrollTop` | 1 | nothing. A compiled console does not scroll |

So the whole mapping is **eight style properties and four element properties**, and the interesting
half is not the translation but the **condition**: a write only becomes a slot when it cannot move
anything else. `top` and `height` on an absolutely positioned element reach that element's box and
nothing else; on a flow element they move every sibling, and only Yoga knows where those land. That
condition is a fact about the *stylesheet*, known at compile time, and the compiler must report the
cases it cannot serve rather than emit a slot that quietly does the wrong thing.

**`innerHTML` at 10 writes is the real work**, and it is step 6's, not step 3's: a page rebuilding
markup is enumerating a state, and the answer is to emit the states and gate them rather than to
translate the rebuild.

---
**What is NOT built, and is the next thing.** The transpiler translates the *language*; it does not
yet know **where a DOM write lands in the scene**. `$('legA').style.height = la + 'px'` has to become
a write to `$legA_h`, and only the emitter knows whether `legA` even has its own box, whether its
`top` reaches that box or goes through Yoga, and whether a `G` wrapper carries it. That mapping is
step 3's classification, and it is the join between the two halves. Until it exists the prelude
records DOM writes by name, which is what the harness diffs.

---

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
