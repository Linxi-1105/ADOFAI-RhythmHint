const { test } = require('node:test');
const assert = require('node:assert/strict');
const C = require('./core.js');
function fixture() {
  return { format: 'rhythmhint-review', schemaVersion: 1, chartId: 'test-1', name: '测试', source: 'game', algorithm: 'NotePlanner/1.0.0', exportedAt: '2026-11-17T00:00:00Z',
    options: { ChordWindow: .035, ResetWindow: .165, ChordFocusWindow: .45, JackWindow: .15, UseRoll: true, OuterRoll: true, LeftHanded: false }, keyLabels: ['D', 'F', 'J', 'K'],
    notes: [{ id: 'a', tileIndex: 1, lastTileIndex: 1, time: -.1, lanes: [{ lane: 2, holdSeconds: 0 }] },
      { id: 'b', tileIndex: 2, lastTileIndex: 3, time: .1, lanes: [{ lane: 2, holdSeconds: .3 }, { lane: 3, holdSeconds: 0 }] },
      { id: 'c', tileIndex: 4, lastTileIndex: 4, time: .6, lanes: [{ lane: 1, holdSeconds: 0 }] }], review: { status: 'draft', comment: '', edits: [] } };
}
test('source angle participates in draft identity and legacy files remain unchanged', () => {
  const old = fixture(), snapshot = JSON.stringify(old);
  assert.deepEqual(C.validate(old), old);
  assert.equal(JSON.stringify(old), snapshot);
  const first = fixture(); first.sourceHits = [{ tileIndex: 1, time: 0, holdSeconds: 0, angle: 30 }];
  const second = C.clone(first); second.sourceHits[0].angle = 20;
  assert.notEqual(C.baseline(first), C.baseline(second));
  assert.throws(() => C.restore(second, { baseline: C.baseline(first), review: first.review }), /不同/);
  second.sourceHits[0].angle = Infinity; assert.throws(() => C.validate(second));
});
test('baseline survives edit, export, JSON roundtrip and re-import', () => {
  const source = fixture(), original = C.clone(source);
  const edited = C.update(source, 'a', { lanes: [{ lane: 1, holdSeconds: 0 }], reason: '双押前换手', reviewed: true });
  const imported = C.validate(JSON.parse(JSON.stringify(edited)));
  assert.deepEqual(source, original);
  assert.deepEqual(imported.notes, original.notes);
  assert.equal(C.baseline(imported), C.baseline(original));
  assert.deepEqual(C.stats(imported), { total: 3, reviewed: 1, changed: 1, missingReasons: 0 });
});
test('reassigning a chord preserves both hold lengths and offers explicit finger swap', () => {
  const source = fixture(), chord = source.notes[1];
  const assigned = C.assign(chord, [0, 1]);
  assert.deepEqual(assigned, [{ lane: 0, holdSeconds: .3 }, { lane: 1, holdSeconds: 0 }]);
  const edited = C.update(source, 'b', { lanes: assigned });
  assert.doesNotThrow(() => C.update(edited, 'b', { lanes: [{ lane: 0, holdSeconds: 0 }, { lane: 1, holdSeconds: .3 }] }));
  assert.throws(() => C.update(source, 'b', { lanes: [{ lane: 0, holdSeconds: .2 }, { lane: 1, holdSeconds: 0 }] }), /长按/);
  assert.throws(() => C.update(source, 'b', { lanes: [{ lane: 0, holdSeconds: .3 }] }), /增删/);
});
test('invalid lane, duplicate IDs, duplicate edits and unknown edit are rejected', () => {
  const variants = [
    d => d.notes[0].lanes[0].lane = 4,
    d => d.notes[1].id = 'a',
    d => d.notes[1].lanes[1].lane = 2,
    d => d.notes[0].time = Infinity,
    d => d.notes[0].time = 1,
    d => d.notes[0].lanes[0].holdSeconds = -1,
    d => d.schemaVersion = 2,
    d => d.options.ResetWindow = NaN,
    d => d.review.edits.push({ noteId: 'absent', lanes: [{ lane: 0, holdSeconds: 0 }], reason: '', reviewed: false }),
    d => { const e = { noteId: 'a', lanes: [{ lane: 0, holdSeconds: 0 }], reason: '', reviewed: false }; d.review.edits.push(e, e); }
  ];
  for (const mutate of variants) { const source = fixture(); mutate(source); assert.throws(() => C.validate(source)); }
});
test('negative and simultaneous note times are supported', () => {
  const source = fixture(); source.notes[1].time = -.1; assert.equal(C.validate(source).notes[0].time, -.1);
});
test('draft restore verifies baseline, including exact holds and planner options', () => {
  const source = fixture(), revised = C.update(source, 'a', { reviewed: true });
  const saved = { baseline: C.baseline(source), review: revised.review };
  assert.equal(C.stats(C.restore(source, saved)).reviewed, 1);
  const other = fixture(); other.notes[1].lanes[0].holdSeconds = .4;
  assert.throws(() => C.restore(other, saved), /不同/);
  other.notes[1].lanes[0].holdSeconds = .3; other.options.LeftHanded = true;
  assert.throws(() => C.restore(other, saved), /不同/);
});
test('summary includes reason, adjacent original/revised notes and actual gap', () => {
  const revised = C.update(fixture(), 'a', { lanes: [{ lane: 1, holdSeconds: 0 }], reason: '双押前换手', reviewed: true });
  const summary = C.summarize(revised, 'test-time');
  assert.equal(summary.changes.length, 1);
  assert.equal(summary.changes[0].gapBeforeMs, null);
  assert.equal(summary.changes[0].gapAfterMs, 200);
  assert.equal(summary.changes[0].next.id, 'b');
  assert.equal(summary.changes[0].reason, '双押前换手');
  assert.deepEqual(summary.transitions, [{ transition: 'J → F', count: 1 }]);
  assert.match(summary.interpretation, /不是已验证/);
});
test('unchanged reviewed notes count as evidence, and completion requires all notes', () => {
  let source = C.update(fixture(), 'a', { reviewed: true });
  assert.equal(C.stats(source).changed, 0);
  assert.throws(() => C.validate({ ...source, review: { ...source.review, status: 'complete' } }), /全部音符/);
  source = C.update(C.update(source, 'b', { reviewed: true }), 'c', { reviewed: true });
  source.review.status = 'complete'; assert.doesNotThrow(() => C.validate(source));
  const reopened = C.update(source, 'a', { reviewed: false }); assert.equal(reopened.review.status, 'draft');
});
test('restoring original lanes leaves audit reasons and review state independently expressible', () => {
  const source = fixture(); let revised = C.update(source, 'a', { lanes: [{ lane: 1, holdSeconds: 0 }], reason: '先试换手', reviewed: true });
  revised = C.update(revised, 'a', { lanes: source.notes[0].lanes, reason: '', reviewed: false });
  assert.deepEqual(C.stats(revised), { total: 3, reviewed: 0, changed: 0, missingReasons: 0 });
});
test('lane comparison ignores JSON property order and preserves extra export metadata', () => {
  const source = fixture(); source.sourceHits = [{ tileIndex: 1, time: -.1, holdSeconds: 0 }];
  const revised = C.update(source, 'a', { lanes: [{ holdSeconds: 0, lane: 2 }], reviewed: true });
  assert.equal(C.stats(revised).changed, 0);
  assert.deepEqual(revised.sourceHits, source.sourceHits);
});
test('drafts cannot cross different raw timings hidden by the same pseudo chord', () => {
  const source = fixture();
  source.sourceHits = [{ tileIndex: 1, time: -.1, holdSeconds: 0 }, { tileIndex: 2, time: .1, holdSeconds: .3 }, { tileIndex: 3, time: .125, holdSeconds: 0 }, { tileIndex: 4, time: .6, holdSeconds: 0 }];
  const saved = { baseline: C.baseline(source), review: C.update(source, 'b', { reviewed: true }).review };
  const changed = C.clone(source); changed.sourceHits[2].time = .13;
  assert.throws(() => C.restore(changed, saved), /不同/);
  const propertyOrder = C.clone(source); propertyOrder.sourceHits[0] = { holdSeconds: 0, time: -.1, tileIndex: 1 };
  assert.equal(C.stats(C.restore(propertyOrder, saved)).reviewed, 1);
});
test('invalid original hit data is rejected before it can become replay evidence', () => {
  for (const hits of [[], [{ tileIndex: 1, time: NaN, holdSeconds: 0 }], [{ tileIndex: 1, time: 0, holdSeconds: -1 }], [{ tileIndex: 1, time: 0, holdSeconds: 0 }, { tileIndex: 1, time: 1, holdSeconds: 0 }], [{ tileIndex: 1, time: 1, holdSeconds: 0 }, { tileIndex: 2, time: 0, holdSeconds: 0 }]]) {
    assert.throws(() => C.validate({ ...fixture(), sourceHits: hits }), /原始击打/);
  }
});
