// 编辑器的草稿（浏览器里改到一半的进度）：在假页面里跑 app.js 原文，复现 2026-11-19 第二轮评审找到的问题。
//   草稿只认谱面、不认是在哪份手序上改的；保存途中又改了、换了关卡；恢复后改了别处再「放弃」；别的标签页删了草稿；存储写满。
// 运行：node --test src/RhythmHint.Editor/tests/
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { boot, fakeDir, fakeIndexedDb, DRAFT, session, lanesOf, allLanes, otherLevel, recentOf, editSingle, T, hasToast, findToast, flush, draftIn } = require('./app-harness.cjs');

const singles = doc => doc.notes.filter(n => n.lanes.length === 1).map(n => lanesOf(n)[0]);
const origOf = n => Array.from(n.orig, l => l[0]);
const dirty = h => h.el('docDirty').classList.contains('on');

/** 这一关的手序文件：作者 author，单押都按 lane 轨。 */
function fileText(author, lane) {
  const f = session().fingering;
  f.author = author;
  f.notes = f.notes.map(n => (n[2].length === 1 ? [n[0], n[1], [[lane, n[2][0][1]]]] : n));
  return JSON.stringify(f);
}
/** 游戏用着 fileName 这份手序导出的这一关：作者 author，单押都按 lane 轨（lane 为 null 时照样例）。 */
function gameWith(fileName, author, lane) {
  const s = session();
  s.fileName = s.usedFile = fileName;
  s.fingering.author = author;
  if (lane !== null) s.fingering.notes = s.fingering.notes.map(n => (n[2].length === 1 ? [n[0], n[1], [[lane, n[2][0][1]]]] : n));
  return s;
}
/** 存进游戏用的文件夹（已经给过权限）。 */
function folder() {
  const dir = fakeDir('Fingerings');
  return { dir, idb: fakeIndexedDb(new Map([['fingerings', dir]])) };
}
/** 文件夹权限提示开着（等玩家点）：grant() 放行。 */
function slowFolder() {
  const dir = fakeDir('Fingerings');
  const gate = {};
  dir.queryPermission = async () => 'prompt';
  dir.requestPermission = () => new Promise(r => { gate.grant = () => r('granted'); });
  return { dir, gate, idb: fakeIndexedDb(new Map([['fingerings', dir]])) };
}
async function saveNow(h, dir) {
  const before = dir.files.size;
  h.key('s', { ctrlKey: true });
  await h.until(() => dir.files.size > before || !dirty(h), 'saved');
  await h.wait(20);
}

// ---------- 草稿认「是在哪份手序上改的」 ----------

test('progress on one fingering file is not put on another file of the same chart', async () => {
  // 复现：打开过 mine.json 改了一处，后来打开朋友的 friend.json，显示的是自己的旧键位，文件名、作者也变回 mine.json、Me
  const storage = new Map();
  let h = boot({ storage });
  h.openFile('mine.json', fileText('Me', 0));
  await h.until(() => h.t.state.doc, 'mine');
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  flush(h);

  h = boot({ storage });
  h.openFile('friend.json', fileText('Friend', 3));
  await h.until(() => h.t.state.doc, 'friend');
  h.frames(2);
  const doc = h.t.state.doc;
  assert.equal(doc.fileName, 'friend.json');
  assert.equal(doc.meta.author, 'Friend');
  assert.ok(singles(doc).every(l => l === 3), singles(doc).join());
  assert.ok(hasToast(h, 'toast.opened', 'friend.json'));
  assert.equal(dirty(h), false);
});

test('reopening the same fingering file picks the progress up again', async () => {
  const storage = new Map();
  let h = boot({ storage });
  h.openFile('mine.json', fileText('Me', 0));
  await h.until(() => h.t.state.doc, 'mine');
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  const edited = lanesOf(h.t.state.doc.notes[0]);
  flush(h);

  h = boot({ storage });
  h.openFile('mine.json', fileText('Me', 0));
  await h.until(() => h.t.state.doc, 'mine again');
  h.frames(2);
  assert.deepEqual(lanesOf(h.t.state.doc.notes[0]), edited);
  assert.ok(hasToast(h, 'draft.restored', 1));
  assert.ok(hasToast(h, 'toast.opened', 'mine.json'));
  assert.equal(dirty(h), true);
});

