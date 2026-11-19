using RhythmHint.Core;
using RhythmHint.Reference;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 画节拍线用的拍子和小节（BeatGrid.Infer）。复现（玩家反馈：编辑器自动分的小节不聪明）：
/// 关卡 BPM 常是谱师为转角设的数，工坊 2802147583 按关卡变速算出 32352 BPM、每秒 90 条线；×1.5 这类连音写法每次都重新起小节。
/// </summary>
public class BeatGridTests
{
    /// <summary>每 spb 秒一下，从 start 起 count 下；转角按 180° 一拍（关卡每拍 levelSpb 秒）。</summary>
    private static List<TileHit> Run(double start, int count, double spb, double levelSpb, int firstIndex = 1) =>
        Enumerable.Range(0, count).Select(i => new TileHit(firstIndex + i, start + (i * spb), 0, spb / levelSpb * 180)).ToList();

    private static List<double> Measures(BeatMap map) => map.Lines.Where(l => l.IsMeasure).Select(l => Math.Round(l.Time, 4)).ToList();

    /// <summary>相邻两条小节线之间不是 4 拍的个数。</summary>
    private static int IrregularMeasures(BeatMap map, double end)
    {
        int irregular = 0, beats = -1;
        foreach (var l in map.Lines.Where(l => l.Time <= end + 1e-6))
        {
            if (l.IsMeasure)
            {
                irregular += beats >= 0 && beats != 4 ? 1 : 0;
                beats = 1;
            }
            else if (beats >= 0)
            {
                beats++;
            }
        }

        return irregular;
    }

    [Fact]
    public void CharterBpmIsFoldedIntoMusicTempo()
    {
        // 关卡 1011.5 BPM（音乐 126.4375 BPM 的 8 倍），十六分音符每下 90° 之外再隔两个关卡拍；旧算法每秒 17 条线
        const double level = 60 / 1011.5;
        var hits = Run(level, 256, 2 * level, level);

        var map = BeatGrid.Infer(hits, new[] { (0.0, 1.0) }, level, 0, hits[^1].Time + 2);

        Assert.Equal(126.4375, map.MainBpm, 3);
        Assert.Equal(BeatSource.Level, map.Source);
        Assert.InRange(map.Lines.Count / (hits[^1].Time + 2), 1.5, 3);
        Assert.All(map.Lines.Zip(map.Lines.Skip(1)), p => Assert.Equal(8 * level, p.Second.Time - p.First.Time, 6));
    }

    [Fact]
    public void TupletSpeedChangeKeepsCountingBeatsAndMeasures()
    {
        // 120 BPM，中间用 ×1.5 的变速写 6 下三连音（正好 4 拍），再回到原速：音乐的拍子没变，不该重新起小节
        var hits = Run(0.5, 8, 0.5, 0.5);
        hits.AddRange(Run(4.5, 6, 1 / 3.0, 0.5 / 1.5, 9));
        hits.AddRange(Run(6.5, 8, 0.5, 0.5, 15));
        var speeds = new[] { (0.0, 1.0), (4.5, 1.5), (6.5, 1.0) };

        var map = BeatGrid.Infer(hits, speeds, 0.5, 0, 12);

        Assert.Single(map.Sections);
        Assert.Equal(0, IrregularMeasures(map, 12));
        Assert.Equal(new[] { 0.5, 2.5, 4.5, 6.5, 8.5, 10.5 }, Measures(map));
    }

    [Fact]
    public void RealTempoChangeOnABeatContinuesTheMeasure()
    {
        // 120 BPM 到第 3.5 秒（第 7 下，小节里第 3 拍）换成 150 BPM：Malody 那样拍子接着数，小节跨过变速点，不在变速点重新起
        var hits = Run(0.5, 7, 0.5, 0.5);
        hits.AddRange(Run(3.5, 12, 0.4, 0.4, 8));
        var speeds = new[] { (0.0, 1.0), (3.5, 1.25) };

        var map = BeatGrid.Infer(hits, speeds, 0.5, 0, 8);

        Assert.Equal(2, map.Sections.Count);
        Assert.False(map.Sections[1].Restart);
        Assert.Equal(new[] { 0.5, 2.5, 4.3, 5.9, 7.5 }, Measures(map));
        Assert.Equal(0, IrregularMeasures(map, 8));
    }

