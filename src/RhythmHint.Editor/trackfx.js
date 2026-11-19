/* 轨道预览的动画：按关卡事件算出任一时刻每块砖的位移、旋转、缩放、透明度，以及砖块的出现 / 消失动画。
 * 范围（2026-11-18 用户定）：MoveTrack、PositionTrack、AnimateTrack；不含颜色和轨道样式；
 * 镜头不用谱面自带的（MoveCamera，用户：镜头不要用谱面自带的），只跟着星球（见 playerAt）。
 * 没看游戏的实现（用户选了不用 REA），按事件参数的含义做：缓动用 DOTween 同名缓动的标准公式；位移以砖长（1.5）为单位；
 * 出现 / 消失动画的具体形态和时长是近似。这里只算数，不画（画在 track.js），node 测试直接测。 */
(function (root) {
  'use strict';

  const TILE = 1.5; // 一块砖长（游戏世界坐标）
  const PI = Math.PI;
  const num = v => (typeof v === 'number' && Number.isFinite(v) ? v : null);

  // ---------- 缓动（DOTween 的名字，公式同 Robert Penner） ----------
  const inPow = n => u => Math.pow(u, n);
  const outPow = n => u => 1 - Math.pow(1 - u, n);
  const inOutPow = n => u => (u < 0.5 ? Math.pow(2, n - 1) * Math.pow(u, n) : 1 - Math.pow(-2 * u + 2, n) / 2);
  const BACK = 1.70158, BACK2 = BACK * 1.525;
  function outBounce(u) {
    const n = 7.5625, d = 2.75;
    if (u < 1 / d) return n * u * u;
    if (u < 2 / d) return n * (u -= 1.5 / d) * u + 0.75;
    if (u < 2.5 / d) return n * (u -= 2.25 / d) * u + 0.9375;
    return n * (u -= 2.625 / d) * u + 0.984375;
  }
  const EASES = {
    Linear: u => u,
    InSine: u => 1 - Math.cos(u * PI / 2),
    OutSine: u => Math.sin(u * PI / 2),
    InOutSine: u => -(Math.cos(PI * u) - 1) / 2,
    InQuad: inPow(2), OutQuad: outPow(2), InOutQuad: inOutPow(2),
    InCubic: inPow(3), OutCubic: outPow(3), InOutCubic: inOutPow(3),
    InQuart: inPow(4), OutQuart: outPow(4), InOutQuart: inOutPow(4),
    InQuint: inPow(5), OutQuint: outPow(5), InOutQuint: inOutPow(5),
    InExpo: u => (u === 0 ? 0 : Math.pow(2, 10 * u - 10)),
    OutExpo: u => (u === 1 ? 1 : 1 - Math.pow(2, -10 * u)),
    InOutExpo: u => (u === 0 ? 0 : u === 1 ? 1 : u < 0.5 ? Math.pow(2, 20 * u - 10) / 2 : (2 - Math.pow(2, -20 * u + 10)) / 2),
    InCirc: u => 1 - Math.sqrt(1 - u * u),
    OutCirc: u => Math.sqrt(1 - Math.pow(u - 1, 2)),
    InOutCirc: u => (u < 0.5 ? (1 - Math.sqrt(1 - Math.pow(2 * u, 2))) / 2 : (Math.sqrt(1 - Math.pow(-2 * u + 2, 2)) + 1) / 2),
    InBack: u => (BACK + 1) * u * u * u - BACK * u * u,
    OutBack: u => 1 + (BACK + 1) * Math.pow(u - 1, 3) + BACK * Math.pow(u - 1, 2),
    InOutBack: u => (u < 0.5 ? (Math.pow(2 * u, 2) * ((BACK2 + 1) * 2 * u - BACK2)) / 2
      : (Math.pow(2 * u - 2, 2) * ((BACK2 + 1) * (u * 2 - 2) + BACK2) + 2) / 2),
    InElastic: u => (u === 0 ? 0 : u === 1 ? 1 : -Math.pow(2, 10 * u - 10) * Math.sin((u * 10 - 10.75) * (2 * PI / 3))),
    OutElastic: u => (u === 0 ? 0 : u === 1 ? 1 : Math.pow(2, -10 * u) * Math.sin((u * 10 - 0.75) * (2 * PI / 3)) + 1),
    InOutElastic: u => (u === 0 ? 0 : u === 1 ? 1 : u < 0.5
      ? -(Math.pow(2, 20 * u - 10) * Math.sin((20 * u - 11.125) * (2 * PI / 4.5))) / 2
      : (Math.pow(2, -20 * u + 10) * Math.sin((20 * u - 11.125) * (2 * PI / 4.5))) / 2 + 1),
    InBounce: u => 1 - outBounce(1 - u),
    OutBounce: outBounce,
    InOutBounce: u => (u < 0.5 ? (1 - outBounce(1 - 2 * u)) / 2 : (1 + outBounce(2 * u - 1)) / 2),
  };

  /** 缓动：u 是 0–1 的进度；不认识的名字（比如 Flash 系列）按线性。 */
  function ease(name, u) {
    const f = EASES[name] || EASES.Linear;
    return f(Math.max(0, Math.min(1, u)));
  }

  /** 事件里的相对砖号 [n, "ThisTile" | "Start" | "End"] 换成路径下标（限在 0..last）。 */
  function resolveTile(ref, floor, last) {
    if (!Array.isArray(ref)) return null;
    const n = num(ref[0]) || 0;
    const v = ref[1] === 'Start' ? n : ref[1] === 'End' ? last + n : floor + n;
    return Math.max(0, Math.min(last, Math.round(v)));
  }

  /** 每块砖一个稳定的「随机」方向（散开 / 聚拢动画用），同一块砖每次一样。 */
  function scatterAngle(i) {
    const x = Math.sin(i * 12.9898 + 78.233) * 43758.5453;
    return (x - Math.floor(x)) * 2 * PI;
  }

  const PROPS = ['dx', 'dy', 'rot', 'sx', 'sy', 'alpha'];
  const DEFAULT_BASE = Object.freeze({ dx: 0, dy: 0, rot: 0, sx: 1, sy: 1, alpha: 1 }); // 没有 PositionTrack 的砖合用这一份，不能改

  /** 一组砖某个属性在 t 时刻的值：二分找最后一段已开始的缓动，还没开始就是底值。 */
  function sample(group, t) {
    const list = group.list;
    if (!list.length || list[0].t0 > t) return group.base;
    let lo = 0, hi = list.length - 1;
    while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (list[mid].t0 <= t) lo = mid; else hi = mid - 1; }
    const w = list[lo];
    if (t >= w.t1) return w.to;
    return w.from + (w.to - w.from) * ease(w.ease, (t - w.t0) / (w.t1 - w.t0));
  }

  class TrackFx {
    /**
     * @param path 砖块（{ x, y, time, tile }，按顺序），位置已含 PositionTrack 的挪动（游戏里读的就是挪过的）
     * @param fx session.fx：{ settings, actions }；没有就是静止的轨道
     * @param spbAt 某一时刻一拍几秒（fx.beat 没有时用）
     */
    constructor(path, fx, spbAt) {
      this.path = path;
      this.last = path.length - 1;
      // 事件的拍数按砖块当时的速度换算（fx.beat：[时间, 一拍几秒]，Mod 按砖块速度算好）；
      // 节拍线的 BPM 把 ×2、×0.5 当成只改疏密，用它换算会差很多（工坊 2802147583：8 拍的镜头事件只剩 0.02 秒）
      const beat = fx && Array.isArray(fx.beat) ? fx.beat.filter(b => Array.isArray(b) && num(b[0]) !== null && num(b[1]) > 0) : [];
      this.spbAt = beat.length ? t => {
        let lo = 0, hi = beat.length - 1;
        while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (beat[mid][0] <= t + 1e-6) lo = mid; else hi = mid - 1; }
        return beat[lo][1];
      } : spbAt;
      spbAt = this.spbAt;
      const settings = (fx && fx.settings) || {};
      const indexOf = new Map(path.map((p, i) => [p.tile, i]));
      const actions = ((fx && Array.isArray(fx.actions)) ? fx.actions : [])
        .filter(a => a && typeof a === 'object' && indexOf.has(a.floor))
        .map(a => {
          const index = indexOf.get(a.floor), spb = spbAt(path[index].time);
          return { a, index, spb, t0: path[index].time + (num(a.angleOffset) || 0) / 180 * spb };
        });
      // 缓动按属性分开存，受同一串事件影响、底值也相同的砖合成一组、共用一份缓动列表（整轨事件多的谱不用每块砖各存一份）：
      // 属性名 → { of: 砖下标 → 组号, groups: [ { base, size, list: [ { t0, t1, from, to, ease } … ] } ] }，list 按开始时间排好
      this.channels = {};
      this.base = new Map(); // 砖下标 → PositionTrack 定下的旋转、缩放、透明度
      this.buildBase(actions.filter(e => e.a.eventType === 'PositionTrack'));
      this.buildMoves(actions.filter(e => e.a.eventType === 'MoveTrack').sort((x, y) => x.t0 - y.t0));
      this.buildAnimations(settings, actions.filter(e => e.a.eventType === 'AnimateTrack').sort((x, y) => x.index - y.index));
    }

    /** PositionTrack：这块砖固定的旋转、缩放、透明度（位置游戏里已经挪好了）。 */
    buildBase(events) {
      for (const { a, index } of events) {
        const b = { ...this.baseOf(index) }; // 复制一份再改：默认值是共享的
        if (num(a.rotation) !== null) b.rot = a.rotation;
        const scale = a.scale;
        if (num(scale) !== null) b.sx = b.sy = scale / 100;
        else if (Array.isArray(scale)) { if (num(scale[0]) !== null) b.sx = scale[0] / 100; if (num(scale[1]) !== null) b.sy = scale[1] / 100; }
        if (num(a.opacity) !== null) b.alpha = a.opacity / 100;
        this.base.set(index, b);
      }
    }

    /** 这块砖的底值（PositionTrack 定的，没有就是共享的默认值）：只读，别改返回的对象。 */
    baseOf(i) {
      return this.base.get(i) || DEFAULT_BASE;
    }

    /** 一个属性的初始分组：按底值分，没有 PositionTrack（或底值和默认一样）的砖都在第 0 组。 */
    channel(key) {
      const of = new Int32Array(this.path.length);
      const groups = [{ base: DEFAULT_BASE[key], size: this.path.length, list: [] }];
      const byBase = new Map([[DEFAULT_BASE[key], 0]]);
      for (const [i, b] of this.base) {
        let g = byBase.get(b[key]);
        if (g === undefined) { byBase.set(b[key], g = groups.length); groups.push({ base: b[key], size: 0, list: [] }); }
        if (g) { of[i] = g; groups[g].size++; groups[0].size--; }
      }
      return { of, groups };
    }

    /** MoveTrack：每块受影响的砖、每个写了的属性，从触发那一刻的当前值缓到目标值。 */
    buildMoves(events) {
      for (const key of PROPS) this.channels[key] = this.channel(key);
      const hits = new Int32Array(this.path.length + 1); // cover 用的计数，用完清零；组数不会超过砖数 + 1
      for (const { a, index, spb, t0 } of events) {
        let s = resolveTile(a.startTile, index, this.last), e = resolveTile(a.endTile, index, this.last);
        if (s === null) s = index;
        if (e === null) e = s;
        if (s > e) [s, e] = [e, s];
        const step = Math.max(1, Math.round(num(a.gapLength) || 0) + 1);
        const t1 = t0 + Math.max(0, num(a.duration) || 0) * spb;
        const targets = [];
        if (Array.isArray(a.positionOffset)) {
          if (num(a.positionOffset[0]) !== null) targets.push(['dx', a.positionOffset[0] * TILE]);
          if (num(a.positionOffset[1]) !== null) targets.push(['dy', a.positionOffset[1] * TILE]);
        }
        const rotation = num(a.rotationOffset);
        if (num(a.scale) !== null) targets.push(['sx', a.scale / 100], ['sy', a.scale / 100]);
        else if (Array.isArray(a.scale)) {
          if (num(a.scale[0]) !== null) targets.push(['sx', a.scale[0] / 100]);
          if (num(a.scale[1]) !== null) targets.push(['sy', a.scale[1] / 100]);
        }
        if (num(a.opacity) !== null) targets.push(['alpha', a.opacity / 100]);
        // 每组从这一刻组里的当前值缓到目标值（同组的砖这一刻的值一样）
        const add = (key, toOf) => this.cover(this.channels[key], s, e, step, hits,
          g => g.list.push({ t0, t1, from: sample(g, t0), to: toOf(g), ease: a.ease }));
        for (const [key, to] of targets) add(key, () => to);
        if (rotation !== null) add('rot', g => g.base + rotation); // 旋转相对砖块原来的角度
      }
    }

    /**
     * s..e（隔 step 块）这些砖在一个属性上加一段缓动，add 对涉及的每组调一次。
     * 整组都被盖住的直接加；只盖住一部分的，把盖住的砖拆成新组（复制已有的缓动列表）再加，所以同组的砖缓动始终一样。
     */
    cover(ch, s, e, step, hits, add) {
      const { of, groups } = ch, touched = [];
      for (let i = s; i <= e; i += step) if (hits[of[i]]++ === 0) touched.push(of[i]);
      let split = false;
      for (const g of touched) {
        const whole = groups[g];
        if (hits[g] === whole.size) { add(whole); hits[g] = 0; continue; }
        const part = { base: whole.base, size: hits[g], list: whole.list.slice() };
        whole.size -= part.size;
        hits[g] = groups.push(part); // 记下新组号 + 1，下面把盖住的砖挪过去
        add(part);
        split = true;
      }
      if (!split) return;
      for (let i = s; i <= e; i += step) { const moved = hits[of[i]]; if (moved) of[i] = moved - 1; }
      for (const g of touched) hits[g] = 0;
    }

    /** 某块砖某个属性在 t 时刻的值。 */
    value(i, key, t) {
      const ch = this.channels[key], g = ch && ch.groups[ch.of[i]];
      return g ? sample(g, t) : this.baseOf(i)[key];
    }

    /** 某块砖在 t 时刻的移动状态：位移（世界坐标）、旋转（度，逆时针）、缩放、透明度。 */
    state(i, t) {
      const out = {};
      for (const key of PROPS) out[key] = this.value(i, key, t);
      return out;
    }

    /** AnimateTrack：每块砖用的出现 / 消失方式（这块砖之前最后一个事件定的，没有用关卡设置）。 */
    buildAnimations(settings, events) {
      let cur = {
        appear: settings.trackAnimation || 'None', ahead: num(settings.beatsAhead) ?? 3,
        disappear: settings.trackDisappearAnimation || 'None', behind: num(settings.beatsBehind) ?? 4,
      };
      this.anim = new Array(this.path.length);
      let k = 0;
      for (let i = 0; i <= this.last; i++) {
        while (k < events.length && events[k].index <= i) {
          const a = events[k++].a;
          cur = {
            appear: typeof a.trackAnimation === 'string' ? a.trackAnimation : cur.appear,
            ahead: num(a.beatsAhead) ?? cur.ahead,
            disappear: typeof a.trackDisappearAnimation === 'string' ? a.trackDisappearAnimation : cur.disappear,
            behind: num(a.beatsBehind) ?? cur.behind,
          };
        }
        this.anim[i] = cur;
      }
    }

    /**
     * 出现 / 消失动画在 t 时刻对这块砖的影响：{ alpha, scale, dx, dy, rot }。
     * 出现：星球到这块砖前 ahead 拍开始，消失：星球到过这块砖 behind 拍后开始；动画时长取半拍（0.12–0.6 秒）。
     */
    reveal(i, t) {
      const out = { alpha: 1, scale: 1, dx: 0, dy: 0, rot: 0 };
      const a = this.anim[i], p = this.path[i];
      if (!a) return out;
      const spb = this.spbAt(p.time), dur = Math.max(0.12, Math.min(0.6, spb / 2));
      if (a.appear !== 'None') {
        const u = (t - (p.time - a.ahead * spb)) / dur;
        if (u <= 0) { out.alpha = 0; return out; }
        if (u < 1) this.shape(a.appear, ease('OutCubic', u), i, out, true);
      }
      if (a.disappear !== 'None') {
        const u = (t - (p.time + a.behind * spb)) / dur;
        if (u >= 1) { out.alpha = 0; return out; }
        if (u > 0) this.shape(a.disappear, ease('InCubic', u), i, out, false);
      }
      return out;
    }

    /** 出现时 k 从 0 到 1（1 是完全出现），消失时 k 从 0 到 1（1 是完全消失）。 */
    shape(kind, k, i, out, appearing) {
      const gone = appearing ? 1 - k : k; // 离原样有多远
      const dir = scatterAngle(i);
      switch (kind) {
        case 'Fade': out.alpha *= 1 - gone; break;
        case 'Grow': case 'Shrink': case 'Extend': case 'Retract': out.scale *= 1 - gone; break;
        case 'Grow_Spin': case 'Shrink_Spin': out.scale *= 1 - gone; out.rot += gone * 360; break;
        case 'Assemble': case 'Scatter':
          out.dx += Math.cos(dir) * gone * 2 * TILE; out.dy += Math.sin(dir) * gone * 2 * TILE; out.alpha *= 1 - gone; break;
        case 'Assemble_Far': case 'Scatter_Far':
          out.dx += Math.cos(dir) * gone * 8 * TILE; out.dy += Math.sin(dir) * gone * 8 * TILE; out.alpha *= 1 - gone; break;
        case 'Drop': out.dy += gone * 3 * TILE; out.alpha *= 1 - gone; break;
        case 'Rise': out.dy -= gone * 3 * TILE; out.alpha *= 1 - gone; break;
        default: break;
      }
    }

    /** 某块砖 t 时刻的位置（含 MoveTrack 位移）。 */
    tileAt(i, t) {
      const p = this.path[i];
      return [p.x + this.value(i, 'dx', t), p.y + this.value(i, 'dy', t)];
    }

    /** 星球沿砖块走到哪（相邻两块砖之间按时间插值，砖块被移动时跟着走）：镜头对准它。 */
    playerAt(t) {
      const path = this.path;
      let lo = 0, hi = this.last;
      while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (path[mid].time <= t) lo = mid; else hi = mid - 1; }
      const i = Math.min(lo, Math.max(0, this.last - 1)), j = Math.min(i + 1, this.last);
      const a = this.tileAt(i, t), b = this.tileAt(j, t);
      const span = path[j].time - path[i].time;
      const f = span > 0 ? Math.max(0, Math.min(1, (t - path[i].time) / span)) : 1;
      return [a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f];
    }
  }

  const api = { TrackFx, ease, resolveTile, TILE };
  if (typeof module === 'object' && module.exports) module.exports = api;
  root.TrackFx = TrackFx;
  root.TrackFxEase = ease;
})(typeof globalThis !== 'undefined' ? globalThis : this);