test('after the game switches to another fingering, the old progress is offered instead of put on it', async () => {
  // 复现：mine.json 改完存进游戏，后来在游戏里换成 friend.json 再点「编辑本关手序」，编辑器显示自己以前存的那版、还说已保存
  const storage = new Map(), { dir, idb } = folder();
  let h = boot({ session: gameWith('mine.json', '', null), storage, idb, picks: [] });
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  const mine = allLanes(h.t.state.doc);
  await saveNow(h, dir);
  assert.equal(dirty(h), false);

  const friend = gameWith('friend.json', 'Friend', 3);
  h = boot({ session: friend, storage });
  h.frames(2);
  let doc = h.t.state.doc;
  assert.equal(doc.fileName, 'friend.json');
  assert.equal(doc.meta.author, 'Friend');
  assert.ok(singles(doc).every(l => l === 3));
  assert.equal(dirty(h), false);
  const ask = findToast(h, 'draft.found');
  assert.ok(ask, h.toasts.map(x => x.text).join(' | '));
  assert.deepEqual(ask.buttons.map(b => b.textContent), [T(h, 'draft.load'), T(h, 'draft.skip')]);

  // 没回答就接着改了一处：新改的照常存（换关卡、关网页不丢），提示条上照样能载入那份进度
  editSingle(h, doc.notes[1]);
  flush(h);
  assert.equal(draftIn(storage, friend.id).fileName, 'friend.json');

  ask.buttons[0].click(); // 载入
  assert.deepEqual(allLanes(doc), mine);
  assert.equal(doc.fileName, 'mine.json');
  // 载入了就是在这份上接着改：下次打开同样的导出直接接上
  h = boot({ session: friend, storage });
  h.frames(2);
  doc = h.t.state.doc;
  assert.deepEqual(allLanes(doc), mine);
  assert.ok(!findToast(h, 'draft.found'));
});

test('declining the old progress keeps the fingering the game gave and drops that progress', async () => {
  const storage = new Map();
  let h = boot({ session: gameWith('mine.json', '', null), storage });
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  editSingle(h, h.t.state.doc.notes[1]);
  flush(h);

  const friend = gameWith('friend.json', 'Friend', 3);
  h = boot({ session: friend, storage });
  h.frames(2);
  findToast(h, 'draft.found').buttons[1].click(); // 不载入
  assert.ok(singles(h.t.state.doc).every(l => l === 3));
  assert.equal(storage.has(DRAFT + friend.id), false);
});

test('progress saved to the game comes back from history, and is not offered again when the game uses the saved file', async () => {
  const storage = new Map(), { dir, idb } = folder();
  const first = gameWith('mine.json', '', null);
  let h = boot({ session: first, storage, idb, picks: [] });
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  const mine = allLanes(h.t.state.doc);
  await saveNow(h, dir);

  // 从「历史」打开（还是保存前游戏导出的那一份）：接上保存时的样子，算已保存
  h = boot({ session: first, storage });
  h.frames(2);
  assert.deepEqual(allLanes(h.t.state.doc), mine);
  assert.ok(hasToast(h, 'draft.restoredSaved', 1));
  assert.equal(dirty(h), false);

  // 游戏用上了存进去的文件再导出：和草稿一样，什么都不用问
  const saved = session();
  saved.fileName = saved.usedFile = 'mine.json';
  saved.fingering = JSON.parse(dir.files.get('mine.json'));
  saved.fromAuto = saved.fromAuto.map(() => 0);
  h = boot({ session: saved, storage });
  h.frames(2);
  assert.deepEqual(allLanes(h.t.state.doc), mine);
  assert.ok(!findToast(h, 'draft.found'));
  assert.equal(dirty(h), false);
});

