# What a script can and cannot change on a compiled page

**15 of 47 common `el.style.*` writes reach a scene slot.** That is the lowest number in the whole
system and it is now a real one — the probe used to assert its own answer.

```bash
cd ScriptedScreensHtml.Tests && dotnet run -c Release -- --domlanguage   # section 5b
```

## How the number was wrong before

`DomLanguage.StyleProperties` carried a **hand-written list of eleven slot names** and asked
`DomSlots.Map` against it. It had no `e_s`, no `e_sw` and no `e_fo`, and it declared the element to
have no background. So the probe reported `borderColor`, `borderWidth` and `visibility` as writes the
compiler refuses — on the strength of its own list. Sixth instance of that shape in one session.

It now emits a real, fully-styled, script-driven element through `ScriptedScreensHtml.Bench --slots`
and reads the slot names out of the scene. Those names, as of 2026-09-23:

```
e  e_x  e_y  e_w  e_h  e_f  e_rx  e_size  e_r  e_a_0  e_a_1  e_t_0  e_t_1  e_s_0  e_s_1
e__2_x  e__2_y  e__2_w  e__2_h  e__2_f
e__b_x  e__b_y  e__b_w  e__b_h  e__b_rx  e__b_s  e__b_sw
L2_o
```

Three of the four things that followed are fixed; the fourth (`L2_o`) is the one still worth reading.

## 1. The border IS in the scene, under a name nothing can reach — FIXED 2026-09-22

The border is emitted as a **second `R` beside the element's own**, and it took a synthetic
positional name (`L4`) rather than one derived from the element. `DomSlots` composes
`id + "_" + key`, so it looked for `e_s` and `e_sw`, which did not exist under that naming.

**It was a naming convention, not a feature.** The border shape now takes `<name>__b` when its
element is script-driven, and `DomSlots` knows `s`/`sw` live on that companion. The companion is not
optional: `e_s_0`/`e_s_1` is the transform SCALE, so `s` at the element's own name would have
collided with it silently.

Only the two single-uniform-stroke paths carry it. An element drawing a different colour per side, or
`inset`/`outset`/`double`, has no one stroke for a single write to land on, and is refused by name.
An element with no border at all is refused too.

**11/47 → 13/47.** Pinned by three tests in `CompiledPageTests`, each reverted and watched to fail.
Still to be seen on a console.

## 2. A box's fill and its label's fill — FIXED 2026-09-23

An element with a background is two lines carrying one id: the `R` and then the `T`. `SceneSlots`
named slots `<id>_<key>` with first come wins, so the label's `x y w h f` fell back to a
**positional** name (`L5_f`) nothing could address, and `color` was refused on every element that
paints a background — most of them.

The second line carrying an id is now `<id>__2_<key>` (`SceneSlots.SecondSuffix`), stable and
id-derived; only a third falls back to the positional name. The box keeps the bare name, because
`background` and every geometry write land there, and `DomSlots` sends `color` to `<id>__2_f` when
the element has a background. `textContent` (`<id>`) and `fontSize` (`<id>_size`) never collided and
are unchanged. An author's own id shaped like the generated name (`e__2`) is not a slot name any
more, so it cannot claim the label's slots; `e__b` still is, since no digit follows.

**13/47 → 14/47.** One test, reverted and watched to fail (`e__2_f` came back `(none)` and `color`
was refused). Still to be seen on a console — the label of every scripted element with a background
changes slot name, so a console still drawing its labels is the thing to look at.

## 3. `visibility`, with its value arm — WIRED 2026-09-23, and it lands on a slot the scene does not have

`visibility` now takes the same `GroupKeys` + `Result.Group` path as `opacity`, and `DOM.bind` has a
`visibility` arm mapping `hidden`/`collapse` to 0 and anything else to 1. Without the arm the write
fell through to `length('hidden')`, which is nil, and silently did nothing — the missing-`state`-arm
bug again. One test drives a compiled page through `setInterval` and reads `e_o` back as 0, then 1;
reverted, the arm's absence fails it exactly that way.

**14/47 → 15/47.** But read the slot list: the opacity group is **`L2_o`**, positional. The emitter
writes `G o=…` with no id (`VectorEmitter.cs:491-494`), even for an element a script drives — only
the TRANSFORM group gets `id=` (`VectorEmitter.cs:452`). So `opacity` and `visibility` both bind to
`<id>_o`, a slot that does not exist, and the probe counts them as reaching the scene because
`Result.Group` is a request the emitter never honours. **That is the project's characteristic bug,
and `opacity` has had it all along.** Two one-line patches outside this pass's file ownership:

