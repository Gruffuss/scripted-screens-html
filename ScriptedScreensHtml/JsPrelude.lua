-- The runtime a compiled page's Lua sits on: the handful of JavaScript behaviours that differ from
-- Lua's, plus enough of the browser's objects for a console page.
--
-- This ships WITH the compiled page and is the only thing of this mod that outlives compilation.
-- It is plain Lua source, loaded into the chunk's own _ENV, so it cannot reach the author's globals
-- and the author's globals cannot reach it.
--
-- Written as a .lua file rather than a C# string on purpose: the JavaScript prelude is a C# verbatim
-- string, every quote in it has to be doubled, and getting that wrong once cost two build rounds.

local floor, ceil, abs, fmod = math.floor, math.ceil, math.abs, math.fmod
local concat, format, rep, sub = table.concat, string.format, string.rep, string.sub

-- ---- the six places JavaScript and Lua disagree ------------------------------------------------

-- 0 and "" are false in JavaScript and true in Lua. Everything else agrees.
function js_truthy(v)
  if v == nil or v == false then return false end
  if v == 0 or v == "" then return false end
  if v ~= v then return false end            -- NaN
  return true
end

-- `+` concatenates when either side is a string, and adds otherwise. It is the only arithmetic
-- operator that does: -, * and / coerce to number unconditionally, so they need no helper.
function js_add(a, b)
  if type(a) == "number" and type(b) == "number" then return a + b end
  if type(a) == "string" or type(b) == "string" then return js_str(a) .. js_str(b) end
  return js_num(a) + js_num(b)
end

-- A ternary. Lua's `a and b or c` yields c whenever b is false or nil, which a ternary does not.
function js_if(test, yes, no)
  if test then return yes end
  return no
end

-- `&&` and `||` in value position yield the operand, not a boolean, and they short-circuit: the
-- right side arrives as a thunk and is called only if it is reached. Guarding with them is the
-- whole reason a page writes them.
function js_and(a, rest) if js_truthy(a) then return rest() end return a end
function js_or(a, rest) if js_truthy(a) then return a end return rest() end

function js_typeof(v)
  local t = type(v)
  if v == nil then return "undefined" end
  if t == "number" or t == "string" or t == "boolean" then return t end
  if t == "function" then return "function" end
  return "object"
end

-- `.length`: a field on an array, the character count on a string.
function js_len(v)
  if type(v) == "string" then return #v end
  if type(v) == "table" then return v.length or #v end
  return 0
end

-- ---- values -------------------------------------------------------------------------------------

function js_num(v)
  if type(v) == "number" then return v end
  if type(v) == "string" then return tonumber(v) or (0 / 0) end
  if v == true then return 1 end
  if v == false or v == nil then return 0 end
  return 0 / 0
end

-- A number as JavaScript prints it: an integral value has no decimal point, which Lua's tostring
-- does not honour, and a console that shows "5.0" where the page said 5 looks broken.
function js_str(v)
  local t = type(v)
  if t == "string" then return v end
  if v == nil then return "undefined" end
  if v == true then return "true" end
  if v == false then return "false" end
  if t == "number" then
    if v ~= v then return "NaN" end
    if v == math.huge then return "Infinity" end
    if v == -math.huge then return "-Infinity" end
    if v == floor(v) and abs(v) < 1e15 then return format("%d", v) end
    -- JavaScript prints the SHORTEST string that reads back as the same double, which is why it
    -- shows 933.3333333333334 where %.17g shows 933.33333333333337. Found by asking for more
    -- digits until one round-trips; measured against a real page, this is the only difference
    -- between a frame run as JavaScript and the same frame run as Lua.
    for digits = 15, 17 do
      local text = format("%." .. digits .. "g", v)
      if tonumber(text) == v then return text end
    end
    return format("%.17g", v)
  end
  if t == "table" and v.length then
    local parts = {}
    for i = 0, v.length - 1 do parts[i + 1] = js_str(v[i]) end
    return concat(parts, ",")
  end
  return "[object Object]"
end

-- An array: 0-based with a length field, so every index in the page's source stays as it was.
function js_array(items, n)
  items = items or {}
  items.length = n or 0
  return items
end

-- `{ ...a, b: 1 }`. Later keys win, as in JavaScript.
function js_merge(...)
  local out = {}
  for i = 1, select("#", ...) do
    local t = select(i, ...)
    if type(t) == "table" then for k, v in pairs(t) do out[k] = v end end
  end
  return out
end

