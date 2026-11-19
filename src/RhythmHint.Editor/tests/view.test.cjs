// 谱面画布每帧画了什么：画布打桩只计数，不真的画。
const { test } = require('node:test');
const assert = require('node:assert/strict');

globalThis.window = globalThis;
globalThis.devicePixelRatio = 1;

/** 假的 2D 画布：每种调用记个数。 */
function fakeContext(counts) {
  return new Proxy({}, {
    get(target, key) {
      if (key in target) return target[key];
      if (key === 'createLinearGradient' || key === 'createRadialGradient') return () => ({ addColorStop() {} });
      if (key === 'createPattern') return () => ({ pattern: true });
      if (key === 'measureText') return () => ({ width: 40 });
      return () => { counts[key] = (counts[key] || 0) + 1; };
    },
    set(target, key, value) { target[key] = value; return true; },
  });
}
function fakeCanvas(w, h, counts) {
  const g = fakeContext(counts);
  return { getContext: () => g, getBoundingClientRect: () => ({ width: w, height: h, left: 0, top: 0 }), width: 0, height: 0 };
}
// 背景点阵的小图块画在离屏画布上
globalThis.document = { createElement: () => fakeCanvas(22, 22, {}) };
require('../view.js');

/** 长谱：perSec 个音符每秒，两种参考排法都和当前手序不一样。 */
function makeDoc(count, perSec) {
  const notes = [], standard = new Map(), alternate = new Map();
  for (let i = 0; i < count; i++) {
    const lanes = [[i % 4, 0]];
    notes.push({ id: i, time: i / perSec, tile: i + 1, lastTile: i + 1, lanes, orig: lanes.map(l => l.slice()), auto: false });
    standard.set(i + 1, { lastTile: i + 1, lanes: [[(i + 1) % 4, 0]] });
    alternate.set(i + 1, { lastTile: i + 1, lanes: [[(i + 2) % 4, 0]] });
  }
  const beats = [];
  for (let t = 0, k = 0; t < count / perSec; t += 0.3, k++) beats.push({ time: t, measure: k % 4 === 0 });
  return { keys: 4, keyLabels: ['D', 'F', 'J', 'K'], notes, refs: { standard, alternate }, beats, tempo: [{ time: 0, bpm: 200 }] };
}

function stateAt(doc, time) {
  return {
    doc, time, pxPerSec: 240, show: { beats: true, wave: false, gaps: true, ghost: true, standard: true, alternate: true },
    leftInset: 0, rightInset: 312, refScale: 0.5, round: false, preview: null, selection: new Set(), hover: null, anims: new Map(), now: 0,
    jacks: new Set(), playing: true, loop: { a: null, b: null, on: false }, flash: [], press: [], labels: doc.keyLabels, t: k => k, waveform: null, audioOffset: 0, box: null,
  };
}

test('reference columns skip notes that went past the judge line long ago', () => {
  const doc = makeDoc(6000, 30);
  // 标准排法里有一个长按：头在画面下面，尾巴还在画面里，要照常画
  doc.refs.standard.set(3001, { lastTile: 3001, lanes: [[0, 6]] });
  const view = new globalThis.ChartView(fakeCanvas(1920, 1000, {}));
  view.resize();
  view.setDoc(doc);
  const state = stateAt(doc, 103);
  let hidden = 0, holds = 0;
  view.gameNote = function (x, lw, s, y, color) {
    if (y > this.geom.bottom + 30) hidden++; // 判定线下面、被裁掉看不见
    return globalThis.ChartView.prototype.gameNote.call(this, x, lw, s, y, color);
  };
  view.gameHold = function (...args) { holds++; return globalThis.ChartView.prototype.gameHold.apply(this, args); };
  view.draw(state);
  assert.equal(hidden, 1); // 只有那个长按的头
  assert.equal(holds, 1);
});

test('the background dots are one pattern fill, not thousands of tiny squares', () => {
  const counts = {};
  const view = new globalThis.ChartView(fakeCanvas(1920, 1000, counts));
  view.resize();
  view.drawDots(stateAt(makeDoc(10, 10), 12.345));
  assert.ok((counts.fillRect || 0) <= 1, String(counts.fillRect));
});
