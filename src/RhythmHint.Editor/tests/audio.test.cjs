// 音频排程的时间单位是秒。这里的媒体和声卡时钟可各自指定，便于复现固定的输出延迟。
const { test } = require('node:test');
const assert = require('node:assert/strict');
require('../audio.js');
const near = (actual, expected) => assert.ok(Math.abs(actual - expected) < 0.002, actual + ' vs ' + expected);

function setup() {
  const el = { addEventListener() {}, pause() {}, currentTime: 0, readyState: 4, seekable: { length: 1, start: () => 0, end: () => 30 } };
  const p = new globalThis.EditorPlayer(el);
  p.ctx = {
    currentTime: 4.05, state: 'running',
    getOutputTimestamp: () => ({ contextTime: 4, performanceTime: performance.now() }),
  };
  p.hasAudio = true;
  p.playing = true;
  const starts = [];
  p.click = (strong, delay) => starts.push(delay);
  return { p, starts };
}
function schedule(p, visual, times, end) {
  const notes = times.map(time => ({ time, lanes: [[0, 0]] }));
  p.scheduleClicks(visual, notes, t => notes.filter(n => n.time < t).length, end);
}

test('visual calibration moves the picture, and the click reaches the output on the musical beat', () => {
  const { p, starts } = setup();
  p.baseOffset = -1.5;
  p.calibration = 0.09;
  p.userOffsetMs = 30;
  near(p.offset, -1.38);
  // At 2x the picture is at 10.000, music at 10.090, and 50 ms of sound is queued.
  p.rate = 2;
  schedule(p, 10, [10.25]);
  assert.equal(starts.length, 1);
  near(starts[0], 0.030);
});

test('short seeks re-arm cancelled notes, small media clock fluctuations do not repeat them', () => {
  const { p, starts } = setup();
  schedule(p, 1, [1.09]);
  schedule(p, 0.996, [1.09]);
  assert.equal(starts.length, 1);
  p.seek(1.012);
  schedule(p, 1.012, [1.09]);
  assert.equal(starts.length, 1); // 等新位置开始播放
  p.el.currentTime = 1.014;
  p.time();
  schedule(p, 1.012, [1.09]);
  assert.equal(starts.length, 2);
});

test('lookahead excludes the loop end and advances in real seconds at slow rates', () => {
  const { p, starts } = setup();
  p.rate = 0.5;
  schedule(p, 1, [1.04, 1.06, 1.12], 1.06);
  assert.equal(starts.length, 1);
  near(starts[0], 0.03);
});

test('a seek or buffering stall freezes the picture at the media position', () => {
  const { p } = setup();
  p.el.currentTime = 3;
  p.lastAudio = 3;
  p.lastAudioAt = performance.now() - 220;
  p.el.seeking = true;
  near(p.time(), 3);
});

test('level pitch and practice speed multiply without changing the file position of a tile', () => {
  const { p, starts } = setup();
  p.baseOffset = -.4;
  for (const pitch of [.75, 1.25]) {
    p.setRate(.5, pitch);
    near(p.el.playbackRate, pitch * .5);
    p.seek(10);
    near(p.el.currentTime, 9.6);
    p.el.currentTime += .01;
    near(p.time(), 10.01);
    starts.length = 0;
    schedule(p, 10.01, [10.01 + pitch * .5 * .10]);
    near(starts[0], .05);
  }
});

// ---------- 2026-11-19 用户录屏：暂停、滚动之后音画不同步 ----------
// 浏览器在 OGG、VBR MP3 里跳播放位置不准（Edge 实测：播放中连续跳转后 OGG 差 0.08–0.9 秒，VBR MP3 差 0.5–2.3 秒；WAV 准）。
// 修法：暂停后原地继续不再跳转；有 Mod 另存的歌曲数据时整首解码，用 Web Audio 从采样里直接跳。

test('pausing and resuming in place does not move the music', () => {
  const { p } = setup();
  let sets = 0, position = 12.3;
  Object.defineProperty(p.el, 'currentTime', { get: () => position, set: v => { sets++; position = v; }, configurable: true });
  p.el.play = () => Promise.resolve();
  p.el.pause = () => { p.el.paused = true; };
  p.el.paused = false;
  p.lastAudio = 12.3;
  p.lastAudioAt = performance.now() - 30; // 媒体时钟 30 毫秒没更新，画面按经过的时间往前推
  p.pause();
  p.seek(p.time()); // 界面：暂停时记下 player.time()，再按播放时先 seek 到它
  p.play();
  assert.equal(sets, 0);
});

