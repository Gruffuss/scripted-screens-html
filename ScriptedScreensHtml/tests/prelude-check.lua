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

-- DOM: boxes are zero, and say so
eq(js_m(el, 'getBoundingClientRect').width, 0, 'rect width')
eq(el.offsetWidth, 0, 'offsetWidth')
eq(el.scrollHeight, nil, 'scrollHeight stays undefined')
eq(js_m(getComputedStyle(el), 'getPropertyValue', 'font-size'), '12px', 'getComputedStyle reads own style')

-- events
local fired = 0
js_m(el, 'addEventListener', 'click', function() fired = fired + 1 end)
js_m(el, 'dispatchEvent', { type = 'click' })
eq(fired, 1, 'dispatchEvent')
addEventListener('resize', function() end)
eq(DOM.listeners['window'] ~= nil, true, 'window listener kept')

-- a bound method is made once and kept, not per access
eq(el.appendChild == el.appendChild, true, 'bound method is stable')

if fails == 0 then print('all prelude checks pass') else print(fails .. ' FAILED') os.exit(1) end
