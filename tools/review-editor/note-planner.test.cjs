const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const P = require('./note-planner.js');
const run = (gap, count = 12, start = 1) => Array.from({ length: count }, (_, i) => ({ tileIndex: i + 1, time: start + i * gap, holdSeconds: 0 }));
const lanesOf = notes => notes.map(n => n.lanes.map(l => l.lane));
const comparable = notes => notes.map(({ tileIndex, lastTileIndex, time, lanes }) => ({ tileIndex, lastTileIndex, time, lanes }));

test('current C# demonstration matches every note without mutation', () => {
  const original = JSON.parse(fs.readFileSync(path.join(__dirname, 'demo.json'), 'utf8'));
  const before = JSON.stringify(original);
  assert.equal(original.algorithm, P.algorithmFor(original.options));
  assert.deepEqual(P.plan(original.sourceHits, original.options), comparable(original.notes));
  assert.equal(JSON.stringify(original), before);
});
test('browser demo and portable JSON are the identical current C# export, including 3K and 4K', () => {
  const expected = JSON.parse(fs.readFileSync(path.join(__dirname, 'demo.json'), 'utf8'));
  const context = vm.createContext({ window: {} });
  vm.runInContext(fs.readFileSync(path.join(__dirname, 'demo.js'), 'utf8'), context);
  assert.deepEqual(JSON.parse(JSON.stringify(context.window.RHYTHMHINT_DEMO)), expected);
  assert.ok(expected.notes.some(note => note.lanes.length === 3));
  assert.ok(expected.notes.some(note => note.lanes.length === 4));
});

test('C# uniform-speed state-machine examples and exact thresholds', () => {
  for (const gap of [.2, .3, .6]) assert.deepEqual(lanesOf(P.plan(run(gap))), Array.from({ length: 12 }, () => [2]));
  for (const gap of [.09, .12, .165]) assert.deepEqual(lanesOf(P.plan(run(gap))), Array.from({ length: 12 }, (_, i) => [i % 2 ? 1 : 2]));
  for (const gap of [.06, .0825]) assert.deepEqual(lanesOf(P.plan(run(gap))), Array.from({ length: 12 }, (_, i) => [[3, 2, 0, 1][i % 4]]));
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
  const original = JSON.parse(fs.readFileSync(path.join(__dirname, 'demo.json'), 'utf8'));
  const right = P.plan(original.sourceHits), left = P.plan(original.sourceHits, { LeftHanded: true });
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
