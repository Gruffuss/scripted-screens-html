# Bugs found, and what happened to them

Every defect noticed while doing something else, so none is lost to the end of a session. Fixed ones
stay on the list: the shape repeats, and the list is the evidence for that.

**The shape that keeps recurring** — something looks wired up, passes every test, and silently does
nothing or the wrong thing. Five of the entries below are that one bug wearing different clothes.
It is why the project's rule is to fail toward a loud refusal rather than toward a plausible default.

---

## Open

| # | what | where | why it matters |
|---|---|---|---|
| 1 | **`snap: true` on forwarded data is built and never measured.** A data page's payload was eased, which keeps the renderer's blend window open, which keeps the scene "animated" — so a 2 Hz page rebuilt its mesh at up to 60 Hz. | `HtmlSurface.cs` (ForwardData) | Should be ~25× on the term that dominates allocation. Also a visible change: a gauge with no CSS `transition` will step instead of gliding. Needs one game session to confirm. |
| 2 | **A rebuild allocates ~73 KB in game where the same scene tessellates to ~5.5 KB offline.** Never explained. | vector side | This is the other factor in `MB/s ≈ rebuilds/s × KB/rebuild`, and it sets the floor for hand-written consoles too. No amount of work on the rate removes it. |
| 3 | **The blocker histogram cannot tell `ctx.fill()` from `Array.fill`.** The manifest is keyed by method name alone, so implementing the array method removed the canvas one from the scoreboard. | `JsToLua.PreludeMethods`, `Corpus.cs` | Two pages read as unblocked that are still blocked by `beginPath`/`arc`. A scoreboard that reads better than the truth. Same family as #8. |
| 4 | **`getElementsByTagName` / `ByClassName` see only nodes the script built.** The page's own markup is not enumerable at run time. | `JsPrelude.lua` | A page that queries its own static markup gets an empty list, not an error. Needs the id table to carry tags and classes. |
| 5a | **`AblateSend` is read by nothing** (see #33) — and the measurements it produced were used to decide where allocation was going. | `HtmlConfig.cs` | Either wire it into the chunk or delete it; a diagnostic that lies is worse than none. |
| 5b | **`console.*`, `localStorage` and `JSON.parse` are inert stubs.** | `JsPrelude.lua` | Compiling a page silently deletes its own diagnostics: the author's `console.error` — the first thing anyone reaches for when a console draws wrong — goes nowhere. `JSON.parse` returns nil always, so `JSON.parse(s).foo` nil-indexes and `JSON.parse(s) || {}` quietly takes defaults. |
| 5c | **A `className` state that changes anything but x/y compiles as a state that draws nothing.** | `PageCompiler.cs` | `el.className = 'alarm'` against `.alarm { background:#f00 }` compiles, logs a state binding, and never changes colour. |
| 5d | **`DomWrites` misses writes inside non-top-level function declarations.** | `DomWrites.cs` | No write means no `Unmapped` entry, so the page compiles and the animation is simply dead. |
| 5e | **`style.opacity`/`transform` on an element whose group carries no id bind to slots the compiler has just proved do not exist.** | `DomSlots.cs` | The chip computes and sends them every frame for the life of the console and the renderer ignores them. |
| 5g | **`DomWrites` never marks an `on*` assignment's body as runtime.** | `DomWrites.cs` | `addEventListener` is a known scheduler so an inline handler's writes are runtime; `el.onclick = function(){…}` is an *assignment*, so its writes are classified setup and baked instead of bound. Harmless while `onclick` was dead. It fires now. |
| 5h | **`CompiledRun.Pointer` is dead code** — nothing calls it. | `CompiledRun.cs` | `mousemove`/`mouseover`/`mouseout` reach the interpreted path only. |
| 5i | **`Date`'s calendar accessors need one number from the host.** | `JsPrelude.lua` | `js_now()` is milliseconds since the scene was applied, so `getFullYear()` built on it answers 1970 for ever — silently wrong rather than a visible limit. The honest route is an `EPOCH` set from the **game's own world clock** (not `DateTime.UtcNow`, which differs per client). Left out until that exists. |
| 5f | **Twelve probe fixtures cannot reveal the property they test**, so CSS coverage is understated. | `CssLanguage.cs` | `margin-bottom` on a fixed-height item in a row, `grid-row-end` where start 2 / end 3 *is* the baseline, `object-fit` with no image present, the SVG properties probed on a `rect`. A measurement that reads worse than the truth wastes the same time as one that reads better. |
| 5 | **`getComputedStyle` returns the element's own inline style.** A value that came from the stylesheet reads as nil. | `JsPrelude.lua` | Wrong answer rather than no answer. Should either resolve the cascade or refuse. |

---

## Fixed this session

| # | what | where | how it presented |
|---|---|---|---|
| 6 | **`DOM.missing` was written by the runtime and read by nothing.** | `JsPrelude.lua` → now drained by `ChipHost.MissingIn` | Every write to an element the scene has no shape for went nowhere and said nothing. Became urgent the moment the prelude gained `appendChild`: a script-created node cannot have been laid out, so a page creating rows would compile, run, and draw none of them silently. |
| 7 | **`Math.log2` was in the manifest and had never been defined.** | `JsToLua.PreludeMethods` / `JsPrelude.lua` | Compiled cleanly, died on a console with no line from the page. The manifest is checked by a regular expression, which cannot see a method stored under a quoted key — so the check passed. |
| 8 | **`Array` and `Date` were listed as provided globals that did not exist.** | same | `Array.isArray(x)` and `Date.now()` compiled and then died. |
| 9 | **`clearInterval` was a no-op.** | `JsPrelude.lua` | A page that started a poll and stopped it kept polling for ever. |
| 10 | **`StyleApplier.Reported` was a process-global that was never cleared.** | `StyleApplier.cs` | An unsupported declaration warned once per *process*, so every page after the first looked clean. Harmless in game (one page per surface); in the corpus sweep it undercounted badly. |
| 11 | **CSS escapes were not handled at all.** | `CssParser.cs` | 763 warnings. Tailwind writes class `dark:bg-x` as `.dark\:bg-x`; the parser split on the escaped colon and discarded the rule. Several hundred rules were being thrown away — fixing it made them parse *and match*. |
| 12 | **Brackets inside a functional pseudo were hoisted out before the pseudo was read.** | `CssParser.cs` | `div:not([hidden])` became `:not()` with an empty argument — dropping the rule — *and* applied `[hidden]` positively to the compound. Wrong twice in opposite directions. |
| 13 | **An unsupported pseudo took its rule-mates with it.** | `CssParser.cs` | `::selection, .keep { … }` silently lost `.keep`. |
| 14 | **A multi-layer background was parsed as one gradient.** | `StyleApplier.cs` | 212 warnings. The whole string went to the single-gradient parser, which took `LastIndexOf(')')` and read layer two's text as layer one's stops. |
| 15 | **The cascade failed three ways on `{{ }}` template placeholders and named none of them.** | `HtmlRenderer.cs` | Reported as `animation "}}" has no @keyframes` and `border value "{{" not understood`. I had diagnosed these as a parser nesting bug plus an unresolved `var()`; both wrong. 13 design-tool exports simply ship unrendered. |
| 16 | **The bench emitted anonymous transform groups.** | `ScriptedScreensHtml.Bench` | It never called `NameDrivenGroups`, so the scene it measured differed from the game's in exactly the place a compiled page writes. Every measurement taken with it was of a different scene. |
| 17 | **`Motion` expressed a value sitting behind an early return.** | `Motion.cs` — caught by its own test before shipping | `if (state !== 'running') return` is how a page pauses; reading past it produced a parallax that kept scrolling after the run had stopped. Arithmetic pure in `t` and still the wrong picture. |
| 18 | **`Motion` folded a self-referential assignment to a constant.** | `Motion.cs` — caught by its own test before shipping | `g.phase = g.phase + 0.02` evaluated to `0.02` and stayed there: a page frozen one frame after it loaded. |
| 19 | **`Motion` refused a single-axis move for the axis it never touched.** | `CompiledPage.cs` | `translateX` says nothing about y, and treating that as "could not express it" refused most real motion. |
| 20 | **My own markup probe reported "0 fixed" for pages whose entire structure had reduced.** | `MarkupProbe.cs` | It counted only top-level parts, and a real page's top level is a choice chain. A scoreboard reading worse than the truth wastes as much time as one reading better. |
| 21 | **Every ternary was treated as a choice of shapes.** | `Markup.cs` | `(on ? 'var(--cb-live)' : 'transparent')` inside a style attribute picks a colour, not a shape — but counting it as structure gave AtmoDark 119 choices, which is 2^119 scenes to emit, against the 23 the page really has. A branch whose sides contain no markup is a value and gets one slot. 149 → 6 on AtmoApple. |
| 22 | **A markup reduction that produced no structure said nothing.** | `Markup.cs` | A page whose document is built in a loop reduced to a single hole and no tags, and the caller got "1 hole, 4 characters" with no problem reported — indistinguishable from a page that genuinely has almost no markup. It now says the markup is computed rather than built from literals. |

| 23 | **`try { var a = 1; } catch (e) {}` — the most ordinary shape there is — was refused outright.** | `JsToLua.cs` | `try` compiles to `pcall(function() ... end)`, so a declaration inside became a local of the closure and vanished. Names are now declared before the pcall. |
| 24 | **`catch (e)` never bound `e`.** | `JsToLua.cs` | `pcall` returns success AND the error and only success was read, so the handler saw an unset global. It compiled, it ran, and every catch block was blind. Silent-failure family again. |
| 25 | **A `return` inside a `try` was swallowed.** | `JsToLua.cs` | It returned from the pcall closure, not from the function the page wrote it in. A flag and a value carry it out now. |
| 26 | **An uncaught error inside `try`/`finally` was discarded.** | `JsToLua.cs` | With no catch clause the pcall absorbed the fault and execution continued, so a page with a genuine error looked merely frozen. It re-raises. |
| 27 | **I nearly shipped a prelude that would not parse.** | `JsPrelude.lua` | Bitwise was written with Lua 5.3's `&` `\|` `<<`, which the interpreter the game embeds does not have. A prelude that fails to parse takes down EVERY page, not just ones doing bitwise. Caught only by the test that runs each page under that same interpreter — the standalone `lua` on PATH accepted it happily. Rewritten as arithmetic and checked against JavaScript's own answers for `& \| ^ ~ << >> >>>`. |

| 28 | **`Date.now()`, `new Date().getTime()` and `performance.now()` all returned a hard 0, for ever.** | `JsPrelude.lua` → clock now defined in `CompiledPage.Runtime` | They read `js_now`, which was referenced twice and **defined nowhere**. A page using the ordinary elapsed-time idiom — `Date.now() - start` — got zero every frame and froze at its first, with nothing in any log. `Date` was in `Provided` and `now`/`getTime`/`valueOf` in the manifest, so it compiled clean. The prelude's own check asserted `type(Date.now()) == 'number'`, and 0 is a number. |
| 29 | **`setTimeout` repeated for ever.** | `CompiledPage.cs` (frame loop) | It recorded `once = true` and the loop never read it. The shipped example `09-transition.lua`, whose own comment says it "fires once ... and is then finished forever", flipped its text every second instead. A one-shot is now cleared *before* it runs, so a handler that reschedules gets a fresh slot. |
| 30 | **A page using `requestAnimationFrame` never ran any timer.** | `CompiledPage.cs` (frame loop) | Timers were the `else` of the rAF branch, so the ordinary browser combination — a render loop plus a polling interval — silently lost every interval callback while the animation looked perfectly healthy. |
| 31 | **`DOM.known` was read and never assigned, so the missing-element reader added the same day could not fire.** | `JsPrelude.lua` / `CompiledPage.cs` | The characteristic bug with the halves swapped: #6 added a reader for a table nothing wrote, and the producer's own guard tested a set nobody filled. Both halves now exist. |
| 32 | **`SENDNOTE` was read exactly once per page.** | `CompiledRun.cs` | The first success was reported and every later send failure was written and never read — the same silence the note was added to remove. Every distinct note is now reported. |
| 33 | **`HtmlConfig.AblateSend` is bound, shown in the settings UI, and read by nothing.** | `HtmlConfig.cs` | **Still open.** Switching it on changes nothing, so the ablation measurement returns the un-ablated number — i.e. it reports the send path costs zero. Measurements taken with it are suspect, including ones quoted in CLAUDE.md. |

| 34 | **`@supports` blocks were taken unconditionally, `not` included.** | `CssParser.cs` | A page's `@supports not (...) { fallback }` was applied *on top of* the rules it was the fallback for — and being later in the sheet, it won. The probe row for it was green only because of this. |
| 35 | **`[attr=v s]` silently matched nothing.** | `CssParser.cs` | The `s` flag was never stripped, so the value became `"Hello World" s`. The `i` flag was stripped and then compared case-sensitively anyway. |
| 36 | **`colspan` only worked on a cell that carried an `id`.** | `HtmlRenderer.cs` | The row builder looked each cell up in `ById` and skipped it when absent — which is almost every cell. |
| 37 | **`CssParser.ReportedPseudos` was a process-global, never cleared.** | `CssParser.cs` | #10 verbatim, in a second file. The first page reported its gaps and every page after it looked clean. |
| 38 | **`@property`'s `initial-value` was stored and then ignored.** | `CssParser.cs` | The resolver read it and set `unresolved = true` regardless, and the caller drops an unresolved declaration — so a *declared* custom property behaved exactly like an undefined one. |
| 39 | **`<a>` was coloured with or without `href`.** | `HtmlRenderer.cs` | A browser colours `a:any-link`. |
| 40 | **`display: flex` overwrote a cascaded `flex-direction`.** | `StyleApplier.cs` | It set `Row` as a side effect, and inline styles apply last — so `<div style="display:flex">` plus a rule `flex-direction: column` laid out as a row, every time. |
| 41 | **A `background-image` gradient was dropped whenever a `background` colour shorthand also matched.** | `VectorEmitter.cs` | The emitter took the shorthand first, found a colour, and never looked at the longhand. This alone is why six background properties read as unsupported. |
| 42 | **`outline-width` + `outline-color` with no `outline-style` drew an outline a browser does not.** | `StyleApplier.cs` | The initial `outline-style` is `none`; the style was read only to cancel an outline, never to make one. |
| 43 | **A named `justify-items`/`justify-self` was ignored in any auto-sized grid column.** | `GridLayout.cs` | An `|| contentCol` overruled it. |
| 44 | **`grid-template-areas` was parsed for names only**, so a page relying on it for the grid's shape laid out in one column. | `GridLayout.cs` | |
| 45 | **A tiled gradient emitted one shape AND one gradient def per tile.** | `VectorEmitter.cs` | A 4px `background-size` over a panel wanted 1,250 of each. Capped at 64 with a warning. Found before it shipped, not after. |
| 46 | **`MarkupSlots` broke the mod build.** | `MarkupSlots.cs` — mine | `OffThread.Boxes` is null until a surface has run once; the analyzer caught it and `TreatWarningsAsErrors` turned it into a build failure the tests project did not see. |

| 47 | **`PAGE` was a one-shot snapshot, so nothing could observe a page after its first moment.** | `JsToLua.cs` | The chunk copied its locals out once at the end of the top level. Every value an event handler, a timer or a frame produced was invisible. It made a coverage probe report 5 of 22 event members — and **four of those five "passed" because the value before the tail happened to equal the expected one**. They would have passed with the event system deleted. `PAGE_SYNC()` takes a fresh snapshot on demand. |
| 48 | **The probe's frame loop was a stale copy of production**, carrying #29 and #30 after both were fixed. | `DomLanguage.cs` | It still ran timers as the `else` of the animation branch, so the probe measured the old bug and reported timers at 27%. With the real loop: 91%. A probe that disagrees with production measures the probe. |
| 49 | **`event.clientX`/`clientY` were always 0 on a compiled page.** | `HtmlSurface.cs` | `_pointerPage` was computed only inside the handlers a compiled page never reaches, because both pointer entry points return early for it. The coordinates survive the whole chunk path correctly — they started at the origin. |
| 50 | **`document.createElement` returned one node per TAG, not per call.** | `JsPrelude.lua` | Two `createElement('div')` were the same object, so a page building rows appended a node to itself and every write to either landed on the other. |
| 51 | **Four more instances of #7** — a manifest name with no definition. | `JsPrelude.lua` | `contains`, `querySelector`, `querySelectorAll` on an element, and `classList.replace`. Each compiled clean and died on a console. That makes six of this exact bug today. |
| 52 | **`addEventListener`'s third argument was ignored outright.** | `JsPrelude.lua` | A capturing listener silently became a bubbling one, and `{ once: true }` repeated for ever. |
| 53 | **Every event had `bubbles = true`.** | `JsPrelude.lua` | So `focus`, `blur`, `mouseenter` and `mouseleave` would have bubbled, which none of them do. |
| 54 | **`el.onclick = fn` and `style.cssText = …` were recorded as writes to slots no scene carries**, and `el.id` read back nothing at all. | `JsPrelude.lua` | |

---

## Not bugs, recorded so they are not re-investigated

- **`heroEdge`, `tempEdge`, `l.edge` in `border` values** are not custom-property names and `var()`
  was never involved (see #15).
- **8 × `animation "cb-march" has no @keyframes`** is `brand-motion.html`'s own bug: a docs page that
  names four animations and defines none. The warning is correct; there is nothing to implement.
- **The 18 pages blocked on `DCLogic`** are not a compiler gap. They load a 69 KB `support.js`
  through `<script src>` and keep their own code in `<script type="text/x-dc">`, a type no browser
  executes either. A browser without that runtime renders them dead too.
