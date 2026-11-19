const { test } = require('node:test');
const assert = require('node:assert/strict');
const { TimelineClock, chartToAudio, audioToChart, createTransport, buildWaveform } = require('./studio-audio.js');

test('virtual playback keeps position through pause, seek and rate changes', () => {
    let now = 0;
    const clock = new TimelineClock(() => now);
    clock.seek(-1); clock.play(); now = 2;
    assert.equal(clock.getTime(), 1);
    clock.setRate(0.5); now = 4;
    assert.equal(clock.getTime(), 2);
    clock.pause(); now = 40;
    assert.equal(clock.getTime(), 2);
    clock.seek(10); clock.play(); now = 42;
    assert.equal(clock.getTime(), 11);
});

test('A/B wraps overshoot without accumulating frame drift', () => {
    let now = 0;
    const clock = new TimelineClock(() => now);
    clock.setLoop(2, 4, true); clock.seek(3.5); clock.play(); now = 1;
    assert.equal(clock.getTime(), 2.5);
    now = 8;
    assert.equal(clock.getTime(), 3.5);
    clock.setLoop(2, 4, false); now = 10;
    assert.equal(clock.getTime(), 5.5);
    assert.throws(() => clock.setLoop(4, 2, true));
});

test('offset conversion is reversible including silent negative audio time', () => {
    assert.equal(chartToAudio(2, -3000), -1);
    assert.equal(audioToChart(1, -3000), 4);
    assert.equal(audioToChart(chartToAudio(-2.5, 125), 125), -2.5);
});

function deferred() {
    let resolve, reject;
    const promise = new Promise((a, b) => { resolve = a; reject = b; });
    return { promise, resolve, reject };
}
class FakeAudio {
    constructor() { this.listeners = new Map(); this.duration = 10; this.currentTime = 0; this.readyState = 0; this.paused = true; this.ended = false; this.playbackRate = 1; }
    addEventListener(type, fn) { const list = this.listeners.get(type) || new Set(); list.add(fn); this.listeners.set(type, list); }
    removeEventListener(type, fn) { this.listeners.get(type)?.delete(fn); }
    emit(type) { for (const fn of [...(this.listeners.get(type) || [])]) fn(); }
    play() { this.paused = false; return this.nextPlay || Promise.resolve(); }
    pause() { this.paused = true; }
    removeAttribute() {}
    load() {}
}
function environment(extra = {}) {
    let now = 0;
    const audios = [], revoked = [], errors = [];
    const transport = createTransport({ now: () => now, audioFactory: () => { const a = new FakeAudio(); audios.push(a); return a; },
        urlApi: { createObjectURL: () => 'blob:' + audios.length, revokeObjectURL: u => revoked.push(u) },
        onError: e => errors.push(e), ...extra });
    return { transport, audios, revoked, errors, advance: seconds => { now += seconds; },
        async load(name = 'song.ogg') { const promise = transport.loadFile({ name }); audios.at(-1).emit('loadedmetadata'); return promise; } };
}

test('without music transport still plays for chart preview', async () => {
    const e = environment();
    await e.transport.play(); e.advance(3);
    assert.equal(e.transport.getTime(), 3);
    e.transport.setRate(.5); e.advance(2);
    assert.equal(e.transport.getTime(), 4);
    e.transport.pause(); e.advance(10);
    assert.equal(e.transport.getTime(), 4);
    assert.equal(e.transport.getDuration(), Infinity);
});

test('music time is master, and seek/offset preserve intended chart position', async () => {
    const e = environment(); await e.load();
    e.transport.setOffset(500); e.transport.seek(2); await e.transport.play();
    assert.equal(e.audios[0].currentTime, 2.5);
    e.audios[0].currentTime = 3; e.advance(100);
    assert.equal(e.transport.getTime(), 2.5);
    e.transport.pause(); e.advance(100);
    assert.equal(e.transport.getTime(), 2.5);
    e.transport.setOffset(1000);
    assert.equal(e.transport.getTime(), 2.5);
    await e.transport.play();
    assert.equal(e.audios[0].currentTime, 3.5);
    assert.equal(e.transport.getDuration(), 9);
});

test('negative audio position runs silently then starts at offset boundary', async () => {
    const e = environment(); await e.load();
    e.transport.setOffset(-2000); await e.transport.play();
    assert.equal(e.audios[0].paused, true);
    e.advance(1); assert.equal(e.transport.getTime(), 1);
    e.advance(1.1); assert.equal(e.transport.getTime(), 2.1);
    await Promise.resolve();
    assert.equal(e.audios[0].paused, false);
    assert.ok(Math.abs(e.audios[0].currentTime - .1) < 1e-9);
    assert.ok(Math.abs(e.transport.getTime() - 2.1) < 1e-9);
});

