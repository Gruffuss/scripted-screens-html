-- One runnable check over everything added to the prelude. Fails loudly on the first difference.
-- Run from anywhere:  lua ScriptedScreensHtml/tests/prelude-check.lua
-- Nothing builds or references this; it is the smallest thing that fails if the prelude's DOM,
-- array, string or object methods stop behaving the way a browser does.
dofile(((arg and arg[0] or ''):gsub('[^/\\]*$', '')) .. '../JsPrelude.lua')

local fails = 0
local function eq(got, want, what)
  if got ~= want then fails = fails + 1 print('FAIL ' .. what .. ': got ' .. tostring(got) .. ', want ' .. tostring(want)) end
end
local function arr(a) local p = {} for i = 0, a.length - 1 do p[i+1] = tostring(a[i]) end return table.concat(p, ',') end

-- strings
eq(js_m('hello world', 'substr', 6), 'world', 'substr from')
eq(js_m('hello', 'substr', 1, 3), 'ell', 'substr count')
eq(js_m('hello', 'substr', -3, 2), 'll', 'substr negative')
eq(js_m('A', 'charCodeAt', 0), 65, 'charCodeAt')
eq(js_m('A', 'codePointAt', 0), 65, 'codePointAt')
eq(js_m('abc', 'at', -1), 'c', 'at negative')
eq(js_m('abc', 'at', 5), nil, 'at past end')
eq(js_m('abcabc', 'lastIndexOf', 'b'), 4, 'lastIndexOf')
eq(js_m('a', 'localeCompare', 'b'), -1, 'localeCompare')
eq(js_m('ab', 'repeat', 3), 'ababab', 'repeat')
eq(js_m('  x  ', 'trimStart'), 'x  ', 'trimStart')
eq(js_m('  x  ', 'trimEnd'), '  x', 'trimEnd')
eq(js_m('aXb', 'toLocaleUpperCase'), 'AXB', 'toLocaleUpperCase')
eq(js_m('hello', 'search', 'llo'), 2, 'search')

-- numbers
eq(js_m(1.5, 'toPrecision', 4), '1.500', 'toPrecision pads')
-- JavaScript writes the fewest exponent digits it can, where C writes at least two and the
-- interpreter the game embeds writes three AND a capital E. This row used to expect C's "1.2e+02",
-- which is not what a browser prints and not what the game printed either.
eq(js_m(123.456, 'toPrecision', 2), '1.2e+2', 'toPrecision exponential')
eq(js_m(0.000123, 'toPrecision', 2), '0.00012', 'toPrecision small')
eq(js_m(12345, 'toExponential', 2), '1.23e+4', 'toExponential')
-- toExponential with no argument: `%e` without a precision is ignored outright by the game's Lua,
-- so this used to answer "12345" in game and "1.2345e+4" here.
eq(js_m(12345, 'toExponential'), '1.2345e+4', 'toExponential with no precision')
eq(js_m(0.00012, 'toExponential'), '1.2e-4', 'toExponential negative exponent')
eq(js_m(1, 'toExponential'), '1e+0', 'toExponential of one')
-- and the same exponent shape wherever a number is turned into text
eq(js_str(1e21), '1e+21', 'a number past the positional range prints as JavaScript writes it')
eq(js_str(1e-7), '1e-7', 'and so does a small one')
eq(js_str(1.5e300), '1.5e+300', 'a three-digit exponent is not padded further')

-- arrays
local a = js_array({[0]=1,[1]=2,[2]=3}, 3)
eq(js_m(a, 'shift'), 1, 'shift value')
eq(arr(a), '2,3', 'shift rest')
eq(js_m(a, 'unshift', 0, 1), 4, 'unshift length')
eq(arr(a), '0,1,2,3', 'unshift order')
eq(arr(js_m(a, 'splice', 1, 2, 'x')), '1,2', 'splice removed')
eq(arr(a), '0,x,3', 'splice result')
eq(a.length, 3, 'splice length')
local b = js_array({[0]=1,[1]=2,[2]=3,[3]=4}, 4)
eq(arr(js_m(b, 'reverse')), '4,3,2,1', 'reverse')
eq(arr(js_m(b, 'fill', 9, 1, 3)), '4,9,9,1', 'fill')
eq(js_m(b, 'at', -1), 1, 'array at')
eq(js_m(b, 'findIndex', function(v) return v == 9 end), 1, 'findIndex')
eq(js_m(b, 'findLast', function(v) return v == 9 end), 9, 'findLast')
eq(js_m(b, 'findLastIndex', function(v) return v == 9 end), 2, 'findLastIndex')
eq(js_m(b, 'lastIndexOf', 9), 2, 'array lastIndexOf')
local inner = js_array({[0]=3}, 1)
local nest = js_array({[0]=js_array({[0]=1,[1]=2},2), [1]=js_array({[0]=inner},1)}, 2)
eq(js_m(nest, 'flat').length, 3, 'flat depth 1 count')
eq(js_m(nest, 'flat')[2], inner, 'flat depth 1 keeps the inner array')
eq(arr(js_m(nest, 'flat', 2)), '1,2,3', 'flat depth 2')
eq(arr(js_m(js_array({[0]=1,[1]=2},2), 'flatMap', function(v) return js_array({[0]=v,[1]=v},2) end)), '1,1,2,2', 'flatMap')
-- splice with no items and a lone deleteCount, the log-trimming idiom
local log = js_array({[0]='a',[1]='b',[2]='c',[3]='d'}, 4)
js_m(log, 'splice', 0, 2)
eq(arr(log), 'c,d', 'splice trim')

-- Array statics
eq(Array.isArray(a), true, 'isArray array')
eq(Array.isArray({}), false, 'isArray object')
eq(arr(Array.of(1,2,3)), '1,2,3', 'Array.of')
eq(arr(Array.from('abc')), 'a,b,c', 'Array.from string')
eq(arr(Array.from(js_array({[0]=1,[1]=2},2), function(v) return v * 10 end)), '10,20', 'Array.from map')
eq(arr(Array.from(js_set(js_array({[0]='p',[1]='q'},2)))), 'p,q', 'Array.from set')

-- Object
local o = { a = 1 }
eq(arr(Object.values(o)), '1', 'Object.values')
eq(Object.fromEntries(js_array({[0]=js_array({[0]='k',[1]='v'},2)},1)).k, 'v', 'Object.fromEntries')
eq(Object.freeze(o), o, 'Object.freeze')
eq(Object.hasOwn(o, 'a'), true, 'Object.hasOwn yes')
eq(Object.hasOwn(o, 'z'), false, 'Object.hasOwn no')

