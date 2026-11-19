/* Local audio transport. All positions are chart seconds; offset is audio - chart. */
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else root.StudioAudio = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    'use strict';
    const systemNow = () => performance.now() / 1000;
    function finite(value, name) {
        if (typeof value !== 'number' || !Number.isFinite(value)) throw new Error(name + '必须是有限数值');
        return value;
    }
    const chartToAudio = (time, offsetMs) => time + offsetMs / 1000;
    const audioToChart = (time, offsetMs) => time - offsetMs / 1000;
    function wrapLoop(time, a, b) { return time >= b ? a + ((time - a) % (b - a)) : time; }

    class TimelineClock {
        // Injectable now returns seconds (not milliseconds).
        constructor(now = systemNow) {
            this.now = now;
            this.position = 0;
            this.anchor = now();
            this.rate = 1;
            this.running = false;
            this.loop = { a: 0, b: 1, enabled: false };
        }
        getTime() {
            let time = this.position + (this.running ? (this.now() - this.anchor) * this.rate : 0);
            if (this.running && this.loop.enabled && time >= this.loop.b) {
                time = wrapLoop(time, this.loop.a, this.loop.b);
                this.position = time;
                this.anchor = this.now();
            }
            return time;
        }
        isPlaying() { return this.running; }
        seek(time) { this.position = finite(time, '播放位置'); this.anchor = this.now(); }
        play() { if (!this.running) { this.anchor = this.now(); this.running = true; } }
        pause() { this.seek(this.getTime()); this.running = false; }
        setRate(rate) {
            finite(rate, '播放速度');
            if (rate <= 0) throw new Error('播放速度必须大于零');
            this.seek(this.getTime()); this.rate = rate;
        }
        setLoop(a, b, enabled) {
            finite(a, '循环起点'); finite(b, '循环终点');
            if (a >= b) throw new Error('循环终点必须晚于起点');
            this.seek(this.getTime()); this.loop = { a, b, enabled: !!enabled };
        }
    }

    function buildWaveform(channels, duration, requestedBins = 1500) {
        const length = channels[0]?.length || 0;
        const bins = Math.min(1500, Math.max(1, Math.floor(requestedBins)), length);
        const peaks = new Array(bins).fill(0);
        for (let bin = 0; bin < bins; bin++) {
            const from = Math.floor(bin * length / bins), to = Math.floor((bin + 1) * length / bins);
            let peak = 0;
            for (const samples of channels.slice(0, 2)) {
                for (let i = from; i < to; i++) peak = Math.max(peak, Math.abs(samples[i] || 0));
            }
            peaks[bin] = Math.min(1, peak);
        }
        return { peaks, duration };
    }

    function createTransport(options = {}) {
        const clock = new TimelineClock(options.now || systemNow);
        const makeAudio = options.audioFactory || (url => new Audio(url));
        const urls = options.urlApi || (typeof URL !== 'undefined' ? URL : null);
        const contextFactory = options.audioContextFactory || (() => {
            const Type = globalThis.AudioContext || globalThis.webkitAudioContext;
            return Type ? new Type() : null;
        });
        let audio = null, objectUrl = null, waveform = null, loaded = false;
        let running = false, mode = 'idle', offsetMs = 0, rate = 1;
        let generation = 0, playGeneration = 0, disposed = false, pendingPlay = null;
        // Only an explicit position change (seek/offset/load/loop/preroll) needs
        // decoder repositioning. Plain pause/resume retains the media position.
        let needsSeek = true, pendingSeekTime = null;
        let cancelLoad = null, removeAudioEvents = () => {};
        const usesMediaTime = () => audio && (mode === 'audio' || mode === 'starting');
        const peek = () => usesMediaTime() ? audioToChart(audio.currentTime, offsetMs) : clock.getTime();
        const getDuration = () => loaded && audio ? audioToChart(audio.duration, offsetMs) : Infinity;
        function notify(reason) {
            if (!disposed) options.onChange?.({ reason, playing: running, time: peek(), duration: getDuration() });
        }
        function report(error) { options.onError?.(error instanceof Error ? error : new Error(String(error))); }
        function requireLive() { if (disposed) throw new Error('播放器已关闭'); }
        function pause() {
            if (disposed) return;
            const fromMedia = usesMediaTime();
            const unfinishedSeek = fromMedia && audio.seeking && pendingSeekTime !== null;
            // pause() synchronously refreshes the official playback position.
            // Reading it before pause would save an older snapshot and seek
            // backwards on every resume. Starting media may already have played
            // even though its play() promise has not settled yet.
            if (audio) audio.pause();
            // During an unfinished seek, native pause can still report the old
            // decoded position. Preserve the user's explicit target in that case.
            const time = unfinishedSeek ? pendingSeekTime : fromMedia ? audioToChart(audio.currentTime, offsetMs) : clock.getTime();
            if (unfinishedSeek) needsSeek = true;
            else pendingSeekTime = null;
            running = false; playGeneration++; pendingPlay = null;
            clock.pause(); clock.seek(time); mode = 'idle';
            notify('pause');
        }
        function releaseAudio() {
            removeAudioEvents(); removeAudioEvents = () => {};
            if (audio) { audio.pause(); audio.removeAttribute?.('src'); audio.load?.(); }
            if (objectUrl) urls.revokeObjectURL(objectUrl);
            audio = null; objectUrl = null; waveform = null; loaded = false; needsSeek = true; pendingSeekTime = null;
        }
        function stopAt(time) {
            running = false; playGeneration++; pendingPlay = null;
            clock.pause(); clock.seek(time); mode = 'idle';
            if (audio) audio.pause();
            notify('ended');
        }
        function startAudio(time) {
            const target = audio, request = ++playGeneration;
            if (!target || !loaded) return Promise.resolve();
            const at = chartToAudio(time, offsetMs);
            if (at >= target.duration) { stopAt(time); return Promise.resolve(); }
            clock.pause(); clock.seek(time); mode = 'starting';
            let playback;
            try {
                if (needsSeek) {
                    target.currentTime = Math.max(0, at);
                    pendingSeekTime = time; needsSeek = false;
                }
                target.playbackRate = rate;
                playback = target.play();
            } catch (error) { playback = Promise.reject(error); }
            const promise = Promise.resolve(playback).then(() => {
                if (disposed || target !== audio || request !== playGeneration || !running) {
                    if (target !== audio || !running) target.pause();
                    return;
                }
                mode = 'audio';
                if (!target.seeking) pendingSeekTime = null;
                // The virtual clock remains stopped while music is the master.
                clock.seek(audioToChart(target.currentTime, offsetMs));
                pendingPlay = null; notify('play');
            }, error => {
                if (disposed || target !== audio || request !== playGeneration) return;
                running = false; mode = 'idle'; clock.pause(); pendingPlay = null;
                report(error); notify('error');
                throw error;
            });
            pendingPlay = promise;
            return promise;
        }
        function resumeAt(time) {
            clock.seek(time);
            if (loaded && audio && chartToAudio(time, offsetMs) >= 0) return startAudio(time);
            needsSeek = true;
            mode = 'clock'; clock.play(); notify('play');
            return Promise.resolve();
        }
        function getTime() {
            if (!running) return clock.getTime();
            let time = peek();
            if (mode === 'starting') return time;
            if (mode === 'audio' && clock.loop.enabled && time >= clock.loop.b) {
                time = wrapLoop(time, clock.loop.a, clock.loop.b);
                audio.pause(); mode = 'clock'; needsSeek = true;
                resumeAt(time).catch(() => {});
                return time;
            }
            if (mode === 'audio') {
                if (!audio.seeking) pendingSeekTime = null;
                clock.seek(time);
                if (audio.ended) stopAt(time);
            } else if (loaded && audio && chartToAudio(time, offsetMs) >= 0) {
                startAudio(time).catch(() => {});
            }
            return time;
        }
        function play() {
            requireLive();
            if (running) return pendingPlay || Promise.resolve();
            running = true;
            return resumeAt(clock.getTime());
        }
        function seek(seconds) {
            requireLive(); finite(seconds, '播放位置');
            playGeneration++; pendingPlay = null;
            needsSeek = true; pendingSeekTime = null;
            if (audio) audio.pause();
            mode = 'idle'; clock.pause(); clock.seek(seconds);
            if (running) resumeAt(seconds).catch(() => {});
            notify('seek');
        }
        function setRate(value) {
            requireLive(); finite(value, '播放速度');
            if (value < 0.25 || value > 4) throw new Error('播放速度需要在 0.25× 至 4× 之间');
            const time = getTime();
            if (audio) audio.playbackRate = value;
            rate = value; clock.setRate(value); clock.seek(time);
            notify('rate');
        }
        function setOffset(value) {
            requireLive(); finite(value, '音频偏移');
            const time = getTime(); offsetMs = value;
            seek(time); notify('offset');
        }
        function setLoop(a, b, enabled) {
            requireLive();
            clock.setLoop(a, b, enabled);
            notify('loop');
        }
        async function decodeWaveform(file, token) {
            if (typeof file.arrayBuffer !== 'function') return;
            let context = null;
            try {
                const buffer = await file.arrayBuffer();
                if (disposed || token !== generation) return;
                context = contextFactory();
                if (!context) return;
                const decoded = await context.decodeAudioData(buffer);
                if (disposed || token !== generation) return;
                const channels = [];
                for (let i = 0; i < Math.min(2, decoded.numberOfChannels); i++) channels.push(decoded.getChannelData(i));
                waveform = buildWaveform(channels, decoded.duration);
                notify('waveform');
            } catch (error) {
                if (!disposed && token === generation) report(new Error('音频可以继续试听，但波形解码失败：' + error.message));
            } finally {
                if (context) { try { await context.close(); } catch (_) { /* Already closed. */ } }
            }
        }
        function loadFile(file) {
            requireLive();
            if (!file) return Promise.reject(new Error('请选择本地音频文件'));
            pause();
            if (cancelLoad) cancelLoad();
            releaseAudio();
            const token = ++generation;
            try {
                objectUrl = urls.createObjectURL(file);
                audio = makeAudio(objectUrl);
            } catch (error) {
                releaseAudio(); report(error); return Promise.reject(error);
            }
            const target = audio;
            target.preload = 'auto';
            return new Promise((resolve, reject) => {
                let settled = false;
                function cleanupLoad() {
                    target.removeEventListener('loadedmetadata', metadata);
                    cancelLoad = null;
                }
                function metadata() {
                    if (settled || disposed || token !== generation) return;
                    if (!Number.isFinite(target.duration) || target.duration <= 0) {
                        failure(); return;
                    }
                    settled = true; loaded = true; cleanupLoad();
                    notify('loaded');
                    resolve({ name: file.name || '本地音频', duration: target.duration });
                    decodeWaveform(file, token);
                }
                function failure() {
                    if (disposed || token !== generation) return;
                    const error = new Error('浏览器无法读取这个音频，请尝试 OGG、MP3 或 WAV。');
                    if (!settled) { settled = true; cleanupLoad(); reject(error); }
                    loaded = false; pause(); report(error);
                }
                function ended() {
                    if (disposed || token !== generation || !running) return;
                    const time = audioToChart(target.currentTime, offsetMs);
                    if (clock.loop.enabled && time >= clock.loop.b) {
                        seek(wrapLoop(time, clock.loop.a, clock.loop.b));
                    } else stopAt(time);
                }
                cancelLoad = () => {
                    if (!settled) {
                        settled = true; cleanupLoad();
                        const error = new Error('音频加载已替换'); error.name = 'AbortError'; reject(error);
                    }
                };
                target.addEventListener('loadedmetadata', metadata);
                target.addEventListener('error', failure);
                target.addEventListener('ended', ended);
                removeAudioEvents = () => {
                    target.removeEventListener('loadedmetadata', metadata);
                    target.removeEventListener('error', failure);
                    target.removeEventListener('ended', ended);
                };
                if (target.readyState >= 1) metadata();
            });
        }
        function dispose() {
            if (disposed) return;
            pause(); disposed = true; generation++;
            if (cancelLoad) cancelLoad();
            releaseAudio();
        }
        return { loadFile, play, pause, seek, setRate, setOffset, setLoop, getTime,
            isPlaying: () => running, getDuration, getWaveform: () => waveform, dispose };
    }
    return { TimelineClock, chartToAudio, audioToChart, wrapLoop, buildWaveform, createTransport };
});