-- A getter is Lua's __index, which is the same idea under another name.
function js_getters(fields, getters)
  return setmetatable(fields, { __index = function(self, key)
    local g = getters[key]
    if g then return g(self) end
    return nil
  end })
end

function js_delete(t, k)
  if type(t) == "table" then t[k] = nil end
  return true
end

-- Only a pattern of plain characters ever reaches here: JsToLua reports anything with regular
-- expression syntax in it rather than pretending a Lua pattern is a regex.
function js_regex(pattern, flags)
  return { __regex = true, source = pattern, global = flags:find("g") ~= nil }
end

-- ---- method dispatch ------------------------------------------------------------------------------
--
-- `obj.name(args)` routes here rather than becoming Lua's `obj:name(args)`. Giving JavaScript's
-- string methods to Lua's string metatable would work, and that metatable is shared with the whole
-- VM: the generated chunk runs in its own _ENV precisely so it cannot reach the author's Lua, and
-- mutating a global metatable walks straight past that.
--
-- A function the PAGE put on an object is called with the arguments it was written with and no
-- receiver. That is not a shortcut: every function stored on an object across these pages is an
-- arrow function, which by definition ignores `this`.

local StringMethods, ArrayMethods, NumberMethods = {}, {}, {}

function js_m(obj, name, ...)
  if type(obj) == "table" then
    local own = rawget(obj, name)
    if type(own) == "function" then return own(...) end
    local viaIndex = obj[name]
    if type(viaIndex) == "function" then return viaIndex(...) end
    local m = ArrayMethods[name]
    if m then return m(obj, ...) end
  elseif type(obj) == "string" then
    local m = StringMethods[name]
    if m then return m(obj, ...) end
  elseif type(obj) == "number" then
    local m = NumberMethods[name]
    if m then return m(obj, ...) end
  end
  error("the page calls ." .. tostring(name) .. "() on a " .. js_typeof(obj) .. ", which the prelude does not provide")
end

-- strings

