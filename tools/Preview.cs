#:project ../src/RhythmHint.Reference/RhythmHint.Reference.csproj
#:property TargetFramework=net10.0
#:property TreatWarningsAsErrors=false
#:property PublishAot=false

// 离线预览：不开游戏，用文字画出某一关的排键结果（和 Mod 用同一套规则，只看关卡自己的砖块）。
// 可选再给一张自己手排的参考谱：只用来对照「自动排键像不像自己的手法」，不会改变排键结果。
// 用法：dotnet run tools/Preview.cs -- <关卡.adofai> [参考谱.pez/.json] [--from 秒] [--seconds 秒]
//       [--reset 毫秒] [--focus 毫秒] [--jack 毫秒] [--chord 毫秒] [--no-roll] [--inner-roll] [--left-handed] [--chord-other-hand]   临时试排键参数
//       [--export 文件.json]   导出砖块时间和节拍线，给 tools/layout-lab 网页用
// 只支持常见的 angleData + SetSpeed + Twirl 关卡，用来大致验证；游戏里以游戏算出的时间为准。

using System.Globalization;
using Newtonsoft.Json.Linq;
using RhythmHint.Core;
using RhythmHint.Reference;

var files = args.Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal)
    && (i == 0 || !args[i - 1].StartsWith("--", StringComparison.Ordinal))).ToList();
double from = ReadOption("--from", 0), span = ReadOption("--seconds", 6);
if (files.Count == 0)
{
    Console.Error.WriteLine("用法：dotnet run tools/Preview.cs -- <关卡.adofai> [参考谱] [--from 秒] [--seconds 秒] [--reset|--focus|--jack|--chord 毫秒] [--no-roll] [--inner-roll] [--left-handed] [--chord-other-hand]");
    return 2;
}

var options = new FingeringOptions();
options.ResetWindow = ReadOption("--reset", options.ResetWindow * 1000) / 1000;
options.ChordFocusWindow = ReadOption("--focus", options.ChordFocusWindow * 1000) / 1000;
options.UseRoll = !args.Contains("--no-roll");
options.OuterRoll = !args.Contains("--inner-roll");
options.LeftHanded = args.Contains("--left-handed");
options.ChordsOnOtherHand = args.Contains("--chord-other-hand");
options.JackWindow = ReadOption("--jack", options.JackWindow * 1000) / 1000;
options.ChordWindow = ReadOption("--chord", options.ChordWindow * 1000) / 1000;

List<TileHit> tiles;
List<(double Time, double Speed)> speeds;
double crotchet;
try
{
    (tiles, speeds, crotchet) = AdofaiTiming.Read(files[0]);
}
catch (Exception ex) when (ex is Newtonsoft.Json.JsonReaderException or InvalidDataException or IOException)
{
    Console.Error.WriteLine($"读不了关卡文件 {files[0]}：{ex.Message}");
    Console.Error.WriteLine("（有的关卡文件不是标准 JSON，这个预览工具读不了；游戏里的 Mod 用的是游戏算好的砖块，不受影响）");
    return 1;
}