test('switching handedness in the game still picks the progress up (mirrored)', async () => {
  const storage = new Map();
  const right = session();
  let h = boot({ session: right, storage });
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  const edited = allLanes(h.t.state.doc);
  flush(h);

  const left = session(); // 游戏里改成左撇子：自动排键整体镜像
  left.fingering.leftHanded = true;
  left.fingering.notes = left.fingering.notes.map(n => [n[0], n[1], n[2].map(([l, hold]) => [3 - l, hold])]);
  h = boot({ session: left, storage });
  h.frames(2);
  assert.deepEqual(allLanes(h.t.state.doc), edited.map(lanes => lanes.map(l => 3 - l).sort((a, b) => a - b)));
  assert.ok(!findToast(h, 'draft.found'));
});

test('an old draft without the fingerprint is put back when the notes it did not change still match, and offered otherwise', async () => {
  const storage = new Map(), s = session();
  let h = boot({ session: s, storage });
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  const edited = allLanes(h.t.state.doc);
  flush(h);
  const old = draftIn(storage, s.id);
  delete old.base; // 这一版以前的编辑器写的草稿
  delete old.savedBase;
  storage.set(DRAFT + s.id, JSON.stringify(old));

  h = boot({ session: s, storage }); // 同一份导出：没改过的音符都对得上
  h.frames(2);
  assert.deepEqual(allLanes(h.t.state.doc), edited);
  assert.ok(hasToast(h, 'draft.restored', 1));

  storage.set(DRAFT + s.id, JSON.stringify(old));
  h = boot({ session: gameWith('friend.json', 'Friend', 3), storage }); // 换了一份手序
  h.frames(2);
  assert.ok(singles(h.t.state.doc).every(l => l === 3));
  assert.ok(findToast(h, 'draft.found'));
});

test('a draft left by an older editor for an opened file is still picked up, then kept under the new name', async () => {
  const storage = new Map(), text = fileText('Me', 0);
  let h = boot({ storage });
  h.openFile('mine.json', text);
  await h.until(() => h.t.state.doc, 'mine');
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  const edited = lanesOf(h.t.state.doc.notes[0]);
  flush(h);
  // 换成以前的样子：按谱面签名存、没有指纹
  const [key] = [...storage.keys()].filter(k => k.startsWith(DRAFT));
  const old = JSON.parse(storage.get(key));
  delete old.base;
  storage.clear();
  const legacy = DRAFT + h.t.state.doc.signature;
  storage.set(legacy, JSON.stringify(old));

  h = boot({ storage });
  h.openFile('mine.json', text);
  await h.until(() => h.t.state.doc, 'mine again');
  h.frames(2);
  assert.deepEqual(lanesOf(h.t.state.doc.notes[0]), edited);
  assert.equal(storage.has(legacy), false);
  assert.ok([...storage.keys()].some(k => k.startsWith(DRAFT) && k !== legacy));
});

// ---------- 保存途中 ----------

test('an edit made while saving stays unsaved', async () => {
  // 复现：存进游戏要等文件夹权限，期间又改了一处，文件里没有这一处，界面却显示已保存
  const s = session(), storage = new Map(), { dir, gate, idb } = slowFolder();
  const h = boot({ session: s, storage, idb, picks: [] });
  h.frames(2);
  const doc = h.t.state.doc;
  editSingle(h, doc.notes[0]);
  h.key('s', { ctrlKey: true });
  await h.until(() => gate.grant, 'permission prompt');
  editSingle(h, doc.notes[1]);
  gate.grant();
  await h.until(() => dir.files.size === 1, 'file written');
  await h.wait(20);
  const written = JSON.parse([...dir.files.values()][0]);
  assert.deepEqual(written.notes[1][2].map(l => l[0]), origOf(doc.notes[1]), 'the second edit is not in the file');
  assert.equal(dirty(h), true);
  assert.equal(h.t.state.history.dirty, true);
  flush(h);
  assert.equal(draftIn(storage, s.id).saved, false);
  h.key('z', { ctrlKey: true }); // 撤销到存进去的那一步：和文件一样
  assert.equal(dirty(h), false);
});

