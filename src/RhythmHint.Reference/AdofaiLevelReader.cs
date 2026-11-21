using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RhythmHint.Core;

namespace RhythmHint.Reference;

/// <summary>离线读出的一关：砖块时间（原曲秒数）、起始一拍的秒数、播放倍率，以及没处理的计时事件。</summary>
/// <param name="Floors">每块砖（第 0 块是起点），交给 <see cref="FloorHits.From"/> 得到要按的砖。</param>
/// <param name="Crotchet">起始 BPM 一拍的秒数。</param>
/// <param name="PlaybackRate">settings.pitch / 100：实际播放比原曲快多少倍。</param>
/// <param name="Unsupported">影响时间、但这里没算的事件（有的话离线时间不准，校准时跳过这一关）。</param>
public sealed record AdofaiLevel(List<FloorInfo> Floors, double Crotchet, double PlaybackRate, IReadOnlyCollection<string> Unsupported)
{
    /// <summary>要按的砖（和游戏里的 Mod 同一套 FloorHits 规则）。</summary>
    public List<TileHit> Hits() => FloorHits.From(Floors, Crotchet);
}

/// <summary>
/// 宽松地读 .adofai，给难度校准这类「大量外部谱面、允许个别不准」的用途：
/// 算 SetSpeed、Twirl、Pause（含游戏对 9–16 版暂停的迁移）、Hold、AutoPlayTiles 和中旋，记下双押砖（Multitap），其余事件不影响时间的忽略，
/// 影响时间但没算的（多星球、自由区、带 angleOffset 的变速等）记进 Unsupported，由调用方决定要不要这一关。
/// 游戏里以游戏算出的砖块时间为准，这里只做离线近似。
/// </summary>
public static class AdofaiLevelReader
{
    /// <summary>pathData 的绝对方向字母（度）；'!' 是中旋。</summary>
    private static readonly Dictionary<char, double> PathAngles = new()
    {
        ['R'] = 0,
        ['p'] = 15,
        ['J'] = 30,
        ['E'] = 45,
        ['T'] = 60,
        ['o'] = 75,
        ['U'] = 90,
        ['q'] = 105,
        ['G'] = 120,
        ['Q'] = 135,
        ['H'] = 150,
        ['W'] = 165,
        ['L'] = 180,
        ['x'] = 195,
        ['N'] = 210,
        ['Z'] = 225,
        ['F'] = 240,
        ['V'] = 255,
        ['D'] = 270,
        ['Y'] = 285,
        ['B'] = 300,
        ['C'] = 315,
        ['M'] = 330,
        ['A'] = 345,
        ['!'] = 999,
    };

    /// <summary>新版 pathData 的相对方向字母（相对上一块的转角，度）。</summary>
    private static readonly Dictionary<char, double> RelativeAngles = new()
    {
        ['5'] = 72,
        ['6'] = -72,
        ['7'] = 52,
        ['8'] = -52,
        ['9'] = -30,
        ['h'] = 120,
        ['j'] = -120,
        ['t'] = 60,
        ['y'] = 300,
    };

    /// <summary>影响砖块时间、但这里没有模拟的事件。</summary>
    private static readonly HashSet<string> TimingEvents = new(StringComparer.Ordinal)
    {
        "MultiPlanet", "FreeRoam", "FreeRoamTwirl", "FreeRoamRemove", "FreeRoamWarning", "ScaleMargin",
    };

