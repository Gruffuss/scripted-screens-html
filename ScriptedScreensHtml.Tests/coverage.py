# coverage.py -- what the HTML/CSS/JS layer handles, measured against reference lists.
# Reads the code (not the docs) for the handled sets, and compares them with MDN's CSS data
# (mdn/*.json), the HTML living standard's element list and a list of the DOM and Web APIs a
# page script commonly uses. Writes COVERAGE.md next to the mod and prints a summary.
#
#   python coverage.py
import json, os, re

HERE = os.path.dirname(os.path.abspath(__file__))
MOD = os.path.join(HERE, "..", "ScriptedScreensHtml")

def read(name):
    return open(os.path.join(MOD, name), encoding="utf-8").read()

def mdn(name):
    p = os.path.join(HERE, "mdn", name + ".json")
    return json.load(open(p, encoding="utf-8")) if os.path.exists(p) else {}

style = read("StyleApplier.cs"); parser = read("CssParser.cs"); renderer = read("HtmlRenderer.cs")
emitter = read("VectorEmitter.cs"); surface = read("HtmlSurface.cs"); host = read("ScriptHost.cs")
grid = read("GridLayout.cs"); tweens = read("Tweens.cs"); anim = read("Animation.cs")
code = style + parser + renderer + emitter + surface + grid + tweens + anim

# ---------------------------------------------------------------- CSS properties
css_props = set(re.findall(r'case "([a-z-]+)":', style))
elsewhere = style[style.index("HashSet<string> Elsewhere"):]
elsewhere = elsewhere[:elsewhere.index("};")]
css_props |= set(re.findall(r'"([a-z-]+)"', elsewhere))
for src in (renderer, emitter, surface, grid, tweens, anim):
    css_props |= set(re.findall(r'TryGetValue\("([a-z-]+)"', src))
    css_props |= set(re.findall(r'ContainsKey\("([a-z-]+)"', src))
    css_props |= set(re.findall(r'd\.Name == "([a-z-]+)"', src))
    css_props |= set(re.findall(r'Name is "([a-z-]+)"', src))
# logical properties expand to physical ones: everything Expand() maps
logical = set(re.findall(r'case "([a-z-]+)": a = ', style)) | {
    "margin-inline", "margin-block", "margin-inline-start", "margin-inline-end", "margin-block-start", "margin-block-end",
    "padding-inline", "padding-block", "padding-inline-start", "padding-inline-end", "padding-block-start", "padding-block-end",
    "inset-inline", "inset-block", "inset-inline-start", "inset-inline-end", "inset-block-start", "inset-block-end",
    "border-inline", "border-block", "border-inline-start", "border-inline-end", "border-block-start", "border-block-end",
    "border-inline-width", "border-inline-color", "border-inline-style", "border-block-width", "border-block-color", "border-block-style",
    "border-inline-start-width", "border-inline-start-color", "border-inline-start-style", "border-inline-end-width", "border-inline-end-color", "border-inline-end-style",
    "border-block-start-width", "border-block-start-color", "border-block-start-style", "border-block-end-width", "border-block-end-color", "border-block-end-style",
    "scroll-margin-inline", "scroll-margin-block", "scroll-margin-inline-start", "scroll-margin-inline-end", "scroll-margin-block-start", "scroll-margin-block-end",
    "scroll-padding-inline", "scroll-padding-block", "scroll-padding-inline-start", "scroll-padding-inline-end", "scroll-padding-block-start", "scroll-padding-block-end",
}
css_props |= logical
# handled by prefix in the renderer (ApplyAnimationDeclaration) and the tween compiler
css_props |= {"animation", "animation-name", "animation-duration", "animation-delay", "animation-iteration-count", "animation-direction", "animation-fill-mode", "animation-play-state", "animation-timing-function", "animation-composition",
              "transition", "transition-property", "transition-duration", "transition-delay", "transition-timing-function"}
mdn_props = {k: v for k, v in mdn("properties").items() if v.get("status") == "standard" and not k.startswith("-")}

# ---------------------------------------------------------------- at-rules, selectors, functions, units
at_rules = set(re.findall(r'header\.StartsWith\("([a-z-]+)', parser)) | set(re.findall(r'stmt\.StartsWith\("([a-z-]+)', parser))
at_rules |= {"charset", "layer"}  # statement forms are skipped on purpose (nothing to do)
mdn_at = {k[1:] for k, v in mdn("at-rules").items() if v.get("status") == "standard"}

pseudo_block = parser[parser.index('case "root":'):parser.index("private static int TypeIndex")]
pseudos = set(re.findall(r'case "([a-z-]+)"', pseudo_block))
pe_match = re.search(r'name is not \(([^)]*)\)', parser)
pseudo_elements = set(re.findall(r'"([a-z-]+)"', pe_match.group(1))) if pe_match else set()
mdn_sel = {k for k, v in mdn("selectors").items() if v.get("status") == "standard"}