-- Math / Number
eq(Math.log2(8), 3.0, 'log2')
-- Exact at an exact power. The game's Lua answers 2.9999999999999996 for log10(1000), which makes
-- the digit-counting idiom `floor(log10(n)) + 1` one too few for every power of ten.
eq(Math.log10(1000), 3.0, 'log10 is exact at a power of ten')
eq(Math.log10(1e6), 6.0, 'and at a larger one')
eq(Math.log2(1024), 10.0, 'log2 is exact at a power of two')
eq(math.abs(Math.log10(5) - 0.6989700043360189) < 1e-15, true, 'and is unchanged elsewhere')
eq(Math.log10(1000), 3.0, 'log10')
eq(math.floor(Math.cbrt(27) + 0.5), 3, 'cbrt')
eq(Math.cbrt(-8) < 0, true, 'cbrt negative')
eq(Math.clz32(1), 31, 'clz32 one')
eq(Math.clz32(0), 32, 'clz32 zero')
eq(Number.isInteger(4), true, 'isInteger yes')
eq(Number.isInteger(4.5), false, 'isInteger no')
eq(Number.parseInt('42'), 42, 'Number.parseInt')
eq(type(Date.now()), 'number', 'Date.now')

-- timers
local ran = 0
local h = setInterval(function() ran = ran + 1 end, 10)
clearInterval(h)
Pending.timers[h].fn()
eq(ran, 0, 'clearInterval silences')
local h2 = setTimeout(function() ran = ran + 1 end, 10)
clearTimeout(h2)
Pending.timers[h2].fn()
eq(ran, 0, 'clearTimeout silences')
local h3 = requestAnimationFrame(function() ran = ran + 1 end)
cancelAnimationFrame(h3)
Pending.frame[h3]()
eq(ran, 0, 'cancelAnimationFrame silences')

-- DOM: attributes
local el = document.getElementById('box')
js_m(el, 'setAttribute', 'data-act', '7')
eq(js_m(el, 'getAttribute', 'data-act'), '7', 'getAttribute')
eq(js_m(el, 'hasAttribute', 'data-act'), true, 'hasAttribute')
eq(el.dataset.act, '7', 'dataset read')
el.dataset.actIndex = 3
eq(js_m(el, 'getAttribute', 'data-act-index'), '3', 'dataset camelCase')
js_m(el, 'removeAttribute', 'data-act')
eq(js_m(el, 'hasAttribute', 'data-act'), false, 'removeAttribute')
eq(js_m(el, 'toggleAttribute', 'hidden'), true, 'toggleAttribute on')
eq(js_m(el, 'toggleAttribute', 'hidden'), false, 'toggleAttribute off')
eq(js_m(el, 'getAttribute', 'id'), 'box', 'getAttribute id')
el.className = 'card wide'
eq(js_m(el, 'getAttribute', 'class'), 'card wide', 'getAttribute class')

-- DOM: style
js_m(el.style, 'setProperty', 'font-size', '12px')
eq(el.style.fontSize, '12px', 'setProperty kebab to camel')
eq(js_m(el.style, 'getPropertyValue', 'font-size'), '12px', 'getPropertyValue')
js_m(el.style, 'setProperty', '--accent', '#f00')
eq(el.style['--accent'], '#f00', 'setProperty custom property')

-- DOM: tree
local parent = document.createElement('div')
local one, two = js_m(document, 'createElement', 'span'), document.createElement('b')
eq(js_m(parent, 'appendChild', one), one, 'appendChild returns')
js_m(parent, 'appendChild', two)
eq(one.parentNode, parent, 'parentNode')
eq(parent.children.length, 2, 'children length')
eq(parent.firstChild, one, 'firstChild')
eq(parent.lastChild, two, 'lastChild')
eq(parent.tagName, 'DIV', 'tagName')
local three = document.createElement('i')
js_m(parent, 'insertBefore', three, two)
eq(parent.children[1], three, 'insertBefore position')
js_m(parent, 'removeChild', three)
eq(parent.children.length, 2, 'removeChild')
js_m(one, 'after', three)
eq(parent.children[1], three, 'after')
js_m(three, 'remove')
eq(parent.children.length, 2, 'remove')
js_m(one, 'before', three)
eq(parent.children[0], three, 'before')
js_m(three, 'replaceWith', document.createElement('u'))
eq(parent.children[0].tagName, 'U', 'replaceWith')
-- appending a node into itself is refused rather than looping for ever
js_m(parent, 'appendChild', parent)
eq(parent.parentNode, nil, 'self append refused')
local clone = js_m(parent, 'cloneNode', true)
eq(clone.tagName, 'DIV', 'cloneNode tag')
eq(clone.children.length, parent.children.length, 'cloneNode deep')
eq(js_m(parent, 'cloneNode').children.length, 0, 'cloneNode shallow')
eq(clone == parent, false, 'cloneNode is new')

-- DOM: selectors
one.className = 'row hot'
eq(js_m(one, 'matches', '.hot'), true, 'matches class')
eq(js_m(one, 'matches', 'span'), true, 'matches tag')
eq(js_m(one, 'matches', 'div, span'), true, 'matches list')
eq(js_m(one, 'matches', '.cold'), false, 'matches miss')
eq(js_m(one, 'closest', 'div'), parent, 'closest up the built tree')
eq(js_m(parent, 'getElementsByTagName', 'span').length, 1, 'getElementsByTagName')
eq(js_m(parent, 'getElementsByClassName', 'hot').length, 1, 'getElementsByClassName')
eq(js_m(parent, 'getElementsByClassName', 'row hot').length, 1, 'getElementsByClassName both')
eq(js_m(parent, 'getElementsByClassName', 'row cold').length, 0, 'getElementsByClassName and')
eq(document.querySelector('#box'), el, 'querySelector by id')
eq(document.querySelector('.box'), nil, 'querySelector other')
eq(document.createElementNS('http://www.w3.org/2000/svg', 'rect').tagName, 'RECT', 'createElementNS')

-- closest walks the compiled PARENT chain too, which is the page's own markup
PARENT['leaf'] = 'trunk'
eq(js_m(document.getElementById('leaf'), 'closest', '#trunk'), document.getElementById('trunk'), 'closest via PARENT')

