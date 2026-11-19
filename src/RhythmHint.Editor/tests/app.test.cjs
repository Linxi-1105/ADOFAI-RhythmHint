// 手序编辑器的界面层：在假页面里跑 app.js 原文（tests/app-harness.cjs），复现 2026-11-19 评审找到的交互问题。
// 运行：node --test src/RhythmHint.Editor/tests/
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { boot, fakeDir, fakeIndexedDb, named, El } = require('./app-harness.cjs');

const fixtures = path.join(__dirname, 'fixtures');
const session = () => JSON.parse(fs.readFileSync(path.join(fixtures, 'session.json'), 'utf8'));
const DRAFT = 'rhythmhint.editor.draft.';
const lanesOf = n => Array.from(n.lanes, l => l[0]); // Array.from：假页面里的数组和这里的不是同一个 Array，严格比较要换过来
const draftOf = (h, s) => JSON.parse(h.storage.get(DRAFT + s.id));

/** 第 3 个和最后一个音符是自动补齐的会话。 */
function autoSession() {
  const s = session();
  s.fromAuto = s.fromAuto.map((_, i) => (i === 3 || i === s.fromAuto.length - 1 ? 1 : 0));
  return s;
}
function otherLevel(id, shift) {
  const s = session();
  s.id = id;
  s.fingering.hits.forEach(h => { h[1] += shift; });
  s.fingering.song = 'Other ' + id;
  return s;
}
const recentOf = (...list) => ({
  format: 'rhythmhint-editor-recent',
  items: list.map((s, i) => ({ id: s.id, session: s.id + '.js', song: s.fingering.song, exported: 1000 - i, notes: s.fingering.notes.length })),
});
/** 选中一个单押，按下一条轨道的键。 */
function editSingle(h, n) {
  h.select(n.id);
  const labels = h.t.state.doc.keyLabels;
  h.key(labels[(lanesOf(n)[0] + 1) % labels.length].toLowerCase());
}
const T = (h, key, ...args) => h.t.state.t(key, ...args);
const hasToast = (h, key, ...args) => h.toasts.some(x => x.text === T(h, key, ...args));

test('N goes on to the next place to review and wraps around', () => {
  // 复现：跳过去时画面停在音符前 0.6 秒，下一次从画面时间往后找还是它，按 N 永远停在第一处
  const h = boot({ session: autoSession() });
  h.frames(2);
  const last = h.t.state.doc.notes.length - 1;
  const seen = [];
  for (let k = 0; k < 3; k++) { h.key('n'); h.frames(60); seen.push([...h.t.state.selection]); }
  assert.deepEqual(seen, [[3], [last], [3]]);
});

test('the review marks go away once a note is changed or confirmed, and stay away', async () => {
  const s = autoSession(), storage = new Map();
  let h = boot({ session: s, storage });
  h.frames(2);
  const doc = h.t.state.doc;
  assert.equal(h.el('docCheck').textContent, T(h, 'doc.check', 2));
  editSingle(h, doc.notes[3]);
  assert.equal(h.el('docCheck').textContent, T(h, 'doc.check', 1), 'changed');
  const last = doc.notes.at(-1);
  h.select(last.id);
  h.key(doc.keyLabels[lanesOf(last)[0]].toLowerCase()); // 按它现在的键：看过了
  assert.ok(h.el('docCheck').hidden, 'confirmed');
  assert.equal(last.auto, false);
  await h.wait(700);
  assert.deepEqual(draftOf(h, s).confirmed, [[last.tile, last.lastTile]]);

  h = boot({ session: s, storage });
  h.frames(2);
  assert.ok(h.el('docCheck').hidden, 'still confirmed after reopening');
});

test('a download does not count as saved', async () => {
  // 复现：浏览器没有文件夹接口时下载文件就算保存了，玩家没放进去也显示已保存
  const s = session();
  const h = boot({ session: s });
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  h.key('s', { ctrlKey: true });
  assert.ok(hasToast(h, 'save.downloaded', h.t.state.doc.fileName, h.t.state.doc.saveFolder || '…/Mods/RhythmHint/Fingerings'));
  assert.ok(h.el('docDirty').classList.contains('on'), 'still unsaved');
  await h.wait(700);
  assert.equal(draftOf(h, s).saved, false);
});

