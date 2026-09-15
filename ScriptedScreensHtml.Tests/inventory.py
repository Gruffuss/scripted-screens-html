"""Gap inventory: what real pages use that the HTML mod does not handle.

    python inventory.py page1.html page2.html ...   (or a folder: every .html under it)

Reads the supported sets straight from the C# sources (StyleApplier's property switch and
its silent list, the emitter's css keys, CssParser's pseudo-classes and at-rules, the
renderer's tags, the emitter's svg elements, the script prelude's members), scans each page
for what it uses, and prints what is used but unsupported, most used first. A measurement,
not a memory: run it after adding features to see what is left.
"""
import os, re, sys, collections

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "ScriptedScreensHtml")

def read(name):
    return open(os.path.join(SRC, name), encoding="utf-8").read()

# ---------------------------------------------------------------- supported sets, from the code
style = read("StyleApplier.cs")
css_props = set(re.findall(r'case "([a-z-]+)":', style))
elsewhere = style[style.index("HashSet<string> Elsewhere"):]
elsewhere = elsewhere[:elsewhere.index("};")]
css_props |= set(re.findall(r'"([a-z-]+)"', elsewhere))
for f in ("VectorEmitter.cs", "GridLayout.cs", "HtmlRenderer.cs", "HtmlSurface.cs", "Tweens.cs"):
    css_props |= set(re.findall(r'TryGetValue\("([a-z-]+)"', read(f)))
css_props |= {"transition", "animation", "animation-name", "animation-duration", "animation-delay", "animation-iteration-count",
              "animation-timing-function", "animation-direction", "animation-fill-mode", "animation-play-state"}
parser = read("CssParser.cs")
pseudo_block = parser[parser.index("private static bool ParsePseudos"):]
pseudos = set(re.findall(r'case "([a-z-]+)"', pseudo_block))
pseudo_elements = {"before", "after"}
at_rules = {"keyframes", "-webkit-keyframes", "media", "supports", "font-face"}
renderer = read("HtmlRenderer.cs")
inline = set(re.findall(r'"([a-z]+)"', renderer[renderer.index("HashSet<string> Inline"):renderer.index("HashSet<string> Skipped")]))
skipped = {"head", "title", "meta", "link", "script", "style", "template"}
handled_tags = set(re.findall(r'node\.Tag (?:==|is) "([a-z0-9]+)"', renderer)) | set(re.findall(r'case "([a-z0-9]+)":', renderer)) | inline | skipped
handled_tags |= {"html", "body", "div", "span", "p", "section", "article", "nav", "header", "footer", "main", "aside", "form", "label", "canvas",
                 "input", "select", "textarea", "option", "optgroup", "button", "img", "video", "audio", "table", "thead", "tbody", "tfoot", "tr", "td", "th",
                 "caption", "ul", "ol", "li", "dl", "dt", "dd", "details", "summary", "dialog", "progress", "meter", "figure", "figcaption", "blockquote",
                 "pre", "code", "hr", "br", "svg", "a", "picture", "source", "fieldset", "legend", "address", "small", "strong", "em", "b", "i", "u", "s"}
emitter = read("VectorEmitter.cs")
svg_elements = {"svg", "g", "defs", "lineargradient", "radialgradient", "stop", "line", "polyline", "polygon", "rect", "circle", "ellipse", "path"}
prelude = read("ScriptHost.cs")
prelude = prelude[prelude.index('Prelude = @"'):]
js_members = set(re.findall(r"\b([a-zA-Z_]\w*)\s*(?::|=|\()", prelude)) | set(re.findall(r"(?:get|set) ([a-zA-Z_]\w*)\(", prelude))
js_members |= set(re.findall(r"function ([a-zA-Z_]\w*)", prelude)) | set(re.findall(r"var ([a-zA-Z_]\w*)", prelude))

# JS APIs a page reaches for that need a definition here (checked against the prelude and Jint built-ins)
js_builtin = {"Math", "JSON", "Date", "Promise", "Object", "Array", "String", "Number", "Boolean", "RegExp", "Map", "Set", "WeakMap", "Symbol", "Error", "parseInt",
              "parseFloat", "isNaN", "isFinite", "encodeURIComponent", "decodeURIComponent", "Intl", "Reflect", "Proxy", "Uint8Array", "Float32Array", "ArrayBuffer",
              "Function", "undefined", "NaN", "Infinity", "globalThis", "arguments", "this", "async", "await", "new", "typeof", "instanceof", "eval", "escape", "unescape"}

# ---------------------------------------------------------------- MDN data (mdn/*.json from github.com/mdn/data), for a spec-level view
import json
def mdn(name):
    path = os.path.join(HERE, "mdn", name + ".json")
    return json.load(open(path, encoding="utf-8")) if os.path.exists(path) else {}
mdn_props = {k: v for k, v in mdn("properties").items() if v.get("status") == "standard" and not k.startswith("-")}
mdn_at = {k[1:] for k, v in mdn("at-rules").items() if v.get("status") == "standard"}
mdn_sel = {k for k, v in mdn("selectors").items() if v.get("status") == "standard"}
mdn_fn = {k.rstrip("()") for k, v in mdn("functions").items() if v.get("status") == "standard"}