Console.WriteLine($"关卡：{Path.GetFileName(files[0])}，需要按键 {tiles.Count} 次，{tiles[0].Time:F2}s – {tiles[^1].Time:F2}s");
var beats = BeatGrid.Build(BeatGrid.MusicalTempo(speeds, crotchet), 0, tiles[^1].Time + 2);
var exportIndex = Array.IndexOf(args, "--export");
if (exportIndex >= 0 && exportIndex + 1 < args.Length)
{
    // 给排键试验台网页用：砖块时间（毫秒）、长按、节拍线
    var levelSettings = JObject.Parse(File.ReadAllText(files[0]))["settings"];
    var export = new
    {
        name = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(files[0]))),
        bpm = Math.Round(60 / crotchet, 3),
        // 歌曲：音频时间 = 砖块时间 + offset（和游戏 scrConductor 的 addoffset 一样）
        song = (string?)levelSettings?["songFilename"] ?? "",
        offset = (double?)levelSettings?["offset"] ?? 0,
        pitch = (double?)levelSettings?["pitch"] ?? 100,
        tiles = tiles.Select(t => new[] { t.TileIndex, Math.Round(t.Time * 1000, 1), Math.Round(t.HoldSeconds * 1000, 1) }),
        beats = beats.Select(b => new[] { Math.Round(b.Time * 1000, 1), b.IsMeasure ? 1 : 0 }),
    };
    File.WriteAllText(args[exportIndex + 1], Newtonsoft.Json.JsonConvert.SerializeObject(export));
    Console.WriteLine($"已导出 {tiles.Count} 块砖、{beats.Count} 条节拍线到 {args[exportIndex + 1]}");
}
var notes = NotePlanner.Plan(tiles, options);
var chords = notes.Count(n => n.IsChord);
var rightChords = notes.Count(n => n.IsChord && n.Lanes.ContainsKey(3));
var laneUse = Enumerable.Range(0, Lanes.Count).Select(l => notes.Count(n => n.Lanes.ContainsKey(l)));
Console.WriteLine($"提示 {notes.Count} 个，双押 {chords} 个（右手 {rightChords}，左手 {chords - rightChords}），各轨使用次数 [{string.Join(", ", laneUse)}]");
var tooFast = notes.Zip(notes.Skip(1), (a, b) => (a, b)).Count(p => p.a.IsChord && p.b.IsChord && p.b.Time - p.a.Time < options.JackWindow);
Console.WriteLine($"两个双押挨得太近、同一只手来不及（只能左右手轮流）：{tooFast} 处");

// 散点程度：连在一起（间隔不超过 300 ms）的相邻两下，第一条轨道相差 2 条及以上算「跳轨」
int linked = 0, jumps = 0;
for (var i = 1; i < notes.Count; i++)
{
    if (notes[i].Time - notes[i - 1].Time > LayoutStats.DefaultLinkGap)
    {
        continue;
    }

    linked++;
    var step = notes[i].Lanes.Keys.First() - notes[i - 1].Lanes.Keys.First();
    jumps += Math.Abs(step) >= 2 ? 1 : 0;
}

Console.WriteLine($"跳轨：{jumps}/{linked}（{(linked == 0 ? 0 : jumps * 100.0 / linked):F1}%）");
var lone = LayoutStats.LoneNotes(notes, LayoutStats.DefaultLinkGap);
Console.WriteLine($"孤立音符（前后都隔 2 条以上轨道）：{lone.Count} 个{(lone.Count == 0 ? "" : "，例如 " + string.Join("、", lone.Take(5).Select(n => $"{n.Time:F2}s")))}");

// 手序：快的地方同一只手连着按两个单押（轮指以外不该有）；停顿之后从哪只手起手（应该总是主手：默认右手，--left-handed 时左手）
int HandOf(PlannedNote n) => Lanes.HandOf(n.Lanes.Keys.First());
var mainHand = options.LeftHanded ? 0 : 1;
int fastPairs = 0, sameHand = 0, restarts = 0, mainStarts = 0;
for (var i = 1; i < notes.Count; i++)
{
    var gap = notes[i].Time - notes[i - 1].Time;
    if (gap > options.ResetWindow)
    {
        restarts++;
        mainStarts += HandOf(notes[i]) == mainHand ? 1 : 0;
    }
    else if (!notes[i].IsChord && !notes[i - 1].IsChord)
    {
        fastPairs++;
        sameHand += HandOf(notes[i]) == HandOf(notes[i - 1]) ? 1 : 0;
    }
}

Console.WriteLine($"手序：主手重置间隔以内的相邻单押 {fastPairs} 对，同一只手 {sameHand} 对（轮指）；停顿之后{(options.LeftHanded ? "左" : "右")}手（主手）起手 {mainStarts}/{restarts}");

List<ChartGroup>? reference = null;
AlignResult? align = null;
if (files.Count > 1)
{
    var chart = RpeChart.Load(files[1]);
    reference = chart.ToGroups().ToList();
    var c = ReferenceComparison.Compare(tiles, notes, reference);
    align = c.Align;
    Console.WriteLine($"参考谱「{chart.Name}」：对上 {c.Compared} 个提示（平移 {c.Align.Shift:+0.000;-0.000}s）");
    Console.WriteLine($"  同手 {c.SameHands * 100.0 / c.Compared:F1}%，同轨 {c.SameLanes * 100.0 / c.Compared:F1}%，单押/双押一致 {c.ChordAgree * 100.0 / c.Compared:F1}%");
}

