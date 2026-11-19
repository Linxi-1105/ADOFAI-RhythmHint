/* 播放和画面：播放暂停、循环、每一帧（播放位置、平滑滚动和缩放、命中闪光、打拍声）、下面的全曲概览、轨道预览的缩放和放大、画布尺寸。 */
(function (App) {
  'use strict';

  const { reduceMotion, $, prefs, savePrefs, state, view, track, player, toast, restart, fmt } = App;

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
  $('toStart').addEventListener('click', () => { if (state.doc) App.seek(state.loop.on && state.loop.a !== null ? state.loop.a : state.doc.notes[0].time - 1.2, true); });
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
      App.applyZoom(Math.abs(state.zoomTarget - px) < 0.5 ? state.zoomTarget : px);
      if (state.pxPerSec === state.zoomTarget) { state.zoomTarget = null; state.zoomAnchor = null; }
    }
    if (state.timeTarget !== null && !player.playing) {
      const d = state.timeTarget - state.time;
      if (Math.abs(d) < 0.002) { state.time = state.timeTarget; state.timeTarget = null; player.seek(state.time); }
      else state.time += d * 0.22;
    }
    if (state.doc) {
      App.stepWaveform();
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
    App.seek(Math.max(0, Math.min(1, (e.clientX - r.left) / r.width)) * songEnd());
  };
  mini.addEventListener('mousedown', e => { miniDrag = true; miniSeek(e); });
  window.addEventListener('mousemove', e => { if (miniDrag) miniSeek(e); });
  window.addEventListener('mouseup', () => { miniDrag = false; });

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

  // ---------- 尺寸 ----------
  // 左侧面板开关时谱面让出的宽度慢慢变过去（2026-11-18 用户：原来一下子跳过去很生硬），和面板滑进来的时间差不多
  let insetTween = null;
  const INSET_MS = 260;
  function resize() {
    view.resize();
    if (prefs.track) track.resize();
    sizeMinimap();
    const left = App.openPanel ? 354 : 0;
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

  Object.assign(App, { togglePlay, renderPlayButton, setLoop, renderLoop, songEnd, frame, trackSizeTitle, resize });
})(window.EditorApp);
