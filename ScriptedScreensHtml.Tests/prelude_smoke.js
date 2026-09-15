// Runs the extracted prelude with every C# binding stubbed, then exercises the DOM shim the
// way a page would. Any exception here would be a broken page script in game.
const fs = require('fs');
const src = fs.readFileSync(__dirname + '/prelude_extracted.js', 'utf8');
const attrs = { body: {}, a: { class: 'x y', value: 'v', 'data-k': 'z' }, b: {}, form1: {} };
const parents = { a: 'body', b: 'a' };
const children = { body: ['a', 'form1'], a: ['b'] };
const calls = [];
const stubs = {
  __log: (l, m) => calls.push('log:' + l + ':' + m),
  __has: id => id in attrs,
  __query: sel => (sel === '.x' ? ['a'] : sel === 'form' ? ['form1'] : sel === '*' ? Object.keys(attrs) : []),
  __setStyle: (id, p, v) => calls.push('style:' + id + ':' + p + '=' + v),
  __setText: (id, t) => calls.push('text:' + id + '=' + t),
  __setHtml: (id, h) => calls.push('html:' + id + '=' + h),
  __setClass: (id, c) => { attrs[id].class = c; },
  __getAttr: (id, n) => (attrs[id] && attrs[id][n] !== undefined ? attrs[id][n] : null),
  __setAttr: (id, n, v) => { (attrs[id] = attrs[id] || {})[n] = v; },
  __removeAttr: (id, n) => { delete attrs[id][n]; },
  __appendHtml: (p, h) => calls.push('append:' + p + ':' + h),
  __insertHtml: (p, h, b) => calls.push('insert:' + p + ':' + h + ':' + b),
  __remove: id => calls.push('remove:' + id),
  __size: () => [10, 20],
  __canvasFrame: () => {},
  __now: () => 0,
  __setValue: (id, v) => calls.push('value:' + id + '=' + v),
  __wantClicks: id => calls.push('wantclicks:' + id),
  __wantPointer: t => calls.push('wantpointer:' + t),
  __textOf: id => 'text of ' + id,
  __htmlOf: (id, outer) => (outer ? '<div id="' + id + '">x</div>' : 'x'),
  __children: id => children[id] || [],
  __parent: id => parents[id] || null,
  __attrs: id => Object.entries(attrs[id] || {}).flat(),
  __contains: (a, id) => { for (let p = parents[id]; p; p = parents[p]) if (p === a) return true; return false; },
  __rect: () => [1, 2, 3, 4],
  __cssOf: (id, p) => (p === 'color' ? 'red' : ''),
  __viewport: () => [640, 480],
  __media: q => q.indexOf('min-width') >= 0,
  __animate: () => 7,
  __cancelAnimation: h => calls.push('cancel:' + h),
  __children_rects: () => [30, 40],
};
Object.assign(globalThis, stubs);
require('vm').runInThisContext(src);   // top-level vars become globals, as in the engine

