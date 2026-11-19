// 轨道预览的动画（trackfx.js）：按关卡事件算每块砖每一刻的状态和镜头
const test = require('node:test');
const assert = require('node:assert/strict');
const { TrackFx, ease, resolveTile, TILE } = require('../trackfx.js');

// 10 块砖排成一条直线，每半秒一块；一拍 0.5 秒
const path = Array.from({ length: 10 }, (_, i) => ({ x: i * TILE, y: 0, time: i * 0.5, tile: i }));
const spb = () => 0.5;
const near = (a, b, msg) => assert.ok(Math.abs(a - b) < 1e-6, `${msg ?? ''} ${a} ≈ ${b}`);

test('缓动：两头是 0 和 1，常见几种的中点', () => {
  for (const name of ['Linear', 'InSine', 'OutQuad', 'InOutCubic', 'InExpo', 'OutBack', 'InElastic', 'OutBounce', 'InOutCirc', 'Flash']) {
    near(ease(name, 0), 0, name);
    near(ease(name, 1), 1, name);
  }
  near(ease('Linear', 0.5), 0.5);
  near(ease('OutQuad', 0.5), 0.75);
  near(ease('InQuad', 0.5), 0.25);
  near(ease('Unknown', 0.3), 0.3, '不认识的按线性');
});

/** DOTween（EaseManager.Evaluate）的 Elastic 原式，照抄它的 time / duration 写法；默认振幅 1.70158、周期 0（= 时长的 0.3，InOut 0.45）。 */
function dotweenElastic(name, time, duration, amp = 1.70158, period = 0) {
  const TWO_PI = 2 * Math.PI;
  const shift = p => (amp < 1 ? p / 4 : p / TWO_PI * Math.asin(1 / amp));
  if (time === 0) return 0;
  if (name === 'InOutElastic') {
    if ((time /= duration * 0.5) === 2) return 1;
    const p = period || duration * (0.3 * 1.5), s = shift(p);
    if (time < 1) return -0.5 * (amp * Math.pow(2, 10 * (time -= 1)) * Math.sin((time * duration - s) * TWO_PI / p));
    return amp * Math.pow(2, -10 * (time -= 1)) * Math.sin((time * duration - s) * TWO_PI / p) * 0.5 + 1;
  }
  if ((time /= duration) === 1) return 1;
  const p = period || duration * 0.3, s = shift(p);
  if (name === 'InElastic') return -(amp * Math.pow(2, 10 * (time -= 1)) * Math.sin((time * duration - s) * TWO_PI / p));
  return amp * Math.pow(2, -10 * time) * Math.sin((time * duration - s) * TWO_PI / p) + 1;
}

test('Elastic 缓动用 DOTween 的默认振幅和周期（Penner 原式振幅是 1，回弹小很多）', () => {
  for (const name of ['InElastic', 'OutElastic', 'InOutElastic']) {
    for (const u of [0.05, 0.1, 0.2, 0.33, 0.5, 0.61, 0.8, 0.95]) {
      near(ease(name, u), dotweenElastic(name, u * 2.5, 2.5), `${name}(${u})`); // 时长随便取，结果只看进度
    }
  }
  assert.ok(ease('OutElastic', 0.1) > 1.8, 'OutElastic 在 0.1 冲到 1.85 左右（Penner 只有 1.25）');
});

test('相对砖号：本砖、开头、结尾，限在范围里', () => {
  assert.equal(resolveTile([2, 'ThisTile'], 3, 9), 5);
  assert.equal(resolveTile([4, 'Start'], 3, 9), 4);
  assert.equal(resolveTile([-1, 'End'], 3, 9), 8);
  assert.equal(resolveTile([20, 'ThisTile'], 3, 9), 9);
  assert.equal(resolveTile(null, 3, 9), null);
});