# ---------------------------------------------------------------- scanning
def strip(html, tag):
    return re.sub(r"<%s\b[^>]*>.*?</%s>" % (tag, tag), "", html, flags=re.S | re.I)

def scan(path):
    html = open(path, encoding="utf-8", errors="replace").read()
    out = collections.defaultdict(collections.Counter)
    styles = re.findall(r"<style\b[^>]*>(.*?)</style>", html, flags=re.S | re.I)
    scripts = [s for t, s in re.findall(r"<script\b([^>]*)>(.*?)</script>", html, flags=re.S | re.I) if "src=" not in t.lower() and ("javascript" in t.lower() or "type=" not in t.lower() or "module" in t.lower())]
    ext_scripts = re.findall(r"<script\b[^>]*\bsrc=", html, flags=re.I)
    ext_styles = re.findall(r"<link\b[^>]*rel=[\"']?stylesheet", html, flags=re.I)
    body = strip(strip(html, "style"), "script")
    inline_styles = re.findall(r"\bstyle=\"([^\"]*)\"", body, flags=re.I) + re.findall(r"\bstyle='([^']*)'", body, flags=re.I)

    # tags and attributes
    in_svg = False
    for m in re.finditer(r"<(/?)([a-zA-Z][a-zA-Z0-9-]*)([^>]*)>", body):
        close, tag, attrs = m.group(1), m.group(2).lower(), m.group(3)
        if tag == "svg": in_svg = not close
        if close: continue
        if in_svg or tag == "svg":
            if tag not in svg_elements: out["svg elements"][tag] += 1
            for a in re.findall(r"\s([a-zA-Z-:]+)=", attrs): out["svg attributes"][a] += 1
        else:
            if tag not in handled_tags: out["html tags"][tag] += 1
            if tag == "input":
                t = re.search(r"type=[\"']?([a-z-]+)", attrs, flags=re.I)
                out["input types"][(t.group(1).lower() if t else "text")] += 1
            for a in re.findall(r"\s([a-zA-Z-]+)=", attrs):
                if a.startswith("on") and a not in ("onclick", "onchange", "oninput", "onsubmit"): out["inline handlers"][a] += 1
                if a in ("contenteditable", "draggable", "tabindex", "role", "aria-hidden", "autofocus", "required", "pattern", "form"): out["attributes"][a] += 1

    # CSS: a block walker. A block whose body holds no nested block is declarations; the
    # text before a brace is a selector (or an at-rule header). Nothing inside a value is
    # ever read as a selector.
    css_all = "\n".join(styles)
    css_all = re.sub(r"/\*.*?\*/", "", css_all, flags=re.S)
    selectors, decls, ats = [], [], []
    def walk(text):
        i = 0
        while True:
            open_ = text.find("{", i)
            if open_ < 0: break
            head = text[text.rfind("}", 0, open_) + 1:open_]
            head = head[head.rfind(";") + 1:].strip() if head.rfind(";") > head.rfind("{") else head.strip()
            depth, j = 0, open_
            while j < len(text):
                if text[j] == "{": depth += 1
                elif text[j] == "}":
                    depth -= 1
                    if depth == 0: break
                j += 1
            body = text[open_ + 1:j]
            if head.startswith("@"):
                ats.append(head.split("(")[0].split(" ")[0][1:].lower())
                if "{" in body: walk(body)
                else: decls.append(body)
            else:
                selectors.append(head)
                if "{" in body:
                    # nesting: declarations before the first nested block, then the nested blocks
                    decls.append(body[:body.find("{")])
                    walk(body)
                else:
                    decls.append(body)
            i = j + 1
    walk(css_all)
    for at in ats:
        if at not in at_rules: out["css at-rules"]["@" + at] += 1
    for sel in selectors:
        sel = re.sub(r"\\.", "", sel)   # an escaped character is part of a name
        for part in sel.split(","):
            for p in re.findall(r"(?<![a-zA-Z0-9_-]):([a-zA-Z-]+)", part):
                if p in pseudos or p in pseudo_elements or p in ("hover", "active", "focus", "root"): continue
                out["css pseudo-classes"][":" + p] += 1
            for pe in re.findall(r"::([a-zA-Z-]+)", part):
                if pe not in pseudo_elements: out["css pseudo-elements"]["::" + pe] += 1
            if re.search(r"\[[^\]]*[^~|^$*=a-zA-Z0-9\"'\s-][^\]]*\]", part): out["css selectors"]["unusual [attr] syntax"] += 1
    known_fns = {"rgb", "rgba", "hsl", "hsla", "var", "calc", "min", "max", "clamp", "url", "linear-gradient", "radial-gradient", "conic-gradient",
                 "repeating-linear-gradient", "repeating-radial-gradient", "translate", "translatex", "translatey", "rotate", "scale", "scalex", "scaley", "skew",
                 "skewx", "skewy", "matrix", "cubic-bezier", "steps", "attr", "inset", "circle", "ellipse", "polygon", "drop-shadow", "brightness", "contrast",
                 "grayscale", "sepia", "invert", "saturate", "hue-rotate", "opacity", "blur", "repeat", "minmax", "format", "counter", "translate3d", "scale3d",
                 "rotate3d", "matrix3d", "rotatex", "rotatey", "rotatez", "perspective", "local"}
    for block in decls + inline_styles:
        for prop, val in re.findall(r"(?:^|;)\s*(-?[a-zA-Z-]+)\s*:\s*([^;]+)", block):
            prop = prop.lower().strip()
            if prop.startswith("--"): continue
            if mdn_props and prop not in mdn_props and not prop.startswith("-webkit-") and not prop.startswith("-moz-"): continue
            if prop not in css_props: out["css properties"][prop] += 1
            for fn in re.findall(r"([a-zA-Z-]+)\(", val):
                if fn.lower() not in known_fns: out["css functions"][fn + "()"] += 1
            v = val.strip().lower()
            if prop in css_props and v in ("inherit", "initial", "unset", "revert"): pass
    # JS: globals and members a page reaches for
    js = "\n".join(scripts)
    for g in re.findall(r"\b(document|window|navigator|location|history|localStorage|screen|performance)\.([a-zA-Z_]\w*)", js):
        if g[1] not in js_members: out["js %s.*" % g[0]]["%s.%s" % g] += 1
    for name in re.findall(r"\bnew\s+([A-Z][a-zA-Z0-9]*)", js):
        if name not in js_members and name not in js_builtin: out["js constructors"]["new " + name] += 1
    for name in ("fetch", "XMLHttpRequest", "WebSocket", "customElements", "attachShadow", "IntersectionObserver", "ResizeObserver", "MutationObserver",
                 "requestAnimationFrame", "getComputedStyle", "matchMedia", "scrollIntoView", "scrollTo", "focus", "blur", "cloneNode", "insertAdjacentHTML",
                 "dispatchEvent", "addEventListener", "querySelector", "classList", "dataset", "getBoundingClientRect", "import", "export", "Worker", "AbortController",
                 "structuredClone", "IndexedDB", "indexedDB", "sessionStorage", "Notification", "Audio", "Image", "FormData", "Blob", "FileReader", "TextEncoder", "URL"):
        n = len(re.findall(r"\b%s\b" % name, js))
        if n and name not in js_members and name not in js_builtin: out["js apis"][name] += n
    for ev in re.findall(r"addEventListener\(\s*['\"]([a-zA-Z]+)['\"]", js):
        if ev not in ("click", "change", "input", "submit", "toggle", "load", "DOMContentLoaded", "data", "mouseover", "mouseout", "mousemove", "mousedown", "mouseup", "mouseenter", "mouseleave"):
            out["js events"][ev] += 1

    out["_meta"]["external scripts"] = len(ext_scripts)
    out["_meta"]["external stylesheets"] = len(ext_styles)
    return out