-- DOM: an element the compiler measured answers with its real box; one it did not - anything the
-- script created - keeps the zeros a browser gives a detached node.
eq(js_m(el, 'getBoundingClientRect').width, 0, 'rect width with no box')
eq(el.offsetWidth, 0, 'offsetWidth with no box')
eq(el.offsetLeft, nil, 'offsetLeft stays undefined without an offset parent')
BOXES['box'] = { 8, 12, 100, 20, 96, 16 }
eq(js_m(el, 'getBoundingClientRect').width, 100, 'rect width from BOXES')
eq(js_m(el, 'getBoundingClientRect').bottom, 32, 'rect bottom from BOXES')
eq(js_m(el, 'getBoundingClientRect') == js_m(el, 'getBoundingClientRect'), true, 'rect is made once')
eq(el.offsetWidth, 100, 'offsetWidth from BOXES')
eq(el.clientWidth, 96, 'clientWidth is the content box')
eq(el.scrollHeight, nil, 'scrollHeight stays undefined')
eq(js_m(getComputedStyle(el), 'getPropertyValue', 'font-size'), '12px', 'getComputedStyle reads own style')

-- DOM: the reads that used to answer nothing
eq(el.id, 'box', 'id')
eq(el.nodeType, 1, 'nodeType')
eq(js_m(one, 'matches', 'span.hot'), true, 'matches tag and class')
eq(js_m(one, 'matches', 'div.hot'), false, 'matches wrong tag')
eq(js_m(one, 'matches', 'span#other'), false, 'a compound with an id is refused, not guessed')
eq(js_m(parent, 'contains', one), true, 'contains a child')
eq(js_m(one, 'contains', parent), false, 'contains is not upside down')
eq(js_m(parent, 'querySelector', '.hot'), one, 'querySelector')
eq(js_m(parent, 'querySelectorAll', 'span').length, 1, 'querySelectorAll')
eq(one.nextSibling, parent.children[2], 'nextSibling')
eq(js_m(parent, 'insertAdjacentElement', 'afterbegin', document.createElement('em')).tagName, 'EM', 'insertAdjacentElement')
eq(parent.children[0].tagName, 'EM', 'insertAdjacentElement afterbegin')
-- one node per CALL: two createElement('div') used to hand back the same element
eq(document.createElement('div') == document.createElement('div'), false, 'createElement is per call')
eq(document.head ~= nil, true, 'document.head')
el.className = 'card wide'
eq(el.classList.length, 2, 'classList.length')
eq(el.classList[0], 'card', 'classList index')
eq(js_m(el.classList, 'replace', 'card', 'panel'), true, 'classList.replace')
eq(el.className, 'panel wide', 'classList.replace keeps position')
eq(el.attributes.length, 3, 'attributes counts id, class and what setAttribute left')

-- events
local fired = 0
js_m(el, 'addEventListener', 'click', function() fired = fired + 1 end)
js_m(el, 'dispatchEvent', { type = 'click' })
eq(fired, 1, 'dispatchEvent')
addEventListener('resize', function() end)
eq(DOM.listeners['window'] ~= nil, true, 'window listener kept')

-- the event object: what a handler reads, and the modifier keys the host fills in
local seen
js_m(el, 'addEventListener', 'mousedown', function(ev) seen = ev end)
MODS.shift = true
DOM.fire('box', 'mousedown', 3, 4)
eq(seen.type, 'mousedown', 'event.type')
eq(seen.target, el, 'event.target')
eq(seen.currentTarget, el, 'event.currentTarget')
eq(seen.clientX + seen.clientY, 7, 'event coordinates')
eq(seen.shiftKey, true, 'event.shiftKey follows MODS')
eq(seen.ctrlKey, false, 'event.ctrlKey')
eq(seen.eventPhase, 2, 'eventPhase at the target')
eq(seen.bubbles, true, 'a click bubbles')
MODS.shift = false

-- once, capture and the phases, over a two-level chain
PARENT['kid'] = 'box'
local order = ''
local kid = document.getElementById('kid')
js_m(el, 'addEventListener', 'click', function() order = order .. 'A' end, true)
js_m(kid, 'addEventListener', 'click', function() order = order .. 'B' end)
js_m(el, 'addEventListener', 'click', function() order = order .. 'C' end)
DOM.fire('kid', 'click', 0, 0)
eq(order, 'ABC', 'capture runs before the target, bubble after')
local onceRan = 0
js_m(kid, 'addEventListener', 'click', function() onceRan = onceRan + 1 end, { once = true })
DOM.fire('kid', 'click', 0, 0)
DOM.fire('kid', 'click', 0, 0)
eq(onceRan, 1, 'once fires once')
-- removing a capturing listener needs the flag, as it does in a browser
local cap = function() order = order .. 'X' end
js_m(el, 'addEventListener', 'click', cap, true)
js_m(el, 'removeEventListener', 'click', cap)
order = ''
DOM.fire('kid', 'click', 0, 0)
eq(order, 'AXBC', 'removeEventListener without the capture flag leaves it alone')
js_m(el, 'removeEventListener', 'click', cap, true)
order = ''
DOM.fire('kid', 'click', 0, 0)
eq(order, 'ABC', 'removeEventListener with the flag takes it')

-- the property form of a handler is a listener, not a value written to the scene
local propRan = 0
local btn = document.getElementById('btn')
btn.onclick = function() propRan = propRan + 1 end
js_m(btn, 'click')
eq(propRan, 1, 'onclick fires')
eq(DOM.writes['btn.onclick'], nil, 'onclick is not written to the scene')
btn.onclick = nil
js_m(btn, 'click')
eq(propRan, 1, 'onclick cleared')

-- focus moves within the chunk and fires both events; it does not bubble
local focusLog = ''
local fa, fb = document.getElementById('fa'), document.getElementById('fb')
js_m(fa, 'addEventListener', 'focus', function() focusLog = focusLog .. 'in' end)
js_m(fa, 'addEventListener', 'blur', function() focusLog = focusLog .. 'out' end)
js_m(fa, 'focus')
eq(document.activeElement, fa, 'activeElement follows focus')
js_m(fb, 'focus')
eq(focusLog, 'inout', 'focus then blur')
eq(js_m(document.getElementById('fa'), 'contains', fa), true, 'the same element comes back by id')

-- a bound method is made once and kept, not per access
eq(el.appendChild == el.appendChild, true, 'bound method is stable')

