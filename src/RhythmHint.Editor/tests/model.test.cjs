// 手序编辑器的数据层：读 Mod 给的会话、编辑、撤销、保存成 Mod 能读的手序文件。
// 运行：node --test src/RhythmHint.Editor/tests/
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const M = require('../model.js');

const fixtures = path.join(__dirname, 'fixtures');
const session = () => JSON.parse(fs.readFileSync(path.join(fixtures, 'session.json'), 'utf8'));
const lanesOf = n => n.lanes.map(l => l[0]);

test('reads the session the mod writes', () => {
  const doc = M.fromSession(session());
  assert.equal(doc.keys, 4);
  assert.deepEqual(doc.keyLabels, ['D', 'F', 'J', 'K']);
  assert.equal(doc.meta.song, 'Sample Song');
  assert.equal(doc.hits.length, 16);
  assert.ok(doc.notes.length > 0 && doc.notes.length < 16, 'chords group several tiles');
  assert.equal(doc.notes.at(-1).auto, true);
  assert.equal(doc.notes[0].auto, false);
  assert.ok(doc.refs.standard.size > 0 && doc.refs.alternate.size > 0);
  assert.equal(doc.beats.length, 5);
  assert.equal(doc.path.length, 16);
  assert.equal(doc.path[0].tile, 1);
  assert.equal(doc.fileName, 'Sample Artist - Sample Song.json');
  assert.ok(doc.notes.some(n => n.lanes.length === 3), 'the sample has a triple');
  assert.ok(doc.notes.some(n => n.lanes.some(l => l[1] > 0)), 'the sample has a hold');
});

test('keeps timing for a manually selected recording and defaults old sessions to zero calibration', () => {
  const input = session();
  input.audio = { file: '', offset: -1.5, calibration: -0.09 };
  assert.equal(M.fromSession(input).audio.offset, -1.5);
  assert.equal(M.fromSession(input).audio.calibration, -0.09);
  input.audio = { file: 'legacy.ogg', offset: -1.5 };
  assert.equal(M.fromSession(input).audio.calibration, 0);
});

test('edits keep note count, times and holds; invalid edits change nothing', () => {
  const doc = M.fromSession(session());
  const before = doc.notes.map(n => [n.tile, n.lastTile, n.time, n.lanes.length, n.lanes.reduce((s, l) => s + l[1], 0)]);
  const h = new M.History(doc);
  const single = doc.notes.find(n => n.lanes.length === 1);
  const chord = doc.notes.find(n => n.lanes.length === 2);

  assert.ok(h.apply(M.ops.toLane(doc, [single], 0)));
  assert.deepEqual(lanesOf(single), [0]);
  assert.ok(h.apply(M.ops.mirror(doc, [chord])));
  assert.equal(M.ops.shift(doc, [chord], 5), null, 'out of range');
  const [a] = lanesOf(chord);
  assert.equal(M.ops.moveKey(doc, chord, a, lanesOf(chord)[1]), null, 'target already used');
  const cross = M.ops.set(doc, chord, [1, 2]);
  assert.ok(cross && h.apply(cross), 'cross-hand chords are allowed in custom fingering');
  assert.deepEqual(lanesOf(chord), [1, 2]);
  assert.equal(M.ops.set(doc, chord, [1]), null, 'must keep the key count');

  assert.deepEqual(doc.notes.map(n => [n.tile, n.lastTile, n.time, n.lanes.length, n.lanes.reduce((s, l) => s + l[1], 0)]), before);
  assert.ok(h.dirty);
  h.undo(); h.undo(); h.undo();
  assert.ok(!h.dirty);
  assert.deepEqual(doc.notes.map(n => n.lanes), doc.notes.map(n => n.orig));
  h.redo();
  assert.deepEqual(lanesOf(single), [0]);
});

test('a different edit after saving and undoing still counts as unsaved', () => {
  // 复现（2026-11-19 评审）：只数改了几步时，改 A → 保存 → 撤销 → 改 B 回到保存时的步数，显示已保存，其实游戏里还是 A
  const doc = M.fromSession(session());
  const h = new M.History(doc);
  const [a, b] = doc.notes.filter(n => n.lanes.length === 1);
  const next = n => (lanesOf(n)[0] + 1) % doc.keys;

  assert.ok(h.apply(M.ops.toLane(doc, [a], next(a))));
  h.markSaved();
  assert.ok(!h.dirty);
  h.undo();
  assert.ok(h.dirty, 'undoing the saved edit');
  h.redo();
  assert.ok(!h.dirty, 'redo comes back to exactly what was saved');
  h.undo();
  assert.ok(h.apply(M.ops.toLane(doc, [b], next(b))));
  assert.ok(h.dirty, 'same number of steps, different edit');
});

