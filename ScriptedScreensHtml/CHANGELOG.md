# ScriptedScreens Html changelog

Newest first. The workshop page carries only the latest releases; this file has all of them.

## 0.2.0

- A page's translation to the vector scene runs on a worker thread; the game thread lays the page
  out, copies the values the translation reads and hands the result to the vector mod.
- Pages are laid out with the faces the Fonts mod and the game registered, mirrored by name; the
  mod reads no font files and knows no font folders (a Workshop install names mod folders by item
  id, and player fonts live where the Fonts mod keeps them). A face registered later is picked up
  within seconds.

- Documentation and examples are published to the StationeersLua MCP: search scope `html`,
  `stationeers://html/index` as the quick start, the guide and SUPPORT.md one resource per
  section, the changelog, and every chip under `examples/`.
- `examples/`: six chips that run on paste, one idea each; `mockups/`: the three Atmo Regulator
  consoles written as pages, served as `stationeers://html-mockups/` in a search scope of their own.
- Text nodes directly inside a flex or grid container are items of their own (a lamp span
  followed by a word was dropped).
- The `font` shorthand's longhands reach the cascade record (family, weight, line height), so a
  label draws in the face the page names (Barlow Condensed Bold, not the body's Barlow).
- The layout measures with the real weight face the scene draws, not a synthetic bold.
- Glyphs a face lacks (subscript digits, the gear) are drawn from the game's own face, as a
  browser falls back to a system font; generic families map to a face inside rich text.
- Layered `background` values: the colour layer under gradient layers, each at its own position
  and size (fill edges, corner marks).
- `text-overflow: ellipsis` only where declared; other clipped labels are clipped by their box.
- `font-variant-numeric: tabular-nums` widens the label's box by what the digit cells add; the
  cell is the face's widest digit.
- Flex `gap` survives innerHTML and re-cascades; the baseline pass owns margin-bottom in a row.
- A declared width or height under a pixel lays out as one pixel.
- An undefined `var()` with no fallback drops its declaration, as in a browser.
- `display: inline-block` from a stylesheet makes a box, not only from the inline style.
- innerHTML assigns ids and fills the attribute cache on the worker: a render's handler loop no
  longer waits a game frame per `getAttribute`; a worker read never starves.
- Keyframe runners of removed elements are pruned; a mid-frame drain for a worker read does not
  emit; the emit waits for a settled layout.
- Diagnostics: each dirty cause and the runner count on the per-page line; the config file is
  re-read when it changes on disk.

## 0.1.0

- First release: HTML, CSS and JavaScript pages on ScriptedScreens consoles, drawn through
  ScriptedScreens Vector.
