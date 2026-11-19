/* 轨道预览：像游戏里一样画出砖块和两颗星球，跟着播放走。
 * 砖块按关卡事件动（移动轨道、出现消失动画，算法在 trackfx.js），镜头只跟着星球（不用谱面自带的镜头）；每块砖按游戏里的进出方向单独画，
 * 尖角、平头、浅色砖面深色描边，挨着的砖之间有分隔；两颗星球轮流当中心，另一颗绕着转到下一块。 */
(function (root) {
  'use strict';

  const FIRE = '#ff4f3a', ICE = '#3ea8ff';
  const TILE = '#d9dbe1', EDGE = '#26282f', SEAM = '#5b5f6b';
  const FONT = '"Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI", system-ui, sans-serif';
  const MONO = '"Cascadia Mono", Consolas, ui-monospace, monospace';
  const TAU = Math.PI * 2;
  const GHOST = 0.16; // 被谱面藏起来的砖画多淡
  const CAMERA_LAG = 0.1; // 镜头平滑跟随的时间常数（秒），和原来每帧追 15% 的手感差不多

  function mix(hex, alpha) {
    const n = parseInt(hex.slice(1), 16);
    return 'rgba(' + (n >> 16 & 255) + ',' + (n >> 8 & 255) + ',' + (n & 255) + ',' + alpha + ')';
  }

  class TrackView {
    constructor(canvas) {
      this.canvas = canvas;
      this.g = canvas.getContext('2d');
      this.doc = null;
      this.angleByTile = new Map();
      this.camX = 0;
      this.camY = 0;
      this.scale = 42;
      this.zoom = 1; // 镜头离星球多近（1 = 和游戏默认一样），滚轮调；zoomTarget 是要去的值，画的时候慢慢靠过去
      this.zoomTarget = 1;
      this.trail = [];
      this.lastIndex = -1;
      this.landedAt = 0;
      this.lastDraw = 0;
      this.camFresh = true; // 换了谱面：第一帧镜头直接放到位
      this.lastTime = null; // 上一帧的谱面时间，用来算播放速度
      this.speedMarks = new Map(); // 第几块砖 → true 变快、false 变慢
    }

    setDoc(doc) {
      this.doc = doc;
      this.trail = [];
      this.lastIndex = -1;
      this.camFresh = true;
      this.lastTime = null;
      this.angleByTile.clear();
      if (!doc) return;
      // 砖块转角（度）：从上一块要按的砖转到这一块，判断星球往哪边转
      for (let i = 1; i < doc.hits.length; i++)
        if (doc.hits[i].angle !== null) this.angleByTile.set(doc.hits[i].tile, [doc.hits[i - 1].tile, doc.hits[i].angle]);
      if (doc.path.length > 1) {
        // 一块砖的长度：相邻砖间距的中位数（被移动轨道挪开的砖不影响）
        const steps = [];
        for (let i = 1; i < doc.path.length; i++) {
          const d = Math.hypot(doc.path[i].x - doc.path[i - 1].x, doc.path[i].y - doc.path[i - 1].y);
          if (d > 1e-6) steps.push(d);
        }
        steps.sort((a, b) => a - b);
        this.unit = steps.length ? steps[steps.length >> 1] : 1;
        this.camX = doc.path[0].x;
        this.camY = doc.path[0].y;
      }
      // 轨道的动画时间线（没有事件就是静止的轨道）
      const model = root.FingeringModel;
      const spbAt = t => (model && doc.tempo.length ? model.secondsPerBeat(doc, t) : 0.5) || 0.5;
      this.fx = root.TrackFx && doc.path.length > 1 ? new root.TrackFx(doc.path, doc.fx, spbAt) : null;
      // BPM 变化的地方：标在那个时刻所在的砖上，变快还是变慢
      this.speedMarks.clear();
      if (doc.path.length > 1) {
        for (let i = 1; i < doc.tempo.length; i++) {
          const before = doc.tempo[i - 1].bpm, after = doc.tempo[i].bpm;
          if (Math.abs(after - before) < 1e-6) continue;
          this.speedMarks.set(this.indexAt(doc.tempo[i].time + 1e-6), after > before);
        }
      }
    }

    /** 设镜头远近（夹在 0.35–4 倍），返回实际的值；now 为 true 时直接到位。 */
    setZoom(zoom, now) {
      const z = Math.max(0.35, Math.min(4, Number(zoom) || 1));
      this.zoomTarget = z;
      if (now) this.zoom = z;
      return z;
    }

    /** 按画布在页面上的大小和像素比定画布尺寸；没变就不重设（重设会清空画布、重新分配内存）。 */
    resize() {
      const r = this.canvas.getBoundingClientRect();
      this.ratio = window.devicePixelRatio || 1;
      const dpr = Math.max(1, this.ratio), width = Math.round(r.width * dpr), height = Math.round(r.height * dpr);
      if (this.canvas.width !== width) this.canvas.width = width;
      if (this.canvas.height !== height) this.canvas.height = height;
      this.w = r.width;
      this.h = r.height;
      this.dpr = dpr;
    }

    /** 变速标记：变快是红底兔子，变慢是蓝底蜗牛（自己画的简笔，配色照游戏）。 */
    speedIcon(x, y, r, faster) {
      const g = this.g;
      g.fillStyle = faster ? '#e8443a' : '#2f8fe0';
      g.beginPath();
      g.arc(x, y, r, 0, TAU);
      g.fill();
      g.lineWidth = Math.max(1, r * 0.12);
      g.strokeStyle = '#ffffff';
      g.stroke();
      g.fillStyle = '#ffffff';
      if (faster) {
        // 兔子：圆脑袋加两只竖耳朵
        g.beginPath();
        g.arc(x, y + r * 0.22, r * 0.36, 0, TAU);
        g.fill();
        for (const dx of [-0.17, 0.17]) {
          g.beginPath();
          g.ellipse(x + dx * r, y - r * 0.3, r * 0.1, r * 0.34, dx * 0.6, 0, TAU);
          g.fill();
        }
      } else {
        // 蜗牛：一圈壳加一条身子
        g.beginPath();
        g.arc(x + r * 0.08, y - r * 0.05, r * 0.36, 0, TAU);
        g.fill();
        g.fillRect(x - r * 0.55, y + r * 0.22, r * 1.05, r * 0.16);
        g.strokeStyle = '#2f8fe0';
        g.lineWidth = Math.max(1, r * 0.1);
        g.beginPath();
        g.arc(x + r * 0.08, y - r * 0.05, r * 0.16, 0, Math.PI * 1.5);
        g.stroke();
      }
    }

    indexAt(time) {
      const path = this.doc.path;
      let lo = 0, hi = path.length - 1;
      while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (path[mid].time <= time) lo = mid; else hi = mid - 1; }
      return lo;
    }

    /** 第 i 块砖朝上一块的方向（数学角）：有游戏的进方向就用（被挪开的砖也对），没有按相邻砖的位置。 */
    dirIn(i) {
      const path = this.doc.path, p = path[i], prev = path[i - 1], next = path[i + 1];
      if (p.entry !== null && p.entry !== undefined) return p.entry;
      if (prev) return Math.atan2(prev.y - p.y, prev.x - p.x);
      return next ? Math.atan2(p.y - next.y, p.x - next.x) : Math.PI;
    }

    /** 第 i 块砖朝下一块的方向。 */
    dirOut(i) {
      const path = this.doc.path, p = path[i], next = path[i + 1];
      if (p.exit !== null && p.exit !== undefined) return p.exit;
      return next ? Math.atan2(next.y - p.y, next.x - p.x) : this.dirIn(i) + Math.PI;
    }

    /** 两块砖是不是挨着（没被移动轨道挪开）：挨着的才画节与节之间的分隔。 */
    adjacent(a, b) {
      return Math.abs(Math.hypot(b.x - a.x, b.y - a.y) - (this.unit || 1)) < (this.unit || 1) * 0.1;
    }

    /** 星球从上一块的方向绕这块砖转到下一块：起始角和转过的角度（带方向，正是逆时针）。 */
    sweep(k) {
      const path = this.doc.path, p = path[k], next = path[k + 1];
      const t0 = this.dirIn(k), t1 = this.dirOut(k);
      const cw = ((t0 - t1) % TAU + TAU) % TAU || TAU; // 顺时针转过的角度
      const ccw = TAU - cw;
      // 游戏里星球每拍转 180°：有砖块转角就照转角选方向，没有就看这一段的时长，都没有按顺时针（游戏默认）
      let expected = null;
      const known = this.angleByTile.get(next.tile);
      if (known && known[0] === p.tile) expected = (known[1] || 360) * Math.PI / 180;
      else if (root.FingeringModel && this.doc.tempo.length) {
        const spb = root.FingeringModel.secondsPerBeat(this.doc, p.time);
        if (spb > 0) expected = (next.time - p.time) / spb * Math.PI;
      }
      const clockwise = expected === null || Math.abs(cw - expected) <= Math.abs(ccw - expected) + 1e-6;
      return { from: t0, sweep: clockwise ? -cw : ccw };
    }

    /**
     * 绕着第 k 块砖转的那颗星球此刻在哪个方向（数学角）。
     * 最后一块砖（谱尾）：落上去时在上一块的方向，之后照上一段的转向每拍转半圈（原来停在倒数第二块上，那块被挪走时画面一片空）。
     */
    orbit(k, time) {
      const path = this.doc.path, p = path[k];
      if (k < path.length - 1) {
        const next = path[k + 1];
        const f = next.time > p.time ? Math.max(0, Math.min(1, (time - p.time) / (next.time - p.time))) : 1;
        const { from, sweep } = this.sweep(k);
        return from + sweep * f;
      }
      const spb = this.fx.spbAt(p.time) || 0.5, turn = this.sweep(k - 1).sweep > 0 ? 1 : -1;
      return this.dirIn(k) + turn * Math.PI * Math.max(0, time - p.time) / spb;
    }

    /** @param badge 标题后面的小标签（轨道预览还在试验：2026-11-18 用户「所有写上实验」）。 */
    draw(time, title, badge) {
      const g = this.g;
      // 第一次画、像素比变了（拖到别的显示器）时量一次。卡片藏着（宽高为 0）时不画，也不每帧重量：
      // 卡片显示出来、大小变了时 app.js 的 ResizeObserver 会调 resize
      if (this.w === undefined || (window.devicePixelRatio || 1) !== this.ratio) this.resize();
      if (!this.w || !this.h) return;
      g.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
      g.clearRect(0, 0, this.w, this.h);
      const doc = this.doc, fx = this.fx;
      if (!doc || doc.path.length < 2 || !fx) return;
      const path = doc.path, unit = this.unit || 1.5, half = unit / 2;
      const k = this.indexAt(time), p = path[k];

      // 镜头只跟着星球（不用谱面自带的镜头，2026-11-18 用户）：平滑跟随，播放时对准稍后一点星球会到的位置，抵消平滑带来的落后
      const now = performance.now(), dt = this.lastDraw ? Math.min(0.1, (now - this.lastDraw) / 1000) : 0;
      this.lastDraw = now;
      const rate = dt > 0 && this.lastTime !== null ? Math.max(0, Math.min(2, (time - this.lastTime) / dt)) : 0;
      this.lastTime = time;
      const [aheadX, aheadY] = fx.playerAt(time + CAMERA_LAG * rate);
      if (this.camFresh) { this.camX = aheadX; this.camY = aheadY; this.camFresh = false; }
      const follow = 1 - Math.exp(-dt / CAMERA_LAG);
      this.camX += (aheadX - this.camX) * follow;
      this.camY += (aheadY - this.camY) * follow;
      // 画面高约 10 个单位（6.7 块砖），和游戏默认缩放一样；再乘玩家用滚轮调的远近
      this.zoom += (this.zoomTarget - this.zoom) * (1 - Math.exp(-dt / 0.08));
      if (Math.abs(this.zoomTarget - this.zoom) < 0.002) this.zoom = this.zoomTarget;
      const s = this.h / 10 * this.zoom;
      const cx = this.w / 2, cy = this.h / 2;
      const toScreen = (x, y) => [cx + (x - this.camX) * s, cy - (y - this.camY) * s];

      const bg = g.createRadialGradient(cx, cy, 10, cx, cy, Math.max(this.w, this.h) * 0.75);
      bg.addColorStop(0, '#1d2028');
      bg.addColorStop(1, '#101115');
      g.fillStyle = bg;
      g.fillRect(0, 0, this.w, this.h);

      // 砖块：每块的当前位置、旋转、缩放、透明度 = 移动轨道 × 出现消失动画；画面外的不画（trackfx 先粗筛，只算画面附近的）
      const margin = unit * s * 2, tiles = [];
      const near = fx.tilesNear(time, { x0: this.camX - cx / s, x1: this.camX + cx / s, y0: this.camY - cy / s, y1: this.camY + cy / s, margin: unit * 2 });
      for (const { i, st, rv } of near) {
        // 谱面用移动轨道把砖藏起来（透明或缩成 0）的地方画成淡虚影：游戏里那段通常靠装饰物撑画面（不复刻），
        // 预览里至少看得见路线（2026-11-18 用户：有的谱面轨道预览里没有轨道）。出现消失动画照常
        const hidden = st.alpha < 0.12 || (Math.abs(st.sx) < 0.05 && Math.abs(st.sy) < 0.05);
        // 透明度超过 100%（谱面常写 300%，缓动回弹也会冲过去）照 100% 画：globalAlpha 收到 0–1 以外的值，浏览器会忽略、沿用上一块砖的
        const alpha = Math.max(0, Math.min(1, hidden ? GHOST * rv.alpha : st.alpha * rv.alpha));
        const sx = (hidden ? 1 : st.sx) * rv.scale, sy = (hidden ? 1 : st.sy) * rv.scale;
        if (alpha < 0.01 || (Math.abs(sx) < 1e-3 && Math.abs(sy) < 1e-3)) continue;
        const wx = path[i].x + st.dx + rv.dx, wy = path[i].y + st.dy + rv.dy;
        const [px, py] = toScreen(wx, wy);
        const reach = margin * Math.max(1, Math.abs(sx), Math.abs(sy));
        if (px < -reach || px > this.w + reach || py < -reach || py > this.h + reach) continue;
        const rot = (st.rot + rv.rot) * Math.PI / 180, rc = Math.cos(rot), rs = Math.sin(rot);
        const arm = d => {
          const lx = Math.cos(d) * half * sx, ly = Math.sin(d) * half * sy;
          return toScreen(wx + lx * rc - ly * rs, wy + lx * rs + ly * rc);
        };
        const still = Math.abs(st.dx) + Math.abs(st.dy) + Math.abs(st.rot) + Math.abs(rv.rot) < 1e-6 && Math.abs(sx - 1) + Math.abs(sy - 1) < 1e-6 && alpha > 0.999;
        tiles.push({
          i, alpha, px, py, still, width: unit * 0.37 * s * (Math.abs(sx) + Math.abs(sy)) / 2,
          a: i > 0 || (path[i].entry !== null && path[i].entry !== undefined) ? arm(this.dirIn(i)) : [px, py],
          c: i < path.length - 1 ? arm(this.dirOut(i)) : [px, py],
        });
      }
      const stroke = (t, width, color) => {
        g.globalAlpha = t.alpha;
        g.beginPath();
        g.moveTo(t.a[0], t.a[1]);
        g.lineTo(t.px, t.py);
        g.lineTo(t.c[0], t.c[1]);
        g.lineWidth = width;
        g.strokeStyle = color;
        g.stroke();
      };
      // 尖角（太尖时切成斜角）、两头平切；先画深色描边（两头多 1.5 像素，挨着的会被下一块盖住），再画浅色砖面
      g.lineJoin = 'miter';
      g.miterLimit = 2.2;
      g.lineCap = 'butt';
      for (const t of tiles) {
        const ext = (from, to) => {
          const len = Math.hypot(to[0] - from[0], to[1] - from[1]) || 1;
          return [to[0] + (to[0] - from[0]) / len * 1.5, to[1] + (to[1] - from[1]) / len * 1.5];
        };
        stroke({ alpha: t.alpha, px: t.px, py: t.py, a: ext([t.px, t.py], t.a), c: ext([t.px, t.py], t.c) }, t.width + 3, EDGE);
      }
      for (const t of tiles) stroke(t, t.width, TILE);
      // 挨着、没在动的两块砖之间画分隔
      g.globalAlpha = 1;
      g.lineWidth = Math.max(1, s * 0.03);
      g.strokeStyle = SEAM;
      const byIndex = new Map(tiles.map(t => [t.i, t]));
      for (const t of tiles) {
        const before = byIndex.get(t.i - 1);
        if (!before || !t.still || !before.still || !this.adjacent(path[t.i - 1], path[t.i])) continue;
        const mx = (before.px + t.px) / 2, my = (before.py + t.py) / 2;
        const len = Math.hypot(t.px - before.px, t.py - before.py) || 1, nx = -(t.py - before.py) / len, ny = (t.px - before.px) / len;
        const w = t.width / 2;
        g.beginPath();
        g.moveTo(mx + nx * w, my + ny * w);
        g.lineTo(mx - nx * w, my - ny * w);
        g.stroke();
      }
      // 变速的砖：加速、减速标记（和游戏一样只标变速）
      for (const t of tiles) {
        if (!this.speedMarks.has(t.i)) continue;
        g.globalAlpha = t.alpha;
        this.speedIcon(t.px, t.py, s * 0.22, this.speedMarks.get(t.i));
      }
      g.globalAlpha = 1;

      // 星球：中心那颗在这块砖（跟着砖移动）上，另一颗按固定间距绕它转向下一块；过了最后一块砖停在最后一块上
      const [ox, oy] = fx.tileAt(k, time);
      const theta = this.orbit(k, time);
      const mx = ox + Math.cos(theta) * unit, my = oy + Math.sin(theta) * unit;
      if (k !== this.lastIndex) { this.lastIndex = k; this.landedAt = performance.now(); }
      const [pivotX, pivotY] = toScreen(ox, oy), [moverX, moverY] = toScreen(mx, my);

      // 星球绕行的圈
      g.strokeStyle = 'rgba(255,255,255,0.12)';
      g.lineWidth = 1;
      g.setLineDash([3, 4]);
      g.beginPath();
      g.arc(pivotX, pivotY, unit * s, 0, TAU);
      g.stroke();
      g.setLineDash([]);

      // 刚落到这块砖：圈一下
      const since = (performance.now() - this.landedAt) / 1000;
      if (since < 0.35 && p.hit) {
        g.strokeStyle = 'rgba(255,255,255,' + (0.7 * (1 - since / 0.35)) + ')';
        g.lineWidth = 2;
        g.beginPath();
        g.arc(pivotX, pivotY, s * (0.3 + since * 1.2), 0, TAU);
        g.stroke();
      }

      // 两颗星球轮流当中心，转的那颗留一点拖尾
      const pivotColor = k % 2 === 0 ? FIRE : ICE, moverColor = k % 2 === 0 ? ICE : FIRE;
      this.trail.push([mx, my]);
      if (this.trail.length > 8) this.trail.shift();
      for (let i = 0; i < this.trail.length - 1; i++) {
        const [tx, ty] = toScreen(this.trail[i][0], this.trail[i][1]);
        g.fillStyle = mix(moverColor, (i + 1) / this.trail.length * 0.3);
        g.beginPath();
        g.arc(tx, ty, s * 0.2 * (i + 1) / this.trail.length, 0, TAU);
        g.fill();
      }
      const planet = (x, y, color) => {
        const r = s * 0.26;
        g.save();
        g.shadowColor = color;
        g.shadowBlur = 16;
        g.fillStyle = color;
        g.beginPath();
        g.arc(x, y, r, 0, TAU);
        g.fill();
        g.restore();
        const shine = g.createRadialGradient(x - r * 0.35, y - r * 0.35, 0, x, y, r);
        shine.addColorStop(0, 'rgba(255,255,255,0.55)');
        shine.addColorStop(0.5, 'rgba(255,255,255,0)');
        g.fillStyle = shine;
        g.beginPath();
        g.arc(x, y, r, 0, TAU);
        g.fill();
      };
      planet(pivotX, pivotY, pivotColor);
      planet(moverX, moverY, moverColor);

      // 顶上压暗，写标题和砖号
      const v = g.createLinearGradient(0, 0, 0, 36);
      v.addColorStop(0, 'rgba(16,17,21,0.95)');
      v.addColorStop(1, 'rgba(16,17,21,0)');
      g.fillStyle = v;
      g.fillRect(0, 0, this.w, 36);
      g.fillStyle = '#b3b8c2';
      g.font = '600 11.5px ' + FONT;
      g.textBaseline = 'middle';
      g.textAlign = 'left';
      g.fillText(title, 12, 15);
      if (badge) {
        const x = 12 + g.measureText(title).width + 7;
        g.font = '600 10px ' + FONT;
        const w = g.measureText(badge).width + 10;
        g.fillStyle = 'rgba(242,193,78,0.16)';
        g.beginPath();
        g.roundRect(x, 8, w, 14, 7);
        g.fill();
        g.fillStyle = '#f2c14e';
        g.fillText(badge, x + 5, 15.5);
      }
      g.fillStyle = '#7d838e';
      g.font = '11px ' + MONO;
      g.textAlign = 'right';
      // 右上角让出放大按钮的位置；调过远近时顺带显示倍数
      g.fillText((Math.abs(this.zoom - 1) > 0.01 ? Math.round(this.zoom * 100) + '% · ' : '') + '#' + p.tile, this.w - 38, 15);
    }
  }

  root.TrackView = TrackView;
})(typeof globalThis !== 'undefined' ? globalThis : this);
