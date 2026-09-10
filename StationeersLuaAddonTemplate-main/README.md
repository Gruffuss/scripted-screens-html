# ScriptedScreens Fonts

A client-side **[Stationeers](https://store.steampowered.com/app/544550/Stationeers/)** mod
that makes fonts usable by name in TextMeshPro rich text, including inside
**[ScriptedScreens](https://steamcommunity.com/sharedfiles/filedetails/?id=3666779631)**
labels:

```lua
props = { text = '<font="Barlow Condensed">TANK 3  84%' }
```

It does two independent things:

1. **Registers the game's own font assets**, which TMP otherwise cannot find by name.
2. **Builds font assets from `.ttf`/`.otf` files you drop in a folder**, so you can use a
   font the game does not ship.

No game files are modified and ScriptedScreens is not patched. There is no Lua API — the mod
mutates global TMP state at load, so the only thing that changes is that `<font="X">`
resolves names it previously rejected.

## Using a font

Put the tag inside a label's `text`. It applies from where it appears to the end of the
string, or until `</font>`:

```lua
ui:element({
    id = "readout",
    type = "label",
    rect = { unit = "px", x = 8, y = 8, w = 300, h = 24 },
    props = { text = 'default <font="Barlow Bold">bold Barlow</font> default again' },
    style = { font_size = 16, color = "#22C55E", align = "left" },
})
```

ScriptedScreens enables rich text on every label unless the string contains `<noparse>`, so
nothing needs switching on. The other TMP tags (`<b>`, `<i>`, `<size>`, `<color>`,
`<cspace>`, `<font-weight>`) work alongside it.

**Names are case sensitive.** Take them from `BepInEx/LogOutput.log`, which lists every one
on load:

```
Font available: <font="Barlow Condensed SemiBold"> (212 characters from BarlowCondensed-SemiBold.ttf)
```

Reading a wrong result:

| what you see | what happened |
|---|---|
| the right typeface | the tag resolved |
| the default typeface | TMP silently fell back |
| a literal `<font="X">` | TMP rejected the tag; the name is wrong |

`FontTest.lua` renders every registered name as its own row, which makes all three visible at
a glance.

## Adding your own fonts

Drop `.ttf` or `.otf` files anywhere under:

```
mods/ScriptedScreensFonts/Assets/fonts/
```

Subfolders are scanned, so organising by project is fine. In the repo the same folder is
`Assets/fonts/`, and the build copies it into the mod.

**The name comes from the font's own metadata** — family, plus style when that is not
Regular:

| file | tag |
|---|---|
| `Barlow-Regular.ttf` | `<font="Barlow">` |
| `Barlow-Bold.ttf` | `<font="Barlow Bold">` |
| `BarlowCondensed-SemiBoldItalic.ttf` | `<font="Barlow Condensed SemiBold Italic">` |

**A restart is required.** Atlases are built once, shortly after the game's TMP resources come
up. There is no rescan, so adding a file mid-session does nothing.

**Ship the licence.** Most Google Fonts are SIL OFL, which permits bundling provided the
copyright notice and licence travel with the font and it is not sold separately — but check
per family rather than assuming. The font file itself is authoritative: its `name` table
carries the licence in nameID 13. Barlow's `OFL.txt` sits next to the Barlow files here.

## Character set

Each font is rendered once into a fixed set:

- printable ASCII
- the Latin-1 supplement (accented European text, `° ± µ ² ³ ¼ ×`)
- 81 baked extras — dashes and curly quotes, maths (`− ≈ ≠ ≤ ≥ ∞ √ ∑ ∏ Δ ∇ π Ω`), arrows,
  geometric shapes, `✓ ✗ ⚠`, block bars for text sparklines, and box drawing

Anything else goes in **`ExtraCharacters`** in
`BepInEx/config/gruffuss.stationeers.scriptedscreens.fonts.cfg`, or LaunchPad's settings UI.

**A glyph the font does not contain is skipped.** No substitution is attempted — that would
mean `<font="Barlow">` silently rendering some other typeface. Barlow, for example, is a text
face: it has the punctuation and the maths but no arrows, shapes or box drawing at all. If
you need an arrow from a font that lacks one, switch face for that character
(`<font="noto-punc">→</font>`) or draw it in the vector layer.

## Limits

- **One 1024×1024 atlas per face, about 1 MB.** 36 faces is ~36 MB of texture memory. Prune
  to the weights you actually use before loading several families.
- **The atlas is static.** It cannot grow at runtime and cannot spill into a second texture.
  If a font's coverage overflows it, the load logs `did not fit` and the remainder is absent;
  the fix is a smaller sampling size.
- **Client-side only.** Skipped entirely in batch mode, since a headless server renders
  nothing.
- Fonts arriving from asset bundles register as they load, so the game's own list fills in
  over the first minutes of a session rather than all at once.

## Requirements

- **BepInEx 5.x** and
  **[StationeersLaunchPad](https://github.com/StationeersLaunchPad/StationeersLaunchPad)**
- **ScriptedScreens** if you want to use the fonts in consoles — the mod is useful without
  it, but that is the point of it

## Building

Create `Stationeers.VS.User.props` next to the `.csproj` (gitignored):

```xml
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <!-- The folder CONTAINING the Stationeers game directory. -->
    <SteamLibraryDirectory>D:\SteamLibrary\steamapps\common</SteamLibraryDirectory>
    <!-- Only if Documents is redirected (OneDrive). Must be the folder that
         actually holds modconfig.xml. -->
    <StationeersDocumentsDirectory>C:\Users\$(username)\OneDrive\Documents\My Games\Stationeers</StationeersDocumentsDirectory>
  </PropertyGroup>
</Project>
```

Every deploy `Copy` is `ContinueOnError`, so a wrong `StationeersDocumentsDirectory` fails
silently and presents as the mod not working. If a build succeeds but nothing changes in
game, check that path first.

`dotnet build ScriptedScreensFonts.csproj` deploys to
`My Games\Stationeers\mods\ScriptedScreensFonts\`.

The build publicises `Unity.TextMeshPro` and `UnityEngine.TextCoreFontEngineModule` via
`BepInEx.AssemblyPublicizer.MSBuild`; building a font asset from a file needs several
internal TextCore members. This affects compilation only — the shipped assemblies are
untouched.
