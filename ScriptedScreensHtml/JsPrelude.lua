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

-- Only what a console can answer. There is no wall clock a page should depend on here, and a page
-- that wants elapsed time has the frame's own timestamp, so this is the epoch plus the scene clock.
function js_date(ms)
  local at = type(ms) == "number" and ms or (js_now and js_now() or 0)
  return { __date = true, __ms = at,
           getTime = function() return at end,
           valueOf = function() return at end }
end

-- Only a pattern of plain characters ever reaches here: JsToLua reports anything with regular
-- expression syntax in it rather than pretending a Lua pattern is a regex. So a match IS a plain
-- substring search, and test and exec are exact rather than approximations.
--
-- The two methods are closures on the object because js_m calls whatever it finds without a
-- receiver, which is the same reason js_date is built this way. A regex literal with no syntax in
-- it is rare enough that two closures at the literal cost nothing worth measuring.
function js_regex(pattern, flags)
  flags = flags or ""
  local re
  re = { __regex = true, source = pattern, flags = flags, lastIndex = 0,
         global = flags:find("g", 1, true) ~= nil,
         ignoreCase = flags:find("i", 1, true) ~= nil }
  local function found(s)
    s = js_str(s)
    local needle = re.ignoreCase and pattern:lower() or pattern
    local hay = re.ignoreCase and s:lower() or s
    local from = re.global and (re.lastIndex + 1) or 1
    if from > #hay + 1 then return nil end
    local a, b = hay:find(needle, from, true)
    return a, b, s
  end
  re.test = function(s)
    local a, b = found(s)
    if a == nil then re.lastIndex = 0 return false end
    if re.global then re.lastIndex = b end
    return true
  end
  re.exec = function(s)
    local a, b, text = found(s)
    if a == nil then re.lastIndex = 0 return nil end
    if re.global then re.lastIndex = b end
    local m = js_array({ [0] = sub(text, a, b) }, 1)
    m.index = a - 1
    m.input = text
    return m
  end
  return re
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
    local m = ArrayMethods[name] or ObjectMethods[name]
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

-- Only a pattern of plain characters ever reaches js_regex, so a search is an indexOf. Anything
-- with real regular expression syntax in it was refused at compile time and never arrives.
function StringMethods.search(s, pattern)
  local plain = type(pattern) == "table" and pattern.source or js_str(pattern)
  return StringMethods.indexOf(s, plain)
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
  if text:find("[eE]") ~= nil then return text end
  local body = text:gsub("^-", "")
  local significant = #(body:gsub("%.", ""):gsub("^0+", ""))
  if significant == 0 then significant = 1 end
  if significant >= digits then return text end
  if body:find("%.") == nil then text = text .. "." end
  return text .. rep("0", digits - significant)
end

function NumberMethods.toExponential(v, digits)
  if v ~= v then return "NaN" end
  local text = digits == nil and format("%e", v)
                             or format("%." .. format("%d", floor(js_num(digits))) .. "e", v)
  -- C writes at least two exponent digits ("1.0e+01"); JavaScript writes the fewest it can.
  return (text:gsub("([eE][-+])0*(%d)", "%1%2"))
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
  -- log2 was named in the transpiler's manifest without ever being defined here, so a page using
  -- it compiled cleanly and then died on the console with nothing in its own source to point at.
  log2 = function(v) return math.log(v, 2) end,
  log10 = function(v) return math.log(v, 10) end,
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

-- The hyperbolics, from exp, which is how they are defined. `fround` is deliberately absent: it
-- needs the exponent of a double and this Lua has no frexp, so there is no way to round to float32
-- precision here that would not be an approximation of a rounding.
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

String.fromCodePoint = function(...)
  local parts = {}
  for i = 1, select("#", ...) do parts[i] = utf8char((select(i, ...))) end
  return concat(parts)
end
-- fromCharCode takes UTF-16 code units. Anything outside the basic plane arrives as a surrogate
-- pair, which this does not recombine: a page passing one gets two replacement-shaped sequences
-- rather than the character. Every console page in this repository passes plain ASCII.
String.fromCharCode = String.fromCodePoint
-- No String.raw: the only syntax that reaches it is a tagged template, which the transpiler
-- refuses, so defining it would add a name nothing can call.
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

