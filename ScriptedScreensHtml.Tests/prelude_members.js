// Checks the prelude's smaller Web API members with every binding stubbed to nothing.
const fs = require('fs');
const vm = require('vm');
const stubs = { __log() {}, __has: () => false, __query: () => [], __setStyle() {}, __setText() {}, __setHtml() {}, __setClass() {}, __getAttr: () => null, __setAttr() {}, __removeAttr() {}, __appendHtml() {}, __insertHtml() {}, __remove() {}, __size: () => [1, 1], __canvasFrame() {}, __now: () => 0,  __setValue() {}, __wantClicks() {}, __wantPointer() {}, __textOf: () => '', __htmlOf: () => '', __children: () => [], __parent: () => null, __attrs: () => [], __contains: () => false, __rect: () => [0, 0, 0, 0], __cssOf: () => '', __viewport: () => [1, 1], __media: () => false, __animate: () => 1, __cancelAnimation() {}, __children_rects: () => [0, 0], __setScroll() {}, __scrollBox: () => null, __elementAt: (x, y) => (x > 5 ? 'b' : null) };
Object.assign(globalThis, stubs);
vm.runInThisContext(fs.readFileSync(__dirname + '/prelude_extracted.js', 'utf8'));
if (btoa('hello') !== 'aGVsbG8=' || atob('aGVsbG8=') !== 'hello') throw new Error('base64 ' + btoa('hello') + ' ' + atob('aGVsbG8='));
if (btoa('ab') !== 'YWI=' || atob('YWI=') !== 'ab' || btoa('abc') !== 'YWJj') throw new Error('base64 padding ' + btoa('ab') + ' ' + btoa('abc'));
const e = document.createElement('div');
if (!(e instanceof HTMLElement) || !(e instanceof Node) || !(e instanceof EventTarget) || !(e instanceof Element)) throw new Error('instanceof');
if ({} instanceof HTMLElement) throw new Error('instanceof false positive');
const k = new KeyboardEvent('keydown', { key: 'a' });
if (k.key !== 'a' || !(k instanceof Event)) throw new Error('KeyboardEvent');
const o = new Option('Text', 'v', false, true);
if (o.textContent !== 'Text' || o.getAttribute('value') !== 'v' || o.getAttribute('selected') === null) throw new Error('Option');
if (document.elementFromPoint(1, 1) !== null) throw new Error('elementFromPoint null');
if (document.elementFromPoint(9, 9) === null || document.elementsFromPoint(9, 9).length < 1) throw new Error('elementFromPoint');
process.stdout.write('prelude members ok' + String.fromCharCode(10));
