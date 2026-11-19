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