-- the page's own markup, as the compiler hands it over: the node list, the tags and the classes.
-- Without these the only tree a compiled page can see is the one its script built.
NODES = { 'app', 'card', 'title', 'row' }
PARENT['card'] = 'app' PARENT['title'] = 'card' PARENT['row'] = 'card'
TAG['app'] = 'div' TAG['card'] = 'section' TAG['title'] = 'h1' TAG['row'] = 'p'
CLASS['card'] = 'panel wide' CLASS['row'] = 'row'
local card = document.getElementById('card')
eq(card.tagName, 'SECTION', 'tagName from the markup')
eq(card.className, 'panel wide', 'className from the markup')
eq(js_m(card, 'getAttribute', 'class'), 'panel wide', 'getAttribute class from the markup')
eq(card.classList.contains('wide'), true, 'classList sees the markup class')
eq(js_m(card, 'matches', 'section.panel'), true, 'matches the markup')
eq(js_m(document.getElementById('title'), 'closest', '.panel'), card, 'closest through the markup')
eq(document.querySelectorAll('p').length, 1, 'document.querySelectorAll by tag')
eq(document.querySelector('.panel'), card, 'document.querySelector by class')
eq(document.getElementsByTagName('h1').length, 1, 'document.getElementsByTagName')
eq(document.getElementsByClassName('panel wide').length, 1, 'document.getElementsByClassName')
eq(js_m(card, 'querySelectorAll', '*').length, 2, 'querySelectorAll below an element')
eq(js_m(card, 'querySelectorAll', '*')[0], document.getElementById('title'), 'in document order')
eq(js_m(document.getElementById('title'), 'querySelectorAll', '*').length, 0, 'a leaf has no descendants')
-- a class the SCRIPT writes wins over the markup's, as assigning className does in a browser
card.className = 'panel'
eq(card.classList.contains('wide'), false, 'a written className replaces the markup one')

-- style.cssText is a whole block, split into the properties the compiler bound
js_m(el.style, 'setProperty', 'width', '1px')
el.style.cssText = 'width: 5px; top:2px'
eq(el.style.width, '5px', 'cssText width')
eq(el.style.top, '2px', 'cssText top')
eq(DOM.writes['box.style.cssText'], nil, 'cssText is not a slot')

-- Math, Number and Object, the tail a page reaches for
eq(math.floor(Math.sinh(1) * 1000 + 0.5), 1175, 'sinh')
eq(math.floor(Math.cosh(1) * 1000 + 0.5), 1543, 'cosh')
eq(math.floor(Math.tanh(1) * 1000 + 0.5), 762, 'tanh')
eq(math.floor(Math.asinh(1) * 1000 + 0.5), 881, 'asinh')
eq(Math.acosh(1), 0.0, 'acosh')
eq(math.floor(Math.atanh(0.5) * 1000 + 0.5), 549, 'atanh')
eq(math.floor(Math.expm1(1) * 1000 + 0.5), 1718, 'expm1')
eq(math.floor(Math.log1p(1) * 1000 + 0.5), 693, 'log1p')
-- the browser's own answers, which is the point: a 32-bit product that a double cannot hold
eq(Math.imul(3, 4), 12, 'imul small')
eq(Math.imul(-5, 12), -60, 'imul negative')
eq(Math.imul(0xffffffff, 5), -5, 'imul wraps')
eq(Math.imul(0x7fffffff, 0x7fffffff), 1, 'imul overflows to one')
eq(Number.isSafeInteger(9007199254740991), true, 'isSafeInteger edge')
eq(Number.isSafeInteger(9007199254740993), false, 'isSafeInteger past it')
eq(Object.is(0 / 0, 0 / 0), true, 'Object.is NaN')
-- Lua's integer 0 has no sign, so the two zeros are only distinguishable as floats; a page writing
-- `-0` gets an integer and Object.is answers true where a browser answers false.
eq(Object.is(0.0, -0.0), false, 'Object.is zeros')
eq(Object.is(1, 1), true, 'Object.is equal')
local proto = { greet = 1 }
local made = Object.create(proto)
eq(made.greet, 1, 'Object.create proto')
eq(Object.getPrototypeOf(made), proto, 'Object.getPrototypeOf')
eq(Object.getOwnPropertyNames(proto).length, 1, 'getOwnPropertyNames')
eq(Object.seal(proto), proto, 'Object.seal returns its argument')
eq(js_m({ z = 1 }, 'hasOwnProperty', 'z'), true, 'hasOwnProperty yes')
eq(js_m({ z = 1 }, 'hasOwnProperty', 'q'), false, 'hasOwnProperty no')
eq(String.fromCharCode(65, 66), 'AB', 'fromCharCode')
eq(String.fromCodePoint(960), string.char(207, 128), 'fromCodePoint encodes UTF-8')

-- ---- regular expressions -----------------------------------------------------------------------
--
-- Table driven, because a regex engine is a hundred small behaviours and a handful of hand-written
-- asserts checks the ones that were already working. Every expectation below was produced by
-- running the same pattern, flags and subject through V8 and copying its answer: 164 cases were
-- compared that way and the only difference left is the byte-versus-code-point one noted at the
-- foot of this block.
local B = '\\'
local function rx(p, f) return js_regex(p, f) end

