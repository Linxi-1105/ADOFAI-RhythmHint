const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const vm = require('node:vm');
const { spawnSync } = require('node:child_process');
const importer = require('./adofai-import.js');
const planner = require('./note-planner.js');

const repo = path.resolve(__dirname, '../..');
const dotnet = process.env.DOTNET_HOST_PATH || 'dotnet';
const sdk = spawnSync(dotnet, ['--version'], { encoding: 'utf8', cwd: repo });
const hasSdk = sdk.status === 0 && Number.parseInt(sdk.stdout, 10) >= 10;
const cases = [
  { name: 'angles, midspins and outgoing angle accumulation', chart: { angleData: [0, 90, 999, 180, 150, 135, 120, 0] } },
  { name: 'absolute pathData and midspins', chart: { pathData: 'RpJ!LUN' } },
  { name: 'modern relative pathData', chart: { pathData: 'R56789hjty!U', settings: { version: 13 } } },
  { name: 'speed changes, Twirl and same-floor Pause', chart: { angleData: [0, 90, 180, 270, 0, 30, 60, 90], actions: [
    { floor: 1, eventType: 'Twirl' },
    { floor: 2, eventType: 'Pause', duration: 2 },
    { floor: 2, eventType: 'SetSpeed', speedType: 'Multiplier', bpmMultiplier: 2 },
    { floor: 4, eventType: 'SetSpeed', beatsPerMinute: 180 },
  ] } },
  { name: 'version 9 Pause migration', chart: { angleData: [0, 180, 0, 180, 0, 0], settings: { version: 9 }, actions: [
    { floor: 1, eventType: 'Pause', duration: 2 }, { floor: 2, eventType: 'Twirl' },
  ] } },
  { name: 'version 13 Pause migration with starting and midspin floors', chart: { angleData: [0, 180, 999, 0, 180, 0, 0], settings: { version: 13 }, actions: [
    { floor: 0, eventType: 'Pause', duration: 2 }, { floor: 1, eventType: 'Pause', duration: 2 },
    { floor: 2, eventType: 'Pause', duration: 8 }, { floor: 7, eventType: 'Pause', duration: 8 },
  ] } },
  { name: 'visual events do not modify timing', chart: { angleData: [0, 0, 0, 0], actions: [
    { floor: 1, eventType: 'RepeatEvents', repetitions: 10, interval: 1 },
    { floor: 1, eventType: 'SetConditionalEvents' }, { floor: 2, eventType: 'ScaleRadius', scale: 200 },
  ] } },
];

// Preview rounds what it writes (session.js: 1 µs and 0.001°; --export: 0.1 ms), so compare within that rounding.
function near(actual, expected, step, label) {
  assert.ok(Math.abs(actual - expected) <= step / 2 + 1e-9, `${label}: ${actual} vs ${expected}`);
}
// rows: [tileIndex, time, hold, angle | null], times scaled by `scale` (1 = seconds, 1000 = ms).
function assertHits(rows, hits, scale, step, label) {
  assert.equal(rows.length, hits.length, label + ': hit count');
  rows.forEach(([tileIndex, time, hold, angle], k) => {
    const hit = hits[k], where = `${label} #${k}`;
    assert.equal(tileIndex, hit.tileIndex, where + ' tile');
    near(time, hit.time * scale, step, where + ' time');
    near(hold, hit.holdSeconds * scale, step, where + ' hold');
    if (hit.angle === undefined) assert.equal(angle, null, where + ' unknown angle');
    else near(angle, hit.angle, .001, where + ' angle');
  });
}
// session.js notes: [tileIndex, lastTileIndex, [[lane, hold], ...]]. Offline charts have no holds, so holds are exactly 0.
const noteRows = notes => notes.map(n => [n.tileIndex, n.lastTileIndex, n.lanes.map(l => [l.lane, l.holdSeconds])]);

function readLabChart(text) {
  const directory = path.join(repo, 'tools');
  const html = fs.readFileSync(path.join(directory, 'layout-lab.html'), 'utf8');
  const context = vm.createContext({});
  for (const [, source] of html.matchAll(/<script src="(planner-js\/[^"]+)"\s*><\/script>/g)) {
    vm.runInContext(fs.readFileSync(path.join(directory, source), 'utf8'), context);
  }
  const adapter = html.match(/^function parseAdofai\([^]*?^}/m);
  assert.ok(adapter, 'layout-lab import must use its actual reader');
  vm.runInContext(adapter[0], context);
  return context.parseAdofai(text);
}

