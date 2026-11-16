using RhythmHint.Core;

namespace RhythmHint.Reference;

/// <summary>谱面里同一时刻的一组音符（单押一个轨道，双押两个轨道）。</summary>
public sealed class ChartGroup
{
    public ChartGroup(double time)
    {
        Time = time;
    }

    public double Time { get; }

    /// <summary>每条轨道的长按时长；null 表示这条轨道这一刻没有音符，0 表示普通音符。</summary>
    public double?[] Holds { get; } = new double?[Lanes.Count];

    public int LaneCount
    {
        get
        {
            var n = 0;
            foreach (var h in Holds)
            {
                if (h.HasValue)
                {
                    n++;
                }
            }

            return n;
        }
    }
}