-- pattern, flags, subject, whole match (nil = no match), first group
local matching = {
  { 'abc', '', 'xxabcyy', 'abc' },
  { '[0-9]+', '', 'a123b', '123' },
  { '[^0-9]+', '', '123abc456', 'abc' },
  { B .. 'd+', '', 'ab 42', '42' },
  { B .. 'D+', '', '12ab', 'ab' },
  { B .. 'w+', '', '  foo_1 ', 'foo_1' },
  { B .. 'W+', '', 'ab!! cd', '!! ' },
  { B .. 's+', '', 'a  \tb', '  \t' },
  { B .. 'S+', '', '  ab  ', 'ab' },
  { 'a.c', '', 'a-c', 'a-c' },
  { 'a.c', '', 'a\nc', nil },
  { 'a.c', 's', 'a\nc', 'a\nc' },
  { '^abc', '', 'xabc', nil },
  { 'abc$', '', 'xabc', 'abc' },
  { '^b', 'm', 'a\nb', 'b' },
  { 'a$', 'm', 'a\nb', 'a' },
  { '^.*$', 'm', 'aa\nbb', 'aa' },
  { B .. 'bfoo' .. B .. 'b', '', 'a foo b', 'foo' },
  { B .. 'bfoo' .. B .. 'b', '', 'afoob', nil },
  { B .. 'Bfoo', '', 'afoo', 'foo' },
  { 'a*', '', 'aaab', 'aaa' },
  { 'a+', '', 'b', nil },
  { 'ab?c', '', 'ac', 'ac' },
  { 'a{3}', '', 'aaaa', 'aaa' },
  { 'a{2,}', '', 'aaaa', 'aaaa' },
  { 'a{2,3}', '', 'aaaa', 'aaa' },
  { 'a+?', '', 'aaa', 'a' },
  { 'a{2,3}?', '', 'aaaa', 'aa' },
  { '<.+>', '', '<a><b>', '<a><b>' },
  { '<.+?>', '', '<a><b>', '<a>' },
  { 'cat|dog', '', 'I have a dog', 'dog' },
  { '(ab)+', '', 'ababab', 'ababab', 'ab' },
  { '(?:ab)+', '', 'ababab', 'ababab' },
  { '(a|b)c', '', 'bc', 'bc', 'b' },
  { '(a)?b', '', 'b', 'b', group = false },
  { '(' .. B .. 'w)' .. B .. '1', '', 'abbc', 'bb', 'b' },
  { '(' .. B .. 'w+)' .. B .. 's+' .. B .. '1', '', 'hey hey there', 'hey hey', 'hey' },
  { 'ABC', 'i', 'xxabcyy', 'abc' },
  { '[a-z]+', 'i', 'XYZ', 'XYZ' },
  { '[^a-z]+', 'i', 'abcQ!', '!' },
  { '(A)' .. B .. '1', 'i', 'aA', 'aA', 'a' },
  { 'a(?=b)', '', 'ab', 'a' },
  { 'a(?=b)', '', 'ac', nil },
  { 'a(?!b)', '', 'ac', 'a' },
  { 'a(?!b)', '', 'ab', nil },
  { 'q(?!u)', '', 'quit qatar', 'q' },
  -- the empty iteration of an unbounded repeat is DISCARDED, captures and all, which is why
  -- group 1 here is undefined and why `(|a)*` gets past the empty branch to match "aa"
  { '(a*)*', '', 'b', '', group = false },
  { '(a*)+', '', 'aab', 'aa', 'aa' },
  { '(|a)*', '', 'aa', 'aa', 'a' },
  -- and a repetition starts each round with its own groups unset
  { '(?:(a)|(b))+', '', 'ab', 'ab', group = false },
  { 'colou?r', '', 'color', 'color' },
  { '[' .. B .. 'd.]+', '', 'v1.25x', '1.25' },
  { B .. '$' .. B .. 'd+', '', 'cost $42', '$42' },
  { '[-+]?' .. B .. 'd+', '', 'x-12', '-12' },
  { '(?<year>' .. B .. 'd{4})', '', 'in 2026', '2026', '2026' },
  { '(foo|bar)baz', '', 'xbarbaz', 'barbaz', 'bar' },
  { 'x[^]y', '', 'xQy', 'xQy' },
  { B .. 'u0041', '', 'ZAZ', 'A' },
  { B .. 'x41', '', 'ZAZ', 'A' },
  { '[A-Fa-f0-9]{6}', '', '#1a2B3c!', '1a2B3c' },
  { '[' .. B .. ']]', '', 'a]b', ']' },
  { '[]]', '', 'a]b', nil },                 -- an empty class, then a literal ] - never matches
  { '[a-c-e]', '', 'd-e', '-' },
  { '(a|ab)(c|bcd)(d*)', '', 'abcd', 'abcd', 'a' },
  { '^(?:(' .. B .. 'd+)|(' .. B .. 'w+))$', '', 'abc', 'abc', group = false },
  { '(.)(?=' .. B .. '1)', '', 'aab', 'a', 'a' },
  { '[' .. B .. 'b]', '', 'a\bb', '\b' },    -- a backspace inside a class, not a boundary
}
for _, c in ipairs(matching) do
  local m = js_m(rx(c[1], c[2]), 'exec', c[3])
  local label = 'regex /' .. c[1] .. '/' .. c[2] .. ' on ' .. string.format('%q', c[3])
  eq(m and m[0] or nil, c[4], label)
  -- `group = false` is how a row says the group did NOT participate. A trailing nil in the row
  -- would not be there to test: the table would simply be one shorter and the check would be
  -- skipped in silence, which is the failure this file exists to catch.
  local wantGroup = c.group
  if wantGroup == nil then wantGroup = c[5] end
  if wantGroup ~= nil then
    eq(m and m[1] or nil, wantGroup ~= false and wantGroup or nil, label .. ' group 1')
  end
end

eq(js_m(rx('l+', ''), 'test', 'hello'), true, 'regex test hit')
eq(js_m(rx('z', ''), 'test', 'hello'), false, 'regex test miss')
eq(js_m(rx('lo', ''), 'exec', 'hello').index, 3, 'regex exec index')
eq(js_m(rx('(x)(y)?', ''), 'exec', 'zx').length, 3, 'a match array is one longer than the groups')

-- lastIndex, which is the whole difference a `g` makes to exec
local g = rx('a', 'g')
eq(js_m(g, 'exec', 'aba').index, 0, 'global exec first')
eq(js_m(g, 'exec', 'aba').index, 2, 'global exec advances')
eq(js_m(g, 'exec', 'aba'), nil, 'global exec ends')
eq(g.lastIndex, 0, 'lastIndex resets when the scan runs out')
eq(js_m(rx('b', 'y'), 'test', 'ab'), false, 'a sticky pattern only matches at lastIndex')

-- replace, in all four of its shapes
eq(js_m('a1b22c', 'replace', rx('[' .. B .. 'd]+', 'g'), '#'), 'a#b#c', 'replace global')
eq(js_m('a1b22c', 'replace', rx('[' .. B .. 'd]+', ''), '#'), 'a#b22c', 'replace first only')
eq(js_m('John Smith', 'replace', rx('(' .. B .. 'w+) (' .. B .. 'w+)', ''), '$2 $1'), 'Smith John', 'replace $1 $2')
eq(js_m('abc', 'replace', rx('b', ''), '[$&]'), 'a[b]c', 'replace $&')
eq(js_m('abc', 'replace', rx('b', ''), '$$'), 'a$c', 'replace $$')
eq(js_m('abc', 'replace', rx('b', ''), "$`|$'"), 'aa|cc', 'replace $` and $twice')
eq(js_m('a-b', 'replace', rx('-', ''), function(_, off) return '<' .. off .. '>' end), 'a<1>b', 'a replacer gets the offset')
eq(js_m('a1b2', 'replace', rx('(' .. B .. 'd)', 'g'), function(_, p1) return '[' .. p1 .. ']' end),
   'a[1]b[2]', 'a replacer gets the groups')
