// 1.3.1 原样，不能改：git show v1.3.1:src/RhythmHint.Core/FloorHits.cs，只把命名空间 RhythmHint.Core 换成 RhythmHint131.Core
// （1.3.0 的这个文件和 1.3.1 一字不差）。FingeringFormatLockTests 拿它和现在的 FloorHits 逐项比：取砖规则是手序格式的一部分，
// 只有 1.3.1 会算出负长按的那一种输入（长按链后面紧跟已销毁的砖）现在不一样，别的输入结果必须完全一样。
using System;
using System.Collections.Generic;

namespace RhythmHint131.Core;

/// <summary>从游戏里读到的一块砖。</summary>
/// <param name="SeqId">砖块序号。</param>
/// <param name="Time">到达时间，秒。</param>
/// <param name="IsHold">长按砖（scrFloor.holdLength > -1）。</param>
/// <param name="NoPress">不需要按键：自动砖、中旋砖、装饰用的假砖。</param>
/// <param name="Angle">星球在这块砖上转过的角度（度，到下一块砖为止）；NaN 表示不知道。</param>
/// <param name="Speed">这块砖的速度倍率（相对关卡起始 BPM），用来核对转角和时间对不对得上。</param>
public readonly record struct FloorInfo(int SeqId, double Time, bool IsHold, bool NoPress, double Angle = double.NaN, double Speed = 1);

/// <summary>每块砖记的转角是哪一段（游戏的 scrFloor.angleLength 没有文档，见 FloorHits.CheckAngles）。</summary>
public enum AngleSource
{
    /// <summary>转角和砖块时间对不上，不用转角，排键退回只看时间。</summary>
    None,

    /// <summary>每块砖记的是离开它、转到下一块砖的角度。</summary>
    Exit,

    /// <summary>每块砖记的是从上一块砖转到它的角度。</summary>
    Arrival,
}

/// <summary>转角核对结果。</summary>
/// <param name="Source">用哪种对法。</param>
/// <param name="ExitMatch">按「离开」对时，间隔和转角对得上的相邻砖块比例。</param>
/// <param name="ArrivalMatch">按「到达」对时的比例。</param>
public readonly record struct AngleCheck(AngleSource Source, double ExitMatch, double ArrivalMatch);

/// <summary>把关卡的砖块变成「需要按键的砖」。</summary>
public static class FloorHits
{
    /// <summary>
    /// 第 0 块（起点）不按；自动砖、中旋砖、假砖不按。
    /// 长按砖：按下后一直按住，走到长按结束那块砖时是自动落上的（游戏里这时再按会被忽略，严格长按还算太早），
    /// 所以结束那块不算一次按键，而是长按的尾巴；连续几块长按砖合成一条长按。
    /// 每块要按的砖记下从上一块要按的砖到它星球转过的角度：中间不用按的砖（中旋、自动、长按）的转角累加进去。
    /// 给了 crotchet（起始 BPM 一拍的秒数）时先用 CheckAngles 核对转角记在哪一段；对不上就不信转角，排键退回只看时间。
    /// 不给时按 FloorInfo.Angle 的本意（离开这块砖的转角）用。
    /// </summary>
    public static List<TileHit> From(IReadOnlyList<FloorInfo> floors, double crotchet = double.NaN)
    {
        var hits = new List<TileHit>(floors.Count);
        var source = double.IsNaN(crotchet) ? AngleSource.Exit : CheckAngles(floors, crotchet).Source;
        var angle = 0.0;
        for (var i = 1; i < floors.Count; i++)
        {
            var f = floors[i];
            angle += source switch
            {
                AngleSource.Exit => floors[i - 1].Angle,
                AngleSource.Arrival => f.Angle,
                _ => double.NaN,
            };
            if (f.NoPress || floors[i - 1].IsHold)
            {
                continue;
            }

            var hold = 0.0;
            if (f.IsHold)
            {
                var end = i;
                while (end + 1 < floors.Count && floors[end].IsHold)
                {
                    end++;
                }

                hold = floors[end].Time - f.Time;
            }

            hits.Add(new TileHit(f.SeqId, f.Time, hold, angle));
            angle = 0;
        }

        hits.Sort((a, b) => a.Time.CompareTo(b.Time));
        return hits;
    }

    /// <summary>
    /// 转角记在哪一段：按「离开这块砖」和「到达这块砖」两种对法，各算相邻两块砖的间隔和
    /// 转角 / 180° × 一拍 / 速度倍率 之比，看有多少对和中位数相差不到 3%，多的那种就是（一样多算离开）；
    /// 都不到一半就不信转角。复现（用户截图）：三押第一块拿到了前一段的转角，画成单押 + 双押；
    /// 旧的核对只看比值的中位数，均匀连打里相邻两段转角一样，记错一块也看不出来。
    /// </summary>
    public static AngleCheck CheckAngles(IReadOnlyList<FloorInfo> floors, double crotchet)
    {
        var exit = MatchShare(floors, crotchet, arrival: false);
        var arrival = MatchShare(floors, crotchet, arrival: true);
        var source = Math.Max(exit, arrival) < MinMatchShare ? AngleSource.None
            : exit >= arrival ? AngleSource.Exit : AngleSource.Arrival;
        return new AngleCheck(source, exit, arrival);
    }

    /// <summary>至少这么多相邻砖块对得上才信转角（暂停、长按这类砖时间更长，本来就对不上）。</summary>
    private const double MinMatchShare = 0.5;

    /// <summary>
    /// 对得上的比例。比的是和中位数的差而不是和 1 的差：歌曲变速之类会让所有比值一起偏一点；
    /// 中位数不在 0.5–2 之间说明单位都弄错了（度和弧度差 57 倍），直接算 0。
    /// </summary>
    private static double MatchShare(IReadOnlyList<FloorInfo> floors, double crotchet, bool arrival)
    {
        var ratios = new List<double>(floors.Count);
        for (var i = 0; i + 1 < floors.Count; i++)
        {
            var (f, next) = (floors[i], floors[i + 1]);
            var angle = arrival ? next.Angle : f.Angle;
            var gap = next.Time - f.Time;
            if (f.SeqId < 0 || next.SeqId < 0 || gap <= 0 || !(angle > 0) || !(f.Speed > 0) || !(crotchet > 0))
            {
                continue;
            }

            ratios.Add(gap / (angle / 180 * crotchet / f.Speed));
        }

        if (ratios.Count == 0)
        {
            return 0;
        }

        var sorted = new List<double>(ratios);
        sorted.Sort();
        var median = sorted[sorted.Count / 2];
        if (median is < 0.5 or > 2)
        {
            return 0;
        }

        var match = 0;
        foreach (var r in ratios)
        {
            match += Math.Abs((r / median) - 1) <= 0.03 ? 1 : 0;
        }

        return (double)match / ratios.Count;
    }
}
