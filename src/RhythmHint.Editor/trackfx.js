/* 轨道预览的动画：按关卡事件算出任一时刻每块砖的位移、旋转、缩放、透明度，以及砖块的出现 / 消失动画。
 * 范围（2026-11-18 用户定）：MoveTrack、PositionTrack、AnimateTrack；不含颜色和轨道样式；
 * 镜头不用谱面自带的（MoveCamera，用户：镜头不要用谱面自带的），只跟着星球（见 playerAt）。
 * 重复事件（RepeatEvents）照游戏重放带标签的移动轨道，条件事件（SetConditionalEvents）按全程完美处理（见 expand）。
 * 没看游戏的实现（用户选了不用 REA），按事件参数的含义、编辑器社区文档和开源的重写实现做（依据见 docs/专属手序.md「轨道预览」）：
 * 缓动用 DOTween 同名缓动的公式和默认参数；位移以砖长（1.5）为单位；出现 / 消失动画的具体形态和时长是近似。
 * 这里只算数，不画（画在 track.js），node 测试直接测。 */
(function (root) {
  'use strict';

  const TILE = 1.5; // 一块砖长（游戏世界坐标）
  const PI = Math.PI;
  const num = v => (typeof v === 'number' && Number.isFinite(v) ? v : null);

  // ---------- 缓动（DOTween 的名字和公式：多数同 Robert Penner，Elastic 用 DOTween 自己的写法） ----------
  const inPow = n => u => Math.pow(u, n);
  const outPow = n => u => 1 - Math.pow(1 - u, n);
  const inOutPow = n => u => (u < 0.5 ? Math.pow(2, n - 1) * Math.pow(u, n) : 1 - Math.pow(-2 * u + 2, n) / 2);
  const BACK = 1.70158, BACK2 = BACK * 1.525;
  // Elastic：DOTween 默认振幅 1.70158（Back 的过冲量同一个数）、周期是时长的 0.3（InOut 0.45），相位按振幅错开
  // （DOTween 的 EaseManager；Penner 原式振幅 1，回弹小一截）
  const AMP = 1.70158, TAU = 2 * PI;
  const shift = period => period / TAU * Math.asin(1 / AMP);
  const E1 = 0.3, S1 = shift(E1), E2 = 0.45, S2 = shift(E2);
  function inOutElastic(u) {
    const v = u * 2 - 1;
    return v < 0 ? -0.5 * AMP * Math.pow(2, 10 * v) * Math.sin((v - S2) * TAU / E2)
      : AMP * Math.pow(2, -10 * v) * Math.sin((v - S2) * TAU / E2) * 0.5 + 1;
  }
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
    InElastic: u => (u === 0 ? 0 : u === 1 ? 1 : -AMP * Math.pow(2, 10 * (u - 1)) * Math.sin((u - 1 - S1) * TAU / E1)),
    OutElastic: u => (u === 0 ? 0 : u === 1 ? 1 : AMP * Math.pow(2, -10 * u) * Math.sin((u - S1) * TAU / E1) + 1),
    InOutElastic: u => (u === 0 ? 0 : u === 1 ? 1 : inOutElastic(u)),
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
  const SHOWN = Object.freeze({ alpha: 1, scale: 1, dx: 0, dy: 0, rot: 0 }); // 不在出现 / 消失动画里的砖：原样
  const MAX_BEAT_REPEATS = 10000; // 按拍重复最多展开几次：防谱面写了离谱的次数把预览卡住（按砖重复到最后一块为止，本来就有上限）

  /** eventTag、tag：空格分开的几个标签。 */
  const tagsOf = v => (typeof v === 'string' ? v.split(' ').filter(Boolean) : []);

  /**
   * 重复事件、条件事件按砖号、标签建表（游戏里两者都只管同一块砖上带这个标签的事件）。
   * repeat：砖号 → 标签 → { beat, interval, count, gap, here }；同一块砖同一个标签后写的算数。
   *   按拍（repeatType 不是 Floor，老谱面没写）：在本砖每隔 interval 拍再触发 repetitions 次；
   *   按砖（Floor）：往后每隔 gapLength 块砖、到那块砖时再触发一次，共 floorCount 次；executeOnCurrentFloor 时 ThisTile 跟着那块砖。
   *   interval 不大于 0 的按拍重复游戏里也按砖走（开源重写 Re_ADOJAS 照游戏写的判断，这里照搬）。缺的参数用游戏编辑器的默认值。
   * judged：砖号 → 标签 → 完美时会不会触发（条件事件里写了 perfectTag 的为 true，只写在失误、失败等别的判定里的为 false）。
   */
  function modifierTables(actions) {
    const repeat = new Map(), judged = new Map();
    const table = (map, floor) => map.get(floor) || map.set(floor, new Map()).get(floor);
    for (const a of actions) {
      if (a.eventType === 'RepeatEvents') {
        const byFloor = a.repeatType === 'Floor', interval = byFloor ? -1 : (num(a.interval) ?? 1);
        const info = {
          beat: interval > 0, interval,
          count: Math.max(0, Math.round((byFloor ? num(a.floorCount) : num(a.repetitions)) ?? 1)),
          gap: Math.max(1, Math.round(num(a.gapLength) ?? 1)), here: a.executeOnCurrentFloor === true,
        };
        for (const tag of tagsOf(a.tag)) table(repeat, a.floor).set(tag, info);
      } else if (a.eventType === 'SetConditionalEvents') {
        for (const key of Object.keys(a)) {
          if (key === 'eventTag' || !key.endsWith('Tag')) continue;
          const t = table(judged, a.floor);
          for (const tag of tagsOf(a[key])) if (tag !== 'NONE') t.set(tag, t.get(tag) === true || key === 'perfectTag');
        }
      }
    }
    return { repeat, judged };
  }

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
      const settings = (fx && fx.settings) || {};
      this.indexOf = new Map(path.map((p, i) => [p.tile, i]));
      const actions = this.expand(((fx && Array.isArray(fx.actions)) ? fx.actions : [])
        .filter(a => a && typeof a === 'object' && this.indexOf.has(a.floor)));
      // 缓动按属性分开存，受同一串事件影响、底值也相同的砖合成一组、共用一份缓动列表（整轨事件多的谱不用每块砖各存一份）：
      // 属性名 → { of: 砖下标 → 组号, groups: [ { base, size, list: [ { t0, t1, from, to, ease } … ] } ] }，list 按开始时间排好
      this.channels = {};
      this.base = new Map(); // 砖下标 → PositionTrack 定下的旋转、缩放、透明度
      this.buildBase(actions.filter(e => e.a.eventType === 'PositionTrack'));
      this.buildMoves(actions.filter(e => e.a.eventType === 'MoveTrack').sort((x, y) => x.t0 - y.t0));
      this.buildAnimations(settings, actions.filter(e => e.a.eventType === 'AnimateTrack').sort((x, y) => x.index - y.index));
      this.buildWindows();
      this.stamp = 0; // 画一帧加一：同组的值一帧只算一次（见 tilesNear）
    }

    /**
     * 关卡事件 → 每一次触发 { a, index（ThisTile 按哪块砖算）, spb（换算拍数用的一拍几秒）, t0 }。
     * 带标签的移动轨道按同一块砖上的重复事件展开成多次（第 0 次就是它自己）；出现消失动画、PositionTrack 不重复（游戏里也只用一次）。
     * 条件事件管的事件只在判定时触发：预览不判定、按全程完美算，完美时触发的在本砖照常触发一次（近似：游戏里是落到砖上判完美时），
     * 只在失误、失败、过早等判定时触发的不触发。
     */
    expand(actions) {
      const { repeat, judged } = modifierTables(actions);
      const out = [];
      for (const a of actions) {
        if (a.eventType === 'RepeatEvents' || a.eventType === 'SetConditionalEvents') continue;
        const tags = tagsOf(a.eventTag), base = this.indexOf.get(a.floor), cond = judged.get(a.floor);
        if (cond && tags.some(t => cond.has(t)) && !tags.some(t => cond.get(t))) continue;
        const reps = a.eventType === 'MoveTrack' && repeat.get(a.floor);
        const info = reps ? tags.map(t => reps.get(t)).find(Boolean) : null; // 几个标签取第一个对得上的
        if (info) this.repeatInto(out, a, base, info);
        else out.push(this.fire(a, base, 0));
      }
      return out;
    }

    /** 事件在第 index 块砖上触发，再晚 beats 拍：拍数按那块砖的速度换算成秒。 */
    fire(a, index, beats) {
      const time = this.path[index].time, spb = this.spbAt(time);
      return { a, index, spb, t0: time + ((num(a.angleOffset) || 0) / 180 + beats) * spb };
    }

    /**
     * 一个被重复的移动轨道。按砖重复、ThisTile 不跟着走的，在本砖上推迟到星球到那块砖的拍数：
     * 每块砖按自己的速度折成拍数（游戏按砖块转过的角度算，和变速无关），再按本砖的速度换算成秒。
     */
    repeatInto(out, a, base, r) {
      const floor = this.path[base].tile;
      for (let k = 0; k <= r.count; k++) {
        if (r.beat) {
          if (k > MAX_BEAT_REPEATS) break;
          out.push(this.fire(a, base, r.interval * k));
          continue;
        }
        const target = this.indexOf.get(floor + k * r.gap);
        if (target === undefined) break; // 过了最后一块砖
        out.push(r.here ? this.fire(a, target, 0) : this.fire(a, base, this.beatsTo(target) - this.beatsTo(base)));
      }
    }

    /** 从第 0 块砖到第 i 块砖经过了几拍（每块砖按它自己的速度折算）。 */
    beatsTo(i) {
      if (!this.beatSum) {
        const sum = this.beatSum = new Float64Array(this.path.length), path = this.path;
        for (let j = 1; j < path.length; j++) sum[j] = sum[j - 1] + (path[j].time - path[j - 1].time) / this.spbAt(path[j - 1].time);
      }
      return this.beatSum[i];
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
     * 每块砖出现、消失的时刻（画一帧时先用它粗筛）：showFrom 之前、showUntil 之后完全看不见，
     * appearEnd 之前在出现、fadeFrom 之后在消失，中间原样。没有动画的那一头是 ±无穷。
     * 出现：星球到这块砖前 ahead 拍开始，消失：星球到过这块砖 behind 拍后开始；动画时长取半拍（0.12–0.6 秒）。
     */
    buildWindows() {
      const n = this.path.length;
      this.showFrom = new Float64Array(n).fill(-Infinity);
      this.appearEnd = new Float64Array(n).fill(-Infinity);
      this.fadeFrom = new Float64Array(n).fill(Infinity);
      this.showUntil = new Float64Array(n).fill(Infinity);
      for (let i = 0; i < n; i++) {
        const a = this.anim[i], time = this.path[i].time, spb = this.spbAt(time), dur = Math.max(0.12, Math.min(0.6, spb / 2));
        if (a.appear !== 'None') { this.showFrom[i] = time - a.ahead * spb; this.appearEnd[i] = this.showFrom[i] + dur; }
        if (a.disappear !== 'None') { this.fadeFrom[i] = time + a.behind * spb; this.showUntil[i] = this.fadeFrom[i] + dur; }
      }
    }

    /** 出现 / 消失动画在 t 时刻对这块砖的影响：{ alpha, scale, dx, dy, rot }。 */
    reveal(i, t) {
      const out = { alpha: 1, scale: 1, dx: 0, dy: 0, rot: 0 };
      if (!this.anim[i]) return out;
      if (t <= this.showFrom[i] || t >= this.showUntil[i]) { out.alpha = 0; return out; }
      const a = this.anim[i], from = this.showFrom[i], fade = this.fadeFrom[i];
      if (t < this.appearEnd[i]) this.shape(a.appear, ease('OutCubic', (t - from) / (this.appearEnd[i] - from)), i, out, true);
      if (t > fade) this.shape(a.disappear, ease('InCubic', (t - fade) / (this.showUntil[i] - fade)), i, out, false);
      return out;
    }

    /**
     * t 时刻可能在画面里的砖，按砖号排好：[{ i, st（state）, rv（reveal） }]。
     * box：画面的世界坐标范围 { x0, x1, y0, y1 }，外加边距 margin（再按砖的缩放放大）；砖的中心（含移动和出现消失的位移）在里面才算。
     * 先按出现消失的时刻、每组共用的位移和缩放粗筛，只给留下的砖算完整状态（2 万块砖的谱原来每帧逐块算，13 ms）。
     */
    tilesNear(t, box) {
      const out = [], path = this.path, stamp = ++this.stamp;
      const [dx, dy, sx, sy] = ['dx', 'dy', 'sx', 'sy'].map(key => this.memo(key));
      const at = (m, i) => {
        const g = m.of[i];
        if (m.stamp[g] !== stamp) { m.stamp[g] = stamp; m.value[g] = sample(m.groups[g], t); }
        return m.value[g];
      };
      for (let i = 0; i <= this.last; i++) {
        if (t <= this.showFrom[i] || t >= this.showUntil[i]) continue;
        const rv = t < this.appearEnd[i] || t > this.fadeFrom[i] ? this.reveal(i, t) : SHOWN;
        const x = path[i].x + at(dx, i) + rv.dx, y = path[i].y + at(dy, i) + rv.dy;
        const m = box.margin * Math.max(1, Math.abs(at(sx, i) * rv.scale), Math.abs(at(sy, i) * rv.scale));
        if (x < box.x0 - m || x > box.x1 + m || y < box.y0 - m || y > box.y1 + m) continue;
        out.push({ i, st: this.state(i, t), rv });
      }
      return out;
    }

    /** 一个属性每组这一帧的值（tilesNear 用）：组在建好以后不再变，按组号存。 */
    memo(key) {
      const ch = this.channels[key];
      if (!ch.memo) ch.memo = { of: ch.of, groups: ch.groups, value: new Float64Array(ch.groups.length), stamp: new Int32Array(ch.groups.length) };
      return ch.memo;
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