    [Fact]
    public void TempoChangeOffTheBeatRestartsOnce()
    {
        // 变速点在半拍上（3.25 秒）、之后的砖都按新速度从变速点起：接着数会全落在半拍上，从这里重新起拍、重新起小节
        var hits = Run(0.5, 6, 0.5, 0.5);
        hits.AddRange(Run(3.25, 16, 0.4, 0.4, 7));
        var speeds = new[] { (0.0, 1.0), (3.25, 1.25) };

        var map = BeatGrid.Infer(hits, speeds, 0.5, 0, 10);

        Assert.Equal(2, map.Sections.Count);
        Assert.True(map.Sections[1].Restart);
        Assert.Equal(3.25, map.Sections[1].Anchor, 6);
        Assert.All(hits.Skip(6), h => Assert.True(BeatGrid.IsOnBeat(map.Lines, h.Time)));
        Assert.Equal(1, IrregularMeasures(map, 10));
    }

    [Fact]
    public void AnglesThatDisagreeWithTheLevelBpmUseTheTileGaps()
    {
        // 工坊 / TUF「Life」那样：关卡 64.711 BPM 配 182° 的砖，每下正好 0.9375 秒（64 BPM 的一拍）
        const double level = 60 / 64.711;
        var hits = Enumerable.Range(1, 120).Select(i => new TileHit(i, i * 0.9375, 0, 182)).ToList();

        var map = BeatGrid.Infer(hits, new[] { (0.0, 1.0) }, level, 0, hits[^1].Time + 2);

        Assert.Equal(BeatSource.Tiles, map.Source);
        Assert.Equal(64, map.MainBpm, 2);
        Assert.All(hits, h => Assert.True(BeatGrid.IsOnBeat(map.Lines, h.Time)));
    }

    [Fact]
    public void MeasuresStartOnTheAccents()
    {
        // 每拍一下，第 2、6、10……下是双押（两块相隔 20 ms、转 15°）：小节从双押起
        var hits = Run(0.5, 32, 0.5, 0.5);
        foreach (var t in Enumerable.Range(0, 8).Select(k => 1.0 + (k * 2.0)))
        {
            hits.Add(new TileHit(100 + hits.Count, t + 0.02, 0, 15));
        }

        hits.Sort((a, b) => a.Time.CompareTo(b.Time));
        var map = BeatGrid.Infer(hits, new[] { (0.0, 1.0) }, 0.5, 0, 17);

        Assert.Equal(new[] { 1.0, 3.0, 5.0, 7.0 }, Measures(map).Take(4));
    }

    [Fact]
    public void WithoutAccentsMeasuresStartAtTheFirstHit()
    {
        var hits = Run(1.5, 32, 0.5, 0.5);

        var map = BeatGrid.Infer(hits, new[] { (0.0, 1.0) }, 0.5, 0, 18);

        Assert.Equal(1.5, Measures(map)[0], 6);
        Assert.Equal(0, IrregularMeasures(map, 18));
    }

    [Fact]
    public void ShortSpeedTricksThatReturnOnTheGridKeepTheTempo()
    {
        // 工坊 / TUF「Digital Warrior」那样的伪双押：180 BPM 里一块砖用 ×1.125、再一块 ×1.5，半拍后回到原速、正好落在拍网上
        const double spb = 60.0 / 180;
        var hits = Run(spb, 16, spb / 2, spb);
        hits.Add(new TileHit(50, (9 * spb) + 0.014, 0, 90));
        hits.AddRange(Run(9.5 * spb, 16, spb / 2, spb, 51));
        hits.Sort((a, b) => a.Time.CompareTo(b.Time));
        var speeds = new[] { (0.0, 1.0), (9 * spb, 1.125), ((9 * spb) + 0.014, 1.5), (9.5 * spb, 1.0) };

        var map = BeatGrid.Infer(hits, speeds, spb, 0, hits[^1].Time + 1);

        Assert.Single(map.Sections);
        Assert.Equal(180, map.MainBpm, 6);
    }

    [Fact]
    public void AccelerationKeepsCountingWithoutRestarts()
    {
        // 「Hello (BPM)」那样一块砖一个速度、越来越快（关卡 400 BPM 起，每块砖快 1%）：节拍线跟着砖走，每条都压在砖上，中间不重新起小节
        var hits = new List<TileHit>();
        var speeds = new List<(double Time, double Speed)> { (0, 1) };
        double t = 0, speed = 1;
        for (var i = 1; i <= 48; i++)
        {
            t += 0.15 / speed;
            hits.Add(new TileHit(i, t, 0, 180));
            speed *= 1.01;
            speeds.Add((t, speed));
        }

        var map = BeatGrid.Infer(hits, speeds, 0.15, 0, t);

        Assert.DoesNotContain(map.Sections, s => s.Restart);
        Assert.All(map.Lines.Where(l => l.Time <= t), l => Assert.Contains(hits, h => Math.Abs(l.Time - h.Time) < 1e-6));
        Assert.InRange(map.Lines.Count(l => l.Time <= t), 48 / 4, 48 / 2);
        Assert.Equal(0, IrregularMeasures(map, t));
    }

