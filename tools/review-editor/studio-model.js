/* Geometry and atomic editing operations for the four-lane chart workspace. */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory(require('./core.js'));
  else root.StudioModel = factory(root.ReviewCore);
})(typeof globalThis !== 'undefined' ? globalThis : this, function (C) {
  'use strict';

  function requireThat(condition, message) { if (!condition) throw new Error(message); }
  const validLane = lane => Number.isInteger(lane) && lane >= 0 && lane < 4;
  const timeToY = (time, now, pps, hitY) => hitY - (time - now) * pps;
  const yToTime = (y, now, pps, hitY) => now + (hitY - y) / pps;

  function layout(width, height) {
    requireThat(Number.isFinite(width) && Number.isFinite(height) && width > 0 && height > 0, '画布尺寸无效');
    const gutter = Math.min(56, width * 0.12);
    const trackWidth = Math.min(600, width - 2 * gutter);
    const top = Math.min(28, height * 0.1);
    const bottom = Math.max(top, height - Math.min(24, height * 0.1));
    const hitY = Math.max(top, Math.min(bottom, height - Math.min(80, height * 0.22)));
    return { x: (width - trackWidth) / 2, laneWidth: trackWidth / 4, width: trackWidth, hitY, top, bottom };
  }

  function currentLanes(note, edits) {
    const entry = edits?.get(note.id);
    return Array.isArray(entry) ? entry : entry?.lanes || note.lanes;
  }

  function lowerBound(notes, time) {
    let lo = 0, hi = notes.length;
    while (lo < hi) {
      const mid = (lo + hi) >>> 1;
      if (notes[mid].time < time) lo = mid + 1;
      else hi = mid;
    }
    return lo;
  }

  function upperBound(notes, time) {
    let lo = 0, hi = notes.length;
    while (lo < hi) {
      const mid = (lo + hi) >>> 1;
      if (notes[mid].time <= time) lo = mid + 1;
      else hi = mid;
    }
    return lo;
  }

  // start/end are a half-open candidate range. maxHoldSeconds is the largest hold
  // in the entire document; Infinity (the safe default) retains all earlier holds.
  function visibleRange(notes, now, pps, geometry, maxHoldSeconds = Infinity) {
    requireThat(Number.isFinite(pps) && pps > 0, '缩放必须大于零');
    requireThat(maxHoldSeconds >= 0, '长按范围无效');
    const firstTime = yToTime(geometry.bottom + 10, now, pps, geometry.hitY);
    const lastTime = yToTime(geometry.top - 10, now, pps, geometry.hitY);
    return { start: lowerBound(notes, firstTime - maxHoldSeconds), end: upperBound(notes, lastTime) };
  }

  // The four lane boundaries are authoritative. Heads take precedence over hold
  // bodies so a long hold passing behind a nearby tap cannot steal its click.
  function hitTest(notes, edits, x, y, now, pps, geometry, tolerance = 10) {
    if (x < geometry.x || x >= geometry.x + geometry.width || y < geometry.top || y > geometry.bottom) return null;
    const lane = Math.floor((x - geometry.x) / geometry.laneWidth);
    if (!validLane(lane)) return null;
    let head = null, body = null, headDistance = Infinity, bodyDistance = Infinity;
    for (const note of notes) {
      const item = currentLanes(note, edits).find(entry => entry.lane === lane);
      if (!item) continue;
      const headY = timeToY(note.time, now, pps, geometry.hitY);
      const distance = Math.abs(y - headY);
      if (distance <= tolerance && distance < headDistance) {
        head = { noteId: note.id, lane }; headDistance = distance;
      }
      if (item.holdSeconds > 0) {
        const tailY = timeToY(note.time + item.holdSeconds, now, pps, geometry.hitY);
        const centerX = geometry.x + (lane + 0.5) * geometry.laneWidth;
        if (y >= tailY - 3 && y <= headY + 3 && Math.abs(x - centerX) <= Math.max(6, geometry.laneWidth * 0.2) && distance < bodyDistance) {
          body = { noteId: note.id, lane }; bodyDistance = distance;
        }
      }
    }
    return head || body;
  }

  function boxSelect(notes, edits, rect, now, pps, geometry) {
    const left = Math.min(rect.x, rect.x + rect.width), right = Math.max(rect.x, rect.x + rect.width);
    const top = Math.min(rect.y, rect.y + rect.height), bottom = Math.max(rect.y, rect.y + rect.height);
    const result = [];
    for (const note of notes) {
      const y = timeToY(note.time, now, pps, geometry.hitY);
      if (y < top || y > bottom || y < geometry.top || y > geometry.bottom) continue;
      if (currentLanes(note, edits).some(item => {
        const x = geometry.x + (item.lane + 0.5) * geometry.laneWidth;
        return x >= left && x <= right;
      })) result.push(note.id);
    }
    return result;
  }

  function applyBatch(document, ids, action) {
    requireThat(Array.isArray(ids) || ids instanceof Set, '请选择需要调整的音符');
    requireThat(action && ['shift', 'mirror', 'assign', 'review', 'reset', 'reason'].includes(action.type), '未知的编辑操作');
    if (action.type === 'shift') requireThat(Number.isInteger(action.delta), '移轨数量必须为整数');
    if (action.type === 'review') requireThat(typeof action.reviewed === 'boolean', '审核状态无效');
    if (action.type === 'reason') requireThat(typeof action.reason === 'string', '审核理由必须是文字');

    const chosen = new Set(ids), notes = new Map(document.notes.map(note => [note.id, note]));
    for (const id of chosen) requireThat(notes.has(id), '选区中有不存在的音符');
    const edits = C.editMap(document);
    let status = document.review.status;
    for (const id of chosen) {
      const note = notes.get(id);
      const previous = edits.get(id) || { noteId: id, lanes: note.lanes, reason: '', reviewed: false };
      let next = { ...previous };
      switch (action.type) {
        case 'shift':
          next.lanes = previous.lanes.map(item => ({ ...item, lane: item.lane + action.delta }));
          requireThat(next.lanes.every(item => validLane(item.lane)), '选区会超出四条轨道，本次移动未生效');
          break;
        case 'mirror':
          next.lanes = previous.lanes.map(item => ({ ...item, lane: 3 - item.lane }));
          break;
        case 'assign':
          next.lanes = C.assign(note, action.lanes, previous.lanes);
          break;
        case 'review':
          next.reviewed = action.reviewed;
          break;
        case 'reason':
          next.reason = action.reason;
          break;
        case 'reset':
          next = { ...previous, lanes: note.lanes, reason: '', reviewed: false };
          break;
      }
      if (!C.sameLanes(previous.lanes, next.lanes)) next.reviewed = false;
      if (!next.reviewed) status = 'draft';
      edits.set(id, next);
    }
    // Construct only new review objects before validation. Failure at any point
    // leaves the original document untouched; validation clones exactly once.
    return C.validate({ ...document, review: { ...document.review, status, edits: [...edits.values()] } });
  }

  function moveLane(document, noteId, fromLane, toLane) {
    requireThat(validLane(fromLane) && validLane(toLane), '轨道必须为 0–3');
    const note = document.notes.find(item => item.id === noteId);
    requireThat(note, '音符不存在');
    const edits = C.editMap(document);
    const previous = edits.get(noteId) || { noteId, lanes: note.lanes, reason: '', reviewed: false };
    requireThat(previous.lanes.some(item => item.lane === fromLane), '原轨道上没有这个音符');
    requireThat(fromLane === toLane || !previous.lanes.some(item => item.lane === toLane), '该音符已占用目标轨道，请拖到空轨');
    if (fromLane === toLane) return C.validate(document);
    const lanes = previous.lanes.map(item => item.lane === fromLane ? { ...item, lane: toLane } : item);
    edits.set(noteId, { ...previous, lanes, reviewed: false });
    return C.validate({ ...document, review: { ...document.review, status: 'draft', edits: [...edits.values()] } });
  }

  return { timeToY, yToTime, layout, hitTest, boxSelect, lowerBound, visibleRange, applyBatch, moveLane };
});
