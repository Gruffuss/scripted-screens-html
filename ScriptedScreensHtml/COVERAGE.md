# Coverage: the plan to make the mod take any page

The mod's promise is that a page written the way you would write it for a browser runs on a console.
Two things have to be true for that, they are independent, and only one of them has been worked on.

**A. It compiles.** Any valid HTML, CSS and JavaScript a person writes is translated. Not our
examples — the language.

**B. It then costs nothing.** A compiled page allocates nothing per frame. The whole game with 59
other mods sits at 2.2–2.6 MB/s; this mod must disappear into that, not sit beside it.

A gates B. A page that cannot compile runs the interpreter every frame for ever, so it cannot be
silent whatever else is done to it. Today **7 of 50 scripted pages compile**, so B currently applies
to seven pages and the other forty-three are not even in the race.

---

## The scoreboard

Two sweeps, both offline, both a second to run. Every change is scored by them; nothing is "done"
because one file got better.

```bash
cd ScriptedScreensHtml.Tests && dotnet run -c Release -- --corpus
```
Every page, what blocks it, and the blocker histogram across the whole corpus. Answers A.

```bash
cd ScriptedScreensHtml.Bench && dotnet run -c Release -- --css
```
Every page laid out by the real cascade, and what it warns about. Answers the CSS and HTML half of A.

For B the only instrument is the game: a paired A/B with 15 consoles, camera untouched, settled
medians. Offline allocation numbers do not transfer — Mono answers 0 for the per-thread counter and
the renderer's share is invisible to the bench.

---

## Where it stands, measured 2026-09-22

