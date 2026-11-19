using System;
using System.Collections.Generic;

namespace RhythmHint.Core;

/// <summary>难度估算的结果。</summary>
/// <param name="Tier">档位 1–60（P1…U20，见 <see cref="DifficultyTiers"/>）。</param>
/// <param name="Score">没取整的分数，越大越难；离线校准用它算误差。</param>
/// <param name="Features">估算用到的特征，调试和校准时看。</param>
public readonly record struct DifficultyEstimate(int Tier, double Score, DifficultyFeatures Features);

/// <summary>从一关要按的砖算出的特征（时间都按实际播放速度换算成真实秒数）。</summary>
public sealed class DifficultyFeatures
{
    /// <summary>特征名，顺序就是模型的输入顺序（tools/difficulty-fit.py 按名字核对）。</summary>
    public static readonly string[] Names =
    {
        "logHits", "meanNps", "dens50", "densMax", "fast60", "chord", "strain0.3", "strain4.0",
        "gapEntropy", "fastTrigram", "peakVariety", "speedJumps", "logSeconds", "rollShare", "rollSize",
    };

    public DifficultyFeatures(double[] values)
    {
        if (values.Length != Names.Length)
        {
            throw new ArgumentException($"需要 {Names.Length} 个特征，给了 {values.Length} 个。", nameof(values));
        }

        Values = values;
    }

    public double[] Values { get; }

    public IEnumerable<(string Name, double Value)> Named()
    {
        for (var i = 0; i < Names.Length; i++)
        {
            yield return (Names[i], Values[i]);
        }
    }
}

/// <summary>
/// 只看这一关自己要按的砖，估一个 TUF 风格的难度档位（P1–U20）。
/// 特征：按键多少、平均和局部密度（每个音符前后 1 秒内按几下）、快间隔和多押的比例、
/// 两种时间尺度的「负荷」（短的看爆发，长的看耐力）、节奏有多杂（快的地方的节奏花样、最难几段的节奏种类）、突然变速、
/// 谱有多长、滚键组（要一口气滚过去的一串有多少、多长，对应 TUF 参考谱的 ROLLING / KEYCOUNT）。
/// 只看密度的话，整齐的长连打会估高、节奏花和变速多的谱会估低，所以节奏复杂度很要紧（docs/难度估算.md）。
/// 特征到档位用梯度提升树（DifficultyModel.g.cs），拿 TUF 已评级的谱面离线拟合（tools/difficulty-fit.py，谱面不进仓库），
/// 做法和误差见 docs/难度估算.md。
/// </summary>
public static class DifficultyEstimator
{
    /// <summary>两次按键相隔不到这么久算同时按（多押、伪双押）。</summary>
    private const double ChordGap = 0.025;

    /// <summary>算「快的地方的节奏花样」时，间隔短于这个才算快。</summary>
    private const double FastGap = 0.15;

    /// <summary>每度转角用的时间一下变了这么多倍（快慢都算）就算一次突然变速。</summary>
    private const double SpeedJumpRatio = 1.4;

    /// <summary>滚键组：相邻两下间隔都短于这个的一串，玩家要在一只手上滚过去（一组几个音就要几个键）。</summary>
    private const double RollGap = 0.07;

    /// <summary>算「多少音符在大滚键组里」时，至少这么多个音才算大组（超过一只手 4 个手指）。</summary>
    private const int BigRoll = 5;

    /// <summary>
    /// 第一下到最后一下超过这么多秒（2^40 秒，约 3.5 万年）是坏数据，「最难几段的节奏种类」不算：
    /// 秒数大了浮点误差跟着变大，窗口按秒往后挪、往后跳都不准。
    /// </summary>
    private const double MaxSeconds = 1099511627776;

    public static DifficultyEstimate Estimate(IReadOnlyList<TileHit> hits, double playbackRate = 1)
    {
        var features = Measure(hits, playbackRate);
        var score = hits.Count < 3 ? 1 : DifficultyModel.Predict(features.Values);
        var tier = DifficultyTiers.Clamp((int)Math.Round(score, MidpointRounding.AwayFromZero));
        return new DifficultyEstimate(tier, score, features);
    }

