const { test } = require('node:test');
const assert = require('node:assert/strict');
const I = require('./adofai-import.js');
const P = require('./note-planner.js');

test('a Pause chart is read directly and every hit still gets its own note', () => {
    const source = JSON.stringify({ settings: { bpm: 120 }, angleData: [0,0,0],
        actions: [{ floor: 1, eventType: 'Pause', duration: 2 }] });
    const timing = I.parseTiming(source);
    assert.deepEqual(timing.hits, [
        { tileIndex: 1, time: .5, holdSeconds: 0, angle: 180 },
        { tileIndex: 2, time: 2, holdSeconds: 0, angle: 180 },
        { tileIndex: 3, time: 2.5, holdSeconds: 0, angle: 180 },
    ]);
    const notes = P.plan(timing.hits);
    assert.deepEqual(notes.map(note => [note.tileIndex, note.lastTileIndex, note.time]), timing.hits.map(hit => [hit.tileIndex, hit.tileIndex, hit.time]));
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