    public static AdofaiLevel Parse(string text)
    {
        var root = ParseJson(text);
        var unsupported = new SortedSet<string>(StringComparer.Ordinal);
        var angles = root["angleData"] is JArray angleData
            ? ReadAngles(angleData, unsupported)
            : ReadPathAngles(root, unsupported);
        if (angles.Length == 0)
        {
            throw new FormatException("关卡没有 angleData 或 pathData。");
        }

        var settings = root["settings"] as JObject ?? new JObject();
        var bpm = (double?)settings["bpm"] ?? 0;
        if (!(bpm > 0))
        {
            throw new FormatException("关卡没有有效的 bpm。");
        }

        var pitch = (double?)settings["pitch"] ?? 100;
        var version = (double?)settings["version"] ?? 0;
        var events = new Dictionary<int, List<JObject>>();
        foreach (var e in (root["actions"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var floor = (int?)e["floor"] ?? -1;
            if (floor < 0 || floor > angles.Length)
            {
                continue;
            }

            if (!events.TryGetValue(floor, out var list))
            {
                events[floor] = list = new List<JObject>();
            }

            list.Add(e);
        }

        var corrections = LegacyPauseCorrections(angles, events, version);
        var floors = new List<FloorInfo>(angles.Length + 1);
        var time = 0.0;
        var clockwise = true;
        var current = bpm;
        var lastDir = 0.0;
        var auto = false;
        for (var k = 0; k < angles.Length; k++)
        {
            var extraBeats = 0.0;
            var hold = false;
            var taps = 1;
            foreach (var e in Events(events, k))
            {
                var kind = (string?)e["eventType"] ?? "";
                switch (kind)
                {
                    case "Twirl":
                        clockwise = !clockwise;
                        break;
                    case "SetSpeed":
                        if (((double?)e["angleOffset"] ?? 0) != 0)
                        {
                            unsupported.Add("SetSpeed.angleOffset");
                        }

                        var multiplier = (string?)e["speedType"] == "Multiplier";
                        var value = (double?)e[multiplier ? "bpmMultiplier" : "beatsPerMinute"] ?? 0;
                        if (value > 0)
                        {
                            current = multiplier ? current * value : value;
                        }

                        break;
                    case "Pause":
                        var duration = (double?)e["duration"] ?? 0;
                        if (corrections[k] != 0)
                        {
                            duration = version == 9 ? duration + corrections[k] : Math.Max(0, duration + corrections[k]);
                        }

                        extraBeats += duration;
                        break;
                    case "Hold":
                        // 长按多转几整圈：每圈 360° = 两拍
                        extraBeats += 2 * ((double?)e["duration"] ?? 0);
                        hold = true;
                        break;
                    case "AutoPlayTiles":
                        auto = Flag(e["enabled"]);
                        break;
                    case "Multitap":
                        // 双押砖：落到这块砖要按 taps 下（游戏 scnGame 读成 scrFloor.tapsNeeded），不影响时间
                        taps = (int?)e["taps"] ?? 1;
                        break;
                    default:
                        if (TimingEvents.Contains(kind))
                        {
                            unsupported.Add(kind);
                        }

                        break;
                }
            }

            if (angles[k] == 999)
            {
                // 中旋：原地转 180°，不花时间；这块砖不用按（游戏里 midSpin 当自动砖）
                floors.Add(new FloorInfo(k, time, false, true, 0, current / bpm));
                lastDir = (lastDir + 180) % 360;
                continue;
            }

            var entry = (k > 0 ? lastDir : 0) + 180;
            var swept = clockwise ? Mod(entry - angles[k]) : Mod(angles[k] - entry);
            if (swept < 1e-6)
            {
                swept = 360;
            }

            floors.Add(new FloorInfo(k, time, hold, k > 0 && auto, swept + (extraBeats * 180), current / bpm, taps));
            time += ((swept / 180) + extraBeats) * 60 / current;
            lastDir = angles[k];
        }

        // angleData 有 N 项就有 N+1 块砖，最后一块（第 N 块）的到达也要按一下
        var lastTaps = Events(events, angles.Length).LastOrDefault(e => (string?)e["eventType"] == "Multitap")?["taps"];
        floors.Add(new FloorInfo(angles.Length, time, false, auto, Taps: (int?)lastTaps ?? 1));
        return new AdofaiLevel(floors, 60 / bpm, pitch > 0 ? pitch / 100 : 1, unsupported);
    }

    private static IEnumerable<JObject> Events(Dictionary<int, List<JObject>> events, int floor) =>
        events.TryGetValue(floor, out var list) ? list : Enumerable.Empty<JObject>();

    /// <summary>angleData；个别谱里夹着 null（游戏怎么处理不确定），按沿用上一块的方向读，并记为时间不准。</summary>
    private static double[] ReadAngles(JArray angleData, ISet<string> unsupported)
    {
        var result = new double[angleData.Count];
        var previous = 0.0;
        for (var i = 0; i < result.Length; i++)
        {
            if (angleData[i].Type is JTokenType.Integer or JTokenType.Float)
            {
                previous = (double)angleData[i];
            }
            else
            {
                unsupported.Add("angleData null");
            }

            result[i] = previous;
        }

        return result;
    }

    private static double[] ReadPathAngles(JObject root, ISet<string> unsupported)
    {
        var path = root["pathData"]?.ToString() ?? "";
        var modern = (double?)root["settings"]?["version"] >= 5 && (bool?)root["settings"]?["legacySpriteTiles"] != true;
        var result = new List<double>(path.Length);
        var previous = 0.0;
        foreach (var c in path)
        {
            if (PathAngles.TryGetValue(c, out var absolute))
            {
                previous = absolute;
            }
            else if (modern && RelativeAngles.TryGetValue(c, out var delta))
            {
                previous = (float)(previous + delta);
            }
            else
            {
                unsupported.Add($"pathData '{c}'");
            }

            result.Add(previous);
        }

        return result.ToArray();
    }

    /// <summary>游戏对 9–16 版关卡暂停时长的迁移（原地折返的砖多算或少算一拍）。</summary>
    private static int[] LegacyPauseCorrections(double[] angles, Dictionary<int, List<JObject>> events, double version)
    {
        var corrections = new int[angles.Length];
        if (version < 9 || version > 16)
        {
            return corrections;
        }

        const double pi = 3.1415927410125732, tau = 6.2831854820251465;
        const float radiansPerDegree = (float)pi / 180f;
        var entry = 4.71238899230957;
        var clockwise = true;
        for (var k = 0; k < angles.Length; k++)
        {
            var angle = (float)angles[k];
            var midspin = angle == 999f;
            var exit = midspin ? (double)(float)entry : (double)((0f - angle + 90f) * radiansPerDegree);
            var raw = ((((exit - entry) * (clockwise ? 1 : -1)) % tau) + tau) % tau;
            var turnaround = !midspin && (Math.Abs(raw) <= 1e-6 || Math.Abs(raw) >= 6.283184482025146);
            if (version == 9)
            {
                corrections[k] = turnaround ? 1 : 0;
            }
            else if (turnaround ^ (Math.Abs(raw - tau) < .0001))
            {
                corrections[k] = turnaround ? -1 : 1;
            }

            entry = (exit + pi) % tau;
            foreach (var e in Events(events, k + 1))
            {
                if ((string?)e["eventType"] == "Twirl")
                {
                    clockwise = !clockwise;
                }
            }
        }

        return corrections;
    }

    /// <summary>
    /// Newtonsoft 本来就接受注释、尾逗号；有些老谱还会在数组属性后漏写逗号，或在对象属性之间写两个逗号。
    /// 先按标准解析，失败了再在字符串外面补上这两种边界。
    /// </summary>
    public static JObject ParseJson(string text)
    {
        text = text.TrimStart('﻿');
        try
        {
            return JObject.Parse(text);
        }
        catch (JsonReaderException)
        {
            // 预料之中的老谱格式问题：下面修补后再解析一次，再失败就把异常抛给调用方
        }

        var output = new StringBuilder(text.Length + 64);
        var containers = new Stack<char>();
        bool quoted = false, escaped = false, lineComment = false, blockComment = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            output.Append(c);
            if (lineComment)
            {
                lineComment = c is not ('\r' or '\n');
                continue;
            }

            if (blockComment)
            {
                if (c == '*' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    output.Append(text[++i]);
                    blockComment = false;
                }

                continue;
            }

            if (quoted)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    quoted = false;
                }

                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] is '/' or '*')
            {
                lineComment = text[i + 1] == '/';
                blockComment = !lineComment;
                output.Append(text[++i]);
                continue;
            }

