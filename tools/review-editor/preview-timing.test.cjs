const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const vm = require('node:vm');
const { spawnSync } = require('node:child_process');
const importer = require('./adofai-import.js');

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
const comparableNotes = notes => notes.map(({ tileIndex, lastTileIndex, time, lanes }) => ({ tileIndex, lastTileIndex, time, lanes }));

function readLabChart(text) {
  const directory = path.join(repo, 'tools');
  const html = fs.readFileSync(path.join(directory, 'layout-lab.html'), 'utf8');
  const context = vm.createContext({});
  for (const [, source] of html.matchAll(/<script src="(review-editor\/[^"]+)"\s*><\/script>/g)) {
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
  function run(text, suffix, extra = []) {
    const input = path.join(directory, suffix + '.adofai'), output = path.join(directory, suffix + '.review.json');
    fs.writeFileSync(input, text);
    return { ...invoke([input, '--seconds', '0', '--review-export', output, ...extra]), output };
  }
  for (const [index, sample] of cases.entries()) {
    await t.test(sample.name, async () => {
      const chart = { settings: { bpm: 120, offset: 1065, songFilename: 'synthetic.ogg', ...sample.chart.settings }, actions: [], ...sample.chart };
      chart.settings = { bpm: 120, offset: 1065, songFilename: 'synthetic.ogg', ...sample.chart.settings };
      const text = JSON.stringify(chart), result = run(text, 'case-' + index);
      assert.equal(result.status, 0, result.stdout + result.stderr);
      const reference = JSON.parse(fs.readFileSync(result.output, 'utf8'));
      const actual = await importer.importText(text, 'case-' + index + '.adofai');
      assert.deepEqual(actual.sourceHits, reference.sourceHits, 'time, tile, hold and angle');
      assert.deepEqual(comparableNotes(actual.notes), comparableNotes(reference.notes));
      assert.deepEqual(actual.options, reference.options);
      assert.equal(actual.algorithm, reference.algorithm);
      assert.deepEqual(actual.timing, reference.timing);
      const lab = readLabChart(text);
      const labHits = Array.from(lab.tiles, ({ i, t, hold, ang }) => ({
        tileIndex: i, time: t, holdSeconds: hold, ...(Number.isNaN(ang) ? {} : { angle: ang }),
      }));
      assert.deepEqual(labHits, reference.sourceHits, 'layout-lab uses the same timing and angle evidence');
      assert.equal(lab.song, actual.sourceFile.songFilename);
      assert.equal(lab.offset, actual.sourceFile.offset / 1000);
      assert.equal(lab.pitch, actual.sourceFile.pitch);
    });
  }
  await t.test('both CLI formats preserve repaired legacy JSON metadata', async () => {
    const text = '{"settings":{"bpm":120,"songFilename":"a\\\"b.ogg","song":"raw\r\u000btext"},"angleData":[0,90,180,270],"actions":[] "decorations":[]}';
    const labOutput = path.join(directory, 'legacy-json.lab.json');
    const result = run(text, 'legacy-json', ['--export', labOutput]);
    assert.equal(result.status, 0, result.stdout + result.stderr);
    const reference = JSON.parse(fs.readFileSync(result.output, 'utf8'));
    const actual = await importer.importText(text, 'legacy-json.adofai');
    assert.deepEqual(actual.sourceHits, reference.sourceHits);
    assert.deepEqual(actual.sourceFile, reference.sourceFile);
    const lab = JSON.parse(fs.readFileSync(labOutput, 'utf8'));
    assert.equal(lab.song, reference.sourceFile.songFilename);
    assert.deepEqual(lab.tiles, reference.sourceHits.map(hit => [hit.tileIndex, hit.time * 1000, hit.holdSeconds * 1000, hit.angle ?? null]));
  });
  await t.test('CLI rejects output paths aliasing an input or each other before writing', () => {
    const text = '{"settings":{"bpm":120},"angleData":[0,90,180],"actions":[]}';
    const input = path.join(directory, 'preserve.adofai');
    const reference = path.join(directory, 'reference.json');
    const output = path.join(directory, 'existing.json');
    const alias = filename => directory + path.sep + '.' + path.sep + filename;
    fs.writeFileSync(input, text);
    fs.writeFileSync(reference, 'reference must remain intact');
    fs.writeFileSync(output, 'output must remain intact');
    for (const args of [
      [input, '--export', output, '--review-export', alias('preserve.adofai')],
      [input, reference, '--export', alias('reference.json')],
      [input, '--export', output, '--review-export', alias('existing.json')],
    ]) {
      const result = invoke(args);
      assert.equal(result.status, 2, result.stdout + result.stderr);
      assert.match(result.stderr, /输出路径不能/);
      assert.equal(fs.readFileSync(input, 'utf8'), text);
      assert.equal(fs.readFileSync(reference, 'utf8'), 'reference must remain intact');
      assert.equal(fs.readFileSync(output, 'utf8'), 'output must remain intact');
    }
  });
  await t.test('optional CLI style maps to the current enum and preserves structure', async () => {
    const text = JSON.stringify({ settings: { bpm: 240 }, angleData: [0, 90, 180, 270, 0, 90, 180, 270], actions: [] });
    const result = run(text, 'review-style', ['--review-rules']);
    assert.equal(result.status, 0, result.stdout + result.stderr);
    const reference = JSON.parse(fs.readFileSync(result.output, 'utf8'));
    const actual = await importer.importText(text, 'review-style.adofai', { Style: 'ReviewPreferenceV1' });
    assert.equal(reference.options.Style, 'ReviewPreferenceV1');
    assert.deepEqual(comparableNotes(actual.notes), comparableNotes(reference.notes));
    assert.deepEqual(actual.options, reference.options);
  });
  await t.test('unsupported timing is rejected instead of exporting guessed notes', async () => {
    const text = JSON.stringify({ settings: { bpm: 120 }, angleData: [0, 0, 0], actions: [{ floor: 1, eventType: 'Hold', duration: 1 }] });
    const result = run(text, 'unsupported');
    assert.equal(result.status, 1, result.stdout + result.stderr);
    assert.equal(fs.existsSync(result.output), false);
    assert.match(result.stderr, /Hold/);
    await assert.rejects(importer.importText(text), /Hold/);
    assert.throws(() => readLabChart(text), /Hold/);
  });
});