def main(paths):
    files = []
    for p in paths:
        if os.path.isdir(p):
            for root, _, names in os.walk(p):
                files += [os.path.join(root, n) for n in names if n.lower().endswith(".html")]
        else:
            files.append(p)
    total = collections.defaultdict(collections.Counter)
    where = collections.defaultdict(lambda: collections.defaultdict(set))
    for f in files:
        try:
            r = scan(f)
        except Exception as e:
            print("skip", f, e); continue
        for cat, c in r.items():
            for k, n in c.items():
                total[cat][k] += n
                where[cat][k].add(os.path.basename(f))
    print("%d pages" % len(files))
    for cat in sorted(total):
        if cat == "_meta": continue
        items = total[cat].most_common()
        if not items: continue
        print("\n== %s (%d distinct)" % (cat, len(items)))
        for k, n in items[:60]:
            print("  %-40s %5d  in %d page(s)" % (k, n, len(where[cat][k])))
    print("\n== external:", dict(total["_meta"]))
    if mdn_props:
        missing = sorted(k for k in mdn_props if k not in css_props)
        groups = collections.defaultdict(list)
        for k in missing:
            g = mdn_props[k].get("groups", ["?"])[0]
            groups[g].append(k)
        print("\n== spec coverage: %d of %d standard CSS properties handled; %d not, by group:" % (len(mdn_props) - len(missing), len(mdn_props), len(missing)))
        for g in sorted(groups, key=lambda g: -len(groups[g])):
            print("  %-32s %3d  %s" % (g, len(groups[g]), " ".join(groups[g])))
        if mdn_at:
            print("\n== at-rules not handled:", " ".join("@" + a for a in sorted(mdn_at - at_rules)))
        if mdn_sel:
            have = {":" + p for p in pseudos} | {"::" + p for p in pseudo_elements} | {":hover", ":active", ":focus", ":root"}
            print("== selectors not handled:", " ".join(sorted(k for k in mdn_sel if k.startswith(":") and k not in have)))

if __name__ == "__main__":
    main(sys.argv[1:] or [os.path.join(HERE, "corpus")])
