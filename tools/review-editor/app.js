/* Local four-key studio. Editing changes fingering, while source timing remains intact. */
(function () {
  'use strict';
  const C = window.ReviewCore, M = window.StudioModel, A = window.StudioAudio;
  const $ = id => document.getElementById(id);
  const canvas = $('chart'), ctx = canvas.getContext('2d'), mini = $('overview'), mc = mini.getContext('2d');
  const miniCache = document.createElement('canvas');
  let chart = null, edits = new Map(), byId = new Map(), selected = new Set(), active = null;
  let view = 0, pps = 220, minTime = 0, endTime = 1, maxHold = 0, geometry, width = 900, height = 650;
  let undo = [], redo = [], savedDraft = null, saveTimer = null, fieldIds = [], miniDirty = true;
  let tool = 'select', gesture = null, frame = null, lastPlayed = 0, sound = null, audioGeneration = 0;
  let lastWaveform = null, waveformGain = 1, importGeneration = 0;
  let transport = makeTransport();
  const sources = { game: '游戏内导出', demo: '操作演示', 'offline-approximation': '离线近似' };
  const current = note => edits.get(note.id) || { noteId: note.id, lanes: note.lanes, reason: '', reviewed: false };
  const changed = note => !C.sameLanes(note.lanes, current(note).lanes);
  const laneText = items => C.ordered(items).map(l => chart.keyLabels[l.lane]).join(' + ');
  const storageKey = () => 'rhythmhint-review:v1:' + chart.chartId;
  function message(text, error = false) { $('message').textContent = text; $('message').classList.toggle('error', error); }
  function makeTransport() { return A.createTransport({ onError: e => message(String(e.message || e), true), onChange: () => requestFrame() }); }
  function timeText(time) {
    const ms = Math.round(Math.abs(time) * 1000);
    return (time < 0 ? '−' : '') + String(Math.floor(ms / 60000)).padStart(2, '0') + ':' + String(Math.floor(ms / 1000) % 60).padStart(2, '0') + '.' + String(ms % 1000).padStart(3, '0');
  }

  function persist() {
    clearTimeout(saveTimer); saveTimer = null;
    if (!chart) return;
    try { localStorage.setItem(storageKey(), JSON.stringify({ baseline: C.baseline(chart), review: chart.review })); $('save-state').textContent = '草稿已保存'; }
    catch (_) { $('save-state').textContent = '未能保存'; message('浏览器无法保存草稿，请用「导出审核」保存文件。', true); }
  }
  function queueSave() { clearTimeout(saveTimer); $('save-state').textContent = '保存中…'; saveTimer = setTimeout(persist, 300); }
  function commit(next, textOnly = false) {
    if (JSON.stringify(next.review) === JSON.stringify(chart.review)) return;
    undo.push(C.clone(chart.review)); if (undo.length > 100) undo.shift(); redo = [];
    chart = next; edits = C.editMap(chart); savedDraft = null; $('draft-banner').hidden = true; miniDirty = true;
    queueSave(); progress(); if (!textOnly) inspector(); draw();
  }
  function flushFields() {
    if (!chart) return;
    let next = chart;
    if ($('reason').dataset.dirty === 'true' && fieldIds.length) next = M.applyBatch(next, fieldIds, { type: 'reason', reason: $('reason').value });
    if ($('comment').value !== next.review.comment) next = C.validate({ ...next, review: { ...next.review, comment: $('comment').value } });
    $('reason').dataset.dirty = 'false';
    if (next !== chart) commit(next, true); // Keep the impending blur/click target in the DOM.
  }
  function batch(action) {
    if (!chart || !selected.size) return;
    flushFields();
    try { commit(M.applyBatch(chart, [...selected], action)); } catch (e) { message(e.message, true); }
  }
  function history(which) {
    flushFields(); const from = which === 'undo' ? undo : redo, to = which === 'undo' ? redo : undo;
    if (!chart || !from.length) return;
    to.push(C.clone(chart.review)); chart = C.validate({ ...chart, review: from.pop() }); edits = C.editMap(chart);
    miniDirty = true; queueSave(); progress(); inspector(); draw();
  }
  function progress() {
    $('undo').disabled = !undo.length; $('redo').disabled = !redo.length;
    for (const id of ['download', 'download-summary', 'play', 'stop', 'next-unreviewed', 'comment']) $(id).disabled = !chart;
    if (!chart) return;
    const s = C.stats(chart);
    $('count-total').textContent = s.total; $('count-reviewed').textContent = s.reviewed; $('count-changed').textContent = s.changed;
    $('complete').disabled = s.reviewed !== s.total; $('complete').checked = chart.review.status === 'complete';
    $('completion-hint').textContent = s.reviewed + ' / ' + s.total + ' 已审核' + (s.missingReasons ? ' · ' + s.missingReasons + ' 处修改未写理由' : '');
  }
  function inspector() {
    const notes = [...selected].map(id => byId.get(id)).filter(Boolean), first = (selected.has(active) && byId.get(active)) || notes[0];
    fieldIds = notes.map(n => n.id);
    for (const id of ['mirror', 'shift-left', 'shift-right', 'review-selection', 'reset-note', 'reviewed', 'reason']) $(id).disabled = !notes.length;
    $('selection-label').textContent = notes.length > 1 ? '已选择 ' + notes.length + ' 个音符' : first ? '砖块 ' + first.tileIndex + (first.lastTileIndex !== first.tileIndex ? '–' + first.lastTileIndex : '') : '未选择音符';
    $('selection-detail').textContent = first ? timeText(first.time) + (notes.length > 1 ? ' · 可整组移轨 / 镜像' : ' · ' + first.lanes.length + ' 键') : '点击选择 · 拖动换轨 · 空白处框选';
    $('original-lanes').textContent = notes.length === 1 ? laneText(first.lanes) : '—'; $('revised-lanes').textContent = notes.length === 1 ? laneText(current(first).lanes) : '—';
    const reasons = [...new Set(notes.map(n => current(n).reason))];
    $('reason').value = reasons.length === 1 ? reasons[0] : ''; $('reason').dataset.dirty = 'false';
    $('reason').placeholder = reasons.length > 1 ? '理由不同；输入可统一修改' : '可选：这段为什么这样排';
    $('reviewed').checked = !!notes.length && notes.every(n => current(n).reviewed);
    $('reviewed').indeterminate = notes.some(n => current(n).reviewed) && !$('reviewed').checked;
    $('comment').value = chart?.review.comment || '';
    const container = $('lane-buttons'); container.replaceChildren();
    if (notes.length === 1) for (const item of C.ordered(current(first).lanes)) {
      const row = document.createElement('div'); row.className = 'finger-row';
      const label = document.createElement('span'); label.textContent = chart.keyLabels[item.lane] + (item.holdSeconds > 0 ? ' · ' + item.holdSeconds.toFixed(2) + 's' : ''); row.append(label);
      chart.keyLabels.forEach((name, lane) => {
        const button = document.createElement('button'); button.textContent = name; button.type = 'button'; button.title = '将 ' + chart.keyLabels[item.lane] + ' 移到 ' + name;
        button.classList.toggle('active', lane === item.lane); button.disabled = lane !== item.lane && current(first).lanes.some(l => l.lane === lane);
        button.addEventListener('click', () => { flushFields(); try { commit(M.moveLane(chart, first.id, item.lane, lane)); } catch (e) { message(e.message, true); } }); row.append(button);
      }); container.append(row);
    }
  }
  function setSelection(ids, focus = null) { flushFields(); selected = new Set(ids); active = focus || [...selected][0] || null; inspector(); draw(); }
  function load(value, label) {
    const next = C.validate(value); flushFields(); if (saveTimer) persist();
    importGeneration++;
    transport.dispose(); transport = makeTransport(); audioGeneration++;
    $('audio-file').value = ''; $('audio-name').textContent = '未载入音乐 · 可静音播放'; $('audio-offset').value = 0;
    $('grid-bpm').value = 120; $('grid-origin').value = 0;
    chart = next; edits = C.editMap(chart); byId = new Map(chart.notes.map(n => [n.id, n]));
    const sourceFile = chart.sourceFile;
    if (sourceFile) {
      if (Number.isFinite(sourceFile.offset)) { $('audio-offset').value = sourceFile.offset; transport.setOffset(sourceFile.offset); }
      if (Number.isFinite(sourceFile.bpm) && sourceFile.bpm > 0) $('grid-bpm').value = sourceFile.bpm;
      if (typeof sourceFile.songFilename === 'string' && sourceFile.songFilename) $('audio-name').textContent = '请载入同目录音乐：' + sourceFile.songFilename;
    }
    selected = new Set(); active = null; undo = []; redo = []; savedDraft = null; gesture = null;
    minTime = Math.min(0, chart.notes[0].time); endTime = 0; maxHold = 0;
    for (const note of chart.notes) for (const item of note.lanes) { maxHold = Math.max(maxHold, item.holdSeconds); endTime = Math.max(endTime, note.time + item.holdSeconds); }
    endTime = Math.max(minTime + 1, endTime); view = minTime; transport.seek(view); transport.setRate(Number($('speed').value));
    $('loop-enabled').checked = false; $('loop-a').value = minTime.toFixed(3); $('loop-b').value = Math.min(endTime, minTime + 4).toFixed(3);
    $('chart-name').textContent = chart.name; $('source-label').textContent = sources[chart.source] + (sourceFile ? ' · ' + sourceFile.name : '') + ' · ' + chart.algorithm;
    $('seek').min = minTime; $('seek').max = endTime + .5; $('draft-banner').hidden = true; $('save-state').textContent = '已载入';
    let issue = '';
    try { const saved = JSON.parse(localStorage.getItem(storageKey()) || 'null'); if (saved) { C.restore(chart, saved); if (JSON.stringify(saved.review) !== JSON.stringify(chart.review)) { savedDraft = saved; $('draft-banner').hidden = false; } } }
    catch (e) { issue = '；草稿未恢复：' + e.message; }
    miniDirty = true; progress(); inspector(); syncTransport(); draw(); message(label + '。滚轮浏览，拖音符换轨，空格播放。' + issue, !!issue);
  }

  function seek(time, pause = true) {
    if (!chart || !Number.isFinite(time)) return;
    if (pause) transport.pause(); const t = Math.max(minTime, Math.min(endTime + .5, time)); transport.seek(t); view = t; lastPlayed = t; syncTransport(); draw();
  }
  function syncTransport() {
    const t = transport.getTime(); $('time-display').textContent = timeText(t); $('seek').value = t;
    $('play').textContent = transport.isPlaying() ? '❚❚ 暂停' : '▶ 播放'; $('play').setAttribute('aria-label', transport.isPlaying() ? '暂停播放' : '开始播放'); $('zoom-label').textContent = Math.round(pps) + ' px/s';
  }
  async function togglePlayback() {
    if (!chart) return; flushFields();
    if (transport.isPlaying()) { transport.pause(); if ($('follow').checked) view = transport.getTime(); syncTransport(); draw(); return; }
    const playbackEnd = Math.min(endTime + .49, transport.getDuration());
    if (transport.getTime() >= playbackEnd) seek(minTime);
    // Do not leave the transport looking paused while an auxiliary sound context
    // resumes: a second click must pause this request, not launch another play.
    if ($('metronome').checked) try { sound ||= new (window.AudioContext || window.webkitAudioContext)(); sound.resume().catch(() => {}); } catch (_) { /* Visual playback remains available. */ }
    lastPlayed = transport.getTime();
    try { await transport.play(); syncTransport(); requestFrame(); } catch (e) { message('播放失败：' + e.message, true); }
  }
  function clickSound(isChord) {
    if (!sound || sound.state !== 'running') return;
    const osc = sound.createOscillator(), gain = sound.createGain(), t = sound.currentTime;
    osc.frequency.value = isChord ? 1100 : 1500; gain.gain.setValueAtTime(.045, t); gain.gain.exponentialRampToValueAtTime(.001, t + .025);
    osc.connect(gain); gain.connect(sound.destination); osc.start(t); osc.stop(t + .03); osc.onended = () => { osc.disconnect(); gain.disconnect(); };
  }
  function requestFrame() { if (!frame) frame = requestAnimationFrame(tick); }
  function tick() {
    frame = null; const now = transport.getTime();
    if (chart && transport.isPlaying()) {
      if ($('follow').checked) view = now;
      if ($('metronome').checked && now >= lastPlayed && now - lastPlayed < .25) {
        for (let i = M.lowerBound(chart.notes, lastPlayed + 1e-7), n = 0; i < chart.notes.length && chart.notes[i].time <= now && n < 8; i++, n++) clickSound(chart.notes[i].lanes.length > 1);
      }
      if (now > endTime + .5 && !$('loop-enabled').checked) transport.pause(); lastPlayed = now;
    }
    syncTransport(); draw(); if (transport.isPlaying()) requestFrame();
  }
  function prepare(target, context) {
    const r = target.getBoundingClientRect(), w = Math.max(1, r.width), h = Math.max(1, r.height), dpr = Math.min(2, window.devicePixelRatio || 1);
    if (target.width !== Math.round(w * dpr) || target.height !== Math.round(h * dpr)) { target.width = Math.round(w * dpr); target.height = Math.round(h * dpr); }
    context.setTransform(dpr, 0, 0, dpr, 0, 0); context.clearRect(0, 0, w, h); return [w, h];
  }
  function draw() {
    [width, height] = prepare(canvas, ctx); geometry = M.layout(width, height);
    const g = geometry, right = g.x + g.width, y = t => M.timeToY(t, view, pps, g.hitY);
    ctx.fillStyle = '#080c11'; ctx.fillRect(0, 0, width, height);
    for (let lane = 0; lane < 4; lane++) { ctx.fillStyle = lane % 2 ? '#111820' : '#0d131a'; ctx.fillRect(g.x + lane * g.laneWidth, g.top, g.laneWidth, g.bottom - g.top); }
    ctx.save(); ctx.beginPath(); ctx.rect(0, g.top, width, g.bottom - g.top); ctx.clip();
    const from = M.yToTime(g.bottom, view, pps, g.hitY), to = M.yToTime(g.top, view, pps, g.hitY);
    const wave = transport.getWaveform();
    if (wave?.peaks?.length) {
      if (wave !== lastWaveform) { lastWaveform = wave; waveformGain = 1 / Math.max(.0001, wave.peaks.reduce((peak, value) => Math.max(peak, value), 0)); }
      ctx.fillStyle = '#365b6a'; const offset = Number($('audio-offset').value) / 1000, x = Math.max(15, g.x - 70);
      for (let py = g.top; py < g.bottom; py += 2) { const audioTime = M.yToTime(py, view, pps, g.hitY) + offset, peak = (wave.peaks[Math.floor(audioTime / wave.duration * wave.peaks.length)] || 0) * waveformGain; ctx.fillRect(x - peak * 20, py, peak * 40, 1.5); }
    }
    const beats = $('grid-mode').value === 'beats', division = Number($('grid-division').value);
    const step = beats ? 60 / Math.max(1, Number($('grid-bpm').value) || 120) / division : pps > 450 ? .1 : pps > 180 ? .25 : .5;
    const origin = beats ? Number($('grid-origin').value) || 0 : 0, density = Math.max(1, Math.ceil(7 / (step * pps)));
    for (let k = Math.ceil((from - origin) / step / density) * density, count = 0; origin + k * step <= to && count < 1500; k += density, count++) {
      const t = origin + k * step, py = y(t), major = beats ? k % (division * 4) === 0 : Math.abs(t - Math.round(t)) < 1e-6, beat = beats && k % division === 0;
      ctx.strokeStyle = major ? '#405361' : beat ? '#2d4a55' : '#202b38'; ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(g.x, py); ctx.lineTo(right, py); ctx.stroke();
      if (major || beat) { ctx.fillStyle = major ? '#acbfcb' : '#718493'; ctx.font = (major ? '12' : '10') + 'px Consolas,monospace'; ctx.textAlign = 'right'; ctx.fillText(beats ? (k / division / 4 + 1).toFixed(major ? 0 : 2) : t.toFixed(0) + 's', g.x - 12, py + 4); }
    }
    for (let lane = 0; lane <= 4; lane++) { ctx.strokeStyle = lane === 2 ? '#405360' : '#283440'; ctx.beginPath(); ctx.moveTo(g.x + lane * g.laneWidth, g.top); ctx.lineTo(g.x + lane * g.laneWidth, g.bottom); ctx.stroke(); }
    if ($('loop-enabled').checked) for (const [id, label] of [['loop-a','A'],['loop-b','B']]) { const py = y(Number($(id).value)); ctx.strokeStyle = '#e7b65f'; ctx.setLineDash([5,4]); ctx.beginPath(); ctx.moveTo(g.x, py); ctx.lineTo(right, py); ctx.stroke(); ctx.setLineDash([]); ctx.fillStyle = '#e7b65f'; ctx.textAlign = 'left'; ctx.fillText(label,right+9,py+4); }
    if (chart) {
      const range = M.visibleRange(chart.notes, view, pps, g, maxHold);
      for (let i = range.start; i < range.end; i++) { const note = chart.notes[i]; if ($('show-original').checked && changed(note)) paint(note, note.lanes, true); paint(note, current(note).lanes, false); }
    }
    function paint(note, items, ghost) {
      const py = y(note.time), picked = selected.has(note.id), edited = changed(note);
      if (py < g.top - 10 || items.every(l => y(note.time + l.holdSeconds) > g.bottom + 10)) return;
      for (const item of items) {
        const x = g.x + item.lane * g.laneWidth + 7, w = g.laneWidth - 14, endY = y(note.time + item.holdSeconds), color = ghost ? '#718698' : picked ? '#e1f8ff' : edited ? '#ffb56a' : '#75d8f1';
        if (item.holdSeconds > 0) { ctx.fillStyle = ghost ? '#8293a115' : '#338dad50'; ctx.fillRect(x + w*.24, endY, w*.52, py-endY); ctx.strokeStyle = color; ctx.globalAlpha = ghost ? .4 : .8; ctx.strokeRect(x+w*.24,endY,w*.52,py-endY); ctx.globalAlpha=1; }
        if (ghost) { ctx.strokeStyle=color;ctx.setLineDash([3,3]);ctx.strokeRect(x,py-6,w,12);ctx.setLineDash([]);continue; }
        ctx.fillStyle='#143c50';ctx.fillRect(x,py-7,w,14);ctx.strokeStyle=color;ctx.lineWidth=picked?2:1.5;ctx.strokeRect(x,py-7,w,14);
        ctx.fillStyle=color;ctx.fillRect(x+4,py-3,Math.max(1,w-8),6);if(picked){ctx.fillStyle='#a8e8ff30';ctx.fillRect(x-3,py-10,w+6,20);}
      }
      ctx.lineWidth=1;if(!ghost&&picked){ctx.fillStyle='#d4e7f1';ctx.font='11px Consolas,monospace';ctx.textAlign='left';ctx.fillText('#'+note.tileIndex,right+14,py+4);}
    }
    if (gesture?.type==='box') { const r=rect(gesture.start,gesture.point);ctx.fillStyle='#70d6ff20';ctx.strokeStyle='#80dfff';ctx.fillRect(r.x,r.y,r.width,r.height);ctx.strokeRect(r.x,r.y,r.width,r.height); }
    if (gesture?.type==='note'&&gesture.moved) { const lane=laneAt(gesture.point.x),note=byId.get(gesture.hit.noteId);if(lane>=0){ctx.strokeStyle='#fff';ctx.setLineDash([4,3]);ctx.strokeRect(g.x+lane*g.laneWidth+4,y(note.time)-11,g.laneWidth-8,22);ctx.setLineDash([]);} }
    const cursor=y(transport.getTime());ctx.strokeStyle='#f1d798';ctx.lineWidth=1;ctx.beginPath();ctx.moveTo(g.x-5,cursor);ctx.lineTo(right+5,cursor);ctx.stroke();ctx.restore();
    ctx.fillStyle='#070b10';ctx.fillRect(g.x,g.hitY+16,g.width,Math.max(0,height-g.hitY-16));
    for(let lane=0;lane<4;lane++){const x=g.x+lane*g.laneWidth;ctx.fillStyle='#173a49';ctx.fillRect(x+1,g.hitY+16,g.laneWidth-2,40);ctx.fillStyle='#72d6e9';ctx.fillRect(x+1,g.hitY+16,g.laneWidth-2,3);ctx.font='600 19px Consolas,monospace';ctx.textAlign='center';ctx.fillStyle='#c5e9f3';ctx.fillText(chart?.keyLabels[lane]||['D','F','J','K'][lane],x+g.laneWidth/2,g.hitY+43);}
    ctx.font='11px Segoe UI,sans-serif';ctx.fillStyle='#7c92a3';ctx.textAlign='left';ctx.fillText(beats?'辅助拍线 · '+$('grid-bpm').value+' BPM · 1/'+division:'时间网格 · 秒',g.x,17);
    if(!chart){ctx.fillStyle='#a0b6c5';ctx.textAlign='center';ctx.font='16px Segoe UI,sans-serif';ctx.fillText('导入排键 JSON 或打开演示',width/2,height/2);}
    drawMini();
  }
  function drawMini() {
    const [w,h]=prepare(mini,mc); if(!chart)return;
    const y=t=>h-10-(t-minTime)/(endTime-minTime)*(h-20);
    if(miniDirty||miniCache.width!==Math.round(w)||miniCache.height!==Math.round(h)){
      miniCache.width=Math.round(w);miniCache.height=Math.round(h);const c=miniCache.getContext('2d'),lw=(w-14)/4;c.fillStyle='#0b1118';c.fillRect(0,0,w,h);
      for(const n of chart.notes){c.fillStyle=changed(n)?'#f5a566':current(n).reviewed?'#7ab7a7':'#426a80';for(const l of current(n).lanes)c.fillRect(7+l.lane*lw,y(n.time)-1,lw-2,2);} miniDirty=false;
    }
    mc.drawImage(miniCache,0,0,w,h);
    const top=Math.max(0,y(M.yToTime(geometry.top,view,pps,geometry.hitY))),bottom=Math.min(h,y(M.yToTime(geometry.bottom,view,pps,geometry.hitY)));
    mc.fillStyle='#67bfe326';mc.fillRect(1,top,w-2,bottom-top);mc.strokeStyle='#8ad9f4';mc.strokeRect(1,top,w-2,bottom-top);mc.strokeStyle='#ffe4a0';mc.beginPath();mc.moveTo(0,y(transport.getTime()));mc.lineTo(w,y(transport.getTime()));mc.stroke();
  }

  const point=e=>{const r=canvas.getBoundingClientRect();return{x:e.clientX-r.left,y:e.clientY-r.top};};
  const rect=(a,b)=>({x:Math.min(a.x,b.x),y:Math.min(a.y,b.y),width:Math.abs(a.x-b.x),height:Math.abs(a.y-b.y)});
  function laneAt(x){const lane=Math.floor((x-geometry.x)/geometry.laneWidth);return lane>=0&&lane<4?lane:-1;}
  const interactionGeometry=()=>({...geometry,bottom:Math.min(geometry.bottom,geometry.hitY+12)});
  canvas.addEventListener('pointerdown',e=>{
    if(!chart||(e.button!==0&&e.button!==1))return;const p=point(e);if(p.y>geometry.hitY+12)return;e.preventDefault();flushFields();transport.pause();syncTransport();canvas.focus();canvas.setPointerCapture(e.pointerId);
    if(tool==='pan'||e.button===1||e.altKey)gesture={type:'pan',start:p,point:p,time:view,moved:false};
    else {const hit=M.hitTest(chart.notes,edits,p.x,p.y,view,pps,interactionGeometry(),10);
      if(hit){if(e.shiftKey){const ids=new Set(selected);if(ids.has(hit.noteId))ids.delete(hit.noteId);else ids.add(hit.noteId);setSelection(ids,hit.noteId);}else if(!selected.has(hit.noteId))setSelection([hit.noteId],hit.noteId);else{active=hit.noteId;inspector();}gesture={type:'note',start:p,point:p,hit,moved:false,group:selected.size>1};}
      else gesture={type:'box',start:p,point:p,moved:false,additive:e.shiftKey,previous:[...selected]};
    }draw();
  });
  canvas.addEventListener('pointermove',e=>{
    const p=point(e);if(!gesture){const hit=chart?M.hitTest(chart.notes,edits,p.x,p.y,view,pps,interactionGeometry(),8):null;canvas.style.cursor=tool==='pan'?'grab':hit?'ew-resize':'crosshair';return;}
    gesture.point=p;if(Math.hypot(p.x-gesture.start.x,p.y-gesture.start.y)>4)gesture.moved=true;if(gesture.type==='pan')seek(gesture.time+(p.y-gesture.start.y)/pps);draw();
  });
  canvas.addEventListener('pointerup',e=>{
    if(!gesture)return;const done=gesture;gesture=null;if(canvas.hasPointerCapture(e.pointerId))canvas.releasePointerCapture(e.pointerId);
    try{if(done.type==='note'&&done.moved&&selected.has(done.hit.noteId)){const lane=laneAt(done.point.x);if(lane<0)throw new Error('请将音符放在四条轨道内。');if(done.group)commit(M.applyBatch(chart,[...selected],{type:'shift',delta:lane-done.hit.lane}));else commit(M.moveLane(chart,done.hit.noteId,done.hit.lane,lane));}
      else if(done.type==='box'){if(done.moved)setSelection([...(done.additive?done.previous:[]),...M.boxSelect(chart.notes,edits,rect(done.start,done.point),view,pps,interactionGeometry())]);else{if(!done.additive)setSelection([]);seek(M.yToTime(done.point.y,view,pps,geometry.hitY));}}}
    catch(error){message(error.message,true);}draw();
  });
  canvas.addEventListener('pointercancel',()=>{gesture=null;draw();});canvas.addEventListener('contextmenu',e=>e.preventDefault());
  canvas.addEventListener('wheel',e=>{e.preventDefault();if(!chart)return;if(e.ctrlKey||e.metaKey){const p=point(e),anchor=M.yToTime(p.y,view,pps,geometry.hitY);pps=Math.max(80,Math.min(650,pps*Math.exp(-e.deltaY*.002)));$('zoom').value=pps;seek(anchor-(geometry.hitY-p.y)/pps);}else seek(view+e.deltaY/pps*(e.shiftKey?.15:.7));syncTransport();draw();},{passive:false});
  let miniDragging=false;
  function miniSeek(e){const r=mini.getBoundingClientRect();seek(minTime+(1-(e.clientY-r.top-10)/(r.height-20))*(endTime-minTime));}
  mini.addEventListener('pointerdown',e=>{if(!chart)return;e.preventDefault();miniDragging=true;mini.setPointerCapture(e.pointerId);miniSeek(e);});mini.addEventListener('pointermove',e=>{if(miniDragging)miniSeek(e);});mini.addEventListener('pointerup',()=>{miniDragging=false;});mini.addEventListener('pointercancel',()=>{miniDragging=false;});
  function neighbour(direction){if(!chart)return;const index=active?chart.notes.findIndex(n=>n.id===active)+direction:M.lowerBound(chart.notes,transport.getTime()),note=chart.notes[Math.max(0,Math.min(chart.notes.length-1,index))];setSelection([note.id],note.id);const y=M.timeToY(note.time,view,pps,geometry.hitY);if(y<geometry.top+30||y>geometry.hitY+5)seek(note.time-(geometry.hitY-geometry.top)/pps*.35);}

  function download(value,suffix){const url=URL.createObjectURL(new Blob([JSON.stringify(value,null,2)],{type:'application/json'})),a=document.createElement('a');a.href=url;a.download=chart.name.replace(/[<>:"/\\|?*\x00-\x1f]/g,'_').slice(0,80)+suffix+'.json';document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);}
  function exportReview(){if(!chart)return;flushFields();download(C.validate(chart),'.review');persist();message('已导出审核文件，包含原始排键、修改和理由。');}
  async function importFile(file) {
    if (!file) return;
    const generation = ++importGeneration;
    try {
      if (file.size > 50 * 1024 * 1024) throw new Error('谱面文件不能超过 50 MB');
      message('正在读取 ' + file.name + '…');
      const text = (await file.text()).replace(/^\uFEFF/, '');
      if (generation !== importGeneration) return;
      let isAdofai = /\.adofai$/i.test(file.name);
      let value;
      if (isAdofai) {
        message('正在从 .adofai 生成四轨排键…');
        value = await window.AdofaiImport.importText(text, file.name);
      } else {
        value = JSON.parse(text);
        if (value && (Array.isArray(value.angleData) || typeof value.pathData === 'string')) {
          isAdofai = true;
          value = await window.AdofaiImport.importText(text, file.name);
        }
      }
      if (generation !== importGeneration) return;
      load(value, (isAdofai ? '已从原谱生成离线排键：' : '已导入 ') + file.name);
    } catch (e) {
      if (generation === importGeneration) message('导入失败：' + e.message + '。当前修改保留。', true);
    }
  }
  async function loadAudio(file){if(!file)return;const generation=++audioGeneration,own=transport;message('正在读取音乐和波形…');try{await own.loadFile(file);if(generation!==audioGeneration||own!==transport)return;own.setOffset(Number($('audio-offset').value));$('audio-name').textContent=file.name;message('音乐已载入。可用偏移对齐时间，设置 A/B 循环试听。');updateLoop();draw();}catch(e){if(generation===audioGeneration&&e.name!=='AbortError')message('音乐读取失败：'+e.message,true);}}
  $('import').addEventListener('change',async e=>{await importFile(e.target.files[0]);e.target.value='';});
  document.addEventListener('dragover',e=>{if(e.dataTransfer.types.includes('Files'))e.preventDefault();});document.addEventListener('drop',e=>{const file=e.dataTransfer.files[0];if(!file)return;e.preventDefault();if(/\.(adofai|json)$/i.test(file.name))importFile(file);else if(file.type.startsWith('audio/')||/\.(ogg|mp3|wav|flac|m4a|aac|opus)$/i.test(file.name))loadAudio(file);else message('请拖入 .adofai 原谱、审核 .json 或音乐文件。',true);});
  $('demo').addEventListener('click',()=>{if(window.RHYTHMHINT_DEMO)load(window.RHYTHMHINT_DEMO,'已打开内置谱面');});$('download').addEventListener('click',exportReview);
  $('download-summary').addEventListener('click',()=>{if(chart){flushFields();download(C.summarize(chart),'.summary');message('差异摘要已导出。');}});
  $('undo').addEventListener('click',()=>history('undo'));$('redo').addEventListener('click',()=>history('redo'));
  $('mirror').addEventListener('click',()=>batch({type:'mirror'}));$('shift-left').addEventListener('click',()=>batch({type:'shift',delta:-1}));$('shift-right').addEventListener('click',()=>batch({type:'shift',delta:1}));
  $('review-selection').addEventListener('click',()=>batch({type:'review',reviewed:true}));$('reviewed').addEventListener('change',e=>batch({type:'review',reviewed:e.target.checked}));$('reset-note').addEventListener('click',()=>batch({type:'reset'}));
  $('reason').addEventListener('input',()=>{$('reason').dataset.dirty='true';});$('reason').addEventListener('change',flushFields);$('comment').addEventListener('change',flushFields);
  $('complete').addEventListener('change',e=>{if(chart){flushFields();commit(C.validate({...chart,review:{...chart.review,status:e.target.checked?'complete':'draft'}}));}});
  $('next-unreviewed').addEventListener('click',()=>{if(!chart)return;const offset=active?chart.notes.findIndex(n=>n.id===active)+1:M.lowerBound(chart.notes,transport.getTime());for(let j=0;j<chart.notes.length;j++){const n=chart.notes[(offset+j)%chart.notes.length];if(!current(n).reviewed){setSelection([n.id],n.id);seek(n.time-(geometry.hitY-geometry.top)/pps*.4);return;}}message('所有音符已审核。');});
  $('restore-draft').addEventListener('click',()=>{if(savedDraft){try{const saved=savedDraft;flushFields();commit(C.restore(chart,saved));$('draft-banner').hidden=true;message('草稿已恢复。');}catch(e){message(e.message,true);}}});$('dismiss-draft').addEventListener('click',()=>{savedDraft=null;$('draft-banner').hidden=true;});
  $('play').addEventListener('click',togglePlayback);$('stop').addEventListener('click',()=>seek($('loop-enabled').checked?Number($('loop-a').value):minTime));$('seek').addEventListener('input',e=>seek(Number(e.target.value)));
  $('zoom').addEventListener('input',e=>{pps=Number(e.target.value);syncTransport();draw();});$('speed').addEventListener('change',e=>transport.setRate(Number(e.target.value)));
  $('follow').addEventListener('change',()=>{if($('follow').checked)view=transport.getTime();draw();});for(const id of ['grid-mode','grid-bpm','grid-origin','grid-division','show-original'])$(id).addEventListener('change',draw);
  function updateLoop(){try{const b=Number($('loop-b').value);if($('loop-enabled').checked&&b>transport.getDuration())throw new Error('循环终点超出音乐长度，请缩短 B 点。');transport.setLoop(Number($('loop-a').value),b,$('loop-enabled').checked);draw();}catch(e){$('loop-enabled').checked=false;transport.setLoop(0,1,false);message(e.message,true);}}
  for(const id of ['loop-enabled','loop-a','loop-b'])$(id).addEventListener('change',updateLoop);
  $('set-loop-a').addEventListener('click',()=>{$('loop-a').value=transport.getTime().toFixed(3);updateLoop();});$('set-loop-b').addEventListener('click',()=>{$('loop-b').value=transport.getTime().toFixed(3);updateLoop();});
  $('audio-file').addEventListener('change',e=>loadAudio(e.target.files[0]));$('audio-offset').addEventListener('change',()=>{const value=Number($('audio-offset').value);if(Number.isFinite(value)){transport.setOffset(value);lastPlayed=transport.getTime();updateLoop();draw();}});
  for(const [id,value]of[['tool-select','select'],['tool-pan','pan']])$(id).addEventListener('click',()=>{tool=value;for(const [button,mode]of[['tool-select','select'],['tool-pan','pan']]){$(button).classList.toggle('active',tool===mode);$(button).setAttribute('aria-pressed',String(tool===mode));}canvas.style.cursor=tool==='pan'?'grab':'crosshair';});
  $('toggle-inspector').addEventListener('click',()=>{flushFields();document.body.classList.toggle('inspector-hidden');$('toggle-inspector').setAttribute('aria-expanded',String(!document.body.classList.contains('inspector-hidden')));requestAnimationFrame(draw);});
  $('help').addEventListener('click',()=>$('help-dialog').showModal());$('help-close').addEventListener('click',()=>$('help-dialog').close());
  document.addEventListener('keydown',e=>{
    if(e.target.closest('input,textarea,select,[contenteditable="true"]')||$('help-dialog').open)return;const mod=e.ctrlKey||e.metaKey;
    if(mod&&e.key.toLowerCase()==='s'){e.preventDefault();exportReview();return;}if(!chart)return;
    if(mod&&e.key.toLowerCase()==='z'){e.preventDefault();history(e.shiftKey?'redo':'undo');return;}if(mod&&e.key.toLowerCase()==='a'){e.preventDefault();setSelection(chart.notes.map(n=>n.id));return;}if(mod||e.altKey)return;
    if(e.code==='Space'){e.preventDefault();if(!e.repeat)togglePlayback();}
    else if(e.key==='ArrowLeft'||e.key==='ArrowRight'){e.preventDefault();batch({type:'shift',delta:e.key==='ArrowLeft'?-1:1});}
    else if(e.key==='ArrowUp'||e.key==='ArrowDown'){e.preventDefault();neighbour(e.key==='ArrowUp'?1:-1);}
    else if(e.key==='Home'){e.preventDefault();seek(minTime);}else if(e.key==='End'){e.preventDefault();seek(endTime);}else if(e.key==='Escape'){gesture=null;setSelection([]);}
    else if(e.key==='Enter'&&e.target===canvas){e.preventDefault();batch({type:'review',reviewed:true});}
    else if(e.key.toLowerCase()==='m'){e.preventDefault();batch({type:'mirror'});}
    else {const lane=['KeyD','KeyF','KeyJ','KeyK'].indexOf(e.code);if(lane>=0&&selected.size&&[...selected].every(id=>byId.get(id).lanes.length===1)){e.preventDefault();batch({type:'assign',lanes:[lane]});}}
  });
  const observer=new ResizeObserver(draw);observer.observe($('stage'));observer.observe(mini);
  window.addEventListener('pagehide',()=>{flushFields();if(saveTimer)persist();transport.pause();});window.addEventListener('beforeunload',()=>{flushFields();if(saveTimer)persist();});
  if(window.innerWidth<900){document.body.classList.add('inspector-hidden');$('toggle-inspector').setAttribute('aria-expanded','false');}
  canvas.tabIndex=0;pps=Number($('zoom').value)||220;progress();inspector();draw();syncTransport();
  if(window.RHYTHMHINT_DEMO)try{load(window.RHYTHMHINT_DEMO,'已打开内置谱面');}catch(e){message(e.message,true);}
})();
