const { test } = require('node:test');
const assert = require('node:assert/strict');
const I = require('./adofai-import.js');

test('legacy Pause migration follows the game version boundaries and individual event clamping', () => {
    for (const [version, destination] of [[8,2.5],[9,3],[10,2],[13,2],[16,2],[17,2.5]]) {
        const doc = { settings: { version, bpm:120 }, angleData:[0,180], actions:[{floor:1,eventType:'Pause',duration:2}] };
        assert.deepEqual(I.parseTiming(JSON.stringify(doc)).hits.map(hit=>hit.time), [.5,destination]);
    }
    const doc = { settings:{version:13,bpm:120},angleData:[0,180],actions:[
        {floor:1,eventType:'Pause',duration:.5},{floor:1,eventType:'Pause',duration:2}
    ] };
    assert.deepEqual(I.parseTiming(JSON.stringify(doc)).hits.map(hit=>hit.time), [.5,2]);
});

test('legacy absolute pathData including midspin matches the corresponding angleData', () => {
    for (const settings of [{bpm:120,version:3},{bpm:120,version:15,legacySpriteTiles:true},{bpm:120}]) {
        const actions = [{floor:2,eventType:'Twirl'}];
        const a = I.parseTiming(JSON.stringify({settings,pathData:'R!UQpB',actions}));
        const b = I.parseTiming(JSON.stringify({settings,angleData:[0,999,90,135,15,300],actions}));
        assert.deepEqual(a.hits,b.hits);
    }
});

test('modern relative pathData uses migration semantics rather than the legacy relative language', () => {
    const settings = {bpm:120,version:13};
    const a = I.parseTiming(JSON.stringify({settings,pathData:'R5t9'}));
    const b = I.parseTiming(JSON.stringify({settings,angleData:[0,72,132,102]}));
    assert.deepEqual(a.hits,b.hits);
    assert.throws(()=>I.parseTiming(JSON.stringify({settings:{bpm:120,version:3},pathData:'R5t9'})), /旧版/);
    assert.throws(()=>I.parseTiming(JSON.stringify({settings,pathData:'R?'})), /字符/);
});

test('visual repeats, conditionals and radius changes do not add beats or presses', () => {
    const chart = {settings:{bpm:120},angleData:[0,0,0],actions:[
        {floor:1,eventType:'MoveTrack',eventTag:'visual',angleOffset:45},
        {floor:1,eventType:'RepeatEvents',tag:'visual',repetitions:12,interval:.25},
        {floor:1,eventType:'SetConditionalEvents',perfectTag:'visual'},
        {floor:1,eventType:'ScaleRadius',scale:300}
    ]};
    assert.deepEqual(I.parseTiming(JSON.stringify(chart)).hits.map(hit=>hit.time),[.5,1,1.5]);
    chart.actions.push({floor:1,eventType:'FutureTimingEvent'});
    assert.throws(()=>I.parseTiming(JSON.stringify(chart)), /FutureTimingEvent/);
});
