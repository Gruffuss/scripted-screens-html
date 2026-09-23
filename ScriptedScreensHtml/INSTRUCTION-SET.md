# The HTML, CSS and JavaScript instruction set, and what the compiler does with each

This file is the compiler's scope and its progress. One table per language, and the rows are the
**whole** instruction set from the standards, not just what our pages happen to use. Work on the
compiler is assigned, done and reported by rows of this file, never by pages. Pages are only
integration checks.

## Rules for everyone who edits this file (sessions and agents)

- **Status** is one of three things:
  - ✅ **translated**: the feature compiles the way `CLAUDE.md` ("THE SPEC") says, into a vector scene
    plus plain Lua that a person could have written by hand for the vector mod, and nothing of this
    mod runs after the compile. A ✅ needs a passing test, named in the Test column.
  - ❌ **not translated**: only with a **strongly good reason** in the Reason column, one a page author
    would accept (e.g. "needs the network: a console has none"). "Not done yet", "hard" or "no page
    uses it" are not reasons.
  - blank: **not done yet**. This is the default; leave a row blank rather than guess.
- **Maps to** says what the feature becomes: the vector scene node or attribute (`R`, `T`, `G`,
  `IMG`, `f=`, `ease`, `t` expressions, text placeholders...) or the Lua construct (the chip's
  `tick`, `on_click`, a slot write `D.x = ...` sent with `VDATA:set_props`...). Use the vector mod's
  native forms (`../ScriptedScreensVector/REFERENCE.md`), not the workarounds of the old hand-written
  pages.
- The old path does **not** count: DOM emulation on the chip (`JsPrelude.lua`'s DOM), the per-frame
  chunk driven from C#, the Jint interpreter. A feature that works only there is blank.
- Change a row in the same commit as the code and test that justify it. Never mark a row you did
  not verify.
- One agent takes one section or one group of rows. Say which rows in the brief and in the report.

## Columns

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|

---

## Tables

The instruction set is split one file per language. Each of the four starts with "Rules and
columns: see INSTRUCTION-SET.md." and ends with its own Sources section (what was fetched, when,
and where rows were grouped rather than enumerated).

- [`INSTRUCTION-SET-HTML.md`](INSTRUCTION-SET-HTML.md) — table 1: elements (incl. the SVG and
  MathML elements HTML embeds, obsolete ones marked), global attributes, element-specific
  attributes grouped by element, event handler attributes.
- [`INSTRUCTION-SET-CSS.md`](INSTRUCTION-SET-CSS.md) — table 2: properties (grouped by module,
  spec status marked), selectors (combinators, attribute selectors, pseudo-classes,
  pseudo-elements), at-rules and their descriptors, value functions and colour/keyword groups,
  units.
- [`INSTRUCTION-SET-JS.md`](INSTRUCTION-SET-JS.md) — table 3: statements and declarations,
  expressions and operators, every standard built-in object one row per member (ECMA-262), the
  Intl objects (ECMA-402) as a group of rows.
- [`INSTRUCTION-SET-DOM.md`](INSTRUCTION-SET-DOM.md) — table 4: DOM and Web APIs a page script
  can call — Window/globals, Document/Node/Element/HTMLElement and the per-element HTML
  interfaces, events, Canvas, SVG DOM, Web Animations, CSSOM View, observers, URL/Blob/text,
  and the rest of the Web API surface grouped by interface.