| | |
|---|---|
| pages in the corpus | 91 (120 including duplicates the CSS sweep sees) |
| no script at all | 41 |
| scripted | 50 |
| **scripted pages that compile** | **7** |
| pages with a frame loop (need B's hard half) | 8 |
| pages with no frame loop (compiling alone silences them) | 42 |
| CSS warnings | 1,182 across 155 distinct kinds |

Per-frame cost, last measured in game across 15 consoles:

| | MB/s |
|---|---|
| hand-written vector console | 6.8 |
| HTML page interpreted | 41.7 |
| HTML page compiled | 39.7 |
| baseline (game + 59 mods) | 2.2–2.6 |

### What blocks A — JavaScript and the DOM

| blocker | pages |
|---|---|
| `class` | **18** |
| `innerHTML` as a write | 7 |
| `appendChild` | 6 |
| canvas 2D (`getContext`, `fillRect`, `fillText`, `arc`, …) | 4 |
| `new` | 3 |
| `substr` | 3 |
| parse failures (JSX / non-JS syntax) | 3 |
| destructuring, `for…in`, `switch` | 2 each |
| a long tail of one-page DOM methods | ~25 distinct |

The tail is one page each but it is the same *kind* of gap thirty times over: `cloneNode`,
`insertBefore`, `insertAdjacentHTML`, `prepend`, `after`, `replaceWith`, `hasAttribute`,
`toggleAttribute`, `setProperty`, `getElementsByClassName`, `getElementsByTagName`,
`getBoundingClientRect`, `getComputedStyle`, `preventDefault`, `elementFromPoint`, `showModal`,
`showPopover`, `animate`, `structuredClone`, `crypto.randomUUID`, `TextEncoder`, `Map.set`,
`Array.shift`, `RegExp.test`. None of these is hard. All of them are missing.

### What blocks A — CSS

| family | warnings |
|---|---|
| selectors | 823 |
| gradients | 212 |
| parser faults | ~90 |
| `@keyframes` lookup | 39 |
| `border` values | 18 |

The selector bulk is modern CSS a design tool emits: escaped class names (`.dark\:bg-x`),
`:has()`, `:is()`, `:where()`, attribute selectors, `&` nesting, `::before`/`::after`.

Three of these are **bugs, not missing features**, and should be found before anything is
implemented: an animation named `}}`, a border value of `{{`, and border values that are plainly
custom-property names (`heroEdge`, `tempEdge`, `l.edge`). The first two mean the block splitter is
losing track of nesting; the third means `var()` is not resolved in `border`.

---

## The work, in order of measured weight

Each phase ends with the sweep numbers, not with a description.

### Phase 1 — the JavaScript language

1. **`class` and `new`.** 21 pages. Lua metatables: constructor, methods, fields, `extends`,
   `super`, getters/setters, `static`. This is the single largest item in the whole plan.
2. **Syntax:** destructuring (declarations, parameters, assignment), `for…in`, `switch`, spread and
   rest, default parameters, optional chaining, `try/finally`, labelled break.
3. **Built-ins:** the String, Array, Object, JSON, Map, Set, Date and RegExp methods real code uses.
   Mechanical, and best done by taking the list from the sweep rather than from memory.

### Phase 2 — the DOM

4. **Tree mutation:** `appendChild`, `insertBefore`, `removeChild`, `replaceWith`, `prepend`,
   `after`, `cloneNode`, `insertAdjacentHTML`.
5. **Queries:** `getElementsByClassName`, `getElementsByTagName`, `closest`, `matches`.
6. **Attributes and classes:** `hasAttribute`, `toggleAttribute`, `classList`, `dataset`,
   `style.setProperty`.
7. **Events:** `preventDefault`, `stopPropagation`, a real event object.
8. **Geometry:** `getBoundingClientRect`, `offsetWidth`/`offsetHeight`, `getComputedStyle`.

### Phase 3 — `innerHTML` as structure

9. Run the page's markup builder with sentinels, parse the result to locate each hole, and map the
   holes with the existing slot machinery. A variable-length list becomes a value (emit at the
   maximum, drive each row's opacity) rather than a separate scene variant.

   **Measured, and the first reading of it was wrong.** A full `innerHTML` parse costs 2.26 MB on the
   Atmo page, but that happens ONCE - the count stays at exactly 1 over 300 frames and over 900, so
   it is the cold first call. Steady state is an in-place morph at ~47 KB. The honest per-console
   arithmetic on .NET is ~118 KB/s of morph plus ~176 KB/s of per-frame interpreter, so ~4.4 MB/s
   across fifteen consoles - against 41.7 measured in game. Even with Mono's 2-3x that leaves most
   of it somewhere the bench cannot see.

   So the reason to compile these pages is NOT the parse cost. It is that a compiled page stops
   dirtying its scene every frame, which takes the renderer from rebuilding at 60 Hz to rebuilding
   at the page's own 2.4 Hz. That is ~25x on the term that actually dominates, and it is the same
   conclusion phase 5 reaches from the other direction.

### Phase 4 — CSS

10. Modern selectors: escaped names, `:has`/`:is`/`:where`, attribute selectors, nesting,
    `::before`/`::after`.
11. Multi-layer backgrounds with position, size and repeat.
12. The three parser faults above.

### Phase 5 — zero per frame

13. **Event-anchored motion.** The closed-form pass built today handles motion that is
    unconditionally a function of time, and refuses anything behind a guard — which on a real
    interactive page is almost everything. The general answer is to anchor expressions on epochs the
    chip writes when state changes: a run's `$t0`/`$d0`/`$tfreeze`, a jump's `$jt0`/`$jv0`. The
    runner then sends 3–4 numbers a second instead of ~296.

    Note the property that makes this all-or-nothing: converting nineteen writes of twenty saves
    nothing, because the twentieth still dirties the payload every frame and the renderer still
    rebuilds. The target per page is zero writes, not fewer.

14. **The 73 KB rebuild.** A rebuild in game allocates ~73 KB where the same scene tessellates to
    ~5.5 KB offline. That gap sets the floor for hand-written consoles too, and it has never been
    explained. It is the other factor in `MB/s ≈ rebuilds/s × KB/rebuild` and no amount of work on
    the first factor removes it.

### Phase 6 — canvas 2D

15. A real 2D context drawing through the vector layer. Four pages here, but it is the feature a
    person reaches for the moment they want something the DOM cannot draw, so "any page" is not
    true without it.

---

## Definition of done

- `--corpus` reports no blockers on any page that is valid HTML, CSS and JavaScript.
- `--css` reports nothing unsupported.
- In game, 15 consoles running the real pages sit inside 2.2–2.6 MB/s, measured by paired A/B.

## The honest boundary

"Any page" cannot include everything a browser does, and pretending otherwise would mean pages that
compile and then behave wrongly. Out of scope, to be **reported to the author** rather than
approximated:

- Network: `fetch`, `XMLHttpRequest`, WebSocket. A console has no network of its own.
- `eval` and `new Function`. There is no JavaScript engine at run time by design.
- Workers, `IntersectionObserver`, `MutationObserver`, the History and Storage APIs.
- Anything needing a real layout read *during* a frame, since the page is laid out once.
- React and the design tool's `DCLogic` runtime: three pages pull in React and about fifteen extend
  a class from an external `support.js` the mod never loads. Supporting `class` moves their failure
  from "class is not translatable" to "React is not defined", which is the correct and honest
  failure, not a regression.

## How this gets worked

Phase at a time, scored by the sweeps, reported at phase boundaries with the numbers — not per item
and not per page. No page is patched to make it compile; if a page fails, the language feature it
needs gets built.

This is a large body of work. Phase 1 alone is the biggest single piece of the compiler so far.
