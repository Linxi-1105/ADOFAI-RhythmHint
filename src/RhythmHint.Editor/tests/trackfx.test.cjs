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
