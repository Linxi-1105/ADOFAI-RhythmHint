using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RhythmHint.Core;

/// <summary>真实排键的便携审核快照。只负责导出，不改变排键，也不依赖游戏或 JSON 程序集。</summary>
public static class ReviewChart
{
    public const string Format = "rhythmhint-review";
    public const int SchemaVersion = 1;
    public const string BaselineRevision = "054fe9cd6a8961afbf0c8f9c181bd3dcedb12b18";
    public const string Algorithm = "NotePlanner/" + BaselineRevision + "/TimeReset";
    public const string ReviewRulesAlgorithm = "NotePlanner/" + BaselineRevision + "/ReviewPreferenceV1";

    public static string AlgorithmFor(FingeringOptions options) => options.Style switch
    {
        FingeringStyle.TimeReset => Algorithm,
        FingeringStyle.ReviewPreferenceV1 => ReviewRulesAlgorithm,
        _ => throw new ArgumentOutOfRangeException(nameof(options), "未知排键风格。"),
    };

    /// <summary>时间和长按均以秒为单位；原始 lanes 永远保留，编辑器将修改另存到 review.edits。</summary>
    public static string Export(string name, string source, IReadOnlyList<PlannedNote> notes,
        FingeringOptions options, IReadOnlyList<string>? keyLabels = null, DateTimeOffset? exportedAt = null,
        IReadOnlyList<TileHit>? sourceHits = null)
    {
        if (source != "game" && source != "offline-approximation" && source != "demo")
        {
            throw new ArgumentException("未知的谱面来源。", nameof(source));
        }

        if (notes == null || notes.Count == 0)
        {
            throw new ArgumentException("没有可供审核的音符。", nameof(notes));
        }

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        keyLabels ??= new[] { "D", "F", "J", "K" };
        if (keyLabels.Count != Lanes.Count)
        {
            throw new ArgumentException("需要四个键位名称。", nameof(keyLabels));
        }

        var parameters = OptionsJson(options);
        var algorithm = AlgorithmFor(options);
        var original = NotesJson(notes);
        var rawHits = sourceHits == null ? null : HitsJson(sourceHits);
        string chartId;
        using (var hash = SHA256.Create())
        {
            // 合并后的双押会丢失第二块砖的时间，标识必须同时绑定重跑算法所需的原始输入。
            var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(algorithm + "\n" + parameters + "\n" + original
                + "\n" + (rawHits ?? "null")));
            chartId = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        var json = new StringBuilder();
        json.Append("{\n  \"format\": ").Append(Quote(Format));
        json.Append(",\n  \"schemaVersion\": ").Append(SchemaVersion);
        json.Append(",\n  \"chartId\": ").Append(Quote(chartId));
        json.Append(",\n  \"name\": ").Append(Quote(name));
        json.Append(",\n  \"source\": ").Append(Quote(source));
        json.Append(",\n  \"algorithm\": ").Append(Quote(algorithm));
        json.Append(",\n  \"exportedAt\": ").Append(Quote((exportedAt ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
        json.Append(",\n  \"options\": ").Append(parameters);
        json.Append(",\n  \"keyLabels\": [");
        for (var i = 0; i < keyLabels.Count; i++)
        {
            if (i != 0) json.Append(',');
            json.Append(Quote(keyLabels[i]));
        }

        json.Append("],\n  \"notes\": ").Append(original);
        if (rawHits != null)
        {
            json.Append(",\n  \"sourceHits\": ").Append(rawHits);
        }

        json.Append(",\n  \"review\": {\"status\":\"draft\",\"comment\":\"\",\"edits\":[]}\n}\n");
        return json.ToString();
    }

    private static string OptionsJson(FingeringOptions options)
    {
        if (options.ResetWindow <= 0) throw new ArgumentException("主手重置间隔必须大于零。", nameof(options));
        if (options.ReviewShortMinNotes < 2 || options.ReviewShortMaxNotes < options.ReviewShortMinNotes
            || options.ReviewShortMaxGap < options.ReviewShortMinGap)
            throw new ArgumentException("实验短串参数范围无效。", nameof(options));
        return "{\"ChordWindow\":" + Nonnegative(options.ChordWindow)
            + ",\"ChordAngle\":" + Nonnegative(options.ChordAngle)
            + ",\"ChordMaxGap\":" + Nonnegative(options.ChordMaxGap)
            + ",\"ResetWindow\":" + Nonnegative(options.ResetWindow)
            + ",\"ChordFocusWindow\":" + Nonnegative(options.ChordFocusWindow)
            + ",\"JackWindow\":" + Nonnegative(options.JackWindow)
            + ",\"UseRoll\":" + Bool(options.UseRoll)
            + ",\"OuterRoll\":" + Bool(options.OuterRoll)
            + ",\"LeftHanded\":" + Bool(options.LeftHanded)
            + ",\"ChordsOnOtherHand\":" + Bool(options.ChordsOnOtherHand)
            + ",\"Style\":" + Quote(options.Style.ToString())
            + ",\"ReviewShortMinGap\":" + Nonnegative(options.ReviewShortMinGap)
            + ",\"ReviewShortMaxGap\":" + Nonnegative(options.ReviewShortMaxGap)
            + ",\"ReviewShortMinNotes\":" + options.ReviewShortMinNotes.ToString(CultureInfo.InvariantCulture)
            + ",\"ReviewShortMaxNotes\":" + options.ReviewShortMaxNotes.ToString(CultureInfo.InvariantCulture)
            + ",\"ReviewTimingTolerance\":" + Nonnegative(options.ReviewTimingTolerance) + "}";
    }

    private static string NotesJson(IReadOnlyList<PlannedNote> notes)
    {
        var json = new StringBuilder("[");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var previous = double.NegativeInfinity;
        foreach (var note in notes)
        {
            if (note.TileIndex < 0 || note.LastTileIndex < note.TileIndex || note.Time < previous
                || note.Lanes.Count < 1 || note.Lanes.Count > Lanes.Count)
            {
                throw new ArgumentException("音符必须按时间排列，且具有有效的砖块范围和轨道。", nameof(notes));
            }

            var time = Number(note.Time);
            var id = note.TileIndex.ToString(CultureInfo.InvariantCulture) + ":"
                + note.LastTileIndex.ToString(CultureInfo.InvariantCulture) + ":" + time;
            if (!ids.Add(id)) throw new ArgumentException("音符标识重复。", nameof(notes));
            if (json.Length > 1) json.Append(',');
            json.Append("\n    {\"id\":").Append(Quote(id));
            json.Append(",\"tileIndex\":").Append(note.TileIndex.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"lastTileIndex\":").Append(note.LastTileIndex.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"time\":").Append(time).Append(",\"lanes\":[");
            var first = true;
            foreach (var lane in note.Lanes)
            {
                if (lane.Key < 0 || lane.Key >= Lanes.Count) throw new ArgumentException("轨道编号必须为 0–3。", nameof(notes));
                if (!first) json.Append(',');
                first = false;
                json.Append("{\"lane\":").Append(lane.Key.ToString(CultureInfo.InvariantCulture));
                json.Append(",\"holdSeconds\":").Append(Nonnegative(lane.Value)).Append('}');
            }

            json.Append("]}");
            previous = note.Time;
        }

        return json.Append("\n  ]").ToString();
    }

    private static string Bool(bool value) => value ? "true" : "false";

    // 保留合并双押之前的全部输入，后续可用它重跑 C# 算法并验证新增规则。
    private static string HitsJson(IReadOnlyList<TileHit> hits)
    {
        var json = new StringBuilder("[");
        var previous = double.NegativeInfinity;
        var tileIds = new HashSet<int>();
        foreach (var hit in hits)
        {
            if (hit.TileIndex < 0 || hit.Time < previous || !tileIds.Add(hit.TileIndex))
            {
                throw new ArgumentException("原始砖块必须按时间排列且具有唯一的有效序号。", nameof(hits));
            }

            if (json.Length > 1) json.Append(',');
            json.Append("{\"tileIndex\":").Append(hit.TileIndex.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"time\":").Append(Number(hit.Time));
            json.Append(",\"holdSeconds\":").Append(Nonnegative(hit.HoldSeconds));
            // Unknown angle is represented by an omitted property, never invalid JSON NaN.
            if (!double.IsNaN(hit.Angle)) json.Append(",\"angle\":").Append(Nonnegative(hit.Angle));
            json.Append('}');
            previous = hit.Time;
        }

        return json.Append(']').ToString();
    }

    private static string Nonnegative(double value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "时长不能为负数。");
        return Number(value);
    }

    private static string Number(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), "时间必须是有限数值。");
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Quote(string value)
    {
        var json = new StringBuilder("\"");
        foreach (var c in value ?? string.Empty)
        {
            if (c == '"' || c == '\\') json.Append('\\').Append(c);
            else if (c < 0x20 || char.IsSurrogate(c)) json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else json.Append(c);
        }

        return json.Append('"').ToString();
    }
}