test('Preview CLI and both browser entry points agree on synthetic timing, angles and full plans', { skip: !hasSdk && 'Requires .NET 10; set DOTNET_HOST_PATH or add dotnet to PATH', timeout: 180000 }, async t => {
  const prefix = path.join(os.tmpdir(), 'rhythmhint-preview-parity-');
  const directory = fs.mkdtempSync(prefix);
  t.after(() => {
    // The only deleted directory is this test's unique, freshly created temp directory.
    assert.ok(path.resolve(directory).startsWith(path.resolve(prefix)));
    fs.rmSync(directory, { recursive: true, force: true });
  });
  function invoke(args) {
    return spawnSync(dotnet, ['run', 'tools/Preview.cs', '--', ...args],
      { cwd: repo, encoding: 'utf8', timeout: 90000, maxBuffer: 4 * 1024 * 1024 });
  }
  // Writes the layout-lab export (--export) and the editor session (--editor-session) for one chart.
  function run(text, suffix, extra = []) {
    const input = path.join(directory, suffix + '.adofai'), lab = path.join(directory, suffix + '.lab.json');
    const script = path.join(directory, suffix + '-session', 'session.js');
    fs.writeFileSync(input, text);
    const result = invoke([input, '--seconds', '0', '--export', lab, '--editor-session', path.dirname(script), ...extra]);
    const read = () => {
      const context = vm.createContext({ window: {} });
      vm.runInContext(fs.readFileSync(script, 'utf8'), context);
      return { exported: JSON.parse(fs.readFileSync(lab, 'utf8')), session: JSON.parse(JSON.stringify(context.window.RHYTHMHINT_SESSION)) };
    };
    return { ...result, lab, script, read };
  }
  for (const [index, sample] of cases.entries()) {
    await t.test(sample.name, () => {
      const chart = { settings: { bpm: 120, offset: 1065, songFilename: 'synthetic.ogg', ...sample.chart.settings }, actions: [], ...sample.chart };
      chart.settings = { bpm: 120, offset: 1065, songFilename: 'synthetic.ogg', ...sample.chart.settings };
      const text = JSON.stringify(chart), result = run(text, 'case-' + index);
      assert.equal(result.status, 0, result.stdout + result.stderr);
      const { exported, session } = result.read();
      const { hits } = importer.parseTiming(text);
      assertHits(session.fingering.hits, hits, 1, 1e-6, 'session hits');
      assertHits(exported.tiles, hits, 1000, .1, 'lab export tiles');
      assert.deepEqual(session.fingering.notes, noteRows(planner.plan(hits)), 'default plan');
      assert.deepEqual(session.references.standard, noteRows(planner.plan(hits, { Style: 'TimeReset' })), 'standard plan');
      assert.deepEqual(session.references.alternate, noteRows(planner.plan(hits, { Style: 'ReviewPreferenceV1' })), 'alternate plan');
      const lab = readLabChart(text);
      const labHits = Array.from(lab.tiles, ({ i, t, hold, ang }) => ({
        tileIndex: i, time: t, holdSeconds: hold, ...(Number.isNaN(ang) ? {} : { angle: ang }),
      }));
      assert.deepEqual(labHits, hits, 'layout-lab uses the same timing and angle evidence');
      assert.equal(lab.beats.length, exported.beats.length, 'beat count');
      lab.beats.forEach((beat, k) => {
        near(exported.beats[k][0], beat.t * 1000, .1, 'beat #' + k);
        assert.equal(exported.beats[k][1], beat.m ? 1 : 0, 'measure line #' + k);
      });
      assert.equal(lab.song, exported.song);
      // 音乐时间 = 砖块时间 + offset − 起始一拍（2026-11-18 按起音对齐实测，见 docs/开发说明.md）
      assert.ok(Math.abs(lab.offset - (exported.offset / 1000 - 60 / exported.bpm)) < 1e-9, 'music offset minus one starting beat');
      assert.equal(lab.pitch, exported.pitch);
    });
  }
  await t.test('the CLI and the JS reader repair legacy JSON the same way', () => {
    const text = '{"settings":{"bpm":120,"songFilename":"a\\\"b.ogg","song":"raw\r\u000btext"},"angleData":[0,90,180,270],"actions":[] "decorations":[]}';
    const result = run(text, 'legacy-json');
    assert.equal(result.status, 0, result.stdout + result.stderr);
    const { exported, session } = result.read();
    const timing = importer.parseTiming(text);
    assert.equal(timing.settings.songFilename, 'a"b.ogg');
    assert.equal(exported.song, timing.settings.songFilename);
    assert.equal(session.fingering.song, timing.settings.song);
    assert.equal(readLabChart(text).song, exported.song);
    assertHits(session.fingering.hits, timing.hits, 1, 1e-6, 'session hits');
    assertHits(exported.tiles, timing.hits, 1000, .1, 'lab export tiles');
  });
  await t.test('legacy duplicate object separators preserve chart strings', () => {
    const text = '{"settings":{"requiredMods":[],,"bpm":120,"song":"keep ,,"},"angleData":[0,90],"actions":[]}';
    const result = run(text, 'duplicate-comma');
    assert.equal(result.status, 0, result.stderr);
    assert.equal(result.read().session.fingering.song, 'keep ,,');
    assert.equal(importer.parseTiming(text).settings.song, 'keep ,,');
  });
  await t.test('PositionTrack moves only its requested floors in world units', () => {
    const pos = (floor, positionOffset, extra) => ({ floor, eventType: 'PositionTrack', positionOffset, ...extra });
    const actions = [
      pos(1, [1, 2]), pos(2, [-1, 1], { justThisTile: true }),
      pos(3, [0, 0], { relativeTo: [2, 'Start'] }),
      pos(4, [20, 20], { editorOnly: true }),
      pos(5, [0, 1], { relativeTo: [0, 'End'], justThisTile: true })
    ];
    const result = run(JSON.stringify({ settings: { bpm: 120 }, angleData: Array(6).fill(0), actions }), 'position');
    assert.equal(result.status, 0, result.stderr);
    assert.deepEqual(result.read().session.path.map(row => row.slice(0, 2)),
      [[0, 0], [3, 3], [3, 4.5], [3, 4.5], [4.5, 4.5], [9, 1.5], [7.5, 4.5]]);
  });
  await t.test('ScaleRadius changes the incoming distance independently of PositionTrack offsets', () => {
    const actions = [
      { floor: 1, eventType: 'ScaleRadius', scale: 50 },
      { floor: 2, eventType: 'PositionTrack', positionOffset: [0, 1], justThisTile: true },
      { floor: 3, eventType: 'ScaleRadius', scale: 100 }
    ];
    const result = run(JSON.stringify({ settings: { bpm: 120 }, angleData: [0, 90, 0, 0], actions }), 'radius');
    assert.equal(result.status, 0, result.stderr);
    assert.deepEqual(result.read().session.path.map(row => row.slice(0, 2)),
      [[0, 0], [0.75, 0], [0.75, 2.25], [2.25, 0.75], [3.75, 0.75]]);
  });
  await t.test('a midspin returns to the previous center without advancing chart time', () => {
    const result = run('{"settings":{"bpm":120},"angleData":[0,90,999,0],"actions":[]}', 'midspin-path');
    assert.equal(result.status, 0, result.stderr);
    const { session } = result.read();
    assert.deepEqual(session.path.map(row => row.slice(0, 2)), [[0, 0], [1.5, 0], [1.5, 1.5], [1.5, 0], [3, 0]]);
    assert.equal(session.path[2][2], session.path[3][2]);
  });
  await t.test('CLI rejects output paths aliasing an input before writing', () => {
    const text = '{"settings":{"bpm":120},"angleData":[0,90,180],"actions":[]}';
    const input = path.join(directory, 'preserve.adofai');
    const reference = path.join(directory, 'reference.json');
    const alias = filename => directory + path.sep + '.' + path.sep + filename;
    fs.writeFileSync(input, text);
    fs.writeFileSync(reference, 'reference must remain intact');
    for (const args of [
      [input, '--export', alias('preserve.adofai')],
      [input, reference, '--export', alias('reference.json')],
    ]) {
      const result = invoke(args);
      assert.equal(result.status, 2, result.stdout + result.stderr);
      assert.match(result.stderr, /输出路径不能/);
      assert.equal(fs.readFileSync(input, 'utf8'), text);
      assert.equal(fs.readFileSync(reference, 'utf8'), 'reference must remain intact');
    }
  });
  await t.test('--alternate switches the CLI plan to the same alternate style as the JS planner', () => {
    // A 5-note run at 62.5 ms (90° at 480 BPM, inside the default 65 ms roll line): the standard style rolls, the alternate style trades index fingers.
    const text = JSON.stringify({ settings: { bpm: 480 }, angleData: [0, 180, 270, 0, 90, 180, 0], actions: [] });
    const result = run(text, 'alternate-style', ['--alternate']);
    assert.equal(result.status, 0, result.stdout + result.stderr);
    const { session } = result.read();
    const { hits } = importer.parseTiming(text);
    const alternate = noteRows(planner.plan(hits, { Style: 'ReviewPreferenceV1' }));
    assert.notDeepEqual(alternate, noteRows(planner.plan(hits)), 'the sample must tell the two styles apart');
    assert.deepEqual(session.fingering.notes, alternate);
    assert.deepEqual(session.references.alternate, alternate);
  });
  await t.test('unsupported timing is rejected instead of exporting guessed notes', () => {
    const text = JSON.stringify({ settings: { bpm: 120 }, angleData: [0, 0, 0], actions: [{ floor: 1, eventType: 'Hold', duration: 1 }] });
    const result = run(text, 'unsupported');
    assert.equal(result.status, 1, result.stdout + result.stderr);
    assert.equal(fs.existsSync(result.lab), false);
    assert.equal(fs.existsSync(result.script), false);
    assert.match(result.stderr, /Hold/);
    assert.throws(() => importer.parseTiming(text), /Hold/);
    assert.throws(() => readLabChart(text), /Hold/);
  });
});