    [Fact]
    public void NoHitsDrawNothing()
    {
        var map = BeatGrid.Infer(new List<TileHit>(), new[] { (0.0, 1.0) }, 0.5, 0, 10);

        Assert.Empty(map.Lines);
        Assert.Equal(BeatSource.None, map.Source);
    }

    /// <summary>
    /// 本机的真实谱面（不进仓库）：设置 RHYTHMHINT_WORKSHOP（创意工坊 977950 文件夹）、RHYTHMHINT_TUF（TUF 校准谱文件夹，含 levels/）才运行。
    /// 每张谱：主 BPM、每秒节拍线最多几条、不满或超出 4 拍的小节最多几个、要按的砖落在拍网（1/16 以内）上至少多少。
    /// </summary>
    [Theory]
    [InlineData("RHYTHMHINT_WORKSHOP", "2802147583/main.adofai", 126.375, 1)] // Camellia - Hello (BPM) 2022：旧算法 32352 BPM、每秒 90 条
    [InlineData("RHYTHMHINT_WORKSHOP", "2156020621/main.adofai", 160, 3)]     // 中间一段加速，旧算法 119 个不满 4 拍的小节
    [InlineData("RHYTHMHINT_WORKSHOP", "2787254380/main.adofai", 148, 0)]     // 普通谱：和旧算法一样
    [InlineData("RHYTHMHINT_TUF", "levels/10276.adofai", 64, 0)]              // Life：64.711 BPM 配 182° 的砖
    [InlineData("RHYTHMHINT_TUF", "levels/8374.adofai", 180, 4)]              // Digital Warrior：伪双押用的变速，旧算法 293 个
    public void RealCharts(string variable, string file, double bpm, int maxIrregular)
    {
        var folder = Environment.GetEnvironmentVariable(variable);
        var path = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, file);
        Assert.SkipWhen(path is null || !File.Exists(path), $"需要本机谱面：未设置 {variable} 或没有 {file}");

        var level = AdofaiLevelReader.Parse(File.ReadAllText(path!));
        var hits = level.Hits();
        var speeds = new List<(double Time, double Speed)>();
        foreach (var f in level.Floors.Where(f => f.Speed > 0 && (speeds.Count == 0 || speeds[^1].Speed != f.Speed)))
        {
            speeds.Add((f.Time, f.Speed));
        }

        var end = hits[^1].Time;
        var map = BeatGrid.Infer(hits, speeds, level.Crotchet, level.Floors[0].Time, end + 2);

        Assert.Equal(bpm, map.MainBpm, 2);
        Assert.InRange(map.Lines.Count / end, 0.5, 8);
        Assert.InRange(IrregularMeasures(map, end), 0, maxIrregular);

        // 多押（含伪多押）按排键的分法合成一下，只看第一块
        var presses = NotePlanner.Plan(hits, new FingeringOptions()).Select(n => n.Time).ToList();
        var onGrid = presses.Count(t => OnGrid(map.Lines, t));
        Assert.True(onGrid >= 0.95 * presses.Count, $"落在拍网上的只有 {onGrid}/{presses.Count}");
    }

    /// <summary>t 落在相邻两条节拍线之间的细分（1/64 以内、一格不短于 6 ms）上，误差不超过 2 ms 和一格的 5%。</summary>
    private static bool OnGrid(List<BeatLine> lines, double t)
    {
        var k = BeatGrid.FirstAtOrAfter(lines, t + 1e-9) - 1;
        if (k < 0 || k + 1 >= lines.Count)
        {
            return true;
        }

        var spb = lines[k + 1].Time - lines[k].Time;
        var pos = (t - lines[k].Time) / spb;
        return new[] { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64 }.Where(d => d == 1 || spb / d >= 0.006)
            .Any(d => Math.Abs((pos * d) - Math.Round(pos * d)) * spb / d <= Math.Min(0.002, 0.05 * spb / d));
    }
}
