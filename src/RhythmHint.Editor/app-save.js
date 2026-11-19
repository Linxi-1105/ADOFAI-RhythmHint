/* 保存：存进游戏的手序文件夹（选文件夹、权限，句柄记在 IndexedDB）、同名文件另起名字、下载、确认框。 */
(function (App) {
  'use strict';

  const { M, $, state, toast, errorText, copy } = App;

  // ---------- 保存 ----------
  const DB_NAME = 'rhythmhint-editor', STORE = 'handles';
  function idb(mode, fn) {
    return new Promise((resolve, reject) => {
      const open = indexedDB.open(DB_NAME, 1);
      open.onupgradeneeded = () => open.result.createObjectStore(STORE);
      open.onerror = () => reject(open.error);
      open.onsuccess = () => {
        const tx = open.result.transaction(STORE, mode);
        const req = fn(tx.objectStore(STORE));
        tx.oncomplete = () => { open.result.close(); resolve(req && req.result); };
        tx.onerror = () => { open.result.close(); reject(tx.error); };
      };
    });
  }
  // 游戏的手序文件夹和文档里的中转文件夹各记一个：游戏换了位置、换成中转文件夹时不会存到旧的地方
  const folderKey = () => (state.doc && state.doc.saveStartIn ? 'fingerings-inbox' : 'fingerings');
  // 读写不了 IndexedDB（隐私模式等）：当作没记住，每次保存问一次
  const getFolder = () => idb('readonly', s => s.get(folderKey())).catch(e => { console.warn('RhythmHint: cannot read the folder', e); return null; });
  const putFolder = handle => idb('readwrite', s => s.put(handle, folderKey())).catch(e => console.warn('RhythmHint: cannot remember the folder', e));
  const forgetFolder = () => idb('readwrite', s => s.delete(folderKey())).catch(e => console.warn('RhythmHint: cannot forget the folder', e));

  function modal(title, body, path, okLabel) {
    $('modalTitle').textContent = title;
    $('modalBody').textContent = body;
    $('modalPathRow').hidden = !path;
    $('modalPath').textContent = path || '';
    $('modalOk').textContent = okLabel;
    $('modalCancel').textContent = state.t('save.cancel');
    $('modal').hidden = false;
    return new Promise(resolve => { modalResolve = resolve; });
  }
  let modalResolve = null;
  function closeModal(ok) { $('modal').hidden = true; if (modalResolve) { modalResolve(ok); modalResolve = null; } }
  $('modalOk').addEventListener('click', () => closeModal(true));
  $('modalCancel').addEventListener('click', () => closeModal(false));
  $('modalCopy').addEventListener('click', e => copy($('modalPath').textContent, e.currentTarget));

  function download(text, name) {
    const url = URL.createObjectURL(new Blob([text], { type: 'application/json' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = name;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 2000); // 有意的延时：等浏览器开始下载再释放
  }

  /**
   * 存进游戏了：记成已保存的是开始保存时的那一步（snap，saveToGame 开头记下的）。存的途中（等文件夹权限时）又改了一处，
   * 那一处不在文件里，照样算没保存（2026-11-19 评审：原来标的是存完时的样子，多改的那处也显示已保存）。
   */
  function saved(snap, name) {
    snap.history.markSaved(snap.step);
    App.metaDirty = App.metaEdits !== snap.metaEdits;
    snap.doc.savedBase = snap.base(name); // 游戏下次用存进去的这份导出时，草稿也认得
    App.allowDraft(); // 这一页刚存进游戏，就以这一页为准
    App.writeDraft(); // 草稿留着、记成已保存：从「历史」打开这一关时还是保存时的样子
    App.renderHeader();
  }

  const saveFolderText = () => (state.doc && state.doc.saveFolder) || '…/Mods/RhythmHint/Fingerings';

  /**
   * 记住的手序文件夹，还有读写权限时才用；玩家拒绝了权限、文件夹不在了就忘掉，重新问（2026-11-19 评审：原来永远留着，只能清浏览器数据）。
   * 别的错误（比如浏览器要玩家再点一下才肯问权限）照旧报出来，记住的不丢，再点一次保存就行。
   */
  async function rememberedFolder() {
    const dir = await getFolder();
    if (!dir) return null;
    try {
      if ((await dir.queryPermission({ mode: 'readwrite' })) === 'granted' || (await dir.requestPermission({ mode: 'readwrite' })) === 'granted') return dir;
    } catch (e) {
      if (!e || e.name !== 'NotFoundError') throw e;
    }
    await forgetFolder();
    return null;
  }

  /** 游戏的手序文件夹（读写权限，句柄记在 IndexedDB 里）：没记住或没权限就问玩家、让他选；玩家取消为 null。 */
  async function fingeringsFolder() {
    const dir = await rememberedFolder();
    if (dir) return dir;
    // 2026-11-19 玩家截图：游戏装在 C:\Program Files (x86) 时浏览器不让选游戏里的文件夹，Mod 改让这里存到文档里的中转文件夹
    const inbox = !!(state.doc && state.doc.saveStartIn);
    if (!(await modal(state.t('save.pickTitle'), state.t(inbox ? 'save.pickBodyInbox' : 'save.pickBody'), saveFolderText(), state.t('save.pickGo')))) return null;
    return pickFolder();
  }

  /**
   * 让玩家选手序文件夹（要在点击里调用：浏览器只在玩家刚点过时打开选择框）。游戏的手序文件夹和文档里的中转文件夹都叫 Fingerings：
   * 选的不叫这个就是选错了，提示后重选，不再给「仍然保存」，也不记住（2026-11-19 评审：选错一次就一直存错地方）。玩家取消为 null。
   */
  async function pickFolder() {
    const inbox = !!(state.doc && state.doc.saveStartIn);
    for (;;) {
      const dir = await window.showDirectoryPicker(inbox
        ? { id: 'rhythmhint-fingerings-inbox', mode: 'readwrite', startIn: state.doc.saveStartIn }
        : { id: 'rhythmhint-fingerings', mode: 'readwrite' });
      if (dir.name.toLowerCase() === 'fingerings') { await putFolder(dir); return dir; }
      if (!(await modal(state.t('save.pickTitle'), state.t('save.wrongFolder', dir.name), saveFolderText(), state.t('save.pickAgain')))) return null;
    }
  }

  /** 文件面板「更换保存位置」：选好了才换，取消了照旧用原来的。 */
  $('changeFolder').addEventListener('click', async e => {
    e.currentTarget.blur();
    try {
      const dir = await pickFolder();
      if (dir) toast(state.t('save.folderSet', dir.name), 'ok');
    } catch (err) {
      if (err && err.name === 'AbortError') return; // 玩家在选文件夹时点了取消
      toast(state.t('save.failed', errorText(err)), 'error');
    }
  });

  /** 文件夹里这个文件的文字；没有这个文件为 null。同名的是个文件夹时给空串（读不出来，这个名字不能用）。 */
  async function existingText(dir, name) {
    try {
      return await (await (await dir.getFileHandle(name)).getFile()).text();
    } catch (e) {
      if (e && e.name === 'NotFoundError') return null;
      if (e && e.name === 'TypeMismatchError') return '';
      throw e;
    }
  }

  // 正在存进游戏（等文件夹权限、写文件）：这期间再按 Ctrl+S、再点保存不再发起一次（2026-11-19：原来每按一次就多一个权限提示、多写一次）。
  // 存的途中又改的不在这次的文件里，照样算没保存，存完再按一次就行
  let saving = false;
  async function saveToGame() {
    const doc = state.doc;
    if (!doc || saving) return;
    const text = M.toFileText(doc);
    if (!window.showDirectoryPicker) {
      // 下载不算保存（2026-11-19 评审：玩家没把文件放进去也显示已保存）：照旧提示放到哪里，没保存的标记留着
      download(text, doc.fileName);
      toast(state.t('save.downloaded', doc.fileName, saveFolderText()), 'warn', null, 12000);
      return;
    }
    // 开头记下存的是哪一关、哪一步、用什么名字：下面要等文件夹权限等，期间玩家可能又改了、改了文件名、在历史里换了关卡
    const snap = {
      doc, history: state.history, step: state.history.current, metaEdits: App.metaEdits, base: M.baseFor(doc),
      target: { fileName: doc.fileName, usedFile: doc.usedFile, meta: { charts: doc.meta.charts } },
    };
    saving = true;
    try {
      let dir = await fingeringsFolder();
      if (!dir) return;
      let name;
      try {
        name = await writeInto(dir, snap.target, text);
      } catch (e) {
        if (!e || (e.name !== 'NotFoundError' && e.name !== 'NotAllowedError')) throw e;
        // 记住的文件夹被删、被挪走或没有权限了：忘掉，问玩家重新选（选择框要玩家再点一下才能打开）
        await forgetFolder();
        if (!(await modal(state.t('save.pickTitle'), state.t('save.folderLost'), saveFolderText(), state.t('save.pickGo')))) return;
        dir = await pickFolder();
        if (!dir) return;
        name = await writeInto(dir, snap.target, text);
      }
      if (name === null) { toast(state.t('save.tooMany', snap.target.fileName), 'error', null, 12000); return; }
      const renamed = name !== snap.target.fileName;
      // 存完时已经换了一关：那一关的文件照样写好了，这一关不算保存
      if (state.doc === doc) {
        if (renamed && doc.fileName === snap.target.fileName) { doc.fileName = name; App.renderFilePanel(); } // 存的途中玩家自己改了名字就留着他的
        saved(snap, name);
      }
      toast(state.t(renamed ? 'save.renamed' : 'save.done', name), 'ok', null, renamed ? 12000 : 6000);
    } catch (e) {
      if (e && e.name === 'AbortError') return; // 玩家在选文件夹时点了取消
      toast(state.t('save.failed', errorText(e)), 'error');
    } finally {
      saving = false;
    }
  }

  /** 写进文件夹，返回存成的文件名；同名的都是别的谱、没名字可用时为 null。target：开始保存时的文件名、游戏正用着的文件、谱面指纹。 */
  async function writeInto(dir, target, text) {
    // 同名的是另一首谱的手序时另起名字，不覆盖（M.saveName，和 Mod 搬中转文件夹时同一规则）
    const name = await M.saveName(target, n => existingText(dir, n));
    if (!name) return null;
    const writable = await (await dir.getFileHandle(name, { create: true })).createWritable();
    await writable.write(text);
    await writable.close();
    return name;
  }
  $('saveBtn').addEventListener('click', saveToGame);
  $('saveAsBtn').addEventListener('click', () => {
    if (!state.doc) return;
    download(M.toFileText(state.doc), state.doc.fileName);
    toast(state.t('saveAs.done', state.doc.fileName), 'ok');
  });

  Object.assign(App, { closeModal, saveToGame });
})(window.EditorApp);