test('switching levels while saving does not mark the new level as saved', async () => {
  // 复现：保存途中在「历史」里换了一关，换过去的那关被标成已保存，草稿写成 saved: true，关网页不提醒
  const a = session(), b = otherLevel('16-abcdef', 0.5), storage = new Map(), { dir, gate, idb } = slowFolder();
  const h = boot({ session: a, storage, idb, picks: [], recent: recentOf(b, a), sessions: { [b.id]: { data: b } } });
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  h.key('s', { ctrlKey: true });
  await h.until(() => gate.grant, 'permission prompt');
  h.clickHistory(0);
  await h.until(() => h.t.state.doc.meta.song === b.fingering.song, 'level b');
  editSingle(h, h.t.state.doc.notes[3]);
  gate.grant();
  await h.until(() => dir.files.size === 1, 'file written');
  await h.wait(20);
  assert.equal(dirty(h), true);
  assert.equal(h.t.state.history.dirty, true);
  flush(h);
  assert.equal(draftIn(storage, b.id).saved, false);
  assert.ok(hasToast(h, 'save.done', a.fileName));
});

test('renaming the file while saving keeps the new name and stays unsaved', async () => {
  const s = session(), { dir, gate, idb } = slowFolder();
  const h = boot({ session: s, idb, picks: [] });
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  h.key('s', { ctrlKey: true });
  await h.until(() => gate.grant, 'permission prompt');
  const input = h.el('fName');
  input.value = 'Renamed';
  input.dispatch('change');
  gate.grant();
  await h.until(() => dir.files.size === 1, 'file written');
  await h.wait(20);
  assert.ok(dir.files.has(s.fileName), 'saved under the name it had when saving started');
  assert.equal(h.t.state.doc.fileName, 'Renamed.json');
  assert.equal(dirty(h), true);
});

test('saving again while a save is still running does not start a second save', async () => {
  // 复现：存进游戏要等文件夹权限，这期间再按 Ctrl+S、在输入框里按 Ctrl+S、再点保存按钮，每次都又发起一次保存（又问一次权限、又写一次文件）
  const s = session(), { dir, gate, idb } = slowFolder();
  let asks = 0;
  const ask = dir.requestPermission;
  dir.requestPermission = () => { asks++; return ask(); };
  const h = boot({ session: s, idb, picks: [] });
  h.frames(2);
  const doc = h.t.state.doc;
  editSingle(h, doc.notes[0]);
  h.key('s', { ctrlKey: true });
  await h.until(() => gate.grant, 'permission prompt');
  editSingle(h, doc.notes[1]); // 保存途中又改了一处
  h.key('s', { ctrlKey: true });
  h.key('s', { ctrlKey: true, target: h.el('fAuthor') });
  h.el('saveBtn').click();
  await h.wait(20);
  assert.equal(asks, 1, 'only the first save is running');
  gate.grant();
  await h.until(() => dir.files.size === 1, 'file written');
  await h.wait(20);
  assert.equal(dirty(h), true, 'the edit made while saving is not in the file');

  // 存完了再按一次：照常保存，这次连那一处一起存进去
  gate.grant = null;
  h.key('s', { ctrlKey: true });
  await h.until(() => gate.grant, 'permission prompt again');
  gate.grant();
  await h.until(() => !dirty(h), 'saved again');
  assert.equal(asks, 2);
  const written = JSON.parse([...dir.files.values()][0]);
  assert.deepEqual(written.notes[1][2].map(l => l[0]), lanesOf(doc.notes[1]));
});

// ---------- 恢复后「放弃」 ----------

