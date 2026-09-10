# ScriptedScreens HTML

A client-side Stationeers mod that adds an **`html` element type** to
[ScriptedScreens](https://steamcommunity.com/sharedfiles/filedetails/?id=3666779631) surfaces:
a console screen written as a web page.

HTML and CSS give the structure and the look, Unity's UI Toolkit lays the boxes out, and the
page is translated to the [ScriptedScreens Vector](https://github.com/Gruffuss/scripted-screens-vector)
mod's scene text and drawn by it as geometry: crisp at any distance, in the fonts of the
Fonts mod, animated by expressions with no script and no Lua running per frame.

```lua
ui:element({ id = "page", type = "html", rect = { unit = "px", x = 0, y = 0, w = 460, h = 460 },
             props = { src = [[<html><head><style>
  body { background: #0B1622; color: #E4F1F7; font-family: 'Barlow'; padding: 16px; }
  .bar { height: 12px; background: #1E293B; border-radius: 6px; overflow: hidden; }
  .fill { height: 100%; width: 0%; background: #38BDF8; transition: width 0.45s ease-out; }
</style></head><body>
  <h1>CO<sub>2</sub> <span id="co2">0</span> ppm</h1>
  <div class="bar"><div class="fill" id="fill"></div></div>
</body></html>]] } })

-- later, per tick: text by id, CSS by id; the transition does the motion
data:set_props({ data = { co2 = "412", fill = { width = "41%" } } })
```

Inline `<svg>` maps one-to-one to vector nodes, and any attribute may be an expression:
`<polygon n="36" x="=i*1.4" y="=100-100*$fill+3*sin(0.1*i+t)" y2="144" fill="#38BDF8"/>`
is a rippling gas surface bound to live data, drawn every frame by the client.

- `ScriptedScreensHtml/` — the mod. `SUPPORT.md` there lists what works from HTML5, CSS and
  JavaScript, what does not, and why.
- `ScriptedScreensHtml.Tests/` — headless parser tests (`dotnet run`).
- `StationeersLuaAddonTemplate-main/` — the Fonts mod (font files → TextMeshPro faces), which
  both the HTML and the vector mod use. Folder name is the unzipped template's.
- `CLAUDE.md` — the working brief: every decision, measurement and dead end, in order.
- `gas-tank-console-ui-final.html` — the reference mockup the gas console port is measured against.

## Build

Requires the vector mod checked out beside this repository (`../ScriptedScreensVector`) and a
`Stationeers.VS.User.props` next to `Stationeers.VS.props` in the fonts mod folder with
`SteamLibraryDirectory` pointing at `steamapps\common`. Then `dotnet build` in
`ScriptedScreensHtml/`; the build deploys to the game's `mods` folder.