test('移动轨道：从触发时刻缓到目标，没写的属性不动', () => {
  const fx = new TrackFx(path, { actions: [
    { floor: 2, eventType: 'MoveTrack', startTile: [0, 'ThisTile'], endTile: [2, 'ThisTile'], duration: 2, positionOffset: [1, null], opacity: 0, ease: 'Linear' },
  ] }, spb);
  near(fx.value(3, 'dx', 0.9), 0, '触发前');
  near(fx.value(3, 'dx', 1.5), 0.75, '一半');
  near(fx.value(3, 'dx', 2.5), 1.5, '结束后（1 砖 = 1.5）');
  near(fx.value(3, 'alpha', 1.5), 0.5);
  near(fx.value(3, 'dy', 2.5), 0, '位置 y 写的是 null，不动');
  near(fx.value(5, 'dx', 2.5), 0, '范围外的砖不动');
  assert.deepEqual(fx.tileAt(4, 3), [4 * TILE + 1.5, 0]);
});

test('移动轨道：后一个事件从当前（缓到一半的）值接着缓', () => {
  const fx = new TrackFx(path, { actions: [
    { floor: 0, eventType: 'MoveTrack', startTile: [3, 'Start'], endTile: [3, 'Start'], duration: 4, positionOffset: [2, 0] },
    { floor: 2, eventType: 'MoveTrack', startTile: [3, 'Start'], endTile: [3, 'Start'], duration: 0, positionOffset: [0, 0] },
  ] }, spb);
  near(fx.value(3, 'dx', 0.5), 0.75, '第一个缓到四分之一');
  near(fx.value(3, 'dx', 1.0), 0, '第二个时长 0：一下子回原位');
});

test('移动轨道：隔几块、缩放两种写法、旋转相对原来的角度、延迟触发', () => {
  const fx = new TrackFx(path, { actions: [
    { floor: 4, eventType: 'PositionTrack', rotation: 30 },
    { floor: 1, eventType: 'MoveTrack', startTile: [0, 'ThisTile'], endTile: [4, 'ThisTile'], gapLength: 1, duration: 0, scale: [200, 50] },
    { floor: 1, eventType: 'MoveTrack', startTile: [3, 'ThisTile'], endTile: [3, 'ThisTile'], duration: 0, rotationOffset: 45, scale: 300, angleOffset: 90 },
  ] }, spb);
  near(fx.value(1, 'sx', 1), 2);
  near(fx.value(1, 'sy', 1), 0.5);
  near(fx.value(2, 'sx', 1), 1, '隔一块：第 2 块不动');
  near(fx.value(3, 'sx', 1), 2);
  near(fx.value(4, 'rot', 0.6), 30, 'PositionTrack 定的角度');
  near(fx.value(4, 'rot', 0.7), 30, '延迟 90 度 = 半拍 = 0.25 秒：0.75 秒才触发');
  near(fx.value(4, 'rot', 0.8), 75, '旋转 45 度相对原来的 30 度');
  near(fx.value(4, 'sx', 0.8), 3);
});

test('出现消失动画：提前几拍出现、经过几拍后消失', () => {
  const fx = new TrackFx(path, { settings: { trackAnimation: 'Fade', beatsAhead: 2, trackDisappearAnimation: 'None' }, actions: [
    { floor: 6, eventType: 'AnimateTrack', trackDisappearAnimation: 'Fade', beatsBehind: 0 },
  ] }, spb);
  // 第 5 块 2.5 秒到：提前 2 拍（1 秒）在 1.5 秒开始出现，动画半拍（0.25 秒）
  assert.equal(fx.reveal(5, 1.4).alpha, 0);
  assert.ok(fx.reveal(5, 1.6).alpha > 0 && fx.reveal(5, 1.6).alpha < 1);
  assert.equal(fx.reveal(5, 2).alpha, 1);
  assert.equal(fx.reveal(5, 10).alpha, 1, '第 5 块还用关卡设置：不消失');
  // 第 6 块起改成经过后马上淡出
  assert.equal(fx.reveal(6, 3.0).alpha, 1);
  assert.equal(fx.reveal(6, 3.5).alpha, 0);
});

test('事件的拍数按砖块速度换算（fx.beat），不用节拍线的 BPM', () => {
  const fx = new TrackFx(path, { beat: [[0, 0.5], [2, 0.25]], actions: [
    { floor: 6, eventType: 'MoveTrack', startTile: [0, 'ThisTile'], endTile: [0, 'ThisTile'], duration: 2, positionOffset: [2, 0] },
  ] }, () => 99);
  // 第 6 块在 3 秒，那时一拍 0.25 秒：2 拍 = 0.5 秒
  near(fx.value(6, 'dx', 3.25), 1.5, '一半');
  near(fx.value(6, 'dx', 3.5), 3, '结束');
});

