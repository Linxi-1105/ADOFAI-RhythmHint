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
  /** 存进 localStorage；存不了（写满了、隐私模式）返回 false，要不要告诉玩家由调用的地方决定。 */
  function writeText(key, text) {
    try {
      localStorage.setItem(key, text);
      return true;
    } catch (e) {
      console.warn('RhythmHint: cannot write ' + key, e);
      return false;
    }
  }
  const writeJson = (key, value) => writeText(key, JSON.stringify(value));
  function removeKey(key) {
    try { localStorage.removeItem(key); } catch (e) { console.warn('RhythmHint: cannot remove ' + key, e); } // 删不掉只是多占点地方
  }
  const savePrefs = () => writeJson(PREFS_KEY, prefs); // 偏好存不了不影响编辑，不提示（草稿存不了才提示）

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
  /** 提示条。actions：[[按钮文字, 点了做什么, 是不是主按钮]]；onClose：提示条关掉时（点了按钮或到时间）。返回关掉它的函数。 */
  function toast(text, kind, actions, ms, onClose) {
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
    let closed = false;
    function close() {
      clearTimeout(timer);
      if (closed) return;
      closed = true;
      if (onClose) onClose();
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
  let openSeq = 0; // 每打开一次加一：还在载入的旧请求回来时认得出自己过时了（历史里快速连点两关）

  /** 打开一份：make 每次调用都重新读出一份（另一个标签页改了这一关时用它重新打开）。读不出来时抛出，原来的照旧。 */
  function openDoc(make, source) {
    const doc = make();
    state.make = make;
    loadDoc(doc, source);
    return doc;
  }

  function loadDoc(doc, source) {
    openSeq++;
    player.pause();
    state.doc = doc;
    state.history = new M.History(doc);
    state.source = source;
    resetView();
    applyNoteStyle();
    M.remember(doc); // 打开文件时文件名是在读出来之后才换的
    // 草稿按「历史」里的 Id 存；打开的是手序文件时按谱面签名加文件内容（两份不同的文件各有各的草稿）
    doc.draftKey = DRAFT_PREFIX + (doc.sessionId || M.fileDraftId(doc));
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
    audioSeq++; // 上一关还在载入、解码的音乐（包括手动选的）回来时不再用
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
    renderLoop();
    renderPlayButton();
    edited(true);
    renderFilePanel(); // 文件面板、参考面板（有没有自动排键参考）跟着这一关（原来只在开着文件面板时更新，参考面板的卡片一直藏着）
    restoreDraft();
    if (openPanel === 'history') renderHistory();
    tellClashes();
  }

  /** 换一关时上一关留下的东西都清掉（2026-11-19 评审：没保存的标记、平滑滚动的目标、循环按钮、播放图标会留着）。 */
  function resetView() {
    state.selection.clear();
    state.loop = { a: null, b: null, on: false };
    state.flash = [];
    state.press = [];
    state.anims.clear();
    state.hover = null;
    state.preview = null;
    state.box = null;
    state.timeTarget = null;
    state.zoomAnchor = null;
    state.playing = false;
    drag = null;
    pan = null;
    pendingModifier = null;
    metaDirty = false;
    inspectorKey = '';
    allowDraft();
  }

  function openFromText(text, name) {
    try {
      const doc = openDoc(() => {
        const d = M.fromFileText(text);
        d.fileName = name || d.fileName;
        return d;
      }, 'file');
      toast(state.t('toast.opened', doc.opened.fileName), 'ok'); // 打开的那个文件（草稿里记的文件名可能不一样）
    } catch (e) {
      toast(state.t('toast.openFailed', errorText(e)), 'error');
    }
  }

  /** 打开选的或拖进来的手序文件：和 Mod 一样超过 20 MB 不读；读不出来告诉玩家。 */
  async function openFile(file) {
    const limit = M.MAX_FILE_BYTES / 1024 / 1024;
    if (file.size > M.MAX_FILE_BYTES) { toast(state.t('toast.tooBig', file.name, limit), 'error', null, 8000); return; }
    let text;
    try {
      text = await file.text();
    } catch (e) {
      toast(state.t('toast.openFailed', errorText(e)), 'error');
      return;
    }
    openFromText(text, file.name);
  }

  function askOpen() {
    if (state.history && state.history.dirty && !window.confirm(state.t('toast.confirmOpen'))) return;
    $('openInput').click();
  }
  $('openInput').addEventListener('change', e => {
    const file = e.target.files[0];
    e.target.value = '';
    if (file) openFile(file);
  });
  document.querySelectorAll('[data-action="open"]').forEach(b => b.addEventListener('click', askOpen));

  // 拖进来：手序文件就打开，音乐就载入
  window.addEventListener('dragover', e => e.preventDefault());
  window.addEventListener('drop', e => {
    e.preventDefault();
    const file = e.dataTransfer.files[0];
    if (!file) return;
    if (/\.json$/i.test(file.name)) {
      if (state.history && state.history.dirty && !window.confirm(state.t('toast.confirmOpen'))) return;
      openFile(file);
    } else if (file.type.startsWith('audio/') || /\.(ogg|mp3|wav|flac|m4a)$/i.test(file.name)) loadAudioFile(file);
  });

  // ---------- 音乐 ----------
  let manualAudio = '';
  // 每换一次音乐（打开关卡、手动选歌）加一：还在载入、解码的旧音乐回来时认得出自己过时了
  // （2026-11-19 评审：刚打开关卡时拖入一首歌，关卡的歌先解完就把手动那首换掉了，没有提示）
  let audioSeq = 0;

  // 整首解码一次只解一首（一首 5 分钟立体声解出来约 110 MB 采样）：正在解的时候换了关卡，新关卡排队等它解完；
  // 排队时又换了，还没开始的直接丢掉。正在解的没法中途停下，解完的结果作废、不留引用（2026-11-19 评审：连点几关几首同时解码）
  let decodeBusy = false, decodeNext = null;
  /**
   * Mod 另存的歌曲数据（file:// 下读不了音乐文件的字节，只能用 <script> 载入）：整首解码后换给播放器，跳转才准。
   * 解码好之前先用 <audio> 播；旧版本导出的历史没有这份数据、解码失败时一直用 <audio>。
   */
  function loadDecoded(doc) {
    decodeNext = { doc, seq: audioSeq };
    if (!decodeBusy) runDecode();
  }
  async function runDecode() {
    decodeBusy = true;
    while (decodeNext) {
      const job = decodeNext;
      decodeNext = null;
      const current = () => state.doc === job.doc && audioSeq === job.seq; // 还是这一关、也没手动换过音乐
      if (!current()) continue;
      try {
        const buffer = await decodeSong(job.doc, current);
        if (buffer && current()) { // 过时的结果直接丢掉
          player.useBuffer(buffer);
          if (!state.waveform) startWaveform(buffer); // 自制关卡游戏里读不到采样，这里补上波形
          renderAudioName();
        }
      } catch (e) {
        console.warn('RhythmHint: decoding the song failed, keeping <audio>', e);
      }
    }
    decodeBusy = false;
  }
  /** 载入这一关的歌曲脚本、整首解码；脚本不在了、载入期间换了关卡为 null。 */
  function decodeSong(doc, current) {
    return new Promise((resolve, reject) => {
      const key = doc.audio.file, tag = document.createElement('script');
      tag.src = doc.audio.script + '?v=' + Date.now();
      tag.onload = () => {
        tag.remove();
        const all = window.RHYTHMHINT_AUDIO || {};
        let text = all[key];
        delete all[key]; // 字节交给解码后就不再留着这份文本
        if (typeof text !== 'string' || !current()) { resolve(null); return; }
        try {
          let bytes = window.EditorPlayer.bytesFromBase64(text);
          text = null; // 换成字节后不再留着几十 MB 的文本，解码期间就能回收
          const decoding = window.EditorPlayer.decode(bytes, player.ctx && player.ctx.sampleRate);
          bytes = null;
          decoding.then(resolve, reject);
        } catch (e) {
          reject(e);
        }
      };
      tag.onerror = () => { tag.remove(); resolve(null); }; // 文件不在了：继续用 <audio>
      document.head.appendChild(tag);
    });
  }
  async function loadAudioFile(file) {
    const seq = ++audioSeq; // 之后关卡自己的歌解完也不再换上去
    try {
      const url = URL.createObjectURL(file);
      if (!await player.load(url) || seq !== audioSeq) return; // 载入期间又换了音乐（换关卡、又选了一首）
      manualAudio = file.name;
      state.audioOffset = player.offset;
      renderAudioName();
      const buffer = await window.EditorPlayer.decode(new Uint8Array(await file.arrayBuffer()), player.ctx && player.ctx.sampleRate);
      if (seq !== audioSeq) return; // 解码期间又换了音乐
      player.useBuffer(buffer);
      startWaveform(buffer);
    } catch (e) {
      if (seq === audioSeq) toast(state.t('audio.failed', errorText(e)), 'error');
    }
  }
  // 解码好的音乐分段算波形，每帧算一点、算好的先画（整首一次算完时长歌会让画面冻住零点几秒、打拍声漏掉）
  const WAVE_MS = 3; // 每帧最多花在算波形上的毫秒数
  let waveJob = null;
  function startWaveform(buffer) {
    waveJob = window.EditorPlayer.waveform(buffer, 100);
    state.waveform = waveJob;
  }
  function stepWaveform() {
    if (!waveJob) return;
    // 换了关卡或音乐（波形换成了别的）就不用再算
    if (state.waveform !== waveJob || waveJob.step(performance.now() + WAVE_MS)) waveJob = null;
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
    $('changeFolder').hidden = !window.showDirectoryPicker || !doc; // 别的浏览器是下载，没有保存位置可换
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
    // 输入框里只写名字，.json 固定显示在框里；控制字符、路径符号、Windows 保留名照建议名一样处理
    const name = M.cleanFileName(e.target.value.trim().replace(/\.json$/i, '')) || M.suggestedFileName(state.doc.meta).replace(/\.json$/i, '');
    e.target.value = name;
    if (state.doc.fileName === name + '.json') return;
    state.doc.fileName = name + '.json';
    markMetaDirty(); // 换了名字还没存：游戏里还是原来那个文件。文件名也进草稿，从历史接着改时沿用
  });
  let metaDirty = false, metaEdits = 0; // metaEdits：改了几次作者、说明、文件名（保存途中又改了，存完照样算没保存）
  function markMetaDirty() { metaDirty = true; metaEdits++; renderHeader(); scheduleDraft(); }
  function copy(text, button) {
    navigator.clipboard.writeText(text).then(() => toast(state.t('file.copied'), 'ok', null, 1600), () => {});
    if (button) button.blur();
  }
  $('copyFolder').addEventListener('click', e => copy($('fFolder').textContent, e.currentTarget));

  function renderShortcuts() {
    const labels = shownLabels();
    const rows = [
      ['keys.play', '<kbd>Space</kbd>'], ['keys.lane', labels.map(k => '<kbd>' + esc(k) + '</kbd>').join('')],
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
    M.markReview(doc); // 改过的自动补齐音符不再待检查（撤销回去又算）
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
  /** 四条轨道的键（游戏里的键名，可能是 Keypad4、LeftShift 这样的 Unity 键名）。 */
  function laneLabels() {
    const doc = state.doc;
    if (!doc) return ['D', 'F', 'J', 'K'];
    return Array.isArray(prefs.laneKeys) && prefs.laneKeys.length === doc.keys ? prefs.laneKeys : doc.keyLabels;
  }
  /** 键帽上显示的字（和游戏里一样缩写：Keypad4 → Num4）；没指定键的轨道显示 —。 */
  const shownLabels = () => laneLabels().map(k => M.keyDisplay(k) || '—');
  function renderKeymap() {
    const labels = shownLabels();
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
    const clashes = M.shortcutClashes(laneLabels());
    $('keysClash').hidden = !clashes.length;
    $('keysClash').textContent = clashes.length ? state.t('keys.clash', clashes.join(' ')) : '';
  }
  /**
   * 轨道键和编辑器的快捷键（M、N、[、]、空格、方向键、Home）重复时，按下按轨道键处理，快捷键用不了：提示一次
   * （2026-11-19 评审：键位是 Z X N M 时按 N、M 不跳转、不镜像，变成了改键）。同一组键位只在打开时提示一次。
   */
  function tellClashes() {
    const clashes = M.shortcutClashes(laneLabels());
    const which = laneLabels().join(' ');
    if (!clashes.length || prefs.clashTold === which) return;
    prefs.clashTold = which;
    savePrefs();
    toast(state.t('keys.clash', clashes.join(' ')), 'warn', null, 10000);
  }
  /** 等新键时按下的键：合法就记下（和别的轨道重复就互换），Esc 取消；按着 Ctrl、Alt 的组合键不录、接着等。 */
  function captureKey(e) {
    e.preventDefault();
    if (e.ctrlKey || e.altKey || e.metaKey) return;
    const key = e.key;
    if (key !== 'Escape' && key.length === 1 && key !== ' ') {
      const labels = laneLabels().slice();
      const k = key.toUpperCase(), other = labels.findIndex(x => x.toUpperCase() === k);
      if (other >= 0 && other !== capturing) labels[other] = labels[capturing];
      labels[capturing] = k;
      prefs.laneKeys = labels;
      prefs.clashTold = labels.join(' '); // 在这里提示过了，下次打开不再提示
      savePrefs();
      if (M.shortcutClashes([k]).length) toast(state.t('keys.clash', M.keyDisplay(k)), 'warn', null, 10000);
    }
    capturing = null;
    renderKeymap();
    renderShortcuts();
    renderInspector();
  }
  $('keysReset').addEventListener('click', () => { prefs.laneKeys = null; savePrefs(); capturing = null; renderKeymap(); renderShortcuts(); renderInspector(); tellClashes(); });
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
    // 本来就在这条轨道上的：玩家看过了、就这么按，自动补齐的不再算待检查
    const kept = notes.filter(n => n.lanes.length === 1 && n.lanes[0][0] === lane && !change.has(n.id));
    applyChange(change);
    confirmNotes(kept);
    // 只选中一个音符时，改完自动跳到下一个（用户要求：用 D F J K 一路改下去；本来就在这条轨道也算确认，照样往下走）
    if (prefs.autoNext && notes.length === 1) stepSelection(1);
  }

  /** 按了音符现在的键、没有改动：自动补齐的记成看过了（2026-11-19 评审：原来改过、看过的待检查标记都不消失）。确认状态进草稿。 */
  function confirmNotes(notes) {
    if (M.confirm(notes)) edited(false);
  }

  /** 需要检查的：还要检查的自动补齐音符（一段只算一处，跳到这一段的开头）和连按。 */
  function checkList() { return M.reviewList(state.doc, state.jacks); }
  function countJackPairs() {
    let count = 0;
    const doc = state.doc;
    for (let i = 1; i < doc.notes.length; i++)
      if (state.jacks.has(doc.notes[i].id) && state.jacks.has(doc.notes[i - 1].id)) count++;
    return count;
  }

  /**
   * 下一处待检查：从选中的音符往后找，到尾了从头再来；没选中时从画面上的位置往后找
   * （2026-11-19 评审：原来按画面时间找，跳过去时画面停在音符前 0.6 秒，下一次找到的还是它）。
   */
  function nextCheck() {
    if (!state.doc) return;
    const list = checkList();
    if (!list.length) { toast(state.t('toast.noCheck'), 'ok'); return; }
    const ids = [...state.selection];
    const from = ids.length ? Math.max(...ids) : view.firstVisible(state.timeTarget !== null ? state.timeTarget : state.time) - 1;
    focusNote(M.nextReview(list, from));
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
    const labels = shownLabels();
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
      if (one.filled) tags.push('<span class="tag auto">' + esc(state.t('source.auto')) + '</span>');
      else if (state.source === 'game' && doc.usedFile) tags.push('<span class="tag custom">' + esc(state.t('source.custom')) + '</span>');
      if (M.isChanged(one)) tags.push('<span class="tag changed">' + esc(state.t('source.changed')) + '</span>');
      html += '<dl class="kv">'
        + '<dt>' + esc(state.t('insp.time')) + '</dt><dd class="mono">' + esc(fmt(one.time)) + '</dd>'
        + '<dt>' + esc(state.t('insp.tile')) + '</dt><dd class="mono">#' + one.tile + (one.lastTile !== one.tile ? '–' + one.lastTile : '') + '</dd>'
        + '<dt>' + esc(state.t('insp.gap')) + '</dt><dd>' + esc(state.t('insp.gapValue', ms(prev, one), ms(one, next))) + '</dd>'
        + '<dt>' + esc(state.t('insp.kind')) + '</dt><dd>' + esc(state.t('kind.' + Math.min(4, one.lanes.length))) + '</dd>'
        + (tags.length ? '<dt>' + esc(state.t('insp.source')) + '</dt><dd>' + tags.join(' ') + '</dd>' : '')
        + '</dl>';
      if (one.auto) html += '<div class="note-callout warn">' + esc(state.t('insp.autoNote')) + '</div>'; // 还要检查时才提醒
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
    // 点的就是它现在的键（没有改动）：算看过了
    const pick = (note, lanes) => {
      const change = M.ops.set(state.doc, note, lanes);
      if (change && change.size === 0) confirmNotes([note]);
      else applyChange(change);
    };
    el.querySelectorAll('[data-lane]').forEach(b => b.addEventListener('click', () => {
      const note = notes[0], lane = Number(b.dataset.lane);
      if (note.lanes.length === 1) { pick(note, [lane]); return; }
      if (chordPick.includes(lane)) chordPick = chordPick.filter(x => x !== lane);
      else chordPick.push(lane);
      el.querySelectorAll('[data-lane]').forEach(x => {
        const l = Number(x.dataset.lane);
        x.classList.toggle('on', chordPick.includes(l));
        x.classList.toggle(note.lanes.length > 2 ? 'c3' : 'c2', chordPick.includes(l));
      });
      if (chordPick.length === note.lanes.length) pick(note, chordPick);
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
  // 轨道键优先于编辑器的快捷键（重复时打开关卡、录键时提示过）。游戏里的 Unity 键名（Keypad4、LeftShift……）按 e.code 认。
  // Shift、Ctrl 这类键当轨道键时，单独按下再松开才算：按着它点音符加选、按组合键都不改键
  let pendingModifier = null;
  window.addEventListener('mousedown', () => { pendingModifier = null; }, true);
  const inTextBox = e => !!(e.target && e.target.closest && e.target.closest('input, textarea, select'));
  const isSaveKey = e => (e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 's';

  /** 按下的是轨道键就改键（或记下按着的 Shift、Ctrl），返回 true；不是返回 false。 */
  function laneKeyDown(e) {
    const labels = laneLabels(), lane = M.laneOfKey(labels, e);
    if (lane < 0) return false;
    if (M.isModifier(labels[lane])) {
      if (!e.repeat) pendingModifier = { code: e.code, lane };
      return true;
    }
    if (e.ctrlKey || e.metaKey || e.altKey) return false; // 组合键：交给快捷键
    e.preventDefault();
    if (document.activeElement && document.activeElement.tagName === 'BUTTON') document.activeElement.blur(); // 空格当轨道键时别再「按」一次刚点过的按钮
    laneKeyPressed(lane);
    return true;
  }

  window.addEventListener('keydown', e => {
    if (pendingModifier && e.code !== pendingModifier.code) pendingModifier = null; // 按着的 Shift、Ctrl 是组合键的一部分
    if (inTextBox(e)) {
      // 输入框里按 Ctrl+S 也是保存，不弹出浏览器的另存网页；先让输入框交出焦点（文件名在 change 时才生效）
      if (isSaveKey(e)) { e.preventDefault(); if ($('modal').hidden) { e.target.blur(); saveToGame(); } }
      return;
    }
    if (!$('modal').hidden) { if (e.key === 'Escape') closeModal(false); else if (isSaveKey(e)) e.preventDefault(); return; }
    if (capturing !== null) { captureKey(e); return; }
    if (state.doc && laneKeyDown(e)) return;
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
    if (pendingModifier && e.code === pendingModifier.code) {
      const lane = pendingModifier.lane;
      pendingModifier = null;
      if (state.doc && !inTextBox(e) && $('modal').hidden && capturing === null) laneKeyPressed(lane);
      return;
    }
    if (e.key !== ' ' || inTextBox(e)) return;
    e.preventDefault();
    const wasDown = spaceDown; // 空格是轨道键时按下已经改了键，这里不再播放
    spaceDown = false;
    canvas.classList.remove('grab');
    if (wasDown && !spacePanned) togglePlay();
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
    renderPlayButton(true);
    state.lastTime = state.time;
  }
  /**
   * 播放按钮跟着播放器：播放中显示暂停。播放器自己停下（放到歌曲末尾）时由 frame 调用，换关卡时由 loadDoc 调用；
   * swap 为 true 时播一下换图标的动画（按按钮、歌曲放完）。
   */
  function renderPlayButton(swap) {
    state.playing = player.playing;
    $('playBtn').innerHTML = '<svg><use href="#i-' + (state.playing ? 'pause' : 'play') + '"/></svg>';
    if (swap) restart($('playBtn'), 'swap');
    $('playBtn').title = state.t(state.playing ? 'tr.pause' : 'tr.play');
  }
  $('playBtn').addEventListener('click', togglePlay);
  $('toStart').addEventListener('click', () => { if (state.doc) seek(state.loop.on && state.loop.a !== null ? state.loop.a : state.doc.notes[0].time - 1.2, true); });
  $('speed').addEventListener('change', e => { prefs.speed = Number(e.target.value); savePrefs(); player.setRate(prefs.speed, state.doc?.audio?.pitch ?? 1); });

  function setLoop(which) {
    if (!state.doc) return;
    state.loop[which] = state.time;
    if (state.loop.a !== null && state.loop.b !== null && state.loop.b < state.loop.a) [state.loop.a, state.loop.b] = [state.loop.b, state.loop.a];
    if (state.loop.a !== null && state.loop.b !== null) state.loop.on = loopLongEnough();
    player.cancelClicks();
    renderLoop();
  }
  /** 区间太短不开循环（暂停时按 [ 再按 ] 起点就等于终点：每帧都跳回起点，一直没有声音），提示一下。 */
  function loopLongEnough() {
    if (state.loop.b - state.loop.a >= window.EditorPlayer.LOOP_MIN) return true;
    toast(state.t('loop.tooShort'), 'warn');
    return false;
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
    else if (state.loop.a !== null && state.loop.b !== null) state.loop.on = loopLongEnough();
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
      const h = view.h, y = box.y1; // 画布高度用 resize 时量好的，不每帧重新量
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
      stepWaveform();
      if (player.playing) {
        const prev = state.lastTime;
        let t = player.time();
        if (state.loop.on) t = player.wrapLoop(t, state.loop.a, state.loop.b);
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
      if (state.playing !== player.playing) { // 播放器自己停了（放到歌曲末尾、换关卡）：停在它停的地方，按钮变回播放
        if (!player.playing) state.time = player.time();
        renderPlayButton(true);
      }
      renderClock();
    }
    view.draw(state);
    // 卡片藏起来时不画：画布大小是 0，轨道预览会每帧重新量尺寸
    if (prefs.track && state.doc && !$('trackCard').hidden) track.draw(state.time, state.t('view.track'), state.t('view.experimental'));
    drawMinimap();
    requestAnimationFrame(frame);
  }

  /** 播放条上的时间：文字变了才改（每帧都改会让页面每帧重新排版）。 */
  let clockText = '', endText = '';
  function renderClock() {
    const now = fmt(state.time), end = '/ ' + fmt(songEnd()).replace(/\.\d+$/, '');
    if (now !== clockText) $('timeNow').textContent = clockText = now;
    if (end !== endText) $('timeEnd').textContent = endText = end;
  }

  // ---------- 概览 ----------
  const mini = $('minimap'), mg = mini.getContext('2d');
  const miniSize = { w: 0, h: 0, dpr: 1 };
  /** 量概览画布、按像素比分配像素。只在尺寸、像素比变了时量：每帧量会在刚改完时间文字后强制页面重新排版。 */
  function sizeMinimap() {
    const r = mini.getBoundingClientRect(), dpr = Math.max(1, window.devicePixelRatio || 1);
    Object.assign(miniSize, { w: r.width, h: r.height, dpr });
    mini.width = Math.round(r.width * dpr);
    mini.height = Math.round(r.height * dpr);
  }
  if (window.ResizeObserver) new ResizeObserver(() => sizeMinimap()).observe(mini); // 概览刚显示出来、大小变了
  function drawMinimap() {
    const { w, h, dpr } = miniSize;
    mg.setTransform(dpr, 0, 0, dpr, 0, 0);
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
    metaDirty = metaEdits !== snap.metaEdits;
    snap.doc.savedBase = snap.base(name); // 游戏下次用存进去的这份导出时，草稿也认得
    allowDraft(); // 这一页刚存进游戏，就以这一页为准
    writeDraft(); // 草稿留着、记成已保存：从「历史」打开这一关时还是保存时的样子
    renderHeader();
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

  async function saveToGame() {
    const doc = state.doc;
    if (!doc) return;
    const text = M.toFileText(doc);
    if (!window.showDirectoryPicker) {
      // 下载不算保存（2026-11-19 评审：玩家没把文件放进去也显示已保存）：照旧提示放到哪里，没保存的标记留着
      download(text, doc.fileName);
      toast(state.t('save.downloaded', doc.fileName, saveFolderText()), 'warn', null, 12000);
      return;
    }
    // 开头记下存的是哪一关、哪一步、用什么名字：下面要等文件夹权限等，期间玩家可能又改了、改了文件名、在历史里换了关卡
    const snap = {
      doc, history: state.history, step: state.history.current, metaEdits, base: M.baseFor(doc),
      target: { fileName: doc.fileName, usedFile: doc.usedFile, meta: { charts: doc.meta.charts } },
    };
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
        if (renamed && doc.fileName === snap.target.fileName) { doc.fileName = name; renderFilePanel(); } // 存的途中玩家自己改了名字就留着他的
        saved(snap, name);
      }
      toast(state.t(renamed ? 'save.renamed' : 'save.done', name), 'ok', null, renamed ? 12000 : 6000);
    } catch (e) {
      if (e && e.name === 'AbortError') return; // 玩家在选文件夹时点了取消
      toast(state.t('save.failed', errorText(e)), 'error');
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
    if (!item) return;
    // 只认最后一次点击（2026-11-19 评审：快速连点两关时后点的先载完，最后显示的却是先点的那关）
    const seq = ++openSeq;
    if (state.doc && state.doc.sessionId === item.id) return;
    if (draftTimer) writeDraft(); // 这一关刚改的先存进草稿，换过去再换回来还在
    let s;
    try {
      s = await loadRecent(item);
    } catch (err) {
      if (seq === openSeq) toast(state.t('history.missing'), 'error', null, 8000);
      return;
    }
    if (seq !== openSeq) return;
    try {
      openDoc(() => M.fromSession(s), 'game');
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
  // 这一关在编辑器里现在的样子，随时存在这台电脑的浏览器里（按「历史」里的 Id；打开的是手序文件时按谱面签名加文件内容，见 loadDoc）。
  // 关掉网页、换到别的关卡都不丢，下次打开这一关自动接着改；保存到游戏后也留着（记成已保存）。
  // 草稿记着是在哪份手序上改的（M.draftMatches）：游戏换了一份手序、打开了别人分享的文件时不自动套，先问玩家（2026-11-19 第二轮评审：
  // 原来只按谱面认，打开朋友的手序显示的是自己的旧键位，文件名、作者也变回自己的）
  const DRAFT_PREFIX = 'rhythmhint.editor.draft.';
  const MAX_DRAFTS = 30; // 浏览器里最多留几关的草稿（2026-11-19 评审：原来从不删，越攒越多，写满了就存不进去）
  const draftKeyOf = doc => doc.draftKey || DRAFT_PREFIX + (doc.sessionId || doc.signature);
  let draftTimer = 0;
  let draftBlocked = false; // 另一个标签页改了这一关的草稿：这一页先不写，等玩家选用哪一边
  let otherTabToast = null, askToast = null, storageFullTold = false; // askToast：正在问要不要载入上次的进度（askDraft）

  /**
   * 把现在的样子写进草稿，写好了（或者不用记、删掉了）为 true。time：草稿里记的最后改动时间（默认现在）；
   * quiet：别的标签页删了这一关的草稿、这里重写时，写不下也不删别的关没保存的进度（两个标签页会轮流删对方的）。
   */
  function writeDraft(time, quiet) {
    clearTimeout(draftTimer);
    draftTimer = 0;
    const doc = state.doc;
    if (!doc) return false;
    if (draftBlocked) { warnOtherTab(); return false; }
    const key = draftKeyOf(doc);
    const draft = M.draftOf(doc, !(state.history.dirty || metaDirty), time);
    let ok = true;
    if (draft.saved && M.untouched(doc)) removeKey(key); // 和打开时一样、也没有要保存的：不用记
    // 存不进去（写满了）：腾了地方还不行就告诉玩家一次（原来空 catch 吞掉，界面还说已自动保留）
    else if (!(ok = storeDraft(key, JSON.stringify(draft), !quiet))) tellStorageFull();
    if (openPanel === 'history') renderHistory();
    return ok;
  }
  function scheduleDraft() {
    clearTimeout(draftTimer);
    draftTimer = 0;
    if (draftBlocked) { warnOtherTab(); return; }
    draftTimer = setTimeout(() => writeDraft(), 600); // 有意的延时：连续改动时少写几次
  }

  function tellStorageFull() {
    if (storageFullTold) return;
    storageFullTold = true;
    toast(state.t('draft.storageFull'), 'error', null, 15000);
  }

  /**
   * 存一份草稿；写满了先删别的关已保存的草稿（游戏里有文件），还不够、dropUnsaved 时再从最久没改的删别的关没保存的，
   * 每删一份试一次，删了几关告诉玩家（2026-11-19 评审：原来只删已保存的，别的关没保存的旧草稿一直占着，这一关存不进去）。
   * 正在编辑的这一关不删。腾完还是存不进去为 false。
   */
  function storeDraft(key, text, dropUnsaved) {
    if (writeText(key, text)) return true;
    if (pruneDrafts(true) && writeText(key, text)) return true;
    if (!dropUnsaved) return false;
    let dropped = 0;
    for (const old of M.draftsToDrop(listDrafts(), 0, key)) {
      removeKey(old);
      dropped++;
      if (writeText(key, text)) {
        toast(state.t('draft.dropped', dropped), 'warn', null, 15000);
        return true;
      }
    }
    return false;
  }

  /** 浏览器里各关的草稿 [{ key, saved, time }]（读不出来的按没保存、很久以前算）。 */
  function listDrafts() {
    const list = [];
    try {
      for (let i = 0; i < localStorage.length; i++) {
        const key = localStorage.key(i);
        if (!key || !key.startsWith(DRAFT_PREFIX)) continue;
        const d = readJson(key);
        list.push({ key, saved: !!(d && d.saved === true), time: d && typeof d.time === 'number' ? d.time : 0 });
      }
    } catch (e) {
      console.warn('RhythmHint: cannot list drafts', e); // localStorage 用不了：也就没有草稿可删
    }
    return list;
  }

  /** 草稿只留最近的 MAX_DRAFTS 关（先删已保存的）；onlySaved：存不进去时把已保存的都删掉。正在编辑的不删。返回删了没有。 */
  function pruneDrafts(onlySaved) {
    const list = listDrafts(), protect = state.doc ? draftKeyOf(state.doc) : '';
    const drop = onlySaved ? list.filter(d => d.saved && d.key !== protect).map(d => d.key) : M.draftsToDrop(list, MAX_DRAFTS, protect);
    drop.forEach(removeKey);
    return drop.length > 0;
  }

  // 多个标签页开着同一关时，各自拿着旧的样子写草稿会互相覆盖（2026-11-19 评审）。storage 事件只在别的标签页改了才来：
  // 来了就先不写，让玩家选载入那边的进度，还是以这一页为准（写一次，那边也会收到同样的提示）
  window.addEventListener('storage', e => {
    if (!e.key || !e.key.startsWith(DRAFT_PREFIX)) return;
    if (openPanel === 'history') renderHistory();
    if (!state.doc || e.key !== draftKeyOf(state.doc)) return;
    if (e.newValue === null) {
      // 别的标签页删了这一关的草稿（启动时清理旧草稿、在那边点了「放弃」）：不是改了进度，不用问，按本页现状重写一份
      // （2026-11-19 评审：原来照样提示「另一个标签页改了这一关」，点「载入另一页的进度」后本页的改动全丢）
      draftBlocked = false;
      if (otherTabToast) otherTabToast();
      writeDraft(undefined, true);
      return;
    }
    draftBlocked = true;
    clearTimeout(draftTimer);
    draftTimer = 0;
    warnOtherTab();
  });
  function warnOtherTab() {
    if (otherTabToast) return;
    otherTabToast = toast(state.t('draft.otherTab'), 'warn', [
      [state.t('draft.useOther'), reopenDoc, true],
      [state.t('draft.keepMine'), () => { allowDraft(); writeDraft(); }],
    ], 60000, () => { otherTabToast = null; });
  }
  /** 以这一页为准（换了一关、刚存进游戏、选了「以本页为准」）：不再等别的标签页，也不再问要不要载入上次的进度。 */
  function allowDraft() {
    draftBlocked = false;
    if (otherTabToast) otherTabToast();
    if (askToast) askToast();
  }
  /** 重新打开这一关：套上另一页存的草稿。 */
  function reopenDoc() {
    if (!state.make) return;
    try { openDoc(state.make, state.source); } catch (e) { toast(state.t('toast.openFailed', errorText(e)), 'error'); }
  }

  /**
   * 这一关的草稿；打开的是手序文件、新的名字下没有时，看看这一版以前的编辑器按谱面签名存的那份（legacyKey，两份文件共用过它）。
   * 游戏的关卡只按 Id 读：早期测试版按签名存过游戏关卡的草稿，没发布过，不再回头读（2026-11-19 评审：
   * 回头读会把打开文件时留下的草稿套到游戏的这一关上，「放弃」又只删 Id 那一份，下次打开再冒出来）。
   */
  function readDraft(doc) {
    const draft = readJson(draftKeyOf(doc));
    if (draft || doc.sessionId) return { draft, legacyKey: '' };
    const key = DRAFT_PREFIX + doc.signature, old = readJson(key);
    return old && !old.session ? { draft: old, legacyKey: key } : { draft: null, legacyKey: '' };
  }
  const metaDiffers = (doc, meta) => meta.author !== doc.opened.author || meta.comment !== doc.opened.comment || meta.fileName !== doc.opened.fileName;

  /** 打开一关时套上上次的进度（可撤销）：是在这份手序上改的直接套上，不是的先问；「放弃」回到游戏这次给的样子。 */
  function restoreDraft() {
    const doc = state.doc;
    const { draft, legacyKey } = readDraft(doc);
    const change = draft && M.draftChange(doc, draft);
    if (!change) return;
    if (M.draftMatches(doc, draft)) { applyDraft(doc, draft, change, legacyKey, !!legacyKey); return; }
    // 游戏换了一份手序、打开的是别人的文件，或者旧版的草稿认不出来：和这次打开的一样就不用问
    if (!change.size && !metaDiffers(doc, M.draftMeta(doc, draft))) return;
    askDraft(doc, draft, legacyKey);
  }

  /**
   * 问玩家要不要载入不是在这份手序上改的进度。提示条开着时都能载入（载入的是打开时读到的那份）；没回答就接着改的照常写草稿，
   * 不为了留着那份进度而不存新改的（换关卡、关网页时新改的会丢）。到时间收起来、也没接着改：那份进度留着，下次打开再问。
   */
  function askDraft(doc, draft, legacyKey) {
    askToast = toast(state.t('draft.found'), 'warn', [
      [state.t('draft.load'), () => {
        if (state.doc !== doc) return;
        const change = M.draftChange(doc, draft);
        if (change) applyDraft(doc, draft, change, legacyKey, true);
      }, true],
      [state.t('draft.skip'), () => { if (state.doc === doc) writeDraft(); }], // 以这次打开的为准：没改过就删掉那份进度，改过了就换成这一页的
    ], 60000, () => { askToast = null; });
  }

  /**
   * 套上草稿（可撤销）。rebase：玩家选了载入、或接上的是旧版编辑器的草稿：马上按这次打开的重写一份（时间照旧），
   * 下次打开同样的这一份直接接上；旧版按谱面签名存的那份删掉。
   */
  function applyDraft(doc, draft, change, legacyKey, rebase) {
    const opened = Object.assign({}, doc.opened), meta = M.draftMeta(doc, draft);
    const metaChanged = metaDiffers(doc, meta);
    const confirmed = M.draftConfirmed(doc, draft);
    confirmed.forEach(id => { doc.notes[id].confirmed = true; }); // 看过的待检查照样算看过
    doc.savedBase = typeof draft.savedBase === 'string' ? draft.savedBase : ''; // 上次存进游戏的那份：游戏下次用它导出时也认得
    if (change.missed) toast(state.t('draft.missed', change.missed), 'error', null, 15000);
    // 旧版的那份在新的名字下写好了才删（存储写满时留着）
    const finish = () => { if (rebase && writeDraft(draft.time) && legacyKey) removeKey(legacyKey); };
    if (!change.size && !metaChanged) { if (confirmed.length) edited(true); finish(); return; }
    const before = new Map([...change.keys()].map(id => [id, doc.notes[id].lanes]));
    if (change.size) state.history.apply(change);
    setMeta(doc, meta); // 文件名也沿用（2026-11-19 评审：从历史接着改时存成默认名，同一关两份文件，游戏认不出用哪份）
    const saved = draft.saved && !change.missed; // 有没接上的改动时，和保存过的文件已经不一样
    if (saved) state.history.markSaved(); // 和保存到游戏的文件一样：不算没保存
    metaDirty = !saved && metaChanged;
    edited(true); // 不重写草稿：草稿里记的是上次改动的时间
    renderFilePanel();
    finish();
    toast(state.t(saved ? 'draft.restoredSaved' : 'draft.restored', change.size), 'ok', [
      [state.t('draft.discard'), () => discardDraft(doc, change, before, opened, confirmed)],
    ], 12000);
  }

  function setMeta(doc, meta) {
    doc.meta.author = meta.author;
    doc.meta.comment = meta.comment;
    doc.fileName = meta.fileName;
  }

  /**
   * 「放弃」恢复的进度：恢复时改过、之后没再动过的音符改回去，作者、说明、文件名回到打开时的，看过的待检查不再算看过。
   * 和打开时一样了就算已保存、删掉草稿；恢复后又改了别处的，那些改动留着、照旧没保存，草稿照现状写
   * （2026-11-19 评审：原来无条件记成已保存、删掉草稿，那处改动关网页时不提醒、丢了）。
   */
  function discardDraft(doc, restored, before, opened, confirmed) {
    if (state.doc !== doc) return;
    const back = M.undoRestore(doc, restored, before);
    if (back.size) state.history.apply(back);
    setMeta(doc, opened);
    confirmed.forEach(id => { doc.notes[id].confirmed = false; });
    doc.savedBase = '';
    const clean = M.untouched(doc);
    if (clean) state.history.markSaved();
    metaDirty = false;
    edited(true);
    if (clean) removeKey(draftKeyOf(doc));
    else writeDraft();
    renderFilePanel();
    if (openPanel === 'history') renderHistory();
  }

  // ---------- 尺寸 ----------
  // 左侧面板开关时谱面让出的宽度慢慢变过去（2026-11-18 用户：原来一下子跳过去很生硬），和面板滑进来的时间差不多
  let insetTween = null;
  const INSET_MS = 260;
  function resize() {
    view.resize();
    if (prefs.track) track.resize();
    sizeMinimap();
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
  // 拖到缩放比例不同的显示器上：像素比变了不一定有 resize 事件，按新的像素比重新分配画布像素（不然会糊）。
  // 每次只盯「现在的像素比」，变了就重新量、换成盯新的
  (function watchPixelRatio() {
    if (!window.matchMedia) return;
    const query = window.matchMedia('(resolution: ' + (window.devicePixelRatio || 1) + 'dppx)');
    if (query.addEventListener) query.addEventListener('change', () => { resize(); watchPixelRatio(); }, { once: true });
  })();

  // ---------- 开始 ----------
  langSelect.value = I18N.LANGUAGES.some(([c]) => c === pickLanguage()) ? pickLanguage() : 'en';
  applyLanguage(langSelect.value);
  $('zoomLabel').textContent = Math.round(state.pxPerSec / 240 * 100) + '%';
  zoomSlider.value = String(sliderOf(state.pxPerSec));
  document.querySelectorAll('[data-panel-body]').forEach(el => { el.hidden = true; });
  if (session) {
    try { openDoc(() => M.fromSession(session), 'game'); } catch (e) { toast(state.t('toast.openFailed', errorText(e)), 'error'); $('empty').hidden = false; }
  } else {
    $('empty').hidden = false;
    renderHeader();
    if (recent.length) togglePanel('history');
  }
  pruneDrafts(false); // 只留最近一批草稿（正在编辑的这一关不删）
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
