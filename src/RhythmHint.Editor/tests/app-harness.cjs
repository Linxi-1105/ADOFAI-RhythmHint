// 编辑器界面层测试用的假页面：在 vm 里跑 app.js 原文（不开浏览器）。
// model.js、i18n.js、about.js、view.js 照原样载入，播放器、轨道预览换成假的；画布的绘图调用全部吞掉。
// app.js 末尾临时挂出几个内部状态（window.__t），只在这里拼，仓库里的 app.js 不带。
// 沿用 2026-11-19 评审的复现脚本（scratchpad/review-logic/harness.cjs）。
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

const ED = path.join(__dirname, '..');

class ClassList {
  constructor() { this.set = new Set(); }
  add(...c) { c.forEach(x => this.set.add(x)); }
  remove(...c) { c.forEach(x => this.set.delete(x)); }
  toggle(c, on) { const v = on === undefined ? !this.set.has(c) : !!on; if (v) this.set.add(c); else this.set.delete(c); return v; }
  contains(c) { return this.set.has(c); }
}
const ctxStub = new Proxy({}, {
  get: (t, p) => (p in t ? t[p] : () => ({ width: 10, addColorStop() {} })),
  set: (t, p, v) => { t[p] = v; return true; },
});

class El {
  constructor(id, tag = 'div', page = null) {
    this.id = id; this.tagName = String(tag).toUpperCase(); this.listeners = {}; this.classList = new ClassList(); this.page = page;
    this.style = {}; this.dataset = {}; this.children = []; this.hidden = false; this.value = ''; this.checked = false;
    this.textContent = ''; this.innerHTML = ''; this.title = ''; this.disabled = false; this.options = []; this.selectedIndex = 0;
    this.firstChild = { textContent: '' }; this.width = 0; this.height = 0; this.src = ''; this.className = '';
  }
  addEventListener(t, f) { (this.listeners[t] ||= []).push(f); }
  removeEventListener() {}
  dispatch(t, e) { (this.listeners[t] || []).forEach(f => f(e)); }
  querySelectorAll() { return []; }
  querySelector() { return new El(); }
  appendChild(c) { this.children.push(c); if (c.tagName === 'SCRIPT' && this.page) this.page.onScript(c); return c; }
  remove() {} after() {} focus() {} load() {} removeAttribute() {} setAttribute() {} getAttribute() { return null; }
  blur() { if (this.page && this.page.document.activeElement === this) { this.page.document.activeElement = null; this.dispatch('change', { target: this }); } }
  click() { this.dispatch('click', { detail: 1, currentTarget: this, target: this }); }
  closest() { return null; } contains() { return false; }
  getBoundingClientRect() { return { left: 0, top: 0, width: 1600, height: 900, right: 1600, bottom: 900 }; }
  getContext() { return ctxStub; }
  add(o) { this.options.push(o); }
  get offsetWidth() { return 100; } get offsetHeight() { return 100; }
  get clientHeight() { return 900; } get clientWidth() { return 1600; }
}

class FakePlayer {
  constructor(el) { this.el = el; this.playing = false; this.hasAudio = false; this.baseOffset = 0; this.calibration = 0; this.userOffsetMs = 0; this.clock = 0; this.ctx = null; }
  get offset() { return this.baseOffset + this.userOffsetMs / 1000 + this.calibration; }
  get end() { return 0; }
  setVolume() {} setRate() {} setHitSound() {} cancelClicks() {} scheduleClicks() {} useBuffer() {}
  seek(t) { this.clock = t; } pause() { this.playing = false; } play() { this.playing = true; } time() { return this.clock; }
  load() { return Promise.resolve(false); }
}
FakePlayer.decode = async () => ({});
FakePlayer.bytesFromBase64 = () => new Uint8Array(0);
class FakeTrack { constructor() { this.zoomTarget = 1; } setDoc() {} draw() {} resize() {} setZoom(z) { return z; } }

