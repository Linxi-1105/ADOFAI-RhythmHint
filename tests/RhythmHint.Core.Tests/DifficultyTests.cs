using RhythmHint.Core;
using RhythmHint.Reference;

namespace RhythmHint.Core.Tests;

/// <summary>难度估算：TUF 档位换算、离线读谱、估算的基本性质。</summary>
public class DifficultyTests
{
    [Theory]
    [InlineData("P1", 1)]
    [InlineData("p20", 20)]
    [InlineData("G1", 21)]
    [InlineData("G12", 32)]
    [InlineData("U1", 41)]
    [InlineData("U20", 60)]
    public void Tiers_RoundTrip(string name, int tier)
    {
        Assert.Equal(tier, DifficultyTiers.Parse(name));
        Assert.Equal(name.ToUpperInvariant(), DifficultyTiers.Name(tier));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Q2")]
    [InlineData("U21")]
    [InlineData("G0")]
    [InlineData("Unranked")]
    [InlineData("-2")]
    public void Tiers_RejectQAndSpecialTiers(string? name)
    {
        Assert.Equal(0, DifficultyTiers.Parse(name));
    }

    [Fact]
    public void Tiers_ClampOutOfRange()
    {
        Assert.Equal("P1", DifficultyTiers.Name(-5));
        Assert.Equal("U20", DifficultyTiers.Name(99));
        Assert.Equal(2, DifficultyTiers.Group(41));
        Assert.Equal(1, DifficultyTiers.Number(41));
    }

    /// <summary>直线 8 块、120 BPM：每块转 180° = 一拍 0.5 秒。</summary>
    private const string StraightLevel = """
        { "angleData": [0, 0, 0, 0, 0, 0, 0, 0],
          "settings": { "version": 13, "bpm": 120, "pitch": 100 },
          "actions": [] }
        """;

    [Fact]
    public void Reader_StraightLine_IsOneBeatPerTile()
    {
        var level = AdofaiLevelReader.Parse(StraightLevel);
        var hits = level.Hits();

        Assert.Equal(8, hits.Count); // 第 0 块是起点不按，后面 8 块都按
        Assert.Empty(level.Unsupported);
        Assert.Equal(1.0, level.PlaybackRate);
        for (var i = 1; i < hits.Count; i++)
        {
            Assert.Equal(0.5, hits[i].Time - hits[i - 1].Time, 6);
            Assert.Equal(180, hits[i].Angle, 6);
        }
    }

    [Fact]
    public void Reader_HandlesSpeedTwirlPauseHoldAndPitch()
    {
        // 第 2 块起 BPM 翻倍；第 4 块暂停一拍；第 6 块长按多转一圈（两拍）；关卡音高 150%
        const string text = """
            { "angleData": [0, 0, 0, 0, 0, 0, 0, 0],
              "settings": { "version": 13, "bpm": 120, "pitch": 150 },
              "actions": [
                { "floor": 2, "eventType": "SetSpeed", "speedType": "Multiplier", "bpmMultiplier": 2 },
                { "floor": 4, "eventType": "Pause", "duration": 1 },
                { "floor": 6, "eventType": "Hold", "duration": 1 },
                { "floor": 3, "eventType": "MoveCamera", "duration": 1 }
              ] }
            """;
        var level = AdofaiLevelReader.Parse(text);
        var floors = level.Floors;

        Assert.Equal(1.5, level.PlaybackRate);
        Assert.Empty(level.Unsupported); // 镜头事件不影响时间
        Assert.Equal(0.5, floors[2].Time - floors[1].Time, 6);   // 原速一拍
        Assert.Equal(0.25, floors[3].Time - floors[2].Time, 6);  // 翻倍后半拍
        Assert.Equal(0.5, floors[5].Time - floors[4].Time, 6);   // 半拍 + 暂停一拍（翻倍后 0.25）
        Assert.True(floors[6].IsHold);
        Assert.Equal(0.75, floors[7].Time - floors[6].Time, 6);  // 一拍半（翻倍后）：转 180° + 长按两拍

        var hits = level.Hits();
        Assert.DoesNotContain(hits, h => h.TileIndex == 7); // 长按结束那块不算一次按键
        Assert.Contains(hits, h => h.TileIndex == 6 && h.HoldSeconds > 0.7);
    }

    [Fact]
    public void Reader_FlagsTimingEventsItCannotSimulate()
    {
        const string text = """
            { "angleData": [0, 0, 0],
              "settings": { "bpm": 100 },
              "actions": [ { "floor": 1, "eventType": "MultiPlanet", "planets": 3 } ] }
            """;
        Assert.Contains("MultiPlanet", AdofaiLevelReader.Parse(text).Unsupported);
    }

    [Fact]
    public void Reader_RepairsDoubleCommaAndMissingCommaAfterArray()
    {
        // 老谱的两种写法：对象属性之间两个逗号；数组属性后面漏了逗号
        const string text = """
            { "angleData": [0, 0, 0] "settings": { "bpm": 100, "requiredMods": [],, "pitch": 100 },
              "actions": [] }
            """;
        var level = AdofaiLevelReader.Parse(text);
        Assert.Equal(3, level.Hits().Count);
    }

    [Fact]
    public void Reader_RepairsMissingCommaBetweenEvents()
    {
        // 老谱 actions 里两个事件之间漏了逗号（本机一张工坊谱就是这样）
        const string text = """
            { "angleData": [0, 0, 0, 0], "settings": { "bpm": 100 },
              "actions": [
                { "floor": 1, "eventType": "Twirl" }
                { "floor": 2, "eventType": "SetSpeed", "speedType": "Bpm", "beatsPerMinute": 200 }
              ] }
            """;
        var level = AdofaiLevelReader.Parse(text);
        Assert.Equal(0.3, level.Floors[3].Time - level.Floors[2].Time, 6); // 第 2 块起 200 BPM，转 180° = 0.3 秒
    }

    /// <summary>bpm 拍一下、一共 seconds 秒的直线连打。</summary>
    private static List<TileHit> Stream(double bpm, double seconds, double subdivision = 1)
    {
        var step = 60 / bpm / subdivision;
        var hits = new List<TileHit>();
        for (var i = 0; i * step <= seconds; i++)
        {
            hits.Add(new TileHit(i + 1, 1 + (i * step), 0, 180 / subdivision));
        }

        return hits;
    }

    [Fact]
    public void Measure_DensityFollowsTempo()
    {
        var slow = DifficultyEstimator.Measure(Stream(120, 60)).Values;
        var fast = DifficultyEstimator.Measure(Stream(240, 60)).Values;
        var index = Array.IndexOf(DifficultyFeatures.Names, "dens50");

        Assert.True(fast[index] > slow[index]);
    }

    [Fact]
    public void Measure_PitchMakesTheSameTilesFaster()
    {
        var hits = Stream(150, 60);
        var normal = DifficultyEstimator.Measure(hits, 1).Values;
        var sped = DifficultyEstimator.Measure(hits, 1.5).Values;
        var index = Array.IndexOf(DifficultyFeatures.Names, "meanNps");

        Assert.True(sped[index] > normal[index]);
    }

    /// <summary>按给定的间隔（循环使用）排 seconds 秒，转角都是 angle。</summary>
    private static List<TileHit> Pattern(double seconds, double angle, params double[] gaps)
    {
        var hits = new List<TileHit>();
        var time = 1.0;
        for (var i = 0; time <= 1 + seconds; i++)
        {
            hits.Add(new TileHit(i + 1, time, 0, angle));
            time += gaps[i % gaps.Length];
        }

        return hits;
    }

    private static double Feature(IReadOnlyList<TileHit> hits, string name) =>
        DifficultyEstimator.Measure(hits).Values[Array.IndexOf(DifficultyFeatures.Names, name)];

    [Fact]
    public void Measure_UniformStreamHasOnePattern_MixedRhythmHasMany()
    {
        var uniform = Pattern(60, 180, 0.08);
        var mixed = Pattern(60, 180, 0.08, 0.12, 0.06, 0.1, 0.07, 0.09);

        Assert.Equal(0, Feature(uniform, "fastTrigram"), 9);
        Assert.True(Feature(mixed, "fastTrigram") > 1);
        Assert.True(Feature(mixed, "peakVariety") > Feature(uniform, "peakVariety"));
    }

    [Fact]
    public void Measure_SpeedJumpsCountSuddenTempoChanges()
    {
        // 8 下一拍 0.5 秒，再 8 下一拍 0.25 秒，来回换：每次换速都算一次
        var gaps = new double[16];
        for (var i = 0; i < 16; i++)
        {
            gaps[i] = i < 8 ? 0.5 : 0.25;
        }

        Assert.Equal(0, Feature(Pattern(60, 180, 0.3), "speedJumps"), 9);
        Assert.True(Feature(Pattern(60, 180, gaps), "speedJumps") > 0);

        // 节奏不变、转角从 180° 变成 90°：星球转得慢了一倍，也算变速
        var hits = Pattern(20, 180, 0.3);
        for (var i = 10; i < hits.Count; i += 10)
        {
            hits[i] = hits[i] with { Angle = 90 };
        }

        Assert.True(Feature(hits, "speedJumps") > 0);
    }

    [Fact]
    public void Measure_RollGroupsCountBurstsThatNeedManyKeys()
    {
        // 每秒一串：8 下、每下隔 30 毫秒（要一口气滚过去，一只手 4 个手指不够）
        var gaps = new double[8];
        for (var i = 0; i < 7; i++)
        {
            gaps[i] = 0.03;
        }

        gaps[7] = 1 - (7 * 0.03);
        var bursts = Pattern(60, 180, gaps);
        var singles = Pattern(60, 180, 0.25);

        Assert.Equal(1, Feature(bursts, "rollShare"), 2); // 几乎每个音都在 8 个音的滚键组里
        Assert.Equal(Math.Log(1 + 8), Feature(bursts, "rollSize"), 6);
        Assert.Equal(0, Feature(singles, "rollShare"), 9);
        Assert.Equal(Math.Log(2), Feature(singles, "rollSize"), 9); // 没有滚键组时当成 1 个音
        Assert.Equal(Math.Log(60), Feature(singles, "logSeconds"), 2);
    }

    [Fact]
    public void Estimate_TooFewHits_IsLowestTier()
    {
        var estimate = DifficultyEstimator.Estimate(new List<TileHit> { new(1, 1, 0, 180) });
        Assert.Equal(1, estimate.Tier);
        Assert.Equal(DifficultyFeatures.Names.Length, estimate.Features.Values.Length);
    }

    /// <summary>同样一分钟的直线连打：越快估得越难，慢的落在 P 档、极快的不在 P 档。</summary>
    [Fact]
    public void Estimate_FasterStreamIsHarder()
    {
        var slow = DifficultyEstimator.Estimate(Stream(100, 60)).Tier;
        var medium = DifficultyEstimator.Estimate(Stream(180, 60, 2)).Tier;
        var fast = DifficultyEstimator.Estimate(Stream(250, 60, 8)).Tier;

        Assert.True(slow <= medium && medium <= fast, $"{slow} {medium} {fast}");
        Assert.True(slow < fast);
        Assert.Equal(0, DifficultyTiers.Group(slow));
        Assert.NotEqual(0, DifficultyTiers.Group(fast));
    }

    [Fact]
    public void Estimate_PitchRaisesTheTier()
    {
        var hits = Stream(160, 90, 2);
        Assert.True(DifficultyEstimator.Estimate(hits, 1.5).Score > DifficultyEstimator.Estimate(hits, 1).Score);
    }

    /// <summary>在后台线程上算，超时算失败（不让测试卡死）。</summary>
    private static DifficultyEstimate EstimateWithin(IReadOnlyList<TileHit> hits, TimeSpan limit)
    {
        var task = Task.Run(() => DifficultyEstimator.Estimate(hits));
        Assert.True(task.Wait(limit), $"估算超过 {limit.TotalSeconds} 秒没算完");
        return task.Result;
    }

    /// <summary>
    /// 一块砖在很远的地方（坏数据）：「最难几段的节奏种类」原来从第一下起一秒一秒往后数窗口，
    /// 复现（2026-11-19 评审）：一块砖在 +∞ 时死循环、在 1e9 秒要 1.36 秒。现在跳过不到 4 下的空窗口，结果和一秒一秒数一样。
    /// </summary>
    [Fact]
    public void Estimate_AFarAwayHitDoesNotHang()
    {
        var hits = Pattern(60, 180, 0.08, 0.12, 0.06, 0.1, 0.07, 0.09);
        var peak = Feature(hits, "peakVariety");

        // 远处多一块砖：前面的窗口一个不少、一个不多（远处那块凑不满 4 下，不成窗口）
        foreach (var far in new[] { 1e9, 1e12 })
        {
            var estimate = EstimateWithin(hits.Append(new TileHit(99999, far, 0, 180)).ToList(), TimeSpan.FromSeconds(10));
            Assert.Equal(peak, estimate.Features.Values[Array.IndexOf(DifficultyFeatures.Names, "peakVariety")]);
        }

        foreach (var broken in new[] { double.PositiveInfinity, double.NaN })
        {
            var estimate = EstimateWithin(hits.Append(new TileHit(99999, broken, 0, 180)).ToList(), TimeSpan.FromSeconds(10));
            Assert.InRange(estimate.Tier, 1, 60);
        }
    }
}