test('the file name typed in the file panel is kept in the draft', async () => {
  const s = session(), storage = new Map();
  let h = boot({ session: s, storage });
  h.frames(2);
  const input = h.el('fName');
  input.value = 'My take';
  input.dispatch('change', { target: input });
  assert.equal(h.t.state.doc.fileName, 'My take.json');
  await h.wait(700);
  assert.equal(draftOf(h, s).fileName, 'My take.json');

  h = boot({ session: s, storage });
  h.frames(2);
  assert.equal(h.t.state.doc.fileName, 'My take.json', 'from history the save goes to the same file');
  assert.equal(h.el('fName').value, 'My take');
});

test('file names typed in the panel are cleaned', () => {
  const h = boot({ session: session() });
  h.frames(2);
  const input = h.el('fName');
  input.value = 'nul';
  input.dispatch('change', { target: input });
  assert.equal(h.t.state.doc.fileName, 'nul_.json');
  input.value = 'a\u0001b';
  input.dispatch('change', { target: input });
  assert.equal(h.t.state.doc.fileName, 'ab.json');
});

test('a folder that is not Fingerings is never remembered or saved into', async () => {
  // 复现：选错文件夹时可以点「仍然保存」，而且记进 IndexedDB，以后一直存错地方
  const idb = fakeIndexedDb(), wrong = fakeDir('Downloads'), right = fakeDir('Fingerings');
  const h = boot({ session: session(), idb, picks: [wrong, right] });
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  h.key('s', { ctrlKey: true });
  await h.until(() => !h.el('modal').hidden, 'the first-time dialog');
  h.el('modalOk').click();
  await h.until(() => h.el('modalBody').textContent === T(h, 'save.wrongFolder', 'Downloads'), 'the wrong-folder dialog');
  assert.equal(h.el('modalOk').textContent, T(h, 'save.pickAgain'), 'no way to save into it anyway');
  assert.equal(idb.store.size, 0);
  h.el('modalOk').click();
  await h.until(() => right.files.size === 1, 'saving into Fingerings');
  assert.equal(wrong.files.size, 0);
  assert.equal([...idb.store.values()][0], right);
});

test('a remembered folder that is gone is forgotten and chosen again', async () => {
  const old = fakeDir('Fingerings'), fresh = fakeDir('Fingerings');
  old.gone = true;
  const idb = fakeIndexedDb(new Map([['fingerings', old]]));
  const h = boot({ session: session(), idb, picks: [fresh] });
  h.frames(2);
  h.key('s', { ctrlKey: true });
  await h.until(() => !h.el('modal').hidden, 'asking for the folder again');
  assert.equal(h.el('modalBody').textContent, T(h, 'save.folderLost'));
  h.el('modalOk').click();
  await h.until(() => fresh.files.size === 1, 'saving into the new folder');
  assert.equal(idb.store.get('fingerings'), fresh);
});

test('a remembered folder whose permission was refused is forgotten and chosen again', async () => {
  const old = fakeDir('Fingerings', { permission: 'denied' }), fresh = fakeDir('Fingerings');
  const idb = fakeIndexedDb(new Map([['fingerings', old]]));
  const h = boot({ session: session(), idb, picks: [fresh] });
  h.frames(2);
  h.key('s', { ctrlKey: true });
  await h.until(() => !h.el('modal').hidden, 'asking for the folder');
  assert.ok(!idb.store.has('fingerings'), 'forgotten');
  h.el('modalOk').click();
  await h.until(() => fresh.files.size === 1, 'saving into the new folder');
  assert.equal(idb.store.get('fingerings'), fresh);
  assert.equal(old.files.size, 0);
});

test('the save location can be changed from the file panel', async () => {
  const old = fakeDir('Fingerings'), other = fakeDir('Fingerings');
  const idb = fakeIndexedDb(new Map([['fingerings', old]]));
  const h = boot({ session: session(), idb, picks: [other] });
  h.frames(2);
  h.el('changeFolder').click();
  await h.until(() => idb.store.get('fingerings') === other, 'remembering the new folder');
  assert.ok(hasToast(h, 'save.folderSet', 'Fingerings'));
  h.el('changeFolder').click(); // 这次在选文件夹时取消：原来的照旧
  await h.wait(20);
  assert.equal(idb.store.get('fingerings'), other);
});