    public static DifficultyFeatures Measure(IReadOnlyList<TileHit> hits, double playbackRate = 1)
    {
        var values = new double[DifficultyFeatures.Names.Length];
        if (hits.Count < 3)
        {
            return new DifficultyFeatures(values);
        }

        var rate = playbackRate > 0 ? playbackRate : 1;
        var n = hits.Count;
        var t = new double[n];
        var angles = new double[n];
        for (var i = 0; i < n; i++)
        {
            t[i] = hits[i].Time / rate;
            angles[i] = hits[i].Angle;
        }

        Array.Sort(t, angles);
        var seconds = Math.Max(t[n - 1] - t[0], 1);
        var gaps = new double[n - 1];
        var spaced = new List<double>(n); // 不算多押的间隔
        for (var i = 1; i < n; i++)
        {
            gaps[i - 1] = t[i] - t[i - 1];
            if (gaps[i - 1] >= ChordGap)
            {
                spaced.Add(gaps[i - 1]);
            }
        }

        if (spaced.Count < 2)
        {
            spaced = new List<double> { 1, 1 };
        }

        var density = LocalDensity(t);
        var sortedDensity = (double[])density.Clone();
        Array.Sort(sortedDensity);
        var rolls = RollSizes(gaps);

        values[0] = Math.Log(n);
        values[1] = Math.Log(1 + (n / seconds));
        values[2] = Math.Log(1 + Percentile(sortedDensity, 50));
        values[3] = Math.Log(1 + sortedDensity[n - 1]);
        values[4] = Share(gaps, g => g < 0.060);
        values[5] = Share(gaps, g => g < ChordGap);
        values[6] = Math.Log(1 + Strain(t, 0.3));
        values[7] = Math.Log(1 + Strain(t, 4.0));
        values[8] = GapEntropy(spaced);
        values[9] = FastTrigramEntropy(spaced);
        values[10] = Math.Log(1 + PeakVariety(t, gaps));
        values[11] = Math.Log(1 + (SpeedJumps(gaps, angles) * 60 / seconds));
        values[12] = Math.Log(seconds);
        values[13] = Sum(rolls, size => size >= BigRoll) / n;
        values[14] = Math.Log(1 + Percentile(rolls, 95));
        return new DifficultyFeatures(values);
    }

    /// <summary>每个音符前后各 1 秒内按了几下，除以 2 秒（每秒几下）。</summary>
    private static double[] LocalDensity(double[] t)
    {
        var result = new double[t.Length];
        int left = 0, right = 0;
        for (var i = 0; i < t.Length; i++)
        {
            while (t[left] < t[i] - 1.0)
            {
                left++;
            }

            while (right < t.Length && t[right] <= t[i] + 1.0)
            {
                right++;
            }

            result[i] = (right - left) / 2.0;
        }

        return result;
    }

    /// <summary>分位数，相邻两个值之间线性插值（和 numpy.percentile 的默认算法一样）。</summary>
    private static double Percentile(double[] sorted, double q)
    {
        var pos = q / 100 * (sorted.Length - 1);
        var lo = (int)Math.Floor(pos);
        var hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + ((sorted[hi] - sorted[lo]) * (pos - lo));
    }

    private static double Share(double[] values, Func<double, bool> test)
    {
        var count = 0;
        foreach (var v in values)
        {
            count += test(v) ? 1 : 0;
        }

        return count / (double)values.Length;
    }

    private static double Sum(double[] values, Func<double, bool> test)
    {
        var sum = 0.0;
        foreach (var v in values)
        {
            sum += test(v) ? v : 0;
        }

        return sum;
    }

    /// <summary>
    /// 每个滚键组有几个音（从小到大）：相邻间隔都短于 RollGap 的一串算一组，多押也算在组里。
    /// 一组也没有时当成一组 1 个音。
    /// </summary>
    private static double[] RollSizes(double[] gaps)
    {
        var sizes = new List<double>();
        var run = 0;
        foreach (var g in gaps)
        {
            if (g < RollGap)
            {
                run++;
            }
            else if (run > 0)
            {
                sizes.Add(run + 1);
                run = 0;
            }
        }

        if (run > 0)
        {
            sizes.Add(run + 1);
        }

        if (sizes.Count == 0)
        {
            sizes.Add(1);
        }

        var result = sizes.ToArray();
        Array.Sort(result);
        return result;
    }

    /// <summary>
    /// 负荷：每按一下加 1，按半衰期 halfLife 秒衰减，除以半衰期（≈ 每秒几下）。
    /// 所有音符上的负荷从大到小、按 0.98 的比例加权平均：只有一小段很密的谱比整首都密的谱低。
    /// </summary>
    private static double Strain(double[] t, double halfLife)
    {
        var values = new double[t.Length];
        var strain = 0.0;
        var last = t[0];
        for (var i = 0; i < t.Length; i++)
        {
            strain = (strain * Math.Pow(0.5, (t[i] - last) / halfLife)) + 1;
            last = t[i];
            values[i] = strain / halfLife;
        }

        Array.Sort(values);
        double total = 0, weights = 0, w = 1;
        for (var i = values.Length - 1; i >= 0; i--)
        {
            total += values[i] * w;
            weights += w;
            w *= 0.98;
        }

        return total / weights;
    }

