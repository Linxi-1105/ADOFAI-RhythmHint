// 界面接线（app-*.js、app.js）和真的播放器（audio.js）：在照 index.html 建的假页面（dom.cjs）里照 index.html 的顺序载入编辑器的脚本，
// 按按钮、一帧帧往前走，看播放按钮、循环、画布尺寸，以及有音乐时 <audio> 后备和整首解码两条路怎么播。
// <audio> 的播放位置、AudioContext 的时间都由测试推进；没有写到的属性就是没有（不再处处为真）。
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { createPage, pageScripts } = require('./dom.cjs');

const dir = path.join(__dirname, '..');
const SCRIPTS = pageScripts().filter(f => f !== 'dropdown.js'); // 下拉菜单换成假的（sandbox.EditorDropdown）
const SIZES = { chart: [1600, 900], minimap: [268, 46], track: [300, 200] };
const near = (actual, expected, eps = 0.002) => assert.ok(Math.abs(actual - expected) < eps, actual + ' vs ' + expected);
const session = () => JSON.parse(fs.readFileSync(path.join(__dirname, 'fixtures', 'session.json'), 'utf8'));

/** 假的 Web Audio：时间由测试推进（ctx.currentTime），声源记在 sources 里。 */
function webAudio(page) {
  const sources = [];
  const node = extra => Object.assign({ connect: x => x, disconnect() {} }, extra);
  const param = () => ({ value: 1, setValueAtTime() {}, exponentialRampToValueAtTime() {} });
  class AudioContext {
    constructor() { this.currentTime = 0; this.state = 'running'; this.sampleRate = 48000; this.destination = node(); this.baseLatency = 0; this.outputLatency = 0; page.ctx = this; }
    resume() { this.state = 'running'; return Promise.resolve(); }
    getOutputTimestamp() { return { contextTime: this.currentTime, performanceTime: page.now }; }
    createGain() { return node({ gain: param() }); }
    createOscillator() { return node({ frequency: param(), start() {}, stop() {} }); }
    createBufferSource() {
      const s = node({ buffer: null, playbackRate: param(), onended: null, startedAt: null, offset: null, stopped: false,
        start(at, offset) { s.startedAt = at; s.offset = offset; }, stop() { s.stopped = true; } });
      sources.push(s);
      return s;
    }
  }
  /** 离线解码：给一首 duration 秒、两声道的假音乐（采样是一段正弦，波形不全是 0）。 */
  class OfflineAudioContext {
    decodeAudioData() {
      const sampleRate = 4800, length = Math.round(page.songSeconds * sampleRate);
      const data = Float32Array.from({ length }, (_, i) => Math.sin(i / 9) * 0.5);
      page.decodes++;
      return Promise.resolve({ duration: page.songSeconds, sampleRate, length, numberOfChannels: 2, getChannelData: () => data });
    }
  }
  return { AudioContext, OfflineAudioContext, sources };
}

/** 打开编辑器（带着样例关卡；s 可以换成带音乐的），返回页面、播放器和「走一帧」。 */
function openEditor(s = session()) {
  const { page: dom, document, Option, $ } = createPage({ size: el => SIZES[el.id] || [100, 30] });
  const page = Object.assign(dom, { media: [], now: 1000, raf: null, observers: [], windowListeners: {}, songSeconds: 20, decodes: 0, scripts: [] });
  const audio = webAudio(page);
  const sandbox = {
    console, atob, Option, document, Uint8Array, Float32Array,
    navigator: { language: 'en', clipboard: { writeText: () => Promise.resolve() } },
    location: { protocol: 'file:', pathname: '/editor/index.html', href: 'file:///editor/index.html' },
    localStorage: { getItem: () => null, setItem() {}, removeItem() {}, key: () => null, length: 0 },
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
    AudioContext: audio.AudioContext, OfflineAudioContext: audio.OfflineAudioContext,
    RHYTHMHINT_SESSION: s,
  };
  sandbox.window = sandbox;
  // Mod 另存的歌曲脚本：插进页面后（下一轮）把 base64 放进 window.RHYTHMHINT_AUDIO 再触发 onload
  page.onScript = el => {
    page.scripts.push(el.src);
    Promise.resolve().then(() => {
      const file = s.audio && s.audio.file;
      (sandbox.RHYTHMHINT_AUDIO ||= {})[file] = Buffer.from('fake song bytes').toString('base64');
      if (el.onload) el.onload();
    });
  };
  vm.createContext(sandbox);
  let player = null;
  for (const name of SCRIPTS) {
    vm.runInContext(fs.readFileSync(path.join(dir, name), 'utf8'), sandbox, { filename: name });
    if (name === 'audio.js') { // 记下界面建的播放器
      const Player = sandbox.EditorPlayer;
      sandbox.EditorPlayer = Object.assign(class extends Player { constructor(el) { super(el); player = this; } }, Player);
    }
  }
  /** 走一帧：页面时间和声卡时间一起往前走。 */
  const frame = (seconds = 1 / 60) => {
    page.now += seconds * 1000;
    if (page.ctx) page.ctx.currentTime += seconds;
    page.raf(page.now);
  };
  return { page, $, player, sandbox, frame, sources: audio.sources };
}