eq(js_m('in 2026', 'replace', rx('(?<y>' .. B .. 'd{4})', ''), 'year $<y>'), 'in year 2026', 'replace $<name>')
eq(js_m('a-b-c', 'replaceAll', '-', '+'), 'a+b+c', 'replaceAll with a plain needle takes every one')
eq(js_m('a-b-c', 'replace', '-', '+'), 'a+b-c', 'replace with a plain needle takes the first')
eq(js_m('abc', 'replaceAll', '', '-'), '-a-b-c-', 'replaceAll with an empty needle')
-- the thousands separator: zero-width, a lookahead, a nested quantifier and a negative lookahead
eq(js_m('1234567', 'replace', rx(B .. 'B(?=(' .. B .. 'd{3})+(?!' .. B .. 'd))', 'g'), ','),
   '1,234,567', 'the thousands-separator idiom')

-- match, matchAll, search, split
local all = js_m('a1b22c', 'match', rx(B .. 'd+', 'g'))
eq(all.length, 2, 'match global count')
eq(all[1], '22', 'match global second')
eq(js_m('abc', 'match', rx('z', 'g')), nil, 'match global miss is null, not an empty array')
local one = js_m('a12b', 'match', rx('(' .. B .. 'd)(' .. B .. 'd)', ''))
eq(one[2], '2', 'match without g gives the groups')
eq(one.index, 1, 'match without g gives the index')
local each = js_m('a1b2', 'matchAll', rx('(' .. B .. 'd)', 'g'))
eq(each.length, 2, 'matchAll count')
eq(each[1][1], '2', 'matchAll keeps each match its groups')
eq(js_m('hello', 'search', rx('l+', '')), 2, 'search')
eq(js_m('a.c', 'search', '.'), 0, 'search takes a STRING as a pattern, not as a literal')
eq(js_m('abc', 'search', rx('z', '')), -1, 'search miss')
eq(js_m(js_m('a1b22c', 'split', rx(B .. 'd+', '')), 'join', '|'), 'a|b|c', 'split by a regex')
eq(js_m(js_m('a1b', 'split', rx('(' .. B .. 'd)', '')), 'join', '|'), 'a|1|b', 'split keeps the captures')
eq(js_m('abc', 'split', rx('(?:)', '')).length, 3, 'split by an empty match')
eq(js_m(js_m('ab cd', 'split', rx(B .. 'b', '')), 'join', '|'), 'ab| |cd', 'a separator at the end is not one')
eq(js_m('a,b,c', 'split', ',', 2).length, 2, 'split honours its limit')

-- a pattern that cannot be answered is an error, never a wrong answer
eq(pcall(js_regex, '(a', ''), false, 'an unbalanced ( is refused')
eq(pcall(js_regex, 'a', 'q'), false, 'an unknown flag is refused')
eq(pcall(js_regex, '(?<=a)b', ''), false, 'lookbehind is refused')
eq(pcall(js_regex, 'a{1,5000}', ''), false, 'an enormous quantifier is refused')
eq(pcall(js_regex, '[z-a]', ''), false, 'a backwards range is refused')
-- and a pattern that backtracks catastrophically gives up loudly rather than freezing the game
eq(pcall(function() return js_m(js_regex('(a*)*b', ''), 'test', string.rep('a', 24) .. 'c') end),
   false, 'catastrophic backtracking gives up')

-- BYTES, not code points: for the ASCII a console page carries this is exactly a browser's answer,
-- and beyond it a multi-byte character counts as its own bytes. Pinned so the boundary is a
-- recorded decision rather than a surprise.
eq(js_m(rx(B .. 'u00e9', ''), 'exec', 'caf\195\169!')[0], '\195\169', 'a non-ASCII literal matches its own bytes')
eq(js_m(rx(B .. 'u00e9', ''), 'exec', 'caf\195\169!').index, 3, 'and its index is a byte index')

-- ---- the standard library tail --------------------------------------------------------------
local nums = js_array({ [0] = 3, [1] = 1, [2] = 2 }, 3)
eq(arr(js_m(nums, 'toSorted')), '1,2,3', 'toSorted')
eq(arr(nums), '3,1,2', 'toSorted leaves the original alone')
eq(arr(js_m(nums, 'toReversed')), '2,1,3', 'toReversed')
eq(arr(js_m(nums, 'with', 1, 9)), '3,9,2', 'with')
eq(arr(nums), '3,1,2', 'with leaves the original alone')
eq(arr(js_m(nums, 'toSpliced', 1, 1, 'x')), '3,x,2', 'toSpliced')
eq(pcall(js_m, nums, 'with', 5, 0), false, 'with refuses an index outside the array')
eq(js_m(js_array({ [0] = 'a', [1] = 'b', [2] = 'c' }, 3), 'reduceRight', function(acc, v) return acc .. v end),
   'cba', 'reduceRight')
eq(arr(js_m(js_array({ [0] = 1, [1] = 2, [2] = 3, [3] = 4, [4] = 5 }, 5), 'copyWithin', 0, 3)),
   '4,5,3,4,5', 'copyWithin')
eq(arr(js_m(js_array({ [0] = 1, [1] = 2, [2] = 3, [3] = 4, [4] = 5 }, 5), 'copyWithin', 1, 0, 3)),
   '1,1,2,3,5', 'copyWithin overlapping forwards')
-- a hole joins as nothing, not as the word "undefined"
eq(js_m(js_array({ [0] = 1, [2] = 3 }, 3), 'join', '-'), '1--3', 'join renders a hole as empty')

eq(Math.fround(16777217), 16777216, 'fround drops what a float32 cannot hold')
eq(Math.fround(0.5), 0.5, 'fround keeps a value a float32 holds exactly')
eq(Math.fround(Math.fround(1.1)), Math.fround(1.1), 'fround is idempotent')
eq(Math.fround(1.1) ~= 1.1, true, 'fround actually rounds')
eq(math.abs(Math.fround(1.1) - 1.1) < 1e-7, true, 'and rounds to something very close')
eq(Math.fround(1e39), math.huge, 'past the largest float32 is Infinity')
eq(Math.fround(1e-46), 0, 'below the smallest subnormal is zero')
eq(Math.fround(-0.0), -0.0, 'fround keeps a zero')
-- Ties at the exponents where this Lua's log(v, 2) is off by one, checked against V8's answers.
-- They do NOT cover the correction in fround: where the log is wrong the value is exactly
-- representable, so removing the correction leaves every answer here unchanged. It is there for
-- the interpreter the game embeds, whose log is a different one.
eq(Math.fround(2 ^ -29 * (1 + 2 ^ -24)), 2 ^ -29, 'fround rounds at an exponent the log gets wrong')
eq(Math.fround(2 ^ -29 * (1 + 3 * 2 ^ -24)), 2 ^ -29 * (1 + 2 ^ -22), 'and rounds the tie to even')
eq(Math.fround(2 ^ -62 * (1 + 2 ^ -24)), 2 ^ -62, 'at another of the exponents it gets wrong')

