/* Pure review functions. The C# export is the immutable baseline; this file never replans notes. */
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.ReviewCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  const clone = value => JSON.parse(JSON.stringify(value));
  const finite = value => typeof value === 'number' && Number.isFinite(value);
  function requireThat(condition, message) { if (!condition) throw new Error(message); }
  function lanes(value, label) {
    requireThat(Array.isArray(value) && value.length > 0 && value.length <= 4, label + '：轨道数量必须为 1–4');
    const seen = new Set();
    value.forEach(item => {
      requireThat(item && Number.isInteger(item.lane) && item.lane >= 0 && item.lane < 4, label + '：轨道必须为 0–3');
      requireThat(!seen.has(item.lane), label + '：轨道重复');
      requireThat(finite(item.holdSeconds) && item.holdSeconds >= 0, label + '：长按必须为有限非负数');
      seen.add(item.lane);
    });
  }
  const ordered = items => [...items].sort((a, b) => a.lane - b.lane);
  const holdSet = items => items.map(x => x.holdSeconds).sort((a, b) => a - b).join('|');
  const sameLanes = (a, b) => JSON.stringify(ordered(a).map(x => [x.lane, x.holdSeconds])) === JSON.stringify(ordered(b).map(x => [x.lane, x.holdSeconds]));
  function validate(document) {
    requireThat(document && typeof document === 'object', '请选择审核 JSON 文件');
    requireThat(document.format === 'rhythmhint-review', '文件不是 RhythmHint 审核谱面；请使用 Mod 的审核导出');
    requireThat(document.schemaVersion === 1, '不支持这个审核文件版本');
    for (const field of ['chartId', 'name', 'algorithm']) requireThat(typeof document[field] === 'string' && document[field].length > 0, '缺少 ' + field);
    requireThat(['game', 'offline-approximation', 'demo'].includes(document.source), '未知数据来源');
    requireThat(Array.isArray(document.keyLabels) && document.keyLabels.length === 4 && document.keyLabels.every(x => typeof x === 'string' && x.length > 0), '需要四个按键标签');
    requireThat(document.options && typeof document.options === 'object', '缺少原始排键参数');
    for (const field of ['ChordWindow', 'ResetWindow', 'ChordFocusWindow', 'JackWindow']) requireThat(finite(document.options[field]) && document.options[field] >= 0, '排键参数无效：' + field);
    for (const field of ['UseRoll', 'OuterRoll', 'LeftHanded']) requireThat(typeof document.options[field] === 'boolean', '排键参数无效：' + field);
    // Stored legacy baselines are editable without being upgraded or replanned.
    // Current exports carry every new option, and must not silently lose one.
    const currentOptions = document.algorithm.startsWith('NotePlanner/054fe9cd6a8961afbf0c8f9c181bd3dcedb12b18/');
    for (const field of ['ChordAngle', 'ChordMaxGap', 'ReviewShortMinGap', 'ReviewShortMaxGap', 'ReviewTimingTolerance'])
      if (currentOptions || field in document.options) requireThat(finite(document.options[field]) && document.options[field] >= 0, '排键参数无效：' + field);
    if (currentOptions || 'ChordsOnOtherHand' in document.options) requireThat(typeof document.options.ChordsOnOtherHand === 'boolean', '排键参数无效：ChordsOnOtherHand');
    if (currentOptions || 'Style' in document.options) {
      requireThat(['TimeReset', 'ReviewPreferenceV1'].includes(document.options.Style), '排键风格无效');
      if (currentOptions) requireThat(document.algorithm.endsWith('/' + document.options.Style), '算法标识与排键风格不一致');
    }
    for (const field of ['ReviewShortMinNotes', 'ReviewShortMaxNotes'])
      if (currentOptions || field in document.options) requireThat(Number.isInteger(document.options[field]) && document.options[field] >= 2, '排键参数无效：' + field);
    if (currentOptions) requireThat(document.options.ReviewShortMaxNotes >= document.options.ReviewShortMinNotes
      && document.options.ReviewShortMaxGap >= document.options.ReviewShortMinGap, '短串参数范围无效');
    requireThat(Array.isArray(document.notes) && document.notes.length > 0 && document.notes.length <= 200000, '音符数量必须为 1–200000');
    const byId = new Map();
    let previous = -Infinity;
    document.notes.forEach((note, index) => {
      requireThat(note && typeof note.id === 'string' && note.id.length > 0 && !byId.has(note.id), '音符 ID 缺失或重复');
      requireThat(Number.isInteger(note.tileIndex) && Number.isInteger(note.lastTileIndex) && note.lastTileIndex >= note.tileIndex, '砖块编号无效');
      requireThat(finite(note.time) && note.time >= previous, '音符时间必须是有限数并按时间排列');
      lanes(note.lanes, '音符 ' + (index + 1));
      previous = note.time;
      byId.set(note.id, note);
    });
    if (document.sourceHits !== undefined) {
      requireThat(Array.isArray(document.sourceHits) && document.sourceHits.length > 0, '原始击打数据必须为非空数组');
      const tiles = new Set();
      let lastTime = -Infinity;
      document.sourceHits.forEach(hit => {
        requireThat(hit && Number.isInteger(hit.tileIndex) && hit.tileIndex >= 0 && !tiles.has(hit.tileIndex), '原始击打砖块编号无效或重复');
        requireThat(finite(hit.time) && hit.time >= lastTime, '原始击打时间必须有限且按时间排列');
        requireThat(finite(hit.holdSeconds) && hit.holdSeconds >= 0, '原始击打长按必须为有限非负数');
        requireThat(hit.angle == null || (finite(hit.angle) && hit.angle >= 0), '原始击打转角必须未知或为有限非负数');
        tiles.add(hit.tileIndex); lastTime = hit.time;
      });
    }
    requireThat(document.review && ['draft', 'complete'].includes(document.review.status) && typeof document.review.comment === 'string' && Array.isArray(document.review.edits), '审核记录格式无效');
    const edited = new Set();
    document.review.edits.forEach(edit => {
      requireThat(edit && byId.has(edit.noteId) && !edited.has(edit.noteId), '审核记录包含未知或重复的音符 ID');
      lanes(edit.lanes, '审核音符');
      requireThat(holdSet(edit.lanes) === holdSet(byId.get(edit.noteId).lanes), '审核只能调整排键，不能增删音符或改变长按时长');
      requireThat(typeof edit.reason === 'string' && typeof edit.reviewed === 'boolean', '审核理由或状态无效');
      edited.add(edit.noteId);
    });
    if (document.review.status === 'complete') requireThat(document.review.edits.filter(x => x.reviewed).length === document.notes.length, '完成审核需要逐个标记全部音符');
    return clone(document);
  }
  function baseline(document) {
    return JSON.stringify({ chartId: document.chartId, algorithm: document.algorithm, source: document.source,
      options: document.options, keyLabels: document.keyLabels,
      sourceHits: document.sourceHits?.map(hit => hit.angle == null ? [hit.tileIndex, hit.time, hit.holdSeconds] : [hit.tileIndex, hit.time, hit.holdSeconds, hit.angle]),
      notes: document.notes.map(n => [n.id, n.tileIndex, n.lastTileIndex, n.time, ordered(n.lanes).map(l => [l.lane, l.holdSeconds])]) });
  }
  function editMap(document) { return new Map(document.review.edits.map(edit => [edit.noteId, edit])); }
  function update(document, noteId, patch) {
    const result = clone(document);
    const note = result.notes.find(n => n.id === noteId);
    requireThat(note, '音符不存在');
    const i = result.review.edits.findIndex(e => e.noteId === noteId);
    const current = i >= 0 ? result.review.edits[i] : { noteId, lanes: clone(note.lanes), reason: '', reviewed: false };
    const next = { ...current, ...clone(patch), noteId };
    if (i >= 0) result.review.edits[i] = next; else result.review.edits.push(next);
    if (!next.reviewed) result.review.status = 'draft';
    return validate(result);
  }
  function assign(note, targetLanes, currentLanes = note.lanes) {
    requireThat(Array.isArray(targetLanes) && targetLanes.length === note.lanes.length, '请保持原有按键数量');
    const source = ordered(currentLanes);
    const target = [...targetLanes].sort((a, b) => a - b).map((lane, i) => ({ lane, holdSeconds: source[i].holdSeconds }));
    lanes(target, '新排键');
    return target;
  }
  function stats(document) {
    const edits = editMap(document);
    let reviewed = 0, changed = 0, missingReasons = 0;
    for (const note of document.notes) {
      const edit = edits.get(note.id);
      if (edit?.reviewed) reviewed++;
      if (edit && !sameLanes(note.lanes, edit.lanes)) { changed++; if (!edit.reason.trim()) missingReasons++; }
    }
    return { total: document.notes.length, reviewed, changed, missingReasons };
  }
  const handName = items => {
    const hands = new Set(items.map(l => l.lane < 2 ? '左手' : '右手'));
    return hands.size > 1 ? '双手' : [...hands][0];
  };
  function summarize(document, now = new Date().toISOString()) {
    const checked = validate(document), edits = editMap(checked), summary = stats(checked);
    const changes = [], transitions = new Map(), reasons = new Map();
    const describe = note => note ? { id: note.id, time: note.time, tileIndex: note.tileIndex,
      original: clone(note.lanes), revised: clone(edits.get(note.id)?.lanes || note.lanes) } : null;
    checked.notes.forEach((note, index) => {
      const edit = edits.get(note.id);
      if (!edit || sameLanes(note.lanes, edit.lanes)) return;
      const previous = checked.notes[index - 1], next = checked.notes[index + 1];
      const transition = ordered(note.lanes).map(x => checked.keyLabels[x.lane]).join('+') + ' → ' + ordered(edit.lanes).map(x => checked.keyLabels[x.lane]).join('+');
      transitions.set(transition, (transitions.get(transition) || 0) + 1);
      const reason = edit.reason.trim();
      if (reason) reasons.set(reason, (reasons.get(reason) || 0) + 1);
      changes.push({ ...describe(note), reason, reviewed: edit.reviewed, originalHand: handName(note.lanes), revisedHand: handName(edit.lanes),
        gapBeforeMs: previous ? (note.time - previous.time) * 1000 : null,
        gapAfterMs: next ? (next.time - note.time) * 1000 : null,
        previous: describe(previous), next: describe(next) });
    });
    return { format: 'rhythmhint-review-summary', schemaVersion: 1, chartId: checked.chartId, name: checked.name,
      source: checked.source, algorithm: checked.algorithm, options: clone(checked.options), keyLabels: clone(checked.keyLabels), generatedAt: now,
      status: checked.review.status, comment: checked.review.comment, statistics: summary,
      interpretation: '以下为人工修改的描述性统计，不是已验证的附加排键规则。需要结合理由、未修改音符和更多谱面验证触发条件与例外。',
      transitions: [...transitions].map(([transition, count]) => ({ transition, count })),
      reasons: [...reasons].map(([reason, count]) => ({ reason, count })), changes };
  }
  function restore(document, saved) {
    requireThat(saved && saved.baseline === baseline(document), '本地草稿属于不同的原始排键，已拒绝恢复');
    return validate({ ...clone(document), review: saved.review });
  }
  return { clone, validate, baseline, sameLanes, ordered, editMap, update, assign, stats, summarize, restore, handName };
});