test('opening another level resets the unsaved mark, loop and play button', async () => {
  const a = session(), b = otherLevel('16-abcdef', 0.5);
  const h = boot({ session: a, recent: recentOf(b, a), sessions: { [b.id]: { data: b } } });
  h.frames(2);
  const author = h.el('fAuthor');
  author.dispatch('input', { target: { value: 'Me' } });
  h.key('['); h.t.state.time += 1; h.key(']');
  assert.ok(h.el('loopBtn').classList.contains('on'));
  h.el('playBtn').click();
  assert.match(h.el('playBtn').innerHTML, /i-pause/);
  h.clickHistory(0);
  await h.until(() => h.t.state.doc.meta.song === b.fingering.song, 'the other level');
  assert.equal(h.t.metaDirty, false);
  assert.ok(!h.el('docDirty').classList.contains('on'));
  assert.ok(!h.el('loopBtn').classList.contains('on'));
  assert.match(h.el('playBtn').innerHTML, /i-play/);
  assert.equal(h.t.state.timeTarget, null);
});

test('lane keys that are also shortcuts are pointed out', () => {
  const s = session();
  s.keyLabels = ['Z', 'X', 'N', 'M'];
  const h = boot({ session: s });
  h.frames(2);
  assert.ok(hasToast(h, 'keys.clash', 'N M'));
  assert.equal(h.el('keysClash').hidden, false);
  assert.equal(h.el('keysClash').textContent, T(h, 'keys.clash', 'N M'));

  // 录键时设成快捷键也提示；按着 Ctrl 的组合不录
  const plain = boot({ session: session() });
  plain.frames(2);
  assert.ok(plain.el('keysClash').hidden);
  plain.t.capture(0);
  plain.key('z', { ctrlKey: true });
  assert.deepEqual(Array.from(plain.t.state.labels), ['D', 'F', 'J', 'K'], 'Ctrl+Z is not recorded');
  plain.key('m');
  assert.deepEqual(Array.from(plain.t.state.labels), ['M', 'F', 'J', 'K']);
  assert.ok(hasToast(plain, 'keys.clash', 'M'));
});

test('game key names like Keypad4 and LeftShift work as lane keys', () => {
  const s = session();
  s.keyLabels = ['Keypad4', 'LeftShift', 'J', 'Alpha1'];
  const h = boot({ session: s });
  h.frames(2);
  const doc = h.t.state.doc;
  h.select(doc.notes[0].id);
  h.key('4', { code: 'Numpad4' });
  assert.deepEqual(lanesOf(doc.notes[0]), [0]);
  h.select(doc.notes[1].id);
  h.key('4', { code: 'Digit4' }); // 主键盘的 4 不是 Keypad4
  assert.deepEqual(lanesOf(doc.notes[1]), [1]);
  h.key('1', { code: 'Digit1' });
  assert.deepEqual(lanesOf(doc.notes[1]), [3]);

  // Shift 单独按下松开才算轨道键；按着 Shift 按别的键、点音符加选都不算
  h.select(doc.notes[3].id);
  h.key('Shift', { code: 'ShiftLeft', shiftKey: true });
  h.key('Z', { code: 'KeyZ', shiftKey: true });
  h.keyUp('Shift', { code: 'ShiftLeft' });
  assert.deepEqual(lanesOf(doc.notes[3]), [2], 'Shift was part of a combination');
  h.select(doc.notes[5].id);
  h.key('Shift', { code: 'ShiftLeft', shiftKey: true });
  h.key('Shift', { code: 'ShiftLeft', shiftKey: true, repeat: true });
  h.keyUp('Shift', { code: 'ShiftLeft' });
  assert.deepEqual(lanesOf(doc.notes[5]), [1]);
  assert.equal(h.el('keymap').innerHTML.includes('Num4'), true, 'shown the way the game shows it');
});

