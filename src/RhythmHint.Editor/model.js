/* 手序编辑器的数据和编辑操作（不碰界面，Node 能直接测）。
 * 文件格式和 src/RhythmHint.Core/CustomFingering.cs 一致：
 *   hits:  [砖号, 时间, 长按, 转角或 null]
 *   notes: [第一块砖号, 最后一块砖号, [[轨道, 长按], …]]
 * 编辑只改每个音符按哪几个键，不增删音符、不改时间和长按（时间以游戏为准）。 */
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.FingeringModel = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  const FILE_FORMAT = 'rhythmhint-fingering';
  const SESSION_FORMAT = 'rhythmhint-editor-session';
  const SCHEMA_VERSION = 1;

  class FingeringError extends Error {
    constructor(code, detail) {
      super(code + (detail ? ': ' + detail : ''));
      this.code = code;
      this.detail = detail || '';
    }
  }

  const finite = v => typeof v === 'number' && Number.isFinite(v);
  const int = v => Number.isInteger(v);
  function need(ok, code, detail) { if (!ok) throw new FingeringError(code, detail); }

  /** 按轨道排好的 [[轨道, 长按]]。 */
  const sortLanes = lanes => lanes.slice().sort((a, b) => a[0] - b[0]);
  const laneKey = lanes => sortLanes(lanes).map(l => l[0]).join('+');

  /** 读一份手序（文件里的 fingering 段，或单独的手序文件）。 */
  function parseFingering(obj) {
    need(obj && typeof obj === 'object', 'notFingering');
    need(obj.format === FILE_FORMAT, 'notFingering');
    need(int(obj.schemaVersion) && obj.schemaVersion >= 1, 'badFile', 'schemaVersion');
    need(obj.schemaVersion <= SCHEMA_VERSION, 'tooNew');
    const keys = obj.keys === undefined ? 4 : obj.keys;
    need(int(keys) && keys >= 1 && keys <= 16, 'badFile', 'keys');
    need(Array.isArray(obj.hits) && obj.hits.length > 0, 'badFile', 'hits');
    need(Array.isArray(obj.notes) && obj.notes.length > 0, 'badFile', 'notes');

    const hits = [];
    const index = new Map();
    let lastTile = -Infinity, lastTime = -Infinity;
    obj.hits.forEach(h => {
      need(Array.isArray(h) && h.length >= 3 && int(h[0]) && finite(h[1]) && finite(h[2]) && h[2] >= 0, 'badFile', 'hit');
      need(h[0] > lastTile && h[1] >= lastTime, 'badFile', 'hitOrder');
      const angle = h.length > 3 && h[3] !== null ? h[3] : null;
      need(angle === null || finite(angle), 'badFile', 'angle');
      index.set(h[0], hits.length);
      hits.push({ tile: h[0], time: h[1], hold: h[2], angle });
      lastTile = h[0]; lastTime = h[1];
    });

    const notes = [];
    let next = 0;
    obj.notes.forEach(n => {
      need(Array.isArray(n) && n.length === 3 && int(n[0]) && int(n[1]) && Array.isArray(n[2]), 'badFile', 'note');
      const first = index.get(n[0]), last = index.get(n[1]);
      need(first === next && last !== undefined && last >= first, 'badFile', 'coverage');
      need(n[2].length >= 1 && n[2].length <= keys, 'badFile', 'laneCount');
      const seen = new Set();
      const lanes = n[2].map(l => {
        need(Array.isArray(l) && l.length === 2 && int(l[0]) && l[0] >= 0 && l[0] < keys && !seen.has(l[0]) && finite(l[1]) && l[1] >= 0, 'badFile', 'lane');
        seen.add(l[0]);
        return [l[0], l[1]];
      });
      notes.push({ id: notes.length, first, last, tile: n[0], lastTile: n[1], time: hits[first].time, lanes: sortLanes(lanes) });
      next = last + 1;
    });
    need(next === hits.length, 'badFile', 'coverage');

    const text = v => typeof v === 'string' ? v.slice(0, 4000) : '';
    return {
      keys,
      meta: {
        song: text(obj.song), artist: text(obj.artist), charter: text(obj.charter),
        author: text(obj.author), comment: text(obj.comment), leftHanded: obj.leftHanded === true,
        // 这份手序认得的谱面指纹（C# CustomFingering.Charts）：原样带回去，Mod 保存时会加上新版的
        charts: Array.isArray(obj.charts) ? obj.charts.filter(c => typeof c === 'string' && c.length <= 100) : [],
      },
      hits,
      notes,
    };
  }

  function emptyExtras() {
    return { keyLabels: ['D', 'F', 'J', 'K'], refs: { standard: new Map(), alternate: new Map() }, beats: [], tempo: [], path: [], fx: null,
      audio: null, fileName: '', saveFolder: '', usedFile: '', language: '', roundNotes: false, sessionId: '', hitSound: '', fromGame: false };
  }

  function finish(fingering, extras) {
    const doc = Object.assign(emptyExtras(), extras, fingering);
    doc.notes.forEach(n => { n.orig = n.lanes.map(l => l.slice()); if (n.auto === undefined) n.auto = false; });
    if (!doc.fileName) doc.fileName = suggestedFileName(doc.meta);
    doc.signature = signature(doc.hits);
    return doc;
  }

  /** 读游戏给的会话（session.js 里的 window.RHYTHMHINT_SESSION）。 */
  function fromSession(s) {
    need(s && s.format === SESSION_FORMAT, 'notSession');
    const fingering = parseFingering(s.fingering);
    const auto = Array.isArray(s.fromAuto) ? s.fromAuto : [];
    fingering.notes.forEach((n, i) => { n.auto = auto[i] === 1 || auto[i] === true; });
    const refs = { standard: refMap(s.references && s.references.standard), alternate: refMap(s.references && s.references.alternate) };
    return finish(fingering, {
      keyLabels: Array.isArray(s.keyLabels) && s.keyLabels.length === fingering.keys ? s.keyLabels.map(String) : defaultLabels(fingering.keys),
      refs,
      beats: Array.isArray(s.beats) ? s.beats.filter(b => Array.isArray(b) && finite(b[0])).map(b => ({ time: b[0], measure: b[1] === 1 })) : [],
      tempo: Array.isArray(s.tempo) ? s.tempo.filter(b => Array.isArray(b) && finite(b[0]) && finite(b[1]) && b[1] > 0).map(b => ({ time: b[0], bpm: b[1] })) : [],
      path: Array.isArray(s.path) ? s.path.filter(p => Array.isArray(p) && finite(p[0]) && finite(p[1]) && finite(p[2])).map((p, i) => ({ x: p[0], y: p[1], time: p[2], hit: p[3] === 1, tile: int(p[4]) ? p[4] : i,
        entry: finite(p[5]) ? p[5] : null, exit: finite(p[6]) ? p[6] : null })) : [], // 进出方向：数学角、弧度，游戏里读的或由离线读谱的原始方向算出
      audio: s.audio && typeof s.audio.file === 'string' ? {
        file: s.audio.file,
        // 同一首歌另存的脚本（整首解码后播，跳转才准）：只认 recent/ 下的 .js，别的地方的脚本不载入
        script: typeof s.audio.script === 'string' && /^recent\/[^\\/]+\.js$/.test(s.audio.script) ? s.audio.script : '',
        offset: finite(s.audio.offset) ? s.audio.offset : 0,
        pitch: finite(s.audio.pitch) && s.audio.pitch > 0 ? s.audio.pitch : 1,
        calibration: finite(s.audio.calibration) ? s.audio.calibration : 0, // 游戏的画面校准（秒）：编辑器照样补
        peaksPerSecond: int(s.audio.peaksPerSecond) ? s.audio.peaksPerSecond : 0, peaks: decodePeaks(s.audio.peaks),
      } : null,
      fileName: typeof s.fileName === 'string' ? s.fileName : '',
      saveFolder: typeof s.saveFolder === 'string' ? s.saveFolder : '',
      usedFile: typeof s.usedFile === 'string' ? s.usedFile : '',
      language: typeof s.language === 'string' ? s.language : '',
      modVersion: typeof s.modVersion === 'string' ? s.modVersion : '', // 导出这份会话的 Mod 版本（关于里显示）
      roundNotes: s.roundNotes === true,
      fx: s.fx && typeof s.fx === 'object' && !Array.isArray(s.fx) ? s.fx : null, // 轨道和镜头事件（trackfx.js 重放）
      sessionId: typeof s.id === 'string' ? s.id : '', // 这一关在「历史」里的 Id（谱面指纹），草稿按它存
      hitSound: typeof s.hitSound === 'string' ? s.hitSound : '',
      fromGame: true,
    });
  }

  /** 读别人分享的手序文件（JSON 文本）。 */
  function fromFileText(text) {
    let obj;
    try { obj = JSON.parse(text); } catch (e) { throw new FingeringError('notJson', e.message); }
    return finish(parseFingering(obj), { keyLabels: defaultLabels(obj.keys || 4) });
  }

  function defaultLabels(keys) {
    return keys === 4 ? ['D', 'F', 'J', 'K'] : Array.from({ length: keys }, (_, i) => String(i + 1));
  }

  /** 参考排法：按「第一块砖」查；只有分组和当前音符一样（同一组砖）时才能对照、套用。 */
  function refMap(list) {
    const map = new Map();
    if (!Array.isArray(list)) return map;
    list.forEach(n => {
      if (Array.isArray(n) && n.length === 3 && int(n[0]) && int(n[1]) && Array.isArray(n[2]))
        map.set(n[0], { lastTile: n[1], lanes: sortLanes(n[2].filter(l => Array.isArray(l) && int(l[0])).map(l => [l[0], finite(l[1]) ? l[1] : 0])) });
    });
    return map;
  }

  function refLanes(doc, note, which) {
    const r = doc.refs[which] && doc.refs[which].get(note.tile);
    return r && r.lastTile === note.lastTile && r.lanes.length === note.lanes.length ? r.lanes : null;
  }

  function decodePeaks(b64) {
    if (typeof b64 !== 'string' || !b64) return new Uint8Array(0);
    if (typeof atob === 'function') {
      const s = atob(b64); const out = new Uint8Array(s.length);
      for (let i = 0; i < s.length; i++) out[i] = s.charCodeAt(i);
      return out;
    }
    return new Uint8Array(Buffer.from(b64, 'base64'));
  }

  function plain(text) { return String(text || '').replace(/<[^>]*>/g, '').trim(); }

  function suggestedFileName(meta) {
    const song = plain(meta.song), artist = plain(meta.artist);
    let name = artist && song ? artist + ' - ' + song : song || 'fingering';
    name = name.replace(/[\\/:*?"<>|\u0000-\u001f]/g, '_').trim().replace(/\.+$/, '');
    return (name.length > 80 ? name.slice(0, 80) : name) + '.json';
  }

  /** 这一关砖块的指纹（草稿按它存）。 */
  function signature(hits) {
    let h = 2166136261 >>> 0;
    const mix = v => { h ^= v; h = Math.imul(h, 16777619) >>> 0; };
    hits.forEach(x => { mix(x.tile); mix(Math.round(x.time * 1000)); mix(x.hold > 0 ? 1 : 0); });
    return hits.length + '-' + h.toString(36);
  }

  const round6 = v => Math.round(v * 1e6) / 1e6;

  /** 存成手序文件（和 C# 写的格式一样，一项一行，方便看改动）。 */
  function toFileText(doc, now) {
    const head = {
      format: FILE_FORMAT, schemaVersion: SCHEMA_VERSION, keys: doc.keys,
      song: doc.meta.song, artist: doc.meta.artist, charter: doc.meta.charter,
      author: doc.meta.author, comment: doc.meta.comment, leftHanded: doc.meta.leftHanded,
      charts: doc.meta.charts || [],
      updatedAt: (now || new Date()).toISOString(),
    };
    const hits = doc.hits.map(h => JSON.stringify([h.tile, round6(h.time), round6(h.hold), h.angle === null ? null : Math.round(h.angle * 1000) / 1000]));
    const notes = doc.notes.map(n => JSON.stringify([n.tile, n.lastTile, sortLanes(n.lanes).map(l => [l[0], round6(l[1])])]));
    const json = JSON.stringify(head);
    return json.slice(0, -1) + ',"hits":[\n' + hits.join(',\n') + '],"notes":[\n' + notes.join(',\n') + ']}';
  }

  // ---------- 编辑 ----------

  /** 把一个音符改成按 targets 这几个轨道（个数不变）；长按按轨道顺序跟过去。不合法返回 null。 */
  function withLanes(doc, note, targets) {
    if (!Array.isArray(targets) || targets.length !== note.lanes.length) return null;
    const sorted = targets.slice().sort((a, b) => a - b);
    for (let i = 0; i < sorted.length; i++)
      if (!int(sorted[i]) || sorted[i] < 0 || sorted[i] >= doc.keys || (i > 0 && sorted[i] === sorted[i - 1])) return null;
    const holds = sortLanes(note.lanes).map(l => l[1]);
    return sorted.map((lane, i) => [lane, holds[i]]);
  }

  /** 一次编辑：{ 音符 id: 新的 lanes }。任何一个不合法就整次不做，返回 null。 */
  function plan(doc, notes, fn) {
    const change = new Map();
    for (const note of notes) {
      const lanes = fn(note);
      if (lanes === undefined) continue;
      if (lanes === null) return null;
      if (laneKey(lanes) !== laneKey(note.lanes)) change.set(note.id, lanes);
    }
    return change;
  }

  const ops = {
    /** 单押改到某条轨道。 */
    toLane: (doc, notes, lane) => plan(doc, notes, n => n.lanes.length === 1 ? withLanes(doc, n, [lane]) : undefined),
    /** 整体左右挪 delta 条轨道。 */
    shift: (doc, notes, delta) => plan(doc, notes, n => withLanes(doc, n, n.lanes.map(l => l[0] + delta))),
    /** 左右镜像。 */
    mirror: (doc, notes) => plan(doc, notes, n => withLanes(doc, n, n.lanes.map(l => doc.keys - 1 - l[0]))),
    /** 多押里单独挪一个键。 */
    moveKey: (doc, note, from, to) => plan(doc, [note], n => {
      if (!n.lanes.some(l => l[0] === from)) return null;
      if (from === to) return undefined;
      if (n.lanes.some(l => l[0] === to)) return null;
      return withLanes(doc, n, n.lanes.map(l => l[0] === from ? to : l[0]));
    }),
    /** 直接指定轨道（检查器里点选）。 */
    set: (doc, note, targets) => plan(doc, [note], n => withLanes(doc, n, targets)),
    /** 套用参考排法（分组不一样的音符跳过）。 */
    reference: (doc, notes, which) => plan(doc, notes, n => {
      const r = refLanes(doc, n, which);
      return r ? withLanes(doc, n, r.map(l => l[0])) : undefined;
    }),
    /** 恢复成打开时的样子。 */
    revert: (doc, notes) => plan(doc, notes, n => n.orig.map(l => l.slice())),
  };

  /** 撤销、重做：每一步记下改了哪些音符、改之前和改之后。 */
  class History {
    constructor(doc) { this.doc = doc; this.undoStack = []; this.redoStack = []; this.version = 0; this.savedVersion = 0; }
    apply(change) {
      if (!change || change.size === 0) return false;
      const step = [];
      change.forEach((lanes, id) => {
        const note = this.doc.notes[id];
        step.push([id, note.lanes, lanes]);
        note.lanes = lanes;
      });
      this.undoStack.push(step);
      this.redoStack = [];
      this.version++;
      return true;
    }
    undo() { return this.move(this.undoStack, this.redoStack, 1, -1); }
    redo() { return this.move(this.redoStack, this.undoStack, 2, 1); }
    move(from, to, pick, dv) {
      const step = from.pop();
      if (!step) return null;
      step.forEach(([id, before, after]) => { this.doc.notes[id].lanes = pick === 1 ? before : after; });
      to.push(step);
      this.version += dv;
      return step.map(s => s[0]);
    }
    get canUndo() { return this.undoStack.length > 0; }
    get canRedo() { return this.redoStack.length > 0; }
    get dirty() { return this.version !== this.savedVersion; }
    markSaved() { this.savedVersion = this.version; }
  }

  // ---------- 参考信息 ----------

  /** 同一条轨道在 window 秒内连按两下的音符（两边都标出来）。 */
  function jacks(doc, window) {
    const last = new Array(doc.keys).fill(null);
    const out = new Set();
    for (const n of doc.notes) {
      for (const [lane] of n.lanes) {
        const prev = last[lane];
        if (prev && n.time - prev.time < window - 1e-6) { out.add(n.id); out.add(prev.id); }
        last[lane] = n;
      }
    }
    return out;
  }

  const FRACTIONS = [[1, 1], [1, 2], [1, 3], [1, 4], [1, 6], [1, 8], [1, 12], [1, 16], [1, 24], [1, 32], [2, 1], [3, 2], [3, 4], [3, 8], [2, 3]];

  /** 某个时间的一拍多少秒。 */
  function secondsPerBeat(doc, time) {
    let bpm = doc.tempo.length ? doc.tempo[0].bpm : 0;
    for (const t of doc.tempo) { if (t.time <= time + 1e-6) bpm = t.bpm; else break; }
    return bpm > 0 ? 60 / bpm : 0;
  }

  /** 两下之间的间隔是几分音符（按一拍是四分音符算）：1/4 拍 = 1/16。认不出返回空串。 */
  function noteValue(doc, time, gap) {
    const spb = secondsPerBeat(doc, time);
    if (!(spb > 0) || !(gap > 0)) return '';
    const beats = gap / spb;
    for (const [a, b] of FRACTIONS) {
      if (Math.abs(beats - a / b) <= 0.03 * (a / b)) {
        // 一拍 = 四分音符：a/b 拍 = a/(4b) 个全音符，能约成 1/n 就写 1/n（八分音符 1/8），否则写成分数（附点四分 3/8）
        const value = 4 * b / a;
        return Number.isInteger(value) ? '1/' + value : a + '/' + (4 * b);
      }
    }
    return '';
  }

  function stats(doc, jackSet) {
    let changed = 0, auto = 0;
    doc.notes.forEach(n => { if (laneKey(n.lanes) !== laneKey(n.orig)) changed++; if (n.auto) auto++; });
    return { notes: doc.notes.length, changed, auto, jacks: jackSet ? jackSet.size : 0 };
  }

  const isChanged = n => laneKey(n.lanes) !== laneKey(n.orig);
  const handOf = (doc, lane) => lane < doc.keys / 2 ? 0 : 1;

  /**
   * 草稿：这一关在编辑器里现在的样子（每个音符按哪几个键），改到一半关掉网页、下次从「历史」打开时接着改。
   * 存全部音符而不只是改过的：游戏重新导出这一关时原来的排法可能变了（比如换了排法、用上了保存的文件），照样能还原。
   * saved：和保存到游戏的文件一样；changed：和打开时相比改了几处；time：最后改动的时间。
   */
  function draftOf(doc, saved, time) {
    return {
      signature: doc.signature, lanes: doc.notes.map(n => n.lanes.map(l => l[0])), author: doc.meta.author, comment: doc.meta.comment,
      saved: saved === true, changed: stats(doc).changed, time: finite(time) ? time : Date.now(),
    };
  }

  /** 草稿和现在这一份不一样的音符（可撤销地套上去）；不是这一关的草稿为 null。旧版草稿是 { 音符号: 键 } 只记改过的，照样认。 */
  function draftChange(doc, draft) {
    if (!draft || draft.signature !== doc.signature || !draft.lanes || typeof draft.lanes !== 'object') return null;
    if (Array.isArray(draft.lanes) && draft.lanes.length !== doc.notes.length) return null;
    const change = new Map();
    for (const [id, targets] of Object.entries(draft.lanes)) {
      const note = doc.notes[Number(id)];
      if (!note || !Array.isArray(targets)) return null;
      const lanes = withLanes(doc, note, targets);
      if (!lanes) return null;
      if (laneKey(lanes) !== laneKey(note.lanes)) change.set(note.id, lanes);
    }
    return change;
  }

  const RECENT_FORMAT = 'rhythmhint-editor-recent';
  const SAFE_ID = /^[0-9]+-[0-9a-f]+$/;

  /**
   * 「历史」里的各关（recent/index.js 里的 window.RHYTHMHINT_RECENT，Mod 的 SessionLibrary 写）：新的在前。
   * 只认 Mod 写的样子，文件名都由 Id 拼出来，不会指到 recent 目录外面。
   */
  function parseRecent(obj) {
    if (!obj || obj.format !== RECENT_FORMAT || !Array.isArray(obj.items)) return [];
    const text = v => (typeof v === 'string' ? v : '');
    return obj.items
      .filter(e => e && typeof e.id === 'string' && SAFE_ID.test(e.id) && e.session === e.id + '.js'
        && (!e.audio || (typeof e.audio === 'string' && e.audio.startsWith(e.id + '-') && !/[\\/]/.test(e.audio))))
      .map(e => ({
        id: e.id, song: text(e.song), artist: text(e.artist), charter: text(e.charter), notes: int(e.notes) ? e.notes : 0,
        session: e.session, audio: text(e.audio), fileName: text(e.fileName), exported: finite(e.exported) ? e.exported : 0,
      }))
      .sort((a, b) => b.exported - a.exported);
  }

  return {
    FILE_FORMAT, SESSION_FORMAT, FingeringError, parseFingering, fromSession, fromFileText, toFileText, parseRecent,
    suggestedFileName, signature, ops, History, jacks, noteValue, secondsPerBeat, stats, refLanes, laneKey,
    isChanged, handOf, draftOf, draftChange, plain,
  };
});
