/* 手序编辑器：把界面、画布、播放和数据接起来。
 * 数据和编辑操作在 model.js（可测）；这里只管交互：选择、拖动换轨、快捷键、检查器、保存、草稿、语言。 */
(function () {
  'use strict';

  const M = window.FingeringModel, I18N = window.EditorI18n;
  // 系统开了「减少动态效果」就不做动画（滑动、缩放、换轨都直接到位）
  const reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const $ = id => document.getElementById(id);
  const esc = s => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

  // ---------- 偏好（只存在这台电脑的浏览器里，读不了就用默认） ----------
  // 打拍声默认打开（2026-11-18 用户：没听到打击声）；换了个键名，旧版存下的「关」不再生效。
  // 每首歌的手动偏移也换了键名（songOffsets）：旧的是在补以前算错的偏移和没算进去的画面校准，留着会补过头
  const PREFS_KEY = 'rhythmhint.editor.prefs';
  const prefs = Object.assign({
    language: '', beats: true, wave: true, gaps: true, ghost: true, track: true, standard: true, alternate: true,
    jackMs: 150, volume: 50, refScale: 50, noteStyle: null, hitsoundOn: true, pxPerSec: 240, speed: 1, songOffsets: {}, laneKeys: null, autoNext: true, wayBackTold: false, trackZoom: 1, trackBig: false,
  }, readJson(PREFS_KEY) || {});
  function readJson(key) {
    try { return JSON.parse(localStorage.getItem(key) || 'null'); } catch (e) { return null; } // 隐私模式下 localStorage 不可用：当作没有
  }
  function writeJson(key, value) {
    try { localStorage.setItem(key, JSON.stringify(value)); } catch (e) { /* 存不了偏好或草稿不影响编辑 */ }
  }
  function removeKey(key) {
    try { localStorage.removeItem(key); } catch (e) { /* 同上 */ }
  }
  const savePrefs = () => writeJson(PREFS_KEY, prefs);

  // ---------- 状态 ----------
  const session = window.RHYTHMHINT_SESSION;
  const state = {
    doc: null, history: null, t: null, source: '',
    time: 0, pxPerSec: prefs.pxPerSec, playing: false,
    selection: new Set(), hover: null, preview: null, jacks: new Set(),
    show: { beats: prefs.beats, wave: prefs.wave, gaps: prefs.gaps, ghost: prefs.ghost, standard: prefs.standard, alternate: prefs.alternate },
    loop: { a: null, b: null, on: false }, flash: [], press: [], round: false, box: null,
    leftInset: 0, rightInset: 0, waveform: null, audioOffset: 0, hitsound: prefs.hitsoundOn, lastTime: 0, refScale: prefs.refScale / 100,
    timeTarget: null, zoomTarget: null, zoomAnchor: null, anims: new Map(), now: 0, seekWarned: false,
  };

  const canvas = $('chart');
  const view = new window.ChartView(canvas);
  const track = new window.TrackView($('track'));
  const player = new window.EditorPlayer($('audio'));
  player.setVolume(prefs.volume / 100);
  player.setRate(prefs.speed);
  player.onSeekFail = () => {
    if (state.seekWarned) return;
    state.seekWarned = true;
    toast(state.t('audio.noSeek'), 'warn', null, 10000);
  };
  $('speed').value = String(prefs.speed);

  // ---------- 语言 ----------
  function pickLanguage() {
    if (prefs.language) return prefs.language;
    if (session && session.language) return session.language;
    return I18N.fromBrowser(navigator.language);
  }

  function applyLanguage(code) {
    state.t = I18N.create(code);
    document.documentElement.lang = state.t.language;
    document.querySelectorAll('[data-i18n]').forEach(el => { el.textContent = state.t(el.dataset.i18n); });
    document.querySelectorAll('[data-i18n-title]').forEach(el => { el.title = state.t(el.dataset.i18nTitle); });
    document.querySelectorAll('[data-i18n-placeholder]').forEach(el => { el.placeholder = state.t(el.dataset.i18nPlaceholder); });
    document.title = 'RhythmHint · ' + state.t('brand.sub');
    renderShortcuts();
    renderKeymap();
    renderHeader();
    renderInspector();
    renderFilePanel();
    renderAudioName();
    if (openPanel === 'history') renderHistory();
    if (openPanel) $('drawerTitle').textContent = state.t('rail.' + openPanel);
    trackSizeTitle();
    // 版本号：游戏导出最近一关时写进 session.js 的 Mod 版本（和 F8 里显示的一样）；不开游戏、没有会话时不显示
    const version = session && typeof session.modVersion === 'string' ? session.modVersion : '';
    $('aboutVersion').textContent = version ? state.t('about.version', version) : '';
    window.EditorDropdown.syncAll();
  }

  const langSelect = $('langSelect');
  I18N.LANGUAGES.forEach(([code, name]) => langSelect.add(new Option(name, code)));
  // 下拉菜单用编辑器自己的样式（dropdown.js；原来的 select 留着，change 事件照旧）
  window.EditorDropdown.enhance(langSelect);
  window.EditorDropdown.enhance($('vStyle'));
  window.EditorDropdown.enhance($('speed'));
  langSelect.addEventListener('change', () => { prefs.language = langSelect.value; savePrefs(); applyLanguage(langSelect.value); });

  // ---------- 提示条 ----------
  function toast(text, kind, actions, ms) {
    const el = document.createElement('div');
    el.className = 'toast ' + (kind || '');
    const span = document.createElement('span');
    span.textContent = text;
    el.appendChild(span);
    (actions || []).forEach(([label, fn, primary]) => {
      const b = document.createElement('button');
      b.className = 'btn' + (primary ? ' primary' : '');
      b.textContent = label;
      b.addEventListener('click', () => { close(); fn(); });
      el.appendChild(b);
    });
    $('toasts').appendChild(el);
    let timer = setTimeout(close, ms || (actions && actions.length ? 15000 : 4200)); // 有意的延时：提示条显示几秒后自己消失
    function close() {
      clearTimeout(timer);
      el.classList.add('leave');
      el.addEventListener('animationend', () => el.remove(), { once: true });
    }
    return close;
  }

  function errorText(e) {
    if (e instanceof M.FingeringError) {
      const key = 'err.' + e.code;
      return I18N.STRINGS[key] ? state.t(key, e.detail) : e.message;
    }
    return e && e.message ? e.message : String(e);
  }

  // ---------- 打开 ----------
  function loadDoc(doc, source) {
    player.pause();
    state.doc = doc;
    state.history = new M.History(doc);
    state.source = source;
    state.selection.clear();
    state.loop = { a: null, b: null, on: false };
    state.flash = [];
    state.press = [];
    applyNoteStyle();
    state.time = doc.notes.length ? doc.notes[0].time - 1.2 : 0;
    view.setDoc(doc);
    track.setDoc(doc);
    $('empty').hidden = true;
    $('trackCard').hidden = !prefs.track || doc.path.length < 2; // 没有轨道形状（打开的是文件或离线数据）就不显示空卡片
    state.waveform = doc.audio && doc.audio.peaks.length ? { peaks: doc.audio.peaks, perSecond: doc.audio.peaksPerSecond } : null;
    const extra = prefs.songOffsets[doc.signature] || 0;
    $('offset').value = String(extra);
    $('offsetOut').textContent = String(extra);
    manualAudio = '';
    player.hasAudio = false;
    player.setRate(prefs.speed, doc.audio?.pitch ?? 1);
    player.baseOffset = doc.audio ? doc.audio.offset : 0;
    player.calibration = doc.audio ? doc.audio.calibration : 0;
    player.userOffsetMs = extra;
    player.seek(state.time);
    state.audioOffset = player.offset;
    if (doc.fromGame) player.setHitSound(doc.hitSound); // 打开的是手序文件时没有打击声，留着上一关的
    player.load(doc.audio && doc.audio.file ? doc.audio.file + '?v=' + Date.now() : '')
      .then(renderAudioName)
      .catch(e => { renderAudioName(); toast(state.t('audio.failed', e.message), 'error'); });
    if (doc.audio && doc.audio.script) loadDecoded(doc);
    renderKeymap();
    edited(true);
    restoreDraft();
    if (openPanel === 'history') renderHistory();
  }

  function openFromText(text, name) {
    try {
      const doc = M.fromFileText(text);
      doc.fileName = name || doc.fileName;
      loadDoc(doc, 'file');
      toast(state.t('toast.opened', name || doc.fileName), 'ok');
    } catch (e) {
      toast(state.t('toast.openFailed', errorText(e)), 'error');
    }
  }

  function askOpen() {
    if (state.history && state.history.dirty && !window.confirm(state.t('toast.confirmOpen'))) return;
    $('openInput').click();
  }
  $('openInput').addEventListener('change', async e => {
    const file = e.target.files[0];
    e.target.value = '';
    if (file) openFromText(await file.text(), file.name);
  });
  document.querySelectorAll('[data-action="open"]').forEach(b => b.addEventListener('click', askOpen));

  // 拖进来：手序文件就打开，音乐就载入
  window.addEventListener('dragover', e => e.preventDefault());
  window.addEventListener('drop', async e => {
    e.preventDefault();
    const file = e.dataTransfer.files[0];
    if (!file) return;
    if (/\.json$/i.test(file.name)) {
      if (state.history && state.history.dirty && !window.confirm(state.t('toast.confirmOpen'))) return;
      openFromText(await file.text(), file.name);
    } else if (file.type.startsWith('audio/') || /\.(ogg|mp3|wav|flac|m4a)$/i.test(file.name)) loadAudioFile(file);
  });

  // ---------- 音乐 ----------
  let manualAudio = '';
  /**
   * Mod 另存的歌曲数据（file:// 下读不了音乐文件的字节，只能用 <script> 载入）：整首解码后换给播放器，跳转才准。
   * 解码好之前先用 <audio> 播；旧版本导出的历史没有这份数据、解码失败时一直用 <audio>。
   */
  function loadDecoded(doc) {
    const key = doc.audio.file, tag = document.createElement('script');
    const take = () => {
      const all = window.RHYTHMHINT_AUDIO || {}, text = all[key];
      delete all[key]; // 字节交给解码后就不再留着这份文本
      return text;
    };
    tag.src = doc.audio.script + '?v=' + Date.now();
    tag.onload = async () => {
      tag.remove();
      const text = take();
      if (typeof text !== 'string' || state.doc !== doc) return;
      try {
        const buffer = await window.EditorPlayer.decode(window.EditorPlayer.bytesFromBase64(text), player.ctx && player.ctx.sampleRate);
        if (state.doc !== doc || manualAudio) return; // 解码期间换了关卡或手动选了音乐
        player.useBuffer(buffer);
        if (!state.waveform) state.waveform = window.peaksFromBuffer(buffer, 100); // 自制关卡游戏里读不到采样，这里补上波形
        renderAudioName();
      } catch (e) {
        console.warn('RhythmHint: decoding the song failed, keeping <audio>', e);
      }
    };
    tag.onerror = () => tag.remove(); // 文件不在了：继续用 <audio>
    document.head.appendChild(tag);
  }
  async function loadAudioFile(file) {
    try {
      const url = URL.createObjectURL(file);
      if (!await player.load(url)) return;
      manualAudio = file.name;
      state.audioOffset = player.offset;
      renderAudioName();
      const buffer = await window.EditorPlayer.decode(new Uint8Array(await file.arrayBuffer()), player.ctx && player.ctx.sampleRate);
      if (manualAudio !== file.name || player.el.src !== url) return; // 解码期间又换了音乐
      player.useBuffer(buffer);
      state.waveform = window.peaksFromBuffer(buffer, 100);
    } catch (e) {
      toast(state.t('audio.failed', errorText(e)), 'error');
    }
  }
  $('loadAudio').addEventListener('click', () => $('audioInput').click());
  $('audioInput').addEventListener('change', e => { const f = e.target.files[0]; e.target.value = ''; if (f) loadAudioFile(f); });
  function renderAudioName() {
    $('audioName').textContent = !player.hasAudio ? state.t('audio.none') : manualAudio || state.t('audio.fromGame');
    const cal = player.hasAudio ? Math.round(player.calibration * 1000) : 0;
    $('calibrationNote').hidden = cal === 0;
    $('calibrationNote').textContent = cal === 0 ? '' : state.t('audio.calibrated', cal);
  }
  $('offset').addEventListener('input', e => {
    const v = Number(e.target.value);
    $('offsetOut').textContent = String(v);
    player.userOffsetMs = v;
    state.audioOffset = player.offset;
    if (state.doc) { prefs.songOffsets[state.doc.signature] = v; savePrefs(); }
    if (player.playing) player.cancelClicks();
    else player.seek(state.time);
  });
  // 音量：音乐抽屉里和底部播放条上各一个，两边同步
  function setVolume(v) {
    prefs.volume = v;
    $('volume').value = String(v);
    $('volumeQuick').value = String(v);
    $('volumeOut').textContent = String(v);
    player.setVolume(v / 100);
    savePrefs();
  }
  $('volume').addEventListener('input', e => setVolume(Number(e.target.value)));
  $('volumeQuick').addEventListener('input', e => setVolume(Number(e.target.value)));
  setVolume(prefs.volume);
  function setHitsound(on) {
    player.cancelClicks();
    state.hitsound = on;
    prefs.hitsoundOn = on;
    savePrefs();
    $('hitsound').checked = on;
    $('hitBtn').classList.toggle('on', on);
  }
  $('hitsound').addEventListener('change', e => setHitsound(e.target.checked));
  $('hitBtn').addEventListener('click', () => setHitsound(!state.hitsound));
  setHitsound(state.hitsound);

  // ---------- 显示设置 ----------
  [['vBeats', 'beats'], ['vWave', 'wave'], ['vGaps', 'gaps'], ['vGhost', 'ghost']].forEach(([id, key]) => {
    $(id).checked = state.show[key];
    $(id).addEventListener('change', e => { state.show[key] = e.target.checked; prefs[key] = e.target.checked; savePrefs(); });
  });
  // 音符样式：默认和游戏里一样（游戏设置里的横条 / 圆形），这里也能临时换
  function applyNoteStyle() {
    state.round = prefs.noteStyle === 'round' || (prefs.noteStyle !== 'bar' && !!(state.doc && state.doc.roundNotes));
  }
  $('vStyle').value = prefs.noteStyle || 'game';
  $('vStyle').addEventListener('change', e => {
    prefs.noteStyle = e.target.value === 'game' ? null : e.target.value;
    savePrefs();
    applyNoteStyle();
  });
  $('vTrack').checked = prefs.track;
  $('trackCard').hidden = !prefs.track;
  $('vTrack').addEventListener('change', e => {
    prefs.track = e.target.checked;
    savePrefs();
    $('trackCard').hidden = !prefs.track || !state.doc || state.doc.path.length < 2;
    resize();
  });
  $('jackMs').value = String(prefs.jackMs);
  $('jackOut').textContent = String(prefs.jackMs);
  $('jackMs').addEventListener('input', e => {
    prefs.jackMs = Number(e.target.value);
    $('jackOut').textContent = String(prefs.jackMs);
    savePrefs();
    edited(false);
  });
  $('refScale').value = String(prefs.refScale);
  $('refScaleOut').textContent = prefs.refScale + '%';
  $('refScale').addEventListener('input', e => {
    prefs.refScale = Number(e.target.value);
    state.refScale = prefs.refScale / 100;
    $('refScaleOut').textContent = prefs.refScale + '%';
    savePrefs();
  });
  document.querySelectorAll('[data-show-ref]').forEach(box => {
    const key = box.dataset.showRef;
    box.checked = state.show[key];
    box.addEventListener('change', () => { state.show[key] = box.checked; prefs[key] = box.checked; savePrefs(); });
  });

  // ---------- 抽屉 ----------
  let openPanel = null;
  function togglePanel(name) {
    const drawer = $('drawer'), was = openPanel;
    openPanel = openPanel === name ? null : name;
    document.querySelectorAll('#rail button').forEach(b => b.classList.toggle('on', b.dataset.panel === openPanel));
    if (!openPanel && was && !reduceMotion) {
      // 关的时候滑出去再藏起来
      drawer.classList.add('closing');
      drawer.addEventListener('animationend', () => { drawer.classList.remove('closing'); if (!openPanel) drawer.hidden = true; }, { once: true });
    } else {
      drawer.classList.remove('closing');
      drawer.hidden = !openPanel;
      if (openPanel && was && was !== openPanel) restart(drawer, 'swap');
    }
    document.querySelectorAll('[data-panel-body]').forEach(el => { el.hidden = el.dataset.panelBody !== openPanel; });
    if (openPanel) $('drawerTitle').textContent = state.t('rail.' + openPanel);
    if (openPanel === 'file') renderFilePanel();
    if (openPanel === 'history') renderHistory();
    resize();
  }
  document.querySelectorAll('#rail button').forEach(b => b.addEventListener('click', () => togglePanel(b.dataset.panel)));
  $('drawerClose').addEventListener('click', () => togglePanel(openPanel));

  function renderFilePanel() {
    const doc = state.doc;
    const meta = doc ? doc.meta : { song: '', artist: '', charter: '', author: '', comment: '' };
    $('fSong').textContent = M.plain(meta.song) || '—';
    $('fArtist').textContent = M.plain(meta.artist) || '—';
    $('fCharter').textContent = M.plain(meta.charter) || '—';
    if (document.activeElement !== $('fAuthor')) $('fAuthor').value = meta.author;
    if (document.activeElement !== $('fComment')) $('fComment').value = meta.comment;
    if (document.activeElement !== $('fName')) $('fName').value = doc ? doc.fileName.replace(/\.json$/i, '') : '';
    const folder = doc && doc.saveFolder ? doc.saveFolder : '…/Mods/RhythmHint/Fingerings';
    const parts = folder.split(/[\\/]/).filter(Boolean);
    $('fFolderName').textContent = parts[parts.length - 1] || folder;
    $('fFolder').textContent = folder;
    if (doc) {
      const s = M.stats(doc, state.jacks);
      const stat = (n, label, hot) => '<div class="stat' + (hot ? ' hot' : '') + '"><b>' + n + '</b><span title="' + esc(label) + '">' + esc(label) + '</span></div>';
      $('fStats').innerHTML = stat(s.notes, state.t('file.statNotes')) + stat(s.changed, state.t('file.statChanged'), s.changed > 0)
        + stat(s.auto, state.t('file.statAuto')) + stat(countJackPairs(), state.t('file.statJacks'));
    } else $('fStats').innerHTML = '';
    const hasRefs = doc && (doc.refs.standard.size || doc.refs.alternate.size);
    $('refsNone').hidden = !!hasRefs || !doc;
    document.querySelectorAll('.ref-card').forEach(c => { c.hidden = !hasRefs; });
  }
  $('fAuthor').addEventListener('input', e => { if (state.doc) { state.doc.meta.author = e.target.value; markMetaDirty(); } });
  $('fComment').addEventListener('input', e => { if (state.doc) { state.doc.meta.comment = e.target.value; markMetaDirty(); } });
  $('fName').addEventListener('change', e => {
    if (!state.doc) return;
    // 输入框里只写名字，.json 固定显示在框里
    let name = e.target.value.trim().replace(/\.json$/i, '').replace(/[\\/:*?"<>|]/g, '_');
    if (!name) name = M.suggestedFileName(state.doc.meta).replace(/\.json$/i, '');
    state.doc.fileName = name + '.json';
    e.target.value = name;
  });
  let metaDirty = false;
  function markMetaDirty() { metaDirty = true; renderHeader(); scheduleDraft(); }
  function copy(text, button) {
    navigator.clipboard.writeText(text).then(() => toast(state.t('file.copied'), 'ok', null, 1600), () => {});
    if (button) button.blur();
  }
  $('copyFolder').addEventListener('click', e => copy($('fFolder').textContent, e.currentTarget));

  function renderShortcuts() {
    const labels = laneLabels().join(' ');
    const rows = [
      ['keys.play', '<kbd>Space</kbd>'], ['keys.lane', labels.split(' ').map(k => '<kbd>' + esc(k) + '</kbd>').join('')],
      ['keys.drag', ''], ['keys.shift', '<kbd>←</kbd><kbd>→</kbd>'], ['keys.mirror', '<kbd>M</kbd>'],
      ['keys.undo', '<kbd>Ctrl</kbd><kbd>Z</kbd> / <kbd>Ctrl</kbd><kbd>Shift</kbd><kbd>Z</kbd>'], ['keys.save', '<kbd>Ctrl</kbd><kbd>S</kbd>'],
      ['keys.home', '<kbd>Home</kbd>'], ['keys.next', '<kbd>N</kbd>'], ['keys.all', '<kbd>Ctrl</kbd><kbd>A</kbd> / <kbd>Esc</kbd>'],
      ['keys.loop', '<kbd>[</kbd><kbd>]</kbd>'], ['keys.scroll', esc(state.t('keys.wheel'))], ['keys.zoom', '<kbd>Ctrl</kbd> + ' + esc(state.t('keys.wheel'))],
      ['keys.pan', esc(state.t('keys.spaceDrag'))], ['keys.box', esc(state.t('keys.emptyDrag'))],
    ];
    $('shortcuts').innerHTML = rows.map(([k, v]) => '<dt>' + esc(state.t(k)) + '</dt><dd>' + v + '</dd>').join('');
  }

  // ---------- 顶栏 ----------
  function renderHeader() {
    const doc = state.doc;
    $('docSource').textContent = doc ? state.t('doc.song') : '';
    $('docTitle').textContent = doc ? [M.plain(doc.meta.artist), M.plain(doc.meta.song)].filter(Boolean).join(' – ') || doc.fileName : state.t('doc.untitled');
    $('docDirty').classList.toggle('on', !!(state.history && (state.history.dirty || metaDirty)));
    $('undoBtn').disabled = !(state.history && state.history.canUndo);
    $('redoBtn').disabled = !(state.history && state.history.canRedo);
    $('saveBtn').disabled = !doc;
    $('saveAsBtn').disabled = !doc;
    const check = doc ? checkList().length : 0;
    $('docCheck').hidden = check === 0;
    $('docCheck').textContent = state.t('doc.check', check);
  }
  $('docCheck').addEventListener('click', () => nextCheck());

  // ---------- 编辑 ----------
  function edited(reset) {
    const doc = state.doc;
    if (!doc) return;
    state.jacks = M.jacks(doc, prefs.jackMs / 1000);
    renderHeader();
    renderInspector();
    if (openPanel === 'file') renderFilePanel();
    if (!reset) scheduleDraft();
  }

  function applyChange(change, animated = true) {
    if (change === null) { toast(state.t('toast.cannotMove'), 'warn'); return false; }
    const before = new Map([...change.keys()].map(id => [id, state.doc.notes[id].lanes]));
    if (!state.history.apply(change)) return false;
    if (animated) animate(before);
    edited(false);
    return true;
  }

  /** 换轨动画：记下改之前在哪几条轨道，画的时候从那里滑过去。 */
  function animate(before) {
    if (reduceMotion) return;
    const t0 = performance.now();
    before.forEach((lanes, id) => state.anims.set(id, { from: lanes, t0 }));
  }

  /** 重新播放一个 CSS 动画（同一个类连续加两次不会再动）。 */
  function restart(el, cls) {
    if (reduceMotion) return;
    el.classList.remove(cls);
    void el.offsetWidth;
    el.classList.add(cls);
  }

  const selectedNotes = () => [...state.selection].sort((a, b) => a - b).map(id => state.doc.notes[id]);

  // ---------- 编辑键位（用户要求能在编辑器里自定义；默认和游戏里的键位一样） ----------
  let capturing = null; // 正在等新键的轨道
  function laneLabels() {
    const doc = state.doc;
    if (!doc) return ['D', 'F', 'J', 'K'];
    return Array.isArray(prefs.laneKeys) && prefs.laneKeys.length === doc.keys ? prefs.laneKeys : doc.keyLabels;
  }
  function renderKeymap() {
    const labels = laneLabels();
    const el = $('keymap');
    el.style.gridTemplateColumns = 'repeat(' + labels.length + ', 1fr)';
    el.innerHTML = labels.map((k, lane) => '<button data-key-lane="' + lane + '" class="' + (capturing === lane ? 'capturing' : '') + '">'
      + '<b>' + esc(capturing === lane ? '…' : k) + '</b><small>' + esc(capturing === lane ? state.t('keys.press') : state.t('keys.laneN', lane + 1)) + '</small></button>').join('');
    el.querySelectorAll('[data-key-lane]').forEach(b => b.addEventListener('click', () => {
      const lane = Number(b.dataset.keyLane);
      capturing = capturing === lane ? null : lane;
      renderKeymap();
    }));
    state.labels = labels;
  }
  /** 等新键时按下的键：合法就记下（和别的轨道重复就互换），Esc 取消。 */
  function captureKey(e) {
    e.preventDefault();
    const key = e.key;
    if (key !== 'Escape' && key.length === 1 && key !== ' ') {
      const labels = laneLabels().slice();
      const k = key.toUpperCase(), other = labels.findIndex(x => x.toUpperCase() === k);
      if (other >= 0 && other !== capturing) labels[other] = labels[capturing];
      labels[capturing] = k;
      prefs.laneKeys = labels;
      savePrefs();
    }
    capturing = null;
    renderKeymap();
    renderShortcuts();
    renderInspector();
  }
  $('keysReset').addEventListener('click', () => { prefs.laneKeys = null; savePrefs(); capturing = null; renderKeymap(); renderShortcuts(); renderInspector(); });
  $('autoNext').checked = prefs.autoNext;
  $('autoNext').addEventListener('change', e => { prefs.autoNext = e.target.checked; savePrefs(); });

  function undo() {
    const step = state.history && state.history.undoStack[state.history.undoStack.length - 1];
    if (!step || !state.history.undo()) return;
    animate(new Map(step.map(([id, , after]) => [id, after])));
    edited(false);
  }
  function redo() {
    const step = state.history && state.history.redoStack[state.history.redoStack.length - 1];
    if (!step || !state.history.redo()) return;
    animate(new Map(step.map(([id, before]) => [id, before])));
    edited(false);
  }
  $('undoBtn').addEventListener('click', undo);
  $('redoBtn').addEventListener('click', redo);

  function laneKeyPressed(lane) {
    const notes = selectedNotes();
    if (!notes.length) return;
    if (notes.every(n => n.lanes.length > 1)) { toast(state.t('toast.chordKey'), 'warn'); return; }
    const change = M.ops.toLane(state.doc, notes, lane);
    state.press[lane] = performance.now(); // 底下这条轨道的按键框亮一下，像在游戏里按下
    if (change === null) { applyChange(null); return; }
    applyChange(change);
    // 只选中一个音符时，改完自动跳到下一个（用户要求：用 D F J K 一路改下去；本来就在这条轨道也算确认，照样往下走）
    if (prefs.autoNext && notes.length === 1) stepSelection(1);
  }

  /** 需要检查的：自动补齐的音符和连按。 */
  function checkList() {
    const doc = state.doc;
    const ids = [];
    let lastAuto = false;
    for (const n of doc.notes) {
      // 自动补齐的一段只算一处（跳到这一段的开头）
      if (n.auto && !lastAuto) ids.push(n.id);
      else if (!n.auto && state.jacks.has(n.id)) ids.push(n.id);
      lastAuto = n.auto;
    }
    return ids;
  }
  function countJackPairs() {
    let count = 0;
    const doc = state.doc;
    for (let i = 1; i < doc.notes.length; i++)
      if (state.jacks.has(doc.notes[i].id) && state.jacks.has(doc.notes[i - 1].id)) count++;
    return count;
  }

  function nextCheck() {
    if (!state.doc) return;
    const list = checkList();
    if (!list.length) { toast(state.t('toast.noCheck'), 'ok'); return; }
    const after = list.find(id => state.doc.notes[id].time > state.time + 0.05);
    const id = after !== undefined ? after : list[0];
    focusNote(id);
  }

  function focusNote(id) {
    const n = state.doc.notes[id];
    state.selection = new Set([id]);
    seek(n.time - 0.6, true);
    renderInspector();
  }

  /** 跳到某个时间。smooth：暂停时画面平滑滑过去，停下来再让播放器对准；播放中总是直接跳。 */
  function seek(t, smooth) {
    if (smooth && !player.playing && !reduceMotion) { state.timeTarget = t; return; }
    state.timeTarget = null;
    state.time = t;
    state.lastTime = t;
    player.seek(t);
  }

  // ---------- 检查器 ----------
  function renderInspector() {
    const el = $('inspector');
    const doc = state.doc;
    if (!doc) { el.hidden = true; return; }
    el.hidden = false;
    const notes = state.doc ? selectedNotes() : [];
    const labels = laneLabels();
    if (!notes.length) {
      el.innerHTML = '<h2>' + esc(state.t('insp.none')) + '</h2><p class="hint">' + esc(state.t('insp.noneHint', labels.join(' '))) + '</p>'
        + (checkList().length ? '<div class="tool-row"><button class="btn" data-insp="next"><svg><use href="#i-flag"/></svg>' + esc(state.t('insp.next')) + '</button></div>' : '');
      bindInspector(el, notes);
      return;
    }
    const one = notes.length === 1 ? notes[0] : null;
    let html = '<h2>' + esc(state.t('insp.count', notes.length)) + '</h2>';
    if (one) {
      const i = one.id;
      const prev = i > 0 ? doc.notes[i - 1] : null, next = i + 1 < doc.notes.length ? doc.notes[i + 1] : null;
      const ms = (a, b) => a && b ? Math.round((b.time - a.time) * 1000) + ' ms' : '—';
      const tags = [];
      if (one.auto) tags.push('<span class="tag auto">' + esc(state.t('source.auto')) + '</span>');
      else if (state.source === 'game' && doc.usedFile) tags.push('<span class="tag custom">' + esc(state.t('source.custom')) + '</span>');
      if (M.isChanged(one)) tags.push('<span class="tag changed">' + esc(state.t('source.changed')) + '</span>');
      html += '<dl class="kv">'
        + '<dt>' + esc(state.t('insp.time')) + '</dt><dd class="mono">' + esc(fmt(one.time)) + '</dd>'
        + '<dt>' + esc(state.t('insp.tile')) + '</dt><dd class="mono">#' + one.tile + (one.lastTile !== one.tile ? '–' + one.lastTile : '') + '</dd>'
        + '<dt>' + esc(state.t('insp.gap')) + '</dt><dd>' + esc(state.t('insp.gapValue', ms(prev, one), ms(one, next))) + '</dd>'
        + '<dt>' + esc(state.t('insp.kind')) + '</dt><dd>' + esc(state.t('kind.' + Math.min(4, one.lanes.length))) + '</dd>'
        + (tags.length ? '<dt>' + esc(state.t('insp.source')) + '</dt><dd>' + tags.join(' ') + '</dd>' : '')
        + '</dl>';
      if (one.auto) html += '<div class="note-callout warn">' + esc(state.t('insp.autoNote')) + '</div>';
      if (state.jacks.has(one.id)) html += '<div class="note-callout danger">' + esc(state.t('insp.jackNote', prefs.jackMs)) + '</div>';
      const cls = one.lanes.length > 2 ? ' c3' : one.lanes.length > 1 ? ' c2' : '';
      html += '<div><div class="hint" style="margin-bottom:6px">' + esc(one.lanes.length > 1 ? state.t('insp.lanesHint', one.lanes.length) : state.t('insp.lanes')) + '</div>'
        + '<div class="lanes" style="grid-template-columns:repeat(' + doc.keys + ',1fr)">'
        + labels.map((k, lane) => '<button data-lane="' + lane + '" class="' + (one.lanes.some(l => l[0] === lane) ? 'on' + cls : '') + (lane === doc.keys / 2 ? ' hand-gap' : '') + '">' + esc(k) + '</button>').join('')
        + '</div></div>';
    }
    html += '<div class="tool-row">'
      + '<button class="btn" data-insp="left" title="←"><svg><use href="#i-left"/></svg>' + esc(state.t('insp.left')) + '</button>'
      + '<button class="btn" data-insp="right" title="→">' + esc(state.t('insp.right')) + '<svg><use href="#i-right"/></svg></button>'
      + '<button class="btn" data-insp="mirror" title="M"><svg><use href="#i-mirror"/></svg>' + esc(state.t('insp.mirror')) + '</button></div>';
    if (doc.refs.standard.size || doc.refs.alternate.size)
      html += '<div class="tool-row"><button class="btn" data-insp="standard"><span class="dot standard"></span>' + esc(state.t('col.standard')) + '</button>'
        + '<button class="btn" data-insp="alternate"><span class="dot alternate"></span>' + esc(state.t('col.alternate')) + '</button></div>';
    html += '<div class="tool-row"><button class="btn" data-insp="revert"><svg><use href="#i-revert"/></svg>' + esc(state.t('insp.revert')) + '</button>'
      + (checkList().length ? '<button class="btn" data-insp="next"><svg><use href="#i-flag"/></svg>' + esc(state.t('insp.next')) + '</button>' : '') + '</div>';
    el.innerHTML = html;
    bindInspector(el, notes);
    const key = notes.map(n => n.id).join(',');
    if (key !== inspectorKey) { inspectorKey = key; restart(el, 'swap'); }
  }
  let inspectorKey = '';

  // 检查器里点选多押的键：先记下点了哪几个，凑够个数再改
  let chordPick = [];
  function bindInspector(el, notes) {
    chordPick = [];
    el.querySelectorAll('[data-lane]').forEach(b => b.addEventListener('click', () => {
      const note = notes[0], lane = Number(b.dataset.lane);
      if (note.lanes.length === 1) { applyChange(M.ops.set(state.doc, note, [lane])); return; }
      if (chordPick.includes(lane)) chordPick = chordPick.filter(x => x !== lane);
      else chordPick.push(lane);
      el.querySelectorAll('[data-lane]').forEach(x => {
        const l = Number(x.dataset.lane);
        x.classList.toggle('on', chordPick.includes(l));
        x.classList.toggle(note.lanes.length > 2 ? 'c3' : 'c2', chordPick.includes(l));
      });
      if (chordPick.length === note.lanes.length) applyChange(M.ops.set(state.doc, note, chordPick));
    }));
    el.querySelectorAll('[data-insp]').forEach(b => b.addEventListener('click', () => {
      const doc = state.doc;
      switch (b.dataset.insp) {
        case 'left': applyChange(M.ops.shift(doc, notes, -1)); break;
        case 'right': applyChange(M.ops.shift(doc, notes, 1)); break;
        case 'mirror': applyChange(M.ops.mirror(doc, notes)); break;
        case 'revert': applyChange(M.ops.revert(doc, notes)); break;
        case 'standard': case 'alternate': applyReference(b.dataset.insp, notes); break;
        case 'next': nextCheck(); break;
      }
    }));
  }

  function applyReference(which, notes) {
    const change = M.ops.reference(state.doc, notes, which);
    if (!change || change.size === 0) { toast(state.t('toast.nothing'), 'warn'); return; }
    if (applyChange(change)) toast(state.t('toast.applied', change.size), 'ok');
  }
  document.querySelectorAll('[data-apply]').forEach(b => b.addEventListener('click', () => {
    if (!state.doc) return;
    const which = b.dataset.apply;
    if (b.dataset.scope === 'all') {
      if (!window.confirm(state.t('refs.confirmAll', state.t('col.' + which)))) return;
      applyReference(which, state.doc.notes);
    } else applyReference(which, selectedNotes());
  }));

  // ---------- 画布交互 ----------
  let drag = null, pan = null, spaceDown = false, spacePanned = false;
  const pos = e => { const r = canvas.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; };

  canvas.addEventListener('mousedown', e => {
    if (!state.doc) return;
    const [x, y] = pos(e);
    if (e.button === 1 || e.button === 2 || spaceDown) {
      pan = { y, time: state.time };
      spacePanned = true;
      canvas.classList.add('grabbing');
      e.preventDefault();
      return;
    }
    if (e.button !== 0) return;
    const hit = view.hit(x, y, state);
    if (hit.kind === 'note') {
      const id = hit.note.id;
      if (e.shiftKey || e.ctrlKey) {
        if (state.selection.has(id)) state.selection.delete(id); else state.selection.add(id);
        renderInspector();
        return;
      }
      if (!state.selection.has(id)) { state.selection = new Set([id]); renderInspector(); }
      drag = { note: hit.note, lane: hit.lane, x, moved: false, change: undefined };
    } else if (hit.kind === 'ruler' || hit.kind === 'empty' || hit.kind === 'outside') {
      // 拖动就框选。从左边时间尺起手也一样，框伸进中间谱面就选中框住的音符；在时间尺上单击不拖仍跳到那里
      // （2026-11-18 用户：从时间尺往谱面里框选会变成拖着谱面滚动）。起点按谱面时间记，靠边自动滚动时框跟着谱面走
      state.box = { x0: x, y0: y, x1: x, y1: y, add: e.shiftKey, t0: view.timeAt(y, state), seekTo: hit.kind === 'ruler' ? hit.time : null };
    }
  });
  canvas.addEventListener('contextmenu', e => e.preventDefault());

  window.addEventListener('mousemove', e => {
    if (!state.doc) return;
    const [x, y] = pos(e);
    if (pan) { state.time = pan.time + (y - pan.y) / state.pxPerSec; player.seek(state.time); return; }
    if (drag) {
      if (!drag.moved && Math.abs(x - drag.x) < 6) return;
      drag.moved = true;
      const lane = Math.max(0, Math.min(state.doc.keys - 1, view.laneAt(x)));
      const notes = selectedNotes();
      let change;
      if (notes.length === 1 && notes[0].lanes.length > 1) change = M.ops.moveKey(state.doc, notes[0], drag.lane, lane);
      else if (notes.length === 1) change = M.ops.toLane(state.doc, notes, lane);
      else change = M.ops.shift(state.doc, notes, lane - drag.lane);
      drag.change = change;
      state.preview = change;
      canvas.className = 'lane';
      return;
    }
    if (state.box) { state.box.x1 = x; state.box.y1 = y; return; }
    if (e.target !== canvas) return;
    const hit = view.hit(x, y, state);
    state.hover = hit.kind === 'note' ? hit.note.id : null;
    canvas.className = spaceDown ? 'grab' : hit.kind === 'note' ? 'lane' : '';
  });

  window.addEventListener('mouseup', () => {
    if (pan) { pan = null; canvas.classList.remove('grabbing'); return; }
    if (drag) {
      if (drag.moved && drag.change !== undefined) applyChange(drag.change, false);
      drag = null;
      state.preview = null;
      canvas.className = '';
      return;
    }
    if (state.box) {
      const tiny = Math.abs(state.box.x1 - state.box.x0) < 3 && Math.abs(state.box.y1 - state.box.y0) < 3;
      if (tiny && state.box.seekTo !== null) { seek(state.box.seekTo); state.box = null; return; } // 时间尺上单击：跳过去
      const ids = view.notesInBox(state.box, state);
      if (!state.box.add) state.selection = new Set();
      ids.forEach(id => state.selection.add(id));
      if (tiny && !state.box.add) state.selection = new Set();
      state.box = null;
      renderInspector();
    }
  });

  canvas.addEventListener('wheel', e => {
    if (!state.doc) return;
    e.preventDefault();
    const [, y] = pos(e);
    if (e.ctrlKey) {
      const tCursor = view.timeAt(y, state);
      setZoom((state.zoomTarget || state.pxPerSec) * Math.exp(-e.deltaY * 0.0015), tCursor, y);
    } else if (player.playing) {
      seek(state.time - e.deltaY / state.pxPerSec * 0.9);
    } else {
      const base = state.timeTarget !== null ? state.timeTarget : state.time;
      seek(base - e.deltaY / state.pxPerSec * 0.9, true);
    }
  }, { passive: false });

  /**
   * 缩放（平滑过渡）。暂停时以鼠标所在的时间为准不动；播放中以判定线为准、不跳转
   * （复现：播放时按住 Ctrl 滚轮会从头开始播放——原来缩放时会跳转播放位置）。
   */
  const ZOOM_MIN = 30, ZOOM_MAX = 2000; // 每秒多少像素
  const zoomSlider = $('zoomSlider');
  const sliderOf = px => Math.round(Math.log(px / ZOOM_MIN) / Math.log(ZOOM_MAX / ZOOM_MIN) * 1000);
  const pxOfSlider = v => ZOOM_MIN * Math.pow(ZOOM_MAX / ZOOM_MIN, v / 1000);
  function setZoom(px, tCursor, y, fromSlider) {
    const target = Math.max(ZOOM_MIN, Math.min(ZOOM_MAX, px));
    if (!fromSlider) zoomSlider.value = String(sliderOf(target));
    state.zoomTarget = target;
    state.zoomAnchor = !player.playing && tCursor !== undefined ? { time: tCursor, y } : null;
    if (reduceMotion) { applyZoom(target); state.zoomTarget = null; }
    prefs.pxPerSec = target;
    savePrefs();
    $('zoomLabel').textContent = Math.round(target / 240 * 100) + '%';
  }
  function applyZoom(px) {
    state.pxPerSec = px;
    if (state.zoomAnchor && view.geom && !player.playing) {
      state.time = state.zoomAnchor.time - (view.geom.playY - state.zoomAnchor.y) / px;
      state.timeTarget = null;
    }
  }
  zoomSlider.addEventListener('input', () => setZoom(pxOfSlider(Number(zoomSlider.value)), undefined, undefined, true));
  zoomSlider.addEventListener('wheel', e => { // 2026-11-18 用户：鼠标放在滑动条上用滚轮缩放
    e.preventDefault();
    setZoom((state.zoomTarget || state.pxPerSec) * Math.exp(-e.deltaY * 0.0015));
  }, { passive: false });
  $('zoomIn').addEventListener('click', () => setZoom(state.pxPerSec * 1.25));
  $('zoomOut').addEventListener('click', () => setZoom(state.pxPerSec / 1.25));
  $('zoomFit').addEventListener('click', () => {
    if (!state.doc || !view.geom) return;
    const end = songEnd();
    setZoom((view.geom.playY - view.geom.noteTop) / Math.max(1, end));
    seek(0, true);
  });

  // ---------- 键盘 ----------
  window.addEventListener('keydown', e => {
    if (e.target.closest && e.target.closest('input, textarea, select')) return;
    if (!$('modal').hidden) { if (e.key === 'Escape') closeModal(false); return; }
    if (capturing !== null) { captureKey(e); return; }
    const key = e.key, ctrl = e.ctrlKey || e.metaKey;
    if (key === ' ') {
      e.preventDefault();
      // 刚点过的按钮还有焦点时，空格会再「按」一次它：先移走焦点
      if (document.activeElement && document.activeElement.tagName === 'BUTTON') document.activeElement.blur();
      if (!e.repeat) { spaceDown = true; spacePanned = false; }
      return;
    }
    if (!state.doc) { if (ctrl && key.toLowerCase() === 'o') { e.preventDefault(); askOpen(); } return; }
    if (ctrl && key.toLowerCase() === 'z') { e.preventDefault(); e.shiftKey ? redo() : undo(); return; }
    if (ctrl && key.toLowerCase() === 'y') { e.preventDefault(); redo(); return; }
    if (ctrl && key.toLowerCase() === 's') { e.preventDefault(); saveToGame(); return; }
    if (ctrl && key.toLowerCase() === 'a') { e.preventDefault(); state.selection = new Set(state.doc.notes.map(n => n.id)); renderInspector(); return; }
    if (ctrl && key.toLowerCase() === 'o') { e.preventDefault(); askOpen(); return; }
    if (ctrl || e.altKey) return;
    const lane = laneLabels().findIndex(k => k.toUpperCase() === key.toUpperCase());
    if (lane >= 0) { e.preventDefault(); laneKeyPressed(lane); return; }
    switch (key) {
      case 'ArrowLeft': e.preventDefault(); applyChange(M.ops.shift(state.doc, selectedNotes(), -1)); break;
      case 'ArrowRight': e.preventDefault(); applyChange(M.ops.shift(state.doc, selectedNotes(), 1)); break;
      case 'ArrowUp': case 'ArrowDown': e.preventDefault(); stepSelection(key === 'ArrowUp' ? 1 : -1); break;
      case 'm': case 'M': applyChange(M.ops.mirror(state.doc, selectedNotes())); break;
      case 'n': case 'N': nextCheck(); break;
      case 'Home': seek(state.doc.notes[0].time - 1.2, true); break;
      case 'Escape': state.selection = new Set(); renderInspector(); if (openPanel) togglePanel(openPanel); break;
      case '[': setLoop('a'); break;
      case ']': setLoop('b'); break;
      default: return;
    }
  });
  window.addEventListener('keyup', e => {
    if (e.key !== ' ') return;
    if (e.target.closest && e.target.closest('input, textarea, select')) return;
    e.preventDefault();
    spaceDown = false;
    canvas.classList.remove('grab');
    if (!spacePanned) togglePlay();
  });

  /** 上下键：选中下一个 / 上一个音符。 */
  function stepSelection(dir) {
    const notes = state.doc.notes;
    const ids = [...state.selection];
    let i = ids.length ? (dir > 0 ? Math.max(...ids) + 1 : Math.min(...ids) - 1) : view.firstVisible(state.time);
    i = Math.max(0, Math.min(notes.length - 1, i));
    state.selection = new Set([i]);
    const n = notes[i];
    if (!state.playing && (view.yAt(n.time, state) < view.geom.noteTop + 40 || view.yAt(n.time, state) > view.geom.playY)) seek(n.time - 0.4, true);
    renderInspector();
  }

  // ---------- 播放 ----------
  const firstNoteAt = t => view.firstVisible(t);

  function togglePlay() {
    if (!state.doc) return;
    if (state.timeTarget !== null) { state.time = state.timeTarget; state.timeTarget = null; }
    if (player.playing) { player.pause(); state.time = player.time(); }
    else { player.seek(state.time); player.play(); }
    state.playing = player.playing;
    $('playBtn').innerHTML = '<svg><use href="#i-' + (state.playing ? 'pause' : 'play') + '"/></svg>';
    restart($('playBtn'), 'swap');
    $('playBtn').title = state.t(state.playing ? 'tr.pause' : 'tr.play');
    state.lastTime = state.time;
  }
  $('playBtn').addEventListener('click', togglePlay);
  $('toStart').addEventListener('click', () => { if (state.doc) seek(state.loop.on && state.loop.a !== null ? state.loop.a : state.doc.notes[0].time - 1.2, true); });
  $('speed').addEventListener('change', e => { prefs.speed = Number(e.target.value); savePrefs(); player.setRate(prefs.speed, state.doc?.audio?.pitch ?? 1); });

  function setLoop(which) {
    if (!state.doc) return;
    state.loop[which] = state.time;
    if (state.loop.a !== null && state.loop.b !== null && state.loop.b < state.loop.a) [state.loop.a, state.loop.b] = [state.loop.b, state.loop.a];
    if (state.loop.a !== null && state.loop.b !== null) state.loop.on = true;
    player.cancelClicks();
    renderLoop();
  }
  function renderLoop() {
    $('loopA').classList.toggle('on', state.loop.a !== null);
    $('loopB').classList.toggle('on', state.loop.b !== null);
    $('loopBtn').classList.toggle('on', state.loop.on);
  }
  $('loopA').addEventListener('click', () => setLoop('a'));
  $('loopB').addEventListener('click', () => setLoop('b'));
  $('loopBtn').addEventListener('click', () => {
    if (state.loop.on) { state.loop = { a: null, b: null, on: false }; }
    else if (state.loop.a !== null && state.loop.b !== null) state.loop.on = true;
    player.cancelClicks();
    renderLoop();
  });

  function songEnd() {
    const doc = state.doc;
    const last = doc.notes[doc.notes.length - 1];
    return Math.max(last.time + last.lanes.reduce((m, l) => Math.max(m, l[1]), 0) + 1.5, player.end || 0);
  }

  function fmt(t) {
    const sign = t < 0 ? '-' : '';
    t = Math.abs(t);
    const m = Math.floor(t / 60), s = t - m * 60;
    return sign + m + ':' + s.toFixed(3).padStart(6, '0');
  }

  // ---------- 每一帧 ----------
  /**
   * 框选拖到画布上下边缘时自动滚动（越靠边越快；谱面从上往下落，往上是往后）。只在暂停时滚，播放时谱面本来就在走。
   * 框的起点按谱面时间记（box.t0），滚动后重新算出它在屏幕上的位置。
   */
  const BOX_EDGE = 56;
  let lastFrameAt = 0;
  function followBox(now) {
    const dt = lastFrameAt ? Math.min(0.05, (now - lastFrameAt) / 1000) : 0;
    lastFrameAt = now;
    const box = state.box;
    if (!box || !state.doc) return;
    if (!player.playing && dt > 0) {
      const h = canvas.clientHeight, y = box.y1;
      const k = y < BOX_EDGE ? (BOX_EDGE - y) / BOX_EDGE : y > h - BOX_EDGE ? -(y - (h - BOX_EDGE)) / BOX_EDGE : 0;
      if (k) {
        const notes = state.doc.notes, speed = Math.sign(k) * Math.min(2, k * k) * 1400; // 像素每秒
        const t = state.time + speed * dt / state.pxPerSec;
        state.timeTarget = null;
        state.time = Math.max(notes[0].time - 2, Math.min(Math.max(songEnd(), notes[notes.length - 1].time + 2), t));
      }
    }
    box.y0 = view.yAt(box.t0, state);
  }

  function frame() {
    state.now = performance.now();
    stepInset(state.now);
    followBox(state.now);
    if (state.zoomTarget !== null) {
      const px = state.pxPerSec + (state.zoomTarget - state.pxPerSec) * 0.25;
      applyZoom(Math.abs(state.zoomTarget - px) < 0.5 ? state.zoomTarget : px);
      if (state.pxPerSec === state.zoomTarget) { state.zoomTarget = null; state.zoomAnchor = null; }
    }
    if (state.timeTarget !== null && !player.playing) {
      const d = state.timeTarget - state.time;
      if (Math.abs(d) < 0.002) { state.time = state.timeTarget; state.timeTarget = null; player.seek(state.time); }
      else state.time += d * 0.22;
    }
    if (state.doc) {
      if (player.playing) {
        const prev = state.lastTime;
        let t = player.time();
        if (state.loop.on && state.loop.b !== null && t >= state.loop.b) { t = state.loop.a; player.seek(t); }
        const end = songEnd();
        if (t > end) { togglePlay(); t = end; }
        state.time = t;
        if (state.hitsound) player.scheduleClicks(t, state.doc.notes, firstNoteAt, state.loop.on ? state.loop.b : Infinity);
        // 走过判定线的音符：键帽亮一下
        if (t > prev && t - prev < 0.5) {
          for (let i = view.firstVisible(prev); i < state.doc.notes.length && state.doc.notes[i].time <= t; i++) {
            const n = state.doc.notes[i], color = window.ChartColorOf(n);
            n.lanes.forEach(([lane]) => { state.flash[lane] = { t: state.now, color }; }); // 和游戏里一样：命中时按键框闪一下音符的颜色
          }
        }
        state.lastTime = t;
      }
      state.playing = player.playing;
      $('timeNow').textContent = fmt(state.time);
      $('timeEnd').textContent = '/ ' + fmt(songEnd()).replace(/\.\d+$/, '');
    }
    view.draw(state);
    if (prefs.track && state.doc) track.draw(state.time, state.t('view.track'), state.t('view.experimental'));
    drawMinimap();
    requestAnimationFrame(frame);
  }

  // ---------- 概览 ----------
  const mini = $('minimap'), mg = mini.getContext('2d');
  function drawMinimap() {
    const r = mini.getBoundingClientRect(), dpr = Math.max(1, window.devicePixelRatio || 1);
    if (mini.width !== Math.round(r.width * dpr) || mini.height !== Math.round(r.height * dpr)) {
      mini.width = Math.round(r.width * dpr);
      mini.height = Math.round(r.height * dpr);
    }
    mg.setTransform(dpr, 0, 0, dpr, 0, 0);
    const w = r.width, h = r.height;
    mg.clearRect(0, 0, w, h);
    const doc = state.doc;
    if (!doc || !w) return;
    const end = songEnd(), X = t => Math.max(0, Math.min(w, t / end * w));
    const cols = Math.max(1, Math.floor(w / 2)), colW = w / cols;

    // 波形打底（上下对称，淡）
    if (state.waveform) {
      const wf = state.waveform;
      mg.fillStyle = 'rgba(170,180,200,0.16)';
      for (let c = 0; c < cols; c++) {
        const i0 = Math.floor((c / cols * end + state.audioOffset) * wf.perSecond), i1 = Math.floor(((c + 1) / cols * end + state.audioOffset) * wf.perSecond);
        let v = 0;
        for (let i = Math.max(0, i0); i < Math.min(wf.peaks.length, i1); i++) v = Math.max(v, wf.peaks[i]);
        const bh = v / 255 * (h - 6);
        mg.fillRect(c * colW, (h - bh) / 2, Math.max(1, colW - 0.5), bh);
      }
    }

    // 音符密度柱：每一小段有多少音符，颜色看这一段最多的是单押、双押还是三押以上；有自动补齐的段描黄
    const count = new Array(cols).fill(0), chord = new Array(cols).fill(0), multi = new Array(cols).fill(0), auto = new Array(cols).fill(false);
    for (const n of doc.notes) {
      const c = Math.min(cols - 1, Math.floor(n.time / end * cols));
      count[c]++;
      if (n.lanes.length > 2) multi[c]++; else if (n.lanes.length > 1) chord[c]++;
      if (n.auto) auto[c] = true;
    }
    const most = Math.max(1, ...count);
    for (let c = 0; c < cols; c++) {
      if (!count[c]) continue;
      const bh = Math.max(2, Math.sqrt(count[c] / most) * (h - 10));
      mg.fillStyle = auto[c] ? '#f2c14e' : multi[c] * 3 >= count[c] ? '#b273f2' : chord[c] * 2 >= count[c] ? '#3d9ee0' : '#f57829';
      mg.globalAlpha = 0.85;
      mg.fillRect(c * colW, h - 3 - bh, Math.max(1, colW - 0.6), bh);
    }
    mg.globalAlpha = 1;

    // 视窗：外面压暗、框描边
    if (view.geom) {
      const ta = view.timeAt(view.geom.bottom, state), tb = view.timeAt(view.geom.noteTop, state);
      const x0 = X(ta), x1 = Math.max(x0 + 3, X(tb));
      mg.fillStyle = 'rgba(13,14,16,0.55)';
      mg.fillRect(0, 0, x0, h);
      mg.fillRect(x1, 0, w - x1, h);
      mg.strokeStyle = 'rgba(245,120,41,0.95)';
      mg.lineWidth = 1.5;
      mg.beginPath();
      mg.roundRect ? mg.roundRect(x0 + 0.75, 0.75, x1 - x0 - 1.5, h - 1.5, 4) : mg.rect(x0 + 0.75, 0.75, x1 - x0 - 1.5, h - 1.5);
      mg.stroke();
    }

    // 播放头：白线加一个小三角
    const px = X(state.time);
    mg.fillStyle = '#ffffff';
    mg.fillRect(px - 0.75, 0, 1.5, h);
    mg.beginPath();
    mg.moveTo(px - 4, 0);
    mg.lineTo(px + 4, 0);
    mg.lineTo(px, 5);
    mg.closePath();
    mg.fill();
  }
  let miniDrag = false;
  const miniSeek = e => {
    if (!state.doc) return;
    const r = mini.getBoundingClientRect();
    seek(Math.max(0, Math.min(1, (e.clientX - r.left) / r.width)) * songEnd());
  };
  mini.addEventListener('mousedown', e => { miniDrag = true; miniSeek(e); });
  window.addEventListener('mousemove', e => { if (miniDrag) miniSeek(e); });
  window.addEventListener('mouseup', () => { miniDrag = false; });

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
  const getFolder = () => idb('readonly', s => s.get(folderKey())).catch(() => null);
  const putFolder = handle => idb('readwrite', s => s.put(handle, folderKey())).catch(() => null);

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

  function saved() {
    state.history.markSaved();
    metaDirty = false;
    writeDraft(); // 草稿留着、记成已保存：从「历史」打开这一关时还是保存时的样子
    renderHeader();
  }

  /** 游戏的手序文件夹（读写权限，句柄记在 IndexedDB 里）：没记住或没权限就问玩家、让他选；玩家取消为 null。 */
  async function fingeringsFolder() {
    let dir = await getFolder();
    if (dir && (await dir.queryPermission({ mode: 'readwrite' })) !== 'granted' && (await dir.requestPermission({ mode: 'readwrite' })) !== 'granted') dir = null;
    if (dir) return dir;
    const folder = (state.doc && state.doc.saveFolder) || '…/Mods/RhythmHint/Fingerings';
    // 2026-11-19 玩家截图：游戏装在 C:\Program Files (x86) 时浏览器不让选游戏里的文件夹，Mod 改让这里存到文档里的中转文件夹
    const inbox = !!(state.doc && state.doc.saveStartIn);
    if (!(await modal(state.t('save.pickTitle'), state.t(inbox ? 'save.pickBodyInbox' : 'save.pickBody'), folder, state.t('save.pickGo')))) return null;
    dir = await window.showDirectoryPicker(inbox
      ? { id: 'rhythmhint-fingerings-inbox', mode: 'readwrite', startIn: state.doc.saveStartIn }
      : { id: 'rhythmhint-fingerings', mode: 'readwrite' });
    if (dir.name.toLowerCase() !== 'fingerings' && !(await modal(state.t('save.pickTitle'), state.t('save.wrongFolder', dir.name), '', state.t('save.ok')))) return null;
    await putFolder(dir);
    return dir;
  }

  async function saveToGame() {
    const doc = state.doc;
    if (!doc) return;
    const text = M.toFileText(doc);
    const folder = doc.saveFolder || '…/Mods/RhythmHint/Fingerings';
    if (!window.showDirectoryPicker) {
      download(text, doc.fileName);
      saved();
      toast(state.t('save.downloaded', doc.fileName, folder), 'warn', null, 12000);
      return;
    }
    try {
      const dir = await fingeringsFolder();
      if (!dir) return;
      const writable = await (await dir.getFileHandle(doc.fileName, { create: true })).createWritable();
      await writable.write(text);
      await writable.close();
      saved();
      toast(state.t('save.done', doc.fileName), 'ok', null, 6000);
    } catch (e) {
      if (e && e.name === 'AbortError') return; // 玩家在选文件夹时点了取消
      toast(state.t('save.failed', errorText(e)), 'error');
    }
  }
  $('saveBtn').addEventListener('click', saveToGame);
  $('saveAsBtn').addEventListener('click', () => {
    if (!state.doc) return;
    download(M.toFileText(state.doc), state.doc.fileName);
    toast(state.t('saveAs.done', state.doc.fileName), 'ok');
  });

  window.addEventListener('beforeunload', e => {
    if (draftTimer) writeDraft(); // 刚改的还没存进草稿：关网页前存上
    if (state.history && (state.history.dirty || metaDirty)) { e.preventDefault(); e.returnValue = ''; }
  });

  // ---------- 历史 ----------
  // 2026-11-18 用户：很多人一次改不完整张谱就把网页关了，不开游戏很难再找到网页和之前改的东西。
  // Mod 每次「编辑本关手序」把那一关另存到 recent/（SessionLibrary，最多 10 关），这里列出来，点一下带着音乐、轨道预览接着改；
  // 改到一半的音符在草稿里（下面），换关卡、关网页都不丢。
  const recent = M.parseRecent(window.RHYTHMHINT_RECENT);
  const DATE_FORMAT = { month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' };
  const finite = v => typeof v === 'number' && Number.isFinite(v);
  const whenText = ms => new Date(ms).toLocaleString(state.t.language, DATE_FORMAT);

  /** 一关现在的状态和最后改动的时间：正在编辑的看编辑器里的，别的看浏览器里存的草稿。 */
  function progressOf(item) {
    const doc = state.doc;
    if (doc && doc.sessionId === item.id) {
      const unsaved = state.history.dirty || metaDirty, changed = M.stats(doc).changed;
      const draft = readJson(DRAFT_PREFIX + item.id);
      return { unsaved: unsaved ? Math.max(1, changed) : 0, saved: !unsaved && changed > 0, when: draft && finite(draft.time) ? draft.time : item.exported };
    }
    const draft = readJson(DRAFT_PREFIX + item.id);
    if (!draft || !Array.isArray(draft.lanes)) return { unsaved: 0, saved: false, when: item.exported };
    return { unsaved: draft.saved ? 0 : Math.max(1, draft.changed || 0), saved: !!draft.saved, when: finite(draft.time) ? draft.time : item.exported };
  }

  function renderHistory() {
    const list = $('historyList');
    $('copyPage').title = pagePath(); // 悬停看完整路径
    if (!recent.length) {
      list.innerHTML = '<p class="muted">' + esc(state.t('history.empty')) + '</p>';
      return;
    }
    list.innerHTML = recent.map((item, i) => {
      const current = state.doc && state.doc.sessionId === item.id;
      const p = progressOf(item);
      const badge = p.unsaved ? '<span class="state unsaved">' + esc(state.t('history.unsaved', p.unsaved)) + '</span>'
        : p.saved ? '<span class="state saved">' + esc(state.t('history.saved')) + '</span>' : '';
      const sub = M.plain(item.artist);
      return '<button class="history-item' + (current ? ' current' : '') + '" data-index="' + i + '"' + (current ? ' aria-current="true"' : '') + '>'
        + '<span class="row1"><span class="title">' + esc(M.plain(item.song) || item.fileName) + '</span>' + badge + '</span>'
        + '<span class="row2"><span class="sub">' + esc(sub) + '</span><span class="when">' + esc(whenText(p.when)) + '</span></span>'
        + '</button>';
    }).join('');
  }

  /** 这个网页在电脑上的位置（不开游戏时从这里打开）。 */
  function pagePath() {
    try {
      const path = decodeURIComponent(location.pathname);
      return location.protocol === 'file:' ? path.replace(/^\/([A-Za-z]:)/, '$1').replace(/\//g, '\\') : location.href;
    } catch (e) {
      return location.href; // 地址里有解不开的 %：原样给出
    }
  }

  /** 载入 recent/ 里的一关（file:// 下只能用 <script> 读本地文件）。 */
  function loadRecent(item) {
    return new Promise((resolve, reject) => {
      window.RHYTHMHINT_SESSION = undefined;
      const el = document.createElement('script');
      el.src = 'recent/' + item.session + '?v=' + Date.now();
      el.onload = () => { el.remove(); window.RHYTHMHINT_SESSION ? resolve(window.RHYTHMHINT_SESSION) : reject(new Error(item.session)); };
      el.onerror = () => { el.remove(); reject(new Error(item.session)); };
      document.head.appendChild(el);
    });
  }

  $('historyList').addEventListener('click', async e => {
    const button = e.target.closest('button[data-index]');
    const item = button && recent[Number(button.dataset.index)];
    if (!item || (state.doc && state.doc.sessionId === item.id)) return;
    if (draftTimer) writeDraft(); // 这一关刚改的先存进草稿，换过去再换回来还在
    let s;
    try {
      s = await loadRecent(item);
    } catch (err) {
      toast(state.t('history.missing'), 'error', null, 8000);
      return;
    }
    try {
      loadDoc(M.fromSession(s), 'game');
    } catch (err) {
      toast(state.t('toast.openFailed', errorText(err)), 'error');
    }
  });
  $('copyPage').addEventListener('click', e => copy(pagePath(), e.currentTarget));

  // ---------- 关于 ----------
  // 作者、贡献者、仓库、交流群（about.js；和 Mod 设置窗口的「关于」是同一份，about.test.cjs 核对）
  (function renderAbout() {
    const about = window.EditorAbout;
    // 身份（作者 / 贡献者）的文字由 applyLanguage 按 data-i18n 填
    const person = (name, url, role, tone) => '<a class="person" href="' + esc(url) + '" target="_blank" rel="noopener">'
      + '<span class="avatar' + tone + '" aria-hidden="true">' + esc(name.charAt(0).toUpperCase()) + '</span>'
      + '<span class="loc-text"><b>' + esc(name) + '</b><small data-i18n="' + role + '"></small></span>'
      + '<svg class="ext"><use href="#i-external"/></svg></a>';
    $('aboutPeople').innerHTML = person(about.author, about.authorUrl, 'about.author', '')
      + about.contributors.map(c => person(c.name, c.url, 'about.contributor', ' blue')).join('');
    $('aboutRepo').href = about.repo;
    $('aboutRepoText').textContent = about.repo.replace(/^https:\/\/github\.com\//, '');
    $('aboutQq').textContent = about.qq;
    $('copyQq').addEventListener('click', e => copy(about.qq, e.currentTarget));
  })();

  // ---------- 轨道预览：滚轮拉近拉远、双击还原、窗口放大 ----------
  // 2026-11-18 用户：鼠标放在轨道预览上用滚轮控制离星球多近，窗口最好能放大
  track.setZoom(prefs.trackZoom, true);
  $('track').addEventListener('wheel', e => {
    e.preventDefault();
    prefs.trackZoom = track.setZoom(track.zoomTarget * Math.exp(-e.deltaY * 0.0015));
    savePrefs();
  }, { passive: false });
  $('track').addEventListener('dblclick', () => { prefs.trackZoom = track.setZoom(1); savePrefs(); });
  function trackSizeTitle() {
    const button = $('trackSize');
    button.title = state.t(prefs.trackBig ? 'track.shrink' : 'track.expand');
    button.querySelector('use').setAttribute('href', prefs.trackBig ? '#i-shrink' : '#i-expand');
  }
  $('trackSize').addEventListener('click', e => {
    prefs.trackBig = !prefs.trackBig;
    savePrefs();
    $('trackCard').classList.toggle('big', prefs.trackBig);
    trackSizeTitle();
    e.currentTarget.blur();
  });
  $('trackCard').classList.toggle('big', prefs.trackBig);
  // 放大缩小有过渡：画布跟着卡片的大小重新分配像素
  if (window.ResizeObserver) new ResizeObserver(() => { if (prefs.track) track.resize(); }).observe($('trackCard'));

  // ---------- 草稿 ----------
  // 这一关在编辑器里现在的样子，随时存在这台电脑的浏览器里（按「历史」里的 Id；打开的是手序文件时按谱面签名）。
  // 关掉网页、换到别的关卡都不丢，下次打开这一关自动接着改；保存到游戏后也留着（记成已保存）。
  const DRAFT_PREFIX = 'rhythmhint.editor.draft.';
  const draftKeyOf = doc => DRAFT_PREFIX + (doc.sessionId || doc.signature);
  let draftTimer = 0;
  function writeDraft() {
    clearTimeout(draftTimer);
    draftTimer = 0;
    const doc = state.doc;
    if (!doc) return;
    const draft = M.draftOf(doc, !(state.history.dirty || metaDirty));
    if (draft.saved && draft.changed === 0) removeKey(draftKeyOf(doc)); // 和打开时一样、也没有要保存的：不用记
    else writeJson(draftKeyOf(doc), draft);
    if (openPanel === 'history') renderHistory();
  }
  function scheduleDraft() {
    clearTimeout(draftTimer);
    draftTimer = setTimeout(writeDraft, 600); // 有意的延时：连续改动时少写几次
  }

  /** 打开一关时套上上次的进度（可撤销）；「不用了」回到游戏这次给的样子。 */
  function restoreDraft() {
    const doc = state.doc;
    let draft = readJson(draftKeyOf(doc));
    if (!draft && doc.sessionId) draft = readJson(DRAFT_PREFIX + doc.signature); // 早期测试版按谱面签名存的草稿
    const change = draft && M.draftChange(doc, draft);
    if (!change) return;
    const author = typeof draft.author === 'string' ? draft.author : doc.meta.author;
    const comment = typeof draft.comment === 'string' ? draft.comment : doc.meta.comment;
    const metaChanged = author !== doc.meta.author || comment !== doc.meta.comment;
    if (!change.size && !metaChanged) return;
    const before = new Map([...change.keys()].map(id => [id, doc.notes[id].lanes]));
    const meta = { author: doc.meta.author, comment: doc.meta.comment };
    if (change.size) state.history.apply(change);
    doc.meta.author = author;
    doc.meta.comment = comment;
    if (draft.saved) state.history.markSaved(); // 和保存到游戏的文件一样：不算没保存
    metaDirty = !draft.saved && metaChanged;
    edited(true); // 不重写草稿：草稿里记的是上次改动的时间
    renderFilePanel();
    toast(state.t(draft.saved ? 'draft.restoredSaved' : 'draft.restored', change.size), 'ok', [
      [state.t('draft.discard'), () => {
        if (state.doc !== doc) return;
        if (before.size) state.history.apply(before);
        doc.meta.author = meta.author;
        doc.meta.comment = meta.comment;
        state.history.markSaved();
        metaDirty = false;
        removeKey(draftKeyOf(doc));
        edited(true);
        renderFilePanel();
        if (openPanel === 'history') renderHistory();
      }],
    ], 12000);
  }

  // ---------- 尺寸 ----------
  // 左侧面板开关时谱面让出的宽度慢慢变过去（2026-11-18 用户：原来一下子跳过去很生硬），和面板滑进来的时间差不多
  let insetTween = null;
  const INSET_MS = 260;
  function resize() {
    view.resize();
    if (prefs.track) track.resize();
    const left = openPanel ? 354 : 0;
    const target = insetTween ? insetTween.to : state.leftInset;
    if (left !== target) {
      if (reduceMotion || !state.doc) { state.leftInset = left; insetTween = null; }
      else insetTween = { from: state.leftInset, to: left, start: performance.now() };
    }
    state.rightInset = state.doc ? 312 : 0;
  }
  function stepInset(now) {
    if (!insetTween) return;
    const k = Math.min(1, (now - insetTween.start) / INSET_MS), ease = 1 - Math.pow(1 - k, 3);
    state.leftInset = insetTween.from + (insetTween.to - insetTween.from) * ease;
    if (k >= 1) insetTween = null;
  }
  window.addEventListener('resize', resize);

  // ---------- 开始 ----------
  langSelect.value = I18N.LANGUAGES.some(([c]) => c === pickLanguage()) ? pickLanguage() : 'en';
  applyLanguage(langSelect.value);
  $('zoomLabel').textContent = Math.round(state.pxPerSec / 240 * 100) + '%';
  zoomSlider.value = String(sliderOf(state.pxPerSec));
  document.querySelectorAll('[data-panel-body]').forEach(el => { el.hidden = true; });
  if (session) {
    try { loadDoc(M.fromSession(session), 'game'); } catch (e) { toast(state.t('toast.openFailed', errorText(e)), 'error'); $('empty').hidden = false; }
  } else {
    $('empty').hidden = false;
    renderHeader();
    if (recent.length) togglePanel('history');
  }
  if (session && !prefs.wayBackTold) {
    prefs.wayBackTold = true;
    savePrefs();
    toast(state.t('history.tip'), '', [[state.t('history.gotIt'), () => {}, true], [state.t('history.show'), () => { if (openPanel !== 'history') togglePanel('history'); }]], 30000);
  }
  renderInspector();
  renderShortcuts();
  resize();
  requestAnimationFrame(frame);
})();