test('Ctrl+S in a text box saves instead of opening the browser\'s save page dialog', () => {
  const h = boot({ session: session() });
  h.frames(2);
  const box = new El('fAuthor');
  box.closest = () => box;
  const e = h.key('s', { ctrlKey: true, target: box });
  assert.ok(e.prevented);
  assert.ok(hasToast(h, 'save.downloaded', h.t.state.doc.fileName, h.t.state.doc.saveFolder || '…/Mods/RhythmHint/Fingerings'));
});

test('clicking two levels quickly opens the one clicked last', async () => {
  const a = session(), x = otherLevel('16-111111', 0.5), y = otherLevel('16-222222', 1);
  const h = boot({ session: a, recent: recentOf(x, y, a), sessions: { [x.id]: { data: x, delay: 60 }, [y.id]: { data: y, delay: 5 } } });
  h.frames(2);
  h.clickHistory(0); // X（慢）
  h.clickHistory(1); // Y（快），最后点的
  await h.wait(120);
  assert.equal(h.t.state.doc.meta.song, y.fingering.song);
});

test('another tab editing the same level stops this tab from overwriting its draft', async () => {
  const s = session(), storage = new Map();
  const h = boot({ session: s, storage });
  h.frames(2);
  const doc = h.t.state.doc;
  // 另一个标签页改了第一个音符、写了草稿
  const other = boot({ session: s, storage: new Map() });
  other.frames(2);
  editSingle(other, other.t.state.doc.notes[0]);
  await other.wait(700);
  const theirs = other.storage.get(DRAFT + s.id);
  storage.set(DRAFT + s.id, theirs);
  h.fire('storage', { key: DRAFT + s.id, newValue: theirs, oldValue: null });
  const warning = h.toasts.find(x => x.text === T(h, 'draft.otherTab'));
  assert.ok(warning);

  editSingle(h, doc.notes[1]);
  await h.wait(700);
  assert.equal(storage.get(DRAFT + s.id), theirs, 'this tab does not overwrite it');

  warning.buttons[0].click(); // 载入另一页的进度
  assert.deepEqual(lanesOf(h.t.state.doc.notes[0]), lanesOf(other.t.state.doc.notes[0]));
  assert.deepEqual(lanesOf(h.t.state.doc.notes[1]), lanesOf(other.t.state.doc.notes[1]));
});

test('a full browser storage is reported once', async () => {
  // 复现：localStorage 写满时空 catch 吞掉，界面还说已自动保留
  const s = session();
  const h = boot({ session: s, quota: 600 });
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  await h.wait(700);
  editSingle(h, h.t.state.doc.notes[1]);
  await h.wait(700);
  assert.equal(h.toasts.filter(x => x.text === T(h, 'draft.storageFull')).length, 1);
});

test('only the latest drafts are kept, saved ones go first', () => {
  const storage = new Map();
  for (let i = 0; i < 40; i++) storage.set(DRAFT + 'old' + i, JSON.stringify({ signature: 'x', lanes: [], saved: i % 2 === 0, changed: 1, time: i }));
  storage.set('rhythmhint.editor.prefs', JSON.stringify({ wayBackTold: true }));
  const h = boot({ session: session(), storage });
  h.frames(2);
  const drafts = [...storage.keys()].filter(k => k.startsWith(DRAFT));
  assert.equal(drafts.length, 30);
  assert.ok([...Array(20).keys()].every(i => storage.has(DRAFT + 'old' + (2 * i + 1))), 'every unsaved draft is kept');
  assert.ok(storage.has('rhythmhint.editor.prefs'));
});

test('files over 20 MB are not opened, and read errors are shown', async () => {
  const h = boot({});
  let read = false;
  const big = { name: 'huge.json', size: 21 * 1024 * 1024, text: async () => { read = true; return '{}'; } };
  h.el('openInput').dispatch('change', { target: { files: [big], value: '' } });
  await h.wait(10);
  assert.equal(read, false);
  assert.ok(hasToast(h, 'toast.tooBig', 'huge.json', 20));

  const broken = { name: 'gone.json', size: 10, text: async () => { throw named('NotReadableError', 'gone'); } };
  h.el('openInput').dispatch('change', { target: { files: [broken], value: '' } });
  await h.wait(10);
  assert.ok(hasToast(h, 'toast.openFailed', 'gone'));
});