-- `Date.now()`. The instant is whatever the page's own clock says, as js_date uses: there is no
-- wall clock a console page should depend on, and two clients reading different ones would draw
-- different pages.
Date = setmetatable({ now = function() return js_now and js_now() or 0 end },
                    { __call = function(_, ms) return js_date(ms) end })

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

DOM = { writes = {}, order = {}, missing = {}, listeners = {}, captures = 0 }

function DOM.reset()
  DOM.writes, DOM.order, DOM.missing = {}, {}, {}
  DOM.listeners, DOM.captures = {}, 0
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
local function insertAt(el, node, at)
  if type(node) ~= "table" or rawget(node, "__id") == nil then return node end
  local up, guard = el, 0
  while up ~= nil and guard < 64 do
    if up == node then return node end
    up = rawget(up, "__parent")
    guard = guard + 1
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

-- The four positions, as one call. insertAdjacentHTML and insertAdjacentText are deliberately NOT
-- here: both take markup or text rather than a node, and neither has anything to become in a model
-- with no text nodes and no run-time parser, so the compiler goes on refusing them by name.
function ElementMethods.insertAdjacentElement(el, where, node)
  where = js_str(where):lower()
  if where == "beforebegin" then ElementMethods.before(el, node)
  elseif where == "afterbegin" then ElementMethods.prepend(el, node)
  elseif where == "beforeend" then insertAt(el, node)
  elseif where == "afterend" then ElementMethods.after(el, node)
  else return nil end
  return node
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
local function trimmed(s) return (s:gsub("^%s+", ""):gsub("%s+$", "")) end

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

function ElementMethods.querySelectorAll(el, selector)
  local out = {}
  selector = js_str(selector)
  -- The markup below this element first, in document order, then this element's own script-built
  -- children - which collectMarkup cannot have reached, since it starts one level down.
  local n = collectMarkup(rawget(el, "__id"), selector, out, 0)
  return js_array(out, collect(el, selector, out, n))
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
  local out = {}
  selector = js_str(selector)
  local n = collectMarkup(nil, selector, out, 0)
  if #NODES == 0 then
    n = collect(document.documentElement, selector, out, n)
    n = collect(document.body, selector, out, n)
  end
  return js_array(out, n)
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
-- Deliberately NOT scrollHeight or scrollTop. `log.scrollTop = log.scrollHeight` is the idiom for
-- pinning a log to its foot, and the record of that write reading `undefined` is what says the page
-- asked for something a compiled page does not have. A zero would make it look answered.

ElementReads.parentNode = function(el)
  local up = rawget(el, "__parent")
  if up ~= nil then return up end
  local pid = PARENT[rawget(el, "__id")]
  if pid ~= nil then return element(pid) end
  return nil
end
ElementReads.parentElement = ElementReads.parentNode

ElementReads.children = function(el)
  local list = rawget(el, "__kids")
  if list == nil then return js_array({}, 0) end
  return js_array_of(list)
end
ElementReads.childNodes = ElementReads.children

ElementReads.firstChild = function(el)
  local list = rawget(el, "__kids")
  return list ~= nil and list[1] or nil