test('references apply only where the grouping matches', () => {
  const doc = M.fromSession(session());
  const change = M.ops.reference(doc, doc.notes, 'alternate');
  assert.ok(change instanceof Map);
  for (const [id, lanes] of change) {
    const ref = M.refLanes(doc, doc.notes[id], 'alternate');
    assert.deepEqual(lanes.map(l => l[0]), ref.map(l => l[0]));
  }
});

test('saved file round-trips and matches the fixture the C# tests read', () => {
  const doc = M.fromSession(session());
  const h = new M.History(doc);
  h.apply(M.ops.toLane(doc, [doc.notes[0]], 0));
  h.apply(M.ops.mirror(doc, [doc.notes.find(n => n.lanes.length === 2)]));
  doc.meta.author = 'Fixture Author';
  const text = M.toFileText(doc, new Date('2026-11-18T00:00:00Z'));

  const back = M.parseFingering(JSON.parse(text));
  assert.equal(back.notes.length, doc.notes.length);
  assert.deepEqual(back.notes.map(lanesOf), doc.notes.map(lanesOf));
  assert.equal(back.meta.author, 'Fixture Author');

  const fixture = path.join(fixtures, 'saved.json');
  if (process.env.RHYTHMHINT_UPDATE_FIXTURES === '1') fs.writeFileSync(fixture, text + '\n');
  assert.equal(fs.readFileSync(fixture, 'utf8').replace(/\r\n/g, '\n').trimEnd(), text);
});

test('rejects files that are not fingering files', () => {
  assert.throws(() => M.fromFileText('{'), e => e.code === 'notJson');
  assert.throws(() => M.fromFileText('{"format":"something"}'), e => e.code === 'notFingering');
  assert.throws(() => M.fromFileText('{"format":"rhythmhint-fingering","schemaVersion":9,"hits":[[1,1,0,null]],"notes":[[1,1,[[2,0]]]]}'), e => e.code === 'tooNew');
  assert.throws(() => M.fromFileText('{"format":"rhythmhint-fingering","schemaVersion":1,"hits":[[1,1,0,null],[2,2,0,null]],"notes":[[1,1,[[2,0]]]]}'), e => e.code === 'badFile');
  assert.throws(() => M.fromFileText('{"format":"rhythmhint-fingering","schemaVersion":1,"hits":[[1,1,0,null]],"notes":[[1,1,[[7,0]]]]}'), e => e.code === 'badFile');
});

test('jacks and note values', () => {
  const doc = M.fromFileText(JSON.stringify({
    format: 'rhythmhint-fingering', schemaVersion: 1,
    hits: [[1, 1.0, 0, 90], [2, 1.125, 0, 45], [3, 1.5, 0, 90]],
    notes: [[1, 1, [[2, 0]]], [2, 2, [[2, 0]]], [3, 3, [[1, 0]]]],
  }));
  doc.tempo = [{ time: 0, bpm: 120 }];
  assert.deepEqual([...M.jacks(doc, 0.15)].sort(), [0, 1]);
  assert.equal(M.noteValue(doc, 1.125, 0.125), '1/16');
  assert.equal(M.noteValue(doc, 1.5, 0.25), '1/8');
  assert.equal(M.noteValue(doc, 1.5, 0.375), '3/16');
  assert.equal(M.noteValue(doc, 1.5, 0.5), '1/4');
  assert.equal(M.noteValue(doc, 1.5, 0.31), '');
});

test('drafts restore only onto the same level', () => {
  const doc = M.fromSession(session());
  const h = new M.History(doc);
  h.apply(M.ops.toLane(doc, [doc.notes[0]], 0));
  const draft = JSON.parse(JSON.stringify(M.draftOf(doc, false, 1000)));
  assert.equal(draft.changed, 1);
  assert.equal(draft.saved, false);
  assert.equal(draft.time, 1000);

  const fresh = M.fromSession(session());
  const change = M.draftChange(fresh, draft);
  assert.equal(change.size, 1);
  assert.equal(M.draftChange(fresh, { ...draft, signature: 'other' }), null);
  assert.equal(M.draftChange(fresh, { ...draft, lanes: draft.lanes.slice(1) }), null, 'a draft for a different note count does not apply');
});

test('a draft brings back the whole level even when the game exports it differently', () => {
  // 2026-11-18 用户：改到一半关掉网页，下次接着改。游戏重新导出时原来的排法可能变了（换了排法、用上了保存的文件），草稿照样还原成上次的样子
  const doc = M.fromSession(session());
  const target = (doc.notes[0].lanes[0][0] + 1) % doc.keys;
  new M.History(doc).apply(M.ops.toLane(doc, [doc.notes[0]], target));
  const draft = JSON.parse(JSON.stringify(M.draftOf(doc, true)));

  const changedExport = session();
  const single = changedExport.fingering.notes.findIndex((n, i) => i > 0 && n[2].length === 1);
  changedExport.fingering.notes[single][2][0][0] = (changedExport.fingering.notes[single][2][0][0] + 2) % 4;
  const fresh = M.fromSession(changedExport);
  const change = M.draftChange(fresh, draft);
  assert.equal(change.size, 2);
  assert.ok(new M.History(fresh).apply(change));
  assert.deepEqual(fresh.notes.map(lanesOf), doc.notes.map(lanesOf));
});

