using System;
using System.Collections.Generic;
using System.Linq;

namespace RhythmHint.Core;

/// <summary>衡量排键「散不散」的统计，预览工具和测试共用。</summary>
public static class LayoutStats
{
    /// <summary>前后两下隔得不超过它才算「挨着」（统计孤立音符、跳轨时用）。</summary>
    public const double DefaultLinkGap = 0.3;

    /// <summary>
    /// 孤立音符：和前一下、后一下都隔着 2 条以上轨道（双押按离它最近的那条算），
    /// 而且前后两下都和它挨着（间隔不超过 linkGap）。看起来像从队伍里掉出来的一个点。
    /// </summary>
    public static List<PlannedNote> LoneNotes(IReadOnlyList<PlannedNote> notes, double linkGap)
    {
        var lone = new List<PlannedNote>();
        for (var i = 1; i + 1 < notes.Count; i++)
        {
            var (prev, cur, next) = (notes[i - 1], notes[i], notes[i + 1]);
            if (cur.Time - prev.Time > linkGap || next.Time - cur.Time > linkGap)
            {
                continue;
            }

            if (Distance(cur, prev) >= 2 && Distance(cur, next) >= 2)
            {
                lone.Add(cur);
            }
        }

        return lone;
    }

    /// <summary>两个音符的轨道最近相隔几条。</summary>
    public static int Distance(PlannedNote a, PlannedNote b) =>
        a.Lanes.Keys.SelectMany(x => b.Lanes.Keys, (x, y) => Math.Abs(x - y)).Min();
}
