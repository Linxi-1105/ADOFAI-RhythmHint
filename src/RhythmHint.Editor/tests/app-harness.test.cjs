// 测试替身和真的对得上：app-harness.cjs 的假播放器要有 audio.js 的 EditorPlayer 的全部方法、静态成员和字段，
// 时间、循环的走法也一样（2026-11-19 评审：假播放器是手抄的接口，合并时漏了 LOOP_MIN、wrapLoop 也测不出来）。
const { test } = require('node:test');
const assert = require('node:assert/strict');
require('../audio.js');
const { fakePlayerClass, LOOP_MIN } = require('./app-harness.cjs');

const Real = globalThis.EditorPlayer;
const control = () => ({ loads: [], decodes: [], used: [], holdLoads: false, holdDecodes: false, player: null });
const element = () => ({ addEventListener() {}, removeAttribute() {}, load() {}, pause() {}, play: () => Promise.resolve(), src: '', currentTime: 0, paused: true, readyState: 0 });
const kind = (proto, name) => { const d = Object.getOwnPropertyDescriptor(proto, name); return d.get ? 'getter' : typeof d.value; };

test('the fake player has every method and getter of EditorPlayer', () => {
  const Fake = fakePlayerClass(control());
  for (const name of Object.getOwnPropertyNames(Real.prototype)) {
    if (name === 'constructor') continue;
    assert.ok(Object.getOwnPropertyDescriptor(Fake.prototype, name), 'missing ' + name);
    assert.equal(kind(Fake.prototype, name), kind(Real.prototype, name), name);
  }
});

test('the fake player has the static members of EditorPlayer, with the same constants', () => {
  const Fake = fakePlayerClass(control());
  for (const name of Object.getOwnPropertyNames(Real)) {
    if (['length', 'name', 'prototype'].includes(name)) continue;
    assert.ok(name in Fake, 'missing ' + name);
    assert.equal(typeof Fake[name], typeof Real[name], name);
    if (typeof Real[name] !== 'function') assert.equal(Fake[name], Real[name], name);
  }
  assert.equal(LOOP_MIN, Real.LOOP_MIN);
});

test('the fake player has the fields of EditorPlayer', () => {
  const Fake = fakePlayerClass(control());
  const real = new Real(element()), fake = new Fake(element());
  for (const name of Object.keys(real)) assert.ok(name in fake, 'missing ' + name);
});

test('the fake player keeps time and wraps the loop like EditorPlayer', () => {
  const saved = Object.getOwnPropertyDescriptor(globalThis, 'performance');
  let now = 1000;
  Object.defineProperty(globalThis, 'performance', { value: { now: () => now }, configurable: true, writable: true });
  try {
    const Fake = fakePlayerClass(control());
    const players = [new Real(element()), new Fake(element())];
    const run = p => {
      const out = [];
      p.setRate(0.5);
      p.seek(5);
      p.play();
      now += 1500;
      out.push(p.time()); // 0.5 倍速走了 1.5 秒
      out.push(p.wrapLoop(p.time(), 5, 5.5)); // 走过终点：跳回起点
      out.push(p.wrapLoop(p.time(), 5, 5 + LOOP_MIN / 2)); // 区间太短：不绕回
      now += 400;
      p.pause();
      out.push(p.time());
      now += 1000;
      out.push(p.time()); // 暂停着不走
      return out;
    };
    const [real, fake] = players.map(p => { now = 1000; return run(p); });
    assert.deepEqual(fake.map(v => Math.round(v * 1e6)), real.map(v => Math.round(v * 1e6)));
  } finally {
    Object.defineProperty(globalThis, 'performance', saved);
  }
});
