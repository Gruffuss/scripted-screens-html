Rules and columns: see INSTRUCTION-SET.md.

## 4. DOM and Web APIs

### 4.1 Window and globals

#### Timers, animation/idle callbacks, viewport, microtasks, dialogs, misc

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Window.setTimeout() | ✅ | scheduler on the chip's tick (`v_timer`); due timers fire at the next 0.5 s game tick | PlainTranslatorTests: a function shared by two timers; 09-transition in game 2026-09-24 | |
| Window.clearTimeout() | ✅ | `v_clear` | PlainTranslatorTests: an element held in a variable ... clearTimeout | |
| Window.setInterval() | ✅ | `v_timer(fn, ms, true)` on the chip's tick | PlainTranslatorTests: setInterval + clearInterval; plain-counter in game 2026-09-24 | |
| Window.clearInterval() | ✅ | `v_clear` | PlainTranslatorTests: setInterval + clearInterval; plain-counter in game | |
| Window.requestAnimationFrame() | | | | |
| Window.cancelAnimationFrame() | | | | |
| Window.requestIdleCallback() | | | | |
| Window.cancelIdleCallback() | | | | |
| Window.innerWidth | ✅ | a constant: the width the page is laid out at on the console it is compiled for (its `<meta name=viewport>` width, else the console's canvas width; one CSS pixel is one canvas unit), floored as the browser's is. Writing it is refused for now | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerWidth, innerHeight and devicePixelRatio: the console the page is compiled for" | |
| Window.innerHeight | ✅ | a constant: the height the page is laid out at, in the console's own proportions, floored | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerWidth, innerHeight and devicePixelRatio: the console the page is compiled for" | |
| Window.outerWidth | | | | |
| Window.outerHeight | | | | |
| Window.devicePixelRatio | ✅ | the constant 1: one CSS pixel is one unit of the console's canvas, and the vector scene has no pixels of its own | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerWidth, innerHeight and devicePixelRatio: the console the page is compiled for" | |
| Window.getComputedStyle() | | | | |
| Window.queueMicrotask() | | | | |
| Window.structuredClone() | | | | |
| Window.open() | | | | |
| Window.close() | | | | |
| Window.focus() | | | | |
| Window.blur() | | | | |
| Window.print() | | | | |
| Window.scroll() | | | | |
| Window.scrollTo() | | | | |
| Window.scrollBy() | | | | |
| Window.alert() | | | | |
| Window.confirm() | | | | |
| Window.prompt() | | | | |

#### Window.matchMedia() and MediaQueryList

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Window.matchMedia() | | | | |
| MediaQueryList.matches | | | | |
| MediaQueryList.media | | | | |
| MediaQueryList.addEventListener() / removeEventListener() | | | | |
| MediaQueryList.addListener() (deprecated) | | | | |
| MediaQueryList.removeListener() (deprecated) | | | | |

#### Window.location and the Location interface

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Window.location | ✅ | no object on the chip: each member below is resolved at compile time; `typeof location` is "object"; `window.location` the same. `location = url` is a navigation (see href). The object passed around as a value is refused for now | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "location: what a page loaded from no URL reads, and a hash set by a click" | |
| Location.href | ✅ | read: "about:blank" (what a page loaded from no URL has), or `V_HREF` once the script navigates to a fragment. Set to a fragment (`#x`): the hash, below. Set to anything else: refused, it loads another document and a console holds only the page compiled onto it, with no network to fetch another | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "location: what a page loaded from no URL reads, and a hash set by a click" | |
| Location.protocol | ✅ | read: the constant "about:". Setting protocol, host, hostname, port, pathname or search is refused: it loads another document, and a console holds only the page compiled onto it and has no network to fetch another. | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "location: what a page loaded from no URL reads, and a hash set by a click" | |
| Location.host | ✅ | read: the constant "". Setting protocol, host, hostname, port, pathname or search is refused: it loads another document, and a console holds only the page compiled onto it and has no network to fetch another. | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "location: what a page loaded from no URL reads, and a hash set by a click" | |
| Location.hostname | ✅ | read: the constant "". Setting protocol, host, hostname, port, pathname or search is refused: it loads another document, and a console holds only the page compiled onto it and has no network to fetch another. | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "location: what a page loaded from no URL reads, and a hash set by a click" | |
| Location.port | ✅ | read: the constant "". Setting protocol, host, hostname, port, pathname or search is refused: it loads another document, and a console holds only the page compiled onto it and has no network to fetch another. | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "location: what a page loaded from no URL reads, and a hash set by a click" | |
| Location.pathname | ✅ | read: the constant "blank". Setting protocol, host, hostname, port, pathname or search is refused: it loads another document, and a console holds only the page compiled onto it and has no network to fetch another. | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "location: what a page loaded from no URL reads, and a hash set by a click" | |
| Location.search | ✅ | read: the constant "". Setting protocol, host, hostname, port, pathname or search is refused: it loads another document, and a console holds only the page compiled onto it and has no network to fetch another. | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "location: what a page loaded from no URL reads, and a hash set by a click"; refusal "changing location.search" | |
| Location.hash | ✅ | read: "" until the script sets one, then `V_HASH`. Set: `v_sethash` (a leading # dropped, space, ", <, >, ` and control characters percent-encoded as the URL spec does), which also sets `V_HREF`. No hashchange event (a window event, not translated) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "location: what a page loaded from no URL reads, and a hash set by a click" | |
| Location.origin | ✅ | read: the constant "null" (about:blank's opaque origin) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "location: what a page loaded from no URL reads, and a hash set by a click" | |
| Location.ancestorOrigins | ✅ | an empty list: `.length` is 0, `.item()` null, `.contains()` false (a console's page is embedded in nothing). Other uses refused for now | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "location: what a page loaded from no URL reads, and a hash set by a click" | |
| Location.assign() | ✅ | to a fragment of this page (`#x`, known to start with # at compile time): `v_sethash`. Anywhere else: refused, it loads another document and a console has no network to fetch another; a URL only known at run time is refused as possibly another document | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "location: what a page loaded from no URL reads, and a hash set by a click"; refusal "location.assign to a URL only known at run time" | |
| Location.replace() | ✅ | as assign(): the console keeps no history, so the two are one | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "location.reload(): the page starts again ..." | |
| Location.reload() | ✅ | `v_askreload()`; once the script that asked has finished (the timer callback or the click), `v_reload` clears every timer and listener, puts every class, attribute and scene value back as the page's source has them, and runs the page's script again (`v_main`); sessionStorage, localStorage and the hash are kept, as a browser keeps them | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "location.reload(): the page starts again ..." | |
| Location.toString() | ✅ | as href | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "location: what a page loaded from no URL reads, and a hash set by a click" | |

#### Window.history and the History interface

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Window.history | | | | |
| History.length | | | | |
| History.state | | | | |
| History.scrollRestoration | | | | |
| History.pushState() | | | | |
| History.replaceState() | | | | |
| History.back() | | | | |
| History.forward() | | | | |
| History.go() | | | | |

#### Window.navigator and the Navigator interface

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Window.navigator | | | | |
| Navigator.userAgent | | | | |
| Navigator.language | | | | |
| Navigator.languages | | | | |
| Navigator.onLine | | | | |
| Navigator.platform | | | | |
| Navigator.clipboard | | | | |
| Navigator — further members (geolocation, mediaDevices, serviceWorker, bluetooth, usb, gpu, connection, credentials, deviceMemory, hardwareConcurrency, permissions, sendBeacon(), vibrate(), getBattery(), getGamepads(), share(), …; group, no host binding reaches any of them from this sandbox) | | | | |

#### console

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| console.log() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.log` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.info() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.info` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.warn() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.warn` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.error() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.error` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.debug() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.debug` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.table() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.table` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.group() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.group` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.groupCollapsed() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.groupCollapsed` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.groupEnd() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.groupEnd` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.time() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.time` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.timeEnd() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.timeEnd` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.timeLog() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.timeLog` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.timeStamp() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.timeStamp` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.assert() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.assert` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.count() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.count` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.countReset() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.countReset` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.trace() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.trace` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.clear() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.clear` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.dir() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.dir` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.dirxml() | ✅ | removed at compile: a chip has no console. An argument that does something (a call, an assignment, ++) is still evaluated where the call stood; `console.dirxml` passed as a function is `v_void`, which does nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.exception() (non-standard) | ✅ | removed at compile, as console.log | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.profile() (non-standard) | ✅ | removed at compile, as console.log | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |
| console.profileEnd() (non-standard) | ✅ | removed at compile, as console.log | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "console: every call goes, and what its arguments do stays"; "[console]: every console call is gone from the compiled Lua" | |

#### Window.performance and the Performance interface

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Window.performance | | | | |
| Performance.now() | | | | |
| Performance.mark() | | | | |
| Performance.measure() | | | | |
| Performance.clearMarks() | | | | |
| Performance.clearMeasures() | | | | |
| Performance.clearResourceTimings() | | | | |
| Performance.getEntries() | | | | |
| Performance.getEntriesByName() | | | | |
| Performance.getEntriesByType() | | | | |
| Performance.measureUserAgentSpecificMemory() | | | | |
| Performance.setResourceTimingBufferSize() | | | | |
| Performance.toJSON() | | | | |
| Performance.timeOrigin | | | | |
| Performance.eventCounts | | | | |
| Performance.interactionCount | | | | |
| Performance — legacy sub-objects (navigation, timing, memory); group, all three are themselves deprecated/non-standard whole-object properties | | | | |

#### localStorage / sessionStorage (Storage interface)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Window.localStorage | ✅ | a Lua table kept in the chip's own store, `ic.persist` (stationeers://lua/api/persist): each value under "html.ls:<key>", the keys in order under "html.ls", read back when the chunk loads, so values outlive a save/load or a game restart as a browser's outlive a reload. Limits are the store's: a key up to 120 characters, a value up to 8192, 32768 bytes per chip shared with the chip's own program; past them setItem throws QuotaExceededError. The store is keyed to the chip's source, so editing the page starts it empty. `typeof localStorage` is "object"; named properties (`localStorage.x`) are refused for now | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "sessionStorage and localStorage: setItem, getItem, key, length, removeItem and clear"; PlainTranslatorTests: "localStorage outlives a restart, as the chip's own store" (three console sizes) | |
| Window.sessionStorage | ✅ | the same table in the program's memory only (`V_SS`): kept through location.reload(), gone when the page is compiled again | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "sessionStorage and localStorage: setItem, getItem, key, length, removeItem and clear"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "location.reload(): the page starts again ..." | |
| Storage.length | ✅ | `V_LS.n` / `V_SS.n` | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "sessionStorage and localStorage: setItem, getItem, key, length, removeItem and clear" | |
| Storage.key() | ✅ | `v_skeyat`: the keys in the order they were first set; null past the end | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "sessionStorage and localStorage: setItem, getItem, key, length, removeItem and clear" | |
| Storage.getItem() | ✅ | `V_LS.vals[key]` (the key converted as String() converts it); null (nil) when absent, printed "null" in a text | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "sessionStorage and localStorage: setItem, getItem, key, length, removeItem and clear"; PlainTranslatorTests: "localStorage outlives a restart, as the chip's own store" (three console sizes) | |
| Storage.setItem() | ✅ | `v_sset`: the value as String() gives it; written to `ic.persist` only when it changed, and the key list only when a key is new | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "sessionStorage and localStorage: setItem, getItem, key, length, removeItem and clear"; PlainTranslatorTests: "localStorage outlives a restart, as the chip's own store" (three console sizes) | |
| Storage.removeItem() | ✅ | `v_srm` | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "sessionStorage and localStorage: setItem, getItem, key, length, removeItem and clear" | |
| Storage.clear() | ✅ | `v_sclear`: only this page's keys; the chip program's own `ic.persist` keys are left alone | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "sessionStorage and localStorage: setItem, getItem, key, length, removeItem and clear" | |

(Storage's five members are shared identically by both localStorage and sessionStorage — listed once per the task's grouping note.)

#### fetch(), Request, Response, Headers

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Window.fetch() | | | | |
| Request() constructor | | | | |
| Request.body | | | | |
| Request.bodyUsed | | | | |
| Request.cache | | | | |
| Request.credentials | | | | |
| Request.destination | | | | |
| Request.duplex | | | | |
| Request.headers | | | | |
| Request.integrity | | | | |
| Request.isHistoryNavigation | | | | |
| Request.isReloadNavigation | | | | |
| Request.keepalive | | | | |
| Request.method | | | | |
| Request.mode | | | | |
| Request.redirect | | | | |
| Request.referrer | | | | |
| Request.referrerPolicy | | | | |
| Request.signal | | | | |
| Request.targetAddressSpace | | | | |
| Request.url | | | | |
| Request.arrayBuffer() | | | | |
| Request.blob() | | | | |
| Request.bytes() | | | | |
| Request.clone() | | | | |
| Request.formData() | | | | |
| Request.json() | | | | |
| Request.text() | | | | |
| Request.textStream() | | | | |
| Response() constructor | | | | |
| Response.body | | | | |
| Response.bodyUsed | | | | |
| Response.headers | | | | |
| Response.ok | | | | |
| Response.redirected | | | | |
| Response.status | | | | |
| Response.statusText | | | | |
| Response.type | | | | |
| Response.url | | | | |
| Response.arrayBuffer() | | | | |
| Response.blob() | | | | |
| Response.bytes() | | | | |
| Response.clone() | | | | |
| Response.formData() | | | | |
| Response.json() | | | | |
| Response.text() | | | | |
| Response.textStream() | | | | |
| Response.error() (static) | | | | |
| Response.json() (static) | | | | |
| Response.redirect() (static) | | | | |
| Headers() constructor | | | | |
| Headers.append() | | | | |
| Headers.delete() | | | | |
| Headers.entries() | | | | |
| Headers.forEach() | | | | |
| Headers.get() | | | | |
| Headers.getSetCookie() | | | | |
| Headers.has() | | | | |
| Headers.keys() | | | | |
| Headers.set() | | | | |
| Headers.values() | | | | |

#### XMLHttpRequest

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| XMLHttpRequest() constructor | | | | |
| XMLHttpRequest.open() | | | | |
| XMLHttpRequest.send() | | | | |
| XMLHttpRequest.setRequestHeader() | | | | |
| XMLHttpRequest.getResponseHeader() | | | | |
| XMLHttpRequest.getAllResponseHeaders() | | | | |
| XMLHttpRequest.abort() | | | | |
| XMLHttpRequest.overrideMimeType() | | | | |
| XMLHttpRequest.responseType | | | | |
| XMLHttpRequest.response | | | | |
| XMLHttpRequest.responseText | | | | |
| XMLHttpRequest.responseXML | | | | |
| XMLHttpRequest.responseURL | | | | |
| XMLHttpRequest.status | | | | |
| XMLHttpRequest.statusText | | | | |
| XMLHttpRequest.readyState | | | | |
| XMLHttpRequest.onreadystatechange | | | | |
| XMLHttpRequest.timeout | | | | |
| XMLHttpRequest.withCredentials | | | | |
| XMLHttpRequest.upload | | | | |
| XMLHttpRequest.setAttributionReporting() | | | | |
| XMLHttpRequest.setPrivateToken() | | | | |
| XMLHttpRequest.mozAnon (Firefox-specific) | | | | |
| XMLHttpRequest.mozSystem (Firefox-specific) | | | | |

#### WebSocket

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| WebSocket() constructor | | | | |
| WebSocket.send() | | | | |
| WebSocket.close() | | | | |
| WebSocket.readyState | | | | |
| WebSocket.url | | | | |
| WebSocket.protocol | | | | |
| WebSocket.extensions | | | | |
| WebSocket.binaryType | | | | |
| WebSocket.bufferedAmount | | | | |
| WebSocket.onopen | | | | |
| WebSocket.onmessage | | | | |
| WebSocket.onerror | | | | |
| WebSocket.onclose | | | | |
| WebSocket static ready-state constants (CONNECTING=0, OPEN=1, CLOSING=2, CLOSED=3); group, four related enum values | | | | |

### 4.2 Document, Node, ParentNode, ChildNode, Element, HTMLElement

#### Document

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Document.activeElement | | | | |
| Document.activeViewTransition | | | | |
| Document.adoptedStyleSheets | | | | |
| Document.alinkColor (deprecated) | | | | |
| Document.all (deprecated) | | | | |
| Document.anchors | | | | |
| Document.applets (deprecated) | | | | |
| Document.bgColor (deprecated) | | | | |
| Document.body | | | | |
| Document.characterSet | | | | |
| Document.childElementCount | | | | |
| Document.children | | | | |
| Document.compatMode | | | | |
| Document.contentType | | | | |
| Document.cookie | | | | |
| Document.currentScript | | | | |
| Document.customElementRegistry | | | | |
| Document.defaultView | | | | |
| Document.designMode | | | | |
| Document.dir | | | | |
| Document.doctype | | | | |
| Document.documentElement | | | | |
| Document.documentURI | | | | |
| Document.domain | | | | |
| Document.embeds | | | | |
| Document.featurePolicy | | | | |
| Document.fgColor (deprecated) | | | | |
| Document.firstElementChild | | | | |
| Document.fonts | | | | |
| Document.forms | | | | |
| Document.fragmentDirective | | | | |
| Document.fullscreen (deprecated) | | | | |
| Document.fullscreenElement | | | | |
| Document.fullscreenEnabled | | | | |
| Document.head | | | | |
| Document.hidden | | | | |
| Document.images | | | | |
| Document.implementation | | | | |
| Document.lastElementChild | | | | |
| Document.lastModified | | | | |
| Document.lastStyleSheetSet | | | | |
| Document.linkColor (deprecated) | | | | |
| Document.links | | | | |
| Document.location | | | | |
| Document.pictureInPictureElement | | | | |
| Document.pictureInPictureEnabled | | | | |
| Document.plugins | | | | |
| Document.pointerLockElement | | | | |
| Document.preferredStyleSheetSet | | | | |
| Document.prerendering | | | | |
| Document.readyState | | | | |
| Document.referrer | | | | |
| Document.rootElement | | | | |
| Document.scripts | | | | |
| Document.scrollingElement | | | | |
| Document.selectedStyleSheetSet | | | | |
| Document.styleSheets | | | | |
| Document.styleSheetSets (deprecated) | | | | |
| Document.timeline | | | | |
| Document.title | | | | |
| Document.URL | | | | |
| Document.visibilityState | | | | |
| Document.vlinkColor (deprecated) | | | | |
| Document.xmlEncoding (deprecated) | | | | |
| Document.xmlStandalone (deprecated) | | | | |
| Document.xmlVersion (deprecated) | | | | |
| Document.adoptNode() | | | | |
| Document.append() | | | | |
| Document.ariaNotify() | | | | |
| Document.browsingTopics() | | | | |
| Document.caretPositionFromPoint() | | | | |
| Document.caretRangeFromPoint() (non-standard) | | | | |
| Document.clear() (deprecated) | | | | |
| Document.close() | | | | |
| Document.createAttribute() | | | | |
| Document.createAttributeNS() | | | | |
| Document.createCDATASection() | | | | |
| Document.createComment() | | | | |
| Document.createDocumentFragment() | | | | |
| Document.createElement() | | | | |
| Document.createElementNS() | | | | |
| Document.createEvent() (legacy) | | | | |
| Document.createExpression() | | | | |
| Document.createNodeIterator() | | | | |
| Document.createNSResolver() | | | | |
| Document.createProcessingInstruction() | | | | |
| Document.createRange() | | | | |
| Document.createTextNode() | | | | |
| Document.createTouch() (non-standard) | | | | |
| Document.createTouchList() (non-standard) | | | | |
| Document.createTreeWalker() | | | | |
| Document.elementFromPoint() | | | | |
| Document.elementsFromPoint() | | | | |
| Document.enableStyleSheetsForSet() (deprecated) | | | | |
| Document.evaluate() | | | | |
| Document.execCommand() (deprecated) | | | | |
| Document.exitFullscreen() | | | | |
| Document.exitPictureInPicture() | | | | |
| Document.exitPointerLock() | | | | |
| Document.getAnimations() | | | | |
| Document.getElementById() | ✅ | resolved at compile time: a literal id is its element, and the Lua holds its number when the script keeps it as a value. An id from a fixed set (a constant, a ternary, a function's arguments at every call) or a fixed text around one value (`'row' + i`, `` `cell${i}` ``, the value from a loop counter's range or any value, matched against the page's ids) is looked up by that value in a table built once (`V_IDn[i]`, never an id built as a string); an operation on it picks the element's own code from a table (`V_Xn`). An id the compile cannot bound is refused | PlainTranslatorTests: every case; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "an element chosen at run time: an id built from a loop counter, an array of elements, an element passed to a function"; PlainTranslatorTests refusals: "an element chosen by an id the compile cannot bound" | |
| Document.getElementsByClassName() | ✅ | an HTMLCollection (below), found at compile time in the page as written, with the renderer's own selector engine, in document order; in the Lua an element is its number and a list a table of those numbers built once (`V_Ln`), so a script's own loops, conditions and calls work on them unchanged. A selector that tests a class or attribute the script changes (what it finds would change as the page runs), a state pseudo-class or a pseudo-element, and one matching an element drawn as part of its parent's text are refused | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "getElementsByClassName, getElementsByTagName and Element.querySelector, walked by for...of and an index"; PlainTranslatorTests refusals: "a selector testing a class the script changes", "forEach on an HTMLCollection" | |
| Document.getElementsByName() | | | | |
| Document.getElementsByTagName() | ✅ | an HTMLCollection (below), found at compile time in the page as written, with the renderer's own selector engine, in document order; in the Lua an element is its number and a list a table of those numbers built once (`V_Ln`), so a script's own loops, conditions and calls work on them unchanged. A selector that tests a class or attribute the script changes (what it finds would change as the page runs), a state pseudo-class or a pseudo-element, and one matching an element drawn as part of its parent's text are refused | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "getElementsByClassName, getElementsByTagName and Element.querySelector, walked by for...of and an index" | |
| Document.getElementsByTagNameNS() | | | | |
| Document.getSelection() | | | | |
| Document.hasFocus() | | | | |
| Document.hasPrivateToken() | | | | |
| Document.hasRedemptionRecord() | | | | |
| Document.hasStorageAccess() | | | | |
| Document.hasUnpartitionedCookieAccess() | | | | |
| Document.importNode() | | | | |
| Document.moveBefore() | | | | |
| Document.mozSetImageElement() (non-standard) | | | | |
| Document.open() | | | | |
| Document.prepend() | | | | |
| Document.queryCommandEnabled() (deprecated) | | | | |
| Document.queryCommandState() (deprecated) | | | | |
| Document.queryCommandSupported() (deprecated) | | | | |
| Document.querySelector() | ✅ | the first element the selector matches, found at compile time in the page as written, with the renderer's own selector engine, in document order; in the Lua an element is its number and a list a table of those numbers built once (`V_Ln`), so a script's own loops, conditions and calls work on them unchanged. A selector that tests a class or attribute the script changes (what it finds would change as the page runs), a state pseudo-class or a pseudo-element, and one matching an element drawn as part of its parent's text are refused; one that matches nothing, used as an element, is refused as a browser throws there | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "querySelector and querySelectorAll: a list's length, item(), an index and forEach"; PlainTranslatorTests refusals: "a selector matching text drawn inside its parent" | |
| Document.querySelectorAll() | ✅ | a NodeList (below), found at compile time in the page as written, with the renderer's own selector engine, in document order; in the Lua an element is its number and a list a table of those numbers built once (`V_Ln`), so a script's own loops, conditions and calls work on them unchanged. A selector that tests a class or attribute the script changes (what it finds would change as the page runs), a state pseudo-class or a pseudo-element, and one matching an element drawn as part of its parent's text are refused | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "querySelector and querySelectorAll: a list's length, item(), an index and forEach"; PlainTranslatorTests refusals: "a selector testing a class the script changes" | |
| Document.releaseCapture() (non-standard) | | | | |
| Document.replaceChildren() | | | | |
| Document.requestStorageAccess() | | | | |
| Document.requestStorageAccessFor() | | | | |
| Document.startViewTransition() | | | | |
| Document.write() (deprecated) | | | | |
| Document.writeln() (deprecated) | | | | |

#### Node

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Node.baseURI | | | | |
| Node.childNodes | | | | |
| Node.firstChild | | | | |
| Node.isConnected | | | | |
| Node.lastChild | | | | |
| Node.nextSibling | | | | |
| Node.nodeName | | | | |
| Node.nodeType | | | | |
| Node.nodeValue | | | | |
| Node.ownerDocument | | | | |
| Node.parentElement | | | | |
| Node.parentNode | | | | |
| Node.previousSibling | | | | |
| Node.textContent | ✅ | writes: a text placeholder `{$id_pN:%...}` (numbers) or a constant-string slot. Reads: a written label's text rebuilt from its slots as JavaScript prints them (`js_str`, toFixed); an element the script never writes, the constant text its source holds, whitespace as written. `+=` (and the other compound writes) is the text read back and written, every value worked out before any slot is set. Reading an element whose inner labels the script writes is refused for now | PlainTranslatorTests: toFixed and a template literal; text written in two shapes; plain-counter in game; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "reads: textContent, className, classList.contains and style answer what the script wrote"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerText read and written, textContent +=, and classList's item(), length and value" | |
| Node.ELEMENT_NODE (constant = 1) | | | | |
| Node.ATTRIBUTE_NODE (constant = 2) | | | | |
| Node.TEXT_NODE (constant = 3) | | | | |
| Node.CDATA_SECTION_NODE (constant = 4) | | | | |
| Node.PROCESSING_INSTRUCTION_NODE (constant = 7) | | | | |
| Node.COMMENT_NODE (constant = 8) | | | | |
| Node.DOCUMENT_NODE (constant = 9) | | | | |
| Node.DOCUMENT_TYPE_NODE (constant = 10) | | | | |
| Node.DOCUMENT_FRAGMENT_NODE (constant = 11) | | | | |
| Node.appendChild() | | | | |
| Node.cloneNode() | | | | |
| Node.compareDocumentPosition() | | | | |
| Node.contains() | | | | |
| Node.getRootNode() | | | | |
| Node.hasChildNodes() | | | | |
| Node.insertBefore() | | | | |
| Node.isDefaultNamespace() | | | | |
| Node.isEqualNode() | | | | |
| Node.isSameNode() (deprecated) | | | | |
| Node.lookupPrefix() | | | | |
| Node.lookupNamespaceURI() | | | | |
| Node.normalize() | | | | |
| Node.removeChild() | | | | |
| Node.replaceChild() | | | | |

#### ParentNode (mixin)

Note: MDN's dedicated `ParentNode` interface page returns HTTP 404 (confirmed independently twice, 2026-09-23 — once by WebFetch, once via a locale search that surfaced a French page filed under `/fr/docs/orphaned/Web/API/ParentNode`, indicating MDN folded this mixin's documentation into its implementing interfaces). Rows below are cross-checked directly against the live `Document` interface page's own member list, which independently confirms the same 10 members.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| ParentNode.children | | | | |
| ParentNode.firstElementChild | | | | |
| ParentNode.lastElementChild | | | | |
| ParentNode.childElementCount | | | | |
| ParentNode.append() | | | | |
| ParentNode.prepend() | | | | |
| ParentNode.replaceChildren() | | | | |
| ParentNode.moveBefore() | | | | |
| ParentNode.querySelector() | ✅ | as Document.querySelector() and Element.querySelector() | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "querySelector and querySelectorAll: a list's length, item(), an index and forEach"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "getElementsByClassName, getElementsByTagName and Element.querySelector, walked by for...of and an index" | |
| ParentNode.querySelectorAll() | ✅ | as Document.querySelectorAll() and Element.querySelectorAll() | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "querySelector and querySelectorAll: a list's length, item(), an index and forEach"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "getElementsByClassName, getElementsByTagName and Element.querySelector, walked by for...of and an index" | |

#### ChildNode (mixin)

Note: same orphaned-page caveat as ParentNode above.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| ChildNode.remove() | | | | |
| ChildNode.before() | | | | |
| ChildNode.after() | | | | |
| ChildNode.replaceWith() | | | | |

#### Element

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Element.activeViewTransition | | | | |
| Element.ariaActiveDescendantElement | | | | |
| Element.ariaAtomic | | | | |
| Element.ariaAutoComplete | | | | |
| Element.ariaBrailleLabel | | | | |
| Element.ariaBrailleRoleDescription | | | | |
| Element.ariaBusy | | | | |
| Element.ariaChecked | | | | |
| Element.ariaColCount | | | | |
| Element.ariaColIndex | | | | |
| Element.ariaColIndexText | | | | |
| Element.ariaColSpan | | | | |
| Element.ariaControlsElements | | | | |
| Element.ariaCurrent | | | | |
| Element.ariaDescription | | | | |
| Element.ariaDescribedByElements | | | | |
| Element.ariaDetailsElements | | | | |
| Element.ariaDisabled | | | | |
| Element.ariaErrorMessageElements | | | | |
| Element.ariaExpanded | | | | |
| Element.ariaFlowToElements | | | | |
| Element.ariaHasPopup | | | | |
| Element.ariaHidden | | | | |
| Element.ariaInvalid | | | | |
| Element.ariaKeyShortcuts | | | | |
| Element.ariaLabel | | | | |
| Element.ariaLabelledByElements | | | | |
| Element.ariaLevel | | | | |
| Element.ariaLive | | | | |
| Element.ariaModal | | | | |
| Element.ariaMultiLine | | | | |
| Element.ariaMultiSelectable | | | | |
| Element.ariaOrientation | | | | |
| Element.ariaOwnsElements | | | | |
| Element.ariaPlaceholder | | | | |
| Element.ariaPosInSet | | | | |
| Element.ariaPressed | | | | |
| Element.ariaReadOnly | | | | |
| Element.ariaRelevant | | | | |
| Element.ariaRequired | | | | |
| Element.ariaRoleDescription | | | | |
| Element.ariaRowCount | | | | |
| Element.ariaRowIndex | | | | |
| Element.ariaRowIndexText | | | | |
| Element.ariaRowSpan | | | | |
| Element.ariaSelected | | | | |
| Element.ariaSetSize | | | | |
| Element.ariaSort | | | | |
| Element.ariaValueMax | | | | |
| Element.ariaValueMin | | | | |
| Element.ariaValueNow | | | | |
| Element.ariaValueText | | | | |
| Element.assignedSlot | | | | |
| Element.attributes | | | | |
| Element.childElementCount | | | | |
| Element.children | | | | |
| Element.classList | ✅ | class states, every combination laid out at compile time (up to 6 classes, 64 states with the attributes CSS selects on), `v_class`. `contains`, `item`, `length`, `value`: see DOMTokenList; `replace`, `supports`, `forEach` and the iterators are refused for now | PlainTranslatorTests: addEventListener('click') ...; 09-transition in game; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "reads: textContent, className, classList.contains and style answer what the script wrote"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerText read and written, textContent +=, and classList's item(), length and value" | |
| Element.className | ✅ | writes from a fixed set: class states (`v_classname`). A value computed at run time is refused. Reads: the class attribute as the page wrote it until the script changes it, then its classes in the order a browser keeps them (`V_Cn.order`, `table.concat` at the read) | PlainTranslatorTests: a colour picked by a condition ... className; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "reads: textContent, className, classList.contains and style answer what the script wrote" | |
| Element.clientHeight | | | | |
| Element.clientLeft | | | | |
| Element.clientTop | | | | |
| Element.clientWidth | | | | |
| Element.currentCSSZoom | | | | |
| Element.customElementRegistry | | | | |
| Element.elementTiming | | | | |
| Element.firstElementChild | | | | |
| Element.id | ✅ | read: the id the page or markup wrote, "" for an element given none - a constant per element, through the dispatch tables for one chosen at run time. Writing it is refused | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "one listener on a list for every row: e.target, closest() and dataset pick the row a click lands in, the rows rewritten each time" | |
| Element.innerHTML (write; part 1: markup of a fixed shape into an element the script names) | ✅ | compiled once: the markup's string literals are structure and the values between them slots, so the Lua writes values and picks shapes and builds no string. Read from the source as a template - literals, `+`, template literals, a ternary between markup, a helper function returning markup (its parameters, constants, a parameter given a default, `if … return`), a constant holding markup - with each value in the scope it is written in. Refused by name: an element chosen at run time, more than 32 shapes, `&&`/`||` choosing markup, a tag or attribute name that is a value, a value in `id` or an `on…` attribute | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerHTML as rich text: a value in bold, a word shown by a ternary, a colour from a fixed set"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerHTML of elements from a helper function: text, a class and a style width from values"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerHTML choosing between two shapes, with listeners on the buttons each one makes"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerHTML written by a timer, the list as the page wrote it shown until then"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: plain-markup.lua (the in-game page: a parameter's default in a style width, a shape not shown at rest); PlainTranslatorTests refusals: "markup chosen with &&", "markup whose tag is a value" | |
| Element.innerHTML — markup of text and text-level tags (`b`, `i`, `span` with no id or class…) | ✅ | the element's rich text, as textContent is written: one text shape per markup shape, its values placeholders (`{$id_pN:%.1f}`) and its literals TextMeshPro rich text; an attribute value on such a tag (a colour) is a choice between its fixed values | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerHTML as rich text: a value in bold, a word shown by a ternary, a colour from a fixed set" | |
| Element.innerHTML — markup of elements | ✅ | the element built again around every shape's elements (a union laid out once by the renderer, so its markers, gaps and rows are the page's own); one shape shown at a time as a laid-out state (`v_state(V_SHn, k)`, each other shape's groups at `v=0`); a value in an element's text is its text write, in `style` a style write (a number scale+offset, a fixed-set value a state), in `class` a className write, in another attribute an attribute write - an attribute's text looked up whole from a table made once (`V_Jn[value]`). Refused for now: a value in text next to elements (a `<li>` beside its marker, text directly in the element), a classed inline tag next to a value | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerHTML of elements from a helper function: text, a class and a style width from values"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerHTML choosing between two shapes, with listeners on the buttons each one makes" | |
| Element.innerHTML — the elements markup makes, after the write | ✅ | elements of the page like any other: `getElementById`, a listener attached after the write (dropped when markup is written again: `v_unlisten`), attribute and dataset reads. A list of one list's rows (`querySelectorAll`, `getElementsBy…`), one element per row after elements that are always there, is a table of the rows' numbers made once whose length is set to the rows shown when it is looked up (`v_rows`); a selector may test a class every value `className` is given holds, which never changes; a listener per row goes through the dispatch tables. A list or first match over elements of only some shapes otherwise, and a write to an element markup makes (the next markup write would undo it), are refused for now | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerHTML choosing between two shapes, with listeners on the buttons each one makes"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a todo list from .map().join(''): template literals, the index in the markup, a class from each item, a listener per row found by [data-act], a push held to a cap, filter and splice; the rows found again by a class every row keeps"; PlainTranslatorTests refusals: "a list over markup that has more than one shape", "a write to an element markup makes" | |
| Element.innerHTML — what the element held as the page wrote it | ✅ | shown until the first write, as a state of its own, when no write runs while the page loads; a write that runs at load replaces it before a browser draws, so it is left out. Text shown that way next to the markup's elements is refused for now | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerHTML written by a timer, the list as the page wrote it shown until then" | |
| Element.innerHTML — a list repeated over an array (`.map(…).join('')`, a loop adding markup) | ✅ | every row the list can hold laid out once, each row's values slots of its own and no string built: the most rows the array can hold is read from the source - a literal, a constant array, every `push`/`unshift` held by an `if (a.length < N)` before it or a trim after it (`if (a.length > N) a.pop()` or `a.shift()`, `while (a.length > N) a.shift()`, `a.splice(0, a.length - N)`), `a.length = N`, an array made fresh and filled in loops over bounded lists, a slice of fixed size (bounding any array), `filter`/`concat`, a field of an object literal - followed as a name is, through every `x.field = …` and every change made to it wherever the object and the array go (a helper's parameter, `Object.assign` whose sources cannot hold that field, a field of another object literal it is stored in, `Object.assign(target, patch)` copying it on from each object the patch can be, followed as that object's own field), refused by name where the object goes where the compile cannot follow it - a helper's parameter or its `||` default. A state per length (`v_state(V_SHn, …)`): the rows past it hidden (`v=0`) and what follows moved up, combined with other markup whose lengths and shapes move the same things (one state per combination, each write keeping its own part, `V_Mn`). When the markup is written the Lua reads the array once, takes its slices as bounds (`v_slice`), tests its filters item by item into a row table built once (`V_Rn.i`), counts the rows (`V_Rn.n`) and writes each shown row's values as part 1 writes values (text, style, class, attribute). Forms: `.map(cb).join(sep)` with slices and filters before it, the callback written there or named, its item, index and `{ a, b }` parameters; `for…of`, `for (let i = 0; i < n; i++)` over an array's length or a count from a fixed set, `forEach`, a name given markup and then `+=` (at the write, or in a helper that returns it), with `const`s, `if`/`else` and `continue` in the body; a row that is all one ternary with nothing on one side is a filter. A choice the row's own item or index decides (`i > 0 ? … : ''`) is made at compile time for that row. A row whose markup is a ternary is a shape state of its own (`V_Rn.w[k]`), its shapes laid out in the row's place; a row of more than 8 shapes that way has each choice of elements drawn in place as every one of its shapes (gates) and is a state per choice (`V_Rn.g[j]`), so its shapes add up rather than multiply - choices moving the same things, and a choice drawing nothing at rest with any that changes size, laid out together, at most 8 combinations. A list over a constant array has one length; one a helper it is passed to takes items from is as short as that leaves it. Refused by name: a list whose length the compile cannot bound (it names what), text between a list's elements, a list of text-level markup, a list in a list's row or inside an attribute, rows whose shapes are different sizes, CSS picking elements by their place (`:last-child`, `:nth-child`, `:empty`) where a length or shape changes what it matches, more than 64 rows or 160 states, rows of more than 8 shapes with a choice of text or inside an attribute, or choices laid out together in more than 8 combinations | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a todo list from .map().join(''): template literals, the index in the markup, a class from each item, a listener per row found by [data-act], a push held to a cap, filter and splice; the rows found again by a class every row keeps"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a table of readings built in a for loop with +=: a constant per row, a colour from each value, text from a fixed array"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a log that grows to a cap, newest first, with a message while it is empty and a paragraph after it that moves"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "rows whose markup depends on their item, after a filter: each row its own shape, the rows after a filtered one moving up"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "markup added to the element with += in a for...of, a string built in a forEach with a class from the index, and a slice of a list that is never held to a length"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "one listener on a list for every row: e.target, closest() and dataset pick the row a click lands in, the rows rewritten each time"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a list inside a box that shows only while the list has rows, built by a helper that adds markup in steps; rows kept by a ternary that reads the index"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a list read from a field of an object, and a helper's list given as an argument or left to its default with ||"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a loop over a count from a fixed set, and a list filtered by a test that reads its index"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "lists bounded other ways: an array made fresh and filled in a loop over a bounded list, and a while trim"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "lists bounded other ways: a splice trim, and concat then slice"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "rows of a list of fixed length, each changing its shape with its item"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a list a helper takes items from: the rows it drops hidden, what follows moving up"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: plain-fields.lua (the in-game page, not yet seen in game); PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a log kept in a state object: newest first with concat, trimmed by a slice, the object patched through Object.assign and read back through the values a helper returns"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a list in a state object's field: pushed under a length test, filtered back into the field, an item's field flipped"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "rows with more than 8 shapes: an icon of three, a badge and a note chosen independently, a status colour, fields changed in a forEach and a row pushed under a cap"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "rows with more than 8 shapes whose note draws nothing at rest (written `!note ? '' : …`), after a badge that changes the row's size; a separator on every row but the first"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: plain-list.lua (the in-game page, not yet seen in game); PlainTranslatorTests refusals: "a list whose array grows with nothing holding it", "a list of elements joined with text between them", "a list of text-level markup", "a list inside a list's rows", "a list inside an attribute", "CSS picking a list's rows by their place", "rows whose shapes are different sizes", "a loop over a count only known at run time", "a list in an object's field that grows with nothing holding it", "a list in a field of an object handed where the compile cannot follow it", "a list stored in another object's field, where it is added to", "an array copied into an object's field by Object.assign, then added to there", "rows whose many choices all move each other" | |
| Element.innerHTML — `+=` | ✅ | `el.innerHTML = …` followed in the same block by `el.innerHTML += …` (in loops too, with declarations between): one markup write, its template the reset's and every step's, made after its last step; the steps emit nothing. Refused by name: `+=` outside such a sequence, which grows the markup without a bound the compile can see | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "markup added to the element with += in a for...of, a string built in a forEach with a class from the index, and a slice of a list that is never held to a length"; PlainTranslatorTests refusals: "markup added with += outside a loop" | |
| Element.innerHTML (read) | | | | |
| Element.lastElementChild | | | | |
| Element.localName | | | | |
| Element.namespaceURI | | | | |
| Element.nextElementSibling | | | | |
| Element.outerHTML | | | | |
| Element.part | | | | |
| Element.prefix | | | | |
| Element.previousElementSibling | | | | |
| Element.role | | | | |
| Element.scrollHeight | | | | |
| Element.scrollLeft | | | | |
| Element.scrollLeftMax (non-standard) | | | | |
| Element.scrollTop | | | | |
| Element.scrollTopMax (non-standard) | | | | |
| Element.scrollWidth | | | | |
| Element.shadowRoot | | | | |
| Element.slot | | | | |
| Element.tagName | ✅ | the tag in capitals, a constant per element (through the dispatch tables for one chosen at run time) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "one listener on a list for every row: e.target, closest() and dataset pick the row a click lands in, the rows rewritten each time" | |
| Element.after() | | | | |
| Element.animate() | | | | |
| Element.append() | | | | |
| Element.ariaNotify() | | | | |
| Element.attachShadow() | | | | |
| Element.before() | | | | |
| Element.checkVisibility() | | | | |
| Element.closest() | ✅ | with a selector the compile knows: for each element it can be called on, the nearest of it and its ancestors matching, in a table made once (`V_CLn[el]`, nil for none). Refused by name: a selector only known at run time, one asking about a state, one finding an element drawn as part of its parent | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "one listener on a list for every row: e.target, closest() and dataset pick the row a click lands in, the rows rewritten each time"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: plain-list.lua (the in-game page, not yet seen in game) | |
| Element.computedStyleMap() | | | | |
| Element.getAnimations() | | | | |
| Element.getAttribute() | ✅ | an attribute the script changes (or looks up by a run-time name): the program's table of the element's attributes (`v_attr(at[name])`, the page's own values to start); any other, the constant its source gives, or null. `class` and `style` through it are refused for now | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "setAttribute, removeAttribute, toggleAttribute and dataset on attributes CSS selects on"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "attributes nothing selects on: kept as the program's own values" | |
| Element.getAttributeNames() | | | | |
| Element.getAttributeNode() | | | | |
| Element.getAttributeNodeNS() | | | | |
| Element.getAttributeNS() | | | | |
| Element.getBoundingClientRect() | | | | |
| Element.getBoxQuads() (non-standard) | | | | |
| Element.getClientRects() | | | | |
| Element.getElementsByClassName() | ✅ | as the Document's, among the element's descendants; on an element chosen at run time (it would answer differently for each) refused for now | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "getElementsByClassName, getElementsByTagName and Element.querySelector, walked by for...of and an index" | |
| Element.getElementsByTagName() | ✅ | as the Document's, among the element's descendants; on an element chosen at run time (it would answer differently for each) refused for now | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "getElementsByClassName, getElementsByTagName and Element.querySelector, walked by for...of and an index" | |
| Element.getElementsByTagNameNS() | | | | |
| Element.getHTML() | | | | |
| Element.hasAttribute() | ✅ | as getAttribute: `(at[name] ~= nil)`, or a constant | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "setAttribute, removeAttribute, toggleAttribute and dataset on attributes CSS selects on"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "attributes nothing selects on: kept as the program's own values" | |
| Element.hasAttributeNS() | | | | |
| Element.hasAttributes() | | | | |
| Element.hasPointerCapture() | | | | |
| Element.insertAdjacentElement() | | | | |
| Element.insertAdjacentHTML() | | | | |
| Element.insertAdjacentText() | | | | |
| Element.matches() | ✅ | with a selector the compile knows: true or false per element, a constant (through the dispatch tables for one chosen at run time) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "one listener on a list for every row: e.target, closest() and dataset pick the row a click lands in, the rows rewritten each time" | |
| Element.moveBefore() | | | | |
| Element.prepend() | | | | |
| Element.pseudo() (experimental) | | | | |
| Element.querySelector() | ✅ | as the Document's, among the element's descendants; on an element chosen at run time (it would answer differently for each) refused for now | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "getElementsByClassName, getElementsByTagName and Element.querySelector, walked by for...of and an index" | |
| Element.querySelectorAll() | ✅ | as the Document's, among the element's descendants; on an element chosen at run time (it would answer differently for each) refused for now | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "getElementsByClassName, getElementsByTagName and Element.querySelector, walked by for...of and an index" | |
| Element.releasePointerCapture() | | | | |
| Element.remove() | | | | |
| Element.removeAttribute() | ✅ | as setAttribute: `v_rmattr`, absent being one of the laid-out values | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "setAttribute, removeAttribute, toggleAttribute and dataset on attributes CSS selects on"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "attributes nothing selects on: kept as the program's own values" | |
| Element.removeAttributeNode() | | | | |
| Element.removeAttributeNS() | | | | |
| Element.replaceChildren() | | | | |
| Element.replaceWith() | | | | |
| Element.requestFullscreen() | | | | |
| Element.requestPointerLock() | | | | |
| Element.scroll() | | | | |
| Element.scrollBy() | | | | |
| Element.scrollIntoView() | | | | |
| Element.scrollIntoViewIfNeeded() (non-standard) | | | | |
| Element.scrollTo() | | | | |
| Element.setAttribute() | ✅ | an attribute CSS selects on (`[data-x]`, `[hidden]`, `aria-*` in a selector) changes which rules match: every value the script can give it, from a fixed set, is laid out with the element's classes as one set of states (`v_setattr` then `v_class`); a value only known at run time is refused. Any other attribute (`data-*`, `aria-*`, `role`, `title`...) is the program's own value, for the script to read back. Attributes the page draws from directly (width, src, value, svg geometry...), `on*`, `class`, `style` and `id`, and a name only known at run time, are refused for now | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "setAttribute, removeAttribute, toggleAttribute and dataset on attributes CSS selects on"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "attributes nothing selects on: kept as the program's own values"; refusals "an attribute CSS selects on, set to a value known only at run time", "an attribute the page draws from directly", "setAttribute with a name known only at run time" | |
| Element.setAttributeNode() | | | | |
| Element.setAttributeNodeNS() | | | | |
| Element.setAttributeNS() | | | | |
| Element.setCapture() (non-standard) | | | | |
| Element.setHTML() | | | | |
| Element.setHTMLUnsafe() | | | | |
| Element.setPointerCapture() | | | | |
| Element.startViewTransition() | | | | |
| Element.toggleAttribute() | ✅ | as setAttribute: `v_toggleattr` (present as ""), returning whether it is now present | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "setAttribute, removeAttribute, toggleAttribute and dataset on attributes CSS selects on" | |

#### HTMLElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLElement.accessKey | | | | |
| HTMLElement.accessKeyLabel | | | | |
| HTMLElement.anchorElement | | | | |
| HTMLElement.attributeStyleMap | | | | |
| HTMLElement.autocapitalize | | | | |
| HTMLElement.autocorrect | | | | |
| HTMLElement.autofocus | | | | |
| HTMLElement.contentEditable | | | | |
| HTMLElement.dataset | ✅ | see DOMStringMap: each key is the attribute `data-<kebab-case key>`, through the attribute calls | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "setAttribute, removeAttribute, toggleAttribute and dataset on attributes CSS selects on"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "attributes nothing selects on: kept as the program's own values" | |
| HTMLElement.dir | | | | |
| HTMLElement.draggable | | | | |
| HTMLElement.editContext | | | | |
| HTMLElement.enterKeyHint | | | | |
| HTMLElement.hidden | ✅ | the browser's own `[hidden] { display: none }` as laid-out states: the element keeps its shapes in the scene inside `G v=$name_v` (0: not drawn and not clickable, as display: none), placed where each layout would put it shown, and what follows moves up as the browser moves it (`v_hidden`). Read: `(at.hidden ~= nil)`. Two elements whose hiding moves the same box (two toggled panels above one list) are refused for now, as two elements' classes are | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "hidden: the element leaves the layout and comes back, and one hidden from the start shows" | |
| HTMLElement.inert | | | | |
| HTMLElement.innerText | ✅ | reads: the text as laid out - each run of white space one space, a block's ends trimmed, `<br>` a line break, text-transform uppercase/lowercase applied: a constant for a text the script never writes, `v_collapse` (and `string.upper`/`lower`) over the text read back for one it does. Writes: as Node.textContent. An element holding other elements (the line breaks between its blocks), capitalize and the other transforms, and white-space that keeps spaces are refused for now | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerText read and written, textContent +=, and classList's item(), length and value" | |
| HTMLElement.inputMode | | | | |
| HTMLElement.isContentEditable | | | | |
| HTMLElement.lang | | | | |
| HTMLElement.nonce | | | | |
| HTMLElement.offsetHeight | | | | |
| HTMLElement.offsetLeft | | | | |
| HTMLElement.offsetParent | | | | |
| HTMLElement.offsetTop | | | | |
| HTMLElement.offsetWidth | | | | |
| HTMLElement.outerText | | | | |
| HTMLElement.popover | | | | |
| HTMLElement.spellcheck | | | | |
| HTMLElement.style | ✅ | numbers (px, %, opacity): a slot through scale+offset from the layout; values from a fixed set (colours, words): laid-out states. Writes that move other boxes or use other units are refused. Reads of `style.<property>`: the value last written, kept as written (`V_SVn`) and printed as the browser's inline style gives it back (a length to six figures with its unit, a hex colour as rgb()/rgba(), a keyword in lower case), else what the style attribute says, else "". A property whose shorthand or longhand is also set, and a value in another form, are refused for now | PlainTranslatorTests: setInterval + clearInterval; style.left in px and style.opacity; a colour picked by a condition; plain-counter in game; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "reads: textContent, className, classList.contains and style answer what the script wrote"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "reads: a colour and a keyword written from a fixed set come back as the browser prints them" | |
| HTMLElement.tabIndex | | | | |
| HTMLElement.title | | | | |
| HTMLElement.translate | | | | |
| HTMLElement.virtualKeyboardPolicy | | | | |
| HTMLElement.writingSuggestions | | | | |
| HTMLElement.attachInternals() | | | | |
| HTMLElement.blur() | | | | |
| HTMLElement.click() | | | | |
| HTMLElement.focus() | | | | |
| HTMLElement.hidePopover() | | | | |
| HTMLElement.showPopover() | | | | |
| HTMLElement.togglePopover() | | | | |

### 4.3 Per-element HTML interfaces

#### HTMLInputElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLInputElement.accept | | | | |
| HTMLInputElement.alpha | | | | |
| HTMLInputElement.alt | | | | |
| HTMLInputElement.autocomplete | | | | |
| HTMLInputElement.capture | | | | |
| HTMLInputElement.checked | | | | |
| HTMLInputElement.colorSpace | | | | |
| HTMLInputElement.defaultChecked | | | | |
| HTMLInputElement.defaultValue | | | | |
| HTMLInputElement.dirName | | | | |
| HTMLInputElement.disabled | | | | |
| HTMLInputElement.files | | | | |
| HTMLInputElement.form | | | | |
| HTMLInputElement.formAction | | | | |
| HTMLInputElement.formEnctype | | | | |
| HTMLInputElement.formMethod | | | | |
| HTMLInputElement.formNoValidate | | | | |
| HTMLInputElement.formTarget | | | | |
| HTMLInputElement.height | | | | |
| HTMLInputElement.incremental (non-standard) | | | | |
| HTMLInputElement.indeterminate | | | | |
| HTMLInputElement.labels | | | | |
| HTMLInputElement.list | | | | |
| HTMLInputElement.max | | | | |
| HTMLInputElement.maxLength | | | | |
| HTMLInputElement.min | | | | |
| HTMLInputElement.minLength | | | | |
| HTMLInputElement.multiple | | | | |
| HTMLInputElement.name | | | | |
| HTMLInputElement.pattern | | | | |
| HTMLInputElement.placeholder | | | | |
| HTMLInputElement.popoverTargetAction | | | | |
| HTMLInputElement.popoverTargetElement | | | | |
| HTMLInputElement.readOnly | | | | |
| HTMLInputElement.required | | | | |
| HTMLInputElement.selectionDirection | | | | |
| HTMLInputElement.selectionEnd | | | | |
| HTMLInputElement.selectionStart | | | | |
| HTMLInputElement.size | | | | |
| HTMLInputElement.src | | | | |
| HTMLInputElement.step | | | | |
| HTMLInputElement.type | | | | |
| HTMLInputElement.useMap (deprecated) | | | | |
| HTMLInputElement.validationMessage | | | | |
| HTMLInputElement.validity | | | | |
| HTMLInputElement.value | | | | |
| HTMLInputElement.valueAsDate | | | | |
| HTMLInputElement.valueAsNumber | | | | |
| HTMLInputElement.webkitdirectory | | | | |
| HTMLInputElement.webkitEntries | | | | |
| HTMLInputElement.willValidate | | | | |
| HTMLInputElement.checkValidity() | | | | |
| HTMLInputElement.reportValidity() | | | | |
| HTMLInputElement.select() | | | | |
| HTMLInputElement.setCustomValidity() | | | | |
| HTMLInputElement.setRangeText() | | | | |
| HTMLInputElement.setSelectionRange() | | | | |
| HTMLInputElement.showPicker() | | | | |
| HTMLInputElement.stepDown() | | | | |
| HTMLInputElement.stepUp() | | | | |

#### HTMLTextAreaElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLTextAreaElement.autocomplete | | | | |
| HTMLTextAreaElement.cols | | | | |
| HTMLTextAreaElement.defaultValue | | | | |
| HTMLTextAreaElement.dirName | | | | |
| HTMLTextAreaElement.disabled | | | | |
| HTMLTextAreaElement.form | | | | |
| HTMLTextAreaElement.labels | | | | |
| HTMLTextAreaElement.maxLength | | | | |
| HTMLTextAreaElement.minLength | | | | |
| HTMLTextAreaElement.name | | | | |
| HTMLTextAreaElement.placeholder | | | | |
| HTMLTextAreaElement.readOnly | | | | |
| HTMLTextAreaElement.required | | | | |
| HTMLTextAreaElement.rows | | | | |
| HTMLTextAreaElement.selectionDirection | | | | |
| HTMLTextAreaElement.selectionEnd | | | | |
| HTMLTextAreaElement.selectionStart | | | | |
| HTMLTextAreaElement.textLength | | | | |
| HTMLTextAreaElement.type | | | | |
| HTMLTextAreaElement.validationMessage | | | | |
| HTMLTextAreaElement.validity | | | | |
| HTMLTextAreaElement.value | | | | |
| HTMLTextAreaElement.willValidate | | | | |
| HTMLTextAreaElement.wrap | | | | |
| HTMLTextAreaElement.checkValidity() | | | | |
| HTMLTextAreaElement.reportValidity() | | | | |
| HTMLTextAreaElement.select() | | | | |
| HTMLTextAreaElement.setCustomValidity() | | | | |
| HTMLTextAreaElement.setRangeText() | | | | |
| HTMLTextAreaElement.setSelectionRange() | | | | |

#### HTMLSelectElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLSelectElement.autocomplete | | | | |
| HTMLSelectElement.disabled | | | | |
| HTMLSelectElement.form | | | | |
| HTMLSelectElement.labels | | | | |
| HTMLSelectElement.length | | | | |
| HTMLSelectElement.multiple | | | | |
| HTMLSelectElement.name | | | | |
| HTMLSelectElement.options | | | | |
| HTMLSelectElement.required | | | | |
| HTMLSelectElement.selectedIndex | | | | |
| HTMLSelectElement.selectedOptions | | | | |
| HTMLSelectElement.size | | | | |
| HTMLSelectElement.type | | | | |
| HTMLSelectElement.validationMessage | | | | |
| HTMLSelectElement.validity | | | | |
| HTMLSelectElement.value | | | | |
| HTMLSelectElement.willValidate | | | | |
| HTMLSelectElement.add() | | | | |
| HTMLSelectElement.checkValidity() | | | | |
| HTMLSelectElement.item() | | | | |
| HTMLSelectElement.namedItem() | | | | |
| HTMLSelectElement.remove() | | | | |
| HTMLSelectElement.reportValidity() | | | | |
| HTMLSelectElement.setCustomValidity() | | | | |
| HTMLSelectElement.showPicker() | | | | |

#### HTMLOptionElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLOptionElement.defaultSelected | | | | |
| HTMLOptionElement.disabled | | | | |
| HTMLOptionElement.form | | | | |
| HTMLOptionElement.index | | | | |
| HTMLOptionElement.label | | | | |
| HTMLOptionElement.selected | | | | |
| HTMLOptionElement.text | | | | |
| HTMLOptionElement.value | | | | |

#### HTMLOptGroupElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLOptGroupElement.disabled | | | | |
| HTMLOptGroupElement.label | | | | |

#### HTMLFormElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLFormElement.acceptCharset | | | | |
| HTMLFormElement.action | | | | |
| HTMLFormElement.autocomplete | | | | |
| HTMLFormElement.elements | | | | |
| HTMLFormElement.encoding | | | | |
| HTMLFormElement.enctype | | | | |
| HTMLFormElement.length | | | | |
| HTMLFormElement.method | | | | |
| HTMLFormElement.name | | | | |
| HTMLFormElement.noValidate | | | | |
| HTMLFormElement.rel | | | | |
| HTMLFormElement.relList | | | | |
| HTMLFormElement.target | | | | |
| HTMLFormElement.checkValidity() | | | | |
| HTMLFormElement.reportValidity() | | | | |
| HTMLFormElement.requestSubmit() | | | | |
| HTMLFormElement.reset() | | | | |
| HTMLFormElement.submit() | | | | |

#### HTMLButtonElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLButtonElement.command | | | | |
| HTMLButtonElement.commandForElement | | | | |
| HTMLButtonElement.disabled | | | | |
| HTMLButtonElement.form | | | | |
| HTMLButtonElement.formAction | | | | |
| HTMLButtonElement.formEnctype | | | | |
| HTMLButtonElement.formMethod | | | | |
| HTMLButtonElement.formNoValidate | | | | |
| HTMLButtonElement.formTarget | | | | |
| HTMLButtonElement.interestForElement | | | | |
| HTMLButtonElement.labels | | | | |
| HTMLButtonElement.name | | | | |
| HTMLButtonElement.popoverTargetAction | | | | |
| HTMLButtonElement.popoverTargetElement | | | | |
| HTMLButtonElement.type | | | | |
| HTMLButtonElement.validationMessage | | | | |
| HTMLButtonElement.validity | | | | |
| HTMLButtonElement.value | | | | |
| HTMLButtonElement.willValidate | | | | |
| HTMLButtonElement.checkValidity() | | | | |
| HTMLButtonElement.reportValidity() | | | | |
| HTMLButtonElement.setCustomValidity() | | | | |

#### HTMLLabelElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLLabelElement.control | | | | |
| HTMLLabelElement.form | | | | |
| HTMLLabelElement.htmlFor | | | | |

#### HTMLFieldSetElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLFieldSetElement.disabled | | | | |
| HTMLFieldSetElement.elements | | | | |
| HTMLFieldSetElement.form | | | | |
| HTMLFieldSetElement.name | | | | |
| HTMLFieldSetElement.type | | | | |
| HTMLFieldSetElement.validationMessage | | | | |
| HTMLFieldSetElement.validity | | | | |
| HTMLFieldSetElement.willValidate | | | | |
| HTMLFieldSetElement.checkValidity() | | | | |
| HTMLFieldSetElement.reportValidity() | | | | |
| HTMLFieldSetElement.setCustomValidity() | | | | |

#### HTMLLegendElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLLegendElement.align (deprecated) | | | | |
| HTMLLegendElement.form | | | | |

#### HTMLAnchorElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLAnchorElement.attributionSourceId (experimental) | | | | |
| HTMLAnchorElement.attributionSrc (experimental) | | | | |
| HTMLAnchorElement.download | | | | |
| HTMLAnchorElement.hash | | | | |
| HTMLAnchorElement.host | | | | |
| HTMLAnchorElement.hostname | | | | |
| HTMLAnchorElement.href | | | | |
| HTMLAnchorElement.hreflang | | | | |
| HTMLAnchorElement.interestForElement (experimental) | | | | |
| HTMLAnchorElement.origin | | | | |
| HTMLAnchorElement.password | | | | |
| HTMLAnchorElement.pathname | | | | |
| HTMLAnchorElement.ping | | | | |
| HTMLAnchorElement.port | | | | |
| HTMLAnchorElement.protocol | | | | |
| HTMLAnchorElement.referrerPolicy | | | | |
| HTMLAnchorElement.rel | | | | |
| HTMLAnchorElement.relList | | | | |
| HTMLAnchorElement.search | | | | |
| HTMLAnchorElement.target | | | | |
| HTMLAnchorElement.text | | | | |
| HTMLAnchorElement.type | | | | |
| HTMLAnchorElement.username | | | | |
| HTMLAnchorElement.toString() | | | | |

#### HTMLImageElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLImageElement.alt | | | | |
| HTMLImageElement.attributionSrc (experimental) | | | | |
| HTMLImageElement.complete | | | | |
| HTMLImageElement.crossOrigin | | | | |
| HTMLImageElement.currentSrc | | | | |
| HTMLImageElement.decoding | | | | |
| HTMLImageElement.fetchPriority | | | | |
| HTMLImageElement.height | | | | |
| HTMLImageElement.isMap | | | | |
| HTMLImageElement.loading | | | | |
| HTMLImageElement.naturalHeight | | | | |
| HTMLImageElement.naturalWidth | | | | |
| HTMLImageElement.referrerPolicy | | | | |
| HTMLImageElement.sizes | | | | |
| HTMLImageElement.src | | | | |
| HTMLImageElement.srcset | | | | |
| HTMLImageElement.useMap | | | | |
| HTMLImageElement.width | | | | |
| HTMLImageElement.x | | | | |
| HTMLImageElement.y | | | | |
| HTMLImageElement.decode() | | | | |

#### HTMLCanvasElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLCanvasElement.height | | | | |
| HTMLCanvasElement.width | | | | |
| HTMLCanvasElement.mozOpaque (non-standard) | | | | |
| HTMLCanvasElement.mozPrintCallback (non-standard) | | | | |
| HTMLCanvasElement.captureStream() | | | | |
| HTMLCanvasElement.getContext() | | | | |
| HTMLCanvasElement.toDataURL() | | | | |
| HTMLCanvasElement.toBlob() | | | | |
| HTMLCanvasElement.transferControlToOffscreen() | | | | |

#### HTMLVideoElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLVideoElement.disablePictureInPicture | | | | |
| HTMLVideoElement.height | | | | |
| HTMLVideoElement.poster | | | | |
| HTMLVideoElement.videoHeight | | | | |
| HTMLVideoElement.videoWidth | | | | |
| HTMLVideoElement.width | | | | |
| HTMLVideoElement.mozParsedFrames / mozDecodedFrames / mozPresentedFrames / mozPaintedFrames / mozFrameDelay / mozHasAudio (Firefox-specific); group, six same-vendor diagnostic properties | | | | |
| HTMLVideoElement.cancelVideoFrameCallback() | | | | |
| HTMLVideoElement.getVideoPlaybackQuality() | | | | |
| HTMLVideoElement.requestPictureInPicture() | | | | |
| HTMLVideoElement.requestVideoFrameCallback() | | | | |

#### HTMLAudioElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLAudioElement — adds no properties or methods beyond HTMLMediaElement; only its own `Audio()` constructor | | | | |

#### HTMLMediaElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLMediaElement.audioTracks | | | | |
| HTMLMediaElement.autoplay | | | | |
| HTMLMediaElement.buffered | | | | |
| HTMLMediaElement.controls | | | | |
| HTMLMediaElement.controlsList | | | | |
| HTMLMediaElement.crossOrigin | | | | |
| HTMLMediaElement.currentSrc | | | | |
| HTMLMediaElement.currentTime | | | | |
| HTMLMediaElement.defaultMuted | | | | |
| HTMLMediaElement.defaultPlaybackRate | | | | |
| HTMLMediaElement.disableRemotePlayback | | | | |
| HTMLMediaElement.duration | | | | |
| HTMLMediaElement.ended | | | | |
| HTMLMediaElement.error | | | | |
| HTMLMediaElement.loading (experimental) | | | | |
| HTMLMediaElement.loop | | | | |
| HTMLMediaElement.mediaKeys | | | | |
| HTMLMediaElement.muted | | | | |
| HTMLMediaElement.networkState | | | | |
| HTMLMediaElement.paused | | | | |
| HTMLMediaElement.playbackRate | | | | |
| HTMLMediaElement.played | | | | |
| HTMLMediaElement.preload | | | | |
| HTMLMediaElement.preservesPitch | | | | |
| HTMLMediaElement.readyState | | | | |
| HTMLMediaElement.remote | | | | |
| HTMLMediaElement.seekable | | | | |
| HTMLMediaElement.seeking | | | | |
| HTMLMediaElement.sinkId | | | | |
| HTMLMediaElement.src | | | | |
| HTMLMediaElement.srcObject | | | | |
| HTMLMediaElement.textTracks | | | | |
| HTMLMediaElement.videoTracks | | | | |
| HTMLMediaElement.volume | | | | |
| HTMLMediaElement.addTextTrack() | | | | |
| HTMLMediaElement.canPlayType() | | | | |
| HTMLMediaElement.captureStream() | | | | |
| HTMLMediaElement.fastSeek() | | | | |
| HTMLMediaElement.getStartDate() | | | | |
| HTMLMediaElement.load() | | | | |
| HTMLMediaElement.pause() | | | | |
| HTMLMediaElement.play() | | | | |
| HTMLMediaElement.seekToNextFrame() (non-standard) | | | | |
| HTMLMediaElement.setMediaKeys() | | | | |
| HTMLMediaElement.setSinkId() | | | | |

#### HTMLTableElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLTableElement.caption | | | | |
| HTMLTableElement.tHead | | | | |
| HTMLTableElement.tFoot | | | | |
| HTMLTableElement.rows | | | | |
| HTMLTableElement.tBodies | | | | |
| HTMLTableElement.align (obsolete) | | | | |
| HTMLTableElement.bgColor (obsolete) | | | | |
| HTMLTableElement.border (obsolete) | | | | |
| HTMLTableElement.cellPadding (obsolete) | | | | |
| HTMLTableElement.cellSpacing (obsolete) | | | | |
| HTMLTableElement.frame (obsolete) | | | | |
| HTMLTableElement.rules (obsolete) | | | | |
| HTMLTableElement.summary (obsolete) | | | | |
| HTMLTableElement.width (obsolete) | | | | |
| HTMLTableElement.createTHead() | | | | |
| HTMLTableElement.deleteTHead() | | | | |
| HTMLTableElement.createTFoot() | | | | |
| HTMLTableElement.deleteTFoot() | | | | |
| HTMLTableElement.createTBody() | | | | |
| HTMLTableElement.createCaption() | | | | |
| HTMLTableElement.deleteCaption() | | | | |
| HTMLTableElement.insertRow() | | | | |
| HTMLTableElement.deleteRow() | | | | |

#### HTMLTableRowElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLTableRowElement.cells | | | | |
| HTMLTableRowElement.rowIndex | | | | |
| HTMLTableRowElement.sectionRowIndex | | | | |
| HTMLTableRowElement.deleteCell() | | | | |
| HTMLTableRowElement.insertCell() | | | | |

#### HTMLTableCellElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLTableCellElement.abbr | | | | |
| HTMLTableCellElement.cellIndex | | | | |
| HTMLTableCellElement.colSpan | | | | |
| HTMLTableCellElement.headers | | | | |
| HTMLTableCellElement.rowSpan | | | | |
| HTMLTableCellElement.scope | | | | |
| HTMLTableCellElement.align (obsolete) | | | | |
| HTMLTableCellElement.axis (obsolete) | | | | |
| HTMLTableCellElement.bgColor (obsolete) | | | | |
| HTMLTableCellElement.ch (obsolete) | | | | |
| HTMLTableCellElement.chOff (obsolete) | | | | |
| HTMLTableCellElement.height (obsolete) | | | | |
| HTMLTableCellElement.noWrap (obsolete) | | | | |
| HTMLTableCellElement.vAlign (obsolete) | | | | |
| HTMLTableCellElement.width (obsolete) | | | | |

#### HTMLTableSectionElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLTableSectionElement.align (obsolete) | | | | |
| HTMLTableSectionElement.rows | | | | |
| HTMLTableSectionElement.ch (obsolete) | | | | |
| HTMLTableSectionElement.chOff (obsolete) | | | | |
| HTMLTableSectionElement.vAlign (obsolete) | | | | |
| HTMLTableSectionElement.deleteRow() | | | | |
| HTMLTableSectionElement.insertRow() | | | | |

#### HTMLProgressElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLProgressElement.max | | | | |
| HTMLProgressElement.position | | | | |
| HTMLProgressElement.value | | | | |
| HTMLProgressElement.labels | | | | |

#### HTMLMeterElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLMeterElement.high | | | | |
| HTMLMeterElement.low | | | | |
| HTMLMeterElement.max | | | | |
| HTMLMeterElement.min | | | | |
| HTMLMeterElement.optimum | | | | |
| HTMLMeterElement.value | | | | |
| HTMLMeterElement.labels | | | | |

#### HTMLDetailsElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLDetailsElement.name | | | | |
| HTMLDetailsElement.open | | | | |

#### HTMLDialogElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLDialogElement.closedBy (experimental) | | | | |
| HTMLDialogElement.open | | | | |
| HTMLDialogElement.returnValue | | | | |
| HTMLDialogElement.close() | | | | |
| HTMLDialogElement.requestClose() (experimental) | | | | |
| HTMLDialogElement.show() | | | | |
| HTMLDialogElement.showModal() | | | | |

#### HTMLOutputElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLOutputElement.defaultValue | | | | |
| HTMLOutputElement.form | | | | |
| HTMLOutputElement.htmlFor | | | | |
| HTMLOutputElement.labels | | | | |
| HTMLOutputElement.name | | | | |
| HTMLOutputElement.type | | | | |
| HTMLOutputElement.validationMessage | | | | |
| HTMLOutputElement.validity | | | | |
| HTMLOutputElement.value | | | | |
| HTMLOutputElement.willValidate | | | | |
| HTMLOutputElement.checkValidity() | | | | |
| HTMLOutputElement.reportValidity() | | | | |
| HTMLOutputElement.setCustomValidity() | | | | |

#### HTMLTemplateElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLTemplateElement.content | | | | |
| HTMLTemplateElement.htmlFor (experimental) | | | | |
| HTMLTemplateElement.shadowRootMode (experimental) | | | | |
| HTMLTemplateElement.shadowRootDelegatesFocus (experimental) | | | | |
| HTMLTemplateElement.shadowRootClonable (experimental) | | | | |
| HTMLTemplateElement.shadowRootCustomElementRegistry (experimental) | | | | |
| HTMLTemplateElement.shadowRootSerializable (experimental) | | | | |
| HTMLTemplateElement.shadowRootSlotAssignment (experimental) | | | | |

#### HTMLSlotElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLSlotElement.name | | | | |
| HTMLSlotElement.assign() | | | | |
| HTMLSlotElement.assignedNodes() | | | | |
| HTMLSlotElement.assignedElements() | | | | |

#### HTMLOListElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLOListElement.reversed | | | | |
| HTMLOListElement.start | | | | |
| HTMLOListElement.type | | | | |
| HTMLOListElement.compact (obsolete) | | | | |

#### HTMLUListElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLUListElement.type (obsolete) | | | | |
| HTMLUListElement.compact (obsolete) | | | | |

#### HTMLLIElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLLIElement.type | | | | |
| HTMLLIElement.value | | | | |

#### HTMLIFrameElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLIFrameElement.align (deprecated) | | | | |
| HTMLIFrameElement.allow | | | | |
| HTMLIFrameElement.allowFullscreen (non-standard) | | | | |
| HTMLIFrameElement.allowPaymentRequest (deprecated) | | | | |
| HTMLIFrameElement.browsingTopics (experimental) | | | | |
| HTMLIFrameElement.contentDocument | | | | |
| HTMLIFrameElement.contentWindow | | | | |
| HTMLIFrameElement.credentialless (experimental) | | | | |
| HTMLIFrameElement.csp (non-standard) | | | | |
| HTMLIFrameElement.featurePolicy (non-standard) | | | | |
| HTMLIFrameElement.frameBorder (deprecated) | | | | |
| HTMLIFrameElement.height | | | | |
| HTMLIFrameElement.loading | | | | |
| HTMLIFrameElement.longDesc (deprecated) | | | | |
| HTMLIFrameElement.marginHeight (deprecated) | | | | |
| HTMLIFrameElement.marginWidth (deprecated) | | | | |
| HTMLIFrameElement.name | | | | |
| HTMLIFrameElement.privateToken (experimental) | | | | |
| HTMLIFrameElement.referrerPolicy | | | | |
| HTMLIFrameElement.sandbox | | | | |
| HTMLIFrameElement.scrolling (deprecated) | | | | |
| HTMLIFrameElement.src | | | | |
| HTMLIFrameElement.srcdoc | | | | |
| HTMLIFrameElement.width | | | | |
| HTMLIFrameElement.getSVGDocument() (deprecated) | | | | |

#### HTMLScriptElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLScriptElement.attributionSrc (experimental) | | | | |
| HTMLScriptElement.async | | | | |
| HTMLScriptElement.blocking (experimental) | | | | |
| HTMLScriptElement.charset (deprecated) | | | | |
| HTMLScriptElement.crossOrigin | | | | |
| HTMLScriptElement.defer | | | | |
| HTMLScriptElement.event (deprecated) | | | | |
| HTMLScriptElement.fetchPriority | | | | |
| HTMLScriptElement.integrity | | | | |
| HTMLScriptElement.noModule | | | | |
| HTMLScriptElement.referrerPolicy | | | | |
| HTMLScriptElement.src | | | | |
| HTMLScriptElement.text | | | | |
| HTMLScriptElement.type | | | | |
| HTMLScriptElement.supports() (static) | | | | |

#### HTMLStyleElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLStyleElement.blocking (experimental) | | | | |
| HTMLStyleElement.media | | | | |
| HTMLStyleElement.type (deprecated) | | | | |
| HTMLStyleElement.disabled | | | | |
| HTMLStyleElement.sheet | | | | |

#### HTMLLinkElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLLinkElement.as | | | | |
| HTMLLinkElement.blocking (experimental) | | | | |
| HTMLLinkElement.crossOrigin | | | | |
| HTMLLinkElement.disabled | | | | |
| HTMLLinkElement.fetchPriority | | | | |
| HTMLLinkElement.href | | | | |
| HTMLLinkElement.hreflang | | | | |
| HTMLLinkElement.imageSizes | | | | |
| HTMLLinkElement.imageSrcset | | | | |
| HTMLLinkElement.integrity | | | | |
| HTMLLinkElement.media | | | | |
| HTMLLinkElement.referrerPolicy | | | | |
| HTMLLinkElement.rel | | | | |
| HTMLLinkElement.relList | | | | |
| HTMLLinkElement.sheet | | | | |
| HTMLLinkElement.sizes | | | | |
| HTMLLinkElement.type | | | | |
| HTMLLinkElement.charset (obsolete) | | | | |
| HTMLLinkElement.rev (obsolete) | | | | |
| HTMLLinkElement.target (obsolete) | | | | |

#### Low-signal HTML element interfaces (grouped — each adds almost nothing beyond HTMLElement)

Grouped as instructed: 25 interfaces whose entire member surface is 0–3 items. Each row names the interface and its full addition inline.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLQuoteElement — adds only `cite` | | | | |
| HTMLModElement — adds only `cite`, `dateTime` | | | | |
| HTMLTimeElement — adds only `dateTime` | | | | |
| HTMLDataElement — adds only `value` | | | | |
| HTMLMapElement — adds only `name`, `areas` | | | | |
| HTMLAreaElement — adds `alt`, `coords`, `download`, `hash`, `host`, `hostname`, `href`, `interestForElement`, `noHref` (obsolete), `origin`, `password`, `pathname`, `ping`, `port`, `protocol`, `referrerPolicy`, `rel`, `relList`, `search`, `shape`, `target`, `username` (URL-decomposition twin of HTMLAnchorElement) | | | | |
| HTMLSourceElement — adds only `height`, `media`, `sizes`, `src`, `srcset`, `type`, `width` | | | | |
| HTMLTrackElement — adds only `kind`, `src`, `srclang`, `label`, `default`, `readyState` (+ NONE/LOADING/LOADED/ERROR constants), `track` | | | | |
| HTMLPictureElement — adds nothing beyond HTMLElement | | | | |
| HTMLBaseElement — adds only `href`, `target` | | | | |
| HTMLMetaElement — adds only `charset`, `content`, `httpEquiv`, `media`, `name`, `scheme` (deprecated) | | | | |
| HTMLTitleElement — adds only `text` | | | | |
| HTMLHeadElement — adds nothing beyond HTMLElement | | | | |
| HTMLBodyElement — adds only `aLink`, `background`, `bgColor`, `link`, `text`, `vLink` (all deprecated colour/legacy attributes; its long list of `on*` window-event handler IDL attributes is out of this section's scope — events) | | | | |
| HTMLHtmlElement — adds only `version` (deprecated) | | | | |
| HTMLHRElement — adds only `align`, `color`, `noShade`, `size`, `width` (all deprecated presentational attributes) | | | | |
| HTMLBRElement — adds only `clear` (deprecated) | | | | |
| HTMLPreElement — adds only `width` (obsolete) | | | | |
| HTMLSpanElement — adds nothing beyond HTMLElement | | | | |
| HTMLDivElement — adds only `align` (deprecated) | | | | |
| HTMLParagraphElement — adds only `align` (deprecated) | | | | |
| HTMLHeadingElement (H1–H6) — adds only `align` (deprecated) | | | | |
| HTMLEmbedElement — adds `align` (deprecated), `height`, `name`, `src`, `type`, `width`, plus `getSVGDocument()` | | | | |
| HTMLObjectElement — adds `align` (deprecated), `archive`, `border`, `code`, `codeBase`, `codeType`, `contentDocument`, `contentWindow`, `data`, `declare` (obsolete), `form`, `height`, `hspace` (deprecated), `name`, `standby` (obsolete), `type`, `useMap`, `validationMessage`, `validity`, `vspace` (deprecated), `width`, `willValidate`, plus `checkValidity()`, `getSVGDocument()`, `reportValidity()`, `setCustomValidity()` | | | | |
| HTMLUnknownElement — adds nothing beyond HTMLElement; represents any unrecognised HTML tag | | | | |

### 4.4 CharacterData, Text, CSSStyleDeclaration, DOMTokenList, DOMStringMap, NamedNodeMap, Attr

#### CharacterData

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| CharacterData.data | | | | |
| CharacterData.length | | | | |
| CharacterData.nextElementSibling | | | | |
| CharacterData.previousElementSibling | | | | |
| CharacterData.after() | | | | |
| CharacterData.appendData() | | | | |
| CharacterData.before() | | | | |
| CharacterData.deleteData() | | | | |
| CharacterData.insertData() | | | | |
| CharacterData.remove() | | | | |
| CharacterData.replaceData() | | | | |
| CharacterData.replaceWith() | | | | |
| CharacterData.substringData() | | | | |

#### Text

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Text.assignedSlot | | | | |
| Text.wholeText | | | | |
| Text.splitText() | | | | |

#### CSSStyleDeclaration

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| CSSStyleDeclaration.cssText | | | | |
| CSSStyleDeclaration.length | | | | |
| CSSStyleDeclaration.parentRule | | | | |
| CSSStyleDeclaration.cssFloat | | | | |
| CSSStyleDeclaration.getPropertyValue() | | | | |
| CSSStyleDeclaration.getPropertyPriority() | | | | |
| CSSStyleDeclaration.item() | | | | |
| CSSStyleDeclaration.removeProperty() | | | | |
| CSSStyleDeclaration.setProperty() | | | | |
| CSSStyleDeclaration.getPropertyCSSValue() (deprecated) | | | | |

Note: this table covers the interface's own generic members only (indexed access, cssText parsing, get/set/remove by property name) — not the ~300 individual named CSS-property accessors (`style.color`, `style.width`, …), which belong to the CSS properties/values enumeration (INSTRUCTION-SET-CSS.md), out of this DOM section's scope.

#### DOMTokenList

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| DOMTokenList.length | ✅ | the classes the element has now: `v_clscount(V_Cn.on)`, or a constant for one whose classes the script never changes | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerText read and written, textContent +=, and classList's item(), length and value" | |
| DOMTokenList.value | ✅ | as Element.className: read, the class attribute in the order a browser keeps it; written, a value from a fixed set laid out as class states | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerText read and written, textContent +=, and classList's item(), length and value" | |
| DOMTokenList.item() | ✅ | the class at that place in the order the attribute holds them (`v_clsitem(V_Cn.order, i)`, the order kept as classes change), or a constant; null past the end | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerText read and written, textContent +=, and classList's item(), length and value" | |
| DOMTokenList.contains() | ✅ | as `Element.classList`: `(V_Cn.on[name] == true)`; an element whose classes the script never changes answers from the page's source (a constant for a literal name) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "reads: textContent, className, classList.contains and style answer what the script wrote" | |
| DOMTokenList.add() | ✅ | as `Element.classList` | PlainTranslatorTests: 09-transition; the script's own logic ... | |
| DOMTokenList.remove() | ✅ | as `Element.classList` | PlainTranslatorTests: the script's own logic ... | |
| DOMTokenList.replace() | | | | |
| DOMTokenList.supports() | | | | |
| DOMTokenList.toggle() | ✅ | as `Element.classList` | PlainTranslatorTests: addEventListener('click') ... | |
| DOMTokenList.entries() | | | | |
| DOMTokenList.forEach() | | | | |
| DOMTokenList.keys() | | | | |
| DOMTokenList.toString() | | | | |
| DOMTokenList.values() | | | | |

#### NodeList

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| NodeList.length | ✅ | the list's `length` field (`js_len`), fixed at compile time | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "querySelector and querySelectorAll: a list's length, item(), an index and forEach" | |
| NodeList.item() | ✅ | `v_item(list, i)`: i as a whole number, null past the end | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "querySelector and querySelectorAll: a list's length, item(), an index and forEach" | |
| NodeList.forEach() | ✅ | the prelude's Array forEach over the table of element numbers; the callback's element is one of the list, and what it does with it is picked from a table (`V_Xn[el]`) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "querySelector and querySelectorAll: a list's length, item(), an index and forEach" | |
| NodeList indexed access (`list[i]`) | ✅ | the table indexed as written: a literal index is that element, any other one of the list (or undefined past the end) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "querySelector and querySelectorAll: a list's length, item(), an index and forEach"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "getElementsByClassName, getElementsByTagName and Element.querySelector, walked by for...of and an index" | |
| NodeList iteration (`for...of`) | ✅ | a numeric loop over the table | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "querySelector and querySelectorAll: a list's length, item(), an index and forEach" | |
| NodeList.entries() | | | | |
| NodeList.keys() | | | | |
| NodeList.values() | | | | |

#### HTMLCollection

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| HTMLCollection.length | ✅ | as NodeList.length. A live collection: one whose selector tests a class or attribute the script changes is refused (see Document.getElementsByClassName) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "getElementsByClassName, getElementsByTagName and Element.querySelector, walked by for...of and an index" | |
| HTMLCollection.item() | ✅ | as NodeList.item() | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "getElementsByClassName, getElementsByTagName and Element.querySelector, walked by for...of and an index" | |
| HTMLCollection.namedItem() | | | | |
| HTMLCollection indexed access (`coll[i]`) | ✅ | as NodeList | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "getElementsByClassName, getElementsByTagName and Element.querySelector, walked by for...of and an index" | |
| HTMLCollection iteration (`for...of`) | ✅ | as NodeList; forEach, which an HTMLCollection does not have in a browser either, is refused by name | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "getElementsByClassName, getElementsByTagName and Element.querySelector, walked by for...of and an index"; PlainTranslatorTests refusals: "forEach on an HTMLCollection" | |

#### DOMStringMap

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| DOMStringMap — dynamic named-property access only (`dataset.someName` getter/setter/deleter mapping to `data-some-name`); the spec defines no fixed member list, so this is grouped as a single indexed-access feature rather than enumerated | ✅ | `el.dataset.fooBar` is the attribute `data-foo-bar`: read, write, `delete` and `'fooBar' in el.dataset`, each as the attribute calls (Element.setAttribute); a key only known at run time, and the map as a value, are refused for now | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "setAttribute, removeAttribute, toggleAttribute and dataset on attributes CSS selects on"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "attributes nothing selects on: kept as the program's own values" | |

#### NamedNodeMap

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| NamedNodeMap.length | | | | |
| NamedNodeMap.getNamedItem() | | | | |
| NamedNodeMap.setNamedItem() | | | | |
| NamedNodeMap.removeNamedItem() | | | | |
| NamedNodeMap.item() | | | | |
| NamedNodeMap.getNamedItemNS() | | | | |
| NamedNodeMap.setNamedItemNS() | | | | |
| NamedNodeMap.removeNamedItemNS() | | | | |

#### Attr

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Attr.localName | | | | |
| Attr.name | | | | |
| Attr.namespaceURI | | | | |
| Attr.ownerElement | | | | |
| Attr.prefix | | | | |
| Attr.specified (deprecated) | | | | |
| Attr.value | | | | |

### 4.5 Events, Canvas, SVG DOM, Web Animations, CSSOM View, Observers, URL/binary, and the rest of the Web API surface

#### EventTarget

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| EventTarget.addEventListener() | ✅ | 'click' only: `v_listen`, the element becomes a hit region on the scene's `on_click`. Other event types are refused (the vector mod delivers clicks only) | PlainTranslatorTests: addEventListener('click') ... | |
| EventTarget.removeEventListener() | | | | |
| EventTarget.dispatchEvent() | | | | |

#### Event

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Event() constructor | | | | |
| Event.type | | | | |
| Event.target | ✅ | the element a click lands on, by number, in one event table the click hands every listener (`V_EV.target`, reused: nothing is made per click). Every element inside a listening element becomes a hit region of its own so the click names it; what the script does with the target is resolved for each element it can be. A handler reading its event other than as `target` and `currentTarget` is refused by name | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "one listener on a list for every row: e.target, closest() and dataset pick the row a click lands in, the rows rewritten each time"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: plain-list.lua (the in-game page, not yet seen in game) | |
| Event.currentTarget | ✅ | the listening element, by number (`V_EV.currentTarget`), set for each element the click reaches as it bubbles | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "one listener on a list for every row: e.target, closest() and dataset pick the row a click lands in, the rows rewritten each time" | |
| Event.bubbles | | | | |
| Event.cancelable | | | | |
| Event.defaultPrevented | | | | |
| Event.eventPhase | | | | |
| Event.isTrusted | | | | |
| Event.timeStamp | | | | |
| Event.composed | | | | |
| Event.srcElement (legacy alias for target) | | | | |
| Event.returnValue (legacy, use preventDefault/defaultPrevented) | | | | |
| Event.cancelBubble (legacy alias for stopPropagation) | | | | |
| Event.preventDefault() | | | | |
| Event.stopPropagation() | | | | |
| Event.stopImmediatePropagation() | | | | |
| Event.composedPath() | | | | |
| Event.initEvent() (deprecated) | | | | |
| Event.NONE (constant, eventPhase = 0) | | | | |
| Event.CAPTURING_PHASE (constant, eventPhase = 1) | | | | |
| Event.AT_TARGET (constant, eventPhase = 2) | | | | |
| Event.BUBBLING_PHASE (constant, eventPhase = 3) | | | | |

#### CustomEvent

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| CustomEvent() constructor | | | | |
| CustomEvent.detail | | | | |
| CustomEvent.initCustomEvent() (deprecated) | | | | |

#### MouseEvent

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| MouseEvent() constructor | | | | |
| MouseEvent.altKey | | | | |
| MouseEvent.button | | | | |
| MouseEvent.buttons | | | | |
| MouseEvent.clientX | | | | |
| MouseEvent.clientY | | | | |
| MouseEvent.ctrlKey | | | | |
| MouseEvent.layerX (non-standard) | | | | |
| MouseEvent.layerY (non-standard) | | | | |
| MouseEvent.metaKey | | | | |
| MouseEvent.movementX | | | | |
| MouseEvent.movementY | | | | |
| MouseEvent.offsetX | | | | |
| MouseEvent.offsetY | | | | |
| MouseEvent.pageX | | | | |
| MouseEvent.pageY | | | | |
| MouseEvent.relatedTarget | | | | |
| MouseEvent.screenX | | | | |
| MouseEvent.screenY | | | | |
| MouseEvent.shiftKey | | | | |
| MouseEvent.x (alias for clientX) | | | | |
| MouseEvent.y (alias for clientY) | | | | |
| MouseEvent.mozInputSource (non-standard) | | | | |
| MouseEvent.webkitForce (non-standard) | | | | |
| MouseEvent.getModifierState() | | | | |
| MouseEvent.initMouseEvent() (deprecated) | | | | |

#### PointerEvent

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| PointerEvent() constructor | | | | |
| PointerEvent.pointerId | | | | |
| PointerEvent.width | | | | |
| PointerEvent.height | | | | |
| PointerEvent.pressure | | | | |
| PointerEvent.tangentialPressure | | | | |
| PointerEvent.tiltX | | | | |
| PointerEvent.tiltY | | | | |
| PointerEvent.twist | | | | |
| PointerEvent.altitudeAngle | | | | |
| PointerEvent.azimuthAngle | | | | |
| PointerEvent.pointerType | | | | |
| PointerEvent.isPrimary | | | | |
| PointerEvent.persistentDeviceId | | | | |
| PointerEvent.getCoalescedEvents() (secure context) | | | | |
| PointerEvent.getPredictedEvents() | | | | |

#### KeyboardEvent

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| KeyboardEvent() constructor | | | | |
| KeyboardEvent.altKey | | | | |
| KeyboardEvent.code | | | | |
| KeyboardEvent.ctrlKey | | | | |
| KeyboardEvent.isComposing | | | | |
| KeyboardEvent.key | | | | |
| KeyboardEvent.location | | | | |
| KeyboardEvent.metaKey | | | | |
| KeyboardEvent.repeat | | | | |
| KeyboardEvent.shiftKey | | | | |
| KeyboardEvent.getModifierState() | | | | |
| KeyboardEvent.charCode (deprecated) | | | | |
| KeyboardEvent.keyCode (deprecated) | | | | |
| KeyboardEvent.keyIdentifier (non-standard, deprecated) | | | | |
| KeyboardEvent.DOM_KEY_LOCATION_STANDARD (constant, 0x00) | | | | |
| KeyboardEvent.DOM_KEY_LOCATION_LEFT (constant, 0x01) | | | | |
| KeyboardEvent.DOM_KEY_LOCATION_RIGHT (constant, 0x02) | | | | |
| KeyboardEvent.DOM_KEY_LOCATION_NUMPAD (constant, 0x03) | | | | |

#### WheelEvent

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| WheelEvent() constructor | | | | |
| WheelEvent.deltaX | | | | |
| WheelEvent.deltaY | | | | |
| WheelEvent.deltaZ | | | | |
| WheelEvent.deltaMode | | | | |
| WheelEvent.wheelDelta (deprecated, non-standard) | | | | |
| WheelEvent.wheelDeltaX (deprecated, non-standard) | | | | |
| WheelEvent.wheelDeltaY (deprecated, non-standard) | | | | |
| WheelEvent.DOM_DELTA_PIXEL (constant, 0x00) | | | | |
| WheelEvent.DOM_DELTA_LINE (constant, 0x01) | | | | |
| WheelEvent.DOM_DELTA_PAGE (constant, 0x02) | | | | |

#### FocusEvent

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| FocusEvent() constructor | | | | |
| FocusEvent.relatedTarget | | | | |

#### InputEvent

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| InputEvent() constructor | | | | |
| InputEvent.data | | | | |
| InputEvent.dataTransfer | | | | |
| InputEvent.inputType | | | | |
| InputEvent.isComposing | | | | |
| InputEvent.getTargetRanges() | | | | |

#### TouchEvent

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| TouchEvent() constructor | | | | |
| TouchEvent.altKey | | | | |
| TouchEvent.changedTouches | | | | |
| TouchEvent.ctrlKey | | | | |
| TouchEvent.metaKey | | | | |
| TouchEvent.shiftKey | | | | |
| TouchEvent.targetTouches | | | | |
| TouchEvent.touches | | | | |
| TouchEvent.rotation (non-standard, Safari) | | | | |
| TouchEvent.scale (non-standard, Safari) | | | | |
| Touch (related interface: identifier, target, position/size/pressure of one contact point — grouped, not member-enumerated) | | | | |
| TouchList (related interface: indexed, array-like collection of Touch objects — grouped, not member-enumerated) | | | | |

#### TransitionEvent

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| TransitionEvent() constructor | | | | |
| TransitionEvent.propertyName | | | | |
| TransitionEvent.elapsedTime | | | | |
| TransitionEvent.pseudoElement | | | | |

#### AnimationEvent

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| AnimationEvent() constructor | | | | |
| AnimationEvent.animationName | | | | |
| AnimationEvent.elapsedTime | | | | |
| AnimationEvent.pseudoElement | | | | |

#### Event types (names) a page can listen for

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| "click" | ✅ | the scene element's `on_click(nodeId)`; bubbling resolved at compile time (`V_CHAIN`) | PlainTranslatorTests: onclick and bubbling | |
| "dblclick" | | | | |
| "mousedown" | | | | |
| "mouseup" | | | | |
| "mousemove" | | | | |
| "mouseenter" | | | | |
| "mouseleave" | | | | |
| "mouseover" | | | | |
| "mouseout" | | | | |
| "contextmenu" | | | | |
| "pointerdown" | | | | |
| "pointerup" | | | | |
| "pointermove" | | | | |
| "pointerenter" | | | | |
| "pointerleave" | | | | |
| "pointerover" | | | | |
| "pointerout" | | | | |
| "pointercancel" | | | | |
| "gotpointercapture" | | | | |
| "lostpointercapture" | | | | |
| "keydown" | | | | |
| "keyup" | | | | |
| "keypress" (deprecated) | | | | |
| "wheel" | | | | |
| "focus" | | | | |
| "blur" | | | | |
| "focusin" | | | | |
| "focusout" | | | | |
| "input" | | | | |
| "change" | | | | |
| "submit" | | | | |
| "reset" | | | | |
| "invalid" | | | | |
| "select" | | | | |
| "touchstart" | | | | |
| "touchend" | | | | |
| "touchmove" | | | | |
| "touchcancel" | | | | |
| "transitionstart" | | | | |
| "transitionend" | | | | |
| "transitionrun" | | | | |
| "transitioncancel" | | | | |
| "animationstart" | | | | |
| "animationend" | | | | |
| "animationiteration" | | | | |
| "animationcancel" | | | | |
| "load" | | | | |
| "error" | | | | |
| "resize" | | | | |
| "scroll" | | | | |
| "DOMContentLoaded" | | | | |
| "beforeunload" | | | | |
| "unload" | | | | |
| "hashchange" | | | | |
| "popstate" | | | | |
| "drag" | | | | |
| "dragstart" | | | | |
| "dragend" | | | | |
| "dragenter" | | | | |
| "dragleave" | | | | |
| "dragover" | | | | |
| "drop" | | | | |
| "copy" | | | | |
| "cut" | | | | |
| "paste" | | | | |
| "toggle" | | | | |
| "close" | | | | |
| "cancel" | | | | |

#### CanvasRenderingContext2D

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| CanvasRenderingContext2D.canvas | | | | |
| CanvasRenderingContext2D.getContextAttributes() | | | | |
| CanvasRenderingContext2D.isContextLost() | | | | |
| CanvasRenderingContext2D.reset() | | | | |
| CanvasRenderingContext2D.clearRect() | | | | |
| CanvasRenderingContext2D.fillRect() | | | | |
| CanvasRenderingContext2D.strokeRect() | | | | |
| CanvasRenderingContext2D.fillText() | | | | |
| CanvasRenderingContext2D.strokeText() | | | | |
| CanvasRenderingContext2D.measureText() | | | | |
| CanvasRenderingContext2D.lineWidth | | | | |
| CanvasRenderingContext2D.lineCap | | | | |
| CanvasRenderingContext2D.lineJoin | | | | |
| CanvasRenderingContext2D.miterLimit | | | | |
| CanvasRenderingContext2D.lineDashOffset | | | | |
| CanvasRenderingContext2D.getLineDash() | | | | |
| CanvasRenderingContext2D.setLineDash() | | | | |
| CanvasRenderingContext2D.font | | | | |
| CanvasRenderingContext2D.textAlign | | | | |
| CanvasRenderingContext2D.textBaseline | | | | |
| CanvasRenderingContext2D.direction | | | | |
| CanvasRenderingContext2D.letterSpacing | | | | |
| CanvasRenderingContext2D.fontKerning | | | | |
| CanvasRenderingContext2D.fontStretch | | | | |
| CanvasRenderingContext2D.fontVariantCaps | | | | |
| CanvasRenderingContext2D.textRendering | | | | |
| CanvasRenderingContext2D.wordSpacing | | | | |
| CanvasRenderingContext2D.lang | | | | |
| CanvasRenderingContext2D.fillStyle | | | | |
| CanvasRenderingContext2D.strokeStyle | | | | |
| CanvasRenderingContext2D.createLinearGradient() | | | | |
| CanvasRenderingContext2D.createRadialGradient() | | | | |
| CanvasRenderingContext2D.createConicGradient() | | | | |
| CanvasRenderingContext2D.createPattern() | | | | |
| CanvasRenderingContext2D.shadowBlur | | | | |
| CanvasRenderingContext2D.shadowColor | | | | |
| CanvasRenderingContext2D.shadowOffsetX | | | | |
| CanvasRenderingContext2D.shadowOffsetY | | | | |
| CanvasRenderingContext2D.beginPath() | | | | |
| CanvasRenderingContext2D.closePath() | | | | |
| CanvasRenderingContext2D.moveTo() | | | | |
| CanvasRenderingContext2D.lineTo() | | | | |
| CanvasRenderingContext2D.bezierCurveTo() | | | | |
| CanvasRenderingContext2D.quadraticCurveTo() | | | | |
| CanvasRenderingContext2D.arc() | | | | |
| CanvasRenderingContext2D.arcTo() | | | | |
| CanvasRenderingContext2D.ellipse() | | | | |
| CanvasRenderingContext2D.rect() | | | | |
| CanvasRenderingContext2D.roundRect() | | | | |
| CanvasRenderingContext2D.fill() | | | | |
| CanvasRenderingContext2D.stroke() | | | | |
| CanvasRenderingContext2D.drawFocusIfNeeded() | | | | |
| CanvasRenderingContext2D.clip() | | | | |
| CanvasRenderingContext2D.isPointInPath() | | | | |
| CanvasRenderingContext2D.isPointInStroke() | | | | |
| CanvasRenderingContext2D.getTransform() | | | | |
| CanvasRenderingContext2D.rotate() | | | | |
| CanvasRenderingContext2D.scale() | | | | |
| CanvasRenderingContext2D.translate() | | | | |
| CanvasRenderingContext2D.transform() | | | | |
| CanvasRenderingContext2D.setTransform() | | | | |
| CanvasRenderingContext2D.resetTransform() | | | | |
| CanvasRenderingContext2D.globalAlpha | | | | |
| CanvasRenderingContext2D.globalCompositeOperation | | | | |
| CanvasRenderingContext2D.drawImage() | | | | |
| CanvasRenderingContext2D.createImageData() | | | | |
| CanvasRenderingContext2D.getImageData() | | | | |
| CanvasRenderingContext2D.putImageData() | | | | |
| CanvasRenderingContext2D.imageSmoothingEnabled | | | | |
| CanvasRenderingContext2D.imageSmoothingQuality | | | | |
| CanvasRenderingContext2D.save() | | | | |
| CanvasRenderingContext2D.restore() | | | | |
| CanvasRenderingContext2D.filter | | | | |

#### Path2D

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Path2D() constructor | | | | |
| Path2D.addPath() | | | | |
| Path2D.closePath() | | | | |
| Path2D.moveTo() | | | | |
| Path2D.lineTo() | | | | |
| Path2D.bezierCurveTo() | | | | |
| Path2D.quadraticCurveTo() | | | | |
| Path2D.arc() | | | | |
| Path2D.arcTo() | | | | |
| Path2D.ellipse() | | | | |
| Path2D.rect() | | | | |
| Path2D.roundRect() | | | | |

#### ImageData

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| ImageData() constructor | | | | |
| ImageData.data | | | | |
| ImageData.width | | | | |
| ImageData.height | | | | |
| ImageData.colorSpace | | | | |

#### CanvasGradient

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| CanvasGradient.addColorStop() | | | | |

#### CanvasPattern

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| CanvasPattern.setTransform() | | | | |

#### SVGElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| SVGElement.attributeStyleMap | | | | |
| SVGElement.autofocus | | | | |
| SVGElement.className (deprecated, use classList) | | | | |
| SVGElement.dataset | | | | |
| SVGElement.nonce | | | | |
| SVGElement.ownerSVGElement | | | | |
| SVGElement.style | | | | |
| SVGElement.tabIndex | | | | |
| SVGElement.viewportElement | | | | |
| SVGElement.blur() | | | | |
| SVGElement.focus() | | | | |

#### SVGGraphicsElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| SVGGraphicsElement.requiredExtensions | | | | |
| SVGGraphicsElement.systemLanguage | | | | |
| SVGGraphicsElement.transform | | | | |
| SVGGraphicsElement.getBBox() | | | | |
| SVGGraphicsElement.getCTM() | | | | |
| SVGGraphicsElement.getScreenCTM() | | | | |

#### SVGGeometryElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| SVGGeometryElement.pathLength | | | | |
| SVGGeometryElement.isPointInFill() | | | | |
| SVGGeometryElement.isPointInStroke() | | | | |
| SVGGeometryElement.getTotalLength() | | | | |
| SVGGeometryElement.getPointAtLength() | | | | |

#### SVGSVGElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| SVGSVGElement.x | | | | |
| SVGSVGElement.y | | | | |
| SVGSVGElement.width | | | | |
| SVGSVGElement.height | | | | |
| SVGSVGElement.viewBox | | | | |
| SVGSVGElement.preserveAspectRatio | | | | |
| SVGSVGElement.currentScale | | | | |
| SVGSVGElement.currentTranslate | | | | |
| SVGSVGElement.createSVGNumber() | | | | |
| SVGSVGElement.createSVGLength() | | | | |
| SVGSVGElement.createSVGAngle() | | | | |
| SVGSVGElement.createSVGPoint() | | | | |
| SVGSVGElement.createSVGMatrix() | | | | |
| SVGSVGElement.createSVGRect() | | | | |
| SVGSVGElement.createSVGTransform() | | | | |
| SVGSVGElement.createSVGTransformFromMatrix() | | | | |
| SVGSVGElement.getElementById() | | | | |
| SVGSVGElement.suspendRedraw() (deprecated) | | | | |
| SVGSVGElement.unsuspendRedraw() (deprecated) | | | | |
| SVGSVGElement.unsuspendRedrawAll() (deprecated) | | | | |
| SVGSVGElement.forceRedraw() (deprecated) | | | | |
| SVGSVGElement.pauseAnimations() | | | | |
| SVGSVGElement.unpauseAnimations() | | | | |
| SVGSVGElement.animationsPaused() | | | | |
| SVGSVGElement.getCurrentTime() | | | | |
| SVGSVGElement.setCurrentTime() | | | | |
| SVGSVGElement.checkIntersection() (deprecated) | | | | |
| SVGSVGElement.checkEnclosure() (deprecated) | | | | |
| SVGSVGElement.deselectAll() (deprecated) | | | | |

#### SVGRectElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| SVGRectElement.x | | | | |
| SVGRectElement.y | | | | |
| SVGRectElement.width | | | | |
| SVGRectElement.height | | | | |
| SVGRectElement.rx | | | | |
| SVGRectElement.ry | | | | |

#### SVGCircleElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| SVGCircleElement.cx | | | | |
| SVGCircleElement.cy | | | | |
| SVGCircleElement.r | | | | |

#### SVGEllipseElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| SVGEllipseElement.cx | | | | |
| SVGEllipseElement.cy | | | | |
| SVGEllipseElement.rx | | | | |
| SVGEllipseElement.ry | | | | |

#### SVGLineElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| SVGLineElement.x1 | | | | |
| SVGLineElement.y1 | | | | |
| SVGLineElement.x2 | | | | |
| SVGLineElement.y2 | | | | |

#### SVGPolylineElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| SVGPolylineElement.points | | | | |
| SVGPolylineElement.animatedPoints | | | | |

#### SVGPolygonElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| SVGPolygonElement.points | | | | |
| SVGPolygonElement.animatedPoints | | | | |

#### SVGPathElement

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| SVGPathElement.getPathData() | | | | |
| SVGPathElement.setPathData() | | | | |

(SVGPathElement.pathLength, .getTotalLength(), .getPointAtLength(), .isPointInFill(), .isPointInStroke() are inherited from SVGGeometryElement, already listed there.)

#### SVGTransform

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| SVGTransform.type | | | | |
| SVGTransform.matrix | | | | |
| SVGTransform.angle | | | | |
| SVGTransform.setMatrix() | | | | |
| SVGTransform.setTranslate() | | | | |
| SVGTransform.setScale() | | | | |
| SVGTransform.setRotate() | | | | |
| SVGTransform.setSkewX() | | | | |
| SVGTransform.setSkewY() | | | | |
| SVGTransform.SVG_TRANSFORM_UNKNOWN (constant, 0) | | | | |
| SVGTransform.SVG_TRANSFORM_MATRIX (constant, 1) | | | | |
| SVGTransform.SVG_TRANSFORM_TRANSLATE (constant, 2) | | | | |
| SVGTransform.SVG_TRANSFORM_SCALE (constant, 3) | | | | |
| SVGTransform.SVG_TRANSFORM_ROTATE (constant, 4) | | | | |
| SVGTransform.SVG_TRANSFORM_SKEWX (constant, 5) | | | | |
| SVGTransform.SVG_TRANSFORM_SKEWY (constant, 6) | | | | |

#### SVGTransformList

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| SVGTransformList.numberOfItems | | | | |
| SVGTransformList.length | | | | |
| SVGTransformList.clear() | | | | |
| SVGTransformList.initialize() | | | | |
| SVGTransformList.getItem() | | | | |
| SVGTransformList.insertItemBefore() | | | | |
| SVGTransformList.replaceItem() | | | | |
| SVGTransformList.removeItem() | | | | |
| SVGTransformList.appendItem() | | | | |
| SVGTransformList.createSVGTransformFromMatrix() | | | | |
| SVGTransformList.consolidate() | | | | |

#### Animated-value wrapper types (grouped)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| SVGAnimated* family (SVGAnimatedLength, SVGAnimatedNumber, SVGAnimatedRect, SVGAnimatedString, SVGAnimatedBoolean, SVGAnimatedEnumeration, SVGAnimatedInteger, SVGAnimatedAngle, SVGAnimatedTransformList, SVGAnimatedPreserveAspectRatio, SVGAnimatedNumberList, SVGAnimatedLengthList) — grouped: every one exposes only `.baseVal` (the static/authored value) and `.animVal` (the current value, animated or equal to baseVal), so scripting them is the same two-property pattern regardless of the wrapped type | | | | |

#### Web Animations

`Element.animate()` itself is already listed under 4.2 Element above — not repeated here.

##### Animation

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Animation() constructor | | | | |
| Animation.currentTime | | | | |
| Animation.effect | | | | |
| Animation.finished | | | | |
| Animation.id | | | | |
| Animation.overallProgress | | | | |
| Animation.pending | | | | |
| Animation.playbackRate | | | | |
| Animation.playState | | | | |
| Animation.ready | | | | |
| Animation.replaceState | | | | |
| Animation.startTime | | | | |
| Animation.timeline | | | | |
| Animation.cancel() | | | | |
| Animation.commitStyles() | | | | |
| Animation.finish() | | | | |
| Animation.pause() | | | | |
| Animation.persist() | | | | |
| Animation.play() | | | | |
| Animation.reverse() | | | | |
| Animation.updatePlaybackRate() | | | | |
| Animation "cancel" event / oncancel | | | | |
| Animation "finish" event / onfinish | | | | |
| Animation "remove" event / onremove | | | | |

##### KeyframeEffect

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| KeyframeEffect() constructor | | | | |
| KeyframeEffect.target | | | | |
| KeyframeEffect.pseudoElement | | | | |
| KeyframeEffect.composite | | | | |
| KeyframeEffect.iterationComposite | | | | |
| KeyframeEffect.getKeyframes() | | | | |
| KeyframeEffect.setKeyframes() | | | | |
| KeyframeEffect.getTiming() | | | | |
| KeyframeEffect.getComputedTiming() | | | | |
| KeyframeEffect.updateTiming() | | | | |

#### CSSOM View

Element/HTMLElement geometry and scrolling members (`getBoundingClientRect()`, `getClientRects()`, `offsetWidth`/`offsetHeight`/`offsetTop`/`offsetLeft`/`offsetParent`, `clientWidth`/`clientHeight`/`clientTop`/`clientLeft`, `scrollWidth`/`scrollHeight`/`scrollTop`/`scrollLeft`, `scroll()`/`scrollBy()`/`scrollTo()`/`scrollIntoView()`) are already listed under 4.2 Element and HTMLElement above — not repeated here. `Window.scroll()`/`scrollBy()`/`scrollTo()` are already listed under 4.1 Window — only the CSSOM View members not covered anywhere above follow.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Window.scrollX | | | | |
| Window.scrollY | | | | |
| Window.pageXOffset (alias for scrollX) | | | | |
| Window.pageYOffset (alias for scrollY) | | | | |
| Window.visualViewport | | | | |

#### MutationObserver

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| MutationObserver() constructor | | | | |
| MutationObserver.observe() | | | | |
| MutationObserver.disconnect() | | | | |
| MutationObserver.takeRecords() | | | | |
| MutationRecord.type | | | | |
| MutationRecord.target | | | | |
| MutationRecord.addedNodes | | | | |
| MutationRecord.removedNodes | | | | |
| MutationRecord.previousSibling | | | | |
| MutationRecord.nextSibling | | | | |
| MutationRecord.attributeName | | | | |
| MutationRecord.attributeNamespace | | | | |
| MutationRecord.oldValue | | | | |

#### ResizeObserver

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| ResizeObserver() constructor | | | | |
| ResizeObserver.observe() | | | | |
| ResizeObserver.unobserve() | | | | |
| ResizeObserver.disconnect() | | | | |
| ResizeObserverEntry.target | | | | |
| ResizeObserverEntry.contentRect | | | | |
| ResizeObserverEntry.borderBoxSize | | | | |
| ResizeObserverEntry.contentBoxSize | | | | |
| ResizeObserverEntry.devicePixelContentBoxSize | | | | |

#### IntersectionObserver

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| IntersectionObserver() constructor | | | | |
| IntersectionObserver.root | | | | |
| IntersectionObserver.rootMargin | | | | |
| IntersectionObserver.thresholds | | | | |
| IntersectionObserver.delay | | | | |
| IntersectionObserver.scrollMargin | | | | |
| IntersectionObserver.trackVisibility | | | | |
| IntersectionObserver.observe() | | | | |
| IntersectionObserver.unobserve() | | | | |
| IntersectionObserver.disconnect() | | | | |
| IntersectionObserver.takeRecords() | | | | |
| IntersectionObserverEntry.boundingClientRect | | | | |
| IntersectionObserverEntry.intersectionRatio | | | | |
| IntersectionObserverEntry.intersectionRect | | | | |
| IntersectionObserverEntry.isIntersecting | | | | |
| IntersectionObserverEntry.rootBounds | | | | |
| IntersectionObserverEntry.target | | | | |
| IntersectionObserverEntry.time | | | | |

#### URL

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| URL() constructor | | | | |
| URL.href | | | | |
| URL.origin | | | | |
| URL.protocol | | | | |
| URL.username | | | | |
| URL.password | | | | |
| URL.host | | | | |
| URL.hostname | | | | |
| URL.port | | | | |
| URL.pathname | | | | |
| URL.search | | | | |
| URL.searchParams | | | | |
| URL.hash | | | | |
| URL.toString() | | | | |
| URL.toJSON() | | | | |
| URL.createObjectURL() (static) | | | | |
| URL.revokeObjectURL() (static) | | | | |
| URL.canParse() (static) | | | | |
| URL.parse() (static) | | | | |

#### URLSearchParams

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| URLSearchParams() constructor | | | | |
| URLSearchParams.size | | | | |
| URLSearchParams.append() | | | | |
| URLSearchParams.delete() | | | | |
| URLSearchParams.entries() | | | | |
| URLSearchParams.forEach() | | | | |
| URLSearchParams.get() | | | | |
| URLSearchParams.getAll() | | | | |
| URLSearchParams.has() | | | | |
| URLSearchParams.keys() | | | | |
| URLSearchParams.set() | | | | |
| URLSearchParams.sort() | | | | |
| URLSearchParams.toString() | | | | |
| URLSearchParams.values() | | | | |

#### TextEncoder

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| TextEncoder() constructor | | | | |
| TextEncoder.encoding | | | | |
| TextEncoder.encode() | | | | |
| TextEncoder.encodeInto() | | | | |

#### TextDecoder

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| TextDecoder() constructor | | | | |
| TextDecoder.encoding | | | | |
| TextDecoder.fatal | | | | |
| TextDecoder.ignoreBOM | | | | |
| TextDecoder.decode() | | | | |

#### Blob

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Blob() constructor | | | | |
| Blob.size | | | | |
| Blob.type | | | | |
| Blob.arrayBuffer() | | | | |
| Blob.bytes() | | | | |
| Blob.slice() | | | | |
| Blob.stream() | | | | |
| Blob.text() | | | | |
| Blob.textStream() | | | | |

#### File

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| File() constructor | | | | |
| File.name | | | | |
| File.lastModified | | | | |
| File.lastModifiedDate (deprecated) | | | | |
| File.webkitRelativePath | | | | |

(File also inherits Blob.size/type/arrayBuffer()/slice()/stream()/text(), already listed under Blob.)

#### FileReader

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| FileReader() constructor | | | | |
| FileReader.error | | | | |
| FileReader.readyState | | | | |
| FileReader.result | | | | |
| FileReader.abort() | | | | |
| FileReader.readAsArrayBuffer() | | | | |
| FileReader.readAsBinaryString() (deprecated) | | | | |
| FileReader.readAsDataURL() | | | | |
| FileReader.readAsText() | | | | |
| FileReader "abort" event / onabort | | | | |
| FileReader "error" event / onerror | | | | |
| FileReader "load" event / onload | | | | |
| FileReader "loadend" event / onloadend | | | | |
| FileReader "loadstart" event / onloadstart | | | | |
| FileReader "progress" event / onprogress | | | | |

#### The rest of the standard Web API surface, grouped by interface/family

All of the rows below are single grouped rows (interface family, not full member enumeration), per the task instructions. Every one of these is out of reach for a Stationeers console script — the compiled/interpreted page runs inside a Jint sandbox on a game-mod chip with no host bindings to GPU contexts, audio/media devices, network peers, OS device APIs, or browser chrome — which is why they are named as families rather than enumerated member-by-member. Deliberately not repeated here because they belong to sections above: Fetch API, History API, Web Storage, Performance API, CSSOM (stylesheets/rules), Web Components (customElements/ShadowRoot), Service Worker's own container/registration surface beyond the one mention folded into the Web Workers row below.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| WebGL (WebGLRenderingContext, WebGL2RenderingContext, plus ~30 supporting interfaces for buffers/textures/shaders/programs — GPU-accelerated 2D/3D rendering via `<canvas>`) | | | | |
| Web Audio API (AudioContext plus its node graph — AudioNode, GainNode, OscillatorNode, AudioBufferSourceNode, AnalyserNode, etc. — sound synthesis and processing) | | | | |
| WebRTC (RTCPeerConnection plus RTCDataChannel, RTCIceCandidate, RTCSessionDescription, etc. — real-time peer-to-peer audio/video/data) | | | | |
| Web Workers (Worker, SharedWorker, ServiceWorker, plus MessageChannel/MessagePort for structured-clone messaging — background script execution) | | | | |
| IndexedDB (IDBDatabase plus IDBObjectStore, IDBCursor, IDBTransaction, IDBKeyRange, IDBIndex, IDBRequest — client-side transactional object database) | | | | |
| Geolocation API (Geolocation, GeolocationPosition, GeolocationCoordinates — device location) | | | | |
| Notifications API (Notification, NotificationEvent — OS-level notification popups) | | | | |
| Clipboard API (navigator.clipboard, Clipboard, ClipboardItem — programmatic read/write of the system clipboard; distinct from the copy/cut/paste DOM events already listed above) | | | | |
| Drag and Drop (DataTransfer, DataTransferItem, DataTransferItemList — payload carried by the drag* events already listed above) | | | | |
| File System Access API (FileSystemHandle, FileSystemFileHandle, FileSystemDirectoryHandle — user-granted access to the local filesystem) | | | | |
| Gamepad API (Gamepad, GamepadButton, GamepadEvent — connected game controller input) | | | | |
| Payment Request API (PaymentRequest, PaymentResponse, PaymentAddress — browser-mediated checkout) | | | | |
| Web Speech API (SpeechRecognition, SpeechSynthesis, SpeechSynthesisUtterance — speech-to-text and text-to-speech) | | | | |
| WebXR Device API (XRSession, XRFrame, XRPose, etc. — VR/AR sessions) | | | | |
| Web Bluetooth (Bluetooth, BluetoothDevice, BluetoothRemoteGATTServer, etc. — pairing with nearby Bluetooth devices) | | | | |
| WebUSB (USB, USBDevice, USBInterface, etc. — direct access to USB devices) | | | | |
| Screen Wake Lock API (WakeLock, WakeLockSentinel — preventing the screen from sleeping) | | | | |
| Broadcast Channel API (BroadcastChannel — same-origin cross-tab/cross-worker messaging) | | | | |
| Web Share API (navigator.share() — invoking the OS's native share sheet) | | | | |
| Battery Status API (BatteryManager — battery level/charging state) | | | | |
| Vibration API (navigator.vibrate() — device vibration) | | | | |
| Push API (PushManager, PushSubscription, PushEvent — server-sent push messages to a service worker) | | | | |
| Background Fetch / Background Sync (BackgroundFetchManager, BackgroundFetchRegistration, SyncManager, SyncEvent — deferred/retrying network work via a service worker) | | | | |
| Credential Management API (CredentialsContainer, Credential, PasswordCredential, FederatedCredential — storing/retrieving sign-in credentials) | | | | |
| Encoding streams (TextEncoderStream, TextDecoderStream — streaming variants of TextEncoder/TextDecoder already listed above) | | | | |
| Streams API (ReadableStream, WritableStream, TransformStream, plus their reader/writer/controller types — chunked, backpressured data flow) | | | | |
| Media Capture and Streams (MediaStream, MediaStreamTrack, MediaDevices, MediaRecorder — camera/microphone capture and recording) | | | | |
| Media Source Extensions (MediaSource, SourceBuffer, SourceBufferList — programmatic media buffers feeding a `<video>`/`<audio>` element) | | | | |
| Encrypted Media Extensions (MediaKeys, MediaKeySession, MediaEncryptedEvent — DRM-protected media playback) | | | | |
| WebAssembly JS API (WebAssembly.instantiate(), WebAssembly.Module, WebAssembly.Memory, WebAssembly.Table, etc. — loading and running Wasm modules) | | | | |
| Trusted Types (TrustedTypePolicy, TrustedTypePolicyFactory, TrustedHTML, TrustedScript — CSP-enforced sanitization of injection sinks) | | | | |
| Permissions API (Permissions, PermissionStatus — querying/observing permission state for other APIs on this list) | | | | |
| Content Index API (ContentIndex, ContentIndexEvent — registering offline-available content with a service worker) | | | | |
| Idle Detection API (IdleDetector, IdleDeadline — detecting user/system idle state) | | | | |
| Storage Manager (navigator.storage, StorageManager, StorageEstimate — querying/persisting site storage quota) | | | | |
| Web Locks API (LockManager, Lock — cooperative mutual-exclusion locks across tabs/workers) | | | | |
| Screen Orientation API (ScreenOrientation — reading/locking device screen orientation) | | | | |
| Fullscreen API (Element.requestFullscreen(), Document.fullscreenElement, Document.exitFullscreen() — these DOM-side members already have their own rows under 4.2 Element/Document above; grouped here only as an API-family entry. The `:fullscreen` CSS pseudo-class is a CSS-section concern, not listed here) | | | | |
| Page Visibility API (Document.hidden, Document.visibilityState, the "visibilitychange" event — already have their own rows under 4.2 Document above; grouped here only as an API-family entry) | | | | |
| Pointer Lock API (Element.requestPointerLock(), Document.pointerLockElement — capturing raw mouse movement) | | | | |
| Selection API (window.getSelection(), Selection, Range — the user's current text selection) | | | | |
| Server-Sent Events (EventSource — one-way server-to-client event stream over HTTP) | | | | |
| Generic Sensor API (Sensor base plus Accelerometer, Gyroscope, Magnetometer, AmbientLightSensor, OrientationSensor and friends — device motion/environment sensors) | | | | |
| Media Session API (MediaSession, MediaMetadata — OS media-key/lock-screen integration for playback) | | | | |
| Picture-in-Picture (HTMLVideoElement.requestPictureInPicture(), PictureInPictureWindow, plus the Document Picture-in-Picture window variant — floating always-on-top video) | | | | |
| View Transitions API (document.startViewTransition(), ViewTransition — animated same-document DOM state transitions) | | | | |
| Web MIDI API (MIDIAccess, MIDIInput, MIDIOutput, MIDIMessageEvent — MIDI device I/O) | | | | |
| Web NFC API (NDEFReader, NDEFMessage, NDEFRecord — reading/writing NFC tags) | | | | |
| Web Serial API (Serial, SerialPort — direct access to serial ports) | | | | |
| WebHID API (HID, HIDDevice — direct access to raw HID devices) | | | | |
| WebTransport (WebTransport plus its bidirectional/unidirectional stream types — low-latency client/server transport over HTTP/3) | | | | |
| Reporting API (ReportingObserver — collecting browser-generated deprecation/intervention/CSP reports) | | | | |
| Navigation API (Navigation, NavigateEvent — programmatic, event-driven control of same-document/cross-document navigation) | | | | |
| Cookie Store API (CookieStore, navigator.cookieStore — async, event-driven cookie read/write) | | | | |
| EyeDropper API (EyeDropper — invoking the OS colour-picker eyedropper tool) | | | | |
| Keyboard API (navigator.keyboard, Keyboard, KeyboardLayoutMap — querying physical keyboard layout and locking keys; distinct from the KeyboardEvent already listed above) | | | | |

## Sources

- MDN Window, Document, Node, Element, HTMLElement (and per-element interfaces listed inline below), MediaQueryList, Location, History, Navigator, console (Console API), Performance, Storage, Request, Response, Headers, XMLHttpRequest, WebSocket, ParentNode/ChildNode (see gap resolution below), CharacterData, Text, CSSStyleDeclaration, DOMTokenList, DOMStringMap, NamedNodeMap, Attr — all developer.mozilla.org, en-US, accessed 2026-09-23
- MDN per-element HTML interface pages: HTMLInputElement, HTMLTextAreaElement, HTMLSelectElement, HTMLOptionElement, HTMLOptGroupElement, HTMLFormElement, HTMLButtonElement, HTMLLabelElement, HTMLFieldSetElement, HTMLLegendElement, HTMLAnchorElement, HTMLImageElement, HTMLCanvasElement, HTMLVideoElement, HTMLAudioElement, HTMLMediaElement, HTMLTableElement, HTMLTableRowElement, HTMLTableCellElement, HTMLTableSectionElement, HTMLProgressElement, HTMLMeterElement, HTMLDetailsElement, HTMLDialogElement, HTMLOutputElement, HTMLTemplateElement, HTMLSlotElement, HTMLOListElement, HTMLUListElement, HTMLLIElement, HTMLIFrameElement, HTMLScriptElement, HTMLStyleElement, HTMLLinkElement, plus HTMLQuoteElement/HTMLModElement/HTMLTimeElement/HTMLDataElement/HTMLMapElement/HTMLAreaElement/HTMLSourceElement/HTMLTrackElement/HTMLPictureElement/HTMLBaseElement/HTMLMetaElement/HTMLTitleElement/HTMLHeadElement/HTMLBodyElement/HTMLHtmlElement/HTMLHRElement/HTMLBRElement/HTMLPreElement/HTMLSpanElement/HTMLDivElement/HTMLParagraphElement/HTMLHeadingElement/HTMLEmbedElement/HTMLObjectElement/HTMLUnknownElement (grouped) — accessed 2026-09-23
- MDN EventTarget, Event, CustomEvent, MouseEvent, PointerEvent, KeyboardEvent, WheelEvent, FocusEvent, InputEvent, TouchEvent, TransitionEvent, AnimationEvent — accessed 2026-09-23
- MDN CanvasRenderingContext2D, Path2D, ImageData, CanvasGradient, CanvasPattern — accessed 2026-09-23
- MDN SVGElement, SVGGraphicsElement, SVGGeometryElement, SVGSVGElement, SVGRectElement, SVGCircleElement, SVGEllipseElement, SVGLineElement, SVGPolylineElement, SVGPolygonElement, SVGPathElement, SVGTransform, SVGTransformList, SVGAnimatedLength — accessed 2026-09-23
- MDN Element/animate, Animation, KeyframeEffect — accessed 2026-09-23
- MDN MutationObserver, MutationRecord, ResizeObserver, ResizeObserverEntry, IntersectionObserver, IntersectionObserverEntry — accessed 2026-09-23
- MDN URL, URLSearchParams, TextEncoder, TextDecoder, Blob, File, FileReader — accessed 2026-09-23
- MDN Web API index (interfaces list) — https://developer.mozilla.org/en-US/docs/Web/API#interfaces — accessed 2026-09-23, used to cross-check the grouped long tail is complete

**Gap flagged during research, and its resolution:** ParentNode/ChildNode have no live standalone MDN interface page (both 404 as of 2026-09-23). Re-checked directly: a fresh fetch of `developer.mozilla.org/en-US/docs/Web/API/ParentNode` still 404s, and a fetch of the `Document` interface page's own member list independently confirms the same 10 ParentNode members shown above (children, firstElementChild, lastElementChild, childElementCount, append, prepend, replaceChildren, moveBefore, querySelector, querySelectorAll) plus the 4 ChildNode members are consistent with the Element page. Treated as resolved by cross-reference rather than left open — the member lists are correct, only their canonical source page no longer exists on MDN.

**Deduplication applied during assembly (per the coordinator's note on cross-file/in-file duplicates):** the original dom-b research draft carried its own "CSSOM View" section re-listing 18 Element/HTMLElement geometry members and 3 Window scroll methods that are already fully enumerated under 4.2 Element/HTMLElement and 4.1 Window above, plus its own one-row "Element.animate()" table duplicating the row already present in 4.2 Element. All four are removed from this merged file and replaced with cross-reference notes at their original heading, leaving only the CSSOM View members (Window.scrollX/scrollY/pageXOffset/pageYOffset/visualViewport) not covered elsewhere. The `aria-*`/`role` attribute (HTML content attribute, INSTRUCTION-SET-HTML.md 1b) versus `Element.aria*` (DOM IDL property, this file) and the HTML `on*` content attributes (INSTRUCTION-SET-HTML.md 1d) versus DOM event names/EventTarget (this file) are NOT duplicates — they are different languages' features (an HTML attribute vs. a DOM property/API) and each is listed once, in its own file, per the coordinator's instruction to keep both where the languages differ.

## Counts

**NodeList and HTMLCollection** had no rows although every list-giving lookup returns one; their members were added in 4.4 on 2026-09-24 (13 rows, not in the count below).

**1,715 total data rows** (verified by counting table rows in the assembled file — sub-agent self-reported subtotals below are approximate hand counts, kept for orientation): 4.1-4.4 (Window/globals ~212, Document/Node/ParentNode/ChildNode/Element/HTMLElement ~366, per-element HTML interfaces ~488, CharacterData/Text/CSSStyleDeclaration/DOMTokenList/DOMStringMap/NamedNodeMap/Attr ~54) plus 4.5 (events ~196, canvas ~92, SVG ~102, Web Animations ~34, CSSOM View 5, observers ~40, URL/text/binary ~71, grouped long tail 58 — after removing 22 rows duplicated against 4.1/4.2/4.3 during assembly).
