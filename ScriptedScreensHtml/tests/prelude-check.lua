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
eq(js_m(123.456, 'toPrecision', 2), '1.2e+02', 'toPrecision exponential')
eq(js_m(0.000123, 'toPrecision', 2), '0.00012', 'toPrecision small')
eq(js_m(12345, 'toExponential', 2), '1.23e+4', 'toExponential')

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

-- RegExp: only a literal pattern ever reaches the prelude, so both answers are exact
local re = js_regex('lo', '')
eq(re.test('hello'), true, 'regex test hit')
eq(re.test('heck'), false, 'regex test miss')
eq(re.exec('hello').index, 3, 'regex exec index')
eq(re.exec('hello')[0], 'lo', 'regex exec match')
eq(js_regex('LO', 'i').test('hello'), true, 'regex ignoreCase')
local g = js_regex('a', 'g')
eq(g.exec('aba').index, 0, 'global exec first')
eq(g.exec('aba').index, 2, 'global exec advances')
eq(g.exec('aba'), nil, 'global exec ends')

if fails == 0 then print('all prelude checks pass') else print(fails .. ' FAILED') os.exit(1) end
