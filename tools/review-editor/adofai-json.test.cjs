const { test } = require('node:test');
const assert = require('node:assert/strict');
const { parseJson } = require('./adofai-import.js');

test('game-compatible raw control characters preserve their exact string values', () => {
    const controls = Array.from({ length: 32 }, (_, code) => String.fromCharCode(code)).join('');
    const parsed = parseJson('{"text":"' + controls + '","next":42}');
    assert.equal(parsed.text, controls);
    assert.equal(parsed.next, 42);
    assert.deepEqual(JSON.parse(JSON.stringify(parsed)), parsed);
});

test('raw CR and vertical tabs in chart lyrics and comments preserve escaped newlines', () => {
    const input = '{"decorations":[{"eventType":"AddText","decText":"line one\r\\n\r\\n"}],'
        + '"actions":[{"eventType":"EditorComment","comment":"first\u000bsecond\u000b"}]}';
    const parsed = parseJson(input);
    assert.equal(parsed.decorations[0].decText, 'line one\r\n\r\n');
    assert.equal(parsed.actions[0].comment, 'first\u000bsecond\u000b');
});

test('legacy array-to-property separators are accepted only at object boundaries', () => {
    const input = '\uFEFF{ "settings":{"bpm":120}, "angleData":[0,90],'
        + '"actions":[{"floor":1,"eventType":"Twirl",},] /* legacy writer */ '
        + '"decorations":[], "nested":{"values":[1,2] "quoted\\\"key":[3]}, }';
    assert.deepEqual(parseJson(input), {
        settings: { bpm: 120 }, angleData: [0, 90],
        actions: [{ floor: 1, eventType: 'Twirl' }], decorations: [],
        nested: { values: [1, 2], 'quoted"key': [3] }
    });
});

test('quoted delimiters, comments, escaped quotes and backslashes remain literal', () => {
    const values = ['] "decorations": [', '// line /* block */ ,}', '\\u000b',
        'a\\b\\c.ogg', 'quote " then slash \\', 'literal \\ then actual\rcontrol'];
    const input = JSON.stringify({ values });
    assert.deepEqual(parseJson(input), { values });
    assert.deepEqual(parseJson('{"array":[[],"decorations",{"x":1}]}'),
        { array: [[], 'decorations', { x: 1 }] });
});

test('normalization does not hide malformed structure or evaluate chart code', () => {
    for (const input of [
        '{"actions":[] "decorations" []}', // missing colon
        '{"actions":[] "decorations":[]', // missing object close
        '{"actions":[} "decorations":[]}', // mismatched containers
        '{"actions":[1,,]}', // missing array element
        '{"a":1 "b":2}', // arbitrary missing scalar separator is not normalized
        '[[] "decorations":[]]', // property syntax inside an array
        '{"text":"bad\\q"}', // invalid escape
        '{"text":"bad\\\n"}', // escaped raw newline remains invalid
        '{"value":(()=>{throw new Error("executed")})()}',
        'globalThis.__chartCodeExecuted = true'
    ]) assert.throws(() => parseJson(input), /无法读取谱面 JSON/);
    assert.equal(globalThis.__chartCodeExecuted, undefined);
});
