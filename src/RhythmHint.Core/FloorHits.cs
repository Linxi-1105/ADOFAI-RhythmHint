using System.Collections.Generic;

namespace RhythmHint.Core;

/// <summary>从游戏里读到的一块砖。</summary>
/// <param name="SeqId">砖块序号。</param>
/// <param name="Time">到达时间，秒。</param>
/// <param name="IsHold">长按砖（scrFloor.holdLength > -1）。</param>
/// <param name="NoPress">不需要按键：自动砖、中旋砖、装饰用的假砖。</param>
public readonly record struct FloorInfo(int SeqId, double Time, bool IsHold, bool NoPress);

/// <summary>把关卡的砖块变成「需要按键的砖」。</summary>
public static class FloorHits
{
    /// <summary>
    /// 第 0 块（起点）不按；自动砖、中旋砖、假砖不按。
    /// 长按砖：按下后一直按住，走到长按结束那块砖时是自动落上的（游戏里这时再按会被忽略，严格长按还算太早），
    /// 所以结束那块不算一次按键，而是长按的尾巴；连续几块长按砖合成一条长按。
    /// </summary>
    public static List<TileHit> From(IReadOnlyList<FloorInfo> floors)
    {
        var hits = new List<TileHit>(floors.Count);
        for (var i = 1; i < floors.Count; i++)
        {
            var f = floors[i];
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

            hits.Add(new TileHit(f.SeqId, f.Time, hold));
        }

        hits.Sort((a, b) => a.Time.CompareTo(b.Time));
        return hits;
    }
}