test('没有事件：砖块不动、全都看得见', () => {
  const fx = new TrackFx(path, null, spb);
  assert.deepEqual(fx.state(3, 5), { dx: 0, dy: 0, rot: 0, sx: 1, sy: 1, alpha: 1 });
  assert.equal(fx.reveal(3, -100).alpha, 1);
});

test('星球位置：两块砖之间按时间插值，砖被移动时跟着走', () => {
  const fx = new TrackFx(path, { actions: [
    { floor: 0, eventType: 'MoveTrack', startTile: [3, 'Start'], endTile: [3, 'Start'], duration: 0, positionOffset: [0, 2] },
    { floor: 0, eventType: 'MoveCamera', duration: 0, relativeTo: 'Global', position: [9, 9], zoom: 300 },
  ] }, spb);
  const [x, y] = fx.playerAt(1.25); // 第 2 块（1.0 秒）和第 3 块（1.5 秒，被往上挪 3）中间
  near(x, 2.5 * TILE);
  near(y, 1.5);
  assert.equal(fx.camera, undefined, '谱面自带的镜头不用');
});

// ---------- 重复事件（RepeatEvents）、条件事件（SetConditionalEvents） ----------

const hide = (floor, tile, tag) => ({ floor, eventType: 'MoveTrack', startTile: tile, endTile: tile, duration: 0, opacity: 0, eventTag: tag });
const show = (floor, tile) => ({ floor, eventType: 'MoveTrack', startTile: tile, endTile: tile, duration: 0, opacity: 100 });

test('重复事件（按砖）：每到下一块砖重放一次，ThisTile 跟着到达的砖（工坊 3386617648 身后的砖逐块藏起）', () => {
  const fx = new TrackFx(path, { actions: [
    { floor: 2, eventType: 'RepeatEvents', repeatType: 'Floor', repetitions: 1, floorCount: 3, interval: 1, gapLength: 1, executeOnCurrentFloor: true, tag: '1' },
    hide(2, [-1, 'ThisTile'], '1'),
  ] }, spb);
  // 第 k 块 k × 0.5 秒到：本砖（2）一次 + 往后 3 块（3、4、5）各一次，每次藏起身后那块
  for (const k of [2, 3, 4, 5]) {
    near(fx.value(k - 1, 'alpha', k * 0.5 - 0.01), 1, `第 ${k - 1} 块藏起之前`);
    near(fx.value(k - 1, 'alpha', k * 0.5), 0, `星球到第 ${k} 块时藏起第 ${k - 1} 块`);
  }
  near(fx.value(5, 'alpha', 10), 1, '只往后重放 floorCount 块');
});

test('重复事件（按砖）：不跟着到达的砖时仍按本砖算 ThisTile，只是到那块砖时触发；隔 gapLength 块一次；多个标签', () => {
  const fx = new TrackFx(path, { actions: [
    { floor: 1, eventType: 'RepeatEvents', repeatType: 'Floor', floorCount: 2, gapLength: 2, executeOnCurrentFloor: false, tag: 'a b' },
    hide(1, [0, 'ThisTile'], 'x b'),
    show(2, [1, 'Start']),
    show(4, [1, 'Start']),
  ] }, spb);
  // 本砖 1（0.5 秒）、隔两块的 3（1.5 秒）、5（2.5 秒）各藏一次第 1 块；中间第 2、4 块（1.0、2.0 秒）又显示出来
  for (const [t, alpha] of [[0.5, 0], [1.0, 1], [1.5, 0], [2.0, 1], [2.5, 0], [3.5, 0]]) near(fx.value(1, 'alpha', t), alpha, `@${t}`);
  near(fx.value(3, 'alpha', 2), 1, 'ThisTile 没跟着挪到第 3 块');
});

