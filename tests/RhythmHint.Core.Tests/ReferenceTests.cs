using RhythmHint.Core;
using RhythmHint.Reference;

namespace RhythmHint.Core.Tests;

/// <summary>开发用的参考谱对照工具。</summary>
public class ReferenceTests
{
    /// <summary>160 BPM 下的一段节奏：慢音符、1/4 拍连打、再慢下来。</summary>
    private static List<double> SampleRhythm()
    {
        const double beat = 60.0 / 160;
        var times = new List<double>();
        var t = 1.0;
        foreach (var (count, step) in new[] { (4, beat), (16, beat / 4), (4, beat * 0.75) })
        {
            for (var i = 0; i < count; i++)
            {
                times.Add(t);
                t += step;
            }
        }

        return times;
    }

    [Fact]
    public void Aligner_FindsShiftDespiteMissingAndExtraNotes()
    {
        var tiles = SampleRhythm();
        var rng = new Random(7);
        var chart = tiles.Select(t => t + 2.63 + ((rng.NextDouble() - 0.5) * 0.008)).ToList();
        chart.RemoveAt(10);          // 谱面少一个
        chart.Add(chart[^1] + 0.9);  // 谱面多一个
        chart.Insert(0, 2.0);        // 预备拍
        chart.Sort();

        var r = ChartAligner.Align(tiles, chart);

        Assert.InRange(r.Shift, 2.625, 2.635);
        Assert.Equal(tiles.Count - 1, r.Matched);
        Assert.Equal(-1, r.GroupOfTile[10]);
    }

    [Fact]
    public void Aligner_ReportsChartCoverageWithinLevelSpan()
    {
        var tiles = SampleRhythm();
        var chart = tiles.Select(t => t + 2.0).ToList();
        chart.Insert(0, 0.5);             // 关卡开始前的预备拍不算进覆盖率
        chart.Add(tiles[1] + 2.0 + 0.19); // 关卡范围内多出一个，没有砖对应
        chart.Sort();

        var r = ChartAligner.Align(tiles, chart);

        Assert.Equal(tiles.Count, r.Matched);
        Assert.Equal(tiles.Count + 1, r.GroupsInSpan);
        Assert.Equal(tiles.Count / (double)(tiles.Count + 1), r.Coverage, 6);
    }

    [Fact]
    public void Comparison_CountsLaneAndHandAgreement()
    {
        var tiles = new[] { new TileHit(1, 1.0), new TileHit(2, 1.1), new TileHit(3, 1.2), new TileHit(4, 1.3) };
        var notes = NotePlanner.Plan(tiles, new FingeringOptions());
        var auto = notes.Select(n => n.Lanes.Keys.First()).ToArray();

        // 参考谱：第 1、4 个和自动排键相同，第 2、3 个左右镜像（换了手）
        var reference = auto.Select((lane, i) =>
        {
            var g = new ChartGroup(tiles[i].Time + 5);
            g.Holds[i is 1 or 2 ? 3 - lane : lane] = 0;
            return g;
        }).ToList();

        var c = ReferenceComparison.Compare(tiles, notes, reference);

        Assert.Equal(4, c.Compared);
        Assert.Equal(2, c.SameLanes);
        Assert.Equal(2, c.SameHands);
        Assert.Equal(4, c.ChordAgree);
    }
}
