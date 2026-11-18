const { test } = require('node:test');
const assert = require('node:assert/strict');
const C = require('./core.js');
const M = require('./studio-model.js');

function fixture() {
  return {
    format: 'rhythmhint-review', schemaVersion: 1, chartId: 'studio-fixture', name: '工作站测试', source: 'game', algorithm: 'NotePlanner/1.0.0',
    options: { ChordWindow: .035, ResetWindow: .165, ChordFocusWindow: .45, JackWindow: .15, UseRoll: true, OuterRoll: true, LeftHanded: false }, keyLabels: ['D', 'F', 'J', 'K'],
    notes: [
      { id: 'a', tileIndex: 1, lastTileIndex: 1, time: -.2, lanes: [{ lane: 0, holdSeconds: 0 }] },
      { id: 'b', tileIndex: 2, lastTileIndex: 3, time: .5, lanes: [{ lane: 1, holdSeconds: 1.2 }, { lane: 2, holdSeconds: .4 }] },
      { id: 'c', tileIndex: 4, lastTileIndex: 4, time: 1, lanes: [{ lane: 1, holdSeconds: 0 }] },
      { id: 'd', tileIndex: 5, lastTileIndex: 5, time: 2, lanes: [{ lane: 3, holdSeconds: 0 }] }
    ],
    sourceHits: [{ tileIndex: 1, time: -.2, holdSeconds: 0 }, { tileIndex: 2, time: .5, holdSeconds: 1.2 }, { tileIndex: 3, time: .525, holdSeconds: .4 }, { tileIndex: 4, time: 1, holdSeconds: 0 }, { tileIndex: 5, time: 2, holdSeconds: 0 }],
    audio: { file: 'local.ogg', offset: .15 }, review: { status: 'draft', comment: '原始备注', edits: [] }
  };
}
const geometry = M.layout(800, 600);
const x = lane => geometry.x + (lane + .5) * geometry.laneWidth;
const y = time => M.timeToY(time, 0, 200, geometry.hitY);

test('future notes rise above the judgement line and screen/time transforms roundtrip', () => {
  assert.equal(M.timeToY(1, 1, 200, 520), 520);
  assert.equal(M.timeToY(2, 1, 200, 520), 320);
  for (const t of [-2.3, 0, .125, 20]) {
    assert.ok(Math.abs(M.yToTime(M.timeToY(t, .25, 160, 520), .25, 160, 520) - t) < 1e-12);
  }
  assert.deepEqual(geometry, { x: 100, laneWidth: 150, width: 600, hitY: 520, top: 28, bottom: 576 });
  const narrow = M.layout(120, 100);
  assert.ok(narrow.x >= 0 && narrow.x + narrow.width <= 120);
  assert.ok(narrow.hitY >= narrow.top && narrow.hitY <= narrow.bottom);
});

test('hit testing chooses the actual lane and uses edited positions', () => {
  const doc = fixture();
  assert.deepEqual(M.hitTest(doc.notes, new Map(), x(2), y(.5), 0, 200, geometry), { noteId: 'b', lane: 2 });
  assert.equal(M.hitTest(doc.notes, new Map(), x(0), y(.5), 0, 200, geometry), null);
  const revised = M.moveLane(doc, 'c', 1, 3);
  assert.deepEqual(M.hitTest(doc.notes, C.editMap(revised), x(3), y(1), 0, 200, geometry), { noteId: 'c', lane: 3 });
  assert.deepEqual(M.hitTest(doc.notes, new Map([['c', revised.review.edits[0].lanes]]), x(3), y(1), 0, 200, geometry), { noteId: 'c', lane: 3 });
});

test('hold body is clickable but a nearby tap head takes precedence', () => {
  const notes = fixture().notes;
  assert.deepEqual(M.hitTest(notes, new Map(), x(1), y(1.3), 0, 200, geometry), { noteId: 'b', lane: 1 });
  assert.deepEqual(M.hitTest(notes, new Map(), x(1), y(1), 0, 200, geometry), { noteId: 'c', lane: 1 });
  assert.equal(M.hitTest(notes, new Map(), geometry.x - 1, y(.5), 0, 200, geometry), null);
  assert.equal(M.hitTest(notes, new Map(), x(1), geometry.top - 1, 0, 200, geometry), null);
});

test('box select handles reverse drags and selects a chord once from either head', () => {
  const notes = fixture().notes;
  const rect = { x: x(1) + 10, y: y(.5) + 10, width: -20, height: -20 };
  assert.deepEqual(M.boxSelect(notes, new Map(), rect, 0, 200, geometry), ['b']);
  assert.deepEqual(M.boxSelect(notes, new Map(), { x: geometry.x, y: y(1) - 10, width: geometry.width, height: y(.5) - y(1) + 20 }, 0, 200, geometry), ['b', 'c']);
});

