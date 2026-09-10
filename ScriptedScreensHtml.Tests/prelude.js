
var window = globalThis;
var console = {
  log: function(){ __log('log', Array.prototype.slice.call(arguments).join(' ')); },
  warn: function(){ __log('warn', Array.prototype.slice.call(arguments).join(' ')); },
  error: function(){ __log('error', Array.prototype.slice.call(arguments).join(' ')); },
  info: function(){ __log('log', Array.prototype.slice.call(arguments).join(' ')); }
};
var performance = { now: function(){ return __now(); } };

// ---- timers and animation frames ----
var __timers = [], __rafs = [], __nextId = 1, __now_ms = 0;
function setTimeout(fn, ms){ var id = __nextId++; __timers.push({id:id, fn:fn, at:__now_ms + (ms||0), every:0, args:Array.prototype.slice.call(arguments,2)}); return id; }
function setInterval(fn, ms){ var id = __nextId++; __timers.push({id:id, fn:fn, at:__now_ms + (ms||0), every:Math.max(1, ms||1), args:Array.prototype.slice.call(arguments,2)}); return id; }
function clearTimeout(id){ __timers = __timers.filter(function(t){ return t.id !== id; }); }
var clearInterval = clearTimeout;
function requestAnimationFrame(fn){ var id = __nextId++; __rafs.push({id:id, fn:fn}); return id; }
function cancelAnimationFrame(id){ __rafs = __rafs.filter(function(r){ return r.id !== id; }); }
function __hasPending(){ return __rafs.length > 0 || __timers.length > 0; }
function __tick(now){
  __now_ms = now;
  var rafs = __rafs; __rafs = [];
  for (var i = 0; i < rafs.length; i++) { try { rafs[i].fn(now); } catch (e) { console.error(String(e && e.stack || e)); } }
  var due = __timers.filter(function(t){ return t.at <= now; });
  for (var j = 0; j < due.length; j++) {
    var t = due[j];
    if (t.every) t.at = now + t.every; else __timers = __timers.filter(function(x){ return x !== t; });
    try { t.fn.apply(null, t.args); } catch (e) { console.error(String(e && e.stack || e)); }
  }
  __flushCanvases();
  return __hasPending();
}

// ---- events ----
var __listeners = {};
function addEventListener(type, fn){ (__listeners[type] = __listeners[type] || []).push(fn); }
function removeEventListener(type, fn){ if (__listeners[type]) __listeners[type] = __listeners[type].filter(function(f){ return f !== fn; }); }
function __hasDataHandler(){ return !!(window.ondata) || !!(__listeners['data'] && __listeners['data'].length); }
function __emit(type, json){
  var detail = json ? JSON.parse(json) : null;
  var ev = { type: type, detail: detail };
  if (type === 'data' && typeof window.ondata === 'function') { try { window.ondata(detail, ev); } catch (e) { console.error(String(e && e.stack || e)); } }
  var ls = __listeners[type] || [];
  for (var i = 0; i < ls.length; i++) { try { ls[i](ev); } catch (e) { console.error(String(e && e.stack || e)); } }
  __flushCanvases();
}

// ---- canvas 2D context recorder (op codes match CanvasElement) ----
var __canvases = {};
function __ctx(id){
  if (__canvases[id]) return __canvases[id];
  var c = { __id:id, __cmds:[], __cols:[], __colIdx:{}, fillStyle:'#000', strokeStyle:'#000', lineWidth:1, lineCap:'butt', lineJoin:'miter', globalAlpha:1 };
  function col(s){ s = String(s); var i = c.__colIdx[s]; if (i === undefined) { i = c.__cols.length; c.__cols.push(s); c.__colIdx[s] = i; } return i; }
  c.beginPath = function(){ c.__cmds.push(0); };
  c.moveTo = function(x,y){ c.__cmds.push(1,x,y); };
  c.lineTo = function(x,y){ c.__cmds.push(2,x,y); };
  c.quadraticCurveTo = function(cx,cy,x,y){ c.__cmds.push(3,cx,cy,x,y); };
  c.bezierCurveTo = function(a,b,d,e,x,y){ c.__cmds.push(4,a,b,d,e,x,y); };
  c.arc = function(x,y,r,a0,a1,ccw){ c.__cmds.push(5,x,y,r,a0,a1,ccw?1:0); };
  c.arcTo = function(x1,y1,x2,y2,r){ c.__cmds.push(10,x1,y1,x2,y2,r); };
  c.rect = function(x,y,w,h){ c.__cmds.push(11,x,y,w,h); };
  c.closePath = function(){ c.__cmds.push(6); };
  c.fill = function(){ c.__cmds.push(7, col(c.fillStyle), c.globalAlpha); };
  c.stroke = function(){ c.__cmds.push(8, col(c.strokeStyle), c.globalAlpha, c.lineWidth, c.lineCap==='round'?2:c.lineCap==='square'?1:0, c.lineJoin==='round'?2:c.lineJoin==='bevel'?1:0); };
  c.fillRect = function(x,y,w,h){ c.__cmds.push(9,x,y,w,h, col(c.fillStyle), c.globalAlpha); };
  c.clearRect = function(){ c.__cmds.length = 0; c.__cols.length = 0; c.__colIdx = {}; };
  c.save = function(){}; c.restore = function(){};
  c.__flush = function(){ __canvasFrame(id, c.__cmds, c.__cols, c.__cmds.length); c.__cmds = []; c.__cols = []; c.__colIdx = {}; };
  __canvases[id] = c;
  return c;
}
function __flushCanvases(){ for (var k in __canvases) { var c = __canvases[k]; if (c.__cmds.length) c.__flush(); } }

// ---- elements ----
function __el(id){
  var el = {
    id: id,
    get style(){ return new Proxy({}, { set: function(o, p, v){ __setStyle(id, String(p), String(v)); return true; }, get: function(){ return ''; } }); },
    set textContent(v){ __setText(id, String(v)); }, get textContent(){ return ''; },
    set innerText(v){ __setText(id, String(v)); },
    set innerHTML(v){ __setHtml(id, String(v)); }, get innerHTML(){ return ''; },
    set className(v){ __setClass(id, String(v)); },
    classList: { add: function(){ }, remove: function(){ } },
    get clientWidth(){ return __size(id)[0]; }, get clientHeight(){ return __size(id)[1]; },
    get offsetWidth(){ return __size(id)[0]; }, get offsetHeight(){ return __size(id)[1]; },
    get width(){ return Number(__getAttr(id,'width')) || 0; }, set width(v){ __setAttr(id,'width',String(v)); },
    get height(){ return Number(__getAttr(id,'height')) || 0; }, set height(v){ __setAttr(id,'height',String(v)); },
    get dataset(){ return new Proxy({}, { get: function(o, p){ return __getAttr(id, 'data-' + String(p).replace(/[A-Z]/g, function(m){ return '-' + m.toLowerCase(); })); } }); },
    getAttribute: function(n){ return __getAttr(id, n); },
    setAttribute: function(n, v){ __setAttr(id, n, String(v)); },
    getContext: function(){ return __ctx(id); },
    addEventListener: function(){ }
  };
  return el;
}
var document = {
  getElementById: function(id){ return __has(id) ? __el(id) : null; },
  querySelectorAll: function(sel){ return __query(sel).map(__el); },
  querySelector: function(sel){ var r = __query(sel); return r.length ? __el(r[0]) : null; },
  addEventListener: addEventListener,
  body: null
};
