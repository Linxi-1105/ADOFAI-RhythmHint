/* Shared browser port of src/RhythmHint.Core/NotePlanner.cs.
 * Keep the method order, epsilon and post-processing in sync with the C# source.
 * This plans already-timed TileHits; .adofai timing is a separate concern.
 */
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.NotePlanner = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  const Epsilon = 1e-6;
  const AngleTolerance = 0.1;
  const revision = '054fe9cd6a8961afbf0c8f9c181bd3dcedb12b18';
  const LeftOuter = 0, LeftIndex = 1, RightIndex = 2, RightOuter = 3;
  const defaults = Object.freeze({
    ChordWindow: 0.035,
    ChordAngle: 30,
    ChordMaxGap: 0.06,
    ResetWindow: 0.165,
    ChordFocusWindow: 0.45,
    JackWindow: 0.15,
    UseRoll: true,
    OuterRoll: true,
    LeftHanded: false,
    ChordsOnOtherHand: false,
    Style: 'TimeReset',
    ReviewShortMinGap: 0.060,
    ReviewShortMaxGap: 0.0825,
    ReviewShortMinNotes: 4,
    ReviewShortMaxNotes: 5,
    ReviewTimingTolerance: 0.001
  });
  const handOf = lane => lane < 2 ? 0 : 1;
  const indexLane = hand => hand === 0 ? LeftIndex : RightIndex;
  const partner = lane => lane === 0 ? 1 : lane === 1 ? 0 : lane === 2 ? 3 : 2;
  const sorted = lanes => lanes.sort((a, b) => a.lane - b.lane);
  const firstLane = note => note.lanes[0].lane;
  const isChord = note => note.lanes.length > 1;
  const hasLane = (note, lane) => note.lanes.some(item => item.lane === lane);
  const pairHand = note => note.lanes.reduce((sum, item) => sum + handOf(item.lane), 0) * 2 >= note.lanes.length ? 1 : 0;
  const algorithmFor = options => 'NotePlanner/' + revision + '/' + (options?.Style ?? defaults.Style);

  function plan(hits, options = defaults) {
    const o = { ...defaults, ...options };
    validateInput(hits, o);
    // C# TileHit has a default HoldSeconds of zero; accept the same omission.
    const tiles = hits.map(hit => ({ tileIndex: hit.tileIndex, time: hit.time, holdSeconds: hit.holdSeconds ?? 0, angle: hit.angle ?? NaN }));
    const moments = Moments(tiles, o);
    const reviewPreference = o.Style === 'ReviewPreferenceV1' && !tiles.some(hit => hit.holdSeconds > 0);
    const notes = [], rolled = [], hands = new HandState(), focus = FocusChords(moments, o, reviewPreference);
    const preferredChordHand = o.ChordsOnOtherHand ? 0 : 1;
    const yieldIndex = indexLane(1 - preferredChordHand), yieldOuter = partner(yieldIndex);
    for (let i = 0; i < moments.length; i++) {
      const moment = moments[i], time = moment[0].time, last = notes.length ? notes[notes.length - 1] : null;
      if (moment.length > 1) {
        notes.push(BuildChord(moment, ChordHand(preferredChordHand, time, last, o), last, o));
        rolled.push(false);
      } else {
        let [lane, roll] = hands.Next(time, o);
        if (focus[i] >= 0) {
          const chordHand = focus[i] < i ? pairHand(notes[focus[i]]) : preferredChordHand;
          [lane, roll] = [FocusLane(1 - chordHand, time, last, o), false];
        }
        if (lane === yieldIndex && i + 1 < moments.length && focus[i + 1] === i + 2
          && moments[i + 1][0].time - time < o.JackWindow - Epsilon && !Blocked(yieldOuter, time, last, o)) {
          [lane, roll] = [yieldOuter, false];
        }
        if (Blocked(lane, time, last, o)) [lane, roll] = [NearestFreeLane(lane, time, o, last), false];
        notes.push({ tileIndex: moment[0].tileIndex, lastTileIndex: moment[0].tileIndex, time,
          lanes: [{ lane, holdSeconds: moment[0].holdSeconds }] });
        rolled.push(roll);
      }
      hands.Pressed(notes[notes.length - 1]);
    }
    if (o.UseRoll && o.OuterRoll) OuterFirst(notes, rolled, o);
    if (reviewPreference) ReviewPhrases(notes, o);
    return o.LeftHanded ? notes.map(Mirror) : notes;
  }

  function Mirror(note) {
    return { tileIndex: note.tileIndex, lastTileIndex: note.lastTileIndex, time: note.time,
      lanes: sorted(note.lanes.map(item => ({ lane: 3 - item.lane, holdSeconds: item.holdSeconds }))) };
  }

  class HandState {
    constructor() { this.last = -1; this.lastTime = 0; this.lastRight = -Infinity; }
    Next(time, o) {
      const r = o.ResetWindow, d = time - this.lastTime;
      switch (this.last) {
        case RightIndex:
          if (d > r + Epsilon) return [RightIndex, false];
          if (d > r / 2 + Epsilon || !o.UseRoll) return [LeftIndex, false];
          return [RightOuter, true];
        case RightOuter:
          return d > r + Epsilon ? [RightIndex, false] : [LeftIndex, false];
        case LeftIndex:
          if (o.UseRoll && (d <= r / 2 + Epsilon || time - this.lastRight <= r + Epsilon)) return [LeftOuter, true];
          return [RightIndex, false];
        default:
          return [RightIndex, false];
      }
    }
    Pressed(note) {
      const first = firstLane(note), lane = isChord(note) ? (pairHand(note) === 1 ? RightOuter : LeftOuter) : first;
      this.last = lane; this.lastTime = note.time;
      if (note.lanes.some(item => handOf(item.lane) === 1)) this.lastRight = note.time;
    }
  }

  function FocusChords(moments, o, reviewPreference) {
    const focus = new Array(moments.length).fill(-1);
    const focusWindow = reviewPreference ? Math.min(o.ChordFocusWindow, o.ResetWindow) : o.ChordFocusWindow;
    for (let i = 0; i < moments.length; i++) {
      if (moments[i].length > 1) continue;
      const time = moments[i][0].time;
      const before = i > 0 && moments[i - 1].length > 1 ? time - moments[i - 1][0].time : Infinity;
      const after = i + 1 < moments.length && moments[i + 1].length > 1 ? moments[i + 1][0].time - time : Infinity;
      if (after <= focusWindow + Epsilon && after <= before + Epsilon) focus[i] = i + 1;
      else if (before <= focusWindow + Epsilon) focus[i] = i - 1;
    }
    return focus;
  }

  function FocusLane(hand, time, last, o) {
    const index = indexLane(hand);
    return Blocked(index, time, last, o) ? partner(index) : index;
  }

  function Moments(tiles, o) {
    const moments = [];
    for (const tile of tiles) {
      const group = moments[moments.length - 1];
      const samePress = group && (Number.isNaN(tile.angle)
        ? tile.time - group[0].time < o.ChordWindow
        : tile.angle <= o.ChordAngle + AngleTolerance && tile.time - group[group.length - 1].time <= o.ChordMaxGap + Epsilon);
      if (samePress) group.push(tile);
      else moments.push([tile]);
    }
    return moments.flatMap(group => group.length > 4 ? group.map(tile => [tile]) : [group]);
  }

  function ChordHand(preferred, time, last, o) {
    const handBlocked = hand => Blocked(hand * 2, time, last, o) || Blocked(hand * 2 + 1, time, last, o);
    return handBlocked(preferred) && !handBlocked(1 - preferred) ? 1 - preferred : preferred;
  }

  function BuildChord(moment, hand, last, o) {
    const first = moment[0], main = indexLane(hand), otherIndex = indexLane(1 - hand);
    const extra = Blocked(otherIndex, first.time, last, o) && !Blocked(partner(otherIndex), first.time, last, o) ? partner(otherIndex) : otherIndex;
    const order = [main, partner(main), extra, partner(extra)];
    return { tileIndex: first.tileIndex, lastTileIndex: moment[moment.length - 1].tileIndex, time: first.time,
      lanes: sorted(moment.map((hit, i) => ({ lane: order[i], holdSeconds: hit.holdSeconds }))) };
  }

  function ReviewPhrases(notes, o) {
    for (let start = 0; start < notes.length;) {
      if (isChord(notes[start])) { start++; continue; }
      let end = start;
      while (end + 1 < notes.length && !isChord(notes[end + 1]) && notes[end + 1].time - notes[end].time <= o.ResetWindow + Epsilon) end++;
      const length = end - start + 1;
      if (length >= 2) {
        let medium = true, shortRoll = o.UseRoll && o.OuterRoll && length >= o.ReviewShortMinNotes && length <= o.ReviewShortMaxNotes;
        const firstGap = notes[start + 1].time - notes[start].time;
        for (let i = start + 1; i <= end; i++) {
          const gap = notes[i].time - notes[i - 1].time;
          medium = medium && gap > o.ResetWindow / 2 + Epsilon;
          shortRoll = shortRoll && gap >= o.ReviewShortMinGap - Epsilon && gap <= Math.min(o.ReviewShortMaxGap, o.ResetWindow / 2) + Epsilon
            && Math.abs(gap - firstGap) <= o.ReviewTimingTolerance + Epsilon;
        }
        if (medium || shortRoll) {
          let preferred = RightIndex;
          if (start > 0 && isChord(notes[start - 1]) && notes[start].time - notes[start - 1].time <= Math.min(o.ChordFocusWindow, o.ResetWindow) + Epsilon)
            preferred = indexLane(1 - pairHand(notes[start - 1]));
          const first = ReviewPhaseFits(notes, start, end, preferred, o) ? preferred
            : ReviewPhaseFits(notes, start, end, OtherIndex(preferred), o) ? OtherIndex(preferred) : -1;
          if (first >= 0) for (let i = start; i <= end; i++) notes[i] = WithLane(notes[i], (i - start) % 2 === 0 ? first : OtherIndex(first));
        }
      }
      start = end + 1;
    }
  }
  const OtherIndex = lane => lane === RightIndex ? LeftIndex : RightIndex;
  function ReviewPhaseFits(notes, start, end, first, o) {
    const last = (end - start) % 2 === 0 ? first : OtherIndex(first);
    return (start === 0 || ReviewBoundaryFits(notes[start - 1], notes[start].time, first, true, o))
      && (end + 1 === notes.length || ReviewBoundaryFits(notes[end + 1], notes[end].time, last, false, o));
  }
  function ReviewBoundaryFits(neighbour, time, lane, before, o) {
    if (Jacks(neighbour, lane, time, o)) return false;
    if (!o.UseRoll || !o.OuterRoll || isChord(neighbour) || Math.abs(neighbour.time - time) > o.ResetWindow + Epsilon) return true;
    const from = before ? firstLane(neighbour) : lane, to = before ? lane : firstLane(neighbour);
    return !((from === LeftIndex && to === LeftOuter) || (from === RightIndex && to === RightOuter));
  }

  function OuterFirst(notes, rolled, o) {
    for (let i = 1; i < notes.length; i++) {
      const prev = notes[i - 1], cur = notes[i];
      if (!rolled[i] || isChord(prev) || cur.time - prev.time > o.ResetWindow + Epsilon) continue;
      const outer = firstLane(cur), index = firstLane(prev);
      if (partner(outer) !== index) continue;
      if ((i >= 2 && Jacks(notes[i - 2], outer, prev.time, o))
        || (i + 1 < notes.length && Jacks(notes[i + 1], index, cur.time, o))) {
        const other = indexLane(1 - handOf(outer));
        if (!Jacks(prev, other, cur.time, o) && !(i + 1 < notes.length && Jacks(notes[i + 1], other, cur.time, o))) notes[i] = WithLane(cur, other);
        continue;
      }
      notes[i - 1] = WithLane(prev, outer);
      notes[i] = WithLane(cur, index);
    }
  }

  function Jacks(neighbour, lane, time, o) { return hasLane(neighbour, lane) && Math.abs(neighbour.time - time) < o.JackWindow - Epsilon; }
  function WithLane(note, lane) {
    return { tileIndex: note.tileIndex, lastTileIndex: note.lastTileIndex, time: note.time, lanes: [{ lane, holdSeconds: note.lanes[0].holdSeconds }] };
  }
  function Blocked(lane, time, last, o) { return last !== null && hasLane(last, lane) && time - last.time < o.JackWindow - Epsilon; }
  function NearestFreeLane(lane, time, o, last) {
    let best = lane, bestScore = 2147483647;
    for (let c = 0; c < 4; c++) {
      if (Blocked(c, time, last, o)) continue;
      const score = Math.min(...last.lanes.map(item => Math.abs(item.lane - c))) * 10 + Math.abs(c - lane);
      if (score < bestScore) { best = c; bestScore = score; }
    }
    return best;
  }

  function validateInput(hits, options) {
    if (!Array.isArray(hits)) throw new Error('排键输入必须为击打数组');
    for (const field of ['ChordWindow', 'ChordAngle', 'ChordMaxGap', 'ResetWindow', 'ChordFocusWindow', 'JackWindow']) {
      if (typeof options[field] !== 'number' || !Number.isFinite(options[field]) || options[field] < 0) throw new Error('排键参数无效：' + field);
    }
    for (const field of ['UseRoll', 'OuterRoll', 'LeftHanded', 'ChordsOnOtherHand']) {
      if (typeof options[field] !== 'boolean') throw new Error('排键参数无效：' + field);
    }
    if (options.Style === 'ReviewPreferenceV1') {
      for (const field of ['ReviewShortMinGap', 'ReviewShortMaxGap', 'ReviewTimingTolerance'])
        if (typeof options[field] !== 'number' || !Number.isFinite(options[field]) || options[field] < 0) throw new Error('排键参数无效：' + field);
      if (!Number.isInteger(options.ReviewShortMinNotes) || !Number.isInteger(options.ReviewShortMaxNotes)
        || options.ReviewShortMinNotes < 2 || options.ReviewShortMaxNotes < options.ReviewShortMinNotes || options.ReviewShortMaxGap < options.ReviewShortMinGap)
        throw new Error('短串排键参数无效');
    }
    let previous = -Infinity;
    for (const hit of hits) {
      if (!hit || !Number.isInteger(hit.tileIndex) || hit.tileIndex < 0
        || typeof hit.time !== 'number' || !Number.isFinite(hit.time) || hit.time < previous
        || (hit.holdSeconds !== undefined && (typeof hit.holdSeconds !== 'number' || !Number.isFinite(hit.holdSeconds) || hit.holdSeconds < 0))
        || (hit.angle != null && (typeof hit.angle !== 'number' || (!Number.isNaN(hit.angle) && (!Number.isFinite(hit.angle) || hit.angle < 0))))) {
        throw new Error('击打必须按时间排序，并包含有效砖号、时间和长按时长');
      }
      previous = hit.time;
    }
  }

  return { defaults, plan, revision, algorithmFor, version: algorithmFor(defaults) };
});