test('重复事件（按拍）：本砖每隔 interval 拍再触发 repetitions 次，拍数按本砖的速度换算', () => {
  const fx = new TrackFx(path, { beat: [[0, 0.5], [2, 0.25]], actions: [
    { floor: 4, eventType: 'RepeatEvents', repeatType: 'Beat', repetitions: 2, floorCount: 9, interval: 3, executeOnCurrentFloor: true, tag: 'p' },
    hide(4, [0, 'Start'], 'p'),
    show(5, [0, 'Start']), // 2.5 秒
    show(7, [0, 'Start']), // 3.5 秒
  ] }, () => 99);
  // 第 4 块在 2 秒，那时一拍 0.25 秒：2、2.75、3.5 秒各藏一次（同一时刻后写的「显示」排在重放后面）
  for (const [t, alpha] of [[2, 0], [2.5, 1], [2.75, 0], [3.4, 0], [5, 1]]) near(fx.value(0, 'alpha', t), alpha, `@${t}`);
});

test('重复事件：老谱面没有 repeatType（按拍），也没写 floorCount 和 executeOnCurrentFloor', () => {
  const fx = new TrackFx(path, { actions: [
    { floor: 0, eventType: 'RepeatEvents', repetitions: 2, interval: 2, tag: 'old' },
    hide(0, [6, 'Start'], 'old'),
    show(1, [6, 'Start']),
    show(3, [6, 'Start']),
  ] }, spb);
  for (const [t, alpha] of [[0, 0], [0.5, 1], [1, 0], [1.5, 1], [2, 0]]) near(fx.value(6, 'alpha', t), alpha, `@${t}`);
});

test('重复事件：只认同一块砖上、标签对得上的移动轨道；按砖重放到最后一块为止', () => {
  const fx = new TrackFx(path, { actions: [
    { floor: 7, eventType: 'RepeatEvents', repeatType: 'Floor', floorCount: 50, executeOnCurrentFloor: true, tag: 't' },
    hide(7, [0, 'ThisTile'], 't'),
    hide(6, [0, 'ThisTile'], 't'), // 别的砖上的：不重复
    hide(7, [-3, 'ThisTile'], 'other'), // 标签不对：不重复
  ] }, spb);
  for (const i of [7, 8, 9]) near(fx.value(i, 'alpha', 5), 0, `第 ${i} 块`);
  near(fx.value(6, 'alpha', 5), 0, '第 6 块上的事件照常触发一次，只藏第 6 块');
  near(fx.value(5, 'alpha', 5), 1, '-3 的那个不跟着重放（第 5 块不受影响）');
  near(fx.value(4, 'alpha', 5), 0, '它自己照常触发一次：藏第 4 块');
});

test('条件事件：只在失误、失败等判定时触发的不触发，完美时触发的按全程完美在本砖触发一次', () => {
  const fx = new TrackFx(path, { actions: [
    { floor: 3, eventType: 'SetConditionalEvents', perfectTag: 'good', hitTag: 'NONE', missTag: 'bad', lossTag: 'lose both' },
    hide(3, [0, 'ThisTile'], 'good'),
    hide(3, [1, 'ThisTile'], 'bad'),
    hide(3, [2, 'ThisTile'], 'lose'),
    hide(3, [3, 'ThisTile'], 'both good'),
    hide(4, [3, 'ThisTile'], 'bad'), // 条件事件在别的砖上：照常触发
  ] }, spb);
  // 第 3 块在 1.5 秒，第 4 块在 2 秒
  near(fx.value(3, 'alpha', 1.5), 0, '完美');
  near(fx.value(4, 'alpha', 3), 1, '失误');
  near(fx.value(5, 'alpha', 3), 1, '失败');
  near(fx.value(6, 'alpha', 1.4), 1);
  near(fx.value(6, 'alpha', 1.5), 0, '既有完美又有失败的标签：完美时会触发');
  near(fx.value(7, 'alpha', 1.9), 1);
  near(fx.value(7, 'alpha', 2), 0, '别的砖上的照常触发');
});

