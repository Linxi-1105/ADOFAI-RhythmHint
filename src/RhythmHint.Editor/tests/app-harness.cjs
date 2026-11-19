// 编辑器界面层测试用的假页面：在 vm 里跑 app.js 原文（不开浏览器）。
// 页面照 index.html 建（dom.cjs），按钮、面板、innerHTML 生成的元素都能查、能点；model.js、i18n.js、about.js、view.js 照原样载入。
// 播放器换成假的（FakePlayer：接口和 audio.js 的 EditorPlayer 一一对上，app-harness.test 里核对），轨道预览换成假的。
// app.js 末尾临时挂出几个内部状态（window.__t），只在这里拼，仓库里的 app.js 不带。
// 沿用 2026-11-19 评审的复现脚本（scratchpad/review-logic/harness.cjs）。
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const { createPage, ED } = require('./dom.cjs');

const LOOP_MIN = 0.05; // 同 audio.js 的 LOOP_MIN（app-harness.test 核对）

/** 假的整首解码结果：只有界面会读的几个属性。 */
const fakeBuffer = (duration = 30) => ({ duration, sampleRate: 48000, length: duration * 48000, numberOfChannels: 2, getChannelData: () => new Float32Array(0) });

/**
 * 假播放器：时间按 performance.now() 走（播放中）、循环照真的绕回；载入音乐、整首解码由测试控制（control）。
 * 不发声音，打拍声、声源相关的方法什么都不做。
 */
function fakePlayerClass(control) {
  class FakePlayer {
    constructor(el) {
      this.el = el; this.hasAudio = false; this.cancelLoad = null; this.baseOffset = 0; this.calibration = 0; this.userOffsetMs = 0;
      this.rate = 1; this.playing = false; this.clock = 0; this.clockAt = 0; this.preroll = false; this.lastAudio = -1; this.lastAudioAt = 0; this.mediaStart = null;
      this.ctx = null; this.kick = null; this.kickSource = ''; this.pending = new Set(); this.scheduledUntil = null; this.clockOffset = null;
      this.volume = 0.8; this.buffer = null; this.source = null; this.gain = null; this.anchor = null; this.onSeekFail = null;
      control.player = this;
    }
    get offset() { return this.baseOffset + this.userOffsetMs / 1000 + this.calibration; }
    get end() { return this.buffer ? this.buffer.duration - this.offset : 0; }
    /** 载入音乐：空地址马上给 false；别的记进 control.loads，control.holdLoads 时等测试调 finish()，否则下一轮就读好。 */
    load(url) {
      this.pause();
      if (this.cancelLoad) this.cancelLoad();
      this.buffer = null;
      this.hasAudio = false;
      if (!url) return Promise.resolve(false);
      return new Promise(resolve => {
        const entry = { url, done: false };
        entry.finish = () => { if (entry.done) return; entry.done = true; this.cancelLoad = null; this.hasAudio = true; this.el.src = url; resolve(true); };
        entry.fail = message => { if (entry.done) return; entry.done = true; this.cancelLoad = null; resolve(Promise.reject(new Error(message))); };
        this.cancelLoad = () => { if (entry.done) return; entry.done = true; this.cancelLoad = null; resolve(false); };
        control.loads.push(entry);
        if (!control.holdLoads) setImmediate(entry.finish);
      });
    }
    clockTime(now = performance.now()) { return this.clock + (now - this.clockAt) / 1000 * this.rate; }
    time() { return this.playing ? this.clockTime() : this.clock; }
    seek(t) { this.clock = t; this.clockAt = performance.now(); }
    wrapLoop(t, a, b) {
      if (!this.playing || a === null || b === null || !(b - a >= LOOP_MIN) || t < b) return t;
      this.seek(a);
      return a;
    }
    play() { if (this.playing) return; this.playing = true; this.clockAt = performance.now(); }
    canSeekTo() { return true; }
    startAudio() {}
    pause() { if (!this.playing) return; this.clock = this.clockTime(); this.playing = false; }
    setRate(practiceSpeed, pitch = 1) { const t = this.time(); this.rate = Math.min(16, Math.max(1 / 16, practiceSpeed * pitch)); this.clock = t; this.clockAt = performance.now(); }
    setVolume(v) { this.volume = v; }
    /** 换成整首解码好的音乐（记进 control.used）；和真的一样会取消还在载入的音乐。 */
    useBuffer(buffer) {
      const t = this.time();
      if (this.cancelLoad) this.cancelLoad();
      this.buffer = buffer;
      this.hasAudio = true;
      this.clock = t;
      this.clockAt = performance.now();
      control.used.push(buffer);
    }
    audibleTime() { return 0; }
    bufferPosition() { return this.time() + this.offset; }
    startSource() {}
    stopSource() {}
    ensureContext() {}
    setHitSound(base64) { this.kick = null; this.kickSource = base64 || ''; return Promise.resolve(false); }
    cancelClicks() {}
    scheduleClicks() {}
    renderedBeat(musicTime) { return musicTime; }
    track() {}
    click() {}
  }
  FakePlayer.LOOP_MIN = LOOP_MIN;
  FakePlayer.bytesFromBase64 = text => new Uint8Array(Buffer.from(text, 'base64'));
  /** 整首解码：记进 control.decodes；control.holdDecodes 时等测试调 resolve(buffer)，否则马上给一个假的解码结果。 */
  FakePlayer.decode = bytes => {
    const job = { bytes: bytes.length };
    const done = new Promise((resolve, reject) => { job.resolve = resolve; job.reject = reject; });
    control.decodes.push(job);
    if (!control.holdDecodes) job.resolve(fakeBuffer());
    return done;
  };
  FakePlayer.waveform = (buffer, perSecond) => ({ peaks: new Uint8Array(0), perSecond, done: 0, step: () => true });
  return FakePlayer;
}
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