eq(String.raw({ [0] = 'a', [1] = 'b', length = 2 }, 7), 'a7b', 'String.raw joins the pieces')
eq(js_m('plain', 'normalize'), 'plain', 'normalize leaves ASCII alone')
eq(pcall(js_m, 'caf\195\169', 'normalize'), false, 'and says so rather than guessing at anything else')

-- ---- Date -------------------------------------------------------------------------------------
-- Every calendar answer below was taken from V8's own UTC accessors for the same instant.
eq(js_m(js_date(1234), 'getTime'), 1234, 'getTime needs no world clock')
eq(pcall(js_m, js_date(0), 'getFullYear'), false, 'a calendar accessor refuses without EPOCH')
EPOCH = 0
local when = js_date('2026-09-22T14:30:05.250Z')
eq(js_m(when, 'getFullYear'), 2026, 'getFullYear')
eq(js_m(when, 'getMonth'), 8, 'getMonth is 0-based')
eq(js_m(when, 'getDate'), 22, 'getDate')
eq(js_m(when, 'getDay'), 2, 'getDay')
eq(js_m(when, 'getHours'), 14, 'getHours')
eq(js_m(when, 'getMinutes'), 30, 'getMinutes')
eq(js_m(when, 'getSeconds'), 5, 'getSeconds')
eq(js_str(js_m(when, 'getMilliseconds')), '250', 'getMilliseconds')
eq(js_m(when, 'getTimezoneOffset'), 0, 'everything here is UTC')
eq(js_m(when, 'toISOString'), '2026-09-22T14:30:05.250Z', 'toISOString')
eq(js_m(when, 'toLocaleDateString'), '2026-09-22', 'a fixed date format, not a per-player one')
eq(js_m(js_date('1969-07-20T20:17:00.000Z'), 'getFullYear'), 1969, 'before the epoch')
eq(js_m(js_date('2000-02-29T00:00:00.000Z'), 'getDate'), 29, 'a leap day')
eq(js_m(js_date(2026, 0, 32), 'toISOString'), '2026-02-01T00:00:00.000Z', 'the component form rolls over')
eq(js_m(js_date(2026, 13, 1), 'getFullYear'), 2027, 'and carries a month into the year')
eq(js_m(js_date('not a date'), 'toISOString'), 'Invalid Date', 'an unparseable string is an Invalid Date')
EPOCH = nil

-- ---- text nodes, fragments and the tree they make ---------------------------------------------
local list = document.getElementById('a-list')
local row = document.createElement('li')
local text = document.createTextNode('hello')
js_m(row, 'appendChild', text)
js_m(list, 'appendChild', row)
eq(text.nodeType, 3, 'a text node is nodeType 3')
eq(text.nodeValue, 'hello', 'and carries its text')
eq(text.nodeName, '#text', 'and is named #text')
eq(text.tagName, nil, 'and has no tag name')
eq(row.childNodes.length, 1, 'childNodes counts a text node')
eq(row.children.length, 0, 'children does not')
eq(row.firstElementChild, nil, 'nor does firstElementChild')
js_m(row, 'insertAdjacentText', 'beforeend', 'more')
eq(row.lastChild.nodeValue, 'more', 'insertAdjacentText appends one')

local frag = document.createDocumentFragment()
local em, strong = document.createElement('em'), document.createElement('strong')
js_m(frag, 'appendChild', em)
js_m(frag, 'appendChild', strong)
eq(frag.childNodes.length, 2, 'a fragment holds its children')
eq(frag.nodeType, 11, 'and is nodeType 11')
js_m(list, 'appendChild', frag)
eq(frag.childNodes.length, 0, 'appending a fragment empties it')
eq(list.children.length, 3, 'and leaves its children behind, in order')
eq(list.children[1].tagName, 'EM', 'first of them')
eq(list.children[2].tagName, 'STRONG', 'then the next')
eq(row.nextElementSibling.tagName, 'EM', 'nextElementSibling skips text nodes')


-- js_m: an array method only on an array. A canvas context has fill(), save() and translate() too,
-- and reaching Array.fill with it died inside the method rather than naming the call.
local okFill, errFill = pcall(js_m, {}, 'fill', 0)
eq(okFill, false, 'fill on a plain object is refused')
eq(tostring(errFill):find('the page calls .fill() on a object', 1, true) ~= nil, true, 'and the refusal names the call')
local clsView = document.getElementById('cls')
clsView.className = 'one two'
local seenClasses = ''
js_m(clsView.classList, 'forEach', function(c) seenClasses = seenClasses .. c end)
eq(seenClasses, 'onetwo', 'but classList, whose length is derived, still takes an array method')