/** 已经解码好的整首歌：假的 AudioContext 记下每个声源从哪开始放。输出延迟 20 毫秒。 */
function decodedSetup() {
  const el = { addEventListener() {}, pause() {}, play: () => Promise.resolve(), load() {}, removeAttribute() {}, currentTime: 0, readyState: 4,
    seekable: { length: 1, start: () => 0, end: () => 30 } };
  const p = new globalThis.EditorPlayer(el);
  const sources = [];
  const ctx = {
    currentTime: 10, state: 'running', destination: {},
    getOutputTimestamp() { return { contextTime: this.currentTime - 0.02, performanceTime: performance.now() }; },
    createBufferSource() {
      const s = { playbackRate: { value: 1 }, connect: n => n, disconnect() {}, start(when, offset) { this.when = when; this.offset = offset; }, stop() { this.stopped = true; } };
      sources.push(s);
      return s;
    },
    createGain: () => ({ gain: { value: 1 }, connect: n => n }),
    resume() {},
  };
  p.ctx = ctx;
  p.useBuffer({ duration: 120 });
  p.baseOffset = -1; // 音乐里的位置 = 谱面时间 − 1
  return { p, ctx, sources };
}
const live = sources => sources.filter(s => !s.stopped);

test('decoded music starts exactly where the picture is, and the picture follows what is heard', () => {
  const { p, ctx, sources } = decodedSetup();
  p.seek(5);
  p.play();
  const s = live(sources)[0];
  near(s.offset, 4);
  near(p.time(), 5); // 声音还没出来：画面停在起点
  ctx.currentTime = s.when + 1.02; // 开始放 1 秒后（加上 20 毫秒输出延迟）
  near(p.time(), 6);
});

test('decoded music: wheel seeks while playing land exactly on the last position', () => {
  const { p, ctx, sources } = decodedSetup();
  p.seek(20);
  p.play();
  for (let k = 9; k >= 0; k--) { ctx.currentTime += 0.03; p.seek(40 + k * 0.08); }
  assert.equal(live(sources).length, 1);
  const s = live(sources)[0];
  near(s.offset, 39);
  ctx.currentTime = s.when + 0.52;
  near(p.time(), 40.5);
});

test('decoded music: pause and resume continue from the same sample', () => {
  const { p, ctx, sources } = decodedSetup();
  p.seek(5);
  p.play();
  ctx.currentTime = live(sources)[0].when + 2.02;
  p.pause();
  near(p.time(), 7);
  assert.equal(live(sources).length, 0);
  p.seek(p.time());
  p.play();
  near(live(sources)[0].offset, 6);
});

test('decoded music: a speed change keeps the position and changes the pitch', () => {
  const { p, ctx, sources } = decodedSetup();
  p.seek(5);
  p.play();
  ctx.currentTime = live(sources)[0].when + 1.02;
  p.setRate(0.5, 1);
  const s = live(sources)[0];
  near(s.offset, 5);
  near(s.playbackRate.value, 0.5);
  ctx.currentTime = s.when + 1.02;
  near(p.time(), 6.5);
});

test('decoded music: before the song begins the picture runs and the music is scheduled to start on time', () => {
  const { p, ctx, sources } = decodedSetup();
  p.seek(-1); // 音乐位置 −2：两秒后才开始
  p.play();
  const s = live(sources)[0];
  near(s.offset, 0);
  near(s.when, 10 + 0.02 + 2); // 排在两秒后从头放
  ctx.currentTime = s.when - 1 + 0.02; // 再过一秒（加上输出延迟）才放到音乐开头
  near(p.time(), 0);
});

test('decoded music: the song end stops playback', () => {
  const { p, sources } = decodedSetup();
  p.seek(100);
  p.play();
  near(p.end, 121);
  live(sources)[0].onended();
  assert.equal(p.playing, false);
});

test('the song script written by the Mod decodes to the same bytes', () => {
  const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
  const sandbox = { window: {} };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, 'fixtures', 'audio-script.js'), 'utf8'), sandbox);
  const bytes = globalThis.EditorPlayer.bytesFromBase64(sandbox.window.RHYTHMHINT_AUDIO['recent/sample.ogg']);
  assert.deepEqual(Array.from(bytes), Array.from({ length: 256 }, (_, i) => i));
});