test('画面附近的砖：和逐块算完状态再筛的结果一样（出现消失窗口、移动、缩放都算进去）', () => {
  const rand = seeded(7);
  for (let round = 0; round < 30; round++) {
    const { path: tiles, fx: level, end } = randomLevel(rand);
    level.settings = { trackAnimation: ['None', 'Fade', 'Assemble_Far', 'Grow'][round % 4], beatsAhead: 2,
      trackDisappearAnimation: ['None', 'Scatter_Far', 'Fade'][round % 3], beatsBehind: 1 };
    const fx = new TrackFx(tiles, level, t => (t > 3 ? 0.5 : 0.4));
    for (let t = -1; t <= end + 3; t += 0.37) {
      const cx = tiles[Math.floor(rand() * tiles.length)].x, cy = rand() * 6 - 3;
      const box = { x0: cx - 4, x1: cx + 4, y0: cy - 2.5, y1: cy + 2.5, margin: 3 };
      const want = [];
      for (let i = 0; i < tiles.length; i++) {
        const st = fx.state(i, t), rv = fx.reveal(i, t);
        if (rv.alpha <= 0) continue;
        const x = tiles[i].x + st.dx + rv.dx, y = tiles[i].y + st.dy + rv.dy;
        const m = box.margin * Math.max(1, Math.abs(st.sx * rv.scale), Math.abs(st.sy * rv.scale));
        if (x >= box.x0 - m && x <= box.x1 + m && y >= box.y0 - m && y <= box.y1 + m) want.push({ i, st, rv });
      }
      assert.deepEqual(fx.tilesNear(t, box), want, `第 ${round} 关 @${t}`);
    }
  }
});

test('画面附近的砖：2 万块砖、整条轨道都在动，每帧只给画面附近的砖算状态', () => {
  const n = 20000;
  const big = Array.from({ length: n }, (_, i) => ({ x: i * TILE, y: 0, time: i * 0.2, tile: i }));
  const actions = Array.from({ length: 200 }, (_, k) => ({
    floor: Math.floor(k * n / 200), eventType: 'MoveTrack', startTile: [0, 'Start'], endTile: [0, 'End'], duration: 2,
    positionOffset: [0, k % 3], rotationOffset: k % 7, ease: 'OutSine',
  }));
  actions.push({ floor: 0, eventType: 'RepeatEvents', repeatType: 'Floor', floorCount: n, executeOnCurrentFloor: true, tag: 'r' },
    { floor: 0, eventType: 'MoveTrack', startTile: [-2, 'ThisTile'], endTile: [-2, 'ThisTile'], duration: 1, opacity: 0, eventTag: 'r' });
  const fx = new TrackFx(big, { settings: { trackAnimation: 'Fade', beatsAhead: 4, trackDisappearAnimation: 'None' }, actions, beat: [[0, 0.4]] }, () => 0.4);
  const at = k => k * 0.2 + 0.05, box = k => ({ x0: k * TILE - 9, x1: k * TILE + 9, y0: -5, y1: 5, margin: 3 });
  for (let k = 0; k < 50; k++) fx.tilesNear(at(k * 397), box(k * 397)); // 预热
  const start = performance.now();
  let drawn = 0;
  for (let k = 0; k < 200; k++) drawn += fx.tilesNear(at(k * 97), box(k * 97)).length;
  const ms = (performance.now() - start) / 200;
  assert.ok(drawn / 200 < 40, `每帧留下 ${drawn / 200} 块`);
  assert.ok(ms < 4, `每帧筛砖用了 ${ms.toFixed(2)} ms`);
  // 重复事件展开成 2 万次：身后第 2 块逐块淡出
  near(fx.value(10000, 'alpha', 10002 * 0.2 + 1), 0);
  near(fx.value(10000, 'alpha', 10002 * 0.2 - 0.01), 1);
});

test('PositionTrack：同一块砖的几个事件叠在一起，其他砖合用默认值、不会被改坏', () => {
  const fx = new TrackFx(path, { actions: [
    { floor: 3, eventType: 'PositionTrack', rotation: 30 },
    { floor: 3, eventType: 'PositionTrack', scale: [200, null], opacity: 50 },
    { floor: 5, eventType: 'PositionTrack', rotation: -15 },
  ] }, spb);
  assert.deepEqual(fx.state(3, 0), { dx: 0, dy: 0, rot: 30, sx: 2, sy: 1, alpha: 0.5 });
  assert.deepEqual(fx.state(5, 0), { dx: 0, dy: 0, rot: -15, sx: 1, sy: 1, alpha: 1 });
  assert.deepEqual(fx.state(4, 0), { dx: 0, dy: 0, rot: 0, sx: 1, sy: 1, alpha: 1 });
  assert.equal(fx.baseOf(4), fx.baseOf(6), '没有 PositionTrack 的砖合用一份默认值，每帧不新建对象');
  assert.ok(Object.isFrozen(fx.baseOf(4)), '默认值冻结，调用方改不了');
});