end
ElementReads.lastChild = function(el)
  local list = rawget(el, "__kids")
  return list ~= nil and list[#list] or nil
end
ElementReads.firstElementChild = ElementReads.firstChild
ElementReads.lastElementChild = ElementReads.lastChild

-- There are no text nodes here, so every child is an element and the two sibling families are the
-- same walk. Over the tree the script built; the page's own markup is shapes in a scene by now.
local function siblingOf(el, step)
  local up = ElementReads.parentNode(el)
  if up == nil then return nil end
  local list = rawget(up, "__kids")
  if list == nil then return nil end
  for i = 1, #list do if list[i] == el then return list[i + step] end end
  return nil
end

ElementReads.nextSibling = function(el) return siblingOf(el, 1) end
ElementReads.previousSibling = function(el) return siblingOf(el, -1) end
ElementReads.nextElementSibling = ElementReads.nextSibling
ElementReads.previousElementSibling = ElementReads.previousSibling

-- The element's own id, which is what it is keyed by - `el.id` read back nothing at all before,
-- because nothing had ever assigned the property. A page that assigns it gets its own value back;
-- it does not rename the element, since the scene's slots are bound to the compiled id.
ElementReads.id = function(el) return rawget(el, "__props").id or rawget(el, "__id") end

-- Node.ELEMENT_NODE. Everything this model holds is an element: there are no text or comment nodes.
ElementReads.nodeType = function() return 1 end

-- Enough of a NamedNodeMap to be counted and read. Built per call rather than kept, because it has
-- to follow setAttribute and className, and nothing reads it on a per-frame path.
ElementReads.attributes = function(el)
  local out, n = {}, 0
  out[n] = { name = "id", value = rawget(el, "__id") } n = n + 1
  local className = rawget(el, "__props").className
  if className ~= nil and className ~= "" then
    out[n] = { name = "class", value = js_str(className) } n = n + 1
  end
  local t = rawget(el, "__attrs")
  if t ~= nil then
    for name, value in pairs(t) do out[n] = { name = name, value = js_str(value) } n = n + 1 end
  end
  return js_array(out, n)
end

ElementReads.tagName = function(el)
  local tag = rawget(el, "__tag") or TAG[rawget(el, "__id")]
  return tag ~= nil and tag:upper() or nil
end

ElementReads.className = function(el)
  local own = rawget(el, "__props").className
  if own ~= nil then return own end
  return CLASS[rawget(el, "__id")]
end
ElementReads.nodeName = ElementReads.tagName

ElementReads.dataset = function(el)
  local ds = rawget(el, "__dataset")
  if ds == nil then
    ds = setmetatable({ __el = el }, DatasetMeta)
    rawset(el, "__dataset", ds)
  end
  return ds
end

-- The element's OWN style, which is all a compiled page can answer with: the cascade ran at compile
-- time and its result is in the scene, not in the chunk. So a page reads back what its script has
-- set and nothing else - a value that came from the stylesheet reads as nil, not as the stylesheet's
-- value. Returning the live style object rather than a copy is deliberate: it costs no allocation
-- and getPropertyValue works on it unchanged.
function getComputedStyle(el)
  if type(el) ~= "table" then return nil end
  return rawget(el, "__style")
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
function DOM.on(id, kind, fn, options)
  if fn == nil or id == nil then return end
  local capture, once = optionsOf(options)
  local list = listeners(id, js_str(kind), true)
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

--- Fires one event at `id`, down to it and back up, as a browser does.
function DOM.fire(id, kind, x, y)
  if id == nil or kind == nil then return nil end
  kind = js_str(kind)
  local ev = make_event(id, kind, x, y)

  if DOM.captures > 0 then
    local depth, at, guard = 0, PARENT[id], 0
    while at ~= nil and guard < 64 do depth = depth + 1 at = PARENT[at] guard = guard + 1 end
    for up = depth, 1, -1 do
      local step = ancestorAt(id, up)
      if step == nil then break end
      if deliver(ev, step, kind, 1) or ev.__stop then return ev end
    end
  end

  if deliver(ev, id, kind, 2) or ev.__stop or not ev.bubbles then return ev end

  local at, guard = PARENT[id], 0
  while at ~= nil and guard < 64 do
    guard = guard + 1
    if deliver(ev, at, kind, 3) or ev.__stop then return ev end
    at = PARENT[at]
  end
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

-- A microtask runs before the next render in a browser and on the next frame here, which is the
-- finest grain a chunk driven once a frame has. Worth knowing for ordering against a timer set in
-- the same breath; nothing else about it differs.
function queueMicrotask(fn) return setTimeout(fn, 0) end

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
document.dispatchEvent = function(ev) DOM.fire("document", ev ~= nil and ev.type or nil, 0, 0) return true end
