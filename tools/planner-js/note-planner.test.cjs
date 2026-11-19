const { test } = require('node:test');
const assert = require('node:assert/strict');
const P = require('./note-planner.js');
const run = (gap, count = 12, start = 1) => Array.from({ length: count }, (_, i) => ({ tileIndex: i + 1, time: start + i * gap, holdSeconds: 0 }));
const lanesOf = notes => notes.map(n => n.lanes.map(l => l.lane));
// Synthetic chart mixing slow notes, alternation, rolls, 2–4 key chords and holds (no real level).
function mixedChart() {
  const hits = []; let time = 1;
  const add = (gap, angle, holdSeconds = 0) => { time += gap; hits.push({ tileIndex: hits.length + 1, time, holdSeconds, angle }); };
  for (const gap of [.3, .12, .06]) for (let i = 0; i < 12; i++) add(gap, 180, i % 5 === 0 ? .2 : 0);
  for (const count of [2, 3, 4, 2]) { add(.3, 180, .125); for (let j = 1; j < count; j++) add(.02, 15, (j + 1) * .125); }
  return hits;
}

test('C# uniform-speed state-machine examples and exact thresholds', () => {
  for (const gap of [.2, .3, .6]) assert.deepEqual(lanesOf(P.plan(run(gap))), Array.from({ length: 12 }, () => [2]));
  // 默认重置间隔 130 ms（2026-11-19 从 165 改）：130 ms 以上单指，65–130 ms 两只手食指轮流，65 ms 以内轮指
  for (const gap of [.09, .12, .13]) assert.deepEqual(lanesOf(P.plan(run(gap))), Array.from({ length: 12 }, (_, i) => [i % 2 ? 1 : 2]));
  for (const gap of [.05, .065]) assert.deepEqual(lanesOf(P.plan(run(gap))), Array.from({ length: 12 }, (_, i) => [[3, 2, 0, 1][i % 4]]));
  assert.deepEqual(lanesOf(P.plan(run(.06, 8), { OuterRoll: false })), Array.from({ length: 8 }, (_, i) => [[2, 3, 1, 0][i % 4]]));
  assert.deepEqual(lanesOf(P.plan(run(.04), { UseRoll: false })), Array.from({ length: 12 }, (_, i) => [i % 2 ? 1 : 2]));
});

test('pseudo chords compare against the first tile, not the preceding tile', () => {
  const hits = [0, .025, .05].map((time, i) => ({ tileIndex: i + 1, time, holdSeconds: 0 }));
  const notes = P.plan(hits);
  assert.equal(notes.length, 2);
  assert.equal(notes[0].tileIndex, 1); assert.equal(notes[0].lastTileIndex, 2); assert.equal(notes[1].tileIndex, 3);
  assert.equal(P.plan([{ tileIndex: 1, time: 0 }, { tileIndex: 2, time: .035 }]).length, 2);
});

test('three and four-key chords preserve every individual hold', () => {
  for (const count of [3, 4]) {
    const hits = Array.from({ length: count }, (_, i) => ({ tileIndex: i + 1, time: i * .02, angle: i ? 15 : 180, holdSeconds: (i + 1) * .1 }));
    const notes = P.plan(hits);
    assert.equal(notes.length, 1); assert.equal(notes[0].lanes.length, count);
    assert.deepEqual(notes[0].lanes.map(l => l.holdSeconds).sort(), hits.map(h => h.holdSeconds).sort());
  }
});

test('TimeReset and unknown styles retain core fallback despite unused invalid review parameters', () => {
  const expected = P.plan(run(.06666666666666667, 5));
  for (const Style of ['TimeReset', 999]) assert.deepEqual(P.plan(run(.06666666666666667, 5), {
    Style, ReviewShortMinGap: NaN, ReviewShortMaxGap: -1, ReviewShortMinNotes: 0, ReviewShortMaxNotes: -3, ReviewTimingTolerance: Infinity
  }), expected);
  assert.throws(() => P.plan(run(.06, 5), { Style: 'ReviewPreferenceV1', ReviewShortMinNotes: 1 }));
});

test('C# chord-focus and fast-chord alternation examples', () => {
  const hits = [1, 1.11, 1.135, 1.33].map((time, i) => ({ tileIndex: i + 1, time }));
  assert.deepEqual(lanesOf(P.plan(hits)), [[1], [2, 3], [1]]);
  const fast = Array.from({ length: 8 }, (_, i) => [{ tileIndex: i * 2 + 1, time: 1 + i * .1 }, { tileIndex: i * 2 + 2, time: 1 + i * .1 + .01 }]).flat();
  assert.deepEqual(lanesOf(P.plan(fast)), Array.from({ length: 8 }, (_, i) => i % 2 ? [0, 1] : [2, 3]));
});

test('mirroring is applied after the same full fingering and hold processing', () => {
  const hits = mixedChart();
  const right = P.plan(hits), left = P.plan(hits, { LeftHanded: true });
  for (const count of [2, 3, 4]) assert.ok(right.some(note => note.lanes.length === count), count + '-key chord covered');
  assert.deepEqual(left, right.map(note => ({ ...note, lanes: note.lanes.map(item => ({ ...item, lane: 3 - item.lane })).sort((a, b) => a.lane - b.lane) })));
});

test('inputs are immutable and invalid timing is rejected before planning', () => {
  assert.deepEqual(P.plan([]), []);
  const hits = run(.06).map((hit, i) => ({ ...hit, holdSeconds: i === 1 ? .33 : 0 })), before = structuredClone(hits);
  Object.freeze(hits); hits.forEach(Object.freeze);
  const planned = P.plan(hits); assert.deepEqual(hits, before); assert.equal(planned[1].lanes[0].holdSeconds, .33);
  assert.throws(() => P.plan([{ tileIndex: 1, time: 1 }, { tileIndex: 2, time: 0 }]), /排序/);
  assert.throws(() => P.plan([{ tileIndex: 1, time: NaN }]), /有效/);
  assert.throws(() => P.plan([{ tileIndex: 1, time: 0, holdSeconds: -1 }]), /长按/);
  assert.throws(() => P.plan([], { JackWindow: Infinity }), /参数/);
});
