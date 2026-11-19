/* Offline .adofai timing reader: tools/Preview.cs AdofaiTiming + Core/FloorHits, never the full game event engine. */
(function (root, factory) {
    if (typeof module === 'object' && module.exports) module.exports = factory();
    else root.AdofaiImport = factory();
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    'use strict';
    const VERSION = 'RhythmHint/adofai-import/js-v2-054fe9c';
    const gameHint = '离线读谱只是近似，以游戏里的时序为准。';
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
    // case, duplicate object separators, comments and trailing commas. JSON.parse checks the remaining grammar.
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
                if (cleaned[next] === ']' || cleaned[next] === '}' || (cleaned[next] === ',' && containers[containers.length - 1] === '{')) {
                    separators.replace(i, i + 1, '');
                }
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
            if (root.angleData.length > 200000) fail('谱面超过离线读谱支持的 200000 个角度。');
            return root.angleData.map((value, index) => number(value, 'angleData[' + index + ']'));
        }
        if (typeof root.pathData !== 'string' || !root.pathData.length) fail('谱面需要 angleData 或非空 pathData。');
        if (root.pathData.length > 200000) fail('谱面超过离线读谱支持的 200000 个角度。');
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

    return { parseTiming, parseJson, version: VERSION };
});
