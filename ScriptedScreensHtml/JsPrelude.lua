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

-- An exponent as JavaScript writes one. The interpreter the game embeds formats `%g` and `%e` with
-- a CAPITAL E and pads the exponent to three digits, where C and JavaScript use a small e and the
-- fewest digits: `1.23E+004` against `1.23e+4`. The standalone Lua the prelude's own checks run on
-- agrees with C, so every number a page printed in exponential form came out wrong in game and
-- right in the test - which is why this went unnoticed. Every %g and %e result goes through here.
local function jsExponent(text)
  text = text:gsub("E", "e")
  return (text:gsub("e([-+])0*(%d)", "e%1%2"))
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
      if tonumber(text) == v then return jsExponent(text) end
    end
    return jsExponent(format("%.17g", v))
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

-- ---- bitwise ----------------------------------------------------------------------------------
--
-- JavaScript's bitwise operators are defined on 32-bit signed integers: both operands are truncated
-- to int32, the operation is done there, and the result comes back as a signed 32-bit number. Lua's
-- own operators are 64-bit, so `~a` and `a << 1` agree with JavaScript for small positive numbers
-- and diverge everywhere else - silently, and only for the inputs a page hits in anger.
--
-- The one exception is `>>>`, which is the only operator that yields an UNSIGNED result, so it is
-- the only one that does not come back through ToInt32.

local function to_uint32(v)
  v = js_num(v)
  if v ~= v or v == math.huge or v == -math.huge then return 0 end
  v = v < 0 and -floor(-v) or floor(v)
  v = v % 4294967296
  if v < 0 then v = v + 4294967296 end
  return v
end

local function to_int32(v)
  local n = to_uint32(v)
  if n >= 2147483648 then n = n - 4294967296 end
  return n
end

-- Written as arithmetic rather than with Lua's own `&` and `<<`, because the interpreter the game
-- embeds does not have them: a prelude containing one fails to PARSE, which takes down every page
-- rather than only the one doing bitwise. Caught by the test that runs each page under that same
-- interpreter, which is the only thing standing between this and a mod that loads nothing.
local function pairwise(a, b, op)
  local x, y = to_uint32(a), to_uint32(b)
  local result, bit = 0, 1
  for _ = 1, 32 do
    local xb, yb = x % 2, y % 2
    if op(xb, yb) == 1 then result = result + bit end
    x = (x - xb) / 2
    y = (y - yb) / 2
    bit = bit * 2
    if x == 0 and y == 0 then break end
  end
  return result
end

local function op_and(x, y) if x == 1 and y == 1 then return 1 end return 0 end
local function op_or(x, y) if x == 1 or y == 1 then return 1 end return 0 end
local function op_xor(x, y) if x ~= y then return 1 end return 0 end

function js_band(a, b) return to_int32(pairwise(a, b, op_and)) end
function js_bor(a, b) return to_int32(pairwise(a, b, op_or)) end
function js_bxor(a, b) return to_int32(pairwise(a, b, op_xor)) end
function js_bnot(a) return to_int32(4294967295 - to_uint32(a)) end

-- Only the low five bits of the shift count are used, as in JavaScript: `1 << 32` is 1, not 0.
local function shift_by(b) return to_uint32(b) % 32 end

function js_shl(a, b) return to_int32(to_uint32(a) * (2 ^ shift_by(b)) % 4294967296) end
function js_shr(a, b)
  -- Arithmetic shift: the sign is preserved, so -8 >> 1 is -4 rather than a huge positive.
  local n, by = to_int32(a), shift_by(b)
  local shifted = n / (2 ^ by)
  return to_int32(shifted >= 0 and floor(shifted) or -ceil(-shifted) - (n % (2 ^ by) ~= 0 and 1 or 0))
end
function js_ushr(a, b) return floor(to_uint32(a) / (2 ^ shift_by(b))) end

-- ---- membership and identity --------------------------------------------------------------------

-- `k in o`. On an array this asks about an INDEX, not a value, which is the part people get wrong -
-- and which is why `'length' in xs` is true while `1 in [9]` is false for a one-element array.
function js_in(key, obj)
  if type(obj) ~= "table" then return false end
  if type(key) == "number" then
    local n = obj.length
    if n ~= nil then return key >= 0 and key < n end
  end
  return obj[key] ~= nil
end

-- `x instanceof C`, walking the chain js_class builds.
function js_instanceof(value, class)
  if type(value) ~= "table" or type(class) ~= "table" then return false end
  local proto = class.__proto
  if proto == nil then return false end
  local mt = getmetatable(value)
  while mt ~= nil do
    if mt == proto then return true end
    local base = mt.__baseproto
    mt = base
  end
  return false
end

-- Spreading an array into an argument list. Bounds are explicit because these arrays are 0-based
-- with their own length field, which table.unpack cannot infer.
function js_spread(t)
  if type(t) ~= "table" then return end
  return table.unpack(t, 0, (t.length or 0) - 1)
end

-- `[a, ...xs, b]`: the pieces in order, each either a single value or an array to flatten.
function js_concat(...)
  local out, n = {}, 0
  for i = 1, select("#", ...) do
    local piece = select(i, ...)
    if type(piece) == "table" and piece.__spread then
      local items = piece[1]
      for k = 0, (items.length or 0) - 1 do out[n] = items[k] n = n + 1 end
    else
      out[n] = piece n = n + 1
    end
  end
  return js_array(out, n)
end

function js_spread_of(items) return { __spread = true, items } end

-- An object literal with setters as well as getters. Separate from js_getters because a plain
-- __index costs nothing and __newindex on every write does not.
function js_accessors(fields, getters, setters)
  return setmetatable(fields, {
    __index = function(self, key)
      local g = getters and getters[key]
      if g then return g(self) end
      return nil
    end,
    __newindex = function(self, key, value)
      local s = setters and setters[key]
      if s then s(self, value) return end
      rawset(self, key, value)
    end,
  })
end

function js_delete(t, k)
  if type(t) == "table" then t[k] = nil end
  return true
end

-- ---- classes --------------------------------------------------------------------------------------
--
-- A class is a prototype table used as the instance metatable, plus a class table holding the
-- statics. They are two namespaces in JavaScript - a static `make` and an instance `make` are
-- different functions - so they are two tables here rather than one, which is the only way a page
-- that has both keeps working.
--
-- Inheritance is flattened at definition time: a subclass copies its base's methods and accessors
-- into its own prototype, so looking a method up is one table deep however long the chain is. A
-- chain walk per property read would be on the per-frame path of every compiled page that uses a
-- class, which is the whole reason the page was compiled.
--
-- `__jsclass` on the prototype is what tells the method dispatcher that a function found through it
-- takes the instance as its first argument. A function the page stored on a plain object is an
-- arrow function and takes none, and calling one of those with a receiver shifts every argument.

function js_class(name, base, methods, getters, setters, statics)
  local proto, get, set = {}, {}, {}

  if base ~= nil then
    for k, v in pairs(base.__proto) do
      -- not the metamethods: this prototype gets its own below, and inheriting the base's would
      -- make every instance of the subclass look its methods up in the base
      if k ~= "__index" and k ~= "__newindex" then proto[k] = v end
    end
    for k, v in pairs(base.__getters) do get[k] = v end
    for k, v in pairs(base.__setters) do set[k] = v end
  end

  if methods ~= nil then
    for k, v in pairs(methods) do
      -- Field initialisers chain base-first, so a subclass's fields are set after its base's. The
      -- rest simply override.
      if k == "__fields" and proto.__fields ~= nil then
        local inherited, own = proto.__fields, v
        proto.__fields = function(obj) inherited(obj) own(obj) end
      else
        proto[k] = v
      end
    end
  end
  if getters ~= nil then for k, v in pairs(getters) do get[k] = v end end
  if setters ~= nil then for k, v in pairs(setters) do set[k] = v end end

  proto.__jsclass = true
  -- The chain instanceof walks. Flattening the methods means the prototype does not reference its
  -- base any more, so the relationship has to be recorded separately or `x instanceof Base` is false
  -- for every subclass instance.
  proto.__baseproto = base ~= nil and base.__proto or nil
  -- A plain table __index is a raw lookup the VM does itself; the function form costs a Lua call on
  -- every property read, so it is used only by a class that really declares an accessor.
  if next(get) == nil then
    proto.__index = proto
  else
    proto.__index = function(obj, key)
      local g = get[key]
      if g ~= nil then return g(obj) end
      return proto[key]
    end
  end
  if next(set) ~= nil then
    proto.__newindex = function(obj, key, value)
      local s = set[key]
      if s ~= nil then s(obj, value) return end
      rawset(obj, key, value)
    end
  end

  local cls = { __name = name, __base = base, __proto = proto, __getters = get, __setters = set }
  if statics ~= nil then for k, v in pairs(statics) do cls[k] = v end end
  return cls
end

function js_new(cls, ...)
  if type(cls) ~= "table" or cls.__proto == nil then
    error("new on " .. tostring(cls) .. ", which is not a class", 2)
  end
  local obj = setmetatable({}, cls.__proto)
  local fields = cls.__proto.__fields
  if fields ~= nil then fields(obj) end
  local ctor = cls.__proto.__ctor
  if ctor ~= nil then ctor(obj, ...) end
  return obj
end

-- ---- the built-in constructors --------------------------------------------------------------------
--
-- Written as classes so they reach the method dispatcher by the same route a page's own class does,
-- and so `size` can be the property JavaScript has rather than a method nobody writes.
--
-- Keys are held in a plain table and insertion order in a list beside it, because JavaScript's Map
-- and Set both iterate in insertion order and `pairs` does not. A page that builds a legend or a
-- table from a Map and gets it in a different order every session would look broken for a reason
-- nothing in it explains.

local function ordered_delete(keys, key)
  for i = 1, #keys do if keys[i] == key then table.remove(keys, i) return end end
end

