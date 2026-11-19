const { test } = require('node:test');
const assert = require('node:assert/strict');
const I = require('./adofai-import.js');
const P = require('./note-planner.js');
// Every rejection tells the reader this is an offline approximation of the game's timing.
const offlineHint = /离线读谱只是近似/;

test('midspin skips its actual floor while retaining destination and final tile', () => {
    const timing = I.parseTiming('{"settings":{"bpm":120},"angleData":[0,999,90]}');
    assert.deepEqual(timing.hits, [{ tileIndex: 2, time: .5, holdSeconds: 0, angle: 180 }, { tileIndex: 3, time: 1.25, holdSeconds: 0, angle: 270 }]);
    assert.deepEqual(I.parseTiming('{"settings":{"bpm":120},"angleData":[0,999]}').hits, [{ tileIndex: 2, time: .5, holdSeconds: 0, angle: 180 }]);
});
test('speed and twirl match the reference outgoing interval semantics', () => {
    const timing = I.parseTiming(JSON.stringify({ settings: { bpm: 120 }, angleData: [0,90,0], actions: [
        { floor: 1, eventType: 'Twirl' }, { floor: 1, eventType: 'SetSpeed', speedType: 'Multiplier', bpmMultiplier: 2 },
        { floor: 2, eventType: 'SetSpeed', speedType: 'Bpm', beatsPerMinute: 120 }
    ] }));
    assert.deepEqual(timing.hits.map(hit => hit.time), [.5,.875,1.125]);
    assert.deepEqual(timing.speeds, [{ time: 0, speed: 1 }, { time: .5, speed: 2 }, { time: .875, speed: 1 }]);
});
test('BOM, comments and trailing commas do not alter quoted content', () => {
    const input = '\uFEFF{ "settings": {"bpm":120,"songFilename":"a,} // not a comment.ogg",}, /* \" */ "angleData":[0,0,], "actions":[], }';
    const parsed = I.parseJson(input);
    assert.equal(parsed.settings.songFilename, 'a,} // not a comment.ogg');
    assert.deepEqual(I.parseTiming(input).hits.map(hit => hit.time), [.5,1]);
    assert.throws(() => I.parseJson('{"a":[1,,]}'), /JSON/);
    assert.throws(() => I.parseJson('{/* not closed'), /注释/);
    assert.throws(() => I.parseJson('globalThis.pwned = true'), /JSON/);
});
test('unknown events and unsupported timing cannot quietly generate a misleading chart', () => {
    for (const kind of ['Hold','FreeRoam','MultiPlanet','AutoPlayTiles','FutureEvent']) {
        assert.throws(() => I.parseTiming(JSON.stringify({ settings: { bpm: 120 }, angleData: [0,0], actions: [{ floor: 1, eventType: kind }] })), error => error.message.includes(kind) && offlineHint.test(error.message));
    }
    for (const action of [
        { floor: 1.5, eventType: 'Twirl' }, { floor: 1, eventType: 'Twirl', angleOffset: .125 },
        { floor: 1, eventType: 'Twirl', eventTag: 'conditional' }, { floor: 1, eventType: 'Twirl', duration: 1 },
        { floor: 1, eventType: 'SetSpeed', speedType: 'Multiplier', bpmMultiplier: 0 },
        { floor: 1, eventType: 'SetSpeed', speedType: 'Other', beatsPerMinute: 120 }
    ]) assert.throws(() => I.parseTiming(JSON.stringify({ settings: { bpm: 120 }, angleData: [0,0], actions: [action] })));
});
test('malformed angleData, BPM and pathData fail with the offline-approximation hint', () => {
    for (const value of [null, [], { settings: { bpm: 0 }, angleData: [0] }, { settings: { bpm: 120 }, angleData: [] },
        { settings: { bpm: 120 }, angleData: [0,'90'] },
        { settings: { bpm: 120 }, pathData: 'R?R' }, { settings: { bpm: 120 }, angleData: [0], actions: null }
    ]) assert.throws(() => I.parseTiming(JSON.stringify(value)), offlineHint);
});
test('offline timing is deterministic, keeps music settings and feeds the shared planner directly', () => {
    const settings = { bpm: 120, songFilename: 'music.ogg', offset: -150, pitch: 100 };
    const text = JSON.stringify({ settings, angleData: [0,0,0] });
    const first = I.parseTiming(text), second = I.parseTiming(text);
    assert.deepEqual(first, second);
    assert.deepEqual(first.settings, settings);
    assert.equal(first.bpm, 120); assert.equal(first.crotchet, .5);
    assert.deepEqual(first.hits, [1, 2, 3].map(tileIndex => ({ tileIndex, time: tileIndex / 2, holdSeconds: 0, angle: 180 })));
    // Hits survive a JSON round trip (no NaN), so they can be handed to the planner or stored as-is.
    assert.deepEqual(JSON.parse(JSON.stringify(first.hits)), first.hits);
    const right = P.plan(first.hits), left = P.plan(first.hits, { LeftHanded: true });
    assert.deepEqual(right.map(note => note.tileIndex), [1, 2, 3]);
    assert.deepEqual(left.map(note => note.lanes.map(l => 3 - l.lane)), right.map(note => note.lanes.map(l => l.lane)));
});
