#:project ../src/RhythmHint.Reference/RhythmHint.Reference.csproj
#:property TargetFramework=net10.0
#:property TreatWarningsAsErrors=false
#:property PublishAot=false

// 难度估算的离线校准：拿一批已有 TUF 评级的谱面，看估算和评级差多少。
// 谱面和评级不进仓库（版权 / 第三方数据），放在仓库外的文件夹里：
//   <文件夹>/index.json      { "<id>": { "tier": "G12", "song": ..., ... }, ... }
//   <文件夹>/levels/<id>.adofai
// 用法：
//   dotnet run tools/Difficulty.cs -- export <文件夹> <输出.csv>    导出每关的特征和 TUF 档位，拟合系数用
//   dotnet run tools/Difficulty.cs -- hits <文件夹> <输出.json>      导出每关要按的砖（真实秒数、转角），试新特征用
//   dotnet run tools/Difficulty.cs -- evaluate <文件夹>              用 Core 的估算跑一遍，按档位列出误差
//       （模型就是用这批谱拟合的，这里是训练集误差，偏乐观；真实误差看 difficulty-fit.py 的交叉验证。
//        和 difficulty-fit.py 打印的「全部数据拟合」误差一致，说明 C# 的特征和模型实现没有偏差）
//   dotnet run tools/Difficulty.cs -- level <关卡.adofai>            估算一关并列出特征

using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RhythmHint.Core;
using RhythmHint.Reference;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length < 2)
{
    Console.Error.WriteLine("用法：dotnet run tools/Difficulty.cs -- export <文件夹> <输出.json> | evaluate <文件夹> | level <关卡.adofai>");
    return 2;
}

switch (args[0])
{
    case "export":
        return Export(args[1], args.Length > 2 ? args[2] : "hits.json");
    case "hits":
        return ExportHits(args[1], args.Length > 2 ? args[2] : "hits.json");
    case "evaluate":
        return Evaluate(args[1]);
    case "level":
        return Level(args[1]);
    default:
        Console.Error.WriteLine($"不认识的命令：{args[0]}");
        return 2;
}

static IEnumerable<(string Id, string Tier, JObject Info, AdofaiLevel? Level, string? Error)> Load(string folder)
{
    var index = JObject.Parse(File.ReadAllText(Path.Combine(folder, "index.json")));
    foreach (var (id, token) in index)
    {
        var info = (JObject)token!;
        var tier = (string?)info["tier"] ?? "";
        AdofaiLevel? level = null;
        string? error = null;
        try
        {
            level = AdofaiLevelReader.Parse(File.ReadAllText(Path.Combine(folder, "levels", id + ".adofai")));
        }
        catch (Exception e) when (e is FormatException or JsonException or IOException or InvalidCastException or ArgumentException)
        {
            error = e.GetType().Name + ": " + e.Message;
        }

        yield return (id, tier, info, level, error);
    }
}