const showsPlay = $ => $('playBtn').innerHTML.includes('#i-play');
/** 带音乐的关卡：offset 是关卡的音乐偏移（秒），script 是 Mod 另存的歌曲脚本。 */
function withSong({ offset = 0.5, script = '' } = {}) {
  const s = session();
  s.audio = { file: 'recent/16-5cea7782a36abca967cf2ecf-song.ogg', script, offset, pitch: 1, calibration: 0, peaksPerSecond: 0, peaks: '' };
  return s;
}
const tick = () => new Promise(r => setImmediate(r));

test('with <audio> playback follows the song, and the button turns back when the song ends', () => {
  const { $, player, frame } = openEditor(withSong({ offset: 2 }));
  const el = $('audio');
  assert.match(el.src, /song\.ogg\?v=/);
  el.ready(8); // 读到了元数据
  assert.equal(player.hasAudio, true);
  frame();
  const start = player.time();
  $('playBtn').click();
  assert.equal(el.paused, false);
  near(el.currentTime, start + 2); // 音乐位置 = 画面时间 + 偏移
  assert.equal(showsPlay($), false);
  for (let f = 0; f < 30; f++) { el.currentTime += 1 / 60; frame(); }
  near(player.time(), start + 0.5, 0.02);
  el.currentTime = el.duration; // 放到了歌曲末尾
  el.paused = true;
  el.fire('ended');
  assert.equal(player.playing, false);
  frame();
  assert.equal(showsPlay($), true);
});

test('right after a seek the picture holds the target even if <audio> still reads 0', () => {
  // 2026-11-19：第一次播放时画面跳到 −(关卡 offset − 一拍)，即音乐文件第 0 秒。<audio> 跳转刚发出、还没准备好时 currentTime 可能读成 0
  const { $, player, frame } = openEditor(withSong({ offset: 6.9 }));
  const el = $('audio');
  el.ready(30);
  let stored = 0;
  Object.defineProperty(el, 'currentTime', { get: () => (el.seeking ? 0 : stored), set: v => { stored = v; el.seeking = true; el.readyState = 1; } });
  frame();
  const start = player.time();
  $('playBtn').click();
  frame();
  near(player.time(), start);
  near(Number.parseFloat($('timeNow').textContent.split(':')[1]), start % 60, 0.002);
  el.seeking = false; // 跳好了，开始走
  el.readyState = 4;
  stored += 0.1;
  frame();
  near(player.time(), start + 0.1, 0.02);
});

test('the song script from the Mod is decoded and played from the samples', async () => {
  const { $, page, player, frame, sources, sandbox } = openEditor(withSong({ offset: 0.5, script: 'recent/16-5cea7782a36abca967cf2ecf-song.js' }));
  assert.equal(page.scripts.length, 1);
  await tick();
  await tick();
  assert.equal(page.decodes, 1);
  assert.ok(player.buffer, 'uses the decoded song');
  assert.equal(sandbox.RHYTHMHINT_AUDIO['recent/16-5cea7782a36abca967cf2ecf-song.ogg'], undefined, 'the base64 text is let go');
  assert.equal($('audio').src, '', '<audio> is no longer used');
  frame();
  const start = player.time();
  $('playBtn').click();
  assert.equal(sources.length, 1);
  near(sources[0].offset, start + 0.5); // 从画面所在的位置放（音乐位置 = 画面时间 + 偏移）
  for (let f = 0; f < 60; f++) frame();
  near(player.time(), start + 1 - 0.02, 0.01); // 提前 20 毫秒排好的声源，1 秒后放到起点后 0.98 秒
  sources[0].onended(); // 放到了歌曲末尾
  frame();
  assert.equal(showsPlay($), true);
});

test('setting the loop start and end on the same spot does not turn the loop on, and playback keeps going', () => {
  const { $, page, player, frame } = openEditor();
  frame();
  const toasts = $('toasts').children.length;
  $('loopA').click();
  $('loopB').click();
  assert.equal($('loopBtn').classList.contains('on'), false);
  assert.equal($('toasts').children.length, toasts + 1); // 提示区间太短
  const start = player.time();
  $('playBtn').click();
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
