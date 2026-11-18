const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const { spawnSync } = require('node:child_process');
const P = require('./note-planner.js');
const C = require('./core.js');
const M = require('./studio-model.js');
const root = path.resolve(__dirname, '../..');
const normalize = value => JSON.parse(JSON.stringify(value));
const comparable = notes => notes.map(({ tileIndex, lastTileIndex, time, lanes }) => ({ tileIndex, lastTileIndex, time, lanes }));
function run(command, args) {
  const result = spawnSync(command, args, { cwd: root, encoding: 'utf8', timeout: 180000, windowsHide: true });
  assert.equal(result.status, 0, `${command} failed. Install .NET 10 / Python or set DOTNET_HOST_PATH / PYTHON.\n${result.error || ''}\n${result.stdout}\n${result.stderr}`);
}
function layoutContext(html, planner) {
  // Extract whole top-level functions by the next top-level closing brace. Nested
  // braces remain indented in the source; this executes the actual wrapper, not a copy.
  const names = ['toMoments', 'plannerOptions', 'planMod'];
  const parts = names.map(name => {
    const match = html.match(new RegExp('function ' + name + '\\([^]*?^}', 'm'));
    assert.ok(match, name + ' must remain testable'); return match[0];
  });
  const params = html.match(/const P = \{[^]*?^};/m);
  assert.ok(params);
  const context = vm.createContext({ NotePlanner: planner });
  vm.runInContext(params[0] + '\nconst EPS=1e-6, ANGLE_TOL=.1;\n' + parts.join('\n') + '\nglobalThis.parameters=P;', context);
  return context;
}
const fields = { ChordWindow:'chordWindow', ChordAngle:'chordAngle', ChordMaxGap:'chordGap', ResetWindow:'reset',
  ChordFocusWindow:'focus', JackWindow:'jack', UseRoll:'roll', OuterRoll:'outer', LeftHanded:'left', ChordsOnOtherHand:'chordOther',
  Style:'style', ReviewShortMinGap:'reviewShortMinGap', ReviewShortMaxGap:'reviewShortMaxGap', ReviewShortMinNotes:'reviewShortMinNotes',
  ReviewShortMaxNotes:'reviewShortMaxNotes', ReviewTimingTolerance:'reviewTimingTolerance' };

test('current C# planner, browser planner and layout-lab agree per note across synthetic boundaries and seeded random charts', () => {
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'rhythmhint-parity-'));
  try {
    const output = path.join(temporary, 'fixtures.json');
    run(process.env.DOTNET_HOST_PATH || 'dotnet', ['run', path.join(root, 'tools/ExportPlannerParity.cs'), '--', output]);
    const cases = JSON.parse(fs.readFileSync(output, 'utf8'));
    const layout = layoutContext(fs.readFileSync(path.join(root, 'tools/layout-lab.html'), 'utf8'), P);
    let count = 0, three = 0, four = 0;
    for (const chart of cases) {
      assert.equal(chart.algorithm, P.algorithmFor(chart.options));
      const actual = P.plan(chart.sourceHits, chart.options);
      assert.deepEqual(actual, comparable(chart.notes), chart.name);
      C.validate(chart);
      for (const [key, value] of Object.entries(chart.options)) {
        assert.ok(fields[key], 'layout-lab must expose ' + key);
        layout.parameters[fields[key]] = value;
      }
      assert.deepEqual(normalize(layout.plannerOptions()), chart.options);
      const tiles = chart.sourceHits.map(h => ({ i: h.tileIndex, t: h.time, hold: h.holdSeconds, ang: h.angle ?? NaN }));
      assert.deepEqual(normalize(layout.planMod(layout.toMoments(tiles))), chart.notes.map(n => n.lanes.map(l => l.lane)), chart.name + ' layout-lab');
      count += chart.notes.length;
      for (const note of chart.notes.filter(n => n.lanes.length >= 3)) {
        if (note.lanes.length === 3) three++; else four++;
        let edited = M.applyBatch(chart, [note.id], { type: 'mirror' });
        const mirrored = C.editMap(edited).get(note.id).lanes;
        assert.equal(mirrored.length, note.lanes.length);
        assert.deepEqual(mirrored.map(l => l.holdSeconds).sort(), note.lanes.map(l => l.holdSeconds).sort());
        if (note.lanes.length === 3) {
          const free = [0, 1, 2, 3].find(lane => !mirrored.some(l => l.lane === lane));
          edited = M.moveLane(edited, note.id, mirrored[0].lane, free);
        }
        assert.equal(C.baseline(C.validate(normalize(edited))), C.baseline(chart));
        assert.deepEqual(edited.sourceHits, chart.sourceHits);
      }
    }
    assert.ok(cases.length > 600 && count > 20000 && three > 0 && four > 0);
    // A portable output outside the repository must contain the same shared planner.
    const source = path.join(temporary, 'input.json'), portable = path.join(temporary, 'lab.html');
    fs.writeFileSync(source, JSON.stringify({ name: '</script> synthetic', tiles: [], beats: [] }));
    run(process.env.PYTHON || 'python', [path.join(root, 'tools/build-layout-lab.py'), portable, 'synthetic=' + source]);
    const packed = fs.readFileSync(portable, 'utf8');
    assert.ok(!packed.includes('src="review-editor/note-planner.js"'));
    const shared = packed.match(/<script>\s*(\/\* Shared browser port[^]*?)<\/script>/);
    assert.ok(shared, 'portable artifact must inline the shared planner');
    const bundled = vm.createContext({}); vm.runInContext(shared[1], bundled);
    assert.equal(bundled.NotePlanner.version, P.version);
    for (const chart of cases) assert.deepEqual(normalize(bundled.NotePlanner.plan(chart.sourceHits, chart.options)), comparable(chart.notes), chart.name + ' portable');
  } finally { fs.rmSync(temporary, { recursive: true, force: true }); }
});
