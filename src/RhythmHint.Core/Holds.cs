using System;
using System.Collections.Generic;

namespace RhythmHint.Core;

/// <summary>一条正在按住的长按。</summary>
/// <param name="Note">所属音符。</param>
/// <param name="Lane">轨道。</param>
/// <param name="Remaining">还要按住多久，秒。</param>
/// <param name="Progress">已经按住的比例，0..1。</param>
public readonly record struct ActiveHold(PlannedNote Note, int Lane, double Remaining, double Progress);

/// <summary>找出已经按下、还没松开的长按，用来画按住中的动画（长条被逐渐吃掉、轨道持续发亮）。</summary>
public static class Holds
{
    /// <summary>只往回找这么多秒以内按下的音符，更早的长按不可能还没结束。</summary>
    public const double MaxHoldSeconds = 30;

    /// <param name="notes">全部音符，按时间排好。</param>
    /// <param name="nextNote">第一个还没按到的音符下标；它之前的音符都已按下。</param>
    /// <param name="now">当前时间，秒。</param>
    public static List<ActiveHold> Active(IReadOnlyList<PlannedNote> notes, int nextNote, double now)
    {
        var result = new List<ActiveHold>();
        for (var i = Math.Min(nextNote, notes.Count) - 1; i >= 0; i--)
        {
            var note = notes[i];
            if (now - note.Time > MaxHoldSeconds)
            {
                break;
            }

            foreach (var kv in note.Lanes)
            {
                var end = note.Time + kv.Value;
                if (kv.Value > 0 && now < end)
                {
                    var progress = Math.Max(0, Math.Min(1, (now - note.Time) / kv.Value));
                    result.Add(new ActiveHold(note, kv.Key, end - now, progress));
                }
            }
        }

        return result;
    }
}