test('batch shift is atomic when any selected note would leave the lanes', () => {
  const doc = fixture(), before = C.clone(doc);
  assert.throws(() => M.applyBatch(doc, ['b', 'd'], { type: 'shift', delta: 1 }), /超出/);
  assert.deepEqual(doc, before);
  assert.throws(() => M.applyBatch(doc, ['b', 'missing'], { type: 'shift', delta: -1 }), /不存在/);
  assert.deepEqual(doc, before);
  const shifted = M.applyBatch(doc, ['b', 'b'], { type: 'shift', delta: -1 });
  assert.equal(shifted.review.edits.length, 1);
  assert.deepEqual(shifted.review.edits[0].lanes, [{ lane: 0, holdSeconds: 1.2 }, { lane: 1, holdSeconds: .4 }]);
});

test('mirror preserves each finger hold, original timing, raw hits and extra metadata', () => {
  const doc = fixture(), original = C.clone(doc);
  const mirrored = M.applyBatch(doc, new Set(['b', 'c']), { type: 'mirror' });
  assert.deepEqual(C.editMap(mirrored).get('b').lanes, [{ lane: 2, holdSeconds: 1.2 }, { lane: 1, holdSeconds: .4 }]);
  assert.deepEqual(mirrored.notes, original.notes);
  assert.deepEqual(mirrored.sourceHits, original.sourceHits);
  assert.deepEqual(mirrored.audio, original.audio);
  assert.deepEqual(doc, original);
  const restored = M.applyBatch(mirrored, ['b', 'c'], { type: 'mirror' });
  for (const note of doc.notes.filter(n => ['b', 'c'].includes(n.id))) assert.ok(C.sameLanes(C.editMap(restored).get(note.id).lanes, note.lanes));
});

test('assign accepts cross-hand chords without changing note count or hold durations', () => {
  const assigned = M.applyBatch(fixture(), ['b'], { type: 'assign', lanes: [0, 3] });
  assert.deepEqual(assigned.review.edits[0].lanes, [{ lane: 0, holdSeconds: 1.2 }, { lane: 3, holdSeconds: .4 }]);
  assert.throws(() => M.applyBatch(fixture(), ['b'], { type: 'assign', lanes: [1] }), /数量/);
  assert.throws(() => M.applyBatch(fixture(), ['b'], { type: 'assign', lanes: [1, 1] }), /重复/);
});

test('dragging one chord head retains its hold and refuses collisions atomically', () => {
  const doc = fixture(), before = C.clone(doc);
  assert.throws(() => M.moveLane(doc, 'b', 1, 2), /占用/);
  assert.throws(() => M.moveLane(doc, 'b', 0, 3), /没有/);
  assert.deepEqual(doc, before);
  const moved = M.moveLane(doc, 'b', 1, 3);
  assert.deepEqual(moved.review.edits[0].lanes, [{ lane: 3, holdSeconds: 1.2 }, { lane: 2, holdSeconds: .4 }]);
  assert.deepEqual(moved.notes, before.notes);
  assert.deepEqual(moved.sourceHits, before.sourceHits);
});

test('review state becomes draft only when the current assignment changes or is reset', () => {
  let doc = M.applyBatch(fixture(), ['a', 'b', 'c', 'd'], { type: 'review', reviewed: true });
  doc.review.status = 'complete';
  doc = M.applyBatch(doc, ['b'], { type: 'reason', reason: '这处原排键合适' });
  assert.equal(doc.review.status, 'complete');
  assert.equal(M.moveLane(doc, 'b', 1, 1).review.status, 'complete');
  const moved = M.applyBatch(doc, ['b'], { type: 'shift', delta: -1 });
  assert.equal(moved.review.status, 'draft');
  assert.equal(C.editMap(moved).get('b').reviewed, false);
  assert.equal(C.editMap(moved).get('b').reason, '这处原排键合适');
  const reset = M.applyBatch(moved, ['b'], { type: 'reset' });
  assert.deepEqual(C.editMap(reset).get('b'), { noteId: 'b', lanes: doc.notes[1].lanes, reason: '', reviewed: false });
});

test('visible candidates include long holds whose heads precede the viewport', () => {
  const notes = fixture().notes;
  assert.equal(M.lowerBound(notes, .5), 1);
  assert.equal(M.lowerBound(notes, 100), 4);
  const range = M.visibleRange(notes, 1.6, 600, geometry, 1.2);
  assert.ok(range.start <= 1 && range.end > 1);
  const defaults = M.visibleRange(notes, 1.6, 600, geometry);
  assert.equal(defaults.start, 0);
  assert.throws(() => M.visibleRange(notes, 0, 0, geometry), /缩放/);
});