function StringMethods.trim(s) return (s:gsub("^%s+", ""):gsub("%s+$", "")) end
function StringMethods.toUpperCase(s) return s:upper() end
function StringMethods.toLowerCase(s) return s:lower() end
function StringMethods.charAt(s, i) return sub(s, i + 1, i + 1) end
function StringMethods.indexOf(s, find) local at = s:find(find, 1, true) return at and at - 1 or -1 end
function StringMethods.includes(s, find) return s:find(find, 1, true) ~= nil end
function StringMethods.startsWith(s, find) return sub(s, 1, #find) == find end
function StringMethods.endsWith(s, find) return #find == 0 or sub(s, -#find) == find end

-- JavaScript's slice: a negative index counts from the end, and end is exclusive.
function StringMethods.slice(s, from, to)
  local n = #s
  from = from or 0
  if from < 0 then from = math.max(0, n + from) end
  to = to or n
  if to < 0 then to = n + to end
  return sub(s, from + 1, to)
end

function StringMethods.substring(s, from, to) return StringMethods.slice(s, from, to) end

function StringMethods.padStart(s, width, pad)
  pad = pad or " "
  if #s >= width or #pad == 0 then return s end
  return sub(rep(pad, ceil((width - #s) / #pad)), 1, width - #s) .. s
end

function StringMethods.padEnd(s, width, pad)
  pad = pad or " "
  if #s >= width or #pad == 0 then return s end
  return s .. sub(rep(pad, ceil((width - #s) / #pad)), 1, width - #s)
end

function StringMethods.split(s, sep)
  local out, n = {}, 0
  if sep == "" then
    for i = 1, #s do out[n] = sub(s, i, i) n = n + 1 end
  else
    local from = 1
    while true do
      local a, b = s:find(sep, from, true)
      if not a then out[n] = sub(s, from) n = n + 1 break end
      out[n] = sub(s, from, a - 1) n = n + 1
      from = b + 1
    end
  end
  return js_array(out, n)
end

function StringMethods.replace(s, find, with)
  local plain = type(find) == "table" and find.source or find
  local all = type(find) == "table" and find.global
  local out, from, n = {}, 1, 0
  while true do
    local a, b = s:find(plain, from, true)
    if not a then break end
    n = n + 1
    out[n] = sub(s, from, a - 1)
    n = n + 1
    out[n] = with
    from = b + 1
    if not all then break end
  end
  out[n + 1] = sub(s, from)
  return concat(out)
end

StringMethods.replaceAll = StringMethods.replace

-- numbers

function NumberMethods.toFixed(v, digits)
  digits = digits or 0
  return format("%." .. format("%d", digits) .. "f", v)
end

function NumberMethods.toString(v) return js_str(v) end

-- arrays and objects

function ArrayMethods.push(a, ...)
  for i = 1, select("#", ...) do
    a[a.length] = select(i, ...)
    a.length = a.length + 1
  end
  return a.length
end

function ArrayMethods.pop(a)
  if a.length == 0 then return nil end
  a.length = a.length - 1
  local v = a[a.length]
  a[a.length] = nil
  return v
end

function ArrayMethods.forEach(a, fn)
  for i = 0, a.length - 1 do fn(a[i], i, a) end
end

function ArrayMethods.map(a, fn)
  local out = {}
  for i = 0, a.length - 1 do out[i] = fn(a[i], i, a) end
  return js_array(out, a.length)
end

function ArrayMethods.filter(a, fn)
  local out, n = {}, 0
  for i = 0, a.length - 1 do
    if js_truthy(fn(a[i], i, a)) then out[n] = a[i] n = n + 1 end
  end
  return js_array(out, n)
end

function ArrayMethods.join(a, sep)
  local parts = {}
  for i = 0, a.length - 1 do parts[i + 1] = js_str(a[i]) end
  return concat(parts, sep or ",")
end

-- Both ends are clamped into the array, which is the half that is easy to leave out: JavaScript's
-- slice(0, 7) over four entries returns four, where an unclamped copy returns seven with three
-- holes in it - and the next .map over those holes is where it is finally noticed. Every Atmo page
-- trims its event log this way.
function ArrayMethods.slice(a, from, to)
  local n = a.length
  from = from or 0
  if from < 0 then from = math.max(0, n + from) else from = math.min(from, n) end
  to = to or n
  if to < 0 then to = math.max(0, n + to) else to = math.min(to, n) end
  local out, m = {}, 0
  for i = from, to - 1 do out[m] = a[i] m = m + 1 end
  return js_array(out, m)
end

-- concat flattens an array argument by exactly one level and appends anything else whole, so
-- [a].concat(b) with b an array is a + b, and with b a plain object is a + [b].
--
-- The extra arguments are read out FIRST, before anything else loops. This Lua's `...` does not
-- survive a numeric `for` in the same function: after one has run, `select("#", ...)` over-counts
-- and `select(i, ...)` hands back values that were never passed. It cost an afternoon here, where
-- a one-argument concat quietly produced an array one slot too long with a nil in it.
function ArrayMethods.concat(a, ...)
  local rest, count = {}, select("#", ...)
  for i = 1, count do rest[i] = select(i, ...) end
  local out, n = {}, 0
  for i = 0, a.length - 1 do out[n] = a[i] n = n + 1 end
  for i = 1, count do
    local v = rest[i]
    if type(v) == "table" and type(v.length) == "number" then
      for j = 0, v.length - 1 do out[n] = v[j] n = n + 1 end
    else
      out[n] = v n = n + 1
    end
  end
  return js_array(out, n)
end

function ArrayMethods.indexOf(a, v)
  for i = 0, a.length - 1 do if a[i] == v then return i end end
  return -1
end

function ArrayMethods.includes(a, v) return ArrayMethods.indexOf(a, v) >= 0 end

function ArrayMethods.find(a, fn)
  for i = 0, a.length - 1 do if js_truthy(fn(a[i], i, a)) then return a[i] end end
  return nil
end

function ArrayMethods.some(a, fn)
  for i = 0, a.length - 1 do if js_truthy(fn(a[i], i, a)) then return true end end
  return false
end

function ArrayMethods.every(a, fn)
  for i = 0, a.length - 1 do if not js_truthy(fn(a[i], i, a)) then return false end end
  return true
end

function ArrayMethods.reduce(a, fn, seed)
  local acc, start = seed, 0
  if acc == nil then acc = a[0] start = 1 end
  for i = start, a.length - 1 do acc = fn(acc, a[i], i, a) end
  return acc
end

function ArrayMethods.sort(a, fn)
  local flat = {}
  for i = 0, a.length - 1 do flat[i + 1] = a[i] end
  table.sort(flat, fn and function(x, y) return js_num(fn(x, y)) < 0 end
                       or function(x, y) return js_str(x) < js_str(y) end)
  for i = 1, #flat do a[i - 1] = flat[i] end
  return a
end

-- ---- the globals a page expects ------------------------------------------------------------------

Infinity = math.huge
NaN = 0 / 0
undefined = nil

Math = {
  PI = math.pi, E = math.exp(1),
  floor = floor, ceil = ceil, abs = abs, sqrt = math.sqrt,
  sin = math.sin, cos = math.cos, tan = math.tan, atan = math.atan, asin = math.asin, acos = math.acos,
  exp = math.exp, log = math.log, pow = function(a, b) return a ^ b end,
  sign = function(v) if v > 0 then return 1 elseif v < 0 then return -1 else return 0 end end,
  -- JavaScript rounds .5 toward +Infinity, so -2.5 is -2 and Lua's floor(v+0.5) agrees
  round = function(v) return floor(v + 0.5) end,
  trunc = function(v) if v < 0 then return ceil(v) else return floor(v) end end,
  hypot = function(a, b) return math.sqrt(a * a + b * b) end,
  atan2 = function(y, x) return math.atan(y, x) end,
}

function Math.min(...)
  local best
  for i = 1, select("#", ...) do
    local v = js_num(select(i, ...))
    if best == nil or v < best then best = v end
  end
  return best or math.huge
end

function Math.max(...)
  local best
  for i = 1, select("#", ...) do
    local v = js_num(select(i, ...))
    if best == nil or v > best then best = v end
  end
  return best or -math.huge
end

-- Deterministic on purpose. A page drawn on several clients has to draw the SAME thing on each: the
-- vector layer's own hash() exists for exactly this reason, and a page whose obstacle course
-- differed per player would be a bug nobody could reproduce. Seeded from the page, not the clock.
-- MINSTD rather than a wider multiplier, and the reason is worth keeping: Lua 5.4 does integer
-- arithmetic in 64 bits where JavaScript does everything in doubles, so any product above 2^53
-- gives two different answers. 2^31 * 16807 stays under it, so this generator produces the same
-- sequence in both languages - which is what let a transpiled page be diffed against its original.
local rngState = 48271
function Math.random()
  rngState = math.fmod(rngState * 16807, 2147483647)
  return rngState / 2147483647
end

function js_seed(n) rngState = math.fmod(n, 2147483647) if rngState <= 0 then rngState = 48271 end end

Number = {
  isFinite = function(v) return type(v) == "number" and v == v and abs(v) ~= math.huge end,
  isNaN = function(v) return v ~= v end,
  parseFloat = function(v) return tonumber(v) or (0 / 0) end,
  MAX_SAFE_INTEGER = 9007199254740991,
}
setmetatable(Number, { __call = function(_, v) return js_num(v) end })

setmetatable({}, {})
String = setmetatable({}, { __call = function(_, v) return js_str(v) end })
Boolean = setmetatable({}, { __call = function(_, v) return js_truthy(v) end })

function isNaN(v) local n = js_num(v) return n ~= n end
function parseFloat(v) return tonumber(v) or (0 / 0) end
function parseInt(v, base)
  if type(v) == "number" then return Math.trunc(v) end
  return tonumber(v, base) or (0 / 0)
end

Object = {
  keys = function(t)
    local out, n = {}, 0
    for k in pairs(t) do if k ~= "length" then out[n] = k n = n + 1 end end
    return js_array(out, n)
  end,
  assign = js_merge,
  entries = function(t)
    local out, n = {}, 0
    for k, v in pairs(t) do
      if k ~= "length" then out[n] = js_array({ [0] = k, [1] = v }, 2) n = n + 1 end
    end
    return js_array(out, n)
  end,
}

-- Enough of the browser that a page's guards resolve. `location` is nil so
-- `typeof location !== 'undefined'` takes its else branch, which is what a page means by it.
console = { log = function() end, warn = function() end, error = function() end }
localStorage = { getItem = function() return nil end, setItem = function() end }
location = nil
window = { innerWidth = 0, innerHeight = 0 }
performance = { now = function() return 0 end }
JSON = { stringify = js_str, parse = function() return nil end }

-- ---- the DOM, as far as a compiled page needs one -------------------------------------------------
--
-- A page's script only ever reaches the document to WRITE: a style property, some text, a class.
-- Each write is recorded against the element's id, and the compiler turns those records into slot
-- writes on the vector scene. Nothing here reads layout, because by the time this runs the layout
-- has already happened, once, in the compiler.

DOM = { writes = {}, order = {}, missing = {} }

function DOM.reset()
  DOM.writes, DOM.order, DOM.missing = {}, {}, {}
end

-- A page assigning `undefined` has still written: `log.scrollTop = log.scrollHeight` does exactly
-- that here, since nothing lays out and scrollHeight is not a thing a compiled page has. Storing
-- nil would delete the entry instead, so the record would say the page never touched it.
UNDEFINED = setmetatable({}, { __tostring = function() return "undefined" end })

local function record(id, key, value)
  local slot = id .. "." .. key
  if DOM.writes[slot] == nil then DOM.order[#DOM.order + 1] = slot end
  DOM.writes[slot] = value == nil and UNDEFINED or value
end

-- classList is a view over className, as it is in the DOM: adding a class writes className, which
-- is an ordinary recorded write, so a page that styles by class reaches the scene the same way one
-- that assigns className does. Derived from className on every call rather than cached, because a
-- page is free to assign className directly between two classList calls.
local function classList(el)
  local function read()
    local names, seen = {}, {}
    for word in js_str(rawget(el, "__props").className or ""):gmatch("%S+") do
      if not seen[word] then seen[word] = true names[#names + 1] = word end
    end
    return names, seen
  end
  local function write(names) el.className = concat(names, " ") end
  local cl
  cl = {
    add = function(...)
      local names, seen = read()
      for i = 1, select("#", ...) do
        local c = js_str(select(i, ...))
        if not seen[c] then seen[c] = true names[#names + 1] = c end
      end
      write(names)
    end,
    remove = function(...)
      local names = read()
      local drop = {}
      for i = 1, select("#", ...) do drop[js_str(select(i, ...))] = true end
      local kept = {}
      for _, c in ipairs(names) do if not drop[c] then kept[#kept + 1] = c end end
      write(kept)
    end,
    contains = function(c) local _, seen = read() return seen[js_str(c)] == true end,
    -- force is a tri-state: absent means toggle, present means set. `false` is a value here, so it
    -- cannot be tested for truthiness the way an optional argument usually is.
    toggle = function(c, force)
      local has = cl.contains(c)
      local want
      if select("#", c, force) < 2 or force == nil then want = not has else want = js_truthy(force) end
      if want then cl.add(c) else cl.remove(c) end
      return want
    end,
  }
  return cl
end

local ElementMeta = {}
ElementMeta.__index = function(el, key)
  if key == "style" then return rawget(el, "__style") end
  if key == "classList" then return rawget(el, "__classList") end
  if key == "setAttribute" then return function(name, value) DOM.attribute(el, name, value) end end
  if key == "addEventListener" then return function() end end
  if key == "getAttribute" then return function() return nil end end
  return rawget(el, "__props")[key]
end
ElementMeta.__newindex = function(el, key, value)
  rawget(el, "__props")[key] = value
  record(rawget(el, "__id"), key, value)
end

local StyleMeta = {}
StyleMeta.__index = function(st, key) return rawget(st, "__props")[key] end
StyleMeta.__newindex = function(st, key, value)
  rawget(st, "__props")[key] = value
  record(rawget(st, "__id"), "style." .. key, value)
end

local elements = {}

local function element(id)
  local el = elements[id]
  if el then return el end
  el = setmetatable({ __id = id, __props = {}, __style = setmetatable({ __id = id, __props = {} }, StyleMeta) }, ElementMeta)
  rawset(el, "__classList", classList(el))
  elements[id] = el
  return el
end

document = {
  getElementById = function(id)
    -- An id the compiled scene does not carry is worth saying so about, once: a page writing to an
    -- element that is not there looks exactly like a page that does nothing.
    if DOM.known and not DOM.known[id] then DOM.missing[id] = true end
    return element(id)
  end,
  querySelector = function() return nil end,
  querySelectorAll = function() return js_array({}, 0) end,
  addEventListener = function() end,
  body = element("body"),
  documentElement = element("html"),
  createElement = function(tag) return element("__new_" .. tag) end,
}
-- setAttribute is a write like any other, recorded against the element it names.
function DOM.attribute(el, name, value) record(rawget(el, "__id"), "@" .. name, value) end

-- A page may schedule work. Both are recorded rather than run: the compiler decides what becomes an
-- on_frame chain and what becomes a tick, and neither is this prelude's business.
Pending = { frame = {}, timers = {} }
function requestAnimationFrame(fn) Pending.frame[#Pending.frame + 1] = fn return #Pending.frame end
function setInterval(fn, ms) Pending.timers[#Pending.timers + 1] = { fn = fn, ms = ms } return #Pending.timers end
function setTimeout(fn, ms) Pending.timers[#Pending.timers + 1] = { fn = fn, ms = ms, once = true } return #Pending.timers end
function clearInterval() end
