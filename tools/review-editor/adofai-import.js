/* Offline .adofai import: tools/Preview.cs AdofaiTiming + Core/FloorHits, never the full game event engine. */
(function (root, factory) {
    if (typeof module === 'object' && module.exports) module.exports = factory(require('./note-planner.js'), require('./core.js'));
    else root.AdofaiImport = factory(root.NotePlanner, root.ReviewCore);
})(typeof globalThis !== 'undefined' ? globalThis : this, function (planner, core) {
    'use strict';
    const VERSION = 'RhythmHint/adofai-import/js-v2-054fe9c';
    const gameHint = '请在游戏中 F8 → 状态 → 导出当前关卡排键，再导入审核 JSON。';
    const presentationEvents = new Set([
        'AddDecoration', 'AddText', 'AddObject', 'AddParticle', 'AnimateTrack', 'Bloom', 'Bookmark',
        'Checkpoint', 'ColorTrack', 'CustomBackground', 'EditorComment', 'EmitParticle', 'Flash',
        'HallOfMirrors', 'Hide', 'MoveCamera', 'MoveDecorations', 'MoveTrack', 'PlaySound', 'PositionTrack',
        'RecolorTrack', 'ScalePlanets', 'ScreenScroll', 'ScreenTile', 'SetDefaultText', 'SetFilter',
        'SetFilterAdvanced', 'SetFrameRate', 'SetHitsound', 'SetObject', 'SetParticle', 'SetPlanetRotation',
        'SetText', 'ShakeScreen', 'RepeatEvents', 'SetConditionalEvents', 'ScaleRadius'
    ]);
    const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
    const has = (value, name) => Object.prototype.hasOwnProperty.call(value, name);
    function fail(message) { throw new Error(message + ' ' + gameHint); }
    function number(value, label) {
        if (typeof value !== 'number' || !Number.isFinite(value)) fail(label + ' 必须是有限数值。');
        return value;
    }
    function positive(value, label) {
        number(value, label);
        if (value <= 0) fail(label + ' 必须大于零。');
        return value;
    }

    // The game's GDMiniJSON parser accepts raw control characters inside strings and
    // omitted object separators. Normalize the observed array-to-property separator
    // case, comments and trailing commas; JSON.parse still checks all other grammar.
    // String contents are preserved and chart content is never evaluated as code.
    function parseJson(text) {
        if (typeof text !== 'string') fail('谱面内容必须是文本。');
        const input = text.replace(/^\uFEFF+/, '');
        // Most exported charts are strict JSON. Avoid allocating normalization buffers
        // for those files, especially maps with many megabytes of decorations.
        try { return JSON.parse(input); } catch (_) { /* Normalize the game's relaxed syntax below. */ }
        // Keep unchanged regions as slices, and coalesce a bounded number of changed
        // fragments at a time. Per-character concatenation creates millions of V8 rope
        // nodes; even an ordinary 50 MiB chart can then exhaust a 1 GiB heap.
        function edits(source) {
            const blocks = [];
            let parts = [], pendingSize = 0, last = 0, changed = false;
            function append(value) {
                if (!value) return;
                parts.push(value); pendingSize += value.length;
                if (parts.length >= 256 || pendingSize >= 65536) {
                    blocks.push(parts.join('')); parts = []; pendingSize = 0;
                }
            }
            return {
                replace(start, end, value) {
                    append(source.slice(last, start)); append(value);
                    last = end; changed = true;
                },
                finish() {
                    if (!changed) return source;
                    append(source.slice(last));
                    if (parts.length) blocks.push(parts.join(''));
                    return blocks.join('');
                }
            };
        }
        const stringsAndComments = edits(input);
        let quoted = false, escaped = false;
        for (let i = 0; i < input.length; i++) {
            const ch = input[i];
            if (quoted) {
                if (!escaped && ch.charCodeAt(0) < 0x20)
                    stringsAndComments.replace(i, i + 1, '\\u' + ch.charCodeAt(0).toString(16).padStart(4, '0'));
                if (escaped) escaped = false;
                else if (ch === '\\') escaped = true;
                else if (ch === '"') quoted = false;
            } else if (ch === '"') quoted = true;
            else if (ch === '/' && input[i + 1] === '/') {
                let end = i + 2;
                while (end < input.length && input[end] !== '\n' && input[end] !== '\r') end++;
                stringsAndComments.replace(i, end, ' '); i = end - 1;
            } else if (ch === '/' && input[i + 1] === '*') {
                const end = input.indexOf('*/', i + 2);
                if (end < 0) fail('谱面中有未结束的块注释。');
                // Retain line breaks and a separator so comments cannot join tokens.
                stringsAndComments.replace(i, end + 2, input.slice(i, end + 2).replace(/[^\r\n]+/g, ' '));
                i = end + 1;
            }
        }
        const cleaned = stringsAndComments.finish();
        if (cleaned !== input) {
            try { return JSON.parse(cleaned); } catch (_) { /* Check legacy separators next. */ }
        }
        const separators = edits(cleaned);
        quoted = false; escaped = false;
        const containers = [];
        for (let i = 0; i < cleaned.length; i++) {
            const ch = cleaned[i];
            if (quoted) {
                if (escaped) escaped = false;
                else if (ch === '\\') escaped = true;
                else if (ch === '"') quoted = false;
            } else if (ch === '"') quoted = true;
            else if (ch === '[' || ch === '{') containers.push(ch);
            else if (ch === ']' || ch === '}') {
                const opened = containers.pop();
                // Older .adofai files omit the comma between actions and decorations.
                // Require an actual array boundary in an object and a quoted key + ':';
                // never rewrite matching-looking text inside strings or array elements.
                if (ch === ']' && opened === '[' && containers[containers.length - 1] === '{') {
                    let next = i + 1;
                    while (next < cleaned.length && /\s/.test(cleaned[next])) next++;
                    if (cleaned[next] === '"') {
                        next++;
                        let keyEscaped = false;
                        while (next < cleaned.length) {
                            const keyChar = cleaned[next++];
                            if (keyEscaped) keyEscaped = false;
                            else if (keyChar === '\\') keyEscaped = true;
                            else if (keyChar === '"') break;
                        }
                        while (next < cleaned.length && /\s/.test(cleaned[next])) next++;
                        if (cleaned[next] === ':') separators.replace(i + 1, i + 1, ',');
                    }
                }
            }
            else if (ch === ',') {
                let next = i + 1;
                while (next < cleaned.length && /\s/.test(cleaned[next])) next++;
                if (cleaned[next] === ']' || cleaned[next] === '}') separators.replace(i, i + 1, '');
            }
        }
        try { return JSON.parse(separators.finish()); }
        catch (error) { fail('无法读取谱面 JSON：' + error.message); }
    }

    // FloorHelper.TryGetPathInDegrees / MigratePathData. Old sprite paths use a
    // different relative-angle language, so only their verified absolute subset is used.
    function readAngles(root, settings) {
        if (has(root, 'angleData')) {
            if (!Array.isArray(root.angleData) || !root.angleData.length) fail('angleData 必须是非空数组。');
            if (root.angleData.length > 200000) fail('谱面超过本地编辑器支持的 200000 个角度。');
            return root.angleData.map((value, index) => number(value, 'angleData[' + index + ']'));
        }
        if (typeof root.pathData !== 'string' || !root.pathData.length) fail('谱面需要 angleData 或非空 pathData。');
        if (root.pathData.length > 200000) fail('谱面超过本地编辑器支持的 200000 个角度。');
        const absolute = { R:0,E:45,U:90,Q:135,L:180,Z:225,D:270,C:315,B:300,T:60,G:120,F:240,J:30,H:150,N:210,M:330,p:15,o:75,q:105,W:165,x:195,V:255,Y:285,A:345,'!':999 };
        const relative = { '5':72,'6':-72,'7':52,'8':-52,'9':-30,h:120,j:-120,t:60,y:300 };
        const modern = typeof settings.version === 'number' && settings.version >= 5 && settings.legacySpriteTiles !== true;
        const angles = []; let previous = 0;
        for (const [index, char] of Array.from(root.pathData).entries()) {
            if (has(absolute, char)) previous = absolute[char];
            else if (modern && has(relative, char)) previous = Math.fround(previous + relative[char]);
            else fail('暂不支持 pathData 第 ' + (index + 1) + ' 个字符「' + char + '」的' + (modern ? '' : '旧版') + '轨道语义。');
            angles.push(previous);
        }
        return angles;
    }

    // LevelData.Decode migrates Pause durations in versions 9–16. Preserve the
    // game's float radians: a near-2π result and a near-zero result migrate differently.
    function legacyPauseCorrections(angles, events, version) {
        const corrections = new Int8Array(angles.length);
        if (version < 9 || version > 16) return corrections;
        const pi = 3.1415927410125732, tau = 6.2831854820251465;
        const radiansPerDegree = Math.fround(pi / 180);
        let entry = 4.71238899230957, clockwise = true;
        for (let k = 0; k < angles.length; k++) {
            const angle = Math.fround(angles[k]), midspin = angle === 999;
            const exit = midspin ? Math.fround(entry) : Math.fround(Math.fround(90 - angle) * radiansPerDegree);
            const raw = (((exit - entry) * (clockwise ? 1 : -1)) % tau + tau) % tau;
            const turnaround = !midspin && (Math.abs(raw) <= 1e-6 || Math.abs(raw) >= 6.283184482025146);
            if (version === 9) corrections[k] = turnaround ? 1 : 0;
            else if (turnaround !== (Math.abs(raw - tau) < .0001)) corrections[k] = turnaround ? -1 : 1;
            entry = (exit + pi) % tau;
            for (const event of events.get(k + 1) || []) if (event.eventType === 'Twirl') clockwise = !clockwise;
        }
        return corrections;
    }

    function parseTiming(text) {
        const root = parseJson(text);
        if (!object(root)) fail('谱面根节点必须是对象。');
        const settings = object(root.settings) ? root.settings : {};
        const angles = readAngles(root, settings);
        const bpm = positive(settings.bpm, 'settings.bpm');
        if (has(root, 'actions') && !Array.isArray(root.actions)) fail('actions 必须是数组。');
        // The game expands RepeatEvents/conditionals only after core floor times are
        // calculated, and only for ffxPlusBase effects; they do not repeat these core events.
        const events = new Map();
        for (const event of root.actions || []) {
            if (!object(event)) fail('actions 中的每个事件必须是对象。');
            const kind = typeof event.eventType === 'string' ? event.eventType : '';
            if (!['SetSpeed', 'Twirl', 'Pause'].includes(kind) && !presentationEvents.has(kind)) fail('直接导入不支持事件 ' + (kind || '(缺少 eventType)') + '。');
            if (!['SetSpeed', 'Twirl', 'Pause'].includes(kind)) continue;
            const floor = number(event.floor, '事件 floor');
            if (!Number.isInteger(floor) || floor < 0 || floor > angles.length) fail('时序事件 floor 必须是有效的整数砖块序号。');
            if (has(event, 'angleOffset') && number(event.angleOffset, 'angleOffset') !== 0) fail('直接导入不支持 ' + kind + ' 的非零 angleOffset。');
            if ((kind !== 'Pause' && has(event, 'duration') && number(event.duration, 'duration') !== 0) || (event.eventTag != null && event.eventTag !== '')) fail('直接导入不支持带 duration/eventTag 的 ' + kind + '。');
            if (kind === 'Pause' && has(event, 'duration')) number(event.duration, 'Pause.duration');
            if (kind === 'SetSpeed') {
                if (event.speedType != null && event.speedType !== 'Multiplier' && event.speedType !== 'Bpm') fail('不支持的 SetSpeed.speedType：' + event.speedType + '。');
                positive(event[event.speedType === 'Multiplier' ? 'bpmMultiplier' : 'beatsPerMinute'], 'SetSpeed 速度');
            }
            if (!events.has(floor)) events.set(floor, []);
            events.get(floor).push(event);
        }
        const hasPause = Array.from(events.values()).some(group => group.some(event => event.eventType === 'Pause'));
        const version = hasPause && has(settings, 'version') ? number(settings.version, 'settings.version') : 0;
        const pauseCorrections = legacyPauseCorrections(angles, events, version);
        const floors = [], speeds = [];
        let time = 0, clockwise = true, current = bpm, lastDir = 0;
        const mod = value => ((value % 360) + 360) % 360;
        for (let k = 0; k < angles.length; k++) {
            let extraBeats = 0;
            for (const event of events.get(k) || []) {
                if (event.eventType === 'Twirl') clockwise = !clockwise;
                else if (event.eventType === 'Pause') {
                    let duration = event.duration ?? 0, correction = pauseCorrections[k];
                    if (correction) duration = version === 9 ? duration + correction : Math.max(0, duration + correction);
                    extraBeats += duration;
                }
                else {
                    current = event.speedType === 'Multiplier' ? current * event.bpmMultiplier : event.beatsPerMinute;
                    positive(current, '累计 BPM');
                }
            }
            const speed = positive(current / bpm, '累计速度倍率');
            if (!speeds.length || Math.abs(speeds[speeds.length - 1].speed - speed) > 1e-9) speeds.push({ time, speed });
            if (angles[k] === 999) {
                floors.push({ tileIndex: k, time, noPress: true, angle: 0, speed });
                lastDir = (lastDir + 180) % 360; continue;
            }
            const entry = (k > 0 ? lastDir : 0) + 180;
            let swept = clockwise ? mod(entry - angles[k]) : mod(angles[k] - entry);
            if (swept < 1e-6) swept = 360;
            floors.push({ tileIndex: k, time, noPress: false, angle: swept, speed });
            // Match upstream Preview's outgoing-floor approximation, including floor 0.
            // Game-exported timing remains authoritative for exact gameplay.
            const interval = (swept / 180 + extraBeats) * 60 / current;
            if (!Number.isFinite(interval) || interval < 0) fail('Pause 使第 ' + k + ' 块砖的出发间隔为负或非有限值。');
            time += interval;
            number(time, '累计时间'); lastDir = angles[k];
        }
        floors.push({ tileIndex: angles.length, time, noPress: false, angle: NaN, speed: 1 });
        const hits = floorHits(floors, 60 / bpm);
        return { hits, speeds, crotchet: 60 / bpm, settings, bpm };
    }

    // Core/FloorHits detects whether stored angles describe departure or arrival.
    // Keep unknown angles absent rather than fabricating a 0-degree pseudo chord.
    function floorHits(floors, crotchet) {
        function matchShare(arrival) {
            const ratios = [];
            for (let i = 0; i + 1 < floors.length; i++) {
                const f = floors[i], next = floors[i + 1], angle = arrival ? next.angle : f.angle;
                const gap = next.time - f.time;
                if (gap <= 0 || !(angle > 0) || !(f.speed > 0) || !(crotchet > 0)) continue;
                ratios.push(gap / (angle / 180 * crotchet / f.speed));
            }
            ratios.sort((a, b) => a - b);
            if (!ratios.length) return 0;
            const median = ratios[Math.floor(ratios.length / 2)];
            if (median < .5 || median > 2) return 0;
            return ratios.filter(ratio => Math.abs(ratio / median - 1) <= .03).length / ratios.length;
        }
        const exit = matchShare(false), arrival = matchShare(true);
        const source = Math.max(exit, arrival) < .5 ? 'none' : exit >= arrival ? 'exit' : 'arrival';
        const hits = []; let angle = 0;
        for (let i = 1; i < floors.length; i++) {
            angle += source === 'exit' ? floors[i - 1].angle : source === 'arrival' ? floors[i].angle : NaN;
            if (floors[i].noPress) continue;
            const hit = { tileIndex: floors[i].tileIndex, time: floors[i].time, holdSeconds: 0 };
            if (!Number.isNaN(angle)) hit.angle = angle;
            hits.push(hit); angle = 0;
        }
        return hits.sort((a, b) => a.time - b.time);
    }

    // Portable SHA-256 is used when file:// does not expose SubtleCrypto.
    function sha256Hex(text) {
        const bytes = new TextEncoder().encode(text), length = bytes.length;
        const padded = new Uint8Array(Math.ceil((length + 9) / 64) * 64);
        padded.set(bytes); padded[length] = 0x80;
        const data = new DataView(padded.buffer);
        data.setUint32(padded.length - 8, Math.floor(length / 0x20000000));
        data.setUint32(padded.length - 4, (length * 8) >>> 0);
        const constants = [0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2];
        const hash = [0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19], words = new Uint32Array(64);
        const rotate = (value, count) => (value >>> count) | (value << (32 - count));
        for (let offset = 0; offset < padded.length; offset += 64) {
            for (let i = 0; i < 16; i++) words[i] = data.getUint32(offset + i * 4);
            for (let i = 16; i < 64; i++) {
                const x = words[i - 15], y = words[i - 2];
                words[i] = (words[i - 16] + (rotate(x,7) ^ rotate(x,18) ^ (x >>> 3)) + words[i - 7] + (rotate(y,17) ^ rotate(y,19) ^ (y >>> 10))) >>> 0;
            }
            let [a,b,c,d,e,f,g,h] = hash;
            for (let i = 0; i < 64; i++) {
                const t1 = (h + (rotate(e,6) ^ rotate(e,11) ^ rotate(e,25)) + ((e & f) ^ (~e & g)) + constants[i] + words[i]) >>> 0;
                const t2 = ((rotate(a,2) ^ rotate(a,13) ^ rotate(a,22)) + ((a & b) ^ (a & c) ^ (b & c))) >>> 0;
                h=g; g=f; f=e; e=(d+t1)>>>0; d=c; c=b; b=a; a=(t1+t2)>>>0;
            }
            [a,b,c,d,e,f,g,h].forEach((value, i) => { hash[i] = (hash[i] + value) >>> 0; });
        }
        return hash.map(value => value.toString(16).padStart(8, '0')).join('');
    }
    async function sha256(text) {
        if (globalThis.crypto?.subtle) {
            try {
                const buffer = await globalThis.crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
                return Array.from(new Uint8Array(buffer), n => n.toString(16).padStart(2, '0')).join('');
            } catch (_) { /* Fall back to the identical local algorithm. */ }
        }
        return sha256Hex(text);
    }

    async function importText(text, fileName = '关卡.adofai', suppliedOptions = {}) {
        if (!planner?.plan || !core?.validate) throw new Error('排键模块尚未载入，请重新打开完整编辑器文件。');
        if (!object(suppliedOptions)) fail('排键参数必须是对象。');
        const defaults = typeof planner.defaults === 'function' ? planner.defaults() : planner.defaults;
        const merged = { ...defaults, ...suppliedOptions }, options = {};
        for (const key of Object.keys(defaults)) options[key] = merged[key];
        const timing = parseTiming(text), hits = timing.hits;
        const notes = planner.plan(hits, options).map(note => ({
            id: note.tileIndex + ':' + note.lastTileIndex + ':' + note.time,
            tileIndex: note.tileIndex, lastTileIndex: note.lastTileIndex, time: note.time,
            lanes: note.lanes.map(lane => ({ lane: lane.lane, holdSeconds: lane.holdSeconds }))
        }));
        const chartId = await sha256(VERSION + '\n' + JSON.stringify({ options, notes, sourceHits: hits }));
        const filename = String(fileName).split(/[\\/]/).pop() || '关卡.adofai';
        const settings = timing.settings;
        const sourceFile = { name: filename, bpm: timing.bpm,
            songFilename: typeof settings.songFilename === 'string' ? settings.songFilename : '',
            offset: has(settings, 'offset') ? number(settings.offset, 'settings.offset') : 0,
            pitch: has(settings, 'pitch') ? positive(settings.pitch, 'settings.pitch') : 100 };
        return core.validate({ format: 'rhythmhint-review', schemaVersion: 1, chartId,
            name: filename.replace(/\.adofai$/i, '') + '（离线近似）', source: 'offline-approximation',
            algorithm: planner.algorithmFor(options), importer: VERSION, exportedAt: new Date().toISOString(),
            options, keyLabels: ['D','F','J','K'], notes, sourceHits: hits, sourceFile,
            timing: { originSeconds: 0, speeds: timing.speeds },
            review: { status: 'draft', comment: '', edits: [] } });
    }
    return { importText, parseTiming, parseJson, sha256Hex, version: VERSION };
});
