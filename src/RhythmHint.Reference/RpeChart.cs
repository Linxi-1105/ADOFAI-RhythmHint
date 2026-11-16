using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RhythmHint.Reference;

/// <summary>谱面里的一个原始音符（还没分轨道）。</summary>
/// <param name="Time">秒，从谱面第 0 拍算起（不含 META.offset，对齐时会自动找平移量）。</param>
/// <param name="PositionX">RPE 坐标的横向位置，用来分轨道。</param>
/// <param name="Type">RPE 音符类型：1 tap，2 hold，3 flick，4 drag。</param>
/// <param name="HoldSeconds">hold 的时长，其它类型为 0。</param>
public readonly record struct RawNote(double Time, double PositionX, int Type, double HoldSeconds);

/// <summary>读 Re:PhiEdit（RPE）格式的谱面，也就是 Phira 的 .pez 包里的 json。</summary>
public sealed class RpeChart
{
    /// <summary>单个谱面文件的大小上限，防止误放的大文件把游戏卡住。</summary>
    public const long MaxFileBytes = 64L * 1024 * 1024;

    private RpeChart(string name, IReadOnlyList<RawNote> notes)
    {
        Name = name;
        Notes = notes;
    }

    public string Name { get; }

    public IReadOnlyList<RawNote> Notes { get; }

    /// <summary>读 .pez / .zip（谱面包）或 .json（谱面本体）。</summary>
    /// <exception cref="ChartFormatException">文件不是可识别的 RPE 谱面。</exception>
    /// <exception cref="IOException">文件读不了。</exception>
    public static RpeChart Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("谱面文件不存在", path);
        }

        if (info.Length > MaxFileBytes)
        {
            throw new ChartFormatException($"文件太大（{info.Length / 1024 / 1024} MB），不像谱面：{path}");
        }

        var ext = info.Extension.ToLowerInvariant();
        if (ext is ".pez" or ".zip")
        {
            try
            {
                using var zip = ZipFile.OpenRead(path);
                var entry = FindChartEntry(zip)
                    ?? throw new ChartFormatException($"谱面包里没有找到谱面 json：{path}");
                using var reader = new StreamReader(entry.Open());
                return Parse(reader.ReadToEnd(), Path.GetFileNameWithoutExtension(path));
            }
            catch (InvalidDataException ex)
            {
                throw new ChartFormatException($"谱面包打不开：{ex.Message}", ex);
            }
        }

        return Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>解析 RPE json 文本。</summary>
    /// <exception cref="ChartFormatException">不是可识别的 RPE 谱面。</exception>
    public static RpeChart Parse(string json, string fallbackName)
    {
        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonReaderException ex)
        {
            throw new ChartFormatException($"谱面 json 格式错误：{ex.Message}", ex);
        }

        if (root["judgeLineList"] is not JArray lines)
        {
            throw new ChartFormatException("不是 RPE 谱面：缺少 judgeLineList");
        }

        var timing = BpmTiming.FromRpe(root["BPMList"] as JArray);
        var name = (string?)root["META"]?["name"];
        var notes = new List<RawNote>();
        foreach (var line in lines.OfType<JObject>())
        {
            var factor = (double?)line["bpmfactor"] ?? 1.0;
            if (factor <= 0)
            {
                factor = 1.0;
            }

            if (line["notes"] is not JArray lineNotes)
            {
                continue;
            }

            foreach (var n in lineNotes.OfType<JObject>())
            {
                if (((int?)n["isFake"] ?? 0) != 0)
                {
                    continue;
                }

                // bpmfactor：这条线的实际 BPM = 全局 BPM / bpmfactor，等于所有时间乘以 bpmfactor
                var start = timing.ToSeconds(ReadBeat(n["startTime"])) * factor;
                var end = timing.ToSeconds(ReadBeat(n["endTime"])) * factor;
                var type = (int?)n["type"] ?? 1;
                var hold = type == 2 ? Math.Max(0, end - start) : 0;
                notes.Add(new RawNote(start, (double?)n["positionX"] ?? 0, type, hold));
            }
        }

        notes.Sort((a, b) => a.Time.CompareTo(b.Time));
        return new RpeChart(string.IsNullOrWhiteSpace(name) ? fallbackName : name!, notes);
    }

    /// <summary>把音符按轨道和时刻分组：横向位置分成四轨，同一时刻的合并成一组（双押）。</summary>
    /// <param name="sameTime">两个音符的时间差小于它就算同一时刻，秒。</param>
    public IReadOnlyList<ChartGroup> ToGroups(double sameTime = 0.002)
    {
        var mapper = LaneMapper.FromPositions(Notes.Select(n => n.PositionX));
        var groups = new List<ChartGroup>();
        foreach (var note in Notes)
        {
            var group = groups.Count > 0 && note.Time - groups[groups.Count - 1].Time <= sameTime
                ? groups[groups.Count - 1]
                : null;
            if (group is null)
            {
                group = new ChartGroup(note.Time);
                groups.Add(group);
            }

            // 同一轨道同一时刻叠了两个音符（例如 hold 上叠 drag）：只留一个，长按取较长的
            var lane = mapper.LaneOf(note.PositionX);
            var old = group.Holds[lane];
            group.Holds[lane] = old.HasValue ? Math.Max(old.Value, note.HoldSeconds) : note.HoldSeconds;
        }

        return groups;
    }

    private static ZipArchiveEntry? FindChartEntry(ZipArchive zip)
    {
        // info.txt / info.yml 里写了 "Chart: xxx.json"
        foreach (var info in zip.Entries.Where(e => e.FullName.Equals("info.txt", StringComparison.OrdinalIgnoreCase)
            || e.FullName.Equals("info.yml", StringComparison.OrdinalIgnoreCase)))
        {
            using var reader = new StreamReader(info.Open());
            string? row;
            while ((row = reader.ReadLine()) is not null)
            {
                var idx = row.IndexOf(':');
                if (idx > 0 && row.Substring(0, idx).Trim().Equals("Chart", StringComparison.OrdinalIgnoreCase))
                {
                    var hit = zip.GetEntry(row.Substring(idx + 1).Trim().Trim('"', '\''));
                    if (hit is not null)
                    {
                        return hit;
                    }
                }
            }
        }

        return zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>RPE 的时间写法是 [整数拍, 分子, 分母]。</summary>
    internal static double ReadBeat(JToken? token)
    {
        if (token is JArray { Count: 3 } a)
        {
            var whole = (double)a[0];
            var num = (double)a[1];
            var den = (double)a[2];
            return den == 0 ? whole : whole + (num / den);
        }

        return token is null ? 0 : (double)token;
    }
}

