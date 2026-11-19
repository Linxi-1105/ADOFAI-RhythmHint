// 1.3.1 原样，不能改：git show v1.3.1:src/RhythmHint.Core/CustomFingering.cs，只把命名空间 RhythmHint.Core 换成 RhythmHint131.Core
// （1.3.0 的这个文件和 1.3.1 一字不差）。FingeringFormatLockTests 用它代表玩家手里 1.3.0 / 1.3.1 的 Mod：
// 现在写出的手序、编辑器存的手序，它都要读得进来、读出来的内容一样。读不进来就是破坏性更新，要改的是现在的代码，不是这里。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RhythmHint131.Core;

/// <summary>
/// 玩家给某首歌做的专属手序，一首歌一份文件，可以分享给别人。
/// 记下做手序时这一关的砖块（节奏）和每个音符按哪几个键；关卡更新以后靠节奏对齐找回对应的砖（见 <see cref="FingeringMatcher"/>），
/// 不要求文件和关卡完全一致。文件里不放音乐（版权）。
/// </summary>
public sealed class CustomFingering
{
    public const string Format = "rhythmhint-fingering";
    public const int SchemaVersion = 1;

    /// <summary>超过这个大小就不是正常的手序文件（四千个音符约 150 KB），不读。</summary>
    public const int MaxFileBytes = 20 * 1024 * 1024;

    private const int MaxHits = 200_000;
    private const int MaxNameLength = 300;
    private const int MaxCommentLength = 4000;