test('特效多的谱：1 万块砖、300 个整轨移动事件，建时间线不卡、不占大内存', () => {
  const n = 10000;
  const big = Array.from({ length: n }, (_, i) => ({ x: i * TILE, y: 0, time: i * 0.2, tile: i }));
  const actions = Array.from({ length: 300 }, (_, k) => ({
    floor: Math.floor(k * n / 300), eventType: 'MoveTrack', startTile: [0, 'Start'], endTile: [0, 'End'], duration: 1,
    positionOffset: [0, k % 2], opacity: k % 2 ? 100 : 60, ease: 'OutSine',
  }));
  const heap = process.memoryUsage().heapUsed, start = performance.now();
  const fx = new TrackFx(big, { actions, beat: [[0, 0.4]] }, () => 0.4);
  const ms = performance.now() - start, mb = (process.memoryUsage().heapUsed - heap) / 1048576;
  assert.ok(ms < 300, `构建用了 ${ms.toFixed(0)} ms`);
  assert.ok(mb < 100, `堆内存多了 ${mb.toFixed(0)} MB`);
  // 第 150 个事件（第 5000 块，1000 秒）把 y 从 1.5 缓回 0、透明度缓到 60%，0.4 秒；整条轨道都跟着动
  for (const i of [0, 7, 5000, n - 1]) {
    near(fx.value(i, 'dy', 1000.3), 1.5 * (1 - ease('OutSine', 0.75)), `第 ${i} 块 y`);
    near(fx.value(i, 'alpha', 1000.3), 1 - 0.4 * ease('OutSine', 0.75), `第 ${i} 块透明度`);
  }
  // 每帧对所有砖取一次状态：二分查找，不随事件数变慢
  const frame = performance.now();
  for (let i = 0; i < n; i++) fx.state(i, 1000.3);
  const frameMs = performance.now() - frame;
  assert.ok(frameMs < 100, `一帧取状态用了 ${frameMs.toFixed(0)} ms`);
});

// ---------- 和修改前的逐砖算法对照 ----------

const num = v => (typeof v === 'number' && Number.isFinite(v) ? v : null);

/** 参照实现：修改前（1.3.1）的逐砖算法，每块砖每个属性各存一份缓动列表。只在这里当对照，产品代码不用它。 */
class PerTileTrackFx extends TrackFx {
  buildBase(events) {
    for (const { a, index } of events) {
      const b = Object.assign(this.baseOf(index), {});
      if (num(a.rotation) !== null) b.rot = a.rotation;
      const scale = a.scale;
      if (num(scale) !== null) b.sx = b.sy = scale / 100;
      else if (Array.isArray(scale)) { if (num(scale[0]) !== null) b.sx = scale[0] / 100; if (num(scale[1]) !== null) b.sy = scale[1] / 100; }
      if (num(a.opacity) !== null) b.alpha = a.opacity / 100;
      this.base.set(index, b);
    }
  }

  baseOf(i) {
    return this.base.get(i) || { dx: 0, dy: 0, rot: 0, sx: 1, sy: 1, alpha: 1 };
  }