/** 选的或拖进来的文件（File 的替身）。 */
const fakeFile = (name, text, type = '') => ({ name, type, size: text.length, text: async () => text, arrayBuffer: async () => new Uint8Array(Buffer.from(text)).buffer });

/**
 * 开一个假页面。storage：localStorage 的内容（Map，几次 boot 共用就是同一个浏览器）；
 * sessions：recent/ 里各关 { 数据, 载入要多久 }；audioScripts：Mod 另存的歌曲脚本 { 'recent/x.js': { file, text, delay } }；
 * picker：showDirectoryPicker 依次返回的文件夹（没有就不提供这个接口）；quota：localStorage 最多存多少个字符（超了 setItem 抛 QuotaExceededError）；
 * holdLoads、holdDecodes：载入音乐、整首解码要等测试放行（h.audio.loads[i].finish()、h.audio.decodes[i].resolve(buffer)）。
 */
function boot({ session, storage = new Map(), recent, sessions = {}, audioScripts = {}, picks, idb, quota = Infinity, holdLoads = false, holdDecodes = false, prefs } = {}) {
  const { page, document, Option, $ } = createPage();
  if (prefs) storage.set('rhythmhint.editor.prefs', JSON.stringify(prefs));
  const winListeners = {};
  const rafs = [];
  const toasts = [];
  // 提示条：记下文字和按钮，方便点按钮（元素照样放进页面）
  const toastBox = $('toasts'), append = toastBox.appendChild.bind(toastBox);
  toastBox.appendChild = el => {
    const text = (el.children.find(c => c.tagName === 'SPAN') || {}).textContent;
    toasts.push({ text, kind: el.className, buttons: el.children.filter(c => c.tagName === 'BUTTON'), el });
    return append(el);
  };
  const used = () => [...storage.values()].reduce((s, v) => s + v.length, 0);
  let limit = quota;
  const storageEvents = [];
  const localStorage = {
    getItem: k => (storage.has(k) ? storage.get(k) : null),
    setItem: (k, v) => {
      const s = String(v);
      if (used() - (storage.get(k) || '').length + s.length > limit) throw named('QuotaExceededError', 'quota');
      storage.set(k, s);
    },
    removeItem: k => { storage.delete(k); storageEvents.push(k); },
    key: i => [...storage.keys()][i] ?? null,
    get length() { return storage.size; },
  };
  // 提示条、草稿计时器等不拖住测试进程：计时器都 unref
  const timer = (...a) => { const t = setTimeout(...a); if (t.unref) t.unref(); return t; };
  const pickQueue = picks ? picks.slice() : null;
  const pickerCalls = [];
  const control = { loads: [], decodes: [], used: [], holdLoads, holdDecodes, player: null };
  let blobs = 0;
  const ctx = {
    console, setTimeout: timer, clearTimeout, setInterval, clearInterval, setImmediate, Promise, Map, Set, JSON, Math, Date, Number, String, Array, Object, Error, Blob, Uint8Array, Float32Array, Buffer,
    URL: { createObjectURL: () => 'blob:fake-' + ++blobs, revokeObjectURL() {} },
    performance, document, localStorage, navigator: { language: 'zh-CN', clipboard: { writeText: () => Promise.resolve() } },
    location: { pathname: '/D:/Game/Mods/RhythmHint/Editor/index.html', protocol: 'file:', href: 'file:///D:/x/index.html' },
    matchMedia: () => ({ matches: false }), devicePixelRatio: 1,
    requestAnimationFrame: f => rafs.push(f),
    addEventListener: (t, f) => (winListeners[t] ||= []).push(f), removeEventListener() {},
    confirm: () => true, Option,
    TrackView: FakeTrack, EditorDropdown: { enhance() {}, syncAll() {}, close() {} },
    RHYTHMHINT_SESSION: session, RHYTHMHINT_RECENT: recent,
    indexedDB: idb,
    showDirectoryPicker: pickQueue ? async options => {
      pickerCalls.push(options);
      const next = pickQueue.shift();
      if (!next) throw named('AbortError');
      return next;
    } : undefined,
  };
  ctx.EditorPlayer = fakePlayerClass(control);
  vm.createContext(ctx);
  ctx.window = ctx;
  ctx.globalThis = ctx;
  // <script> 载入：recent/<id>.js 交给 sessions 表，歌曲脚本交给 audioScripts，按 delay 异步「执行」
  page.onScript = el => {
    const src = String(el.src).replace(/\?.*$/, '');
    const song = audioScripts[src];
    if (song) {
      timer(() => { (ctx.RHYTHMHINT_AUDIO ||= {})[song.file] = song.text; if (el.onload) el.onload(); }, song.delay || 0);
      return;
    }
    const m = /^recent\/([^?]+)\.js$/.exec(src);
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
    const e = { key: k, code: '', target: document.body, ctrlKey: false, metaKey: false, shiftKey: false, altKey: false, repeat: false, prevented: false, ...extra };
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
  /** 左侧栏的按钮（data-panel）。 */
  const railButton = name => document.querySelector('#rail button[data-panel="' + name + '"]');
  /** 在「历史」里点第 index 关：面板没开先点左侧栏打开，再点列表里真的那个按钮。 */
  const clickHistory = index => {
    if (!railButton('history').classList.contains('on')) railButton('history').click();
    const button = $('historyList').querySelector('button[data-index="' + index + '"]');
    if (!button) throw new Error('no history item ' + index);
    button.click();
  };
  /** 打开一份手序文件（文件选择框选好了）。 */
  const openFile = (name, text) => $('openInput').dispatch('change', { target: { files: [fakeFile(name, text)], value: '' } });
  return {
    ctx, t, el: $, $, document, page, frames, key, keyUp, fire, wait, until, select, clickHistory, railButton, openFile,
    storage, storageEvents, used, setQuota: n => { limit = n; }, toasts, pickerCalls, audio: control,
  };
}

// ---------- 几个测试都用的小工具 ----------
const DRAFT = 'rhythmhint.editor.draft.';
/** 样例会话（C# 测试生成的 fixtures/session.json），每次一份新的。 */
const session = () => JSON.parse(fs.readFileSync(path.join(__dirname, 'fixtures', 'session.json'), 'utf8'));
const lanesOf = n => Array.from(n.lanes, l => l[0]); // Array.from：假页面里的数组和这里的不是同一个 Array，严格比较要换过来
const allLanes = doc => Array.from(doc.notes, lanesOf);
/** 另一关：砖块时间整体挪一下（谱面指纹就不同了）。 */
function otherLevel(id, shift) {
  const s = session();
  s.id = id;
  s.fingering.hits.forEach(h => { h[1] += shift; });
  s.fingering.song = 'Other ' + id;
  return s;
}
const recentOf = (...list) => ({
  format: 'rhythmhint-editor-recent',
  items: list.map((s, i) => ({ id: s.id, session: s.id + '.js', song: s.fingering.song, exported: 1000 - i, notes: s.fingering.notes.length })),
});
/** 选中一个单押，按下一条轨道的键。 */
function editSingle(h, n) {
  h.select(n.id);
  const labels = h.t.state.doc.keyLabels;
  h.key(labels[(lanesOf(n)[0] + 1) % labels.length].toLowerCase());
}
const T = (h, key, ...args) => h.t.state.t(key, ...args);
const hasToast = (h, key, ...args) => h.toasts.some(x => x.text === T(h, key, ...args));
const findToast = (h, key, ...args) => h.toasts.find(x => x.text === T(h, key, ...args));
/** 草稿马上写进去（关网页前的 beforeunload 会把还没写的草稿写上），不用等 600 毫秒。 */
const flush = h => h.fire('beforeunload', { preventDefault() {}, returnValue: '' });
const draftIn = (storage, id) => JSON.parse(storage.get(DRAFT + id) || 'null');

module.exports = {
  boot, fakeDir, fakeIndexedDb, fakeFile, fakeBuffer, fakePlayerClass, named, ED, LOOP_MIN,
  DRAFT, session, lanesOf, allLanes, otherLevel, recentOf, editSingle, T, hasToast, findToast, flush, draftIn,
};
