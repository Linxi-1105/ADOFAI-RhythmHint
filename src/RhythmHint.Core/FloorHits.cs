using System;
using System.Collections.Generic;

namespace RhythmHint.Core;

/// <summary>从游戏里读到的一块砖。</summary>
/// <param name="SeqId">砖块序号。</param>
/// <param name="Time">到达时间，秒。</param>
/// <param name="IsHold">长按砖（scrFloor.holdLength > -1）。</param>
/// <param name="NoPress">不需要按键：自动砖、中旋砖、装饰用的假砖。</param>
/// <param name="Angle">星球在这块砖上转过的角度（度，到下一块砖为止）；NaN 表示不知道。</param>
/// <param name="Speed">这块砖的速度倍率（相对关卡起始 BPM），用来核对转角和时间对不对得上。</param>
/// <param name="Taps">关卡要求这块砖按几下（scrFloor.tapsNeeded，关卡事件 Multitap 的 taps；普通砖 1）。</param>
public readonly record struct FloorInfo(int SeqId, double Time, bool IsHold, bool NoPress, double Angle = double.NaN, double Speed = 1, int Taps = 1);

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
    /// 长按砖：按下后按住到下一块砖。下一块不是长按砖时，松开就落上它（游戏里这时再按会被忽略，严格长按还算太早），
    /// 所以它不算一次按键，而是长按的尾巴；下一块也是长按砖时要重新按一下（scrPlayer：下一块是长按时松开不打下一块，
    /// 按键才打），所以连续的长按砖每块都是一次按键（2026-11-20 用户录屏：原来合成一条，照提示按住会死）。
    /// 自动的长按砖后面那块自动落上（OttoHoldHit），也不按。下一块读不到时间（已销毁）时长按是 0。
    /// 每块要按的砖记下从上一块要按的砖到它星球转过的角度：中间不用按的砖（中旋、自动、长按尾巴）的转角累加进去。
    /// 给了 crotchet（起始 BPM 一拍的秒数）时先用 CheckAngles 核对转角记在哪一段；对不上就不信转角，排键退回只看时间。
    /// 不给时按 FloorInfo.Angle 的本意（离开这块砖的转角）用。
    /// 双押砖（FloorInfo.Taps ≥ 2，scrPlanet 里按够 tapsNeeded 下才前进）照样是一块砖、一个提示，TileHit.Taps 记要按几下，
    /// 排键给它几个键；<paramref name="multitapOnce"/>（游戏设置 multitapTileBehavior = HitOnce，第一下就前进）时只按一下。
    /// 收哪些砖、时间、长按、转角都和它无关（谱面指纹不变）。
    /// </summary>
    public static List<TileHit> From(IReadOnlyList<FloorInfo> floors, double crotchet = double.NaN, bool multitapOnce = false)
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
            var prev = floors[i - 1];
            if (f.NoPress || (prev.IsHold && (prev.NoPress || !f.IsHold)))
            {
                continue;
            }

            // 长按的结束早于开始：后面紧跟一块已销毁的砖（GameReader 把它的时间记成 0），或者时间倒退，当普通砖。
            // 原来得出负长按，CustomFingering.From 抛 FormatException，这一关「编辑本关手序」打不开（2026-11-19 评审）。
            // 取砖规则冻结（docs/专属手序.md）：和 1.3.1 只差这种会得出负数的输入、连续的长按砖两处
            var hold = f.IsHold && i + 1 < floors.Count ? Math.Max(0, floors[i + 1].Time - f.Time) : 0;

            var multitap = f.Taps > 1;
            hits.Add(new TileHit(f.SeqId, f.Time, hold, angle, multitap && !multitapOnce ? f.Taps : 1, multitap));
            angle = 0;
        }

        hits.Sort((a, b) => a.Time.CompareTo(b.Time));
        return hits;
    }

    /// <summary>双押砖各要按几下（「砖号x下数」，空格隔开；没有双押砖为空）：谱面指纹不含它，缓存套用结果时一起比。</summary>
    public static string TapsKey(IReadOnlyList<TileHit> hits)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var h in hits)
        {
            if (h.Multitap)
            {
                sb.Append(h.TileIndex).Append('x').Append(h.Taps).Append(' ');
            }
        }

        return sb.ToString();
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
