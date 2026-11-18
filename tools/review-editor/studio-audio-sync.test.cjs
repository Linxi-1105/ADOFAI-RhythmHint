// Run: node --test tools/review-editor/studio-audio-sync.test.cjs
// Uses the actual exported transport. Only the media element is deterministic.
// HTML media semantics reproduced here:
// - currentTime reads the official playback position, which can lag the actual
//   playback position while script is running.
// - pause() synchronously updates the official position to the current one.
// - play() can be pending while media playback has already progressed.
// https://html.spec.whatwg.org/multipage/media.html#internal-pause-steps
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { createTransport } = require('./studio-audio.js');

function deferred() {
    let resolve, reject;
    const promise = new Promise((a, b) => { resolve = a; reject = b; });
    return { promise, resolve, reject };
}

class SpecMedia {
    constructor() {
        this.listeners = new Map();
        this.actual = 0;
        this.official = 0;
        this.duration = 120;
        this.readyState = 4;
        this.paused = true;
        this.ended = false;
        this.playbackRate = 1;
        this.seeking = false;
        this.seekWrites = [];
        this.pending = null;
        this.deferPlay = false;
    }
    get currentTime() { return this.official; }
    set currentTime(value) {
        this.seekWrites.push(value);
        this.actual = this.official = value;
    }
    addEventListener(type, listener) {
        const set = this.listeners.get(type) || new Set();
        set.add(listener); this.listeners.set(type, set);
    }
    removeEventListener(type, listener) { this.listeners.get(type)?.delete(listener); }
    play() {
        this.paused = false;
        if (!this.deferPlay) return Promise.resolve();
        this.pending = deferred();
        return this.pending.promise;
    }
    pause() {
        if (this.paused) return;
        this.paused = true;
        this.official = this.actual;
        if (this.pending) {
            const pending = this.pending; this.pending = null;
            queueMicrotask(() => pending.reject(new DOMException('Playback interrupted', 'AbortError')));
        }
    }
    advance(seconds, snapshotLag = 0) {
        assert.equal(this.paused, false);
        this.actual += seconds * this.playbackRate;
        this.official = this.actual - snapshotLag;
    }
    removeAttribute() {}
    load() {}
}

async function fixture() {
    const media = new SpecMedia();
    let now = 0;
    const transport = createTransport({
        now: () => now,
        audioFactory: () => media,
        urlApi: { createObjectURL: () => 'blob:drift-repro', revokeObjectURL() {} },
        audioContextFactory: () => null
    });
    await transport.loadFile({ name: 'local-song.ogg' });
    return { transport, media, advance(seconds, lag = 0) { now += seconds; media.advance(seconds, lag); } };
}

function near(actual, expected, message) {
    assert.ok(Math.abs(actual - expected) < 1e-9, `${message}: expected ${expected}, got ${actual}`);
}

test('repeated pause/resume retains all played time instead of accumulating rewinds', async t => {
    const e = await fixture();
    e.transport.setOffset(275); e.transport.seek(5);
    const initial = 5, repeats = 12, playedPerCycle = .5, snapshotLag = .02;
    for (let i = 0; i < repeats; i++) {
        await e.transport.play();
        // The previous browser stable-state snapshot predates pause by 20 ms.
        e.advance(playedPerCycle, snapshotLag);
        e.transport.pause();
    }
    const actual = e.transport.getTime(), expected = initial + repeats * playedPerCycle;
    t.diagnostic(JSON.stringify({ repeats, expected, actual, lostSeconds: expected - actual, seekWrites: e.media.seekWrites.length }));
    near(actual, expected, 'Pause/resume lost already-played time');
});

test('pause captures media progress even while play promise is still pending', async t => {
    const e = await fixture();
    e.transport.seek(4); e.media.deferPlay = true;
    const play = e.transport.play();
    e.advance(.08); // playback progressed before the queued playing task settles.
    e.transport.pause();
    await play;
    t.diagnostic(JSON.stringify({ chart: e.transport.getTime(), media: e.media.currentTime }));
    near(e.transport.getTime(), 4.08, 'Pending play lost media progression');
    assert.equal(e.transport.isPlaying(), false);
});

test('resuming an unchanged paused position does not repeatedly seek the decoder', async t => {
    const e = await fixture();
    e.transport.seek(3);
    await e.transport.play();
    const initialWrites = e.media.seekWrites.length;
    for (let i = 0; i < 8; i++) {
        e.advance(.25);
        e.transport.pause();
        await e.transport.play();
    }
    t.diagnostic(JSON.stringify({ initialWrites, afterResumeWrites: e.media.seekWrites.length }));
    assert.equal(e.media.seekWrites.length, initialWrites,
        'Plain resume should continue the same paused media position, without currentTime assignment');
});

test('an explicit paused seek survives repeated pause, then repositions exactly once', async () => {
    const e = await fixture();
    await e.transport.play(); e.advance(1); e.transport.pause();
    e.transport.seek(12); e.transport.pause(); e.transport.pause();
    near(e.transport.getTime(), 12, 'Idle pause must retain the requested seek');
    const writes = e.media.seekWrites.length;
    await e.transport.play();
    near(e.media.currentTime, 12, 'Explicit seek must reach the media');
    assert.equal(e.media.seekWrites.length, writes + 1);
    e.transport.pause(); await e.transport.play();
    assert.equal(e.media.seekWrites.length, writes + 1);
});

test('changing offset while paused preserves chart position but requires one media seek', async () => {
    const e = await fixture();
    e.transport.setOffset(3000); e.transport.seek(2); await e.transport.play();
    e.advance(.5, .02); e.transport.pause();
    e.transport.setOffset(1250); e.transport.pause();
    near(e.transport.getTime(), 2.5, 'Offset change moved the chart');
    const writes = e.media.seekWrites.length;
    await e.transport.play();
    near(e.media.currentTime, 3.75, 'New offset was not applied');
    assert.equal(e.media.seekWrites.length, writes + 1);
});

test('200 pause/resume cycles across playback speeds keep all played time', async () => {
    const e = await fixture(); e.media.duration = 1000;
    e.transport.setOffset(-500); e.transport.seek(5);
    let expected = 5;
    for (let i = 0; i < 200; i++) {
        const rate = [.5, .75, 1, 1.25][i % 4];
        e.transport.setRate(rate); await e.transport.play();
        e.advance(.25, .02); expected += .25 * rate;
        e.transport.pause();
        near(e.transport.getTime(), expected, 'Cycle ' + i + ' lost played time');
    }
    assert.equal(e.media.seekWrites.length, 1);
});

test('pause during an asynchronous explicit seek retains the requested destination', async t => {
    const e = await fixture();
    e.media.actual = e.media.official = 4;
    // HTML seek steps: currentTime assignment changes official position, sets
    // seeking=true, then runs the actual-position update in parallel.
    // Until that update completes, pause() may refresh official from the OLD
    // current playback position. This must not cancel the explicit destination.
    Object.defineProperty(e.media, 'currentTime', {
        configurable: true,
        get() { return this.official; },
        set(value) {
            this.seekWrites.push(value); this.official = value;
            this.seeking = true; this.requestedTarget = value;
        }
    });
    e.transport.seek(12); e.media.deferPlay = true;
    const play = e.transport.play();
    assert.equal(e.media.seeking, true);
    e.transport.pause();
    await play;
    t.diagnostic(JSON.stringify({ requested: 12, chart: e.transport.getTime(), actualMedia: e.media.actual, seeking: e.media.seeking }));
    near(e.transport.getTime(), 12, 'Pausing an unresolved seek must preserve its destination');
});