Console.WriteLine();
Console.WriteLine($"  时间      | 自动 0123 |{(reference is null ? "" : " 参考 0123 |")}  ({from:F1}s 起 {span:F1}s；O 单押，# 双押，= 带长按)");
var groupOfTile = new Dictionary<int, int>();
for (var i = 0; i < tiles.Count && align is not null; i++)
{
    groupOfTile[tiles[i].TileIndex] = align.GroupOfTile[i];
}

foreach (var n in notes.Where(n => n.Time >= from && n.Time < from + span))
{
    var row = string.Concat(Enumerable.Range(0, Lanes.Count).Select(l =>
        !n.Lanes.TryGetValue(l, out var hold) ? "." : hold > 0 ? "=" : n.IsChord ? "#" : "O"));
    var refRow = "";
    if (reference is not null)
    {
        var g = groupOfTile.GetValueOrDefault(n.TileIndex, -1);
        refRow = g < 0 ? " ----      |" : " " + string.Concat(Enumerable.Range(0, Lanes.Count).Select(l =>
            reference[g].Holds[l] is not double h ? "." : h > 0 ? "=" : reference[g].LaneCount > 1 ? "#" : "O")) + "      |";
    }

    Console.WriteLine($"  {n.Time,8:F3}s | {row}      |{refRow}");
}

return 0;

double ReadOption(string name, double fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}

/// <summary>按 ADOFAI 的规则从 angleData 算每块砖的到达时间（只处理 SetSpeed、Twirl 和中旋）。</summary>
internal static class AdofaiTiming
{
    /// <returns>需要按键的砖块、变速点（时间, 相对起始 BPM 的倍率）、起始一拍的秒数。</returns>
    public static (List<TileHit> Hits, List<(double Time, double Speed)> Speeds, double Crotchet) Read(string path)
    {
        var root = JObject.Parse(File.ReadAllText(path));
        var angles = root["angleData"]?.Select(x => (double)x).ToArray()
            ?? throw new InvalidDataException("只支持带 angleData 的关卡");
        var bpm = (double?)root["settings"]?["bpm"] ?? throw new InvalidDataException("关卡没有 bpm");
        var events = (root["actions"] as JArray ?? new JArray()).OfType<JObject>()
            .GroupBy(e => (int)e["floor"]!).ToDictionary(g => g.Key, g => g.ToList());

        var hits = new List<TileHit>();
        var speeds = new List<(double Time, double Speed)>();
        var time = 0.0;
        var clockwise = true;
        var current = bpm;
        var lastDir = 0.0;
        for (var k = 0; k < angles.Length; k++)
        {
            foreach (var e in events.GetValueOrDefault(k) ?? new List<JObject>())
            {
                switch ((string?)e["eventType"])
                {
                    case "Twirl":
                        clockwise = !clockwise;
                        break;
                    case "SetSpeed" when (string?)e["speedType"] == "Multiplier":
                        current *= (double)e["bpmMultiplier"]!;
                        break;
                    case "SetSpeed":
                        current = (double)e["beatsPerMinute"]!;
                        break;
                }
            }

            if (k > 0)
            {
                hits.Add(new TileHit(k, time));
            }

            if (speeds.Count == 0 || Math.Abs(speeds[^1].Speed - (current / bpm)) > 1e-9)
            {
                speeds.Add((time, current / bpm));
            }

            if (angles[k] == 999)
            {
                // 中旋：原地转 180°，不花时间
                lastDir = (lastDir + 180) % 360;
                continue;
            }

            var entry = (k > 0 ? lastDir : 0) + 180;
            var swept = clockwise ? Mod(entry - angles[k]) : Mod(angles[k] - entry);
            if (swept < 1e-6)
            {
                swept = 360;
            }

            time += swept / 180 * 60 / current;
            lastDir = angles[k];
        }

        // angleData 有 N 项就有 N+1 块砖，最后一块（第 N 块）的到达也要按一下
        hits.Add(new TileHit(angles.Length, time));
        return (hits, speeds, 60 / bpm);
    }

    private static double Mod(double x) => ((x % 360) + 360) % 360;
}