-- Promise: reactions run on the microtask queue, drained after the script
local order = {}
local p1 = js_promise(function(res) res(1) end)
js_m(js_m(p1, 'then', function(v) order[#order + 1] = 'then' .. v return v + 1 end), 'then',
     function(v) order[#order + 1] = 'chain' .. v end)
order[#order + 1] = 'sync'
js_microtasks()
eq(table.concat(order, ','), 'sync,then1,chain2', 'then runs after the script, in chain order')
local caught
js_m(js_m(js_m(Promise, 'reject', 'boom'), 'catch', function(e) caught = e return 'ok' end), 'then',
     function(v) caught = caught .. '/' .. v end)
js_microtasks()
eq(caught, 'boom/ok', 'catch recovers and the chain continues')
local fin = ''
js_m(js_m(js_m(Promise, 'resolve', 5), 'finally', function() fin = fin .. 'f' end), 'then', function(v) fin = fin .. v end)
js_microtasks()
eq(fin, 'f5', 'finally runs and passes the value through')
local thrown
js_m(js_m(js_m(Promise, 'resolve', 1), 'then', function() error(js_error('x'), 0) end), 'catch', function(e) thrown = e.message end)
js_microtasks()
eq(thrown, 'x', 'a throw in then rejects the next promise')
local adopted
js_m(js_m(Promise, 'resolve', js_m(Promise, 'resolve', 7)), 'then', function(v) adopted = v end)
js_microtasks()
eq(adopted, 7, 'resolve adopts a promise')
local waited
js_m(js_m(js_m(Promise, 'resolve', 1), 'then', function(v) return js_promise(function(res) res(v + 10) end) end), 'then',
     function(v) waited = v end)
js_microtasks()
eq(waited, 11, 'a promise returned from then is waited for')
local thenable
js_m(js_m(Promise, 'resolve', { ['then'] = function(res) res('foreign') end }), 'then', function(v) thenable = v end)
js_microtasks()
eq(thenable, 'foreign', 'a foreign thenable is adopted')
local allv, allr, raced, settledv, anyv, anye
js_m(js_m(Promise, 'all', js_array({ [0] = js_m(Promise, 'resolve', 1), [1] = 2 }, 2)), 'then', function(v) allv = arr(v) end)
js_m(js_m(Promise, 'all', js_array({ [0] = js_m(Promise, 'reject', 'no'), [1] = 2 }, 2)), 'catch', function(e) allr = e end)
js_m(js_m(Promise, 'race', js_array({ [0] = js_m(Promise, 'resolve', 'a'), [1] = js_m(Promise, 'resolve', 'b') }, 2)), 'then',
     function(v) raced = v end)
js_m(js_m(Promise, 'allSettled', js_array({ [0] = js_m(Promise, 'resolve', 1), [1] = js_m(Promise, 'reject', 'e') }, 2)), 'then',
     function(v) settledv = v[0].status .. v[0].value .. v[1].status .. v[1].reason end)
js_m(js_m(Promise, 'any', js_array({ [0] = js_m(Promise, 'reject', 'x'), [1] = js_m(Promise, 'resolve', 'y') }, 2)), 'then',
     function(v) anyv = v end)
js_m(js_m(Promise, 'any', js_array({ [0] = js_m(Promise, 'reject', 'x') }, 1)), 'catch', function(e) anye = e.name .. arr(e.errors) end)
js_microtasks()
eq(allv, '1,2', 'all collects in order')
eq(allr, 'no', 'all rejects on the first rejection')
eq(raced, 'a', 'race takes the first settled')
eq(settledv, 'fulfilled1rejectede', 'allSettled reports both outcomes')
eq(anyv, 'y', 'any takes the first fulfilled')
eq(anye, 'AggregateErrorx', 'any rejects with every reason when none fulfils')
local seq = ''
js_promise(function(res) seq = seq .. 'e' res() end)
queueMicrotask(function() seq = seq .. 'm' end)
seq = seq .. 's'
js_microtasks()
eq(seq, 'esm', 'the executor is synchronous, a microtask waits for the script')
local nested = ''
queueMicrotask(function() nested = nested .. 'a' queueMicrotask(function() nested = nested .. 'b' end) end)
js_microtasks()
eq(nested, 'ab', 'a microtask queued during the drain runs in it')
-- a click the page itself makes: the handler's reaction waits for the page's own line to finish
local evp, viaClick = document.getElementById('evp'), ''
js_m(evp, 'addEventListener', 'click',
     function() js_m(js_m(Promise, 'resolve', 'M'), 'then', function(v) viaClick = viaClick .. v end) end)
js_m(evp, 'click')
viaClick = viaClick .. 'S'
eq(viaClick, 'S', 'a promise settled in a click handler waits for the script that clicked')
js_microtasks()
eq(viaClick, 'SM', 'and runs when the host drains, after it: the order a browser gives')
-- a job that throws leaves the ones behind it for the next drain
local after = ''
queueMicrotask(function() error('first', 0) end)
queueMicrotask(function() after = after .. 'second' end)
eq(pcall(js_microtasks), false, 'a throwing microtask reaches the host')
js_microtasks()
eq(after, 'second', 'and the rest of the queue survives it')
-- a rejection nobody handles is noted; one caught later in the same script is not
DOM.notes = {}
js_m(Promise, 'reject', js_error('lost'))
js_microtasks()
eq(select(2, next(DOM.notes)), 'lost', 'an unhandled rejection is noted with its message')
DOM.notes = {}
local late = js_m(Promise, 'reject', 'x')
js_m(late, 'catch', function() end)
js_microtasks()
eq(next(DOM.notes), nil, 'a rejection caught before the drain is not')

-- the markup a script reads back: exact for what it wrote or built, noted for the page's own
local made = document.createElement('div')
made.id = 'x'
made.className = 'k'
js_m(made, 'setAttribute', 'data-n', '1')
js_m(made, 'appendChild', document.createTextNode('a<b'))
eq(made.outerHTML, '<div id="x" class="k" data-n="1">a&lt;b</div>', 'outerHTML of a built element')
eq(made.innerHTML, 'a&lt;b', 'innerHTML of a built element')
eq(made.textContent, 'a<b', 'textContent of a built element')
made.innerHTML = '<b>y</b>'
eq(made.innerHTML, '<b>y</b>', 'innerHTML reads back a write')
eq(made.textContent, 'y', 'textContent reads a written innerHTML without its tags')
made.innerText = 'z'
eq(made.innerText, 'z', 'innerText reads back a write')
eq(document.createElement('p').attributes.length, 0, 'a created element has no attributes until one is set')
DOM.notes = {}
eq(card.innerHTML, nil, 'the markup of a page element is not in the chunk')
eq(next(DOM.notes) ~= nil, true, 'and reading it is noted')
DOM.notes = {}
eq(card.scrollTop, nil, 'scrollTop stays undefined')
eq(next(DOM.notes) ~= nil, true, 'and reading it is noted')
DOM.notes = {}
card.scrollTop = 12
eq(card.scrollTop, 12, 'a scroll offset the page wrote reads back')
eq(next(DOM.notes), nil, 'and is not noted: the page asked for nothing the chunk lacks')
card.scrollTop = card.scrollHeight
eq(card.scrollTop, nil, 'one set from a scrollHeight the chunk has not is undefined again')
BOXES['card'] = { 0, 0, 240, 32, 240, 32 }
eq(getComputedStyle(card).width, '240px', 'getComputedStyle width from the laid-out box')
card.style.width = '10px'
eq(getComputedStyle(card).width, '10px', 'a script write wins over the box')
eq(getComputedStyle(card) == getComputedStyle(card), true, 'the computed view is made once')
DOM.notes = {}
eq(getComputedStyle(card).color, nil, 'a cascaded colour is not in the chunk')
eq(next(DOM.notes) ~= nil, true, 'and reading it is noted')
DOM.notes = {}
js_m(card, 'addEventListener', 'keydown', function() end)
eq(next(DOM.notes) ~= nil, true, 'a key listener is noted: no keyboard event reaches a console')
-- the page's own markup is enumerable from an element, not only from the document
eq(js_m(document.getElementById('app'), 'getElementsByClassName', 'panel').length, 1, 'getElementsByClassName finds the markup below an element')
eq(js_m(document.getElementById('app'), 'getElementsByTagName', 'p').length, 1, 'getElementsByTagName finds the markup below an element')

if fails == 0 then print('all prelude checks pass') else print(fails .. ' FAILED') os.exit(1) end
