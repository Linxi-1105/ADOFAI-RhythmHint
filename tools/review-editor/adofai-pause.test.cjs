const { test } = require('node:test');
const assert = require('node:assert/strict');
const I = require('./adofai-import.js');
const C = require('./core.js');

test('a Pause chart can be imported, edited and reopened directly', async () => {
    const source = JSON.stringify({ settings: { bpm: 120 }, angleData: [0,0,0],
        actions: [{ floor: 1, eventType: 'Pause', duration: 2 }] });
    const doc = await I.importText(source, 'pause-regression.adofai');
    assert.equal(doc.sourceHits.length, 3);
    assert.deepEqual(doc.sourceHits.map(hit => hit.time), [.5, 2, 2.5]);
    const edited = C.update(doc, doc.notes[0].id, {
        lanes: [{ lane: 0, holdSeconds: 0 }], reason: '直接导入后的改键', reviewed: true
    });
    const reopened = C.validate(JSON.parse(JSON.stringify(edited)));
    assert.equal(C.stats(reopened).changed, 1);
    assert.deepEqual(reopened.sourceHits, doc.sourceHits);
});

const times = (actions, angles = [0,0,0]) => I.parseTiming(JSON.stringify({
    settings: { bpm: 120 }, angleData: angles, actions
})).hits.map(hit => hit.time);

test('Pause accumulates on departure using the same floor speed, regardless of event order', () => {
    const pause = { floor: 1, eventType: 'Pause', duration: 2 };
    const speed = { floor: 1, eventType: 'SetSpeed', speedType: 'Multiplier', bpmMultiplier: 2 };
    assert.deepEqual(times([pause, speed]), [.5, 1.25, 1.5]);
    assert.deepEqual(times([speed, pause]), [.5, 1.25, 1.5]);
    assert.deepEqual(times([pause, { ...pause, duration: .5 }]), [.5, 2.25, 2.75]);
    for (const angleCorrectionDir of [-1, 0, 1]) {
        assert.deepEqual(times([{ ...pause, countdownTicks: 8, angleCorrectionDir }]), [.5, 2, 2.5]);
    }
});

test('Preview approximation includes starting Pause and ignores midspin and terminal departure', () => {
    assert.deepEqual(times([{ floor: 0, eventType: 'Pause', duration: 8 }]), [4.5, 5, 5.5]);
    assert.deepEqual(times([{ floor: 1, eventType: 'Pause', duration: 8 }], [0,999,90]), [.5, 1.25]);
    assert.deepEqual(times([{ floor: 3, eventType: 'Pause', duration: 8 }]), [.5, 1, 1.5]);
});

test('negative Pause can shorten an interval but cannot make the offline timeline run backwards', () => {
    assert.deepEqual(times([{ floor: 1, eventType: 'Pause', duration: -.5 }]), [.5, .75, 1.25]);
    assert.deepEqual(times([{ floor: 1, eventType: 'Pause', duration: -1 }]), [.5, .5, 1]);
    assert.throws(() => times([{ floor: 1, eventType: 'Pause', duration: -2 }]), /Pause.*间隔/);
    assert.deepEqual(times([{ floor: 1, eventType: 'Pause' }]), [.5, 1, 1.5]);
    for (const duration of [null, '2']) {
        assert.throws(() => times([{ floor: 1, eventType: 'Pause', duration }]), /Pause.duration/);
    }
});