            if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',' && containers.Count > 0 && containers.Peek() == '{')
            {
                // 对象属性之间写了两个逗号（如 "requiredMods": [],,）
                var next = SkipSpace(text, i + 1);
                if (next < text.Length && text[next] == ',')
                {
                    i = next;
                }
            }
            else if (c is '[' or '{')
            {
                containers.Push(c);
            }
            else if (c is ']' or '}')
            {
                var opened = containers.Count > 0 ? containers.Pop() : '\0';
                if (c == ']' && opened == '[' && containers.Count > 0 && containers.Peek() == '{' && NextIsKey(text, i + 1))
                {
                    // 数组属性后面漏了逗号，下一个属性直接开始
                    output.Append(',');
                }
                else if (c == '}' && opened == '{' && containers.Count > 0 && containers.Peek() == '['
                    && SkipSpace(text, i + 1) < text.Length && text[SkipSpace(text, i + 1)] == '{')
                {
                    // 数组里两个对象之间漏了逗号（如 actions 里的 "} {"）
                    output.Append(',');
                }
            }
        }

        return JObject.Parse(output.ToString());
    }

    private static int SkipSpace(string text, int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        return i;
    }

    /// <summary>从 i 开始（跳过空白）是不是「"键":」。</summary>
    private static bool NextIsKey(string text, int i)
    {
        i = SkipSpace(text, i);
        if (i >= text.Length || text[i++] != '"')
        {
            return false;
        }

        var escaped = false;
        while (i < text.Length)
        {
            var c = text[i++];
            if (escaped)
            {
                escaped = false;
            }
            else if (c == '\\')
            {
                escaped = true;
            }
            else if (c == '"')
            {
                break;
            }
        }

        i = SkipSpace(text, i);
        return i < text.Length && text[i] == ':';
    }

    private static bool Flag(JToken? value) => value?.Type == JTokenType.Boolean
        ? (bool)value : string.Equals((string?)value, "Enabled", StringComparison.OrdinalIgnoreCase);

    private static double Mod(double x) => ((x % 360) + 360) % 360;
}