const named = (name, message = name) => Object.assign(new Error(message), { name });

/** 假的文件夹句柄：files 是 文件名 → 文字。gone 时一切操作报 NotFoundError（文件夹被删、被挪走）。 */
function fakeDir(name, { files = new Map(), permission = 'granted' } = {}) {
  const dir = {
    name, files, permission, gone: false, kind: 'directory',
    queryPermission: async () => dir.permission, requestPermission: async () => dir.permission,
    async getFileHandle(n, opts) {
      if (dir.gone) throw named('NotFoundError');
      if (!files.has(n) && !(opts && opts.create)) throw named('NotFoundError');
      return {
        getFile: async () => ({ text: async () => files.get(n) }),
        createWritable: async () => { let text = ''; return { write: async t => { text += t; }, close: async () => { files.set(n, text); } }; },
      };
    },
  };
  return dir;
}

/** 假的 IndexedDB（只够 app.js 存文件夹句柄用）：store 是 键 → 值。 */
function fakeIndexedDb(store = new Map()) {
  const later = f => setImmediate(f);
  return {
    store,
    open() {
      const req = {};
      later(() => {
        req.result = {
          close() {},
          createObjectStore() {},
          transaction() {
            const tx = {};
            tx.objectStore = () => ({
              get: k => ({ result: store.get(k) }),
              put: (v, k) => { store.set(k, v); return {}; },
              delete: k => { store.delete(k); return {}; },
            });
            later(() => tx.oncomplete && tx.oncomplete());
            return tx;
          },
        };
        req.onsuccess();
      });
      return req;
    },
  };
}

/**
 * 开一个假页面。storage：localStorage 的内容（Map，几次 boot 共用就是同一个浏览器）；
 * sessions：recent/ 里各关 { 数据, 载入要多久 }；picker：showDirectoryPicker 依次返回的文件夹（没有就不提供这个接口）；
 * quota：localStorage 最多存多少个字符（超了 setItem 抛 QuotaExceededError）。
 */