test('discarding restored progress after editing elsewhere keeps that edit and stays unsaved', async () => {
  // 复现：恢复草稿后 12 秒内又改了别处再点「放弃」，那处仍是改过的，却显示已保存、草稿被删，关网页不提醒
  const s = session(), storage = new Map();
  let h = boot({ session: s, storage });
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  flush(h);
  h = boot({ session: s, storage });
  h.frames(2);
  const restored = findToast(h, 'draft.restored', 1);
  const doc = h.t.state.doc;
  editSingle(h, doc.notes[4]);
  const mine = lanesOf(doc.notes[4]);
  restored.buttons[0].click(); // 放弃
  assert.deepEqual(lanesOf(doc.notes[0]), origOf(doc.notes[0]), 'the restored edit is gone');
  assert.deepEqual(lanesOf(doc.notes[4]), mine, 'the later edit is kept');
  assert.equal(dirty(h), true);
  assert.equal(h.t.state.history.dirty, true);
  flush(h);
  const draft = draftIn(storage, s.id);
  assert.equal(draft.saved, false);
  assert.deepEqual(draft.lanes[4], mine);
});

test('discarding restored progress right away goes back to what the game gave', async () => {
  const s = session(), storage = new Map();
  let h = boot({ session: s, storage });
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  flush(h);
  h = boot({ session: s, storage });
  h.frames(2);
  findToast(h, 'draft.restored', 1).buttons[0].click();
  assert.deepEqual(lanesOf(h.t.state.doc.notes[0]), origOf(h.t.state.doc.notes[0]));
  assert.equal(dirty(h), false);
  assert.equal(storage.has(DRAFT + s.id), false);
});

// ---------- 别的标签页 ----------

test('another tab deleting this level\'s draft is not taken as an edit: this tab writes its own again', async () => {
  // 复现：别的标签页清理旧草稿、或在那边点了「放弃」，这边弹「另一个标签页改了这一关」，点「载入另一页的进度」后本页改动全丢
  const s = session(), storage = new Map();
  const h = boot({ session: s, storage });
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  const mine = lanesOf(h.t.state.doc.notes[0]);
  flush(h);
  storage.delete(DRAFT + s.id);
  h.fire('storage', { key: DRAFT + s.id, newValue: null, oldValue: '{}' });
  assert.ok(!hasToast(h, 'draft.otherTab'));
  assert.deepEqual(draftIn(storage, s.id).lanes[0], mine);
  editSingle(h, h.t.state.doc.notes[1]); // 之后照常写
  flush(h);
  assert.deepEqual(draftIn(storage, s.id).lanes[1], lanesOf(h.t.state.doc.notes[1]));
});

// ---------- 存储写满 ----------

test('when the storage is full, the oldest unsaved drafts of other levels make room, and the player is told how many', async () => {
  // 复现：草稿只按个数清理，写满时只删已保存的；别的关没保存的旧草稿一直占着，这一关存不进去
  const s = session();
  const measure = boot({ session: s });
  measure.frames(2);
  editSingle(measure, measure.t.state.doc.notes[0]);
  flush(measure);
  const size = measure.storage.get(DRAFT + s.id).length; // 这一关的草稿有多大

  const storage = new Map();
  const old = (name, time) => {
    const draft = { signature: 'x', lanes: [], saved: false, changed: 1, time, pad: '' };
    draft.pad = 'x'.repeat(Math.round(size / 2) - JSON.stringify(draft).length);
    storage.set(DRAFT + name, JSON.stringify(draft));
  };
  old('a-oldest', 1);
  old('b-older', 2);
  old('c-newest', 3);
  const h = boot({ session: s, storage, prefs: { wayBackTold: true } });
  h.frames(2);
  editSingle(h, h.t.state.doc.notes[0]);
  h.setQuota(h.used() + size - Math.round(size * 0.75)); // 要腾出两份旧草稿才放得下
  flush(h);
  assert.ok(storage.has(DRAFT + s.id), 'this level is kept');
  assert.equal(storage.has(DRAFT + 'a-oldest'), false);
  assert.equal(storage.has(DRAFT + 'b-older'), false);
  assert.equal(storage.has(DRAFT + 'c-newest'), true);
  assert.ok(hasToast(h, 'draft.dropped', 2));
  assert.ok(!hasToast(h, 'draft.storageFull'));
});
