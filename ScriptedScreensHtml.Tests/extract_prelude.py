"""Pull the JS prelude out of ScriptHost.cs (a C# verbatim string) into prelude_extracted.js, then run:
    node prelude_smoke.js
Node checks what the C# build cannot: that the DOM shim parses and behaves."""
import os
here = os.path.dirname(os.path.abspath(__file__))
s = open(os.path.join(here, "..", "ScriptedScreensHtml", "ScriptHost.cs"), encoding="utf-8").read()
i = s.index('private const string Prelude = @"') + len('private const string Prelude = @"')
j = i
while True:
    k = s.index('"', j)
    if k + 1 < len(s) and s[k + 1] == '"':
        j = k + 2
        continue
    break
open(os.path.join(here, "prelude_extracted.js"), "w", encoding="utf-8").write(s[i:k].replace('""', '"'))
print("prelude_extracted.js written")
