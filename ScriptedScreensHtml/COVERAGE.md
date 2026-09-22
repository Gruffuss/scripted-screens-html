# Coverage: what the mod supports of HTML, CSS and JavaScript

The mod's promise is that a page written the way you would write it for a browser runs on a console.
So the denominator is **the language**, not the files in this repository. A corpus sweep answers
"do our examples work", which says nothing about what someone writes tomorrow.

Everything below is measured by a probe that can be re-run in a second. No number here is an
estimate.

**[GAPS.md](GAPS.md) lists every missing item by name** — all 255 lines of it, generated from the
probes. This file has the numbers and the plan; that one has the enumeration, so no surface ever has
to be audited twice. Regenerate it whenever the numbers move.

```bash
cd ScriptedScreensHtml.Tests && dotnet run -c Release -- --language      # JavaScript
cd ScriptedScreensHtml.Tests && dotnet run -c Release -- --csslanguage   # CSS
cd ScriptedScreensHtml.Tests && dotnet run -c Release -- --domlanguage   # DOM + HTML
```

---

## Where it stands, 2026-09-22

"Not gaps" are named one by one with a reason, not waved at: a console has no printer, no browser to
hint, no bidi pass, no inline flow, no blending and no third dimension. They are kept out of the
score rather than counted as successes, because counting a correct refusal as a win is the opposite
error and hides a real gap if the reason ever stops being true.

| surface | | | not gaps | accounted for |
|---|---|---|---|---|
| **CSS selectors** | 92% | 81/88 | 7 | **100%** |
| **CSS at-rules** | 91% | 31/34 | 2 | **97%** |
| **HTML elements** | **100%** | 112/112 | — | **100%** |
| `classList` / `style` / `dataset` | **100%** | 16/16 | — | 100% |
| JS standard library | 95% | 165/174 | — | 95% |
| DOM `Document` | 94% | 15/16 | — | 94% |
| CSS values and functions | 92% | 98/106 | — | 92% |
| DOM `Node` / `Element` | 91% | 48/53 | — | 91% |
| Events | 91% | 20/22 | — | 91% |
| Timers and frames | 91% | 10/11 | — | 91% |
| JS syntax | 87% | 61/70 | — | 87% |
| Global HTML attributes | 88% | 22/25 | — | 88% |
| **CSS properties** | 84% | 286/342 | 52 | **99%** |
| DOM `HTMLElement` | 74% | 17/23 | — | 74% |
| **style writes that reach the scene** | **28%** | **13/47** | — | **28%** |

Movement on 2026-09-22, and most of it was the instrument rather than the renderer:

| surface | was | now |
|---|---|---|
| CSS properties | 57% (195/342) | 84% + 52 named |
| CSS selectors | 73% (64/88) | 92% + 7 named, nothing missing |
| CSS at-rules | 71% (24/34) | 91% + 2 named, nothing missing |
| HTML elements | 99% | 100% |
| Events | 23% | 91% |
| Timers | 27% | 91% |
| JS syntax | 80% | 87% |
| JS standard library | 68% | 95% |
| style writes that reach the scene | 23% (11/47) | 28% (13/47) |

**[GAPS-CSS-REMAINING.md](GAPS-CSS-REMAINING.md)** names the four CSS properties and eight values
left, each with its reason. **[GAPS-RUNTIME-STYLE.md](GAPS-RUNTIME-STYLE.md)** is the 28% row: what a
script can change on a compiled page, why the border is in the scene under a name nothing can reach,
and the order the rest is worth doing in.

### How to read these

Five outcomes, and they are not equally bad:

1. **Refused** — the compiler says so, with a line. The page does not run, and the author knows why.
   This is the acceptable failure.
2. **Missing** — not implemented and not claimed.
3. **Accepted, draws the same** — parses without a warning and is then ignored. A page sets
   `background-size`, nothing complains, and the console quietly looks wrong.
4. **Present but answers nothing** — the member exists and returns `0` or `undefined`. A page
   reading `event.clientX` gets `0`, not an error.
5. **Refused although it works** — a warning naming a feature the renderer implements. Found eleven
   times on 2026-09-22, all in `StyleApplier.Dropped`: `backface-visibility`, `appearance`,
   `accent-color`, `offset-rotate`, `border-image-slice`, `mask-position`, `vertical-align`,
   `text-align-last`, `caption-side`, `empty-cells`, `column-span`. **Arguably the worst of the
   five**, because unlike silent acceptance it is read and acted on: it talks an author out of a
   feature that would have worked.

### And a sixth, which is about the probe rather than the mod

**Correctly absent, counted as a hole.** A probe that compares emitted scenes cannot tell a feature
nobody implemented from one that is implemented and correctly does nothing — both draw the baseline.
This cost more than every real gap found on 2026-09-22 put together:

| where | read as missing | actually missing |
|---|---|---|
| CSS selectors | 21 | 4 refusals + 0 gaps |
| CSS properties | 115 | 23 |
| HTML elements | 1 | 0 |

The fixture had no hover state, no circle, no `<text>`, no caption, no empty cell, no digit, no
image, no checkbox, nothing clickable, and a viewBox scale of exactly 1 — the one scale at which
`non-scaling-stroke` cannot be distinguished from doing nothing. **The lesson is not "check the
fixture".** It is that a probe which asserts its own premise will agree with itself, and the only
cure is to emit the thing and read what came out. Every surface here now names what it deliberately
does not do, so a correct refusal is never counted as a hole again.

### One caveat on the timer number

The timer probe exercises the prelude alone, and the loop that actually *drives* timers lives in a
C# string (`CompiledPage.Runtime`). So the row understates it — `setTimeout`/`setInterval`/`rAF` do
run on a compiled page. The probe should compile a real page instead; until it does, treat 91% as a
floor rather than a measurement.

