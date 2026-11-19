using System;
using System.Collections.Generic;
using System.Linq;

namespace RhythmHint.Core;

/// <summary>
/// 把游戏里砖块的进出方向（scrFloor.entryangle / exitangle，弧度）换成编辑器用的数学角（x 轴正方向为 0、逆时针为正），
/// 进方向指向上一块砖、出方向指向下一块砖。轨道预览按它给每块砖单独画，被移动轨道挪开位置的砖也和游戏里一样。
/// 游戏用哪种角度约定没有文档，不靠猜：用位置没被挪过的相邻砖（间距约等于一块砖长）对一下，试 ±角 + 0 / 90 / 180 / 270 度
/// 共 8 种换算，进方向再试是否差 180 度，对上八成以上才用；对不上就去掉角度，编辑器按相邻砖的位置画。
/// 复现（2026-11-18 用户，官方曲目 AR-X Libertas）：被挪开的砖连中心画出斜线和方框。
/// </summary>
public static class PathAngles
{
    private const double Tolerance = 0.05;
    private const double MinShare = 0.8;
    private const int MinPairs = 5;

    public readonly record struct Result(List<PathPoint> Points, bool Detected, int Pairs, int Matched);

    public static Result Normalize(IReadOnlyList<PathPoint> points)
    {
        var plain = points.Select(p => p with { Entry = double.NaN, Exit = double.NaN }).ToList();
        if (points.Count < 2 || points.All(p => double.IsNaN(p.Exit)))
        {
            return new Result(points.ToList(), false, 0, 0);
        }

        var unit = Median(Enumerable.Range(1, points.Count - 1).Select(i => Distance(points[i - 1], points[i])).Where(d => d > 1e-6));
        var exits = new List<(double Raw, double Direction)>();
        var entries = new List<(double Raw, double Direction)>();
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            if (Math.Abs(Distance(a, b) - unit) > unit * 0.05)
            {
                continue; // 挪开过的砖不拿来对
            }

            if (!double.IsNaN(a.Exit))
            {
                exits.Add((a.Exit, Math.Atan2(b.Y - a.Y, b.X - a.X)));
            }

            if (!double.IsNaN(b.Entry))
            {
                entries.Add((b.Entry, Math.Atan2(a.Y - b.Y, a.X - b.X)));
            }
        }

        if (exits.Count < MinPairs)
        {
            return new Result(plain, false, exits.Count, 0);
        }

        // 出方向：试 8 种换算，挑对上最多的
        var best = (Sign: 1, Turn: 0, Matched: -1);
        foreach (var sign in new[] { 1, -1 })
        {
            for (var turn = 0; turn < 4; turn++)
            {
                var matched = exits.Count(e => Close(Convert(e.Raw, sign, turn, 0), e.Direction));
                if (matched > best.Matched)
                {
                    best = (sign, turn, matched);
                }
            }
        }

        // 进方向：同一种换算，再看是不是差 180 度
        var flip = entries.Count(e => Close(Convert(e.Raw, best.Sign, best.Turn, Math.PI), e.Direction))
            > entries.Count(e => Close(Convert(e.Raw, best.Sign, best.Turn, 0), e.Direction)) ? Math.PI : 0;
        var entryMatched = entries.Count(e => Close(Convert(e.Raw, best.Sign, best.Turn, flip), e.Direction));
        if (best.Matched < MinShare * exits.Count || (entries.Count >= MinPairs && entryMatched < MinShare * entries.Count))
        {
            return new Result(plain, false, exits.Count, best.Matched);
        }

        var converted = points.Select(p => p with
        {
            Entry = double.IsNaN(p.Entry) ? double.NaN : Convert(p.Entry, best.Sign, best.Turn, flip),
            Exit = double.IsNaN(p.Exit) ? double.NaN : Convert(p.Exit, best.Sign, best.Turn, 0),
        }).ToList();
        return new Result(converted, true, exits.Count, best.Matched);
    }

    private static double Convert(double raw, int sign, int turn, double extra) => Wrap((sign * raw) + (turn * Math.PI / 2) + extra);

    private static bool Close(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 2 * Math.PI)) < Tolerance;

    private static double Wrap(double a) => Math.IEEERemainder(a, 2 * Math.PI);

    private static double Distance(PathPoint a, PathPoint b) => Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? 1 : sorted[sorted.Count / 2];
    }
}