  buildMoves(events) {
    this.tweens = new Map();
    for (const { a, index, spb, t0 } of events) {
      let s = resolveTile(a.startTile, index, this.last), e = resolveTile(a.endTile, index, this.last);
      if (s === null) s = index;
      if (e === null) e = s;
      if (s > e) [s, e] = [e, s];
      const step = Math.max(1, Math.round(num(a.gapLength) || 0) + 1);
      const t1 = t0 + Math.max(0, num(a.duration) || 0) * spb;
      const targets = [];
      if (Array.isArray(a.positionOffset)) {
        if (num(a.positionOffset[0]) !== null) targets.push(['dx', a.positionOffset[0] * TILE]);
        if (num(a.positionOffset[1]) !== null) targets.push(['dy', a.positionOffset[1] * TILE]);
      }
      const rotation = num(a.rotationOffset);
      if (num(a.scale) !== null) targets.push(['sx', a.scale / 100], ['sy', a.scale / 100]);
      else if (Array.isArray(a.scale)) {
        if (num(a.scale[0]) !== null) targets.push(['sx', a.scale[0] / 100]);
        if (num(a.scale[1]) !== null) targets.push(['sy', a.scale[1] / 100]);
      }
      if (num(a.opacity) !== null) targets.push(['alpha', a.opacity / 100]);
      for (let i = s; i <= e; i += step) {
        for (const [key, to] of targets) this.push(i, key, t0, t1, to, a.ease);
        if (rotation !== null) this.push(i, 'rot', t0, t1, this.baseOf(i).rot + rotation, a.ease);
      }
    }
  }

  push(i, key, t0, t1, to, easeName) {
    let byKey = this.tweens.get(i);
    if (!byKey) this.tweens.set(i, byKey = {});
    const list = byKey[key] || (byKey[key] = []);
    list.push({ t0, t1, from: this.value(i, key, t0), to, ease: easeName });
  }

  value(i, key, t) {
    const list = this.tweens.get(i) && this.tweens.get(i)[key];
    if (!list || !list.length || list[0].t0 > t) return this.baseOf(i)[key];
    let lo = 0, hi = list.length - 1;
    while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (list[mid].t0 <= t) lo = mid; else hi = mid - 1; }
    const w = list[lo];
    if (t >= w.t1) return w.to;
    return w.from + (w.to - w.from) * ease(w.ease, (t - w.t0) / (w.t1 - w.t0));
  }
}

