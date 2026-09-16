"""Re-embed each mockup page into its Lua wrapper: Atmo*.html goes between `local page = [==[`
and `]==]` in Atmo*.lua; the wrapper's header and code stay as they are. Run after editing a page:

    python embed_pages.py

Checks: the page contains no `]==]`, and the TAB substitution in the wrapper finds its target once.
"""
from pathlib import Path

HERE = Path(__file__).parent
OPEN, CLOSE = "local page = [==[\n", "\n]==]\n"
TAB_TARGET = "|| '#atmo').slice(1) || 'atmo'"

for name in ("AtmoApple", "AtmoDark", "AtmoLight"):
    html = (HERE / f"{name}.html").read_text(encoding="utf-8")
    lua_path = HERE / f"{name}.lua"
    lua = lua_path.read_text(encoding="utf-8")
    i, j = lua.index(OPEN), lua.index(CLOSE)
    assert "]==]" not in html, f"{name}.html contains ]==], which would end the Lua string"
    assert html.count(TAB_TARGET) == 1, f"{name}.html: the start-tab default must appear exactly once"
    new = lua[: i + len(OPEN)] + html + lua[j:]
    if new != lua:
        lua_path.write_text(new, encoding="utf-8", newline="\n")
    print(f"{name}.lua: {'updated' if new != lua else 'unchanged'} ({len(html)} page chars)")
