// 轨道预览的绘制（track.js）：用假画布记下画了什么，查透明度、谱尾的星球、画布尺寸
const test = require('node:test');
const assert = require('node:assert/strict');

globalThis.window = globalThis;
globalThis.devicePixelRatio = 1;
const { TILE } = require('../trackfx.js'); // 同时挂上 globalThis.TrackFx
require('../track.js'); // 挂上 globalThis.TrackView
const { TrackView } = globalThis;

/** 假的 2D 画布：浏览器对 0–1 以外的 globalAlpha 直接忽略、沿用上一次的，这里照样处理并记下来。 */
function fakeCanvas(width = 400, height = 240) {
  const log = { alpha: [], arcs: [], sizeSets: 0 };
  const state = { globalAlpha: 1 };
  const noop = () => {};
  const g = new Proxy({}, {
    get(_, key) {
      if (key === 'createRadialGradient' || key === 'createLinearGradient') return () => ({ addColorStop: noop });
      if (key === 'measureText') return () => ({ width: 10 });
      if (key === 'arc') return (x, y, r) => log.arcs.push({ x, y, r });
      return key in state ? state[key] : noop;
    },
    set(_, key, value) {
      if (key === 'globalAlpha') {
        log.alpha.push(value);
        if (value >= 0 && value <= 1) state.globalAlpha = value;
        return true;
      }
      state[key] = value;
      return true;
    },
  });
  const size = { width: 0, height: 0 };
  const canvas = {
    rect: { width, height },
    getContext: () => g,
    getBoundingClientRect() { return this.rect; },
    get width() { return size.width; }, set width(v) { size.width = v; log.sizeSets++; },
    get height() { return size.height; }, set height(v) { size.height = v; log.sizeSets++; },
  };
  return { canvas, log };
}

/** 一条直线上的 n 块砖，每半秒一块，全都要按。 */
function lineDoc(n, actions) {
  const path = Array.from({ length: n }, (_, i) => ({ x: i * TILE, y: 0, time: i * 0.5, tile: i, hit: i > 0, entry: Math.PI, exit: 0 }));
  return { path, hits: path.slice(1).map(p => ({ tile: p.tile, time: p.time, angle: 180 })), tempo: [], fx: { actions, beat: [[0, 0.5]] } };
}

test('透明度超过 100% 的砖照 100% 画：不把 0–1 以外的值交给画布（浏览器会忽略、沿用上一块砖的透明度）', () => {
  const doc = lineDoc(12, [
    { floor: 0, eventType: 'MoveTrack', startTile: [0, 'Start'], endTile: [0, 'End'], duration: 0, opacity: 300 },
    { floor: 0, eventType: 'MoveTrack', startTile: [5, 'Start'], endTile: [5, 'Start'], duration: 0, opacity: -50 },
  ]);
  const { canvas, log } = fakeCanvas();
  const view = new TrackView(canvas);
  view.setDoc(doc);
  view.draw(2.5, '', '');
  assert.ok(log.alpha.length > 10, '画了砖');
  assert.deepEqual(log.alpha.filter(a => !(a >= 0 && a <= 1)), [], '交给画布的透明度都在 0–1');
  assert.ok(log.alpha.includes(1), '300% 的砖按完全不透明画');
});

test('谱尾：过了最后一块砖，星球停在最后一块砖上（跟着它移动），另一颗绕着它转', () => {
  const n = 8;
  const doc = lineDoc(n, [
    // 倒数第二块在最后被挪走：原来中心那颗留在它上面，画面中间（镜头对着最后一块）一片空
    { floor: n - 1, eventType: 'MoveTrack', startTile: [-1, 'ThisTile'], endTile: [-1, 'ThisTile'], duration: 0, positionOffset: [0, 20] },
    { floor: n - 1, eventType: 'MoveTrack', startTile: [0, 'ThisTile'], endTile: [0, 'ThisTile'], duration: 0, positionOffset: [1, 1] },
  ]);
  const { canvas, log } = fakeCanvas();
  const view = new TrackView(canvas);
  view.setDoc(doc);
  for (const t of [(n - 1) * 0.5 + 0.3, (n - 1) * 0.5 + 3]) {
    log.arcs.length = 0;
    view.camFresh = true;
    view.draw(t, '', '');
    const s = view.h / 10, planets = log.arcs.filter(a => Math.abs(a.r - s * 0.26) < 1e-9);
    // 镜头对着最后一块砖（画面中心），中心那颗星球就在那里，另一颗离它一块砖远
    const center = planets.filter(a => Math.hypot(a.x - view.w / 2, a.y - view.h / 2) < 1e-6);
    assert.ok(center.length > 0, `@${t} 画面中心有星球：${JSON.stringify(planets)}`);
    const orbit = planets.filter(a => Math.abs(Math.hypot(a.x - view.w / 2, a.y - view.h / 2) - s * TILE) < 1e-6);
    assert.ok(orbit.length > 0, `@${t} 另一颗绕着它`);
  }
});

test('谱尾前一刻和落到最后一块砖那一刻：画面连续（两颗星球的位置不跳）', () => {
  const n = 6;
  const { canvas, log } = fakeCanvas();
  const view = new TrackView(canvas);
  view.setDoc(lineDoc(n, []));
  const at = t => {
    log.arcs.length = 0;
    view.camFresh = true;
    view.draw(t, '', '');
    const s = view.h / 10;
    return log.arcs.filter(a => Math.abs(a.r - s * 0.26) < 1e-9).map(a => [Math.round(a.x * 100), Math.round(a.y * 100)]).sort().join(' ');
  };
  const end = (n - 1) * 0.5;
  assert.equal(at(end - 1e-9), at(end));
});

test('卡片藏着（宽高为 0）时不每帧重设画布尺寸；显示出来后由 resize 量好', () => {
  const { canvas, log } = fakeCanvas(0, 0);
  const view = new TrackView(canvas);
  view.setDoc(lineDoc(6, []));
  for (let k = 0; k < 20; k++) view.draw(k * 0.1, '', '');
  assert.ok(log.sizeSets <= 2, `重设了 ${log.sizeSets} 次`);
  canvas.rect = { width: 300, height: 200 };
  view.resize(); // app.js 的 ResizeObserver 在卡片显示出来时调
  view.draw(1, '', '');
  assert.equal(canvas.width, 300);
  assert.equal(canvas.height, 200);
});

test('像素比变了（拖到别的显示器）重新量画布；没变时不重设', () => {
  const { canvas, log } = fakeCanvas(300, 200);
  const view = new TrackView(canvas);
  view.setDoc(lineDoc(6, []));
  view.draw(0.5, '', '');
  const sets = log.sizeSets;
  view.draw(0.6, '', '');
  view.resize();
  assert.equal(log.sizeSets, sets, '大小没变不重设');
  globalThis.devicePixelRatio = 2;
  try {
    view.draw(0.7, '', '');
    assert.equal(canvas.width, 600);
    assert.equal(canvas.height, 400);
  } finally {
    globalThis.devicePixelRatio = 1;
  }
});