static int Export(string folder, string output)
{
    // 每关一行：编号、TUF 档位（1–60）、离线时间准不准、Core 算出的各项特征。拟合系数在仓库外用这个表做
    var lines = new List<string> { "id,tier,actual,exact,hits," + string.Join(",", DifficultyFeatures.Names) };
    int ok = 0, skipped = 0;
    foreach (var (id, tier, _, level, error) in Load(folder))
    {
        if (level == null)
        {
            Console.Error.WriteLine($"#{id} {tier}: 读不了（{error}）");
            skipped++;
            continue;
        }

        var hits = level.Hits();
        var features = DifficultyEstimator.Measure(hits, level.PlaybackRate);
        lines.Add($"{id},{tier},{DifficultyTiers.Parse(tier)},{(level.Unsupported.Count == 0 ? 1 : 0)},{hits.Count},"
            + string.Join(",", features.Values.Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
        ok++;
    }

    File.WriteAllLines(output, lines);
    Console.WriteLine($"导出 {ok} 关，读不了 {skipped} 关 → {output}");
    return 0;
}

static int ExportHits(string folder, string output)
{
    // 每关要按的砖（按播放倍率换算成真实秒数）和转角，给分析脚本试新特征用
    var list = new JArray();
    foreach (var (id, tier, _, level, _) in Load(folder))
    {
        if (level == null)
        {
            continue;
        }

        var hits = level.Hits();
        list.Add(new JObject
        {
            ["id"] = id,
            ["tier"] = DifficultyTiers.Parse(tier),
            ["exact"] = level.Unsupported.Count == 0,
            ["t"] = new JArray(hits.Select(h => Math.Round(h.Time / level.PlaybackRate, 5))),
            ["a"] = new JArray(hits.Select(h => double.IsNaN(h.Angle) ? -1 : Math.Round(h.Angle, 3))),
            ["h"] = new JArray(hits.Select(h => Math.Round(h.HoldSeconds / level.PlaybackRate, 4))),
        });
    }

    File.WriteAllText(output, list.ToString(Formatting.None));
    Console.WriteLine($"导出 {list.Count} 关 → {output}");
    return 0;
}

static int Evaluate(string folder)
{
    var rows = new List<(string Id, string Tier, int Actual, double Score)>();
    var skipped = new Dictionary<string, int>();
    foreach (var (id, tier, _, level, error) in Load(folder))
    {
        var actual = DifficultyTiers.Parse(tier);
        if (level == null || actual == 0)
        {
            skipped[error == null ? "档位" : "读不了"] = skipped.TryGetValue(error == null ? "档位" : "读不了", out var n) ? n + 1 : 1;
            continue;
        }

        if (level.Unsupported.Count > 0)
        {
            skipped["时间不准"] = skipped.TryGetValue("时间不准", out var n) ? n + 1 : 1;
            continue;
        }

        var estimate = DifficultyEstimator.Estimate(level.Hits(), level.PlaybackRate);
        rows.Add((id, tier, actual, estimate.Score));
    }

    if (rows.Count == 0)
    {
        Console.Error.WriteLine("没有能用的关卡。");
        return 1;
    }

    var errors = rows.Select(r => Math.Round(r.Score) - r.Actual).ToList();
    Console.WriteLine($"能用 {rows.Count} 关；跳过 {string.Join("，", skipped.Select(kv => $"{kv.Key} {kv.Value}"))}");
    Console.WriteLine("（训练集误差，偏乐观；应和 difficulty-fit.py 的「全部数据拟合」一致）");
    Console.WriteLine($"平均差 {errors.Average(e => Math.Abs(e)):F2} 档；偏差 {errors.Average():+0.00;-0.00} 档");
    foreach (var within in new[] { 0, 1, 2, 3, 5, 8 })
    {
        Console.WriteLine($"  差 ≤ {within} 档：{errors.Count(e => Math.Abs(e) <= within) * 100.0 / errors.Count:F0}%");
    }

    Console.WriteLine("各大档（实际 → 平均估算，平均差）：");
    foreach (var group in rows.GroupBy(r => r.Tier[0]).OrderBy(g => "PGU".IndexOf(g.Key)))
    {
        Console.WriteLine($"  {group.Key}: {group.Count()} 关，平均差 {group.Average(r => Math.Abs(Math.Round(r.Score) - r.Actual)):F2}");
    }

    return 0;
}

static int Level(string path)
{
    var level = AdofaiLevelReader.Parse(File.ReadAllText(path));
    var hits = level.Hits();
    var estimate = DifficultyEstimator.Estimate(hits, level.PlaybackRate);
    Console.WriteLine($"{hits.Count} 次按键，播放倍率 {level.PlaybackRate:0.##}；估算 {DifficultyTiers.Name(estimate.Tier)}（{estimate.Score:F2}）");
    if (level.Unsupported.Count > 0)
    {
        Console.WriteLine($"离线时间可能不准：{string.Join("、", level.Unsupported)}");
    }

    foreach (var (name, value) in estimate.Features.Named())
    {
        Console.WriteLine($"  {name,-16} {value,10:F3}");
    }

    return 0;
}
