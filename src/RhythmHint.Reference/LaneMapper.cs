using System;
using System.Collections.Generic;
using System.Linq;

using RhythmHint.Core;

namespace RhythmHint.Reference;

/// <summary>
/// 把谱面音符的横向位置分成四条轨道。
/// 4K 转谱一般只用几个固定位置，但同一条轨道可能有两个接近的值（例如 -405 和 -337.5），
/// 所以不按固定宽度切，而是在相邻位置之间最大的三个空档处切开。
/// </summary>
public sealed class LaneMapper
{
    private readonly double[] _cuts;

    private LaneMapper(double[] cuts)
    {
        _cuts = cuts;
    }

    /// <summary>切分点（升序，三个）。小于第一个切分点是轨道 0，依此类推。</summary>
    public IReadOnlyList<double> Cuts => _cuts;

    public static LaneMapper FromPositions(IEnumerable<double> positions)
    {
        // 四舍五入到 0.5，避免浮点误差把同一位置算成两个
        var xs = positions.Select(x => Math.Round(x * 2) / 2).Distinct().OrderBy(x => x).ToArray();
        if (xs.Length == 0)
        {
            return new LaneMapper(new[] { -1.0, 0.0, 1.0 });
        }

        if (xs.Length <= Lanes.Count)
        {
            // 位置不超过四个：从左到右依次占轨道，少于四个时尽量散开
            var lanesOf = Enumerable.Range(0, xs.Length)
                .Select(i => xs.Length == 1 ? 1 : (int)Math.Round(i * (Lanes.Count - 1) / (double)(xs.Length - 1)))
                .ToArray();
            var cuts = new double[Lanes.Count - 1];
            for (var c = 0; c < cuts.Length; c++)
            {
                // 第 c 个切分点：最后一个轨道号 <= c 的位置和第一个轨道号 > c 的位置之间
                var left = Array.FindLastIndex(lanesOf, l => l <= c);
                var right = Array.FindIndex(lanesOf, l => l > c);
                cuts[c] = (left, right) switch
                {
                    ( < 0, _) => xs[0] - 1,
                    (_, < 0) => xs[xs.Length - 1] + 1,
                    _ => (xs[left] + xs[right]) / 2,
                };
            }

            return new LaneMapper(cuts);
        }

        // 位置多于四个：找相邻位置之间最大的三个空档，在空档中点切开
        var gaps = Enumerable.Range(0, xs.Length - 1)
            .Select(i => (Index: i, Size: xs[i + 1] - xs[i]))
            .OrderByDescending(g => g.Size)
            .ThenBy(g => g.Index)
            .Take(Lanes.Count - 1)
            .Select(g => (xs[g.Index] + xs[g.Index + 1]) / 2)
            .OrderBy(x => x)
            .ToArray();
        return new LaneMapper(gaps);
    }

    public int LaneOf(double positionX)
    {
        var lane = 0;
        while (lane < _cuts.Length && positionX >= _cuts[lane])
        {
            lane++;
        }

        return lane;
    }
}