// exercise
const a = document.getElementById('a');
if (a.value !== 'v') throw new Error('value');
a.style.width = '5px';
if (a.style.width !== '5px' || a.style.cssText.indexOf('width: 5px') < 0) throw new Error('style cache ' + a.style.cssText);
if (getComputedStyle(a).getPropertyValue('color') !== 'red') throw new Error('computed');
if (a.className !== 'x y' || !a.classList.contains('y')) throw new Error('className');
if (a.getBoundingClientRect().width !== 3) throw new Error('rect');
if (a.scrollHeight !== 40) throw new Error('scrollHeight');
let got = null; a.addEventListener('ping', e => { got = e.detail; });
a.dispatchEvent(new CustomEvent('ping', { detail: 42 }));
if (got !== 42) throw new Error('dispatchEvent');
let bubbled = false; a.addEventListener('click', e => { bubbled = e.target.id === 'b'; });
__click('b', 10, 20);
if (!bubbled) throw new Error('bubbling');
document.getElementById('b').insertAdjacentHTML('afterbegin', '<i>1</i>');
a.prepend(document.createElement('span'));
new URL('https://x.y:1/p?q=2#h').searchParams.get('q') === '2' || (() => { throw new Error('URL'); })();
if (new URLSearchParams('a=1').toString() !== 'a=1') throw new Error('USP');
if (!/^[0-9a-f-]{36}$/.test(crypto.randomUUID())) throw new Error('uuid');
if (new TextDecoder().decode(new TextEncoder().encode('héllo')) !== 'héllo') throw new Error('textenc');
const anim = a.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 100 });
if (anim.id !== 7) throw new Error('animate');
anim.cancel();
if (!matchMedia('(min-width: 10px)').matches || window.innerWidth !== 640) throw new Error('media/viewport');
let ready = 0; document.addEventListener('DOMContentLoaded', () => ready++); window.addEventListener('load', () => ready++);
__ready();
if (ready !== 2 || document.readyState !== 'complete') throw new Error('ready ' + ready);
__input('a', 'typed'); __pointer('a', 'mouseover', 1, 1);
const f = document.getElementById('form1'); f.submit();
localStorage.setItem('k', '1'); if (localStorage.getItem('k') !== '1') throw new Error('storage');
document.getElementsByClassName('x').length === 1 || (() => { throw new Error('byClass'); })();
if (document.getElementsByName('q').length !== 0) throw new Error('byName');
new Audio('u.ogg').play(); new Image().src = 'i.png';
// canvas recorder: every 2D call records, the frame flushes as numbers plus a string table
let frame = null;
globalThis.__canvasFrame = (id, cmds, cols, n) => { frame = { id, cmds: cmds.slice(0, n), cols }; };
const cx = document.getElementById('a').getContext('2d');
cx.fillStyle = '#ff0000'; cx.fillRect(1, 2, 3, 4);
cx.beginPath(); cx.moveTo(0, 0); cx.lineTo(10, 10); cx.arc(5, 5, 3, 0, Math.PI); cx.closePath(); cx.lineWidth = 2; cx.strokeStyle = 'blue'; cx.stroke();
const g = cx.createLinearGradient(0, 0, 10, 0); g.addColorStop(0, 'red'); g.addColorStop(1, 'blue'); cx.fillStyle = g; cx.fill('evenodd');
cx.font = 'bold 14px Barlow'; cx.textAlign = 'center'; cx.fillText('hi', 5, 5);
cx.save(); cx.translate(1, 1); cx.rotate(0.1); cx.scale(2, 2); cx.roundRect(0, 0, 4, 4, 1); cx.clip(); cx.restore();
cx.shadowBlur = 2; cx.shadowColor = '#000'; cx.fillRect(0, 0, 1, 1);
cx.setLineDash([2, 1]); cx.ellipse(1, 1, 2, 3, 0, 0, 1, false); cx.drawImage({ src: 'x.png', width: 4, height: 4 }, 0, 0);
if (cx.measureText('abc').width <= 0) throw new Error('measureText');
__flushCanvases();
if (!frame || frame.cmds.length < 40 || frame.cols.indexOf('#ff0000') < 0 || !frame.cols.some(c => c.indexOf('GL|0|0|10|0|0:red;1:blue') === 0)) throw new Error('canvas frame ' + JSON.stringify(frame));
if (frame.cols.indexOf('bold 14px Barlow') < 0 || frame.cols.indexOf('hi') < 0) throw new Error('canvas strings');
// DocumentFragment: appended as its children, no wrapper; emptied afterwards, reusable
const frag = document.createDocumentFragment();
const li1 = document.createElement('li'); li1.textContent = 'one';
const li2 = document.createElement('li'); li2.className = 'two';
frag.appendChild(li1); frag.appendChild(li2);
if (frag.childNodes.length !== 2 || frag.firstChild !== li1) throw new Error('fragment children');
a.appendChild(frag);
const last = calls[calls.length - 1];
if (last.indexOf('append:a:<li') !== 0 || last.indexOf('<div') >= 0 || last.indexOf('class="two"') < 0 || last.indexOf('one') < 0) throw new Error('fragment append ' + last);
if (frag.childNodes.length !== 0 || !li1.__live) throw new Error('fragment not emptied/adopted');
// an adopted script-made element answers with the live element's API (replaceWith, before, siblings)
const mid = document.createElement('span'); mid.id = 'mid'; mid.textContent = 'mid';
let fired = false; mid.addEventListener('ping', () => { fired = true; });
a.appendChild(mid);
if (typeof mid.replaceWith !== 'function' || typeof mid.before !== 'function') throw new Error('adopted element lacks live API');
attrs.mid = {}; parents.mid = 'a';
mid.dispatchEvent(new CustomEvent('ping'));
if (!fired) throw new Error('listener added before adoption did not carry over');
mid.replaceWith(document.createElement('b'));
if (calls[calls.length - 1] !== 'remove:mid') throw new Error('replaceWith on adopted element ' + calls[calls.length - 1]);
process.stdout.write('prelude smoke ok; ' + calls.length + ' binding calls' + String.fromCharCode(10));