fn_names = set()
for m in re.findall(r'"([a-z-]+)\(', code): fn_names.add(m)
for m in re.findall(r'case "([a-z0-9-]+)":', emitter + style): fn_names.add(m)
for m in re.findall(r'StartsWith\("([a-z-]+)\(', code): fn_names.add(m)
for m in re.findall(r'"([a-z-]+)"', code): fn_names.add(m)  # keyword spellings (min, max, clamp as words)
mdn_fn = {k.rstrip("()") for k, v in mdn("functions").items() if v.get("status") == "standard"}
fn_lower = {f.lower() for f in fn_names} | {"repeating-linear-gradient", "repeating-radial-gradient", "repeating-conic-gradient"}  # "repeating-" is a prefix check

units = set(re.findall(r'"(px|em|rem|%|vw|vh|vmin|vmax|ch|ex|cm|mm|in|pt|pc|q|deg|rad|turn|grad|s|ms|fr|dvh|svh|lvh|dvw|svw|lvw|cap|ic|lh|rlh|vb|vi|x|dpi|dpcm|dppx|hz|khz)"', style + parser + renderer + grid + emitter))
mdn_units = {k for k, v in mdn("units").items() if v.get("status") == "standard"}

# ---------------------------------------------------------------- HTML elements
html_all = set("""a abbr address area article aside audio b base bdi bdo blockquote body br button canvas caption cite code col colgroup
data datalist dd del details dfn dialog div dl dt em embed fieldset figcaption figure footer form h1 h2 h3 h4 h5 h6 head header hgroup hr html
i iframe img input ins kbd label legend li link main map mark menu meta meter nav noscript object ol optgroup option output p picture pre
progress q rp rt ruby s samp script search section select slot small source span strong style sub summary sup table tbody td template
textarea tfoot th thead time title tr track u ul var video wbr""".split())
inline_block = renderer[renderer.index("HashSet<string> Inline"):renderer.index("HashSet<string> Skipped")]
inline = set(re.findall(r'"([a-z0-9]+)"', inline_block))
skipped_block = renderer[renderer.index("HashSet<string> Skipped"):]
skipped_block = skipped_block[:skipped_block.index("};")]
skipped = set(re.findall(r'"([a-z0-9]+)"', skipped_block))
handled_tags = set(re.findall(r'\.Tag (?:==|is) "([a-z0-9]+)"', renderer + surface)) | set(re.findall(r'case "([a-z0-9]+)":', renderer)) | inline | skipped
handled_tags |= set(re.findall(r'Tag is (?:not )?\(?"([a-z0-9]+)"', renderer + surface))
handled_tags |= set(re.findall(r' or "([a-z0-9]+)"', renderer + surface))
handled_tags |= {"html", "body", "head", "h4", "h5", "h6", "div", "section", "article", "header", "footer", "main", "nav", "aside", "p", "span"}
html_missing = sorted(t for t in html_all if t not in handled_tags)
html_have = sorted(t for t in html_all if t in handled_tags)