/// <summary>谱面文件无法识别。</summary>
public sealed class ChartFormatException : Exception
{
    public ChartFormatException(string message)
        : base(message)
    {
    }

    public ChartFormatException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>按 BPM 列表把拍数换算成秒。</summary>
internal sealed class BpmTiming
{
    private readonly List<(double Beat, double Bpm, double Seconds)> _segments;

    private BpmTiming(List<(double Beat, double Bpm, double Seconds)> segments)
    {
        _segments = segments;
    }

    public static BpmTiming FromRpe(JArray? bpmList)
    {
        var raw = new List<(double Beat, double Bpm)>();
        if (bpmList is not null)
        {
            foreach (var item in bpmList.OfType<JObject>())
            {
                var bpm = (double?)item["bpm"] ?? 0;
                if (bpm > 0)
                {
                    raw.Add((RpeChart.ReadBeat(item["startTime"]), bpm));
                }
            }
        }

        if (raw.Count == 0)
        {
            throw new ChartFormatException("谱面没有有效的 BPMList");
        }

        raw.Sort((a, b) => a.Beat.CompareTo(b.Beat));
        var segments = new List<(double Beat, double Bpm, double Seconds)>();
        var seconds = 0.0;
        for (var i = 0; i < raw.Count; i++)
        {
            if (i > 0)
            {
                seconds += (raw[i].Beat - raw[i - 1].Beat) * 60.0 / raw[i - 1].Bpm;
            }

            segments.Add((raw[i].Beat, raw[i].Bpm, seconds));
        }

        return new BpmTiming(segments);
    }

    public double ToSeconds(double beat)
    {
        var seg = _segments[0];
        foreach (var s in _segments)
        {
            if (s.Beat > beat)
            {
                break;
            }

            seg = s;
        }

        return seg.Seconds + ((beat - seg.Beat) * 60.0 / seg.Bpm);
    }
}
