// 界面接线（app.js）：在假的页面里载入编辑器的全部脚本，按按钮、一帧帧往前走，看播放按钮、循环和画布尺寸。
// 页面元素都是桩：没写到的属性和方法照样能调用，界面别处改了不影响这里。
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const dir = path.join(__dirname, '..');
const SCRIPTS = ['i18n.js', 'model.js', 'audio.js', 'view.js', 'trackfx.js', 'track.js', 'about.js', 'app.js'];
const SIZES = { chart: [1600, 900], minimap: [268, 46], track: [300, 200] };

function context2d() {
  return new Proxy({}, {
    get(target, key) {
      if (key in target) return target[key];
      if (key === 'measureText') return () => ({ width: 40 });
      if (key === 'createLinearGradient' || key === 'createRadialGradient') return () => ({ addColorStop() {} });
      if (key === 'createPattern') return () => ({});
      return () => {};
    },
    set(target, key, value) { target[key] = value; return true; },
  });
}

/** 一个页面元素的桩；page 记下读了几次布局、改了几次文字。 */
function element(page, id = '', tag = 'div') {
  const listeners = {}, classes = new Set();
  let g = null;
  const base = {
    id, tagName: tag.toUpperCase(), style: {}, dataset: {}, hidden: false, textContent: '', innerHTML: '', value: '', checked: false, title: '',
    width: 0, height: 0, src: '', paused: true, readyState: 0, currentTime: 0, duration: NaN, children: [],
    classList: {
      add: (...c) => c.forEach(x => classes.add(x)), remove: (...c) => c.forEach(x => classes.delete(x)), contains: c => classes.has(c),
      toggle(c, on) { if (on === undefined ? !classes.has(c) : on) classes.add(c); else classes.delete(c); return classes.has(c); },
    },
    addEventListener(type, fn) { (listeners[type] = listeners[type] || []).push(fn); },
    removeEventListener(type, fn) { listeners[type] = (listeners[type] || []).filter(f => f !== fn); },
    fire(type, extra) {
      const ev = Object.assign({ type, target: proxy, currentTarget: proxy, preventDefault() {}, stopPropagation() {} }, extra);
      [...(listeners[type] || [])].forEach(fn => fn(ev));
    },
    getBoundingClientRect() {
      page.layoutReads++;
      const [width, height] = SIZES[id] || [100, 30];
      return { left: 0, top: 0, width, height, right: width, bottom: height };
    },
    getContext: () => g || (g = context2d()),
    appendChild(child) { base.children.push(child); return child; },
    querySelector: () => element(page), querySelectorAll: () => [], closest: () => null,
    play: () => Promise.resolve(),
  };
  const proxy = new Proxy(base, {
    get(target, key) {
      if (key in target) return target[key];
      if (typeof key === 'symbol') return undefined;
      return () => element(page); // 其他方法（focus、blur、remove、setAttribute……）都当作成功
    },
    set(target, key, value) {
      if (key === 'textContent' && id) page.textWrites[id] = (page.textWrites[id] || 0) + 1;
      target[key] = value;
      return true;
    },
  });
  return proxy;
}