    /// <summary>间隔种类的熵：间隔按 1/6 个八度分组，种类越多、越均匀，节奏越杂。</summary>
    private static double GapEntropy(List<double> spaced)
    {
        var counts = new Dictionary<double, int>();
        foreach (var g in spaced)
        {
            var key = Math.Round(Math.Log(g) / Math.Log(2) * 6); // 和 numpy.round 一样，.5 取偶数
            counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
        }

        var entropy = 0.0;
        foreach (var c in counts.Values)
        {
            var p = c / (double)spaced.Count;
            entropy -= p * Math.Log(p);
        }

        return entropy;
    }

    /// <summary>
    /// 快的地方的节奏花样：每三个相邻间隔（不算多押）看成一个花样，间隔按 1/4 个八度分组，
    /// 只取第一个间隔短于 FastGap 的，算花样的熵。整齐的连打只有一种花样（0），节奏越花越高。
    /// </summary>
    private static double FastTrigramEntropy(List<double> spaced)
    {
        var buckets = new long[spaced.Count];
        for (var i = 0; i < spaced.Count; i++)
        {
            buckets[i] = (long)Math.Round(Math.Log(spaced[i]) / Math.Log(2) * 4); // .5 取偶数
        }

        var counts = new Dictionary<(long, long, long), int>();
        var total = 0;
        for (var i = 0; i + 2 < spaced.Count; i++)
        {
            if (spaced[i] < FastGap)
            {
                var key = (buckets[i], buckets[i + 1], buckets[i + 2]);
                counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
                total++;
            }
        }

        var entropy = 0.0;
        foreach (var c in counts.Values)
        {
            var p = c / (double)total;
            entropy -= p * Math.Log(p);
        }

        return entropy;
    }

    /// <summary>
    /// 最难几段的节奏种类：从第一下起每隔 1 秒取一个 4 秒窗口（至少 4 下），
    /// 窗口里的间隔（不算多押）按 1/6 个八度分组数有几种，乘以每秒几下；取所有窗口的 95% 分位。
    /// 又密又杂才高：整齐的连打种类少，节奏花但慢的密度低。
    /// 不到 4 下的窗口不算，所以遇到空的一段直接跳到下一个可能凑满 4 下的窗口（结果和一秒一秒数一样）。
    /// 复现（2026-11-19 评审）：原来一秒一秒往后数，一块砖在 1e9 秒要 1.36 秒，在 +∞ 死循环。
    /// </summary>
    private static double PeakVariety(double[] t, double[] gaps)
    {
        var first = t[0];
        if (!(t[t.Length - 1] - first < MaxSeconds))
        {
            return 0; // 有 NaN、无穷大（NaN 排在最前，原来也是一个窗口都不数），或者长到按秒数不过来：坏数据
        }

        var values = new List<double>();
        var kinds = new HashSet<long>();
        int lo = 0, hi = 0;
        for (var k = 0.0; first + k < t[t.Length - 1]; k++)
        {
            var start = first + k;
            while (lo < t.Length && t[lo] < start)
            {
                lo++;
            }

            hi = Math.Max(hi, lo);
            while (hi < t.Length && t[hi] < start + 4)
            {
                hi++;
            }

            if (hi - lo < 4)
            {
                if (lo + 3 >= t.Length)
                {
                    break; // 后面不到 4 下，再往后的窗口也凑不满
                }

                // 往后的窗口要凑满 4 下，第 lo + 3 下得在窗口里：起点要晚于它 − 4 秒。少跳一两秒，浮点误差也不会跳过该数的窗口
                k = Math.Max(k, Math.Floor(t[lo + 3] - first - 4) - 2);
                continue;
            }

            kinds.Clear();
            for (var i = lo; i < hi - 1; i++)
            {
                if (gaps[i] >= ChordGap)
                {
                    kinds.Add((long)Math.Round(Math.Log(gaps[i]) / Math.Log(2) * 6));
                }
            }

            values.Add(kinds.Count * (hi - lo) / 4.0);
        }

        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.ToArray();
        Array.Sort(sorted);
        return Percentile(sorted, 95);
    }

    /// <summary>
    /// 突然变速的次数：每度转角用的时间（≈ 星球转得多快）和上一下比，变快或变慢超过 SpeedJumpRatio 倍算一次。
    /// 只看有转角、且和上一下不算同时按的音符（第一下没有上一下，不算）。没有转角时为 0。
    /// </summary>
    private static int SpeedJumps(double[] gaps, double[] angles)
    {
        var limit = Math.Log(SpeedJumpRatio);
        var jumps = 0;
        var last = double.NaN;
        for (var i = 1; i < angles.Length; i++)
        {
            if (!(angles[i] > 0) || gaps[i - 1] < ChordGap)
            {
                continue;
            }

            var speed = gaps[i - 1] / angles[i];
            if (!double.IsNaN(last) && Math.Abs(Math.Log(speed / last)) > limit)
            {
                jumps++;
            }

            last = speed;
        }

        return jumps;
    }
}