/** 固定种子的伪随机数（mulberry32），每次跑出来一样。 */
function seeded(seed) {
  return () => {
    seed = (seed + 0x6D2B79F5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** 随机一关：砖块、几块砖上扎堆的 MoveTrack（相同触发时刻、重叠、先后）和 PositionTrack。 */
function randomLevel(rand) {
  const int = (lo, hi) => lo + Math.floor(rand() * (hi - lo + 1));
  const pick = list => list[int(0, list.length - 1)];
  const maybe = (p, make) => (rand() < p ? make() : undefined);
  const n = int(2, 60);
  let time = 0;
  const path = Array.from({ length: n }, (_, i) => {
    const p = { x: i * TILE + int(-2, 2), y: int(-3, 3), time, tile: i };
    time += pick([0, 0.1, 0.25, 0.5, 0.5, 1]); // 0：中旋那种同一时刻的两块砖
    return p;
  });
  const hot = Array.from({ length: int(1, 5) }, () => int(0, n - 1)); // 事件扎堆的几块砖
  const floor = () => (rand() < 0.05 ? n + 3 : rand() < 0.7 ? pick(hot) : int(0, n - 1)); // 偶尔是路径外的砖
  const ref = () => maybe(0.85, () => [int(-4, 6), pick(['ThisTile', 'Start', 'End'])]);
  const scale = () => (rand() < 0.5 ? pick([0, 50, 100, 250]) : [pick([null, 25, 100, 300]), pick([null, 10, 150])]);
  const actions = [];
  for (let k = int(0, 8); k > 0; k--) {
    actions.push({ floor: floor(), eventType: 'PositionTrack', positionOffset: [1, 0],
      rotation: maybe(0.5, () => pick([-45, 0, 30, 90])), scale: maybe(0.4, scale), opacity: maybe(0.4, () => pick([0, 40, 100])) });
  }
  for (let k = int(0, 25); k > 0; k--) {
    actions.push({
      floor: floor(), eventType: 'MoveTrack', startTile: ref(), endTile: ref(),
      gapLength: maybe(0.5, () => int(0, 3)), duration: pick([0, 0, 0.5, 1, 2, 4, undefined]),
      angleOffset: maybe(0.5, () => pick([-180, -90, -45, 0, 30, 90, 360])),
      positionOffset: maybe(0.6, () => [pick([null, -1, 0, 0.5, 2]), pick([null, -2, 0, 1])]),
      rotationOffset: maybe(0.4, () => pick([-90, -30, 15, 45, 180])), scale: maybe(0.4, scale),
      opacity: maybe(0.4, () => pick([0, 10, 50, 100, 120])),
      ease: pick(['Linear', 'OutSine', 'InOutQuad', 'OutBack', 'InElastic', 'OutBounce', 'Flash', undefined]),
      eventTag: maybe(0.4, () => pick(['r1', 'r2', 'r1 r2', 'c', 'x'])),
    });
  }
  // 重复事件、条件事件：两边共用同一套展开，对照的是展开以后的合用缓动
  for (let k = int(0, 3); k > 0; k--) {
    actions.push({
      floor: pick(hot), eventType: 'RepeatEvents', repeatType: pick(['Beat', 'Floor', undefined]), repetitions: int(0, 4), floorCount: int(0, 5),
      interval: pick([0.5, 1, 2, 0]), gapLength: maybe(0.6, () => int(1, 3)), executeOnCurrentFloor: rand() < 0.5, tag: pick(['r1', 'r2', 'r1 r2']),
    });
  }
  if (rand() < 0.3) actions.push({ floor: pick(hot), eventType: 'SetConditionalEvents', perfectTag: pick(['c', 'NONE']), missTag: pick(['c', 'r1', 'NONE']) });
  actions.push({ floor: floor(), eventType: 'AnimateTrack', trackAnimation: 'Fade' }, { floor: 0, eventType: 'MoveCamera', duration: 1 });
  for (let k = actions.length - 1; k > 0; k--) { const j = int(0, k); [actions[k], actions[j]] = [actions[j], actions[k]]; } // 打乱：同一时刻的事件先后不定
  const fx = { actions };
  if (rand() < 0.5) fx.beat = [[0, 0.5], [time / 2, 0.25]];
  return { path, fx, end: time };
}

test('合用缓动后结果和逐砖算法一样：随机关卡逐砖逐时刻对照', () => {
  const rand = seeded(20261119);
  const spbAt = t => (t > 3 ? 0.5 : 0.4);
  for (let round = 0; round < 40; round++) {
    const { path: tiles, fx: level, end } = randomLevel(rand);
    const fx = new TrackFx(tiles, level, spbAt), ref = new PerTileTrackFx(tiles, level, spbAt);
    // 对照的时刻：均匀撒一遍，再加上每段缓动的开始、结束和中间（同一时刻触发、时长 0 的边界都在这里）
    const times = new Set();
    for (let t = -1; t <= end + 6; t += 0.13) times.add(t);
    for (const byKey of ref.tweens.values()) {
      for (const list of Object.values(byKey)) for (const w of list) times.add(w.t0).add(w.t1).add((w.t0 + w.t1) / 2);
    }
    let worst = 0, where = '';
    const check = (a, b, what) => { const d = Math.abs(a - b); if (!(d <= worst)) { worst = d; where = `${what}：${a} ≠ ${b}`; } };
    for (const t of times) {
      for (let i = 0; i < tiles.length; i++) {
        const got = fx.state(i, t), want = ref.state(i, t);
        for (const key of Object.keys(want)) check(got[key], want[key], `第 ${i} 块 ${key} @${t}`);
        const [x, y] = fx.tileAt(i, t), [rx, ry] = ref.tileAt(i, t);
        check(x, rx, `第 ${i} 块位置 x @${t}`);
        check(y, ry, `第 ${i} 块位置 y @${t}`);
      }
      const [x, y] = fx.playerAt(t), [rx, ry] = ref.playerAt(t);
      check(x, rx, `星球 x @${t}`);
      check(y, ry, `星球 y @${t}`);
      check(fx.value(-1, 'dx', t), ref.value(-1, 'dx', t), '路径外的砖');
      check(fx.value(tiles.length, 'alpha', t), ref.value(tiles.length, 'alpha', t), '路径外的砖');
    }
    assert.ok(worst <= 1e-9, `第 ${round} 关差了 ${worst}（${where}）`);
  }
});