local MapClass = js_class("Map", nil, {
  __ctor = function(self, entries)
    self.__keys, self.__values = {}, {}
    if type(entries) == "table" then
      for i = 0, (entries.length or 0) - 1 do
        local pair = entries[i]
        if type(pair) == "table" then js_m(self, "set", pair[0], pair[1]) end
      end
    end
  end,
  get = function(self, key) return self.__values[key] end,
  set = function(self, key, value)
    if self.__values[key] == nil then self.__keys[#self.__keys + 1] = key end
    self.__values[key] = value
    return self
  end,
  has = function(self, key) return self.__values[key] ~= nil end,
  delete = function(self, key)
    if self.__values[key] == nil then return false end
    self.__values[key] = nil
    ordered_delete(self.__keys, key)
    return true
  end,
  clear = function(self) self.__keys, self.__values = {}, {} end,
  forEach = function(self, fn)
    for i = 1, #self.__keys do local k = self.__keys[i] fn(self.__values[k], k, self) end
  end,
  keys = function(self) return js_array_of(self.__keys) end,
  values = function(self)
    local out = {}
    for i = 1, #self.__keys do out[i] = self.__values[self.__keys[i]] end
    return js_array_of(out)
  end,
  entries = function(self)
    local out = {}
    for i = 1, #self.__keys do
      local k = self.__keys[i]
      out[i] = js_array({ [0] = k, [1] = self.__values[k] }, 2)
    end
    return js_array_of(out)
  end,
}, { size = function(self) return #self.__keys end })

local SetClass = js_class("Set", nil, {
  __ctor = function(self, items)
    self.__keys, self.__has = {}, {}
    if type(items) == "table" then
      for i = 0, (items.length or 0) - 1 do js_m(self, "add", items[i]) end
    end
  end,
  add = function(self, value)
    if not self.__has[value] then
      self.__has[value] = true
      self.__keys[#self.__keys + 1] = value
    end
    return self
  end,
  has = function(self, value) return self.__has[value] == true end,
  delete = function(self, value)
    if not self.__has[value] then return false end
    self.__has[value] = nil
    ordered_delete(self.__keys, value)
    return true
  end,
  clear = function(self) self.__keys, self.__has = {}, {} end,
  forEach = function(self, fn)
    for i = 1, #self.__keys do fn(self.__keys[i], self.__keys[i], self) end
  end,
  values = function(self) return js_array_of(self.__keys) end,
  keys = function(self) return js_array_of(self.__keys) end,
}, { size = function(self) return #self.__keys end })

-- A 1-based Lua list as the 0-based array the rest of this prelude uses.
function js_array_of(list)
  local out = {}
  for i = 1, #list do out[i - 1] = list[i] end
  return js_array(out, #list)
end

function js_map(entries) return js_new(MapClass, entries) end
function js_set(items) return js_new(SetClass, items) end

-- `new Array(5)` is five empty slots; `new Array(1, 2)` is those two values. The one-number form is
-- the one pages write, and reading it as a single element would silently build the wrong array.
function js_new_array(...)
  local n = select("#", ...)
  if n == 1 and type((...)) == "number" then return js_array({}, (...)) end
  local out = {}
  for i = 1, n do out[i - 1] = (select(i, ...)) end
  return js_array(out, n)
end

function js_new_object() return {} end

function js_error(message)
  return { name = "Error", message = message == nil and "" or js_str(message),
           stack = "", __error = true }
end

-- ---- Promise ----------------------------------------------------------------------------------
--
-- A promise's reactions never run inside the call that settles it: they go on the microtask
-- queue, which a browser drains the moment the running script returns. This chunk is driven once
-- a frame, so js_microtasks() is called where a script "returns" here - after the page's own
-- top-level code, at the end of each frame's callbacks, and after every event delivered - which
-- is the finest grain there is. The queue is one flat list kept for the life of the chunk, three
-- slots a job (the function and two arguments), so queueing allocates nothing and draining an
-- empty queue is one comparison: a page that never makes a promise pays nothing per frame.
-- Those three calls belong to the chunk's runtime, not to this prelude: nothing here drains on its
-- own, because a drain inside a call the page made - `el.click()`, `dispatchEvent` - would run the
-- reactions BEFORE the rest of the page's own line, which is the one order a browser never gives.
--
-- Not here: async/await. That is a translation - a function body has to become resumable - and it
-- belongs to the compiler, which refuses it by name.

local PENDING, FULFILLED, REJECTED = 0, 1, 2
local MT, MT_head, MT_tail = {}, 0, 0

local function enqueue(job, a, b)
  MT[MT_tail + 1], MT[MT_tail + 2], MT[MT_tail + 3] = job, a, b
  MT_tail = MT_tail + 3
end

-- A job that throws propagates to the host, as a timer callback's error does, and the jobs behind
-- it are kept for the next drain: the head has moved past the failed one before it runs.
function js_microtasks()
  while MT_head < MT_tail do                 -- a job may queue more, and they run in this drain
    local i = MT_head + 1
    MT_head = i + 2
    local job, a, b = MT[i], MT[i + 1], MT[i + 2]
    MT[i], MT[i + 1], MT[i + 2] = nil, nil, nil
    job(a, b)
  end
  MT_head, MT_tail = 0, 0
end

function queueMicrotask(fn) enqueue(fn) end

local PromiseClass
local settle, resolvePromise

local function isPromise(v) return type(v) == "table" and rawget(v, "__promise") == true end

-- A promise made from inside, with no executor to run.
local function fresh() return setmetatable({ __promise = true, __state = PENDING }, PromiseClass.__proto) end

-- One reaction is three slots on the promise: the two handlers (false to pass the outcome
-- through) and the promise the handler's result settles (false for none - the combinators keep
-- their own). A handler's throw is the child's rejection, as in a browser; nothing escapes here.
local function runReaction(p, i)
  local r = p.__reactions
  local handler
  if p.__state == FULFILLED then handler = r[i] else handler = r[i + 1] end
  local child = r[i + 2]
  if not handler then
    if child then settle(child, p.__state, p.__value) end
    return
  end
  local ok, v = pcall(handler, p.__value)
  if not child then return end
  if ok then resolvePromise(child, v) else settle(child, REJECTED, v) end
end

local function react(p, onFul, onRej, child)
  local r = p.__reactions
  if r == nil then r = {} p.__reactions = r end
  local n = #r
  r[n + 1], r[n + 2], r[n + 3] = onFul or false, onRej or false, child or false
  if p.__state ~= PENDING then enqueue(runReaction, p, n + 1) end
end

-- A rejection nobody handles is noted, as a browser logs "Uncaught (in promise)". The check is a
-- job behind the rejection rather than a test at the rejection, so a catch attached later in the
-- same script - `var p = Promise.reject(e); p.catch(f)` - counts as handling. Without this a
-- page whose chain threw looked like a page that had stopped, with nothing anywhere to say why.
local UNHANDLED = "an unhandled promise rejection: a promise was rejected and nothing had a then/catch on it by the time the microtasks ran"
local function checkUnhandled(p)
  if p.__reactions ~= nil then return end
  local v = p.__value
  DOM.note(UNHANDLED, type(v) == "table" and v.message or js_str(v))
end

settle = function(p, state, value)
  if p.__state ~= PENDING then return end
  p.__state, p.__value = state, value
  local r = p.__reactions
  if r ~= nil then for i = 1, #r, 3 do enqueue(runReaction, p, i) end
  elseif state == REJECTED then enqueue(checkUnhandled, p) end
end

-- Resolving with a promise adopts it; with a foreign thenable - an object carrying its own then()
-- - asks it to call back; with anything else, fulfils.
resolvePromise = function(p, v)
  if p.__state ~= PENDING then return end
  if v == p then settle(p, REJECTED, js_error("a promise cannot resolve to itself")) return end
  if isPromise(v) then react(v, false, false, p) return end
  if type(v) == "table" and type(v["then"]) == "function" then
    local ok, err = pcall(js_m, v, "then",
      function(x) resolvePromise(p, x) end, function(e) settle(p, REJECTED, e) end)
    if not ok then settle(p, REJECTED, err) end
    return
  end
  settle(p, FULFILLED, v)
end

local function aggregate(errors)
  local e = js_error("All promises were rejected")
  e.name, e.errors = "AggregateError", errors
  return e
end

-- all, allSettled, any and race: one result, one reaction per input, and which outcome ends it.
local function combine(list, kind)
  local out = fresh()
  local n = js_len(list)
  if n == 0 then
    if kind == "any" then settle(out, REJECTED, aggregate(js_array({}, 0)))
    elseif kind ~= "race" then settle(out, FULFILLED, js_array({}, 0)) end   -- race stays pending
    return out
  end
  local results, left = {}, n
  for i = 0, n - 1 do
    local item = list[i]
    local p = item
    if not isPromise(p) then p = fresh() resolvePromise(p, item) end
    react(p,
      function(v)
        if kind == "all" then results[i] = v
        elseif kind == "allSettled" then results[i] = { status = "fulfilled", value = v }
        else settle(out, FULFILLED, v) return end             -- race and any: the first to fulfil
        left = left - 1
        if left == 0 then settle(out, FULFILLED, js_array(results, n)) end
      end,
      function(e)
        if kind == "any" then results[i] = e
        elseif kind == "allSettled" then results[i] = { status = "rejected", reason = e }
        else settle(out, REJECTED, e) return end              -- all and race: the first to reject
        left = left - 1
        if left == 0 then
          if kind == "any" then settle(out, REJECTED, aggregate(js_array(results, n)))
          else settle(out, FULFILLED, js_array(results, n)) end
        end
      end, false)
  end
  return out
end

PromiseClass = js_class("Promise", nil, {
  __ctor = function(self, executor)
    self.__promise, self.__state = true, PENDING
    if type(executor) ~= "function" then
      error("Promise resolver " .. js_str(executor) .. " is not a function")
    end
    local ok, err = pcall(executor,
      function(v) resolvePromise(self, v) end, function(e) settle(self, REJECTED, e) end)
    if not ok then settle(self, REJECTED, err) end
  end,
  -- `then` is a Lua keyword, so it is spelled as a key; the dispatcher reaches it by name anyway.
  ["then"] = function(self, onFul, onRej)
    local child = fresh()
    react(self, type(onFul) == "function" and onFul or false,
                type(onRej) == "function" and onRej or false, child)
    return child
  end,
  catch = function(self, onRej) return js_m(self, "then", nil, onRej) end,
  -- The callback runs either way and the outcome passes through it, unless it throws - or returns
  -- a promise, which is waited for first.
  finally = function(self, fn)
    if type(fn) ~= "function" then return js_m(self, "then") end
    return js_m(self, "then",
      function(v)
        local r = fn()
        if isPromise(r) then return js_m(r, "then", function() return v end) end
        return v
      end,
      function(e)
        local r = fn()
        if isPromise(r) then return js_m(r, "then", function() error(e, 0) end) end
        error(e, 0)
      end)
  end,
}, nil, nil, {
  resolve = function(v)
    if isPromise(v) then return v end
    local p = fresh()
    resolvePromise(p, v)
    return p
  end,
  reject = function(e) local p = fresh() settle(p, REJECTED, e) return p end,
  all = function(list) return combine(list, "all") end,
  allSettled = function(list) return combine(list, "allSettled") end,
  any = function(list) return combine(list, "any") end,
  race = function(list) return combine(list, "race") end,
})

Promise = PromiseClass

function js_promise(executor) return js_new(PromiseClass, executor) end

-- ---- Date -------------------------------------------------------------------------------------
--
-- `js_now()` counts milliseconds since the scene was applied, which is all a page needs for
-- elapsed time and is no use at all for a calendar: built on it, getFullYear() would answer 1970
-- for ever, which is the silent-wrong answer this prelude exists to avoid.
--
-- `EPOCH` closes the gap: the host sets it to the milliseconds between the Unix epoch and the
-- instant js_now() calls zero, taken from the GAME's own world clock rather than from the
-- machine's, because two clients reading different wall clocks would draw different pages. It is
-- read on every call, not captured here, so the host may set it after this prelude has run.
--
-- Until it exists, every calendar accessor REFUSES. Elapsed time - getTime, valueOf, Date.now -
-- works either way, because a difference of two readings does not need to know where zero is.
--
-- There is no timezone data either, so everything is UTC: getHours is the UTC hour and
-- getTimezoneOffset is 0. A console in a game has no local time to be local to, and a page that
-- rendered a different hour per player would be the same class of bug.

local MS_DAY = 86400000

-- Days since 1970-01-01 to a civil date, and back (Howard Hinnant's algorithms). Pure arithmetic:
-- the interpreter the game embeds has no bitwise operators, and a prelude using one does not parse.
local function civilFromDays(z)
  z = z + 719468
  local era = floor(z / 146097)
  local doe = z - era * 146097
  local yoe = floor((doe - floor(doe / 1460) + floor(doe / 36524) - floor(doe / 146096)) / 365)
  local y = yoe + era * 400
  local doy = doe - (365 * yoe + floor(yoe / 4) - floor(yoe / 100))
  local mp = floor((5 * doy + 2) / 153)
  local d = doy - floor((153 * mp + 2) / 5) + 1
  local m = mp + (mp < 10 and 3 or -9)
  if m <= 2 then y = y + 1 end
  return y, m, d
end

local function daysFromCivil(y, m, d)
  if m <= 2 then y = y - 1 end
  local era = floor(y / 400)
  local yoe = y - era * 400
  local doy = floor((153 * (m + (m > 2 and -3 or 9)) + 2) / 5) + d - 1
  local doe = yoe * 365 + floor(yoe / 4) - floor(yoe / 100) + doy
  return era * 146097 + doe - 719468
end

-- Everything a calendar accessor needs, from one division. Returns nothing when the date is not a
-- real instant, so each accessor answers NaN rather than a plausible 1970.
local function parts(self)
  local ms = self.__ms
  if type(ms) ~= "number" or ms ~= ms then return nil end
  if EPOCH == nil then
    error("Date's calendar accessors need the game's world clock, which the host has not set"
          .. " (EPOCH). getTime, valueOf and Date.now work without it.")
  end
  local day = floor(ms / MS_DAY)
  local rest = ms - day * MS_DAY
  local y, mo, d = civilFromDays(day)
  return y, mo, d, floor(rest / 3600000), floor(rest / 60000) % 60, floor(rest / 1000) % 60,
         rest % 1000, (day + 4) % 7
end

local function pad(v, width)
  local text = format("%d", abs(v))
  return (v < 0 and "-" or "") .. rep("0", math.max(0, width - #text)) .. text
end

-- The ISO forms a page writes: a date, a date and time, with or without fractional seconds and a
-- trailing Z. Anything else is an Invalid Date, as it is in a browser, rather than a guess.
local function parseDate(text)
  text = js_str(text)
  local y, mo, d = text:match("^(%-?%d+)-(%d%d)-(%d%d)")
  if y == nil then return 0 / 0 end
  local ms = daysFromCivil(tonumber(y), tonumber(mo), tonumber(d)) * MS_DAY
  local h, mi, s = text:match("[T ](%d%d):(%d%d):?(%d*)")
  if h ~= nil then
    ms = ms + tonumber(h) * 3600000 + tonumber(mi) * 60000 + (tonumber(s) or 0) * 1000
    local frac = text:match("%.(%d%d?%d?)")
    if frac ~= nil then ms = ms + tonumber(frac .. rep("0", 3 - #frac)) end
  end
  return ms
end

local DateClass = js_class("Date", nil, {
  __ctor = function(self, ms) self.__date = true self.__ms = ms end,
  getTime = function(self) return self.__ms end,
  valueOf = function(self) return self.__ms end,
  toString = function(self) return js_m(self, "toISOString") end,
  getFullYear = function(self) local y = parts(self) return y or 0 / 0 end,
  getMonth = function(self) local _, mo = parts(self) return mo ~= nil and mo - 1 or 0 / 0 end,
  getDate = function(self) local _, _, d = parts(self) return d or 0 / 0 end,
  getHours = function(self) local _, _, _, h = parts(self) return h or 0 / 0 end,
  getMinutes = function(self) local _, _, _, _, mi = parts(self) return mi or 0 / 0 end,
  getSeconds = function(self) local _, _, _, _, _, s = parts(self) return s or 0 / 0 end,
  getMilliseconds = function(self) local _, _, _, _, _, _, ms = parts(self) return ms or 0 / 0 end,
  getDay = function(self) local _, _, _, _, _, _, _, w = parts(self) return w or 0 / 0 end,
  -- UTC throughout, so there is no offset to report.
  getTimezoneOffset = function() return 0 end,
  toISOString = function(self)
    local y, mo, d, h, mi, s, ms = parts(self)
    if y == nil then return "Invalid Date" end
    return pad(y, 4) .. "-" .. pad(mo, 2) .. "-" .. pad(d, 2) .. "T" .. pad(h, 2) .. ":"
           .. pad(mi, 2) .. ":" .. pad(s, 2) .. "." .. pad(ms, 3) .. "Z"
  end,
  -- A fixed format, not a locale one: there is no locale data here, and a console that showed a
  -- different date per player would be a bug nobody could reproduce.
  toLocaleDateString = function(self)
    local y, mo, d = parts(self)
    if y == nil then return "Invalid Date" end
    return pad(y, 4) .. "-" .. pad(mo, 2) .. "-" .. pad(d, 2)
  end,
  toLocaleTimeString = function(self)
    local y, _, _, h, mi, s = parts(self)
    if y == nil then return "Invalid Date" end
    return pad(h, 2) .. ":" .. pad(mi, 2) .. ":" .. pad(s, 2)
  end,
})

--- `new Date()`, `new Date(ms)`, `new Date(iso)` and `new Date(y, m, d, h, mi, s, ms)`.
--- The component form used to be read as its first argument alone, so `new Date(2026, 0, 1)` was
--- an instant two seconds after the epoch and said nothing about it.
function js_date(a, b, c, d, e, f, g)
  local ms
  if a == nil then
    ms = (EPOCH or 0) + (js_now and js_now() or 0)
  elseif b == nil then
    if type(a) == "table" and a.__date then ms = a.__ms
    elseif type(a) == "string" then ms = parseDate(a)
    else ms = js_num(a) end
  else
    -- The month is 0-based in JavaScript and 1-based in a calendar, and every other field rolls
    -- over: `new Date(2026, 0, 32)` is the first of February. A month is not a fixed number of
    -- days, so the month is carried into the year and the rest is added as milliseconds.
    local y, mo = floor(js_num(a)), floor(js_num(b))
    y = y + floor(mo / 12)
    mo = mo % 12
    ms = (daysFromCivil(y, mo + 1, 1) + (c == nil and 1 or floor(js_num(c))) - 1) * MS_DAY
         + (d == nil and 0 or floor(js_num(d))) * 3600000
         + (e == nil and 0 or floor(js_num(e))) * 60000
         + (f == nil and 0 or floor(js_num(f))) * 1000
         + (g == nil and 0 or floor(js_num(g)))
  end
  return js_new(DateClass, ms)
end

-- ---- regular expressions --------------------------------------------------------------------
--
-- A real backtracking engine, because a Lua pattern is NOT a regular expression - it has no
-- alternation, no grouped quantifier and no backreference - so translating to one would quietly
-- match something else. Until this existed the transpiler refused any pattern with syntax in it,
-- which is the honest half of the same problem and left `.replace(/[0-9]+/g, '')` uncompilable.
--
-- The shape is Thompson's compilation walked by a recursive backtracker (Cox's "regexp2"): the
-- pattern becomes a flat instruction array, and the only recursion is at `split`, `save`, `mark`
-- and `look` - the points where a choice has to be undone. There is no continuation closure and no
-- table allocated per character, which matters because this runs inside a page's render loop.
--
-- It works on BYTES. For ASCII - every console page here - that is exactly the browser's answer.
-- Beyond it, a literal in the pattern still matches its own UTF-8 bytes (a multi-byte literal is
-- compiled as a sequence, so a quantifier still applies to the whole character), but `.` counts one
-- byte, `\w` and `\s` are the ASCII sets, and a multi-byte escape inside `[ ]` is refused rather
-- than half-matched.

-- Codepoints to text, UTF-8 encoded by hand rather than through `utf8.char`, which the interpreter
-- the game embeds is not guaranteed to have - and a prelude that fails to parse takes down every
-- page rather than only the one that called this.
local function utf8char(code)
  code = floor(js_num(code))
  if code < 0 or code ~= code then return "" end
  if code < 128 then return string.char(code) end
  if code < 2048 then
    return string.char(192 + floor(code / 64), 128 + code % 64)
  end
  if code < 65536 then
    return string.char(224 + floor(code / 4096), 128 + floor(code / 64) % 64, 128 + code % 64)
  end
  return string.char(240 + floor(code / 262144), 128 + floor(code / 4096) % 64,
                     128 + floor(code / 64) % 64, 128 + code % 64)
end

local strbyte = string.byte

-- `\d`, `\w` and `\s` as byte sets, built once. \s is the ASCII whitespace only: JavaScript's also
-- includes U+00A0 and the Unicode spaces, whose UTF-8 bytes are continuation bytes that would match
-- in the middle of an unrelated character if they were listed here.
local RX_D, RX_W, RX_S = {}, {}, {}
for b = 48, 57 do RX_D[b] = true RX_W[b] = true end
for b = 65, 90 do RX_W[b] = true end
for b = 97, 122 do RX_W[b] = true end
RX_W[95] = true
RX_S[9] = true RX_S[10] = true RX_S[11] = true RX_S[12] = true RX_S[13] = true RX_S[32] = true

local RX_CLASSES = { d = RX_D, D = RX_D, w = RX_W, W = RX_W, s = RX_S, S = RX_S }
local RX_NEGATED = { D = true, W = true, S = true }
local RX_CONTROL = { n = 10, r = 13, t = 9, f = 12, v = 11, ["0"] = 0 }

-- Pattern text to instruction array. Two passes: a small tree, then emission, because emitting
-- alternation and quantifiers straight would mean inserting instructions in front of ones already
-- written and patching every address after them.
local function rx_compile(src, flags)
  local icase = flags:find("i", 1, true) ~= nil
  local dotall = flags:find("s", 1, true) ~= nil
  local pos, len = 1, #src
  local groups, names, marks = 0, nil, 0

  local function fail(why)
    error("regular expression /" .. src .. "/" .. flags .. ": " .. why, 0)
  end
  local function peek() return sub(src, pos, pos) end
  local function take() pos = pos + 1 return sub(src, pos - 1, pos - 1) end

  -- Case folding is done into the SET, once, so matching stays a single table lookup. ASCII only:
  -- there is no case mapping for a byte above 127 that means anything on its own.
  local function addByte(set, b)
    set[b] = true
    if icase then
      if b >= 65 and b <= 90 then set[b + 32] = true
      elseif b >= 97 and b <= 122 then set[b - 32] = true end
    end
  end

  local function hexAt(count)
    local text = sub(src, pos, pos + count - 1)
    if #text < count or text:find("[^0-9a-fA-F]") ~= nil then return nil end
    pos = pos + count
    return tonumber(text, 16)
  end

  -- The bytes one escape stands for. An unrecognised `\q` is the character itself, as a browser
  -- reads it outside unicode mode.
  local function literalEscape(c)
    local ctl = RX_CONTROL[c]
    if ctl ~= nil then return string.char(ctl) end
    if c == "x" then
      local v = hexAt(2)
      if v == nil then fail("\\x needs two hex digits") end
      return string.char(v)
    end
    if c == "u" then
      if peek() == "{" then
        local close = src:find("}", pos, true)
        if close == nil then fail("\\u{ without a closing }") end
        local v = tonumber(sub(src, pos + 1, close - 1), 16)
        if v == nil then fail("\\u{} needs hex digits") end
        pos = close + 1
        return utf8char(v)
      end
      local v = hexAt(4)
      if v == nil then fail("\\u needs four hex digits") end
      return utf8char(v)
    end
    if c == "c" then
      local letter = peek()
      if letter:match("%a") == nil then fail("\\c needs a letter") end
      pos = pos + 1
      return string.char(letter:byte() % 32)
    end
    if c == "p" or c == "P" then fail("\\p unicode properties are not supported") end
    return c
  end

  local function bytesNode(text)
    if #text == 1 then
      local set = {}
      addByte(set, strbyte(text))
      return { k = "set", set = set }
    end
    -- A multi-byte character is a SEQUENCE, so `é+` repeats the character and not its last byte.
    local items = {}
    for i = 1, #text do items[i] = { k = "set", set = { [strbyte(text, i)] = true } } end
    return { k = "seq", items = items }
  end

  local function parseClass()
    local set, neg = {}, false
    if peek() == "^" then neg = true pos = pos + 1 end
    while true do
      if pos > len then fail("[ without a closing ]") end
      local c = take()
      if c == "]" then break end
      local lo
      if c == "\\" then
        if pos > len then fail("a backslash at the end of the pattern") end
        local e = take()
        local cls = RX_CLASSES[e]
        if cls ~= nil then
          if RX_NEGATED[e] then
            for b = 0, 255 do if not cls[b] then set[b] = true end end
          else
            for b in pairs(cls) do set[b] = true end
          end
        elseif e == "b" then
          lo = 8                                   -- \b is a backspace inside a class
        else
          local text = literalEscape(e)
          if #text ~= 1 then fail("a multi-byte escape inside [ ] is not supported") end
          lo = strbyte(text)
        end
      else
        lo = strbyte(c)
      end
      if lo ~= nil then
        -- `a-z`, but a trailing `-` before the `]` is a literal one
        if peek() == "-" and pos + 1 <= len and sub(src, pos + 1, pos + 1) ~= "]" then
          pos = pos + 1
          local c2, hi = take(), nil
          if c2 == "\\" then
            local e2 = take()
            if RX_CLASSES[e2] ~= nil then fail("a class escape cannot be one end of a range") end
            local text = literalEscape(e2)
            if #text ~= 1 then fail("a multi-byte escape inside [ ] is not supported") end
            hi = strbyte(text)
          else
            hi = strbyte(c2)
          end
          if hi < lo then fail("a range whose ends are the wrong way round") end
          for b = lo, hi do addByte(set, b) end
        else
          addByte(set, lo)
        end
      end
    end
    return { k = "set", set = set, neg = neg }
  end

  local parseAlt

  local function parseAtom()
    local c = take()
    if c == "(" then
      local capture = nil
      if peek() == "?" then
        pos = pos + 1
        local kind = take()
        if kind == "=" or kind == "!" then
          local body = parseAlt()
          if take() ~= ")" then fail("( without a closing )") end
          return { k = "look", neg = kind == "!", node = body }
        elseif kind == "<" then
          local next1 = peek()
          if next1 == "=" or next1 == "!" then fail("lookbehind is not supported") end
          local close = src:find(">", pos, true)
          if close == nil then fail("(?< without a closing >") end
          groups = groups + 1
          capture = groups
          if names == nil then names = {} end
          names[sub(src, pos, close - 1)] = capture
          pos = close + 1
        elseif kind ~= ":" then
          fail("(?" .. kind .. " is not supported")
        end
      else
        groups = groups + 1
        capture = groups
      end
      local body = parseAlt()
      if take() ~= ")" then fail("( without a closing )") end
      return { k = "group", n = capture, node = body }
    end
    if c == "[" then return parseClass() end
    if c == "." then
      if dotall then return { k = "set", set = {}, neg = true } end
      return { k = "set", set = { [10] = true, [13] = true }, neg = true }
    end
    if c == "^" then return { k = "bol" } end
    if c == "$" then return { k = "eol" } end
    if c == ")" then fail("an unmatched )") end
    if c == "*" or c == "+" or c == "?" then fail("a quantifier with nothing to repeat") end
    if c == "\\" then
      if pos > len then fail("a backslash at the end of the pattern") end
      local e = take()
      if e == "b" then return { k = "wb" } end
      if e == "B" then return { k = "nwb" } end
      local cls = RX_CLASSES[e]
      if cls ~= nil then return { k = "set", set = cls, neg = RX_NEGATED[e] == true } end
      if e == "k" and peek() == "<" then
        local close = src:find(">", pos, true)
        if close == nil then fail("\\k< without a closing >") end
        local name = sub(src, pos + 1, close - 1)
        pos = close + 1
        return { k = "ref", name = name }
      end
      if e:match("[1-9]") ~= nil then
        local digits = e
        while peek():match("^%d$") ~= nil do digits = digits .. take() end
        return { k = "ref", n = tonumber(digits) }
      end
      return bytesNode(literalEscape(e))
    end
    return bytesNode(c)
  end

  local function parseSeq()
    local items = {}
    while pos <= len do
      local c = peek()
      if c == "|" or c == ")" then break end
      local atom = parseAtom()
      local min, max = nil, nil
      local q = peek()
      if q == "*" then min, max = 0, -1 pos = pos + 1
      elseif q == "+" then min, max = 1, -1 pos = pos + 1
      elseif q == "?" then min, max = 0, 1 pos = pos + 1
      elseif q == "{" then
        -- `{` that is not a valid quantifier is a literal one, so it is left for the next atom
        local close = src:find("}", pos, true)
        local body = close ~= nil and sub(src, pos + 1, close - 1) or ""
        local lo, hi = body:match("^(%d+),(%d*)$")
        if lo == nil then lo = body:match("^(%d+)$") hi = lo end
        if lo ~= nil then
          pos = close + 1
          min = tonumber(lo)
          max = hi == "" and -1 or tonumber(hi)
        end
      end
      if min ~= nil then
        local k = atom.k
        if k == "bol" or k == "eol" or k == "wb" or k == "nwb" then
          fail("a quantifier on an anchor")
        end
        local greedy = true
        if peek() == "?" then greedy = false pos = pos + 1 end
        if max ~= -1 and max < min then fail("a {n,m} whose ends are the wrong way round") end
        -- A bounded quantifier is unrolled, so an enormous one would be an enormous program.
        if max > 1000 or min > 1000 then fail("a quantifier above 1000 repeats") end
        atom = { k = "rep", node = atom, min = min, max = max, greedy = greedy }
      end
      items[#items + 1] = atom
    end
    if #items == 1 then return items[1] end
    return { k = "seq", items = items }
  end

  parseAlt = function()
    local branches = { parseSeq() }
    while peek() == "|" do
      pos = pos + 1
      branches[#branches + 1] = parseSeq()
    end
    if #branches == 1 then return branches[1] end
    return { k = "alt", branches = branches }
  end

  local prog = {}
  local function add(ins) prog[#prog + 1] = ins return ins end

  -- The capture numbers below a node, for the reset a repetition does at the start of each round.
  local function collectGroups(node, out)
    local k = node.k
    if k == "group" then
      if node.n ~= nil then out[#out + 1] = node.n end
      collectGroups(node.node, out)
    elseif k == "rep" or k == "look" then
      collectGroups(node.node, out)
    elseif k == "seq" then
      for i = 1, #node.items do collectGroups(node.items[i], out) end
    elseif k == "alt" then
      for i = 1, #node.branches do collectGroups(node.branches[i], out) end
    end
  end

  local emit
  emit = function(node)
    local k = node.k
    if k == "set" then
      add({ op = "set", set = node.set, neg = node.neg == true })
    elseif k == "seq" then
      for i = 1, #node.items do emit(node.items[i]) end
    elseif k == "bol" or k == "eol" or k == "wb" or k == "nwb" then
      add({ op = k })
    elseif k == "ref" then
      add({ op = "ref", n = node.n, name = node.name })
    elseif k == "group" then
      if node.n == nil then
        emit(node.node)
      else
        add({ op = "save", n = node.n * 2 - 1 })
        emit(node.node)
        add({ op = "save", n = node.n * 2 })
      end
    elseif k == "look" then
      local la = add({ op = "look", want = node.neg ~= true })
      la.x = #prog + 1
      local lo = marks + 1
      emit(node.node)
      add({ op = "lookend" })
      la.lo, la.hi = lo, marks
      la.next = #prog + 1
    elseif k == "alt" then
      local branches, jumps = node.branches, {}
      for i = 1, #branches - 1 do
        local split = add({ op = "split" })
        split.x = #prog + 1
        emit(branches[i])
        jumps[#jumps + 1] = add({ op = "jmp" })
        split.y = #prog + 1
      end
      emit(branches[#branches])
      for i = 1, #jumps do jumps[i].x = #prog + 1 end
    elseif k == "rep" then
      -- `a*`, `\d+`, `[^"]*`, `.*`: an unbounded repeat of ONE character test. Run as a loop with
      -- one back-off at a time instead of the general machinery, which recurses once per character
      -- and would put `.*` over a long string thousands of frames deep - fine in a standalone Lua
      -- and not something to bet a console on. Nothing about the meaning changes: a set always
      -- consumes a byte, so there is no empty iteration to guard and no capture inside to reset.
      if node.max == -1 and node.node.k == "set" then
        add({ op = node.greedy and "manyg" or "manyl", set = node.node.set,
              neg = node.node.neg == true, min = node.min })
        return
      end
      for _ = 1, node.min do emit(node.node) end
      if node.max == -1 then
        marks = marks + 1
        local id, head = marks, #prog + 1
        local split = add({ op = "split" })
        local body = #prog + 1
        add({ op = "mark", id = id })
        -- Every repetition starts with the groups inside it unset, as the spec's RepeatMatcher
        -- does: `(?:(a)|(b))+` over "ab" leaves group 1 undefined, because the iteration that
        -- filled it was followed by one that took the other branch.
        local inside = {}
        collectGroups(node.node, inside)
        for i = 1, #inside do
          add({ op = "clear", n = inside[i] * 2 - 1 })
          add({ op = "clear", n = inside[i] * 2 })
        end
        emit(node.node)
        -- The empty-iteration check belongs at the END of the body, not the start of the next one.
        -- Failing here unwinds the iteration - restoring its captures and letting the body's own
        -- alternation try its other branch - which is what makes `(|a)*` match "aa" rather than "".
        add({ op = "progress", id = id })
        add({ op = "jmp", x = head })
        local after = #prog + 1
        if node.greedy then split.x, split.y = body, after else split.x, split.y = after, body end
      else
        -- Each optional copy can be skipped, and skipping one skips every later one: all the
        -- splits leave to the same place, past the last copy.
        local splits = {}
        for _ = 1, node.max - node.min do
          local split = add({ op = "split" })
          split.x = #prog + 1
          splits[#splits + 1] = split
          emit(node.node)
        end
        local after = #prog + 1
        for i = 1, #splits do
          if node.greedy then splits[i].y = after
          else splits[i].x, splits[i].y = after, splits[i].x end
        end
      end
    end
  end

  emit(parseAlt())
  if pos <= len then fail("an unmatched )") end
  add({ op = "match" })

  for i = 1, #prog do
    local ins = prog[i]
    if ins.op == "ref" then
      if ins.name ~= nil then
        local g = names ~= nil and names[ins.name] or nil
        if g == nil then fail("\\k<" .. ins.name .. "> names no group") end
        ins.n = g
      end
      if ins.n > groups then fail("\\" .. ins.n .. " refers to a group this pattern has not got") end
      ins.a, ins.b = ins.n * 2 - 1, ins.n * 2
    end
  end

  -- caps and marks are scratch, kept on the compiled program and reused by every match: a fresh
  -- pair per call would be two tables of garbage per replace() in a render loop.
  prog.source = src
  return { prog = prog, groups = groups, names = names, nmarks = marks, icase = icase,
           caps = {}, marks = {} }
end

-- The subject of the match in flight. Module-level rather than upvalues of a closure per regex, so
-- a pattern costs one table and no closures; rx_search saves and restores them, which is what lets
-- a replacer function use a second regex of its own.
local RX_str, RX_len, RX_prog, RX_caps, RX_marks, RX_multi, RX_icase
local RX_steps = 0

local function rx_run(pc, sp)
  local prog = RX_prog
  while true do
    local ins = prog[pc]
    local op = ins.op
    if op == "set" then
      if sp > RX_len then return nil end
      local hit = ins.set[strbyte(RX_str, sp)]
      if ins.neg then
        if hit then return nil end
      elseif not hit then
        return nil
      end
      sp = sp + 1
      pc = pc + 1
    elseif op == "manyg" then
      -- As far as it will go, then one byte back at a time. Each continuation is tried from THIS
      -- frame and has returned before the next is, so the depth is flat however long the run is.
      local set, neg, str, limit = ins.set, ins.neg, RX_str, RX_len
      local from = sp
      while sp <= limit do
        local hit = set[strbyte(str, sp)]
        if neg then if hit then break end elseif not hit then break end
        sp = sp + 1
      end
      local least = from + ins.min
      if sp < least then return nil end
      while sp >= least do
        local r = rx_run(pc + 1, sp)
        if r ~= nil then return r end
        sp = sp - 1
      end
      return nil
    elseif op == "manyl" then
      local set, neg, str, limit = ins.set, ins.neg, RX_str, RX_len
      local taken = 0
      while taken < ins.min do
        if sp > limit then return nil end
        local hit = set[strbyte(str, sp)]
        if neg then if hit then return nil end elseif not hit then return nil end
        sp = sp + 1
        taken = taken + 1
      end
      while true do
        local r = rx_run(pc + 1, sp)
        if r ~= nil then return r end
        if sp > limit then return nil end
        local hit = set[strbyte(str, sp)]
        if neg then if hit then return nil end elseif not hit then return nil end
        sp = sp + 1
      end
    elseif op == "jmp" then
      pc = ins.x
    elseif op == "split" then
      -- The budget exists because backtracking is exponential on a pattern like `(a*)*b`, in this
      -- engine and in a browser's alike - V8 takes 2.7 s over 28 characters. A browser tab can
      -- freeze; a console page shares its thread with the game, so the answer here is a loud error
      -- rather than a stall nobody can attribute.
      RX_steps = RX_steps - 1
      if RX_steps < 0 then
        error("regular expression /" .. (RX_prog.source or "?")
              .. "/: gave up - the pattern backtracks catastrophically on this input", 0)
      end
      local r = rx_run(ins.x, sp)
      if r ~= nil then return r end
      pc = ins.y
    elseif op == "save" then
      local caps = RX_caps
      local was = caps[ins.n]
      caps[ins.n] = sp
      local r = rx_run(pc + 1, sp)
      if r ~= nil then return r end
      caps[ins.n] = was
      return nil
    elseif op == "mark" then
      -- Where this repetition started, for the progress check that ends its body.
      local marks = RX_marks
      local was = marks[ins.id]
      marks[ins.id] = sp
      local r = rx_run(pc + 1, sp)
      if r ~= nil then return r end
      marks[ins.id] = was
      return nil
    elseif op == "progress" then
      -- An unbounded repeat whose body matched nothing would loop for ever: `(a*)*` against "b".
      -- The iteration is failed rather than the next one refused, so its captures are undone.
      if sp <= RX_marks[ins.id] then return nil end
      pc = pc + 1
    elseif op == "clear" then
      local caps = RX_caps
      local was = caps[ins.n]
      if was == nil then
        pc = pc + 1
      else
        caps[ins.n] = nil
        local r = rx_run(pc + 1, sp)
        if r ~= nil then return r end
        caps[ins.n] = was
        return nil
      end
    elseif op == "match" or op == "lookend" then
      return sp
    elseif op == "bol" then
      if sp ~= 1 and not (RX_multi and strbyte(RX_str, sp - 1) == 10) then return nil end
      pc = pc + 1
    elseif op == "eol" then
      if sp ~= RX_len + 1 and not (RX_multi and strbyte(RX_str, sp) == 10) then return nil end
      pc = pc + 1
    elseif op == "wb" or op == "nwb" then
      local before = sp > 1 and RX_W[strbyte(RX_str, sp - 1)] or false
      local after = sp <= RX_len and RX_W[strbyte(RX_str, sp)] or false
      if ((before ~= after) == true) ~= (op == "wb") then return nil end
      pc = pc + 1
    elseif op == "look" then
      local hit = rx_run(ins.x, sp) ~= nil
      -- The lookahead's own loop marks are scratch of a match that is being thrown away; left set,
      -- they could refuse a later iteration that starts at the same place.
      for k = ins.lo, ins.hi do RX_marks[k] = nil end
      if hit ~= ins.want then return nil end
      pc = ins.next
    elseif op == "ref" then
      local caps = RX_caps
      local a, b = caps[ins.a], caps[ins.b]
      if a == nil or b == nil then
        pc = pc + 1                                -- an unmatched group matches the empty string
      else
        local width = b - a
        if sp + width - 1 > RX_len then return nil end
        local want, got = sub(RX_str, a, b - 1), sub(RX_str, sp, sp + width - 1)
        if RX_icase then want, got = want:lower(), got:lower() end
        if want ~= got then return nil end
        sp = sp + width
        pc = pc + 1
      end
    else
      return nil
    end
  end
end

--- The leftmost match at or after `from` (1-based). Returns start and end-exclusive, or nil.
local function rx_search(re, s, from)
  local c = re.__c
  local caps, marks, twice, nmarks = c.caps, c.marks, c.groups * 2, c.nmarks
  local wasStr, wasLen, wasProg, wasCaps, wasMarks, wasMulti, wasIcase =
    RX_str, RX_len, RX_prog, RX_caps, RX_marks, RX_multi, RX_icase
  RX_str, RX_len, RX_prog, RX_caps, RX_marks = s, #s, c.prog, caps, marks
  RX_multi, RX_icase = re.multiline == true, c.icase
  RX_steps = 200000
  local last = re.sticky and from or RX_len + 1
  local at, stop = nil, nil
  for i = from, last do
    for k = 1, twice do caps[k] = nil end
    for k = 1, nmarks do marks[k] = nil end
    local r = rx_run(1, i)
    if r ~= nil then at, stop = i, r break end
  end
  RX_str, RX_len, RX_prog, RX_caps, RX_marks, RX_multi, RX_icase =
    wasStr, wasLen, wasProg, wasCaps, wasMarks, wasMulti, wasIcase
  return at, stop
end

--- The array a browser hands back: [0] the whole match, [n] each group, plus index and input.
local function rx_result(re, s, at, stop)
  local c = re.__c
  local caps = c.caps
  local m = js_array({ [0] = sub(s, at, stop - 1) }, c.groups + 1)
  for g = 1, c.groups do
    local a, b = caps[g * 2 - 1], caps[g * 2]
    if a ~= nil and b ~= nil then m[g] = sub(s, a, b - 1) end
  end
  m.index = at - 1
  m.input = s
  if c.names ~= nil then
    local named = {}
    for name, g in pairs(c.names) do named[name] = m[g] end
    m.groups = named
  end
  return m
end

local function rx_exec(re, s)
  s = js_str(s)
  local from = 1
  if re.global or re.sticky then from = floor(js_num(re.lastIndex)) + 1 end
  if from < 1 then from = 1 end
  if from > #s + 1 then re.lastIndex = 0 return nil end
  local at, stop = rx_search(re, s, from)
  if at == nil then re.lastIndex = 0 return nil end
  if re.global or re.sticky then re.lastIndex = stop - 1 end
  return rx_result(re, s, at, stop)
end

local RegExpClass = js_class("RegExp", nil, {
  __ctor = function(self, compiled, pattern, flags)
    self.__regex = true
    self.__c = compiled
    self.source = pattern
    self.flags = flags
    self.lastIndex = 0
    self.global = flags:find("g", 1, true) ~= nil
    self.ignoreCase = flags:find("i", 1, true) ~= nil
    self.multiline = flags:find("m", 1, true) ~= nil
    self.dotAll = flags:find("s", 1, true) ~= nil
    self.sticky = flags:find("y", 1, true) ~= nil
  end,
  exec = function(self, s) return rx_exec(self, s) end,
  test = function(self, s) return rx_exec(self, s) ~= nil end,
  toString = function(self) return "/" .. self.source .. "/" .. self.flags end,
})

-- Compiling is cached by pattern and flags, because a regex literal inside a function is evaluated
-- on every call - a page writing `s.replace(/\s+/g, '')` in its render loop would otherwise parse
-- and compile that pattern sixty times a second. The regex OBJECT is still fresh each time, since
-- lastIndex is per-object state and two literals are two objects in JavaScript.
local rxCache = {}

function js_regex(pattern, flags)
  if type(pattern) == "table" and pattern.__regex then
    if flags == nil then return pattern end
    pattern = pattern.source
  end
  pattern = pattern == nil and "" or js_str(pattern)
  flags = flags == nil and "" or js_str(flags)
  local key = flags .. "\1" .. pattern
  local compiled = rxCache[key]
  if compiled == nil then
    -- `u` and `v` are accepted and change nothing: this engine is byte-based, and for the ASCII a
    -- console page carries the two modes agree. Anything else is a mistake worth naming.
    if flags:find("[^dgimsuvy]") ~= nil then
      error("regular expression /" .. pattern .. "/" .. flags .. ": unknown flag", 0)
    end
    compiled = rx_compile(pattern, flags)
    rxCache[key] = compiled
  end
  return js_new(RegExpClass, compiled, pattern, flags)
end

--- `$&`, `` $` ``, `$'`, `$$`, `$1`..`$99` and `$<name>`, appended to `out` as a browser expands them.
local function rx_expand(out, n, template, s, at, stop, m, names)
  local i, width = 1, #template
  while i <= width do
    local d = template:find("$", i, true)
    if d == nil then n = n + 1 out[n] = sub(template, i) return n end
    if d > i then n = n + 1 out[n] = sub(template, i, d - 1) end
    local c = sub(template, d + 1, d + 1)
    if c == "$" then n = n + 1 out[n] = "$" i = d + 2
    elseif c == "&" then n = n + 1 out[n] = sub(s, at, stop - 1) i = d + 2
    elseif c == "`" then n = n + 1 out[n] = sub(s, 1, at - 1) i = d + 2
    elseif c == "'" then n = n + 1 out[n] = sub(s, stop) i = d + 2
    elseif c == "<" and names ~= nil then
      local close = template:find(">", d + 2, true)
      if close == nil then n = n + 1 out[n] = "$<" i = d + 2
      else
        local g = names[sub(template, d + 2, close - 1)]
        if g ~= nil and m[g] ~= nil then n = n + 1 out[n] = m[g] end
        i = close + 1
      end
    elseif c:match("%d") ~= nil then
      local two = sub(template, d + 1, d + 2)
      local g
      if two:match("^%d%d$") ~= nil and tonumber(two) < m.length and tonumber(two) > 0 then
        g = tonumber(two) i = d + 3
      else
        g = tonumber(c) i = d + 2
      end
      if g >= 1 and g < m.length then
        if m[g] ~= nil then n = n + 1 out[n] = m[g] end
      else
        n = n + 1 out[n] = sub(template, d, i - 1)
      end
    else
      n = n + 1 out[n] = "$" i = d + 1
    end
  end
  return n
end

-- A replacer is called `fn(match, p1..pn, offset, string)`, plus the named-group object last when
-- the pattern has one. The first four arities are written out because they are every one a page
-- uses and they allocate nothing; past that the arguments go through a table, with explicit bounds
-- so an unmatched group's nil does not truncate the call.
local function rx_replacer(fn, m, at, s, groups, named)
  if named == nil then
    if groups == 0 then return fn(m[0], at - 1, s) end
    if groups == 1 then return fn(m[0], m[1], at - 1, s) end
    if groups == 2 then return fn(m[0], m[1], m[2], at - 1, s) end
    if groups == 3 then return fn(m[0], m[1], m[2], m[3], at - 1, s) end
  end
  local args = {}
  for g = 0, groups do args[g + 1] = m[g] end
  args[groups + 2], args[groups + 3] = at - 1, s
  local count = groups + 3
  if named ~= nil then count = count + 1 args[count] = m.groups end
  return fn(table.unpack(args, 1, count))
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
-- Methods every object has, kept apart from ArrayMethods so the array path - the hot one - is
-- unchanged, and consulted only after it misses.
local ObjectMethods = {}
ObjectMethods.hasOwnProperty = function(obj, key) return rawget(obj, key) ~= nil end

function js_m(obj, name, ...)
  if type(obj) == "table" then
    local own = rawget(obj, name)
    if type(own) == "function" then return own(...) end
    -- A class method reached through the prototype takes the instance first; a function the page
    -- put on a plain object is an arrow function and takes none. Calling one the other way shifts
    -- every argument by one, silently, so the two are told apart rather than assumed.
    local mt = getmetatable(obj)
    if mt ~= nil and rawget(mt, "__jsclass") then
      local method = obj[name]
      if type(method) == "function" then return method(obj, ...) end
    end
    local viaIndex = obj[name]
    if type(viaIndex) == "function" then return viaIndex(...) end
    -- An array method only on something with a length: an array, or a view that derives one, as
    -- classList does. Any table used to reach ArrayMethods, so `ctx.fill()` on an object with no
    -- `length` ran Array.fill and died inside it with a Lua message about nil, where the refusal
    -- below names the call the page made - and a canvas context is exactly the receiver whose
    -- method names overlap the array's (fill, save, translate).
    local m = (type(obj.length) == "number" and ArrayMethods[name]) or ObjectMethods[name]
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

local function isRegex(v) return type(v) == "table" and v.__regex == true end

function StringMethods.split(s, sep, limit)
  local out, n = {}, 0
  local cap = limit == nil and math.huge or floor(js_num(limit))
  if cap <= 0 then return js_array(out, 0) end
  if sep == nil then out[0] = s return js_array(out, 1) end

  if isRegex(sep) then
    -- The capture groups of the separator go into the result too, as a browser's do. An empty
    -- match at the point the last piece started advances instead of splitting, which is what makes
    -- `'abc'.split(/(?:)/)` three characters rather than a loop.
    local c = sep.__c
    if s == "" then
      if rx_search(sep, s, 1) ~= nil then return js_array(out, 0) end
      out[0] = "" return js_array(out, 1)
    end
    local from, last = 1, 1
    while from <= #s do
      local at, stop = rx_search(sep, s, from)
      -- A separator that matches AT the end of the string is not a separator: it would add an
      -- empty piece a browser does not. `'ab cd'.split(/\b/)` is three pieces, not four.
      if at == nil or at > #s then break end
      if stop == at and at == last then
        from = at + 1
      else
        out[n] = sub(s, last, at - 1) n = n + 1
        if n >= cap then return js_array(out, n) end
        for g = 1, c.groups do
          local a, b = c.caps[g * 2 - 1], c.caps[g * 2]
          if a ~= nil and b ~= nil then out[n] = sub(s, a, b - 1) end
          n = n + 1
          if n >= cap then return js_array(out, n) end
        end
        last = stop
        from = stop > at and stop or at + 1
      end
    end
    out[n] = sub(s, last) n = n + 1
    return js_array(out, n)
  end

  sep = js_str(sep)
  if sep == "" then
    for i = 1, #s do
      if n >= cap then break end
      out[n] = sub(s, i, i) n = n + 1
    end
  else
    local from = 1
    while true do
      local a, b = s:find(sep, from, true)
      if not a then out[n] = sub(s, from) n = n + 1 break end
      out[n] = sub(s, from, a - 1) n = n + 1
      if n >= cap then break end
      from = b + 1
    end
  end
  return js_array(out, n)
end

-- One routine behind replace and replaceAll. `with` is either a template, where `$1` and friends
-- expand, or a function called per match. The template is scanned for a `$` once: without one -
-- which is nearly every call - neither the match array nor the expander is built at all.
local function substitute(s, find, with, all)
  s = js_str(s)
  local fn = type(with) == "function" and with or nil
  local template = fn == nil and js_str(with) or nil
  local plain = template ~= nil and template:find("$", 1, true) == nil
  local out, n = {}, 0

  if isRegex(find) then
    local groups, names = find.__c.groups, find.__c.names
    local every = all or find.global
    local from, last = 1, 1
    while from <= #s + 1 do
      local at, stop = rx_search(find, s, from)
      if at == nil then break end
      n = n + 1 out[n] = sub(s, last, at - 1)
      if plain then
        n = n + 1 out[n] = template
      else
        local m = rx_result(find, s, at, stop)
        if fn ~= nil then
          n = n + 1 out[n] = js_str(rx_replacer(fn, m, at, s, groups, names))
        else
          n = rx_expand(out, n, template, s, at, stop, m, names)
        end
      end
      last = stop
      if not every then break end
      if stop > at then from = stop else from = at + 1 end
    end
    n = n + 1 out[n] = sub(s, last)
    if find.global then find.lastIndex = 0 end
    return concat(out)
  end

  find = js_str(find)
  local width, from = #find, 1
  while from <= #s + 1 do
    local a, b
    if width == 0 then a, b = from, from - 1 else a, b = s:find(find, from, true) end
    if a == nil then break end
    n = n + 1 out[n] = sub(s, from, a - 1)
    if fn ~= nil then
      n = n + 1 out[n] = js_str(fn(find, a - 1, s))
    elseif plain then
      n = n + 1 out[n] = template
    else
      n = rx_expand(out, n, template, s, a, b + 1, js_array({ [0] = find }, 1), nil)
    end
    from = b + 1
    -- An empty needle matches between every character, and has to step past one to make progress.
    if width == 0 then
      if a <= #s then n = n + 1 out[n] = sub(s, a, a) end
      from = a + 1
    end
    if not all then break end
  end
  n = n + 1 out[n] = sub(s, from)
  return concat(out)
end

--- Replaces the first match, or every match when the pattern is a global regex.
function StringMethods.replace(s, find, with) return substitute(s, find, with, false) end

--- Replaces every match. It used to be `replace` itself, so with a plain needle - the form the
--- name exists for - it replaced exactly one occurrence and said nothing.
function StringMethods.replaceAll(s, find, with) return substitute(s, find, with, true) end

--- A non-global regex gives one match array; a global one gives every matched string, or null.
function StringMethods.match(s, re)
  s = js_str(s)
  if not isRegex(re) then re = js_regex(re, "") end
  if not re.global then
    local at, stop = rx_search(re, s, 1)
    if at == nil then return nil end
    return rx_result(re, s, at, stop)
  end
  re.lastIndex = 0
  local out, n, from = {}, 0, 1
  while from <= #s + 1 do
    local at, stop = rx_search(re, s, from)
    if at == nil then break end
    out[n] = sub(s, at, stop - 1) n = n + 1
    if stop > at then from = stop else from = at + 1 end
  end
  if n == 0 then return nil end
  return js_array(out, n)
end

--- Every match, each as its own array with groups and index. A browser returns an iterator; this
--- returns the array a page gets by spreading one, which is how every page here uses it.
function StringMethods.matchAll(s, re)
  s = js_str(s)
  if not isRegex(re) then re = js_regex(re, "g") end
  local out, n, from = {}, 0, 1
  while from <= #s + 1 do
    local at, stop = rx_search(re, s, from)
    if at == nil then break end
    out[n] = rx_result(re, s, at, stop) n = n + 1
    if stop > at then from = stop else from = at + 1 end
  end
  return js_array(out, n)
end

-- Unicode normalisation needs the composition tables, which are not here. ASCII is already in
-- normal form under all four, so a page formatting plain text gets the right answer and one
-- handing this accented text is told rather than handed its argument back unchanged.
function StringMethods.normalize(s, form)
  if s:find("[\128-\255]") == nil then return s end
  error("String.normalize needs Unicode tables a compiled page does not carry"
        .. " (the text is not ASCII" .. (form ~= nil and ", form " .. js_str(form) or "") .. ")")
end

-- substr is the legacy pair of slice and differs from it in the SECOND argument: a COUNT, not an
-- end. Reading one as the other silently truncates, which is why it is written out rather than
-- forwarded to slice.
function StringMethods.substr(s, from, count)
  local n = #s
  from = floor(js_num(from or 0))
  if from < 0 then from = math.max(0, n + from) end
  if count == nil then count = n - from else count = floor(js_num(count)) end
  if count <= 0 then return "" end
  return sub(s, from + 1, from + count)
end

-- Byte indices, matching `.length` and charAt, which are bytes here too. A page whose text is
-- plain ASCII - every console page in this repository - gets exactly the browser's answer; one
-- indexing into a multi-byte character gets that character's first byte rather than its codepoint.
function StringMethods.charCodeAt(s, i)
  local b = s:byte((i or 0) + 1)
  if b == nil then return 0 / 0 end
  return b
end

StringMethods.codePointAt = StringMethods.charCodeAt

-- `at` counts from the end for a negative index, where charAt does not.
function StringMethods.at(s, i)
  i = floor(js_num(i or 0))
  if i < 0 then i = #s + i end
  if i < 0 or i >= #s then return nil end
  return sub(s, i + 1, i + 1)
end

function StringMethods.lastIndexOf(s, find)
  local at, from = -1, 1
  while true do
    local a = s:find(find, from, true)
    if a == nil then return at end
    at = a - 1
    from = a + 1
  end
end

-- Byte order, which for ASCII is the order a browser gives. It is not a locale collation and
-- cannot be: there is no locale data here, and a page sorting accented names would need one.
function StringMethods.localeCompare(s, other)
  other = js_str(other)
  if s < other then return -1 end
  if s > other then return 1 end
  return 0
end

-- `repeat` is a Lua keyword, so it cannot be written as a field name.
StringMethods["repeat"] = function(s, times)
  times = floor(js_num(times or 0))
  if times <= 0 then return "" end
  return rep(s, times)
end

function StringMethods.trimStart(s) return (s:gsub("^%s+", "")) end
function StringMethods.trimEnd(s) return (s:gsub("%s+$", "")) end
StringMethods.toLocaleUpperCase = StringMethods.toUpperCase
StringMethods.toLocaleLowerCase = StringMethods.toLowerCase

-- A string argument becomes a regular expression, as it does in a browser: `'a.c'.search('.')` is
-- 0, not 1. That is the one place search and indexOf part company.
function StringMethods.search(s, pattern)
  if not isRegex(pattern) then pattern = js_regex(js_str(pattern), "") end
  local at = rx_search(pattern, js_str(s), 1)
  if at == nil then return -1 end
  return at - 1
end

-- numbers

-- C's %f rounds an exact midpoint to even, JavaScript rounds it away from zero: 2.5.toFixed(0) is
-- "3" in a browser and "2" from %f, and 0.25.toFixed(1) is "0.3" against "0.2". Only a midpoint the
-- double really sits on differs, and the test for one cannot be made on v * 10^digits, which
-- invents them - 1.45 * 10 is exactly 14.5 while 1.45 is 1.44999999999999995559, and rounding that
-- up would be wrong. The product only screens; the value's own decimal expansion decides.
-- Not covered: |v| >= 1e21, where JavaScript falls back to its ordinary number-to-string.
function NumberMethods.toFixed(v, digits)
  digits = floor(digits or 0)
  if v ~= v then return "NaN" end
  local spec = "%." .. format("%d", digits) .. "f"
  local scaled = abs(v) * 10 ^ digits
  if digits < 20 and scaled < 1e15 and fmod(scaled, 1) == 0.5 then
    local exact = format("%.20f", abs(v))
    if sub(exact, #exact - (20 - digits) + 1):match("^50*$") then
      return (v < 0 and "-" or "") .. format(spec, (floor(scaled) + 1) / 10 ^ digits)
    end
  end
  return format(spec, v)
end

function NumberMethods.toString(v) return js_str(v) end

-- %g picks the shorter of fixed and exponential at the same significance, which is what toPrecision
-- does; the one place they part is that %g strips trailing zeros and toPrecision keeps them, so
-- 1.5.toPrecision(4) is "1.500". Restored below rather than left as "1.5", which is a different
-- string in a label.
function NumberMethods.toPrecision(v, digits)
  if digits == nil then return js_str(v) end
  digits = floor(js_num(digits))
  if v ~= v then return "NaN" end
  if digits < 1 or digits > 21 then return js_str(v) end
  local text = format("%." .. format("%d", digits) .. "g", v)
  if text:find("[eE]") ~= nil then return jsExponent(text) end
  local body = text:gsub("^-", "")
  local significant = #(body:gsub("%.", ""):gsub("^0+", ""))
  if significant == 0 then significant = 1 end
  if significant >= digits then return text end
  if body:find("%.") == nil then text = text .. "." end
  return text .. rep("0", digits - significant)
end

-- `%e` with no precision is NOT usable: the interpreter the game embeds ignores the whole
-- conversion and hands back the number as it would print it, so `(12345).toExponential()` was
-- "12345" in game and "1.2345e+4" in the test. With an explicit precision both interpreters
-- agree, so the no-argument form asks for the shortest precision that reads back as the same
-- number - which is what JavaScript's own rule amounts to.
function NumberMethods.toExponential(v, digits)
  if v ~= v then return "NaN" end
  if v == math.huge then return "Infinity" end
  if v == -math.huge then return "-Infinity" end
  if digits ~= nil then
    return jsExponent(format("%." .. format("%d", floor(js_num(digits))) .. "e", v))
  end
  for d = 0, 17 do
    local text = format("%." .. format("%d", d) .. "e", v)
    if tonumber(text) == v then return jsExponent(text) end
  end
  return jsExponent(format("%.17e", v))
end

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

-- A hole, a null and an undefined all join as the EMPTY string, not as the word "undefined":
-- `[1, undefined, 2].join('-')` is "1--2". js_str would have spelled it out, which turned a split
-- whose capture did not participate - `'b'.split(/(a)?b/)` - into the text "undefined".
function ArrayMethods.join(a, sep)
  local parts = {}
  for i = 0, a.length - 1 do
    local v = a[i]
    parts[i + 1] = v == nil and "" or js_str(v)
  end
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

function ArrayMethods.shift(a)
  local n = a.length
  if n == 0 then return nil end
  local first = a[0]
  for i = 1, n - 1 do a[i - 1] = a[i] end
  a[n - 1] = nil
  a.length = n - 1
  return first
end

-- The extra arguments are read out before anything else loops, for the reason concat records: this
-- Lua's `...` does not survive a numeric `for` in the same function.
function ArrayMethods.unshift(a, ...)
  local added = select("#", ...)
  local rest = {}
  for i = 1, added do rest[i] = select(i, ...) end
  local n = a.length
  for i = n - 1, 0, -1 do a[i + added] = a[i] end
  for i = 1, added do a[i - 1] = rest[i] end
  a.length = n + added
  return a.length
end

-- In place, and the return is what was REMOVED - a page trimming a log writes
-- `log.splice(0, log.length - 8)` and ignores it, but one taking an item out writes
-- `const [item] = list.splice(i, 1)` and does not.
function ArrayMethods.splice(a, start, count, ...)
  local added = select("#", ...)
  local rest = {}
  for i = 1, added do rest[i] = select(i, ...) end

  local n = a.length
  start = floor(js_num(start or 0))
  if start < 0 then start = math.max(0, n + start) else start = math.min(start, n) end
  if count == nil then count = n - start
  else count = math.max(0, math.min(floor(js_num(count)), n - start)) end

  local removed = {}
  for i = 0, count - 1 do removed[i] = a[start + i] end

  local moved = added - count
  if moved < 0 then
    for i = start + count, n - 1 do a[i + moved] = a[i] end
    for i = n + moved, n - 1 do a[i] = nil end
  elseif moved > 0 then
    for i = n - 1, start + count, -1 do a[i + moved] = a[i] end
  end
  for i = 1, added do a[start + i - 1] = rest[i] end
  a.length = n + moved
  return js_array(removed, count)
end

function ArrayMethods.reverse(a)
  local i, j = 0, a.length - 1
  while i < j do
    a[i], a[j] = a[j], a[i]
    i, j = i + 1, j - 1
  end
  return a
end

function ArrayMethods.fill(a, value, from, to)
  local n = a.length
  from = floor(js_num(from or 0))
  if from < 0 then from = math.max(0, n + from) else from = math.min(from, n) end
  if to == nil then to = n else to = floor(js_num(to)) end
  if to < 0 then to = math.max(0, n + to) else to = math.min(to, n) end
  for i = from, to - 1 do a[i] = value end
  return a
end

function ArrayMethods.at(a, i)
  i = floor(js_num(i or 0))
  if i < 0 then i = a.length + i end
  if i < 0 or i >= a.length then return nil end
  return a[i]
end

function ArrayMethods.findIndex(a, fn)
  for i = 0, a.length - 1 do if js_truthy(fn(a[i], i, a)) then return i end end
  return -1
end

function ArrayMethods.findLast(a, fn)
  for i = a.length - 1, 0, -1 do if js_truthy(fn(a[i], i, a)) then return a[i] end end
  return nil
end

function ArrayMethods.findLastIndex(a, fn)
  for i = a.length - 1, 0, -1 do if js_truthy(fn(a[i], i, a)) then return i end end
  return -1
end

function ArrayMethods.lastIndexOf(a, v)
  for i = a.length - 1, 0, -1 do if a[i] == v then return i end end
  return -1
end

-- One level by default, as JavaScript's does. Depth is honoured rather than ignored because
-- `flat(Infinity)` is the form pages actually write.
local function spill(src, left, out, n)
  for i = 0, src.length - 1 do
    local v = src[i]
    if left > 0 and type(v) == "table" and type(v.length) == "number" then
      n = spill(v, left - 1, out, n)
    else
      out[n] = v
      n = n + 1
    end
  end
  return n
end

function ArrayMethods.flat(a, depth)
  local out = {}
  return js_array(out, spill(a, depth == nil and 1 or js_num(depth), out, 0))
end

function ArrayMethods.flatMap(a, fn)
  local out, n = {}, 0
  for i = 0, a.length - 1 do
    local v = fn(a[i], i, a)
    if type(v) == "table" and type(v.length) == "number" then
      for j = 0, v.length - 1 do out[n] = v[j] n = n + 1 end
    else
      out[n] = v
      n = n + 1
    end
  end
  return js_array(out, n)
end

function ArrayMethods.reduceRight(a, fn, seed)
  local acc, start = seed, a.length - 1
  if acc == nil then acc = a[start] start = start - 1 end
  for i = start, 0, -1 do acc = fn(acc, a[i], i, a) end
  return acc
end

-- In place and without a temporary, so the overlapping case - which is the only case anyone writes
-- it for - does not read a slot it has already overwritten.
function ArrayMethods.copyWithin(a, target, from, to)
  local n = a.length
  local function bound(v, fallback)
    if v == nil then return fallback end
    v = floor(js_num(v))
    if v < 0 then return math.max(0, n + v) end
    return math.min(v, n)
  end
  target, from = bound(target, 0), bound(from, 0)
  to = bound(to, n)
  local count = math.min(to - from, n - target)
  if count <= 0 then return a end
  if target > from then
    for i = count - 1, 0, -1 do a[target + i] = a[from + i] end
  else
    for i = 0, count - 1 do a[target + i] = a[from + i] end
  end
  return a
end

-- The ES2023 four, which differ from their in-place namesakes in exactly one way: the original is
-- left alone. A page uses them where the array it holds is shared - a render reading state it must
-- not disturb - so copying rather than sorting in place is the whole point of the call.
local function copyOf(a)
  local out = {}
  for i = 0, a.length - 1 do out[i] = a[i] end
  return js_array(out, a.length)
end

function ArrayMethods.toReversed(a) return ArrayMethods.reverse(copyOf(a)) end
function ArrayMethods.toSorted(a, fn) return ArrayMethods.sort(copyOf(a), fn) end

function ArrayMethods.toSpliced(a, start, count, ...)
  local copy = copyOf(a)
  ArrayMethods.splice(copy, start, count, ...)
  return copy
end

-- `with` is a JavaScript keyword and a Lua identifier, so it needs no quoting here.
function ArrayMethods.with(a, index, value)
  index = floor(js_num(index))
  if index < 0 then index = a.length + index end
  if index < 0 or index >= a.length then
    error("Array.with: index " .. js_str(index) .. " is outside an array of " .. js_str(a.length))
  end
  local copy = copyOf(a)
  copy[index] = value
  return copy
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
  -- Exact at an exact power, which neither `math.log(v, base)` nor log(v)/log(base) is: the
  -- interpreter the game embeds answers 2.9999999999999996 for log10(1000), so
  -- `Math.floor(Math.log10(n)) + 1` - counting a number's digits, the reason a page calls this -
  -- was one too few for every power of ten. The standalone Lua the checks run on answers 3, so the
  -- difference showed only in game. Snapped rather than reimplemented: only the powers are wrong.
  -- log2 was named in the transpiler's manifest without ever being defined here, so a page using
  -- it compiled cleanly and then died on the console with nothing in its own source to point at.
  log2 = function(v)
    local r = math.log(v, 2)
    local n = floor(r + 0.5)
    if 2 ^ n == v then return n + 0.0 end
    return r
  end,
  log10 = function(v)
    local r = math.log(v, 10)
    local n = floor(r + 0.5)
    if 10 ^ n == v then return n + 0.0 end
    return r
  end,
  cbrt = function(v) if v < 0 then return -((-v) ^ (1 / 3)) end return v ^ (1 / 3) end,
  -- Leading zero bits of a 32-bit unsigned, which is what JavaScript counts: anything outside that
  -- range is taken modulo 2^32 first, exactly as `>>> 0` would.
  clz32 = function(v)
    v = js_num(v)
    if v ~= v or v == math.huge or v == -math.huge then v = 0 end
    v = floor(v) % 4294967296
    if v == 0 then return 32 end
    local bits = 0
    while v < 2147483648 do v = v * 2 bits = bits + 1 end
    return bits
  end,
}

-- The nearest float32, which is what fround answers. No frexp here, so the exponent comes from
-- log(|v|, 2). That log is not exact at a power of two - this Lua answers -30 for 2^-29, and for
-- 22 other exponents - so the result is corrected against the powers themselves. On THIS libm the
-- correction never changes an answer, because where the log is wrong the value is exactly
-- representable anyway; it is kept because the game embeds a different interpreter with a
-- different log, and one exponent out there would be a silently wrong number rather than a
-- visible failure. Two comparisons, and deliberately not claimed to be covered by a test.
-- Once the exponent is right the rounding is exact: dividing by a power of two is exact, `(q + 2^52) - 2^52` rounds a
-- double to an integer with ties to even, which is what float32 does, and multiplying back is
-- exact again. Subnormals use the fixed smallest step; anything past the largest float32 is
-- Infinity, as it is in a browser.
Math.fround = function(v)
  v = js_num(v)
  if v ~= v or v == 0 or v == math.huge or v == -math.huge then return v end
  local size = abs(v)
  local e = floor(math.log(size, 2))
  if 2 ^ e > size then e = e - 1 elseif 2 ^ (e + 1) <= size then e = e + 1 end
  if e < -126 then e = -126 end                  -- subnormal: one fixed step of 2^-149
  local step = 2 ^ (e - 23)
  local r = ((v / step) + 4503599627370496.0 - 4503599627370496.0) * step
  if r > 3.4028234663852886e38 then return math.huge end
  if r < -3.4028234663852886e38 then return -math.huge end
  return r
end

-- The hyperbolics, from exp, which is how they are defined.
Math.sinh = function(v) return (math.exp(v) - math.exp(-v)) / 2 end
Math.cosh = function(v) return (math.exp(v) + math.exp(-v)) / 2 end
Math.tanh = function(v)
  if v > 20 then return 1 end
  if v < -20 then return -1 end
  local a, b = math.exp(v), math.exp(-v)
  return (a - b) / (a + b)
end
Math.asinh = function(v) return math.log(v + math.sqrt(v * v + 1)) end
Math.acosh = function(v) return math.log(v + math.sqrt(v * v - 1)) end
Math.atanh = function(v) return 0.5 * math.log((1 + v) / (1 - v)) end
Math.expm1 = function(v) return math.exp(v) - 1 end
Math.log1p = function(v) return math.log(1 + v) end

-- The low 32 bits of a 32-bit product, signed. Split into halves because the full product of two
-- 32-bit numbers passes 2^53, where a double stops being exact and the answer would quietly differ
-- from the browser's by a few units in the last place.
Math.imul = function(a, b)
  local x, y = to_uint32(a), to_uint32(b)
  local xl, xh = x % 65536, floor(x / 65536)
  local yl, yh = y % 65536, floor(y / 65536)
  -- the high-by-high term is 2^32 and falls off the end
  return to_int32((xl * yl + ((xl * yh + xh * yl) % 65536) * 65536) % 4294967296)
end

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
  isInteger = function(v) return type(v) == "number" and v == v and abs(v) ~= math.huge and v == floor(v) end,
  MAX_SAFE_INTEGER = 9007199254740991,
  MIN_SAFE_INTEGER = -9007199254740991,
  EPSILON = 2.220446049250313e-16,
  MAX_VALUE = 1.7976931348623157e308,
  MIN_VALUE = 5e-324,
  POSITIVE_INFINITY = math.huge,
  NEGATIVE_INFINITY = -math.huge,
  NaN = 0 / 0,
  isSafeInteger = function(v)
    return type(v) == "number" and v == v and abs(v) ~= math.huge
           and v == floor(v) and abs(v) <= 9007199254740991
  end,
}
Number.parseInt = function(v, base) return parseInt(v, base) end
setmetatable(Number, { __call = function(_, v) return js_num(v) end })

setmetatable({}, {})
String = setmetatable({}, { __call = function(_, v) return js_str(v) end })

-- utf8char is defined with the regular-expression engine above, which needs it for `\u{...}`.
String.fromCodePoint = function(...)
  local parts = {}
  for i = 1, select("#", ...) do parts[i] = utf8char((select(i, ...))) end
  return concat(parts)
end
-- fromCharCode takes UTF-16 code units. Anything outside the basic plane arrives as a surrogate
-- pair, which this does not recombine: a page passing one gets two replacement-shaped sequences
-- rather than the character. Every console page in this repository passes plain ASCII.
String.fromCharCode = String.fromCodePoint

-- `String.raw`a${x}b``. The transpiler builds the pieces array with `raw` pointing at itself and
-- calls the tag with it, so this is the plainest possible tag: the pieces with the values between
-- them. Cooked and raw hold the same strings here, because the parser has already processed the
-- escapes - so `String.raw` does not keep a `\n` as two characters the way a browser would.
-- The values are read out before the loop, for the reason ArrayMethods.concat records: this Lua's
-- `...` does not survive a numeric `for` in the same function.
String.raw = function(pieces, ...)
  if type(pieces) ~= "table" then return "" end
  local held, count = {}, select("#", ...)
  for i = 1, count do held[i] = select(i, ...) end
  local strings = pieces.raw or pieces
  local out, n = {}, 0
  for i = 0, (strings.length or 0) - 1 do
    n = n + 1 out[n] = js_str(strings[i])
    if i + 1 <= count then n = n + 1 out[n] = js_str(held[i + 1]) end
  end
  return concat(out)
end
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
  values = function(t)
    local out, n = {}, 0
    for k, v in pairs(t) do if k ~= "length" then out[n] = v n = n + 1 end end
    return js_array(out, n)
  end,
  fromEntries = function(pairsOf)
    local out = {}
    for i = 0, (pairsOf.length or 0) - 1 do
      local pair = pairsOf[i]
      if type(pair) == "table" then out[js_str(pair[0])] = pair[1] end
    end
    return out
  end,
  -- Returns its argument, which is what a page uses freeze for: it reads the result back. Nothing
  -- is actually made read-only - a later write still lands - so this is a shim, not the guarantee.
  -- A page that relies on a write being rejected would be relying on strict mode throwing, and the
  -- translated page has no strict mode to throw from.
  freeze = function(t) return t end,
  hasOwn = function(t, key)
    if type(t) ~= "table" then return false end
    return rawget(t, key) ~= nil
  end,
  -- Like freeze: a shim that returns its argument. Nothing is made unwritable, because a rejected
  -- write is strict mode throwing and a translated page has no strict mode to throw from.
  seal = function(t) return t end,
  -- SameValue: NaN equals itself and the two zeros do not, which is the only reason this exists
  -- alongside `===`.
  is = function(a, b)
    if a ~= a and b ~= b then return true end
    if a == 0 and b == 0 then return (1 / a) == (1 / b) end
    return a == b
  end,
  create = function(proto, props)
    local t = {}
    if type(proto) == "table" then setmetatable(t, { __index = proto }) end
    if type(props) == "table" then
      for k, d in pairs(props) do if type(d) == "table" then rawset(t, k, d.value) end end
    end
    return t
  end,
  getPrototypeOf = function(t)
    local mt = getmetatable(t)
    if mt == nil then return nil end
    local idx = rawget(mt, "__index")
    if type(idx) == "table" then return idx end
    return mt
  end,
  setPrototypeOf = function(t, proto)
    if type(t) == "table" then setmetatable(t, type(proto) == "table" and { __index = proto } or nil) end
    return t
  end,
  -- Every own key, `length` included, which is where this differs from Object.keys.
  getOwnPropertyNames = function(t)
    local out, n = {}, 0
    if type(t) == "table" then for k in pairs(t) do out[n] = js_str(k) n = n + 1 end end
    return js_array(out, n)
  end,
  -- A data descriptor only. A descriptor with get or set would need an accessor grafted onto
  -- whatever metatable the object already has, and half of that - storing get() once as a value -
  -- is the plausible-looking wrong answer this whole prelude is written to avoid. So it says so.
  defineProperty = function(t, key, desc)
    if type(t) ~= "table" or type(desc) ~= "table" then return t end
    if desc.get ~= nil or desc.set ~= nil then
      error("Object.defineProperty with a getter or setter is not supported on a compiled page")
    end
    rawset(t, key, desc.value)
    return t
  end,
}

-- `Array` was already on the transpiler's list of globals the prelude provides, and was not here:
-- `Array.isArray(x)` compiled and then died on the console. The statics only - `new Array(n)` is
-- js_new_array, which the transpiler emits directly.
Array = {
  isArray = function(v) return type(v) == "table" and type(rawget(v, "length")) == "number" end,
  of = function(...) return js_new_array(...) end,
  -- A string, an array-like, or a Map/Set, plus the optional per-item function. Map and Set are
  -- this prelude's own classes, so they are asked for their contents rather than iterated: there
  -- is no iterator protocol here and building one for two callers would be the wrong trade.
  from = function(src, fn)
    local out, n = {}, 0
    if type(src) == "string" then
      for i = 1, #src do out[i - 1] = sub(src, i, i) end
      n = #src
    elseif type(src) == "table" then
      local list = src
      if rawget(src, "__keys") ~= nil then list = js_m(src, rawget(src, "__values") ~= nil and "entries" or "values") end
      n = list.length or 0
      for i = 0, n - 1 do out[i] = list[i] end
    end
    if fn ~= nil then for i = 0, n - 1 do out[i] = fn(out[i], i) end end
    return js_array(out, n)
  end,
}
setmetatable(Array, { __call = function(_, ...) return js_new_array(...) end })

-- `Date.now()`: the page's own clock, offset by the world clock the host has given as EPOCH so it
-- is a real instant rather than milliseconds since the scene loaded. Without EPOCH it is the scene
-- clock alone, which is still a correct DIFFERENCE - `Date.now() - start` is the idiom - and is
-- what makes the calendar accessors refuse rather than answer 1970.
Date = setmetatable({ now = function() return (EPOCH or 0) + (js_now and js_now() or 0) end },
                    { __call = function(_, ...) return js_date(...) end })

-- Enough of the browser that a page's guards resolve. `location` is nil so
-- `typeof location !== 'undefined'` takes its else branch, which is what a page means by it.
console = { log = function() end, warn = function() end, error = function() end }
localStorage = { getItem = function() return nil end, setItem = function() end }
location = nil
window = { innerWidth = 0, innerHeight = 0 }
-- The chunk's own environment. A page uses it to test for a host object without throwing, which is
-- exactly what it is for; it is NOT the author's globals, which this environment cannot reach.
globalThis = window
performance = { now = function() return 0 end }
JSON = { stringify = js_str, parse = function() return nil end }

-- ---- the DOM, as far as a compiled page needs one -------------------------------------------------
--
-- A page's script only ever reaches the document to WRITE: a style property, some text, a class.
-- Each write is recorded against the element's id, and the compiler turns those records into slot
-- writes on the vector scene. Nothing here reads layout, because by the time this runs the layout
-- has already happened, once, in the compiler.

-- Text as the EMITTER would have written it. A slot write goes straight into the scene and skips
-- the emitter, so the shaping it does has to be reproduced here or the label is wrong in two ways
-- that both look like the page broke rather than like a formatting difference:
--
--   * the scene reader types a clean number as a NUMBER even when it is quoted, and a number has
--     no text, so a score of '00042' simply vanishes. <noparse> keeps it a string.
--   * font-variant-numeric: tabular-nums has no OpenType equivalent here, so the emitter monospaces
--     each digit run instead. Without it a counter's digits shift sideways as they change.
--
-- Also the escapes, which the scene format needs whatever the text is.
local function escaped(s)
  s = s:gsub('\\', '\\\\')      -- the backslash first, or it doubles the ones added below
  s = s:gsub('"', '\\"')
  s = s:gsub('\r', '')
  s = s:gsub('\n', '\\n')
  return s
end

function js_plain(s)
  s = escaped(s)
  -- a purely numeric label, and the leading `=` that the scene reader would take as an expression
  if tonumber(s) ~= nil or s:sub(1, 1) == '=' then return '<noparse>' .. s .. '</noparse>' end
  return s
end

function js_tabular(s)
  s = escaped(s)
  return (s:gsub('%d+', function(run) return '<mspace=0.6em>' .. run .. '</mspace>' end))
end

DOM = { writes = {}, order = {}, missing = {}, notes = {}, listeners = {}, captures = 0 }

function DOM.reset()
  DOM.writes, DOM.order, DOM.missing, DOM.notes = {}, {}, {}, {}
  DOM.listeners, DOM.captures = {}, 0
end

-- What the page asked for that a compiled page cannot give, said once. A read the chunk cannot
-- answer used to hand back nil and nothing else, and a page dividing by a scrollHeight it never
-- had looked like a page that did nothing. Keyed by the reason, which is a constant string, with
-- the element as the value, so noting it from a per-frame read allocates nothing; the host drains
-- and logs the table the way it drains DOM.missing.
-- Noted ONCE per reason and element, not once per read. A page reading a scroll offset every frame
-- refilled the table every frame, and the host's drain built its lists and lines every frame to
-- log nothing new. `noted` is what has already been said; the host dedups by line as well. A field
-- and not a local: the prelude and the page share one main function, and Lua allows it 200 locals.
DOM.noted = {}
function DOM.note(why, what)
  local key = what == nil and true or what
  local said = DOM.noted[why]
  if said == nil then said = {} DOM.noted[why] = said end
  if said[key] then return end
  said[key] = true
  DOM.notes[why] = key
end

-- A page assigning `undefined` has still written: `log.scrollTop = log.scrollHeight` does exactly
-- that here, since nothing lays out and scrollHeight is not a thing a compiled page has. Storing
-- nil would delete the entry instead, so the record would say the page never touched it.
UNDEFINED = setmetatable({}, { __tostring = function() return "undefined" end })

local function record(id, key, value)
  -- A compiled page installs DOM.bind, which sends the write straight to its scene slot and keeps
  -- nothing. Without it - the offline harness, and any page being checked against its original -
  -- the write is recorded by name so the two runs can be compared.
  if DOM.bind then
    DOM.bind(id, key, value)
    return
  end
  local slot = id .. "." .. key
  if DOM.writes[slot] == nil then DOM.order[#DOM.order + 1] = slot end
  DOM.writes[slot] = value == nil and UNDEFINED or value
end

-- The page's own markup, as the compiler laid it out: every element's id in document order, and
-- its tag and class attribute. Without these the only tree a compiled page can see is the one its
-- script built, so a page querying its OWN document got an empty list rather than an answer - and,
-- worse, `classList.contains` on a markup element answered false for a class that was right there
-- in the HTML.
--
-- Read rather than copied into each element, so they can be filled in by the chunk AFTER this
-- prelude has run and after `document.body` and its two siblings already exist.
TAG = TAG or {}
CLASS = CLASS or {}
NODES = NODES or {}

-- classList is a view over className, as it is in the DOM: adding a class writes className, which
-- is an ordinary recorded write, so a page that styles by class reaches the scene the same way one
-- that assigns className does. Derived from className on every call rather than cached, because a
-- page is free to assign className directly between two classList calls.
local function classList(el)
  local function read()
    local names, seen = {}, {}
    for word in js_str(rawget(el, "__props").className or CLASS[rawget(el, "__id")] or ""):gmatch("%S+") do
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
    -- Swaps in place, so the class keeps its position. Returns false when the old name was not
    -- there, which is what a page tests to know whether anything happened.
    replace = function(old, new)
      local names, seen = read()
      old, new = js_str(old), js_str(new)
      if not seen[old] then return false end
      for i = 1, #names do if names[i] == old then names[i] = new end end
      write(names)
      return true
    end,
    item = function(i) local names = read() return names[js_num(i) + 1] end,
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
  -- `length` and `list[0]` are derived on read like everything else here, so they follow a direct
  -- assignment to className rather than going stale the moment a page makes one.
  setmetatable(cl, { __index = function(_, key)
    if key == "length" then return #(read()) end
    if type(key) == "number" then return (read())[key + 1] end
    return nil
  end })
  return cl
end

-- The methods a page reaches as `el.name(...)`. js_m calls whatever __index hands back WITHOUT the
-- element - a function a page stored on an object is an arrow function and takes no receiver - so
-- each of these has to arrive already bound to one. The binding is made once per element and per
-- name and then kept, rather than a fresh closure on every access: `el.getBoundingClientRect()` in
-- a render function is on the per-frame path, and a closure per read is garbage per frame.
--
-- Bound lazily, so an element that never calls one of these carries nothing extra.
--
-- ElementReads are the properties a page reads rather than calls - parentNode, children,
-- offsetWidth. Both tables are filled further down, once there is an element() to reach.
local ElementMethods, ElementReads = {}, {}

local ElementMeta = {}
ElementMeta.__index = function(el, key)
  if key == "style" then return rawget(el, "__style") end
  if key == "classList" then return rawget(el, "__classList") end
  local method = ElementMethods[key]
  if method ~= nil then
    local bound = rawget(el, "__bound")
    if bound == nil then bound = {} rawset(el, "__bound", bound) end
    local fn = bound[key]
    if fn == nil then
      fn = function(...) return method(el, ...) end
      bound[key] = fn
    end
    return fn
  end
  local read = ElementReads[key]
  if read ~= nil then return read(el) end
  return rawget(el, "__props")[key]
end
ElementMeta.__newindex = function(el, key, value)
  -- `el.onclick = fn` registers a handler; it is not a value the scene can draw. It used to be
  -- recorded as an ordinary write to a slot no scene carries, so a page written in the property
  -- form - which is most short pages - had every button dead and nothing said so. Assigning null
  -- removes the handler, as it does in a browser.
  -- Every handler property a browser has is `on` followed by lower-case letters, so requiring that
  -- keeps an ordinary property like `onceDone` out of the event registry.
  local props = rawget(el, "__props")
  if type(key) == "string" and #key > 2 and sub(key, 1, 2) == "on" and key:match("^on%l+$") ~= nil
     and (type(value) == "function" or (value == nil and type(props[key]) == "function")) then
    local id, kind = rawget(el, "__id"), sub(key, 3)
    if type(props[key]) == "function" then DOM.off(id, kind, props[key]) end
    props[key] = value
    if value ~= nil then DOM.on(id, kind, value) end
    return
  end
  props[key] = value
  record(rawget(el, "__id"), key, value)
end

-- setProperty and its pair, which a page uses for a custom property (`--accent`) because a dashed
-- name cannot be written as `style.--accent`. The name is brought to the camelCase the rest of the
-- pipeline uses, so `setProperty('font-size', x)` lands on the same slot `style.fontSize = x` does
-- - anything else would write to a slot the compiler never bound and the page would do nothing.
local function cssKey(name)
  name = js_str(name)
  if sub(name, 1, 2) == "--" then return name end
  return (name:gsub("%-(%a)", string.upper))
end

local StyleMethods = {
  setProperty = function(st, name, value) st[cssKey(name)] = value end,
  removeProperty = function(st, name)
    local key = cssKey(name)
    local was = rawget(st, "__props")[key]
    st[key] = ""
    return was
  end,
  getPropertyValue = function(st, name) return rawget(st, "__props")[cssKey(name)] end,
}

local StyleMeta = {}
StyleMeta.__index = function(st, key)
  local method = StyleMethods[key]
  if method ~= nil then
    local bound = rawget(st, "__bound")
    if bound == nil then bound = {} rawset(st, "__bound", bound) end
    local fn = bound[key]
    if fn == nil then
      fn = function(...) return method(st, ...) end
      bound[key] = fn
    end
    return fn
  end
  return rawget(st, "__props")[key]
end
StyleMeta.__newindex = function(st, key, value)
  -- `style.cssText = 'width:5px;top:2px'` is a whole declaration block, not one property, so it is
  -- split and each half written where the compiler bound it. Assigning it used to land on a slot
  -- called `style.cssText`, which no scene has ever carried.
  if key == "cssText" then
    for name, v in js_str(value):gmatch("([^:;]+):([^;]*)") do
      st[cssKey((name:gsub("^%s+", ""):gsub("%s+$", "")))] = (v:gsub("^%s+", ""):gsub("%s+$", ""))
    end
    return
  end
  rawget(st, "__props")[key] = value
  record(rawget(st, "__id"), "style." .. key, value)
end

local elements = {}
local created = 0

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
  addEventListener = function(kind, fn, options) DOM.on("document", kind, fn, options) end,
  removeEventListener = function(kind, fn, options) DOM.off("document", kind, fn, options) end,
  body = element("body"),
  head = element("head"),
  documentElement = element("html"),
  -- One node PER CALL. It used to be one per tag, so `createElement('div')` twice handed back the
  -- same node: a page building rows in a loop appended one element to itself and every write to
  -- either copy landed on the other. None of them has a shape in the scene either way - the layout
  -- ran before they existed - which DOM.missing reports.
  createElement = function(tag)
    tag = js_str(tag):lower()
    created = created + 1
    local el = element("__new" .. created .. "_" .. tag)
    rawset(el, "__tag", tag)
    return el
  end,
}

-- The namespace is dropped, because nothing here has one: an element built at run time has no shape
-- in the scene whichever namespace it claims, so an SVG node and an HTML node are the same kind of
-- nothing. Kept separate only so a page that builds SVG runs instead of stopping at the call.
document.createElementNS = function(_, tag) return document.createElement(tag) end

-- A text node, which this model did not have at all - so the ordinary shape
-- `li.appendChild(document.createTextNode(name))` was refused at compile time and the page did not
-- run. It is a node of its own, in the tree like any other; it draws nothing, for the same reason
-- a created element draws nothing, and DOM.missing says so. Its text is stored rather than
-- recorded, because creating a node is not a write.
local textNodes = 0
document.createTextNode = function(text)
  textNodes = textNodes + 1
  local el = element("__text" .. textNodes)
  rawset(el, "__tag", "#text")
  rawset(el, "__textnode", true)
  rawget(el, "__props").textContent = js_str(text)
  return el
end

-- A fragment is a holder that disappears when it is inserted, leaving its children behind - which
-- is exactly what a page builds one for: append the rows to it, then the fragment once. Here that
-- is a node insertAt unpacks rather than inserts, so the behaviour is the real one and not a shim.
local fragments = 0
document.createDocumentFragment = function()
  fragments = fragments + 1
  local el = element("__fragment" .. fragments)
  rawset(el, "__tag", "#fragment")
  rawset(el, "__fragment", true)
  return el
end

-- The body, as in a browser with nothing focused. Moved by focus() and blur() above; nothing on the
-- console moves it, because a compiled page has no text field of its own to type into.
document.activeElement = document.body

-- ---- the tree, the attributes, and the boxes there are none of -----------------------------------
--
-- What a script can still do to the document once the page is compiled, and what it cannot.
--
-- CAN: keep a tree. appendChild and its family really do move nodes here, so parentNode, children
-- and the ancestor chain an event bubbles along all read back correctly, and a page that builds a
-- list by createElement + appendChild runs rather than stopping at the first call.
--
-- CANNOT: make that tree appear. The structure was laid out ONCE, at compile time, and the scene
-- holds a shape per element that existed then. A node the script creates has no shape, so its
-- writes go to an id the scene does not carry - which the existing DOM.missing path already
-- reports, so this fails loudly rather than by drawing nothing.
--
-- CANNOT: measure. There is no layout in the chunk at all. Every box below is zero, deliberately
-- and documented, because a page that divides by a width should get a wrong number it can see
-- rather than an error with no line in its own source.

local function kids(el)
  local list = rawget(el, "__kids")
  if list == nil then list = {} rawset(el, "__kids", list) end
  return list
end

local function detach(node)
  local parent = rawget(node, "__parent")
  if parent == nil then return end
  local list = rawget(parent, "__kids")
  if list ~= nil then
    for i = 1, #list do if list[i] == node then table.remove(list, i) break end end
  end
  rawset(node, "__parent", nil)
  local id = rawget(node, "__id")
  if PARENT[id] == rawget(parent, "__id") then PARENT[id] = nil end
end

-- createElement hands back one element per TAG rather than one per call - the shape the offline
-- harness mirrors - so a page appending one div to another is appending a node to itself. That and
-- a node appended below its own descendant both make a cycle, and a cycle here is an infinite walk
-- in closest() and in the subtree search. Refused, as a browser refuses it.
local insertAt
insertAt = function(el, node, at)
  if type(node) ~= "table" or rawget(node, "__id") == nil then return node end
  local up, guard = el, 0
  while up ~= nil and guard < 64 do
    if up == node then return node end
    up = rawget(up, "__parent")
    guard = guard + 1
  end
  -- A fragment is emptied INTO the parent rather than inserted, as a browser does. Its children
  -- are taken from the front each time because inserting one detaches it, which shifts the list.
  local carried = rawget(node, "__fragment") and rawget(node, "__kids") or nil
  if carried ~= nil then
    local step = 0
    while #carried > 0 do
      insertAt(el, carried[1], at ~= nil and (at + step) or nil)
      step = step + 1
    end
    return node
  end
  detach(node)
  local list = kids(el)
  table.insert(list, at or (#list + 1), node)
  rawset(node, "__parent", el)
  PARENT[rawget(node, "__id")] = rawget(el, "__id")
  return node
end

local function indexOfKid(el, node)
  local list = rawget(el, "__kids")
  if list == nil then return nil end
  for i = 1, #list do if list[i] == node then return i end end
  return nil
end

function ElementMethods.appendChild(el, node) return insertAt(el, node) end

function ElementMethods.insertBefore(el, node, ref)
  if ref == nil then return insertAt(el, node) end
  return insertAt(el, node, indexOfKid(el, ref))
end

function ElementMethods.removeChild(el, node)
  if indexOfKid(el, node) ~= nil then detach(node) end
  return node
end

function ElementMethods.replaceChild(el, node, old)
  local at = indexOfKid(el, old)
  if at == nil then return old end
  detach(old)
  insertAt(el, node, at)
  return old
end

function ElementMethods.remove(el) detach(el) end

-- The variadic four. Their arguments are read out before anything else loops, for the reason
-- ArrayMethods.concat records: this Lua's `...` does not survive a numeric `for` in the same
-- function. A string argument would be a text node, which there is no such thing as here, so it is
-- skipped rather than turned into something that cannot be drawn either way.
local function nodesOf(...)
  local n = select("#", ...)
  local list = {}
  for i = 1, n do list[i] = select(i, ...) end
  list.n = n
  return list
end

function ElementMethods.append(el, ...)
  local list = nodesOf(...)
  for i = 1, list.n do insertAt(el, list[i]) end
end

function ElementMethods.prepend(el, ...)
  local list = nodesOf(...)
  for i = 1, list.n do insertAt(el, list[i], i) end
end

function ElementMethods.before(el, ...)
  local parent = rawget(el, "__parent")
  if parent == nil then return end
  local list = nodesOf(...)
  for i = 1, list.n do insertAt(parent, list[i], indexOfKid(parent, el)) end
end

function ElementMethods.after(el, ...)
  local parent = rawget(el, "__parent")
  if parent == nil then return end
  local list = nodesOf(...)
  for i = list.n, 1, -1 do insertAt(parent, list[i], indexOfKid(parent, el) + 1) end
end

function ElementMethods.replaceWith(el, ...)
  local parent = rawget(el, "__parent")
  if parent == nil then return end
  local list = nodesOf(...)
  local at = indexOfKid(parent, el)
  detach(el)
  for i = list.n, 1, -1 do insertAt(parent, list[i], at) end
end

-- The four positions, as one call. insertAdjacentHTML is deliberately NOT here: it takes MARKUP,
-- and there is no parser in the chunk - the page was laid out once, at compile time. Writing one
-- would turn a compile-time refusal the author can read into a run-time no-op nobody sees, which
-- is the wrong direction. insertAdjacentText is here, because text is a node now.
function ElementMethods.insertAdjacentElement(el, where, node)
  where = js_str(where):lower()
  if where == "beforebegin" then ElementMethods.before(el, node)
  elseif where == "afterbegin" then ElementMethods.prepend(el, node)
  elseif where == "beforeend" then insertAt(el, node)
  elseif where == "afterend" then ElementMethods.after(el, node)
  else return nil end
  return node
end

function ElementMethods.insertAdjacentText(el, where, text)
  ElementMethods.insertAdjacentElement(el, where, document.createTextNode(text))
end

-- A clone is a NEW node, so it gets an id of its own: sharing the original's would make every
-- write to either one land on the other. The scene has no shape for it, as for anything created.
local cloneCount = 0
function ElementMethods.cloneNode(el, deep)
  cloneCount = cloneCount + 1
  local copy = element("__clone" .. cloneCount)
  rawset(copy, "__tag", rawget(el, "__tag"))
  local props = rawget(copy, "__props")
  for k, v in pairs(rawget(el, "__props")) do props[k] = v end
  local style = rawget(copy, "__style")
  for k, v in pairs(rawget(el, "__style").__props) do rawget(style, "__props")[k] = v end
  local from = rawget(el, "__attrs")
  if from ~= nil then
    local to = {}
    for k, v in pairs(from) do to[k] = v end
    rawset(copy, "__attrs", to)
  end
  if js_truthy(deep) then
    local list = rawget(el, "__kids")
    if list ~= nil then
      for i = 1, #list do insertAt(copy, ElementMethods.cloneNode(list[i], true)) end
    end
  end
  return copy
end

-- ---- attributes ------------------------------------------------------------------------------
-- Stored as well as recorded, so getAttribute answers with what setAttribute put there. `id` and
-- `class` are kept elsewhere in this model - the element's own id, and className, which classList
-- is a view over - so they are read from where they actually live rather than from a second copy
-- that would go stale the moment a page touched classList.

local function attrs(el)
  local t = rawget(el, "__attrs")
  if t == nil then t = {} rawset(el, "__attrs", t) end
  return t
end

function ElementMethods.getAttribute(el, name)
  name = js_str(name)
  if name == "id" then return rawget(el, "__id") end
  if name == "class" then return ElementReads.className(el) end
  local t = rawget(el, "__attrs")
  local v = t ~= nil and t[name] or nil
  if v == nil then return nil end
  return js_str(v)
end

function ElementMethods.setAttribute(el, name, value) DOM.attribute(el, name, value) end

function ElementMethods.hasAttribute(el, name)
  return ElementMethods.getAttribute(el, name) ~= nil
end

function ElementMethods.removeAttribute(el, name)
  name = js_str(name)
  local t = rawget(el, "__attrs")
  if t ~= nil then t[name] = nil end
  -- recorded too: a slot that was set has to be told it is no longer set, or the scene keeps it
  record(rawget(el, "__id"), "@" .. name, nil)
end

-- force is a tri-state, as classList.toggle's is: absent means flip, present means set. `false` is
-- a value here, so it cannot be tested for truthiness the way an optional argument usually is.
function ElementMethods.toggleAttribute(el, name, force)
  local want
  if force == nil then want = not ElementMethods.hasAttribute(el, name) else want = js_truthy(force) end
  if want then DOM.attribute(el, name, "") else ElementMethods.removeAttribute(el, name) end
  return want
end

-- `el.dataset.actIndex` is the `data-act-index` attribute, so the two names have to be converted
-- between. One proxy per element, made on first use.
local function dashed(c) return "-" .. c:lower() end
local function dataName(key) return "data-" .. (js_str(key):gsub("%u", dashed)) end

local DatasetMeta = {
  __index = function(ds, key) return ElementMethods.getAttribute(rawget(ds, "__el"), dataName(key)) end,
  __newindex = function(ds, key, value) DOM.attribute(rawget(ds, "__el"), dataName(key), value) end,
}

-- ---- events, selectors and boxes ---------------------------------------------------------------

function ElementMethods.addEventListener(el, kind, fn, options) DOM.on(rawget(el, "__id"), kind, fn, options) end
function ElementMethods.removeEventListener(el, kind, fn, options) DOM.off(rawget(el, "__id"), kind, fn, options) end
function ElementMethods.dispatchEvent(el, ev)
  DOM.fire(rawget(el, "__id"), ev ~= nil and ev.type or nil, 0, 0)
  return true
end

-- `el.click()` is a real click as far as the page is concerned - the handlers run and the event
-- bubbles - it simply did not come from the player, so it carries no coordinates. That is what a
-- browser does too.
function ElementMethods.click(el) DOM.fire(rawget(el, "__id"), "click", 0, 0) end

-- Focus moves within the chunk and fires the events a page listens for; it does NOT change what is
-- drawn, because `:focus` was resolved once at compile time and the scene has no state for it. So a
-- page that tracks focus itself works, and one that expects a focus ring from CSS does not get one.
function ElementMethods.focus(el)
  local was = document.activeElement
  if was == el then return end
  document.activeElement = el
  if was ~= nil then DOM.fire(rawget(was, "__id"), "blur", 0, 0) end
  DOM.fire(rawget(el, "__id"), "focus", 0, 0)
end

-- Focus returns to the body, as it does in a browser, rather than to nothing.
function ElementMethods.blur(el)
  if document.activeElement ~= el or el == document.body then return end
  document.activeElement = document.body
  DOM.fire(rawget(el, "__id"), "blur", 0, 0)
end

-- Only the selector shapes a compiled page can be answered about: `#id`, `.class`, a tag name, `*`,
-- and a comma list of those. Anything else - a descendant combinator, an attribute test, a
-- pseudo-class - has no node list to run against and returns false rather than a guess.
-- A selector with nothing to trim is returned as it is: two gsubs made two strings per call, and
-- a search runs this once per node.
local function trimmed(s)
  if s:find("^%s") == nil and s:find("%s$") == nil then return s end
  return (s:gsub("^%s+", ""):gsub("%s+$", ""))
end

-- A selector no part of which this matcher can answer - `[data-act]`, the idiom every page that
-- builds markup with click indices queries after each render - matches nothing, and is known to by
-- looking once. Walking every node of the page to fail the same pattern on each cost more per
-- render than anything else the query did. Remembered by the selector's own string.
DOM.unanswerableSeen = {}
function DOM.unanswerable(selector)
  local known = DOM.unanswerableSeen[selector]
  if known ~= nil then return known end
  known = selector ~= ""
  for part in selector:gmatch("[^,]+") do
    part = trimmed(part)
    if part == "" or part == "*" or part:find("[%s>+~%[:]") == nil then known = false break end
  end
  DOM.unanswerableSeen[selector] = known
  return known
end

local function matchesOne(el, sel)
  if sel == "" or sel == "*" then return true end
  if sel:find("[%s>+~%[:]") ~= nil then return false end
  if sub(sel, 1, 1) == "#" then
    if sel:find("[.#]", 2) ~= nil then return false end
    return rawget(el, "__id") == sub(sel, 2)
  end
  -- A compound of one optional tag and any number of classes: `li`, `.hot`, `li.hot`, `.a.b`.
  -- Anything else - an id inside a compound, an attribute test - is refused rather than guessed at,
  -- because a selector this cannot answer has to read as "no" and not as "everything".
  if sel:find("#", 1, true) ~= nil then return false end
  local tag = sel:match("^[%w_-]*")
  if tag ~= "" then
    local own = rawget(el, "__tag") or TAG[rawget(el, "__id")]
    if own == nil or own ~= tag:lower() then return false end
  end
  local cl = rawget(el, "__classList")
  for name in sel:gmatch("%.([%w_-]+)") do
    if not cl.contains(name) then return false end
  end
  return true
end

local function matchesAny(el, selector)
  selector = js_str(selector)
  if DOM.unanswerable(selector) then return false end
  if selector:find(",", 1, true) == nil then return matchesOne(el, trimmed(selector)) end
  for part in selector:gmatch("[^,]+") do
    if matchesOne(el, trimmed(part)) then return true end
  end
  return false
end

function ElementMethods.matches(el, selector) return matchesAny(el, selector) end

-- Up the ancestor chain, which is PARENT for the page's own markup and __parent for anything the
-- script built. So `#id` and `.class` are both answerable on a real page, not only on a built one.
function ElementMethods.closest(el, selector)
  local at, guard = el, 0
  while at ~= nil and guard < 64 do
    guard = guard + 1
    if matchesAny(at, selector) then return at end
    local up = rawget(at, "__parent")
    if up == nil then
      local pid = PARENT[rawget(at, "__id")]
      if pid ~= nil then up = element(pid) end
    end
    at = up
  end
  return nil
end

-- Over the nodes the SCRIPT built. The page's own markup is not enumerable at run time - the
-- compiled scene carries shapes, not a node list - so a search rooted at a markup element finds
-- nothing. That is a real limit, not an empty result: a page that expects to walk its own document
-- will silently see no elements.
local function collect(el, sel, out, n)
  local list = rawget(el, "__kids")
  if list == nil then return n end
  for i = 1, #list do
    local kid = list[i]
    if matchesAny(kid, sel) then out[n] = kid n = n + 1 end
    n = collect(kid, sel, out, n)
  end
  return n
end

-- Is `id` somewhere below `rootId`? Up the compiled PARENT chain, which is the page's own tree.
local function isUnder(id, rootId)
  local at, guard = PARENT[id], 0
  while at ~= nil and guard < 64 do
    if at == rootId then return true end
    at = PARENT[at]
    guard = guard + 1
  end
  return false
end

-- The page's own markup, in document order, plus anything the script appended below it. NODES is
-- the compiler's list of every element it laid out; while it is empty - the offline harness, or a
-- page compiled before it existed - a query finds only what the script built, exactly as before.
local function collectMarkup(rootId, sel, out, n)
  for i = 1, #NODES do
    local id = NODES[i]
    if rootId == nil or isUnder(id, rootId) then
      local el = element(id)
      if matchesAny(el, sel) then out[n] = el n = n + 1 end
      n = collect(el, sel, out, n)
    end
  end
  return n
end

-- `contains`, `querySelector` and `querySelectorAll` were in the compiler's manifest and defined
-- nowhere, so a page using any of the three compiled cleanly and then died on the console. They
-- search the tree the SCRIPT built, which is the same limit the two getElementsBy* have.
function ElementMethods.contains(el, node)
  local at, guard = node, 0
  while type(at) == "table" and guard < 64 do
    if at == el then return true end
    guard = guard + 1
    at = ElementReads.parentNode(at)
  end
  return false
end

-- A search that found nothing - `render()` asking for `[data-act]` every tick of a compiled page,
-- whose handlers are bound once - hands back one shared empty list rather than a new table each
-- time. Safe because a NodeList has no push or splice to fill it with. Fields rather than locals,
-- for the 200-local limit the prelude and the page share.
DOM.FOUND = {}
do
  local none, found = js_array({}, 0), DOM.FOUND
  function DOM.found(n)
    if n == 0 then return none end
    local out = {}
    for i = 0, n - 1 do out[i] = found[i] found[i] = nil end
    return js_array(out, n)
  end
end

function ElementMethods.querySelectorAll(el, selector)
  selector = js_str(selector)
  if DOM.unanswerable(selector) then return DOM.found(0) end
  -- The markup below this element first, in document order, then this element's own script-built
  -- children - which collectMarkup cannot have reached, since it starts one level down.
  local n = collectMarkup(rawget(el, "__id"), selector, DOM.FOUND, 0)
  return DOM.found(collect(el, selector, DOM.FOUND, n))
end

function ElementMethods.querySelector(el, selector)
  return ElementMethods.querySelectorAll(el, selector)[0]
end

function ElementMethods.getElementsByTagName(el, tag)
  return ElementMethods.querySelectorAll(el, js_str(tag))
end

-- Several names mean an element carrying ALL of them, which is what a compound of classes means to
-- the selector matcher - so the list becomes `.a.b` and there is only one search.
function ElementMethods.getElementsByClassName(el, names)
  local sel = ""
  for one in js_str(names):gmatch("%S+") do sel = sel .. "." .. one end
  if sel == "" then return js_array({}, 0) end
  return ElementMethods.querySelectorAll(el, sel)
end

-- Document-wide: the page's own markup in document order, and - only while the compiler has given
-- it no markup to walk - whatever the script built under the root, so nothing regresses off-console.
document.querySelectorAll = function(selector)
  selector = js_str(selector)
  if DOM.unanswerable(selector) then return DOM.found(0) end
  local n = collectMarkup(nil, selector, DOM.FOUND, 0)
  if #NODES == 0 then
    n = collect(document.documentElement, selector, DOM.FOUND, n)
    n = collect(document.body, selector, DOM.FOUND, n)
  end
  return DOM.found(n)
end

document.getElementsByTagName = function(tag) return document.querySelectorAll(js_str(tag)) end
document.getElementsByClassName = function(names)
  local sel = ""
  for one in js_str(names):gmatch("%S+") do sel = sel .. "." .. one end
  if sel == "" then return js_array({}, 0) end
  return document.querySelectorAll(sel)
end

-- `#id` is the one selector a compiled page can always answer, because an id is exactly what the
-- scene binds its slots by; the rest need the compiler's node list.
document.querySelector = function(selector)
  selector = js_str(selector)
  if sub(selector, 1, 1) == "#" and selector:find("[%s.,>+~%[:]") == nil then
    return document.getElementById(sub(selector, 2))
  end
  return document.querySelectorAll(selector)[0]
end

-- The boxes the COMPILER measured, baked into the chunk as `BOXES[id] = { x, y, w, h, cw, ch }` in
-- page coordinates: the border box's position and size, then the content box's size. The layout ran
-- once, before any of this existed, so these are the real numbers rather than an approximation -
-- and an element the compiler did not measure (anything the script created) keeps the zeros a
-- browser gives a detached node.
--
-- Optional seventh and eighth entries are the offset from the offsetParent. Without them
-- offsetLeft and offsetTop answer nil rather than a page coordinate that would be wrong the moment
-- anything above the element is positioned.
BOXES = BOXES or {}

local ZERO_RECT = { x = 0, y = 0, width = 0, height = 0, top = 0, left = 0, right = 0, bottom = 0 }

-- Made once per element and kept: `getBoundingClientRect()` inside a render function is on the
-- per-frame path, and a table per call is garbage per frame.
function ElementMethods.getBoundingClientRect(el)
  local b = BOXES[rawget(el, "__id")]
  if b == nil then return ZERO_RECT end
  local r = rawget(el, "__rect")
  if r == nil then
    r = { x = b[1], y = b[2], width = b[3], height = b[4],
          left = b[1], top = b[2], right = b[1] + b[3], bottom = b[2] + b[4] }
    rawset(el, "__rect", r)
  end
  return r
end

local function boxed(index, fallback)
  return function(el)
    local b = BOXES[rawget(el, "__id")]
    if b == nil then return fallback end
    local v = b[index]
    if v == nil then return fallback end
    return v
  end
end

ElementReads.offsetWidth, ElementReads.offsetHeight = boxed(3, 0), boxed(4, 0)
ElementReads.clientWidth, ElementReads.clientHeight = boxed(5, 0), boxed(6, 0)
ElementReads.offsetLeft, ElementReads.offsetTop = boxed(7, nil), boxed(8, nil)
-- Deliberately NOT a number for scrollHeight or scrollTop. `log.scrollTop = log.scrollHeight` is
-- the idiom for pinning a log to its foot, and the record of that write reading `undefined` is
-- what says the page asked for something a compiled page does not have - a zero would make it
-- look answered. The read is noted, so the log says so too. An offset the page itself wrote does
-- read back, unclamped: a browser clamps it to the scroll range, and the chunk has no range to
-- clamp against, so what the page set is the one honest number there is.
local NO_SCROLL = "a compiled page has no scroll state: the scene scrolls on the vector side and the offset never comes back to the chip"
local function unscrolled(key)
  return function(el)
    local own = key ~= nil and rawget(el, "__props")[key] or nil
    if own ~= nil then return own end
    DOM.note(NO_SCROLL, rawget(el, "__id"))
    return nil
  end
end
ElementReads.scrollTop, ElementReads.scrollLeft = unscrolled("scrollTop"), unscrolled("scrollLeft")
-- Read-only in a browser, so a write to either is not an answer.
ElementReads.scrollWidth, ElementReads.scrollHeight = unscrolled(nil), unscrolled(nil)

ElementReads.parentNode = function(el)
  local up = rawget(el, "__parent")
  if up ~= nil then return up end
  local pid = PARENT[rawget(el, "__id")]
  if pid ~= nil then return element(pid) end
  return nil
end
ElementReads.parentElement = ElementReads.parentNode

-- `children` is elements only and `childNodes` is everything, which matters now that a text node
-- is a node: a page iterating `children` and reading `.tagName` would otherwise meet one.
local function isText(node) return rawget(node, "__textnode") == true end

ElementReads.childNodes = function(el)
  local list = rawget(el, "__kids")
  if list == nil then return js_array({}, 0) end
  return js_array_of(list)
end

ElementReads.children = function(el)
  local list = rawget(el, "__kids")
  if list == nil then return js_array({}, 0) end
  local out, n = {}, 0
  for i = 1, #list do
    if not isText(list[i]) then out[n] = list[i] n = n + 1 end
  end
  return js_array(out, n)
end

ElementReads.firstChild = function(el)
  local list = rawget(el, "__kids")
  return list ~= nil and list[1] or nil
end
ElementReads.lastChild = function(el)
  local list = rawget(el, "__kids")
  return list ~= nil and list[#list] or nil
end
ElementReads.firstElementChild = function(el)
  local list = rawget(el, "__kids")
  if list == nil then return nil end
  for i = 1, #list do if not isText(list[i]) then return list[i] end end
  return nil
end
ElementReads.lastElementChild = function(el)
  local list = rawget(el, "__kids")
  if list == nil then return nil end
  for i = #list, 1, -1 do if not isText(list[i]) then return list[i] end end
  return nil
end

-- Over the tree the script built; the page's own markup is shapes in a scene by now.
local function siblingOf(el, step, elementsOnly)
  local up = ElementReads.parentNode(el)
  if up == nil then return nil end
  local list = rawget(up, "__kids")
  if list == nil then return nil end
  for i = 1, #list do
    if list[i] == el then
      local at = i + step
      while list[at] ~= nil do
        if not (elementsOnly and isText(list[at])) then return list[at] end
        at = at + step
      end
      return nil
    end
  end
  return nil
end

ElementReads.nextSibling = function(el) return siblingOf(el, 1, false) end
ElementReads.previousSibling = function(el) return siblingOf(el, -1, false) end
ElementReads.nextElementSibling = function(el) return siblingOf(el, 1, true) end
ElementReads.previousElementSibling = function(el) return siblingOf(el, -1, true) end

-- The element's own id, which is what it is keyed by - `el.id` read back nothing at all before,
-- because nothing had ever assigned the property. A page that assigns it gets its own value back;
-- it does not rename the element, since the scene's slots are bound to the compiled id.
ElementReads.id = function(el) return rawget(el, "__props").id or rawget(el, "__id") end

-- Node.ELEMENT_NODE, TEXT_NODE or DOCUMENT_FRAGMENT_NODE. There are no comment nodes.
ElementReads.nodeType = function(el)
  if rawget(el, "__textnode") then return 3 end
  if rawget(el, "__fragment") then return 11 end
  return 1
end

-- A text node's content, under both the names a page reads it by.
ElementReads.nodeValue = function(el)
  if rawget(el, "__textnode") then return rawget(el, "__props").textContent end
  return nil
end
ElementReads.data = ElementReads.nodeValue

-- An element the script built carries `__tag`; one of the page's own markup has its tag in TAG.
local function isMarkup(el) return rawget(el, "__tag") == nil end

-- Enough of a NamedNodeMap to be counted and read. Built per call rather than kept, because it has
-- to follow setAttribute and className, and nothing reads it on a per-frame path. `id` is an
-- attribute only where the element has one: the page's markup always does, a created element
-- only once the script assigns it - its internal id is not an attribute anyone wrote.
ElementReads.attributes = function(el)
  local out, n = {}, 0
  if isMarkup(el) or rawget(el, "__props").id ~= nil then
    out[n] = { name = "id", value = ElementReads.id(el) } n = n + 1
  end
  local className = ElementReads.className(el)
  if className ~= nil and className ~= "" then
    out[n] = { name = "class", value = js_str(className) } n = n + 1
  end
  local t = rawget(el, "__attrs")
  if t ~= nil then
    for name, value in pairs(t) do out[n] = { name = name, value = js_str(value) } n = n + 1 end
  end
  return js_array(out, n)
end

-- ---- the markup a script reads back ---------------------------------------------------------
-- Exact or nothing. What the script wrote, and what it built, can be given back verbatim; the
-- TEXT of the page's own markup is not in the chunk - it was laid out at compile time - so a
-- markup element nothing wrote to answers nil and says why, rather than a plausible half.

local NO_MARKUP = "the page's own markup is not in the chunk: it was laid out once, at compile time, so only what the script wrote or built can be read back"

local function escapeText(s)
  return (js_str(s):gsub("&", "&amp;"):gsub("<", "&lt;"):gsub(">", "&gt;"))
end

local function attrText(el)
  local s = ""
  if isMarkup(el) or rawget(el, "__props").id ~= nil then
    s = ' id="' .. js_str(ElementReads.id(el)):gsub('"', "&quot;") .. '"'
  end
  local cls = ElementReads.className(el)
  if cls ~= nil and cls ~= "" then s = s .. ' class="' .. js_str(cls):gsub('"', "&quot;") .. '"' end
  local t = rawget(el, "__attrs")
  if t ~= nil then
    for k, v in pairs(t) do s = s .. " " .. k .. '="' .. js_str(v):gsub('"', "&quot;") .. '"' end
  end
  return s
end

local serialise

-- The inside of a node, or nil where it is not known exactly.
local function innerOf(el, out, n)
  local props = rawget(el, "__props")
  if props.innerHTML ~= nil then n = n + 1 out[n] = js_str(props.innerHTML) return n end
  local text = props.textContent or props.innerText
  if text ~= nil then n = n + 1 out[n] = escapeText(text) return n end
  if isMarkup(el) then return nil end
  local list = rawget(el, "__kids")
  if list ~= nil then
    for i = 1, #list do
      n = serialise(list[i], out, n)
      if n == nil then return nil end
    end
  end
  return n
end

serialise = function(el, out, n)
  if rawget(el, "__textnode") then
    n = n + 1 out[n] = escapeText(rawget(el, "__props").textContent or "")
    return n
  end
  local tag = rawget(el, "__tag") or TAG[rawget(el, "__id")]
  if tag == nil or tag == "#fragment" then return innerOf(el, out, n) end
  n = n + 1 out[n] = "<" .. tag .. attrText(el) .. ">"
  n = innerOf(el, out, n)
  if n == nil then return nil end
  n = n + 1 out[n] = "</" .. tag .. ">"
  return n
end

local function joined(el, n, out)
  if n == nil then DOM.note(NO_MARKUP, rawget(el, "__id")) return nil end
  return concat(out, "", 1, n)
end

-- What was written reads back as it was written, with no table and no join: it is the one read of
-- these a page makes per tick (`log.innerHTML = line + log.innerHTML`), and it used to be free.
ElementReads.innerHTML = function(el)
  local written = rawget(el, "__props").innerHTML
  if written ~= nil then return js_str(written) end
  local out = {}
  return joined(el, innerOf(el, out, 0), out)
end
ElementReads.outerHTML = function(el) local out = {} return joined(el, serialise(el, out, 0), out) end

-- Text: what was written (an innerHTML with its tags stripped), or what was built.
ElementReads.textContent = function(el)
  local props = rawget(el, "__props")
  local text = props.textContent or props.innerText
  if text ~= nil then return text end
  if props.innerHTML ~= nil then return (js_str(props.innerHTML):gsub("<[^>]*>", "")) end
  if isMarkup(el) then DOM.note(NO_MARKUP, rawget(el, "__id")) return nil end
  local list, out = rawget(el, "__kids"), {}
  if list ~= nil then
    for i = 1, #list do out[i] = ElementReads.textContent(list[i]) or "" end
  end
  return concat(out)
end
ElementReads.innerText = ElementReads.textContent

ElementReads.tagName = function(el)
  if rawget(el, "__textnode") or rawget(el, "__fragment") then return nil end
  local tag = rawget(el, "__tag") or TAG[rawget(el, "__id")]
  return tag ~= nil and tag:upper() or nil
end

ElementReads.className = function(el)
  local own = rawget(el, "__props").className
  if own ~= nil then return own end
  return CLASS[rawget(el, "__id")]
end
-- nodeName is tagName for an element and `#text` for a text node, where tagName is undefined.
ElementReads.nodeName = function(el)
  if rawget(el, "__textnode") then return "#text" end
  if rawget(el, "__fragment") then return "#document-fragment" end
  return ElementReads.tagName(el)
end

ElementReads.dataset = function(el)
  local ds = rawget(el, "__dataset")
  if ds == nil then
    ds = setmetatable({ __el = el }, DatasetMeta)
    rawset(el, "__dataset", ds)
  end
  return ds
end

-- The element's own writes first, then its box: the cascade ran at compile time and its result is
-- in the scene, not in the chunk, but the compiler did measure every element, so `width` and
-- `height` are the real laid-out numbers rather than nothing. Anything else the stylesheet gave
-- the element - a colour, a font size - is not here, and reading it is noted rather than answered
-- with nil alone, which used to read as "the property is unset". One view per element, made on
-- first use, so a read in a render loop allocates nothing.
local NO_CASCADE = "getComputedStyle: the cascade ran at compile time, so only what the script wrote and the element's laid-out size are in the chunk"

local ComputedMeta = {}
ComputedMeta.__index = function(cs, key)
  if key == "getPropertyValue" then return rawget(cs, "__get") end
  local el = rawget(cs, "__el")
  local own = rawget(rawget(el, "__style"), "__props")[key]
  if own ~= nil then return own end
  local b = BOXES[rawget(el, "__id")]
  if b ~= nil then
    -- A box is fixed for the life of the chunk, so its two strings are built once and kept on it.
    if key == "width" then
      local s = b.width
      if s == nil then s = js_str(b[3]) .. "px" b.width = s end
      return s
    end
    if key == "height" then
      local s = b.height
      if s == nil then s = js_str(b[4]) .. "px" b.height = s end
      return s
    end
  end
  DOM.note(NO_CASCADE, key)
  return nil
end
-- Read-only, as a browser's is.
ComputedMeta.__newindex = function() end

function getComputedStyle(el)
  if type(el) ~= "table" then return nil end
  local cs = rawget(el, "__computed")
  if cs == nil then
    cs = setmetatable({ __el = el }, ComputedMeta)
    rawset(cs, "__get", function(name) return cs[cssKey(name)] end)
    rawset(el, "__computed", cs)
  end
  return cs
end
-- ---- events ---------------------------------------------------------------------------------
-- A compiled page is still interactive. `addEventListener` used to be a no-op here, so every
-- handler a page registered was thrown away and every button on a compiled console was dead while
-- looking perfectly alive - the click arrived, the scene had its region, and nothing happened.
--
-- Handlers live in this chunk beside the state they close over, and the host calls `event` below
-- when the player clicks. Bubbling walks PARENT, which the compiler emits from the laid-out tree:
-- a page listens on a container (`field.addEventListener('mousedown', jump)`) and the click lands
-- on whichever child is under the cursor, so without the walk the common case never fires.
PARENT = PARENT or {}

local function listeners(id, kind, make)
  local byKind = DOM.listeners[id]
  if byKind == nil then
    if not make then return nil end
    byKind = {}
    DOM.listeners[id] = byKind
  end
  local list = byKind[kind]
  if list == nil then
    if not make then return nil end
    list = {}
    byKind[kind] = list
  end
  return list
end

-- A listener registered with no options is stored as the function itself, which is every listener
-- on every page in this repository and costs one array slot. One registered WITH options is stored
-- as a small record instead, and the two are told apart by type at dispatch - so `capture` and
-- `once` cost a page that does not use them nothing at all.
local function entryFn(h) if type(h) == "table" then return h.fn end return h end
local function entryCapture(h) return type(h) == "table" and h.capture == true end

-- `capture` is read off the third argument, which is a boolean in the old form and an options
-- object in the current one. It used to be ignored outright, so a page registering a capturing
-- listener got a bubbling one and the difference showed only as a handler firing in the wrong order.
local function optionsOf(options)
  if options == true then return true, false end
  if type(options) == "table" then return js_truthy(options.capture), js_truthy(options.once) end
  return false, false
end

-- DOM.captures counts the capturing listeners that exist anywhere. It is zero on every ordinary
-- page, and DOM.fire skips the whole downward walk while it is, so the phase costs nothing until
-- something asks for it.
-- No keyboard event reaches a console page - the game keeps the keyboard - so a key listener is
-- kept, never fires, and is noted once: a page waiting on one otherwise looks like a page whose
-- handler is broken.
local NO_KEYBOARD = "no keyboard event reaches a console page: the game keeps the keyboard, so a keydown/keyup/keypress listener never fires"

function DOM.on(id, kind, fn, options)
  if fn == nil or id == nil then return end
  kind = js_str(kind)
  if kind == "keydown" or kind == "keyup" or kind == "keypress" then DOM.note(NO_KEYBOARD, id) end
  local capture, once = optionsOf(options)
  local list = listeners(id, kind, true)
  -- A browser ignores a repeat registration of the same function at the same phase.
  for i = 1, #list do
    if entryFn(list[i]) == fn and entryCapture(list[i]) == capture then return end
  end
  if capture or once then
    list[#list + 1] = { fn = fn, capture = capture, once = once }
    if capture then DOM.captures = DOM.captures + 1 end
  else
    list[#list + 1] = fn
  end
end

function DOM.off(id, kind, fn, options)
  local capture = optionsOf(options)
  local list = listeners(id, js_str(kind), false)
  if list == nil then return end
  for i = #list, 1, -1 do
    local h = list[i]
    if entryFn(h) == fn and entryCapture(h) == capture then
      if entryCapture(h) then DOM.captures = DOM.captures - 1 end
      table.remove(list, i)
    end
  end
end

-- Modifier keys as they were at the moment the player acted. The host fills this in before it calls
-- `event`; nothing else may, because a value nobody set would be a plausible `false` rather than an
-- answer. It exists as one table for the life of the chunk so delivering an event allocates nothing.
MODS = MODS or { shift = false, ctrl = false, alt = false, meta = false }

-- Events that do not bubble, which is the whole of the list a console page can receive. Getting
-- this wrong is invisible until a page puts a `focus` handler on a container and it fires.
local NoBubble = { focus = true, blur = true, mouseenter = true, mouseleave = true }

-- Only what a handler actually reads. A page that wants more gets `nil` rather than a wrong number,
-- which is the honest answer for a page with no layout: a compiled scene has no boxes to measure.
-- `key` and `code` are deliberately absent: no keyboard event reaches a console page at all, and a
-- MouseEvent has neither in a browser either.
-- `window` and `document` are event targets and are not elements, so they are answered with
-- themselves. Routing them through getElementById would invent an element and, worse, report the
-- page's scene as missing a shape for something that was never going to have one.
local function targetFor(id)
  if id == "window" then return window end
  if id == "document" then return document end
  return document.getElementById(id)
end

local function make_event(id, kind, x, y)
  local ev
  local at = targetFor(id)
  ev = {
    type = kind,
    target = at,
    currentTarget = at,
    bubbles = not NoBubble[kind], cancelable = true, defaultPrevented = false,
    eventPhase = 0, timeStamp = js_now and js_now() or 0, isTrusted = true,
    button = 0, buttons = (kind == "mousedown") and 1 or 0,
    clientX = x or 0, clientY = y or 0,
    pageX = x or 0, pageY = y or 0,
    offsetX = x or 0, offsetY = y or 0,
    x = x or 0, y = y or 0,
    shiftKey = MODS.shift == true, ctrlKey = MODS.ctrl == true,
    altKey = MODS.alt == true, metaKey = MODS.meta == true,
    preventDefault = function() ev.defaultPrevented = true end,
    stopPropagation = function() ev.__stop = true end,
    stopImmediatePropagation = function() ev.__stop = true ev.__now = true end,
  }
  return ev
end

-- One element's handlers for one phase. Returns true when the walk must stop immediately.
-- At the target both capturing and bubbling listeners run, in registration order, as they do in a
-- browser; on the way down only capturing ones, on the way back up only the rest.
local function deliver(ev, at, kind, phase)
  local list = listeners(at, kind, false)
  if list == nil then return false end
  ev.eventPhase = phase
  ev.currentTarget = targetFor(at)
  -- Over a snapshot: a handler may add or remove listeners while this runs, and the runner page
  -- does exactly that (a jump re-registers). Mutating the list under the loop skips handlers.
  local snapshot, n = {}, #list
  for i = 1, n do snapshot[i] = list[i] end
  for i = 1, n do
    local h = snapshot[i]
    local fn = entryFn(h)
    if fn ~= nil and (phase == 2 or entryCapture(h) == (phase == 1)) then
      -- Removed BEFORE it runs, so a handler that re-registers itself gets a listener of its own
      -- rather than having the registration wiped by this line.
      if type(h) == "table" and h.once then DOM.off(at, kind, fn, h) end
      fn(ev)
      if ev.__now then return true end
    end
  end
  return false
end

-- `up` levels above `id`, or nil past the root. The capture phase needs the chain from the root
-- down, and walking it this way costs O(depth^2) on a chain that is never more than a handful deep
-- while allocating nothing - which a path list per event would not.
local function ancestorAt(id, up)
  local at, guard = id, 0
  while up > 0 and at ~= nil and guard < 64 do at = PARENT[at] up = up - 1 guard = guard + 1 end
  return at
end

local function walk(ev, id, kind)
  if DOM.captures > 0 then
    local depth, at, guard = 0, PARENT[id], 0
    while at ~= nil and guard < 64 do depth = depth + 1 at = PARENT[at] guard = guard + 1 end
    for up = depth, 1, -1 do
      local step = ancestorAt(id, up)
      if step == nil then break end
      if deliver(ev, step, kind, 1) or ev.__stop then return end
    end
  end

  if deliver(ev, id, kind, 2) or ev.__stop or not ev.bubbles then return end

  local at, guard = PARENT[id], 0
  while at ~= nil and guard < 64 do
    guard = guard + 1
    if deliver(ev, at, kind, 3) or ev.__stop then return end
    at = PARENT[at]
  end
end

-- One event at one target, with an event object the caller keeps: a chip's data payload, which
-- arrives every tick. No walk - a window has no parent - and no snapshot table unless a handler is
-- already running one: the list is copied into a scratch kept for the purpose, so a handler that
-- adds or removes a listener does not skip the next one.
DOM.emitScratch = {}
function DOM.emit(id, kind, ev)
  local list = listeners(id, kind, false)
  if list == nil or #list == 0 then return end
  local snapshot, n, was = DOM.emitScratch, #list, DOM.emitting
  if was then snapshot = {} end
  DOM.emitting = true
  for i = 1, n do snapshot[i] = list[i] end
  for i = 1, n do
    local h = snapshot[i]
    snapshot[i] = nil
    local fn = entryFn(h)
    if fn ~= nil then
      if type(h) == "table" and h.once then DOM.off(id, kind, fn, h) end
      fn(ev)
    end
  end
  DOM.emitting = was
end

--- Fires one event at `id`, down to it and back up, as a browser does. It does NOT drain the
--- microtasks the handlers queued: `el.click()` and `dispatchEvent` come through here from the
--- page's own code, and a browser runs those reactions only once the script that made the call
--- has finished. The host's `event` entry drains after it, for a click the player made.
function DOM.fire(id, kind, x, y)
  if id == nil or kind == nil then return nil end
  kind = js_str(kind)
  local ev = make_event(id, kind, x, y)
  walk(ev, id, kind)
  return ev
end

-- The two shapes the compiler emits in place of a CSS string, with the literal pieces carried
-- along. HERE they rebuild exactly the string the page wrote, so a page run against this prelude
-- behaves identically to the original; a COMPILED page replaces both with versions that write the
-- numbers straight to their slots and build nothing. Same call, two runtimes.
-- toFixed's value WITHOUT its string. A page writes `x.toFixed(1) + 'px'` to format a number for
-- CSS, and when that lands on a numeric slot the string is built only to be parsed straight back.
-- Rounds away from zero on a half, as toFixed does - `math.floor(v * 10 + 0.5)` would round -0.05
-- the other way, which is a wrong pixel rather than a wrong string.
function js_fixnum(v, d)
  v = tonumber(v)
  if v == nil or v ~= v then return v end
  local m = 10 ^ (tonumber(d) or 0)
  if v < 0 then return -math.floor(-v * m + 0.5) / m end
  return math.floor(v * m + 0.5) / m
end

local function shown(v, d)
  if v == nil then return "" end
  if d == nil then return js_str(v) end
  return string.format("%." .. tostring(math.floor(d)) .. "f", tonumber(v) or 0)
end

-- `key` arrives complete ("style.height"): the compiler knows it and passing it whole is what
-- keeps the runtime from building one per write.
function DOM.num(el, key, n, unit, d)
  record(rawget(el, "__id"), key, shown(n, d) .. (unit or ""))
end

function DOM.xy(el, key, x, y, l1, l2, l3, dx, dy)
  record(rawget(el, "__id"), key,
         (l1 or "") .. shown(x, dx) .. (l2 or "") .. shown(y, dy) .. (l3 or ""))
end

-- setAttribute is a write like any other, recorded against the element it names - and kept as well,
-- so getAttribute, hasAttribute and dataset all answer with what was actually set rather than the
-- nil this used to return for everything.
function DOM.attribute(el, name, value)
  name = js_str(name)
  attrs(el)[name] = value
  record(rawget(el, "__id"), "@" .. name, value)
end

-- A page may schedule work. Both are recorded rather than run: the compiler decides what becomes an
-- on_frame chain and what becomes a tick, and neither is this prelude's business.
Pending = { frame = {}, timers = {} }
function requestAnimationFrame(fn) Pending.frame[#Pending.frame + 1] = fn return #Pending.frame end
function setInterval(fn, ms) Pending.timers[#Pending.timers + 1] = { fn = fn, ms = ms } return #Pending.timers end
function setTimeout(fn, ms) Pending.timers[#Pending.timers + 1] = { fn = fn, ms = ms, once = true } return #Pending.timers end

-- Cancelling is emptying the slot, not removing it: the handle a page holds is the index it was
-- given, and closing the gap would silently renumber every timer set after it. clearInterval was a
-- no-op here, so a page that started a poll and then stopped it kept polling for ever.
local function noop() end

function clearInterval(handle)
  local timer = Pending.timers[handle]
  if timer ~= nil then timer.fn = noop end
end

clearTimeout = clearInterval

-- The frame list is replaced wholesale each frame, so only a handle still inside it can be
-- cancelled. That is the right answer for the idiom pages write: a loop is stopped by cancelling
-- the handle from its last requestAnimationFrame, which is the one now pending.
function cancelAnimationFrame(handle)
  if type(handle) == "number" and handle >= 1 and handle <= #Pending.frame then
    Pending.frame[handle] = noop
  end
end

-- A page may listen on the window. Registering one is honest - the handler is kept and can be
-- fired - but nothing delivers a resize or a key here: the host sends a click at a scene region and
-- DOM.fire walks PARENT up from it, and PARENT has no entry above the page root. So a window
-- listener is stored and stays silent, which lets the page run rather than stopping it at the call.
function addEventListener(kind, fn, options) DOM.on("window", kind, fn, options) end
function removeEventListener(kind, fn, options) DOM.off("window", kind, fn, options) end
window.addEventListener = addEventListener
window.removeEventListener = removeEventListener
-- As a browser's is: a page that reaches its constructors through the global object finds this one.
window.Promise = Promise
document.dispatchEvent = function(ev) DOM.fire("document", ev ~= nil and ev.type or nil, 0, 0) return true end
