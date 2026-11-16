using System;
using System.Collections.Generic;
using System.Linq;

namespace RhythmHint.Reference;

/// <summary>对齐参数。</summary>
public sealed class AlignOptions
{
    /// <summary>谱面和关卡的时间差最多搜多远，秒（两边音频开头留白不同造成）。</summary>
    public double MaxShift { get; set; } = 30;

    /// <summary>砖块和谱面音符相差多少以内算对上，秒。</summary>
    public double Tolerance { get; set; } = 0.03;
}

/// <summary>对齐结果。</summary>
public sealed class AlignResult
{
    public AlignResult(double shift, int[] groupOfTile, double meanError, int groupsInSpan)
    {
        Shift = shift;
        GroupOfTile = groupOfTile;
        MeanError = meanError;
        GroupsInSpan = groupsInSpan;
        Matched = groupOfTile.Count(g => g >= 0);
    }

    /// <summary>谱面时间 = 砖块时间 + Shift。</summary>
    public double Shift { get; }

    /// <summary>每块砖对上的谱面组下标，没对上为 -1。</summary>
    public int[] GroupOfTile { get; }

    public int Matched { get; }

    /// <summary>对上的砖块平均时间误差，秒。</summary>
    public double MeanError { get; }

    /// <summary>平移后落在关卡首尾之间的谱面时刻数（不含关卡开始前的预备拍等）。</summary>
    public int GroupsInSpan { get; }

    /// <summary>关卡的砖有多少比例在谱面里找到了音符。</summary>
    public double Ratio => GroupOfTile.Length == 0 ? 0 : Matched / (double)GroupOfTile.Length;

    /// <summary>谱面（关卡时间范围内）有多少比例的时刻对上了砖。两边都高才说明是同一张谱。</summary>
    public double Coverage => GroupsInSpan == 0 ? 0 : Matched / (double)GroupsInSpan;
}

/// <summary>
/// 找出谱面相对关卡的整体时间平移，再把每块砖对到最近的谱面时刻。
/// 做法：先统计「谱面时刻 - 砖块时刻」的差值落在哪个 5 毫秒区间最多（投票），
/// 再在票数最高的几个区间附近以 1 毫秒步长细调，取对上砖块最多、误差最小的平移量。
/// </summary>
public static class ChartAligner
{
    private const double CoarseBin = 0.005;
    private const int Candidates = 5;

    public static AlignResult Align(IReadOnlyList<double> tileTimes, IReadOnlyList<double> groupTimes, AlignOptions? options = null)
    {
        options ??= new AlignOptions();
        if (tileTimes.Count == 0 || groupTimes.Count == 0)
        {
            return new AlignResult(0, Enumerable.Repeat(-1, tileTimes.Count).ToArray(), 0, 0);
        }

        var votes = new Dictionary<long, int>();
        var lo = 0;
        foreach (var t in tileTimes)
        {
            while (lo < groupTimes.Count && groupTimes[lo] < t - options.MaxShift)
            {
                lo++;
            }

            for (var j = lo; j < groupTimes.Count && groupTimes[j] <= t + options.MaxShift; j++)
            {
                var bin = (long)Math.Round((groupTimes[j] - t) / CoarseBin);
                votes[bin] = votes.TryGetValue(bin, out var v) ? v + 1 : 1;
            }
        }

        AlignResult? best = null;
        foreach (var bin in votes.OrderByDescending(kv => kv.Value).Take(Candidates).Select(kv => kv.Key))
        {
            var center = bin * CoarseBin;
            for (var step = -10; step <= 10; step++)
            {
                var result = Match(tileTimes, groupTimes, center + (step * 0.001), options.Tolerance);
                if (best is null || IsBetter(result, best))
                {
                    best = result;
                }
            }
        }

        return best!;
    }

    /// <summary>按给定平移量把砖块对到谱面：每块砖取最近的谱面时刻，每个谱面时刻只用一次。</summary>
    public static AlignResult Match(IReadOnlyList<double> tileTimes, IReadOnlyList<double> groupTimes, double shift, double tolerance)
    {
        var groupOfTile = Enumerable.Repeat(-1, tileTimes.Count).ToArray();
        var tileOfGroup = new Dictionary<int, int>();
        var j = 0;
        for (var i = 0; i < tileTimes.Count; i++)
        {
            var target = tileTimes[i] + shift;
            while (j + 1 < groupTimes.Count && groupTimes[j + 1] <= target)
            {
                j++;
            }

            var pick = -1;
            var err = double.MaxValue;
            for (var k = Math.Max(0, j - 1); k <= Math.Min(groupTimes.Count - 1, j + 1); k++)
            {
                var e = Math.Abs(groupTimes[k] - target);
                if (e <= tolerance && e < err)
                {
                    pick = k;
                    err = e;
                }
            }

            if (pick < 0)
            {
                continue;
            }

            if (tileOfGroup.TryGetValue(pick, out var other))
            {
                // 两块砖抢同一个谱面时刻：留给离得更近的那块
                var otherErr = Math.Abs(groupTimes[pick] - (tileTimes[other] + shift));
                if (otherErr <= err)
                {
                    continue;
                }

                groupOfTile[other] = -1;
            }

            groupOfTile[i] = pick;
            tileOfGroup[pick] = i;
        }

        var errors = new List<double>();
        for (var i = 0; i < groupOfTile.Length; i++)
        {
            if (groupOfTile[i] >= 0)
            {
                errors.Add(Math.Abs(groupTimes[groupOfTile[i]] - (tileTimes[i] + shift)));
            }
        }

        var spanStart = tileTimes[0] + shift - tolerance;
        var spanEnd = tileTimes[tileTimes.Count - 1] + shift + tolerance;
        var inSpan = groupTimes.Count(t => t >= spanStart && t <= spanEnd);
        return new AlignResult(shift, groupOfTile, errors.Count == 0 ? 0 : errors.Average(), inSpan);
    }

    private static bool IsBetter(AlignResult a, AlignResult b) =>
        a.Matched > b.Matched || (a.Matched == b.Matched && a.MeanError < b.MeanError);
}
