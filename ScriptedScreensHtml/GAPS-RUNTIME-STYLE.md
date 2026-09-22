# What a script can and cannot change on a compiled page

**11 of 47 common `el.style.*` writes reach a scene slot.** That is the lowest number in the whole
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
and reads the slot names out of the scene. Those names are:

```
e  e_x  e_y  e_w  e_h  e_f  e_rx  e_size  e_r  e_a_0  e_a_1  e_t_0  e_t_1  e_s_0  e_s_1
L2_o
L4_x  L4_y  L4_w  L4_h  L4_rx  L4_s  L4_sw
L5_x  L5_y  L5_w  L5_h  L5_f
```

Three things follow, and none of them were visible from the old list.

## 1. The border IS in the scene, under a name nothing can reach — FIXED 2026-09-22

`L4_s` and `L4_sw` are the border's colour and width. The border is emitted as a **second `R` beside
the element's own**, and it takes a synthetic positional name (`L4`) rather than one derived from
the element. `DomSlots` composes `id + "_" + key`, so it looks for `e_s` and `e_sw`, which do not
exist and never will under that naming.

So `el.style.borderColor = '#f00'` — an alarm state on a panel, the single most ordinary runtime
style write a console page makes — is refused, on a renderer that emits exactly the slot it needs.

**It was a naming convention, not a feature.** The border shape now takes `<name>__b` when its
element is script-driven, and `DomSlots` knows `s`/`sw` live on that companion — the same shape
`GroupKeys` already uses for `opacity` and `transform` on the wrapping group. The companion is not
optional: `e_s_0`/`e_s_1` is the transform SCALE, so `s` at the element's own name would have
collided with it silently.

Only the two single-uniform-stroke paths carry it. An element drawing a different colour per side, or
`inset`/`outset`/`double`, has no one stroke for a single write to land on, and is refused by name.
An element with no border at all is refused too — a script cannot add one to a compiled page, since
there is no shape to write into.

**`style writes that reach the scene` 11/47 → 13/47.** Pinned by three tests in `CompiledPageTests`,
each reverted and watched to fail. Not yet seen on a console.

## 2. `visibility` needs a value translation, not just a slot

`e_fo` does not exist, but `L2_o` does — the opacity group. `opacity` already reaches it, through
`GroupKeys` plus `Result.Group`, which is a REQUEST to the emitter to name the group rather than a
refusal. `visibility` could take the same path.

**It must not be wired without a translation.** `DOM.bind` falls through to `length(value)`, and
`"hidden"` is not a length, so it would return nil and the write would silently do nothing — which
is the exact bug fixed on 2026-09-22 when the `state` arm was missing and every compiled page drew
its base state for ever. A `visibility` arm mapping hidden/visible to 0/1 has to land in the same
commit as the slot.

A set for this was written and then deleted rather than left unwired, because a half-connected
mechanism reads as a working one.

## 3. The rest are genuinely not slots, and that is the architecture

Thirty-three of the thirty-six refusals are properties whose change requires **re-running layout**:
`display`, `position`, `flex`, `flexDirection`, `gap`, `margin`, `padding`, `maxWidth`, `minHeight`,
`overflow`, `gridTemplateColumns`, `justifyContent`, `alignItems`, `textAlign`, `lineHeight`,
`letterSpacing`, `fontFamily`, `fontWeight`, `zIndex`. A compiled page has no layout engine — that is
the premise of compiling it — so these cannot be slots at all. A page that needs them cannot be
compiled, and should be told so rather than compiled into something that ignores them.

Two more are the far-edge positions (`right`, `bottom`), which the compiler *could* resolve since it
knows the parent's size at compile time; they are refused rather than mapped to the wrong edge.

And `color` is refused only when the element paints a background, because the box and its label
share an id and the first `f` is the box's fill. That is a real ambiguity in the emitter's naming,
of the same family as item 1 and fixable the same way.

---

## In order of what it buys

1. ~~**Name the border shape after its element**~~ — done, see above.
2. **Distinguish a box's fill from its label's** — unblocks `color` on any element with a
   background, which is most of them. Same family, and the design is settled: the LABEL takes the
   companion (`<name>__t`) when the box also paints, because the box must keep the bare name — `x`,
   `y`, `w`, `h` are on both lines, and moving them to the label's measured box would silently
   redirect every geometry write.

   **Deliberately not built on 2026-09-22.** It moves the id of the most-written line on every
   compiled page that has a background and a script, `textContent` and `fontSize` have to learn the
   same fallback in the same commit, and no test in the suite covers text slot naming. It is
   verifiable offline only as far as the slot list; whether a console still draws its labels needs a
   game start, and this build has not had one. Worth doing first thing in a session that can test.
3. **`visibility`, with its value arm** — one more slot, and the arm is required.
4. **`right` / `bottom`** — the arithmetic is available at compile time.

Everything after that needs layout, and wanting layout at run time means the page should not have
been compiled.
