/* 播放：有音乐时跟着音乐走（音乐里的位置 = 谱面时间 + 偏移），没有音乐时用自己的时钟。
 * 有 Mod 另存的歌曲数据时整首解码，用 Web Audio 从采样里直接放（useBuffer）：浏览器在 OGG、VBR MP3 里跳播放位置不准，
 * Edge 实测播放中连续跳转后差 0.08–2.3 秒（2026-11-19 用户：暂停、滚动后音画不同步），WAV 和整首解码都准。
 * 没有时用 <audio>：它的 currentTime 更新得不够勤，两次更新之间按经过的时间往前推，画面才顺。
 * 打拍声用这一关游戏里的打击声（Mod 读出来放进 session.js）；没有时用 WebAudio 现合成一个（file:// 下读不了音效文件）。 */
(function (root) {
  'use strict';

  const START_LEAD = 0.02; // 解码好的音乐提前 20 毫秒排好再放：起点准确落在排定的采样上
  const LOOP_MIN = 0.05; // 循环区间至少 50 毫秒：起点等于终点时每帧都跳回起点、重建声源，永远不出声
  const BASE64_PIECE = 4 << 18; // 歌曲数据按 1 MB 文字一段换成字节（4 的倍数），不用先拼出一整份二进制字符串
  const WAVE_SLICE = 100; // 波形每段算多少个峰值（每秒 100 个峰值时是 1 秒音乐）

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
      this.buffer = null; // 整首解码好的音乐（AudioBuffer）；有它时不用 <audio>
      this.source = null; // 正在放的声源
      this.gain = null;
      this.anchor = null; // 声源的起点：AudioContext 时间 at 放到音乐位置 pos，每秒走 rate
      this.onSeekFail = null; // 音乐文件跳不了播放位置时通知界面（比如服务器不支持分段读取）
      el.addEventListener('waiting', () => this.cancelClicks());
      el.addEventListener('ended', () => { if (this.playing) this.pause(); });
    }

    get offset() { return this.baseOffset + this.userOffsetMs / 1000 + this.calibration; }

    /** 音乐的长度换算成谱面时间（没有音乐为 0）。 */
    get end() {
      if (this.buffer) return this.buffer.duration - this.offset;
      return this.hasAudio && Number.isFinite(this.el.duration) ? this.el.duration - this.offset : 0;
    }

    load(url) {
      this.pause();
      if (this.cancelLoad) this.cancelLoad();
      this.stopSource();
      this.buffer = null;
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

    /** 自己的时钟走到的谱面时间（没有音乐、还没到音乐开头时用）。 */
    clockTime(now = performance.now()) {
      return this.clock + (now - this.clockAt) / 1000 * this.rate;
    }

    time() {
      const now = performance.now();
      if (!this.playing) return this.clock;
      if (this.buffer) return this.bufferPosition() - this.offset;
      if (!this.hasAudio || this.preroll) {
        const t = this.clockTime(now);
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
      if (this.buffer) {
        if (this.playing) this.startSource(t + this.offset);
        return;
      }
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

    /**
     * A-B 循环（画面时间）：播放中走过终点就跳回起点，返回这一帧的画面时间。
     * 区间短于 LOOP_MIN 不绕回（界面也不让开）：起点等于终点时每帧都会跳回起点、重建声源，永远不出声。
     */
    wrapLoop(t, a, b) {
      if (!this.playing || a === null || b === null || !(b - a >= LOOP_MIN) || t < b) return t;
      this.seek(a);
      return a;
    }

    play() {
      if (this.playing) return;
      this.ensureContext();
      if (this.ctx && this.ctx.state === 'suspended') this.ctx.resume();
      this.cancelClicks(this.clock);
      this.playing = true;
      this.clockAt = performance.now();
      if (this.buffer) { this.startSource(this.clock + this.offset); return; }
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
      this.clockOffset = null; // 从音乐开头之前接过来时，之前按画面时钟取的对齐基准不含 <audio> 的起播延迟，等它走起来重取
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
      if (this.buffer) {
        this.clock = this.time();
        this.stopSource();
      } else if (this.preroll || !this.hasAudio) {
        // 还没到音乐开头（或没有音乐）：按自己的时钟记位置。不能调 time()，刚越过音乐开头时它会接上音乐、调 el.play()
        this.clock = this.clockTime();
      } else {
        // 先停 <audio> 再记位置：停住后 time() 就是它真正停的地方，原地继续时不用再跳一次（跳一次在 OGG、MP3 里就可能错位）
        this.el.pause();
        this.clock = this.time();
      }
      this.playing = false;
      this.preroll = false;
    }

    setRate(practiceSpeed, pitch = 1) {
      const t = this.time();
      this.cancelClicks(t);
      // 谱面和音乐位置都用原曲秒数；只改变每秒经过的谱面时间。
      this.rate = Math.min(16, Math.max(1 / 16, practiceSpeed * pitch));
      this.el.playbackRate = this.rate;
      this.clock = t;
      this.clockAt = performance.now();
      if (this.buffer && this.playing) this.startSource(t + this.offset);
    }

    setVolume(v) {
      this.volume = v;
      this.el.volume = v;
      if (this.gain) this.gain.gain.value = v;
    }

    /**
     * 换成整首解码好的音乐：之后播放、跳转都从采样里直接来，不再经过 <audio>。
     * 正在播时从画面所在的位置接上。
     */
    useBuffer(buffer) {
      const t = this.time(), wasPlaying = this.playing;
      if (this.cancelLoad) this.cancelLoad();
      this.el.pause();
      if (this.el.src && this.el.src.startsWith('blob:')) URL.revokeObjectURL(this.el.src);
      this.el.removeAttribute('src');
      this.el.load();
      this.buffer = buffer;
      this.hasAudio = true;
      this.preroll = false;
      this.mediaStart = null;
      this.clock = t;
      this.cancelClicks(t);
      if (wasPlaying) this.startSource(t + this.offset);
    }

    /** 声卡此刻放出来的是 AudioContext 时间轴上的哪一刻（比正在生成的采样晚一个输出延迟）。 */
    audibleTime() {
      const ctx = this.ctx;
      const stamp = ctx.getOutputTimestamp && ctx.getOutputTimestamp();
      return stamp && stamp.performanceTime > 0
        ? stamp.contextTime + (performance.now() - stamp.performanceTime) / 1000
        : ctx.currentTime - (ctx.baseLatency || 0) - (ctx.outputLatency || 0);
    }

    /** 解码好的音乐此刻听到的位置；声音还没出来时停在起点。 */
    bufferPosition() {
      const a = this.anchor;
      if (!a || !this.ctx) return this.clock + this.offset;
      return a.pos + Math.max(0, this.audibleTime() - a.at) * a.rate;
    }

    /** 从音乐位置 pos 开始放解码好的音乐（pos 为负：排到那时再从头放）。 */
    startSource(pos) {
      this.stopSource();
      this.ensureContext();
      const ctx = this.ctx;
      if (!ctx) return;
      if (ctx.state === 'suspended') ctx.resume();
      if (!this.gain) {
        this.gain = ctx.createGain();
        this.gain.gain.value = this.volume;
        this.gain.connect(ctx.destination);
      }
      const at = ctx.currentTime + START_LEAD;
      const source = ctx.createBufferSource();
      source.buffer = this.buffer;
      source.playbackRate.value = this.rate;
      source.connect(this.gain);
      if (pos < 0) source.start(at - pos / this.rate, 0);
      else source.start(at, pos);
      source.onended = () => {
        if (this.source !== source) return; // 跳转、暂停时换掉的旧声源
        this.source = null;
        if (this.playing) this.pause(); // 放到了歌曲末尾
      };
      this.source = source;
      this.anchor = { at, pos, rate: this.rate };
    }

    stopSource() {
      const source = this.source;
      if (!source) return;
      this.source = null;
      try { source.stop(); } catch (e) { /* 已经停了：不用管 */ }
      source.disconnect();
    }

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

    /**
     * 把未来 120 毫秒的打拍声交给 Web Audio，排在音乐放到那一拍的同一个采样上。
     * visualTime、loopEnd 是画面时间（循环在画面走到终点时绕回）；音符按音乐拍点时间排，两者差一个画面校准。
     */
    scheduleClicks(visualTime, notes, firstNoteAt, loopEnd = Infinity) {
      const ctx = this.ctx;
      if (!this.playing || !ctx || ctx.state !== 'running') return;
      if (!this.buffer && this.hasAudio && !this.preroll && (this.mediaStart !== null || this.el.seeking || this.el.paused || this.el.readyState < 3)) return;

      const calibration = this.hasAudio ? this.calibration : 0;
      const musicTime = visualTime + calibration;
      const now = this.renderedBeat(musicTime);
      // 还没到音乐开头：<audio> 起播要多久事先不知道，音乐开头之后的拍子等它真正走起来再排
      const until = Math.min(now + 0.12 * this.rate, loopEnd + calibration, this.preroll ? this.calibration - this.offset : Infinity);
      const from = Math.max(this.scheduledUntil ?? musicTime, musicTime - 0.02 * this.rate);
      for (let i = firstNoteAt(from); i < notes.length && notes[i].time < until; i++) {
        this.click(notes[i].lanes.length > 1, (notes[i].time - now) / this.rate);
      }
      this.scheduledUntil = Math.max(from, until);
    }

    /**
     * 声卡此刻正在生成的采样对应哪个音乐拍点时间（打拍声和音乐走同一条输出，排在同一个采样上就同时听到）。
     * 整首解码时声源哪个采样放哪个位置是排定的，直接算；声源刚排上、还没出声时画面停在起点，不能拿画面来对。
     * <audio> 和自己的时钟只知道此刻听到哪里：用输出时间戳对到正在生成的采样，两者相差设备的输出延迟。
     */
    renderedBeat(musicTime) {
      const ctx = this.ctx, a = this.anchor;
      if (this.buffer && a) return a.pos + (ctx.currentTime - a.at) * a.rate - this.offset + this.calibration;
      const sample = musicTime - this.audibleTime() * this.rate;
      // OGG 的媒体位置在相邻画面间有几毫秒抖动。定位、暂停和换速都会丢掉旧锚点。
      this.clockOffset = this.clockOffset === null ? sample : this.clockOffset + (sample - this.clockOffset) * 0.05;
      return ctx.currentTime * this.rate + this.clockOffset;
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

  /**
   * Mod 另存的歌曲数据（base64 文本）换回字节。浏览器自带 Uint8Array.fromBase64 时直接用；
   * 没有时一段一段换，不先拼出和文件一样大的二进制字符串（64 MB 的歌少占 64 MB）。
   */
  Player.bytesFromBase64 = function (text) {
    if (typeof Uint8Array.fromBase64 === 'function') return Uint8Array.fromBase64(text);
    const pad = text.endsWith('==') ? 2 : text.endsWith('=') ? 1 : 0;
    const out = new Uint8Array(Math.floor(text.length * 3 / 4) - pad);
    for (let i = 0, o = 0; i < text.length; i += BASE64_PIECE) {
      const binary = atob(text.slice(i, i + BASE64_PIECE));
      for (let k = 0; k < binary.length; k++) out[o++] = binary.charCodeAt(k);
    }
    return out;
  };

  /**
   * 整首解码（离线的 AudioContext，不用等玩家点一下页面；解出来的声音哪个 AudioContext 都能放）。
   * 字节占满整个缓冲时直接交出去、不再拷一份（几十 MB 的歌）；解码会拿走这块内存，之后 bytes 不能再用。
   */
  Player.decode = function (bytes, sampleRate) {
    const Offline = root.OfflineAudioContext || root.webkitOfflineAudioContext;
    if (!Offline) return Promise.reject(new Error('Web Audio unavailable'));
    const whole = bytes.byteOffset === 0 && bytes.byteLength === bytes.buffer.byteLength;
    const data = whole ? bytes.buffer : bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
    return new Offline(2, 1, sampleRate || 48000).decodeAudioData(data);
  };

  /**
   * 从解码好的音乐算波形（每秒 perSecond 个峰值，0–255），分段算：step(deadline) 一段段算到 performance.now() 过了 deadline
   * （至少算一段），算完返回 true。没算到的峰值是 0，算好的部分先能画。整首一次算完时 5 分钟的歌占主线程 130 毫秒、
   * 20 分钟 500 毫秒，画面会冻住、打拍声可能漏掉。
   */
  Player.waveform = function (buffer, perSecond) {
    const frames = Math.max(1, Math.floor(buffer.sampleRate / perSecond));
    const peaks = new Uint8Array(Math.ceil(buffer.length / frames));
    const channels = Array.from({ length: buffer.numberOfChannels }, (_, c) => buffer.getChannelData(c));
    let done = 0;
    return {
      peaks, perSecond: buffer.sampleRate / frames,
      get done() { return done; },
      step(deadline) {
        do {
          const end = Math.min(peaks.length, done + WAVE_SLICE);
          for (let k = done; k < end; k++) {
            const from = k * frames, to = Math.min(buffer.length, from + frames);
            let max = 0;
            for (const data of channels) for (let i = from; i < to; i++) { const v = Math.abs(data[i]); if (v > max) max = v; }
            peaks[k] = Math.round(Math.sqrt(Math.min(1, max)) * 255); // 先取最大再开方：和逐个采样开方取最大一样，快几倍
          }
          done = end;
        } while (done < peaks.length && performance.now() < deadline);
        return done >= peaks.length;
      },
    };
  };

  Player.LOOP_MIN = LOOP_MIN;
  root.EditorPlayer = Player;
})(typeof globalThis !== 'undefined' ? globalThis : this);
