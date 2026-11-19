const { test } = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const I = require('./adofai-import.js');
const C = require('./core.js');

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
        assert.throws(() => I.parseTiming(JSON.stringify({ settings: { bpm: 120 }, angleData: [0,0], actions: [{ floor: 1, eventType: kind }] })), error => error.message.includes(kind) && error.message.includes('F8'));
    }
    for (const action of [
        { floor: 1.5, eventType: 'Twirl' }, { floor: 1, eventType: 'Twirl', angleOffset: .125 },
        { floor: 1, eventType: 'Twirl', eventTag: 'conditional' }, { floor: 1, eventType: 'Twirl', duration: 1 },
        { floor: 1, eventType: 'SetSpeed', speedType: 'Multiplier', bpmMultiplier: 0 },
        { floor: 1, eventType: 'SetSpeed', speedType: 'Other', beatsPerMinute: 120 }
    ]) assert.throws(() => I.parseTiming(JSON.stringify({ settings: { bpm: 120 }, angleData: [0,0], actions: [action] })));
});
test('malformed angleData, BPM and pathData fail with an actionable import route', () => {
    for (const value of [null, [], { settings: { bpm: 0 }, angleData: [0] }, { settings: { bpm: 120 }, angleData: [] },
        { settings: { bpm: 120 }, angleData: [0,'90'] },
        { settings: { bpm: 120 }, pathData: 'R?R' }, { settings: { bpm: 120 }, angleData: [0], actions: null }
    ]) assert.throws(() => I.parseTiming(JSON.stringify(value)), /F8/);
});
test('portable SHA-256 agrees with standard digest across padding and unicode cases', () => {
    for (const input of ['', 'abc', 'a'.repeat(55), 'a'.repeat(56), 'a'.repeat(64), 'a'.repeat(1000), '节奏🎵'.repeat(70)]) {
        assert.equal(I.sha256Hex(input), crypto.createHash('sha256').update(input).digest('hex'));
    }
});
test('direct import makes deterministic, editable documents and preserves music metadata', async () => {
    const text = JSON.stringify({ settings: { bpm: 120, songFilename: 'music.ogg', offset: -150, pitch: 100 }, angleData: [0,0,0] });
    const first = await I.importText(text, 'one.adofai'), second = await I.importText(text, 'renamed.adofai');
    assert.equal(first.chartId, second.chartId);
    assert.match(first.chartId, /^[0-9a-f]{64}$/);
    assert.equal(first.algorithm, require('./note-planner.js').version);
    assert.equal(first.source, 'offline-approximation');
    assert.deepEqual(first.sourceFile, { name: 'one.adofai', bpm: 120, songFilename: 'music.ogg', offset: -150, pitch: 100 });
    assert.equal(first.notes.length, 3); assert.equal(first.sourceHits.length, 3);
    const left = await I.importText(text, 'one.adofai', { LeftHanded: true });
    assert.notEqual(left.chartId, first.chartId);
    const edited = C.update(first, first.notes[0].id, { lanes: [{ lane: 0, holdSeconds: 0 }], reason: '直接导入审核', reviewed: true });
    assert.deepEqual(C.validate(JSON.parse(JSON.stringify(edited))).sourceHits, first.sourceHits);
});
