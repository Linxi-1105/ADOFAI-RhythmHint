/* 播放：有音乐时跟着 <audio> 走（音乐里的位置 = 谱面时间 + 偏移），没有音乐时用自己的时钟。
 * <audio>.currentTime 更新得不够勤，两次更新之间按经过的时间往前推，画面才顺。
 * 打拍声用这一关游戏里的打击声（Mod 读出来放进 session.js）；没有时用 WebAudio 现合成一个（file:// 下读不了音效文件）。 */
(function (root) {
  'use strict';

  class Player {
    constructor(el) {
      this.el = el;
      // 保音高的时间拉伸会移动瞬态；半速下鼓点能偏出 65 毫秒。练习变速直接随速变调。
      el.preservesPitch = false;
      this.hasAudio = false;
      this.cancelLoad = null;
      this.baseOffset = 0;
      this.calibration = 0; // 游戏的画面校准（秒）
      this.userOffsetMs = 0;
      this.rate = 1;
      this.playing = false;
      this.clock = 0; // 暂停时的谱面时间；用自己的时钟播放时是起点
      this.clockAt = 0;
      this.preroll = false; // 谱面时间还没到音乐开头：先走自己的时钟
      this.lastAudio = -1;
      this.lastAudioAt = 0;
      this.mediaStart = null; // 定位或恢复后，等音乐真正前进再往前排打拍声
      this.ctx = null;
      this.kick = null; // 游戏的打击声（AudioBuffer）；没有时用合成的
      this.kickSource = ''; // 正在用（或正在解码）的打击声数据
      this.pending = new Set(); // 排好还没响完的打拍声（暂停、跳转时取消）
      this.scheduledUntil = null;
      this.clockOffset = null;
      this.volume = 0.8;
      this.onSeekFail = null; // 音乐文件跳不了播放位置时通知界面（比如服务器不支持分段读取）
      el.addEventListener('waiting', () => this.cancelClicks());
      el.addEventListener('ended', () => { if (this.playing) this.pause(); });
    }

    get offset() { return this.baseOffset + this.userOffsetMs / 1000 + this.calibration; }

    /** 音乐的长度换算成谱面时间（没有音乐为 0）。 */
    get end() { return this.hasAudio && Number.isFinite(this.el.duration) ? this.el.duration - this.offset : 0; }

    load(url) {
      this.pause();
      if (this.cancelLoad) this.cancelLoad();
      this.hasAudio = false;
      if (this.el.src.startsWith('blob:')) URL.revokeObjectURL(this.el.src);
      if (!url) {
        this.el.removeAttribute('src');
        this.el.load();
        return Promise.resolve(false);
      }
      return new Promise((resolve, reject) => {
        const cleanup = () => {
          this.el.removeEventListener('loadedmetadata', ok);
          this.el.removeEventListener('error', fail);
          this.cancelLoad = null;
        };
        const ok = () => {
          const t = this.time();
          cleanup();
          this.hasAudio = true;
          this.el.volume = this.volume;
          if (this.playing) { // 文件还没准备好时已经按了播放：从画面走到的位置接入音乐
            this.cancelClicks(t);
            if (t + this.offset < 0) this.preroll = true;
            else this.startAudio(t);
          }
          resolve(true);
        };
        const fail = () => { cleanup(); reject(new Error(this.el.error ? 'code ' + this.el.error.code : 'error')); };
        this.cancelLoad = () => { cleanup(); resolve(false); };
        this.el.addEventListener('loadedmetadata', ok);
        this.el.addEventListener('error', fail);
        this.el.src = url;
        this.el.load();
      });
    }

    time() {
      const now = performance.now();
      if (!this.playing) return this.clock;
      if (!this.hasAudio || this.preroll) {
        const t = this.clock + (now - this.clockAt) / 1000 * this.rate;
        if (this.preroll && t + this.offset >= 0) this.startAudio(t);
        return t;
      }
      const a = this.el.currentTime;
      if (this.el.seeking || this.el.paused || this.el.readyState < 3) {
        this.lastAudio = -1;
        return a - this.offset;
      }
      if (this.mediaStart !== null) {
        if (Math.abs(a - this.mediaStart) < 1e-4) return a - this.offset;
        this.mediaStart = null;
      }
      if (a !== this.lastAudio) { this.lastAudio = a; this.lastAudioAt = now; }
      const ahead = Math.min(0.25, (now - this.lastAudioAt) / 1000) * this.rate;
      return a + ahead - this.offset;
    }

    seek(t) {
      this.cancelClicks(t);
      this.clock = t;
      this.clockAt = performance.now();
      if (this.hasAudio) {
        const target = t + this.offset;
        if (target < 0) {
          this.el.currentTime = 0;
          if (this.playing) { this.el.pause(); this.preroll = true; }
        } else {
          if (Math.abs(this.el.currentTime - target) > 1e-4) this.el.currentTime = target;
          this.mediaStart = this.el.currentTime;
          this.lastAudio = -1;
          if (!this.canSeekTo(target) && this.onSeekFail) this.onSeekFail();
          if (this.playing && this.preroll) { this.preroll = false; this.el.play().catch(() => {}); }
        }
      }
    }

    play() {
      if (this.playing) return;
      this.ensureContext();
      if (this.ctx && this.ctx.state === 'suspended') this.ctx.resume();
      this.cancelClicks(this.clock);
      this.playing = true;
      this.clockAt = performance.now();
      if (!this.hasAudio) return;
      const target = this.clock + this.offset;
      if (target < 0) { this.preroll = true; return; }
      this.startAudio(this.clock);
    }

    /** 音乐能不能跳到这个位置（还没读到长度时当作能）。 */
    canSeekTo(position) {
      if (this.el.readyState < 1) return true;
      const r = this.el.seekable;
      for (let i = 0; i < r.length; i++) if (position >= r.start(i) - 0.05 && position <= r.end(i) + 0.05) return true;
      return position < 0.05;
    }

    startAudio(t) {
      this.preroll = false;
      const target = Math.max(0, t + this.offset);
      if (Math.abs(this.el.currentTime - target) > 1e-4) this.el.currentTime = target;
      this.mediaStart = this.el.currentTime;
      if (!this.canSeekTo(target) && this.onSeekFail) this.onSeekFail();
      this.el.playbackRate = this.rate;
      this.lastAudio = -1;
      this.el.play().catch(() => {});
    }

    pause() {
      this.cancelClicks();
      if (!this.playing) return;
      this.clock = this.time();
      this.playing = false;
      this.preroll = false;
      this.el.pause();
    }

    setRate(practiceSpeed, pitch = 1) {
      const t = this.time();
      this.cancelClicks(t);
      // 谱面和音乐位置都用原曲秒数；只改变每秒经过的谱面时间。
      this.rate = Math.min(16, Math.max(1 / 16, practiceSpeed * pitch));
      this.el.playbackRate = this.rate;
      this.clock = t;
      this.clockAt = performance.now();
    }

    setVolume(v) { this.volume = v; this.el.volume = v; }

    ensureContext() {
      if (this.ctx || !(root.AudioContext || root.webkitAudioContext)) return;
      this.ctx = new (root.AudioContext || root.webkitAudioContext)();
    }

    /**
     * 打拍声换成这一关游戏里的打击声：Mod 从游戏里读出来的 WAV（base64）。用离线的 AudioContext 解码，
     * 不用等玩家点一下页面；解出来的声音哪个 AudioContext 都能放。解码失败就继续用合成的。
     */
    setHitSound(base64) {
      const Offline = root.OfflineAudioContext || root.webkitOfflineAudioContext;
      // 从「历史」换到别的关卡时不留上一关的打击声（这一关没有就用合成的）；还在解码的上一关的声音解完也不用
      this.kick = null;
      const wanted = this.kickSource = base64 || '';
      if (!wanted || !Offline) return Promise.resolve(false);
      const bytes = Uint8Array.from(atob(wanted), c => c.charCodeAt(0));
      return new Offline(1, 1, 44100).decodeAudioData(bytes.buffer)
        .then(buffer => { if (this.kickSource !== wanted) return false; this.kick = buffer; return true; }, () => false);
    }

    /** 取消排好还没响的打拍声。 */
    cancelClicks(visualTime) {
      this.clockOffset = null;
      this.scheduledUntil = visualTime === undefined ? null : visualTime + (this.hasAudio ? this.calibration : 0);
      for (const source of this.pending) {
        try { source.stop(); } catch (e) { /* 已经停了：不用管 */ }
      }
      this.pending.clear();
    }

    /** 用声卡正在输出的采样对齐音乐，再把未来 120 毫秒的打拍声交给 Web Audio。 */
    scheduleClicks(visualTime, notes, firstNoteAt, loopEnd = Infinity) {
      const ctx = this.ctx;
      if (!this.playing || !ctx || ctx.state !== 'running') return;
      if (this.hasAudio && !this.preroll && (this.mediaStart !== null || this.el.seeking || this.el.paused || this.el.readyState < 3)) return;

      const musicTime = visualTime + (this.hasAudio ? this.calibration : 0);
      const renderTime = ctx.currentTime;
      const stamp = ctx.getOutputTimestamp && ctx.getOutputTimestamp();
      // currentTime 是正在生成的采样。扬声器正在播的采样比它早，两者相差设备的输出延迟。
      const audible = stamp && stamp.performanceTime > 0
        ? stamp.contextTime + (performance.now() - stamp.performanceTime) / 1000
        : renderTime - (ctx.baseLatency || 0) - (ctx.outputLatency || 0);
      const sample = musicTime - audible * this.rate;
      // OGG 的媒体位置在相邻画面间有几毫秒抖动。定位、暂停和换速都会丢掉旧锚点。
      this.clockOffset = this.clockOffset === null ? sample : this.clockOffset + (sample - this.clockOffset) * 0.05;
      const now = renderTime * this.rate + this.clockOffset;
      const until = Math.min(now + 0.12 * this.rate, loopEnd);
      const from = Math.max(this.scheduledUntil ?? musicTime, musicTime - 0.02 * this.rate);
      for (let i = firstNoteAt(from); i < notes.length && notes[i].time < until; i++) {
        this.click(notes[i].lanes.length > 1, (notes[i].time - now) / this.rate);
      }
      this.scheduledUntil = Math.max(from, until);
    }

    /** 记下排好的声源，响完自动拿掉。 */
    track(source) {
      this.pending.add(source);
      source.onended = () => this.pending.delete(source);
    }

    /** 打拍声，delay 秒后响（按音符的准确时间提前排好）。 */
    click(strong, delay) {
      this.ensureContext();
      const ctx = this.ctx;
      if (!ctx) return;
      const at = ctx.currentTime + Math.max(0, delay || 0);
      if (this.kick) {
        const source = ctx.createBufferSource(), gain = ctx.createGain();
        source.buffer = this.kick;
        gain.gain.value = this.volume;
        source.connect(gain).connect(ctx.destination);
        source.start(at);
        this.track(source);
        return;
      }
      // 没有游戏的打击声时（离线工具生成的数据、游戏里读不到）用合成的：低频正弦从 150 Hz 快速滑到 48 Hz，前面加一下很短的高频起音，听得出落点（自己合成，不用游戏音效）
      const t = at, level = (strong ? 0.95 : 0.8) * this.volume + 0.02;
      const body = ctx.createOscillator(), bodyGain = ctx.createGain();
      body.type = 'sine';
      body.frequency.setValueAtTime(150, t);
      body.frequency.exponentialRampToValueAtTime(48, t + 0.11);
      bodyGain.gain.setValueAtTime(0.0001, t);
      bodyGain.gain.exponentialRampToValueAtTime(level, t + 0.003);
      bodyGain.gain.exponentialRampToValueAtTime(0.0001, t + 0.22);
      body.connect(bodyGain).connect(ctx.destination);
      body.start(t);
      body.stop(t + 0.24);
      this.track(body);
      const tick = ctx.createOscillator(), tickGain = ctx.createGain();
      tick.type = 'triangle';
      tick.frequency.setValueAtTime(1800, t);
      tick.frequency.exponentialRampToValueAtTime(600, t + 0.012);
      tickGain.gain.setValueAtTime(0.0001, t);
      tickGain.gain.exponentialRampToValueAtTime(level * 0.35, t + 0.001);
      tickGain.gain.exponentialRampToValueAtTime(0.0001, t + 0.018);
      tick.connect(tickGain).connect(ctx.destination);
      tick.start(t);
      tick.stop(t + 0.02);
      this.track(tick);
    }
  }

  /** 从玩家手动选的音乐文件算波形（每秒 perSecond 个峰值，0–255）。 */
  async function peaksFromFile(file, perSecond) {
    const Ctx = root.AudioContext || root.webkitAudioContext;
    if (!Ctx) return null;
    const ctx = new Ctx();
    try {
      const buffer = await ctx.decodeAudioData(await file.arrayBuffer());
      const frames = Math.max(1, Math.floor(buffer.sampleRate / perSecond));
      const out = new Uint8Array(Math.ceil(buffer.length / frames));
      for (let c = 0; c < buffer.numberOfChannels; c++) {
        const data = buffer.getChannelData(c);
        for (let i = 0; i < data.length; i++) {
          const k = (i / frames) | 0, v = Math.round(Math.sqrt(Math.min(1, Math.abs(data[i]))) * 255);
          if (v > out[k]) out[k] = v;
        }
      }
      return { peaks: out, perSecond: buffer.sampleRate / frames };
    } finally {
      ctx.close();
    }
  }

  root.EditorPlayer = Player;
  root.peaksFromFile = peaksFromFile;
})(typeof globalThis !== 'undefined' ? globalThis : this);