# ---------------------------------------------------------------- JavaScript
prelude_path = os.path.join(HERE, "prelude_extracted.js")
prelude = open(prelude_path, encoding="utf-8").read() if os.path.exists(prelude_path) else ""
js_members = set(re.findall(r"\b([a-zA-Z_$][\w$]*)\s*(?::|=)\s*function", prelude)) | set(re.findall(r"(?:get|set) ([a-zA-Z_$][\w$]*)\s*\(", prelude))
js_members |= set(re.findall(r"function ([a-zA-Z_$][\w$]*)", prelude)) | set(re.findall(r"\bvar ([a-zA-Z_$][\w$]*)", prelude))
js_members |= set(re.findall(r"\b([a-zA-Z_$][\w$]*)\s*:\s*(?:function|\{|\[|new |true|false|'|\"|\d)", prelude))
js_members |= set(re.findall(r"defineProperty\([^,]+,\s*'([a-zA-Z_$][\w$]*)'", prelude))
js_members |= set(re.findall(r"window\.([a-zA-Z_$][\w$]*)\s*=", prelude))
js_members |= set(re.findall(r",\s*([a-zA-Z_$][\w$]*)\s*=\s*__", prelude))  # second declarators (var a = x(), b = y())
js_ref = {
 "document": "getElementById querySelector querySelectorAll getElementsByClassName getElementsByTagName getElementsByName createElement createElementNS createTextNode createDocumentFragment body head documentElement title readyState activeElement addEventListener removeEventListener dispatchEvent createEvent cookie hidden visibilityState fonts forms images links scripts hasFocus execCommand elementFromPoint getSelection write open close".split(),
 "element": "id className classList tagName innerHTML outerHTML textContent innerText children childNodes firstChild lastChild firstElementChild lastElementChild parentElement parentNode nextSibling previousSibling nextElementSibling previousElementSibling getAttribute setAttribute removeAttribute hasAttribute toggleAttribute attributes dataset style appendChild removeChild replaceChild insertBefore append prepend before after remove replaceWith replaceChildren insertAdjacentHTML insertAdjacentElement insertAdjacentText cloneNode contains closest matches querySelector querySelectorAll getBoundingClientRect getClientRects clientWidth clientHeight clientTop clientLeft offsetWidth offsetHeight offsetTop offsetLeft offsetParent scrollWidth scrollHeight scrollTop scrollLeft scrollTo scrollBy scrollIntoView focus blur click addEventListener removeEventListener dispatchEvent value checked disabled hidden open selected selectedIndex options name type placeholder src href title alt tabIndex animate getAnimations getContext isConnected nodeType nodeName hasChildNodes normalize compareDocumentPosition requestFullscreen setPointerCapture releasePointerCapture".split(),
 "window": "setTimeout clearTimeout setInterval clearInterval requestAnimationFrame cancelAnimationFrame requestIdleCallback cancelIdleCallback queueMicrotask addEventListener removeEventListener dispatchEvent alert confirm prompt console localStorage sessionStorage navigator location history screen innerWidth innerHeight outerWidth outerHeight devicePixelRatio matchMedia getComputedStyle scrollTo scrollBy scrollX scrollY pageXOffset pageYOffset open close print focus blur fetch XMLHttpRequest WebSocket Worker postMessage structuredClone crypto performance URL URLSearchParams TextEncoder TextDecoder Blob File FileReader FormData Headers Request Response AbortController Event CustomEvent KeyboardEvent MouseEvent PointerEvent InputEvent EventTarget Node Element HTMLElement Image Audio Option DOMParser XMLSerializer MutationObserver ResizeObserver IntersectionObserver Intl atob btoa Promise".split(),
 "canvas": "fillRect strokeRect clearRect beginPath closePath moveTo lineTo arc arcTo ellipse rect roundRect bezierCurveTo quadraticCurveTo fill stroke clip fillText strokeText measureText save restore translate rotate scale transform setTransform resetTransform getTransform drawImage createLinearGradient createRadialGradient createConicGradient createPattern setLineDash getLineDash isPointInPath isPointInStroke getImageData putImageData createImageData fillStyle strokeStyle lineWidth lineCap lineJoin miterLimit lineDashOffset globalAlpha globalCompositeOperation font textAlign textBaseline direction shadowBlur shadowColor shadowOffsetX shadowOffsetY imageSmoothingEnabled filter".split(),
}
jint_builtin = {"Promise", "Intl", "JSON", "Map", "Set", "WeakMap", "Symbol", "Proxy", "Reflect", "Date", "Math", "RegExp", "ArrayBuffer", "DataView", "Uint8Array"}
js_report = {}
for group, names in js_ref.items():
    names = [n for n in names if n not in jint_builtin]
    have = [n for n in names if n in js_members or ("'" + n + "'") in prelude or ('"' + n + '"') in prelude or (n + ":") in prelude or (n + " :") in prelude]
    miss = [n for n in names if n not in have]
    js_report[group] = (have, miss)

# ---------------------------------------------------------------- report
from reasons import R, APPROX, OUT
lines = []
def section(title): lines.append("\n## " + title + "\n")
def table(header, rows):
    lines.append("| " + " | ".join(header) + " |")
    lines.append("|" + "|".join("---" for _ in header) + "|")
    for r in rows: lines.append("| " + " | ".join(str(c).replace("|", "\\|") for c in r) + " |")
def names(items): return ", ".join("`" + n + "`" for n in items) if items else "none"
def why(key):
    k, t = R.get(key, ("?", "no reason recorded yet: add it to reasons.py"))
    return k, t

lines.append("# What works, what does not, and why (" + __import__("datetime").date.today().isoformat() + ")\n")
lines.append("One file. The counts and the item names are read from the code by `ScriptedScreensHtml.Tests/coverage.py` (run it after a change), against MDN's CSS data, the HTML living standard's element list and a list of the DOM and Web APIs a page script commonly uses; the reasons come from `ScriptedScreensHtml.Tests/reasons.py`, written by hand. \"Handled\" means the code names it; whether it behaves as a browser is what the console pages and examples check (`SUPPORT.md`).\n")
lines.append("Kinds: **build** = not done yet, nothing in the way. **approximation** = drawn, but not as a browser draws it, listed to be replaced. **out** = decided against, with the reason; reopen by deciding otherwise. **fine** = a browser does no more.\n")