test('drafts from earlier test builds (only changed notes) still restore', () => {
  const doc = M.fromSession(session());
  const target = (doc.notes[0].lanes[0][0] + 1) % doc.keys;
  const change = M.draftChange(doc, { signature: doc.signature, lanes: { 0: [target] } });
  assert.equal(change.size, 1);
  assert.deepEqual(change.get(0).map(l => l[0]), [target]);
});

test('the session says which level it is in the history', () => {
  const doc = M.fromSession(session());
  assert.match(doc.sessionId, /^16-[0-9a-f]+$/);
  assert.equal(M.fromFileText(M.toFileText(doc)).sessionId, '', 'a fingering file opened on its own has no history entry');
});

test('reads the history list the mod writes and ignores anything odd', () => {
  const recent = JSON.parse(fs.readFileSync(path.join(fixtures, 'recent.json'), 'utf8'));
  const items = M.parseRecent(recent);
  assert.deepEqual(items.map(i => i.id), [recent.items[1].id, recent.items[0].id], 'newest first');
  assert.equal(M.plain(items[0].song), 'Second Song');
  assert.equal(items[1].audio, recent.items[0].audio);
  assert.equal(items[0].audio, '');

  const odd = { ...recent, items: [
    { ...recent.items[0], session: '../index.html' },
    { ...recent.items[0], audio: '../../x.ogg' },
    { ...recent.items[0], id: '../x' },
    recent.items[1],
  ] };
  assert.deepEqual(M.parseRecent(odd).map(i => i.id), [recent.items[1].id]);
  assert.deepEqual(M.parseRecent(undefined), []);
  assert.deepEqual(M.parseRecent({ format: 'other', items: [] }), []);
});

test('saving does not overwrite another chart\'s fingering with the same name', async () => {
  // 复现（2026-11-19 评审）：建议名只有「曲师 - 曲名」，同一首歌另一位谱师的手序会被直接覆盖。规则和 Mod 的 FingeringInbox.SaveName 一样
  const doc = M.fromSession(session());
  doc.usedFile = '';
  const mine = M.toFileText(doc);
  const other = mine.replace(/"charts":\[[^\]]*\]/, '"charts":["9-other"]');
  const folder = files => async name => (name in files ? files[name] : null);
  const base = doc.fileName, second = base.replace(/\.json$/, ' (2).json'), third = base.replace(/\.json$/, ' (3).json');

  assert.equal(await M.saveName(doc, folder({})), base);
  assert.equal(await M.saveName(doc, folder({ [base]: mine })), base, 'same level: overwrite');
  assert.equal(await M.saveName(doc, folder({ [base]: other })), second, 'another chart: keep it');
  assert.equal(await M.saveName(doc, folder({ [base]: '{ broken' })), second, 'unreadable: keep it');
  assert.equal(await M.saveName(doc, folder({ [base]: other, [second]: mine })), second, 'saved as (2) before: overwrite that one');
  assert.equal(await M.saveName(doc, folder({ [base]: other, [second]: other })), third);
  doc.usedFile = base;
  assert.equal(await M.saveName(doc, folder({ [base]: '{ broken' })), base, 'the file the game uses for this level');
});

test('file names are safe and readable', () => {
  assert.equal(M.suggestedFileName({ song: '<color=#f00>A/B</color>', artist: 'X:Y' }), 'X_Y - A_B.json');
  assert.equal(M.suggestedFileName({ song: '', artist: '' }), 'fingering.json');
});

test('the song script is only taken from the history folder', () => {
  const input = session();
  const scriptOf = script => { input.audio = { file: 'recent/a.ogg', script }; return M.fromSession(input).audio.script; };
  assert.equal(scriptOf('recent/a.ogg.js'), 'recent/a.ogg.js');
  assert.equal(scriptOf(undefined), ''); // 1.3.0 导出的历史：没有，用 <audio> 播
  assert.equal(scriptOf('recent/../../evil.js'), '');
  assert.equal(scriptOf('https://example.com/x.js'), '');
  assert.equal(scriptOf('recent/a.ogg'), '');
});

test('a game in a protected folder makes the editor save to the documents inbox', () => {
  const input = session();
  assert.equal(M.fromSession(input).saveStartIn, ''); // 游戏不在系统文件夹：直接存进游戏的手序文件夹
  input.saveStartIn = 'documents';
  input.saveFolder = 'C:\\Users\\A\\Documents\\RhythmHint\\Fingerings';
  const doc = M.fromSession(input);
  assert.equal(doc.saveStartIn, 'documents');
  assert.equal(doc.saveFolder, input.saveFolder);
  input.saveStartIn = 'C:\\Windows'; // 只认浏览器的固定名字
  assert.equal(M.fromSession(input).saveStartIn, '');
});