```csharp
// VectorEmitter.cs:493 — name the fade group like the transform group
ctx.Body.Append(indent).Append("G o=").Append(tw != null ? tw.Lerp(tw.From.Opacity, rs.opacity) : F(rs.opacity));
if (Driven(ctx, ve)) ctx.Body.Append(" id=").Append(ve.name);
ctx.Body.Append(" {\n");

// HtmlRenderer.cs, NameDrivenGroups — a visibility write must keep the group named and emitted
if (!w.Runtime || w.Property is not ("style.transform" or "style.opacity" or "style.visibility" or "className")) continue;
```

Two nodes then share an id (the fade `G` and the transform `G`), as the `R` and `T` already do.
Note `visibility` and `opacity` share the slot, so a page writing both gets whichever was last.

## 4. `right` / `bottom` — MAPPED 2026-09-23, refused until the caller measures

A far-edge position is `x = parent + parentSize - own - right`: the value is **subtracted**, and the
binding mechanism only added. `DomSlots.Result` and `CompiledPage.Binding` now carry a per-slot
`Scale` (−1 for a far edge, 1 otherwise), emitted as a third entry in `to = { slot, bias, -1 }` only
where it is not 1, and applied by the chunk (`n * (to[3] or 1) + to[2]`). A motion expression on a
far edge becomes `bias-(expr)`. Descendants ride along with the same sign, as they do for `left`.
One test compiles `e.style.right = '10px'` against a 300-wide parent at x=100 and a 50-wide element
and reads `e_x` back as 340; reverted (chunk ignoring the scale), it reads 360.

`DomSlots.Box` gained `ParentW ParentH W H`, NaN by default, and `Map` **refuses** `right`/`bottom`
while they are NaN rather than mapping to the near edge — which is why the probe still lists both as
refused: its `Box` (`DomLanguage.cs:546`) passes no sizes, and so does production. Three patches
outside this pass's ownership, to be applied **together** — the third is what keeps the first from
introducing a silent wrong edge on the data path:

```csharp
// PageCompiler.cs BoxOf — remember the containing block the origin loop stops at, and pass the sizes
VisualElement? block = null;                                   // beside `var origin = Vector2.zero;`
    if (absolute.TryGetValue(p, out var at)) origin = at;      // existing line inside the loop
    block = p;                                                 // new, before the existing `break;`
return new DomSlots.Box(outOfFlow, origin.x, origin.y, hasBackground, inside,   // line 275
                        block != null ? block.layout.width : double.NaN, block != null ? block.layout.height : double.NaN,
                        ve.layout.width, ve.layout.height);

// DomLanguage.cs:546 — the probe element is 90x40 in a 400-wide viewport
var box = new DomSlots.Box(outOfFlow: true, parentX: 0, parentY: 0, hasBackground: true, null, 400, 400, 96, 46);

// DataSlots.cs:114/125/168/181/183 — Target must carry and apply mapped.Scale[i], not only Bias
```

A ceiling, marked in the code: the element's own size is the one it was compiled with, so a page
that also writes `width`/`height` at run time moves the far edge and the two are not composed.

## 5. The rest are genuinely not slots, and that is the architecture

The thirty remaining refusals are properties whose change requires **re-running layout**: `display`,
`position`, `flex`, `flexDirection`, `gap`, `margin`, `padding`, `maxWidth`, `minHeight`, `overflow`,
`gridTemplateColumns`, `justifyContent`, `alignItems`, `textAlign`, `lineHeight`, `letterSpacing`,
`fontFamily`, `fontWeight`, `zIndex`. A compiled page has no layout engine — that is the premise of
compiling it — so these cannot be slots at all. A page that needs them cannot be compiled, and
should be told so rather than compiled into something that ignores them.

---

## In order of what it buys

1. ~~Name the border shape after its element~~ — done.
2. ~~Distinguish a box's fill from its label's~~ — done, as `<id>__2_<key>`.
3. ~~`visibility`, with its value arm~~ — wired; **the emitter must name the fade group** (item 3
   above) or neither it nor `opacity` reaches the scene.
4. ~~`right` / `bottom`~~ — mapped; **three callers must pass the sizes** (item 4 above).
5. Nothing in the suite has seen items 2–4 on a console.

Everything after that needs layout, and wanting layout at run time means the page should not have
been compiled.
