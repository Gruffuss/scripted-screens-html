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
for m in re.findall(r'case "([a-z-]+)":', emitter + style): fn_names.add(m)
for m in re.findall(r'StartsWith\("([a-z-]+)\(', code): fn_names.add(m)
for m in re.findall(r'"([a-z-]+)"', code): fn_names.add(m)  # keyword spellings (min, max, clamp as words)
mdn_fn = {k.rstrip("()") for k, v in mdn("functions").items() if v.get("status") == "standard"}

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
lines = []
def section(title): lines.append("\n## " + title + "\n")
lines.append("# Coverage, measured (" + __import__("datetime").date.today().isoformat() + ")\n")
lines.append("Handled sets are read from the code by `ScriptedScreensHtml.Tests/coverage.py`; references are MDN's CSS data, the HTML living standard's element list and a list of the DOM and Web APIs a page script commonly uses. A name counted as handled is one the code names; whether it behaves as a browser is what the console pages and examples check.\n")

section("CSS properties")
missing = sorted(k for k in mdn_props if k not in css_props)
lines.append("**%d of %d standard properties handled**, %d not:\n" % (len(mdn_props) - len(missing), len(mdn_props), len(missing)))
groups = {}
for k in missing: groups.setdefault(mdn_props[k].get("groups", ["?"])[0], []).append(k)
for g in sorted(groups, key=lambda g: -len(groups[g])):
    lines.append("- %s (%d): %s" % (g, len(groups[g]), " ".join(groups[g])))

section("CSS at-rules, selectors, functions, units")
lines.append("- at-rules handled: %s" % " ".join(sorted("@" + a for a in at_rules & (mdn_at | {"keyframes", "-webkit-keyframes"}))))
lines.append("- at-rules not handled: %s" % " ".join(sorted("@" + a for a in mdn_at - at_rules)))
sel_have = {":" + p for p in pseudos} | {"::" + p for p in pseudo_elements}
mdn_sel_norm = {re.sub(r"\(\)$", "", k) for k in mdn_sel if k.startswith(":")}
lines.append("- pseudo-classes and pseudo-elements handled: %s" % " ".join(sorted(sel_have)))
lines.append("- not handled: %s" % " ".join(sorted(k for k in mdn_sel_norm if k not in sel_have)))
fn_lower = {f.lower() for f in fn_names} | {"repeating-linear-gradient", "repeating-radial-gradient", "repeating-conic-gradient"}  # "repeating-" is a prefix check
fn_have = sorted(f for f in mdn_fn if f.lower() in fn_lower)
fn_miss = sorted(f for f in mdn_fn if f.lower() not in fn_lower)
lines.append("- functions handled (%d of %d): %s" % (len(fn_have), len(mdn_fn), " ".join(fn_have)))
lines.append("- functions not handled: %s" % " ".join(fn_miss))
lines.append("- units handled: %s" % " ".join(sorted(units)))
lines.append("- units not handled: %s" % " ".join(sorted(mdn_units - units)))

section("HTML elements")
lines.append("**%d of %d elements handled**. Not handled: %s\n" % (len(html_have), len(html_all), " ".join(html_missing)))

section("JavaScript: DOM and Web APIs")
for group, (have, miss) in js_report.items():
    lines.append("- **%s**: %d of %d. Missing: %s" % (group, len(have), len(have) + len(miss), " ".join(miss) if miss else "none"))

out = os.path.join(MOD, "COVERAGE.md")
open(out, "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("\n".join(lines))
print("\nwritten", out)
