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

---

## Not bugs, recorded so they are not re-investigated

- **`heroEdge`, `tempEdge`, `l.edge` in `border` values** are not custom-property names and `var()`
  was never involved (see #15).
- **8 × `animation "cb-march" has no @keyframes`** is `brand-motion.html`'s own bug: a docs page that
  names four animations and defines none. The warning is correct; there is nothing to implement.
- **The 18 pages blocked on `DCLogic`** are not a compiler gap. They load a 69 KB `support.js`
  through `<script src>` and keep their own code in `<script type="text/x-dc">`, a type no browser
  executes either. A browser without that runtime renders them dead too.