    /// <summary>曲名、曲师、谱师：显示用，也用来认「同一首歌」。</summary>
    public string Song { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string Charter { get; set; } = string.Empty;

    /// <summary>这份手序的作者（编辑器里填）。</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>作者写的说明。</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>几键。现在只有四键；以后有六键、八键时，键数不同的手序不套用。</summary>
    public int Keys { get; set; } = Lanes.Count;

    /// <summary>这份手序按哪只手为主手排的；玩家的惯用手不同时整体镜像。</summary>
    public bool LeftHanded { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 这份手序认得的谱面指纹（<see cref="ChartPrint"/>）：做手序时那一版，以及作者后来为更新版重新保存时的各版。
    /// 进关卡时指纹在这里就直接用，不用对齐。
    /// </summary>
    public List<string> Charts { get; } = new();

    /// <summary>做手序时这一关要按的砖，按时间排好。</summary>
    public List<TileHit> Hits { get; } = new();

    /// <summary>每个音符按哪几个键。音符按顺序、不重不漏地覆盖 <see cref="Hits"/>；时间取自它的第一块砖。</summary>
    public List<PlannedNote> Notes { get; } = new();

    /// <summary>从当前关卡的砖块和排好的音符生成一份手序（导出给编辑器、测试用）。</summary>
    public static CustomFingering From(IReadOnlyList<TileHit> hits, IReadOnlyList<PlannedNote> notes, bool leftHanded)
    {
        var result = new CustomFingering { LeftHanded = leftHanded };
        result.Hits.AddRange(hits);
        foreach (var note in notes)
        {
            result.Notes.Add(note.Clone());
        }

        Check(result);
        result.Charts.Add(ChartPrint(hits));
        return result;
    }

    /// <summary>
    /// 谱面指纹：每块要按的砖「和前一块的间隔（毫秒）、转角（度）、有没有长按」连起来取哈希。
    /// 和绝对时间、砖号、文件名、曲名都无关：改偏移、换文件夹、换电脑不变；谱面改了任何一块要按的砖就变。
    /// </summary>
    public static string ChartPrint(IReadOnlyList<TileHit> hits)
    {
        var sb = new StringBuilder(hits.Count * 12);
        for (var i = 0; i < hits.Count; i++)
        {
            var gap = i == 0 ? 0 : Math.Round((hits[i].Time - hits[i - 1].Time) * 1000);
            var angle = double.IsNaN(hits[i].Angle) ? "-" : Math.Round(hits[i].Angle).ToString(CultureInfo.InvariantCulture);
            sb.Append(gap.ToString(CultureInfo.InvariantCulture)).Append(',').Append(angle).Append(',').Append(hits[i].HoldSeconds > 0 ? '1' : '0').Append(';');
        }

        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return hits.Count.ToString(CultureInfo.InvariantCulture) + "-" + BitConverter.ToString(bytes, 0, 12).Replace("-", string.Empty).ToLowerInvariant();
    }

    /// <summary>建议的文件名：「曲师 - 曲名.json」，去掉富文本标签和文件名里不能用的字符。</summary>
    public string SuggestedFileName()
    {
        var song = PlainText(Song);
        var artist = PlainText(Artist);
        var name = artist.Length > 0 && song.Length > 0 ? $"{artist} - {song}" : song.Length > 0 ? song : "fingering";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        var safe = sb.ToString().Trim().TrimEnd('.');
        return (safe.Length > 80 ? safe.Substring(0, 80) : safe) + ".json";
    }

    /// <summary>去掉游戏曲名里的富文本标签（&lt;color=…&gt; 之类）和首尾空白。</summary>
    public static string PlainText(string text) => Regex.Replace(text ?? string.Empty, "<[^>]*>", string.Empty).Trim();

    /// <summary>认「同一首歌」用：去掉标签、空白，不分大小写。</summary>
    public static string SongKey(string song, string artist) =>
        Regex.Replace(PlainText(artist) + "|" + PlainText(song), @"\s+", string.Empty).ToLowerInvariant();

    public string ToJson()
    {
        Check(this);
        var sw = new StringWriter(CultureInfo.InvariantCulture);
        using (var w = new JsonTextWriter(sw) { Formatting = Formatting.None })
        {
            w.WriteStartObject();
            WriteHeader(w);
            WriteHitsAndNotes(w, Hits, Notes);
            w.WriteEndObject();
        }

        return sw.ToString();
    }

    /// <summary>格式、版本、作者等字段（编辑器会话也用这一段）。</summary>
    internal void WriteHeader(JsonWriter w)
    {
        w.WritePropertyName("format");
        w.WriteValue(Format);
        w.WritePropertyName("schemaVersion");
        w.WriteValue(SchemaVersion);
        w.WritePropertyName("keys");
        w.WriteValue(Keys);
        Text(w, "song", Song);
        Text(w, "artist", Artist);
        Text(w, "charter", Charter);
        Text(w, "author", Author);
        Text(w, "comment", Comment);
        w.WritePropertyName("leftHanded");
        w.WriteValue(LeftHanded);
        w.WritePropertyName("charts");
        w.WriteStartArray();
        foreach (var chart in Charts)
        {
            w.WriteValue(chart);
        }

        w.WriteEndArray();
        w.WritePropertyName("updatedAt");
        w.WriteValue(UpdatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    }

    /// <summary>hits：[砖号, 时间, 长按, 转角或 null]；notes：[第一块砖号, 最后一块砖号, [[轨道, 长按], …]]。一项一行，方便看 diff。</summary>
    internal static void WriteHitsAndNotes(JsonWriter w, IReadOnlyList<TileHit> hits, IReadOnlyList<PlannedNote> notes)
    {
        w.WritePropertyName("hits");
        w.WriteStartArray();
        foreach (var h in hits)
        {
            w.WriteWhitespace("\n");
            w.WriteStartArray();
            w.WriteValue(h.TileIndex);
            w.WriteValue(Round(h.Time));
            w.WriteValue(Round(h.HoldSeconds));
            if (double.IsNaN(h.Angle))
            {
                w.WriteNull();
            }
            else
            {
                w.WriteValue(Math.Round(h.Angle, 3));
            }

            w.WriteEndArray();
        }

        w.WriteEndArray();
        WriteNotes(w, "notes", notes);
    }

    internal static void WriteNotes(JsonWriter w, string property, IReadOnlyList<PlannedNote> notes)
    {
        w.WritePropertyName(property);
        w.WriteStartArray();
        foreach (var n in notes)
        {
            w.WriteWhitespace("\n");
            w.WriteStartArray();
            w.WriteValue(n.TileIndex);
            w.WriteValue(n.LastTileIndex);
            w.WriteStartArray();
            foreach (var kv in n.Lanes)
            {
                w.WriteStartArray();
                w.WriteValue(kv.Key);
                w.WriteValue(Round(kv.Value));
                w.WriteEndArray();
            }

            w.WriteEndArray();
            w.WriteEndArray();
        }

        w.WriteEndArray();
    }

    /// <summary>读一份手序文件。格式不对时抛 <see cref="FormatException"/>，消息说明哪里不对（写日志用）。</summary>
    public static CustomFingering Parse(string json)
    {
        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("不是有效的 JSON：" + ex.Message, ex);
        }

        if ((string?)root["format"] != Format)
        {
            throw new FormatException("不是 RhythmHint 专属手序文件（format 不是 " + Format + "）");
        }

        var version = Int(root["schemaVersion"], "schemaVersion");
        if (version > SchemaVersion)
        {
            throw new FormatException($"文件版本 {version} 比这个 Mod 支持的 {SchemaVersion} 新，请更新 Mod");
        }

        if (version < 1)
        {
            throw new FormatException("schemaVersion 无效");
        }

        var result = new CustomFingering
        {
            Keys = root["keys"] == null ? Lanes.Count : Int(root["keys"], "keys"),
            Song = Str(root, "song", MaxNameLength),
            Artist = Str(root, "artist", MaxNameLength),
            Charter = Str(root, "charter", MaxNameLength),
            Author = Str(root, "author", MaxNameLength),
            Comment = Str(root, "comment", MaxCommentLength),
            LeftHanded = root["leftHanded"]?.Type == JTokenType.Boolean && (bool)root["leftHanded"]!,
            UpdatedAt = DateTimeOffset.TryParse((string?)root["updatedAt"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                ? at : DateTimeOffset.MinValue,
        };
        if (result.Keys < 1 || result.Keys > 16)
        {
            throw new FormatException("keys 必须是 1–16");
        }

        if (root["hits"] is not JArray hits || hits.Count == 0 || hits.Count > MaxHits)
        {
            throw new FormatException($"hits 必须是 1–{MaxHits} 项的数组");
        }

        foreach (var item in hits)
        {
            if (item is not JArray h || h.Count < 3)
            {
                throw new FormatException("hits 的每一项必须是 [砖号, 时间, 长按, 转角]");
            }

            var angle = h.Count > 3 && h[3].Type != JTokenType.Null ? Num(h[3], "转角") : double.NaN;
            result.Hits.Add(new TileHit(Int(h[0], "砖号"), Num(h[1], "时间"), Num(h[2], "长按"), angle));
        }

        if (root["notes"] is not JArray notes || notes.Count == 0)
        {
            throw new FormatException("notes 必须是非空数组");
        }

        var timeOf = new Dictionary<int, double>(result.Hits.Count);
        foreach (var h in result.Hits)
        {
            if (timeOf.ContainsKey(h.TileIndex))
            {
                throw new FormatException($"hits 里砖号 {h.TileIndex} 重复");
            }

            timeOf[h.TileIndex] = h.Time;
        }

        foreach (var item in notes)
        {
            if (item is not JArray n || n.Count != 3 || n[2] is not JArray lanes)
            {
                throw new FormatException("notes 的每一项必须是 [第一块砖号, 最后一块砖号, [[轨道, 长按], …]]");
            }

            var first = Int(n[0], "砖号");
            if (!timeOf.TryGetValue(first, out var time))
            {
                throw new FormatException($"notes 里的砖号 {first} 不在 hits 里");
            }

            var note = new PlannedNote(first, time) { LastTileIndex = Int(n[1], "砖号") };
            foreach (var laneItem in lanes)
            {
                if (laneItem is not JArray pair || pair.Count != 2)
                {
                    throw new FormatException("轨道必须写成 [轨道, 长按]");
                }

                var lane = Int(pair[0], "轨道");
                if (lane < 0 || lane >= result.Keys || note.Lanes.ContainsKey(lane))
                {
                    throw new FormatException($"砖 {first} 的轨道 {lane} 超出范围或重复");
                }

                note.Lanes[lane] = Num(pair[1], "长按");
            }

            result.Notes.Add(note);
        }

        Check(result);
        if (root["charts"] is JArray charts)
        {
            foreach (var chart in charts)
            {
                if (chart.Type == JTokenType.String && ((string)chart!).Length <= 100 && !result.Charts.Contains((string)chart!))
                {
                    result.Charts.Add((string)chart!);
                }
            }
        }

        // 旧文件没有 charts：至少认得它自己记下的那一版
        var own = ChartPrint(result.Hits);
        if (!result.Charts.Contains(own))
        {
            result.Charts.Add(own);
        }

        return result;
    }

    /// <summary>砖块按时间排好、不重复；音符按顺序、不重不漏地覆盖全部砖块；每个音符 1 到键数个键。</summary>
    private static void Check(CustomFingering f)
    {
        if (f.Hits.Count == 0 || f.Notes.Count == 0)
        {
            throw new FormatException("手序没有砖块或音符");
        }

        var lastTile = int.MinValue;
        var lastTime = double.NegativeInfinity;
        foreach (var h in f.Hits)
        {
            if (h.TileIndex <= lastTile || h.Time < lastTime || !IsFinite(h.Time) || !IsFinite(h.HoldSeconds) || h.HoldSeconds < 0
                || (!double.IsNaN(h.Angle) && !IsFinite(h.Angle)))
            {
                throw new FormatException($"砖 {h.TileIndex} 的砖号、时间或长按无效（要按砖号和时间排好、不能重复）");
            }

            lastTile = h.TileIndex;
            lastTime = h.Time;
        }

        var next = 0;
        foreach (var n in f.Notes)
        {
            if (next >= f.Hits.Count || f.Hits[next].TileIndex != n.TileIndex || n.LastTileIndex < n.TileIndex)
            {
                throw new FormatException($"音符（砖 {n.TileIndex}）没有接着上一个音符，手序必须按顺序覆盖全部砖块");
            }

            while (next < f.Hits.Count && f.Hits[next].TileIndex <= n.LastTileIndex)
            {
                next++;
            }

            if (f.Hits[next - 1].TileIndex != n.LastTileIndex)
            {
                throw new FormatException($"音符（砖 {n.TileIndex}）的最后一块砖 {n.LastTileIndex} 不在 hits 里");
            }

            if (n.Lanes.Count < 1 || n.Lanes.Count > f.Keys)
            {
                throw new FormatException($"音符（砖 {n.TileIndex}）要按 1–{f.Keys} 个键");
            }

            foreach (var kv in n.Lanes)
            {
                if (kv.Key < 0 || kv.Key >= f.Keys || !IsFinite(kv.Value) || kv.Value < 0)
                {
                    throw new FormatException($"音符（砖 {n.TileIndex}）的轨道或长按无效");
                }
            }
        }

        if (next != f.Hits.Count)
        {
            throw new FormatException($"手序只覆盖了 {next} / {f.Hits.Count} 块砖");
        }
    }

    private static void Text(JsonWriter w, string name, string value)
    {
        w.WritePropertyName(name);
        w.WriteValue(value ?? string.Empty);
    }

    private static double Round(double value) => Math.Round(value, 6);

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static int Int(JToken? token, string what)
    {
        if (token == null || token.Type != JTokenType.Integer)
        {
            throw new FormatException(what + " 必须是整数");
        }

        var value = (long)token;
        if (value < int.MinValue || value > int.MaxValue)
        {
            throw new FormatException(what + " 超出范围");
        }

        return (int)value;
    }

    private static double Num(JToken? token, string what)
    {
        if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
        {
            throw new FormatException(what + " 必须是数字");
        }

        var value = (double)token;
        if (!IsFinite(value))
        {
            throw new FormatException(what + " 必须是有限数");
        }

        return value;
    }

    private static string Str(JObject root, string name, int max)
    {
        var token = root[name];
        if (token == null || token.Type == JTokenType.Null)
        {
            return string.Empty;
        }

        if (token.Type != JTokenType.String)
        {
            throw new FormatException(name + " 必须是文字");
        }

        var value = (string)token!;
        return value.Length > max ? value.Substring(0, max) : value;
    }
}