---

## The two goals, and only one has moved

**A. It compiles and draws correctly.** Everything above.

**B. It then costs nothing.** The whole game plus 59 other mods sits at 2.2–2.6 MB/s; this mod alone
measured 26–40 across fifteen consoles, which triggers a collection and a visible stutter every few
seconds. **B has not moved at all today.** See the end of this file.

---

## The work, by surface

Each item ends with the probe number, not a description.

### 1. Events — 91%

Two of twenty-two members answer nothing, and they are the same gap twice: `event.key` and
`event.code`, so a compiled page cannot read the keyboard. The event object, the mouse coordinates,
`addEventListener` with its options, bubbling and `removeEventListener` all pass now. A key would
have to come from the input patch, through the surface, into the chunk, exactly as the coordinates
do — so this is not only a prelude job. Next door, `document.addEventListener` answers `0`: a
listener on the document itself is registered nowhere.

### 2. Style properties that reach the scene — 28%

Thirty-four of forty-seven style properties a script writes do not change the drawing. This is the
`style.x = y` path specifically, which is how a script animates anything, and it is now the lowest
row on the board by a wide margin.

### 3. CSS properties — 84%, plus 52 named as not gaps

Four left, and two of them are one item: `container-type` and `container-name` exist only to feed
`@container`, which is itself the one at-rule missing and currently throws the container's name away
and measures the design width instead (BUGS.md #83). The other two are `animation-composition` and
`object-position`. See [GAPS-CSS-REMAINING.md](GAPS-CSS-REMAINING.md).

Anything that genuinely cannot be drawn must **warn**, not be silently accepted.

### 4. `HTMLElement` — 74%

Five of the six gaps are deliberate: a compiled page has no layout to measure, so
`getComputedStyle`, `scrollTop`, `scrollHeight` and the rest honestly answer `undefined` and are
marked `[by design]`. That decision should be **revisited** — the compiler knows every box at
compile time and could bake them in, which would make the whole family real rather than
honest-but-useless.

One is simply absent: `scrollIntoView`. `focus`, `blur` and `click` now exist.

### 5. JS standard library — 95%

One object left: `Promise`, all nine members. It deserves a decision rather than an implementation —
a console page has no I/O to await.

Read the number with the probe's own caveat: the manifest is keyed by method name alone, so a name
two objects share counts as covered when either is (BUGS.md #3).

### 6. CSS selectors, at-rules, values — 92 / 91 / 92%

Selectors have **nothing** missing — the seven below 100% are all named refusals. At-rules have
`@container` alone. Values have eight, listed in GAPS.md, of which `s / ms` is a bad probe row
rather than a gap.

### 7. JS syntax — 87%

All nine remaining constructs are the same family, and it is the out-of-scope one: `Promise`,
`async`/`await`, generators, modules (`import`/`export`/dynamic import), `BigInt`, `Symbol` and
top-level await. Each refuses with a line naming itself, which is the acceptable failure. Regular
expression literals, previously the largest single item here, now translate.

### 8. `innerHTML` as structure — analysed, not wired

`Markup.cs` reduces a page's markup to fixed structure plus holes, and all three Atmo pages now
reduce (AtmoDark: 328 holes, 44 choices, 23,916 characters of fixed structure). The sentinel→slot
step now exists — `MarkupSlots.Resolve` lays out a skeleton, emits it, and reads back which slot
each sentinel landed on — and it carries **BUGS.md #90**, which destroys the live page while doing
it. The rest of the list is unchanged: emit a scene per reachable choice, emit the Lua that writes
them, and bind the registered handlers.

### 9. Canvas 2D

A real feature, its own project. `getContext`, `fillRect`, `fillText`, `beginPath`, `arc`, `stroke`,
gradients, `clip`.

---

## Goal B: it then costs nothing

Untouched. In order:

1. **`snap: true` on forwarded data** is built and never measured. Should be ~25× on the term that
   dominates, because an eased payload keeps the renderer's blend window open and a 2 Hz page
   rebuilds at up to 60 Hz.
2. **Event-anchored motion.** `Motion.cs` handles motion that is unconditionally a function of time
   and refuses anything behind a guard — which on an interactive page is nearly everything. The
   general answer is to anchor expressions on epochs the chip writes when state changes. Note it is
   **all-or-nothing per page**: nineteen writes of twenty converted saves nothing, because the
   twentieth still dirties the payload every frame.
3. **The 73 KB rebuild.** A rebuild allocates ~73 KB in game where the same scene tessellates to
   ~5.5 KB offline. Unexplained, and it is the other factor in `MB/s ≈ rebuilds/s × KB/rebuild`.
4. **`AblateSend` is read by nothing** (BUGS.md #33), so the ablation measurement reports the send
   path costs zero. Figures derived from it — including some in CLAUDE.md — are suspect.

---

## The honest boundary

Out of scope, to be **reported to the author** rather than approximated: network (`fetch`,
`XMLHttpRequest`, WebSocket), `eval` and `new Function`, workers, `IntersectionObserver` and
`MutationObserver`, History and Storage, and anything needing a layout read *during* a frame.

Not a compiler gap: 18 pages in the corpus load a 69 KB `support.js` through `<script src>` and keep
their code in `<script type="text/x-dc">`, a type no browser executes either.

## How this gets worked

By surface, scored by the three probes, reported at surface boundaries with numbers. No page is
patched to make it pass; if a page fails, the feature gets built. Bugs found on the way go to
[BUGS.md](BUGS.md) — 84 fixed, 20 open at the time of writing.
