// 测试用的小页面：照 index.html 建出元素树，app.js 里的 getElementById、querySelectorAll、closest、innerHTML 都按真的来。
// 只支持编辑器用到的选择器：标签、#id、.类名、[属性]、[属性="值"]、后代（空格）、逗号；用到别的写法直接报错，不悄悄返回空。
// 没有的属性就是 undefined（不像万能桩那样处处为真）；<audio> 有媒体元素的那几个属性，测试里自己推进 currentTime、发事件。
'use strict';
const fs = require('node:fs');
const path = require('node:path');

const ED = path.join(__dirname, '..');
const VOID = new Set(['area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'source', 'track', 'wbr']);
const ENTITIES = { amp: '&', lt: '<', gt: '>', quot: '"', '#39': "'", apos: "'", nbsp: ' ' };
const decode = s => s.replace(/&(amp|lt|gt|quot|#39|apos|nbsp);/g, (m, k) => ENTITIES[k]);
const camel = s => s.replace(/-([a-z])/g, (m, c) => c.toUpperCase());

class ClassList {
  constructor() { this.set = new Set(); }
  add(...c) { c.forEach(x => this.set.add(x)); }
  remove(...c) { c.forEach(x => this.set.delete(x)); }
  toggle(c, on) { const v = on === undefined ? !this.set.has(c) : !!on; if (v) this.set.add(c); else this.set.delete(c); return v; }
  contains(c) { return this.set.has(c); }
}

// ---------- 选择器 ----------
const PART = /^(?:([a-zA-Z][\w-]*)|#([\w-]+)|\.([\w-]+)|\[([\w-]+)(?:="([^"]*)")?\])/;
function compound(text) {
  const c = { tag: null, id: null, classes: [], attrs: [] };
  let rest = text;
  while (rest) {
    const m = PART.exec(rest);
    if (!m) throw new Error('dom.cjs 不支持这个选择器：' + text);
    if (m[1]) c.tag = m[1].toUpperCase();
    else if (m[2]) c.id = m[2];
    else if (m[3]) c.classes.push(m[3]);
    else c.attrs.push([m[4], m[5]]);
    rest = rest.slice(m[0].length);
  }
  return c;
}
const parseSelector = sel => sel.split(',').map(s => s.trim().split(/\s+/).map(compound));
function matchCompound(el, c) {
  if (c.tag && el.tagName !== c.tag) return false;
  if (c.id && el.id !== c.id) return false;
  if (c.classes.some(k => !el.classList.contains(k))) return false;
  return c.attrs.every(([name, value]) => {
    const v = el.attrValue(name);
    return v !== null && (value === undefined || v === value);
  });
}
function matchChain(el, chain) {
  if (!matchCompound(el, chain[chain.length - 1])) return false;
  let i = chain.length - 2;
  for (let node = el.parentNode; node && i >= 0; node = node.parentNode) if (matchCompound(node, chain[i])) i--;
  return i < 0;
}
const matches = (el, sel) => parseSelector(sel).some(chain => matchChain(el, chain));

// ---------- 元素 ----------
class Element {
  constructor(page, tag, attrs = []) {
    this.page = page;
    this.tagName = String(tag).toUpperCase();
    this.attributes = new Map();
    this.children = [];
    this.parentNode = null;
    this.listeners = {};
    this.classList = new ClassList();
    this.style = {};
    this.dataset = {};
    this.id = '';
    this.hidden = false;
    this.disabled = false;
    this.title = '';
    this.text = '';
    this.html = '';
    if (['INPUT', 'TEXTAREA', 'SELECT', 'OPTION'].includes(this.tagName)) { this.value = ''; this.checked = false; }
    if (this.tagName === 'SELECT') { this.options = []; this.selectedIndex = 0; }
    if (this.tagName === 'SCRIPT') this.src = '';
    attrs.forEach(([k, v]) => this.setAttribute(k, v));
  }

  get className() { return [...this.classList.set].join(' '); }
  set className(v) { this.classList.set = new Set(String(v).split(/\s+/).filter(Boolean)); }

  setAttribute(name, value) {
    const v = String(value);
    this.attributes.set(name, v);
    if (name === 'id') this.id = v;
    else if (name === 'class') this.className = v;
    else if (name.startsWith('data-')) this.dataset[camel(name.slice(5))] = v;
    else if (name === 'hidden') this.hidden = true;
    else if (name === 'disabled') this.disabled = true;
    else if (name === 'checked') this.checked = true;
    else if (name === 'value') this.value = v;
    else if (name === 'title') this.title = v;
    else if (name === 'src') this.src = v;
  }
  getAttribute(name) { return this.attributes.has(name) ? this.attributes.get(name) : null; }
  removeAttribute(name) {
    this.attributes.delete(name);
    if (name === 'src') this.src = '';
    if (name === 'hidden') this.hidden = false;
  }
  /** 选择器比较用的属性值：id、class、data-* 跟着属性（程序会直接改 dataset、className）。 */
  attrValue(name) {
    if (name === 'id') return this.id || null;
    if (name === 'class') return this.className || null;
    if (name.startsWith('data-')) { const v = this.dataset[camel(name.slice(5))]; return v === undefined ? null : String(v); }
    return this.getAttribute(name);
  }

  get textContent() { return this.text + this.children.map(c => c.textContent).join(''); }
  set textContent(v) { this.detachChildren(); this.text = String(v); this.html = this.text; this.page.textWrites[this.id] = (this.page.textWrites[this.id] || 0) + 1; }
  get innerHTML() { return this.html; }
  set innerHTML(v) { this.detachChildren(); this.text = ''; this.html = String(v); parseInto(this.page, this, this.html); }
  detachChildren() { this.children.forEach(c => { c.parentNode = null; }); this.children = []; }

  appendChild(child) {
    if (child.parentNode) child.remove();
    child.parentNode = this;
    this.children.push(child);
    if (this.tagName === 'SELECT' && child.tagName === 'OPTION') this.options.push(child);
    if (child.tagName === 'SCRIPT' && this.page.onScript) this.page.onScript(child);
    return child;
  }
  add(option) { this.appendChild(option); }
  after(node) {
    const p = this.parentNode;
    if (!p) return;
    if (node.parentNode) node.remove();
    node.parentNode = p;
    p.children.splice(p.children.indexOf(this) + 1, 0, node);
  }
  remove() {
    const p = this.parentNode;
    if (!p) return;
    p.children.splice(p.children.indexOf(this), 1);
    this.parentNode = null;
  }
  contains(node) { for (let n = node; n; n = n.parentNode) if (n === this) return true; return false; }
  *descendants() { for (const c of this.children) { yield c; yield* c.descendants(); } }
  querySelectorAll(sel) { const chains = parseSelector(sel); return [...this.descendants()].filter(el => chains.some(ch => matchChain(el, ch))); }
  querySelector(sel) { return this.querySelectorAll(sel)[0] || null; }
  closest(sel) { for (let n = this; n; n = n.parentNode) if (n.tagName && matches(n, sel)) return n; return null; }

  addEventListener(type, fn, opts) { (this.listeners[type] ||= []).push({ fn, once: !!(opts && opts.once) }); }
  removeEventListener(type, fn) { this.listeners[type] = (this.listeners[type] || []).filter(l => l.fn !== fn); }
  /** 发一个事件，从这个元素往上冒泡（change、input、click 都冒泡）。init 里的字段并进事件对象。 */
  dispatch(type, init = {}) {
    let stopped = false;
    const ev = Object.assign({ type, target: this, defaultPrevented: false }, init);
    ev.preventDefault = () => { ev.defaultPrevented = true; };
    ev.stopPropagation = () => { stopped = true; };
    for (let node = this; node && !stopped; node = node.parentNode) {
      ev.currentTarget = node;
      const list = node.listeners[type] || [];
      node.listeners[type] = list.filter(l => !l.once);
      list.forEach(l => l.fn(ev));
    }
    return ev;
  }
  fire(type, init) { return this.dispatch(type, init); }
  click() { if (!this.disabled) this.dispatch('click', { detail: 1 }); }
  focus() { this.page.document.activeElement = this; }
  blur() {
    if (this.page.document.activeElement !== this) return;
    this.page.document.activeElement = null;
    if (this.tagName === 'INPUT' || this.tagName === 'TEXTAREA') this.dispatch('change');
  }

  getBoundingClientRect() {
    this.page.layoutReads++;
    const [width, height] = this.page.size(this);
    return { left: 0, top: 0, width, height, right: width, bottom: height, x: 0, y: 0 };
  }
  get offsetWidth() { return this.page.size(this)[0]; }
  get offsetHeight() { return this.page.size(this)[1]; }
  get clientWidth() { return this.page.size(this)[0]; }
  get clientHeight() { return this.page.size(this)[1]; }
}

/** 画布：绘图调用全部吞掉（这里不看画了什么），量字给个宽度。 */
class Canvas extends Element {
  constructor(page, tag, attrs) { super(page, tag, attrs); this.width = 300; this.height = 150; this.g = null; }
  getContext() { return this.g || (this.g = context2d()); }
}
function context2d() {
  return new Proxy({}, {
    get(target, key) {
      if (key in target) return target[key];
      if (key === 'measureText') return () => ({ width: 40 });
      if (key === 'createLinearGradient' || key === 'createRadialGradient') return () => ({ addColorStop() {} });
      if (typeof key === 'symbol') return undefined;
      return () => {}; // 其余绘图调用（fillRect、arc、roundRect……）
    },
    set(target, key, value) { target[key] = value; return true; },
  });
}

/** <audio>：浏览器媒体元素的那几个属性。读取元数据、播放到哪里由测试推进（ready、currentTime、fire('ended')）。 */
class Media extends Element {
  constructor(page, tag, attrs) {
    super(page, tag, attrs);
    Object.assign(this, { src: '', currentTime: 0, duration: NaN, paused: true, seeking: false, readyState: 0, error: null,
      playbackRate: 1, volume: 1, preservesPitch: true, seekable: ranges([]), plays: 0, loads: 0 });
  }
  load() { this.loads++; this.paused = true; this.readyState = 0; this.currentTime = 0; this.duration = NaN; this.seekable = ranges([]); }
  play() { this.plays++; this.paused = false; return Promise.resolve(); }
  pause() { this.paused = true; }
  /** 读到了元数据（可以播放）：时长 duration 秒，整首都能跳。 */
  ready(duration) {
    this.readyState = 4;
    this.duration = duration;
    this.seekable = ranges([[0, duration]]);
    this.dispatch('loadedmetadata');
  }
}
const ranges = list => ({ length: list.length, start: i => list[i][0], end: i => list[i][1] });

function make(page, tag, attrs) {
  const t = String(tag).toLowerCase();
  if (t === 'canvas') return new Canvas(page, tag, attrs);
  if (t === 'audio' || t === 'video') return new Media(page, tag, attrs);
  return new Element(page, tag, attrs);
}

// ---------- HTML ----------
const TOKEN = /<!--[\s\S]*?-->|<!doctype[^>]*>|<\/([a-zA-Z][\w:-]*)\s*>|<([a-zA-Z][\w:-]*)((?:\s+[^\s=>/]+(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+))?)*)\s*(\/?)>|([^<]+)/gi;
const ATTR = /([^\s=>/]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+)))?/g;
function parseInto(page, parent, html) {
  const stack = [parent];
  for (const m of html.matchAll(TOKEN)) {
    const top = stack[stack.length - 1];
    if (m[1]) {
      const name = m[1].toUpperCase();
      for (let i = stack.length - 1; i > 0; i--) if (stack[i].tagName === name) { stack.length = i; break; }
    } else if (m[2]) {
      const attrs = [...(m[3] || '').matchAll(ATTR)].map(a => [a[1], decode(a[2] ?? a[3] ?? a[4] ?? '')]);
      const el = make(page, m[2], attrs);
      el.parentNode = top;
      top.children.push(el);
      if (top.tagName === 'SELECT' && el.tagName === 'OPTION') top.options.push(el);
      if (!VOID.has(m[2].toLowerCase()) && !m[4]) stack.push(el);
    } else if (m[5] && m[5].trim()) top.text += decode(m[5]);
  }
}

/**
 * 照 index.html 建一个页面。size(el) 给出元素的 [宽, 高]（画布尺寸、布局）；onScript(el) 在插入 <script> 时调用（模拟载入）。
 * 返回 { document, page, $ }：$ 是 getElementById，找不到的 id 返回 null（app.js 用了页面上没有的 id 会直接出错）。
 */
function createPage({ size = () => [1600, 900] } = {}) {
  const page = { size, layoutReads: 0, textWrites: {}, onScript: null, document: null };
  const root = new Element(page, '#root');
  parseInto(page, root, fs.readFileSync(path.join(ED, 'index.html'), 'utf8'));
  const html = root.children.find(c => c.tagName === 'HTML');
  const head = html.children.find(c => c.tagName === 'HEAD'), body = html.children.find(c => c.tagName === 'BODY');
  const docListeners = {};
  const document = {
    documentElement: html, head, body, activeElement: null, title: '',
    getElementById: id => [...html.descendants()].find(el => el.id === id) || null,
    querySelectorAll: sel => html.querySelectorAll(sel),
    querySelector: sel => html.querySelector(sel),
    createElement: tag => make(page, tag),
    addEventListener: (type, fn) => (docListeners[type] ||= []).push(fn),
    removeEventListener() {},
    listeners: docListeners,
  };
  page.document = document;
  /** new Option(文字, 值)：和浏览器一样是一个 <option>。 */
  function Option(text, value) {
    const o = make(page, 'option');
    o.text = String(text);
    o.value = value === undefined ? String(text) : String(value);
    return o;
  }
  return { page, document, Option, $: document.getElementById };
}

module.exports = { createPage, Element, Media, matches, ED };