function boot({ session, storage = new Map(), recent, sessions = {}, picks, idb, quota = Infinity } = {}) {
  const els = new Map();
  const page = {};
  const get = id => { if (!els.has(id)) els.set(id, new El(id, id === 'chart' || id === 'track' || id === 'minimap' ? 'canvas' : 'div', page)); return els.get(id); };
  get('modal').hidden = true; // index.html 里一开始是藏着的
  const winListeners = {};
  const rafs = [];
  const toasts = [];
  const document = {
    getElementById: get, querySelectorAll: () => [], createElement: tag => new El(null, tag, page),
    documentElement: new El('html'), head: new El('head', 'head', page), body: new El('body', 'body', page), activeElement: null, title: '', addEventListener() {},
  };
  page.document = document;
  // 提示条：记下文字和按钮，方便点按钮
  get('toasts').appendChild = el => {
    const text = (el.children.find(c => c.tagName === 'SPAN') || {}).textContent;
    toasts.push({ text, kind: el.className, buttons: el.children.filter(c => c.tagName === 'BUTTON') });
    return el;
  };
  const used = () => [...storage.values()].reduce((s, v) => s + v.length, 0);
  const localStorage = {
    getItem: k => (storage.has(k) ? storage.get(k) : null),
    setItem: (k, v) => {
      const s = String(v);
      if (used() - (storage.get(k) || '').length + s.length > quota) throw named('QuotaExceededError', 'quota');
      storage.set(k, s);
    },
    removeItem: k => storage.delete(k),
    key: i => [...storage.keys()][i] ?? null,
    get length() { return storage.size; },
  };
  // 提示条、草稿计时器等不拖住测试进程：计时器都 unref
  const timer = (...a) => { const t = setTimeout(...a); if (t.unref) t.unref(); return t; };
  const pickQueue = picks ? picks.slice() : null;
  const pickerCalls = [];
  const ctx = {
    console, setTimeout: timer, clearTimeout, setInterval, clearInterval, Promise, Map, Set, JSON, Math, Date, Number, String, Array, Object, Error, URL, Blob, Uint8Array, Buffer,
    performance, document, localStorage, navigator: { language: 'zh-CN', clipboard: { writeText: () => Promise.resolve() } },
    location: { pathname: '/D:/Game/Mods/RhythmHint/Editor/index.html', protocol: 'file:', href: 'file:///D:/x/index.html' },
    matchMedia: () => ({ matches: false }), devicePixelRatio: 1,
    requestAnimationFrame: f => rafs.push(f),
    addEventListener: (t, f) => (winListeners[t] ||= []).push(f), removeEventListener() {},
    confirm: () => true, Option: class { constructor(text, value) { this.textContent = text; this.value = value; } },
    EditorPlayer: FakePlayer, TrackView: FakeTrack, EditorDropdown: { enhance() {}, syncAll() {}, close() {} }, peaksFromBuffer: () => null,
    RHYTHMHINT_SESSION: session, RHYTHMHINT_RECENT: recent,
    indexedDB: idb,
    showDirectoryPicker: pickQueue ? async options => {
      pickerCalls.push(options);
      const next = pickQueue.shift();
      if (!next) throw named('AbortError');
      return next;
    } : undefined,
  };
  vm.createContext(ctx);
  ctx.window = ctx;
  ctx.globalThis = ctx;
  // <script> 载入：recent/<id>.js 交给 sessions 表，按 delay 异步「执行」
  page.onScript = el => {
    const m = /^recent\/([^?]+)\.js\?/.exec(el.src);
    const entry = m && sessions[m[1]];
    if (!entry) { timer(() => el.onerror && el.onerror(), 0); return; }
    timer(() => { ctx.RHYTHMHINT_SESSION = entry.data; if (el.onload) el.onload(); }, entry.delay || 0);
  };
  for (const f of ['model.js', 'i18n.js', 'about.js', 'view.js']) vm.runInContext(fs.readFileSync(path.join(ED, f), 'utf8'), ctx, { filename: f });
  let app = fs.readFileSync(path.join(ED, 'app.js'), 'utf8');
  const tail = app.lastIndexOf('})();');
  app = app.slice(0, tail) + '  window.__t = { state, get metaDirty() { return metaDirty; }, capture: lane => { capturing = lane; } };\n' + app.slice(tail);
  vm.runInContext(app, ctx, { filename: 'app.js' });

  const t = ctx.__t;
  const frames = n => { for (let i = 0; i < n; i++) { const f = rafs.shift(); if (f) f(); } };
  const fire = (type, e) => (winListeners[type] || []).forEach(f => f(e));
  const keyEvent = (k, extra = {}) => {
    const e = { key: k, code: '', target: new El('body'), ctrlKey: false, metaKey: false, shiftKey: false, altKey: false, repeat: false, prevented: false, ...extra };
    e.preventDefault = () => { e.prevented = true; };
    return e;
  };
  const key = (k, extra) => { const e = keyEvent(k, extra); fire('keydown', e); return e; };
  const keyUp = (k, extra) => { const e = keyEvent(k, extra); fire('keyup', e); return e; };
  const wait = ms => new Promise(r => setTimeout(r, ms));
  /** 等到 cond() 为真（最多 1 秒）。 */
  const until = async (cond, what = 'condition') => {
    for (let i = 0; i < 200; i++) { if (cond()) return; await wait(5); }
    throw new Error('timed out waiting for ' + what);
  };
  const select = id => { t.state.selection = new Set([id]); };
  const clickHistory = index => (get('historyList').listeners.click || []).forEach(f => f({ target: { closest: () => ({ dataset: { index: String(index) } }) } }));
  return { ctx, t, el: get, frames, key, keyUp, fire, wait, until, select, clickHistory, storage, toasts, pickerCalls };
}

module.exports = { boot, fakeDir, fakeIndexedDb, named, El, ED };