test('pause wins over an in-flight audio play promise', async () => {
    const e = environment(); await e.load();
    const waiting = deferred(); e.audios[0].nextPlay = waiting.promise;
    const play = e.transport.play(); e.transport.pause();
    waiting.resolve(); await play;
    assert.equal(e.transport.isPlaying(), false);
    assert.equal(e.audios[0].paused, true);
});

test('new seek wins over an old pending play and exact audio loop seeks back', async () => {
    const e = environment(); await e.load();
    const old = deferred(); e.audios[0].nextPlay = old.promise;
    const play = e.transport.play();
    e.audios[0].nextPlay = null; e.transport.seek(4); await Promise.resolve();
    old.resolve(); await play;
    assert.equal(e.transport.getTime(), 4);
    assert.equal(e.transport.isPlaying(), true);
    e.transport.setLoop(2, 5, true); e.audios[0].currentTime = 5.2;
    assert.ok(Math.abs(e.transport.getTime() - 2.2) < 1e-9);
    await Promise.resolve();
    assert.ok(Math.abs(e.audios[0].currentTime - 2.2) < 1e-9);
});

test('replaced load cannot publish metadata and all object URLs are released', async () => {
    const e = environment();
    const first = e.transport.loadFile({ name: 'first.ogg' });
    const rejected = assert.rejects(first, { name: 'AbortError' });
    const second = e.transport.loadFile({ name: 'second.ogg' });
    e.audios[0].duration = 999; e.audios[0].emit('loadedmetadata');
    e.audios[1].duration = 25; e.audios[1].emit('loadedmetadata');
    assert.deepEqual(await second, { name: 'second.ogg', duration: 25 }); await rejected;
    assert.equal(e.transport.getDuration(), 25);
    e.transport.dispose(); assert.equal(e.revoked.length, 2);
});

test('decode-independent audio failure stops transport and reports error once', async () => {
    const e = environment(); await e.load();
    e.audios[0].nextPlay = Promise.reject(new Error('Playback denied'));
    await assert.rejects(e.transport.play(), /Playback denied/);
    assert.equal(e.transport.isPlaying(), false);
    assert.equal(e.errors.length, 1);
});

test('natural audio ending stops at chart time and waveform preserves both channels', async () => {
    const e = environment(); await e.load(); e.transport.setOffset(1000); await e.transport.play();
    e.audios[0].currentTime = 10; e.audios[0].ended = true; e.audios[0].emit('ended');
    assert.equal(e.transport.getTime(), 9); assert.equal(e.transport.isPlaying(), false);
    const wave = buildWaveform([new Float32Array([.1, -.5, .2, .1]), new Float32Array([.8, .2, -.7, .1])], 2, 2);
    assert.equal(wave.peaks.length, 2);
    assert.ok(Math.abs(wave.peaks[0] - .8) < 1e-6);
    assert.ok(Math.abs(wave.peaks[1] - .7) < 1e-6);
    assert.equal(buildWaveform([new Float32Array(10000)], 1, 3000).peaks.length, 1500);
});

test('stale waveform decode cannot replace the newly loaded song', async () => {
    const oldDecode = deferred(); let closed = 0;
    const e = environment({ audioContextFactory: () => ({ decodeAudioData: () => oldDecode.promise, close: async () => { closed++; } }) });
    const load = e.transport.loadFile({ name: 'old.ogg', arrayBuffer: async () => new ArrayBuffer(1) });
    e.audios[0].emit('loadedmetadata'); await load; await Promise.resolve();
    await e.load('new.ogg');
    oldDecode.resolve({ duration: 3, numberOfChannels: 1, getChannelData: () => new Float32Array([.5]) });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(e.transport.getWaveform(), null);
    assert.equal(closed, 1);
});

test('waveform decode failure does not prevent music playback', async () => {
    let closed = 0;
    const e = environment({ audioContextFactory: () => ({ decodeAudioData: async () => { throw new Error('No decoder'); }, close: async () => { closed++; } }) });
    const load = e.transport.loadFile({ name: 'song.ogg', arrayBuffer: async () => new ArrayBuffer(1) });
    e.audios[0].emit('loadedmetadata'); await load;
    await new Promise(resolve => setImmediate(resolve));
    await e.transport.play();
    assert.equal(e.transport.isPlaying(), true);
    assert.equal(e.errors.length, 1);
    assert.equal(closed, 1);
});