/** 打开编辑器（带着样例关卡），返回页面、播放器和「走一帧」。 */
function openEditor() {
  const page = { byId: {}, media: [], layoutReads: 0, textWrites: {}, now: 1000, raf: null, observers: [], windowListeners: {} };
  const $ = id => page.byId[id] || (page.byId[id] = element(page, id));
  const sandbox = {
    console, atob, Option: function (text, value) { this.text = text; this.value = value; },
    document: {
      getElementById: $, querySelectorAll: () => [], querySelector: () => element(page), createElement: tag => element(page, '', tag),
      documentElement: element(page), head: element(page), body: element(page), activeElement: null, addEventListener() {},
    },
    navigator: { language: 'en', clipboard: { writeText: () => Promise.resolve() } },
    location: { protocol: 'file:', pathname: '/editor/index.html', href: 'file:///editor/index.html' },
    localStorage: { getItem: () => null, setItem() {}, removeItem() {} },
    matchMedia(query) {
      const listeners = [];
      const media = { media: query, matches: false, listeners, addEventListener: (type, fn) => listeners.push(fn), removeEventListener() {} };
      page.media.push(media);
      return media;
    },
    addEventListener: (type, fn) => { (page.windowListeners[type] = page.windowListeners[type] || []).push(fn); },
    removeEventListener() {},
    requestAnimationFrame: fn => { page.raf = fn; return 1; },
    setTimeout: () => 0, clearTimeout() {}, // 提示条到时间消失、草稿延时写入：这里都不需要
    performance: { now: () => page.now },
    devicePixelRatio: 1,
    URL: { createObjectURL: () => 'blob:x', revokeObjectURL() {} },
    ResizeObserver: class { constructor(fn) { this.fn = fn; page.observers.push(this); } observe() {} disconnect() {} },
    EditorDropdown: { enhance() {}, syncAll() {} },
    RHYTHMHINT_SESSION: JSON.parse(fs.readFileSync(path.join(__dirname, 'fixtures', 'session.json'), 'utf8')),
  };
  sandbox.window = sandbox;
  vm.createContext(sandbox);
  let player = null;
  for (const name of SCRIPTS) {
    vm.runInContext(fs.readFileSync(path.join(dir, name), 'utf8'), sandbox, { filename: name });
    if (name === 'audio.js') { // 记下界面建的播放器
      const Player = sandbox.EditorPlayer;
      sandbox.EditorPlayer = Object.assign(class extends Player { constructor(el) { super(el); player = this; } }, Player);
    }
  }
  const frame = (seconds = 1 / 60) => { page.now += seconds * 1000; page.raf(page.now); };
  return { page, $, player, sandbox, frame };
}

const showsPlay = $ => $('playBtn').innerHTML.includes('#i-play');

test('the play button turns back when the song ends by itself', () => {
  const { $, player, frame } = openEditor();
  frame();
  $('playBtn').fire('click');
  frame();
  assert.equal(showsPlay($), false);
  $('audio').fire('ended'); // <audio> 放到了歌曲末尾
  assert.equal(player.playing, false);
  frame();
  assert.equal(showsPlay($), true);
});

test('setting the loop start and end on the same spot does not turn the loop on, and playback keeps going', () => {
  const { $, page, player, frame } = openEditor();
  frame();
  const toasts = $('toasts').children.length;
  $('loopA').fire('click');
  $('loopB').fire('click');
  assert.equal($('loopBtn').classList.contains('on'), false);
  assert.equal($('toasts').children.length, toasts + 1); // 提示区间太短
  const start = player.time();
  $('playBtn').fire('click');
  for (let f = 0; f < 30; f++) frame();
  assert.ok(player.time() > start + 0.4, player.time() + ' vs ' + start);
  assert.ok(page.now > 0);
});

test('a frame reads no layout and rewrites the time only when it changes', () => {
  const { page, frame } = openEditor();
  frame();
  page.layoutReads = 0;
  page.textWrites = {};
  for (let f = 0; f < 5; f++) frame(); // 暂停着：时间不变
  assert.equal(page.layoutReads, 0);
  assert.equal(page.textWrites.timeNow || 0, 0);
  assert.equal(page.textWrites.timeEnd || 0, 0);
});

test('moving to a screen with another pixel ratio re-sizes the canvases', () => {
  const { $, page, sandbox, frame } = openEditor();
  frame();
  assert.equal($('chart').width, 1600);
  const watch = page.media.filter(m => /dppx/.test(m.media)).pop();
  assert.ok(watch, 'nobody watches the pixel ratio');
  sandbox.devicePixelRatio = 2;
  watch.listeners.forEach(fn => fn({ matches: false }));
  frame();
  assert.equal($('chart').width, 3200);
  assert.equal($('minimap').width, 536);
  assert.ok(page.media.filter(m => /dppx/.test(m.media)).pop().media.includes('2dppx')); // 接着盯新的像素比
});