missing = sorted(k for k in mdn_props if k not in css_props)
fn_have = sorted(f for f in mdn_fn if f.lower() in fn_lower); fn_miss = sorted(f for f in mdn_fn if f.lower() not in fn_lower)
sel_have = {":" + p for p in pseudos} | {"::" + p for p in pseudo_elements}
mdn_sel_norm = {re.sub(r"\(\)$", "", k) for k in mdn_sel if k.startswith(":")}
sel_miss = sorted(k for k in mdn_sel_norm if k not in sel_have)
at_have = sorted(a for a in at_rules & (mdn_at | {"keyframes", "-webkit-keyframes"})); at_miss = sorted(mdn_at - at_rules)
unit_miss = sorted(u for u in mdn_units if u.lower() not in {x.lower() for x in units})

section("Summary")
table(["Area", "Handled", "Of", "Share"], [
    ("CSS properties (standard)", len(mdn_props) - len(missing), len(mdn_props), "%d%%" % (100 * (len(mdn_props) - len(missing)) // len(mdn_props))),
    ("CSS at-rules", len(at_have), len(at_have) + len(at_miss), "%d%%" % (100 * len(at_have) // (len(at_have) + len(at_miss)))),
    ("CSS pseudo-classes and pseudo-elements", len(sel_have), len(sel_have) + len(sel_miss), "%d%%" % (100 * len(sel_have) // (len(sel_have) + len(sel_miss)))),
    ("CSS functions", len(fn_have), len(mdn_fn), "%d%%" % (100 * len(fn_have) // len(mdn_fn))),
    ("CSS units", len(units), len(units) + len(unit_miss), "%d%%" % (100 * len(units) // (len(units) + len(unit_miss)))),
    ("HTML elements", len(html_have), len(html_all), "%d%%" % (100 * len(html_have) // len(html_all))),
] + [("JavaScript: " + g, len(h), len(h) + len(m), "%d%%" % (100 * len(h) // max(1, len(h) + len(m)))) for g, (h, m) in js_report.items()])

section("CSS properties, by specification group")
groups_all = {}
for k, v in mdn_props.items(): groups_all.setdefault(v.get("groups", ["?"])[0], []).append(k)
rows = []
for g in sorted(groups_all, key=lambda g: (-len([k for k in groups_all[g] if k not in css_props]), g)):
    ks = groups_all[g]; miss_g = sorted(k for k in ks if k not in css_props)
    if not miss_g: continue
    kind, text = why("group:" + g)
    rows.append((g, "%d of %d" % (len(ks) - len(miss_g), len(ks)), names(miss_g), kind, text))
table(["Group", "Handled", "Not handled", "Kind", "Why, and what closes it"], rows)
full = sorted(g for g in groups_all if all(k in css_props for k in groups_all[g]))
lines.append("\nComplete groups: " + ", ".join(full) + ".")

section("CSS at-rules")
lines.append("Handled: " + names("@" + a for a in at_have) + "\n")
table(["Not handled", "Kind", "Why"], [("`@" + a + "`",) + why("at:@" + a) for a in at_miss])

section("CSS selectors")
lines.append("Handled pseudo-classes: " + names(sorted(k for k in sel_have if not k.startswith("::"))) + "\n")
lines.append("Handled pseudo-elements: " + names(sorted(k for k in sel_have if k.startswith("::"))) + "\n")
table(["Not handled", "Kind", "Why"], [("`" + k + "`",) + why("sel:" + k) for k in sel_miss])

section("CSS functions")
lines.append("Handled: " + names(fn_have) + "\n")
table(["Not handled", "Kind", "Why"], [("`" + f + "()`",) + why("fn:" + f) for f in fn_miss])

section("CSS units")
lines.append("Handled: " + names(sorted(units)) + "\n")
table(["Not handled", "Kind", "Why"], [("`" + u + "`",) + why("unit:" + u) for u in unit_miss])

section("HTML elements")
lines.append("Handled: " + names(html_have) + "\n")
table(["Not handled", "Kind", "Why"], [("`<" + t + ">`",) + why("html:" + t) for t in html_missing])

section("JavaScript: DOM and Web APIs")
rows = []
for g, (h, m) in js_report.items():
    for n in m:
        rows.append((g, "`" + n + "`") + why("js:" + n))
lines.append("Handled: document %d of %d, element %d of %d, window %d of %d, canvas %d of %d.\n" % tuple(x for g in js_report.values() for x in (len(g[0]), len(g[0]) + len(g[1]))))
table(["Object", "Not handled", "Kind", "Why"], rows)

section("Approximations: drawn, but not as a browser draws it")
table(["What", "Why", "What closes it"], APPROX)

section("Decided out, with no single name to measure")
table(["What", "Why"], OUT)

out = os.path.join(MOD, "COVERAGE.md")
open(out, "w", encoding="utf-8").write("\n".join(lines) + "\n")
unreasoned = [l for l in lines if "no reason recorded" in l]
print("written", out, "-", len(unreasoned), "rows without a reason", unreasoned)
