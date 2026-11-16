using System;
using System.Collections.Generic;
using System.Linq;

namespace RhythmHint.Core;

/// <summary>
/// 把关卡的砖块变成要画的音符。只看这一关自己的砖块之间的间隔，不读任何外部谱面、不看 BPM 和小节。
/// 思路来自 LaneReader 的「时间重置法」，和原版「全节奏提示」作者的实际按法一致（见 docs/开发说明.md）：
/// <list type="number">
/// <item>同时到（含伪双押，相隔不到 ChordWindow）的砖合成一个「时刻」，画成一个双押（只在 0+1 或 2+3）</item>
/// <item>每个单押只看和前面几下的间隔（R = ResetWindow）：慢（&gt; R）右手食指连点；R/2–R 两只手食指轮流；
/// R/2 以内同一只手两根手指轮指；停下来（&gt; R）总是回到右手食指</item>
/// <item>轮指默认外侧手指先落：K J D F（用户习惯的 4312）</item>
/// <item>双押优先右手（J K）；右手刚按过来不及才放左手</item>
/// <item>紧挨着双押、隔得不超过 ChordFocusWindow 的单押放另一只手，让按双押的手专心按双押</item>
/// <item>同一只手不出现「食指→外侧手指」（F→D、J→K，用户说的 21 式）：外轮换不了顺序时改按另一只手的食指，
/// 下一下要让给左手食指时这一下先用 D</item>
/// <item>同一条轨道不在 JackWindow 内连续按两下</item>
/// </list>
/// 规则是确定的：同一张图每次排出来都一样，节奏相同的地方形状也相同。
/// 以上按右手为主手写；左撇子（LeftHanded）把结果整体左右镜像。
/// </summary>
public static class NotePlanner
{
    /// <summary>比较时间时的容差，避免浮点误差把正好卡在门槛上的间隔判错。</summary>
    private const double Epsilon = 1e-6;

    // 四个键：D（左手外侧）F（左手食指）J（右手食指）K（右手外侧）
    private const int LeftOuter = 0;
    private const int LeftIndex = 1;
    private const int RightIndex = 2;
    private const int RightOuter = 3;

    /// <param name="tiles">需要按键的砖块，按时间排好。</param>
    public static List<PlannedNote> Plan(IReadOnlyList<TileHit> tiles, FingeringOptions options)
    {
        var moments = Moments(tiles, options.ChordWindow);
        var notes = new List<PlannedNote>(moments.Count);
        var rolled = new List<bool>(moments.Count);
        var hands = new HandState();
        var focus = FocusChords(moments, options);
        for (var i = 0; i < moments.Count; i++)
        {
            var moment = moments[i];
            var time = moment[0].Time;
            var last = notes.Count > 0 ? notes[notes.Count - 1] : null;
            if (moment.Count > 1)
            {
                notes.Add(BuildChord(moment, ChordHand(time, last, options)));
                rolled.Add(false);
            }
            else
            {
                var (lane, roll) = hands.Next(time, options);
                if (focus[i] >= 0)
                {
                    // 前面的双押已经排好（一个时刻一个音符，序号相同）；后面的还没排，按「双押优先右手」算右手
                    var chordHand = focus[i] < i ? Lanes.HandOf(notes[focus[i]].Lanes.Keys.First()) : 1;
                    // 用户：右手单押接右手双押，食指、中指配合不过来。双押附近的单押放另一只手，让那只手专心按双押
                    (lane, roll) = (FocusLane(1 - chordHand, time, last, options), false);
                }

                if (lane == LeftIndex && i + 1 < moments.Count && focus[i + 1] == i + 2
                    && moments[i + 1][0].Time - time < options.JackWindow - Epsilon && !Blocked(LeftOuter, time, last, options))
                {
                    // 下一下要让给左手食指、又挨得太近：这一下先用外侧 D，下一下再按 F（从外向内），
                    // 不然下一下只能 F→D（用户：不要有 21 式）
                    (lane, roll) = (LeftOuter, false);
                }

                if (Blocked(lane, time, last, options))
                {
                    (lane, roll) = (NearestFreeLane(lane, time, options, last!), false);
                }

                var note = new PlannedNote(moment[0].TileIndex, time);
                note.Lanes[lane] = moment[0].HoldSeconds;
                notes.Add(note);
                rolled.Add(roll);
            }

            hands.Pressed(notes[notes.Count - 1]);
        }

        if (options.UseRoll && options.OuterRoll)
        {
            OuterFirst(notes, rolled, options);
        }

        return options.LeftHanded ? notes.ConvertAll(Mirror) : notes;
    }

    /// <summary>左右镜像（D↔K、F↔J）：左撇子的排法就是右手为主时的镜像，规则只写一份，两边始终一致。</summary>
    private static PlannedNote Mirror(PlannedNote note)
    {
        var mirrored = new PlannedNote(note.TileIndex, note.Time) { LastTileIndex = note.LastTileIndex };
        foreach (var kv in note.Lanes)
        {
            mirrored.Lanes[Lanes.Count - 1 - kv.Key] = kv.Value;
        }

        return mirrored;
    }

    /// <summary>
    /// 两只手的状态：上一下用的是哪根手指、右手最后一次按是什么时候。
    /// 下一下按哪根手指只看这些和间隔（LaneReader「时间重置法」的状态表，主手是右手）。
    /// </summary>
    private sealed class HandState
    {
        private int last = -1;
        private double lastTime;
        private double lastRight = double.NegativeInfinity;

        /// <returns>这一下的轨道，以及它是不是轮指出来的（外侧手指，外轮时要和前一下换位置）。</returns>
        public (int Lane, bool Roll) Next(double time, FingeringOptions o)
        {
            var r = o.ResetWindow;
            var d = time - lastTime;
            switch (last)
            {
                case RightIndex when d > r + Epsilon:
                    return (RightIndex, false); // 慢：一根手指点得过来
                case RightIndex when d > (r / 2) + Epsilon || !o.UseRoll:
                    return (LeftIndex, false); // 中速：两只手食指轮流
                case RightIndex:
                    return (RightOuter, true); // 太快：右手两根手指轮指
                case RightOuter:
                    return d > r + Epsilon ? (RightIndex, false) : (LeftIndex, false);
                case LeftIndex when o.UseRoll && (d <= (r / 2) + Epsilon || time - lastRight <= r + Epsilon):
                    return (LeftOuter, true); // 右手刚轮过、左手也得轮
                default:
                    return (RightIndex, false); // 起手、左手外侧之后、停顿之后：回到右手食指
            }
        }

        /// <summary>记下实际按的（可能被双押、连按检查改过）。双押当成那只手外侧手指按的：右手双押之后接左手，左手双押之后回右手。</summary>
        public void Pressed(PlannedNote note)
        {
            var first = note.Lanes.Keys.First();
            var lane = note.IsChord ? (Lanes.HandOf(first) == 1 ? RightOuter : LeftOuter) : first;
            last = lane;
            lastTime = note.Time;
            if (Lanes.HandOf(lane) == 1)
            {
                lastRight = note.Time;
            }
        }
    }

    /// <summary>
    /// 每个单押要给哪个双押让手（双押的时刻序号），-1 表示不用让。只有紧挨着双押（前一下或后一下就是双押）、
    /// 而且隔得不超过 ChordFocusWindow 的单押才让；前后都是双押时看离得近的，一样近看后面的。
    /// 中间的单押照常排，不整串压在一只手上 F D 轮流（用户：不要有 21 式、从内向外的楼梯）。
    /// </summary>
    private static int[] FocusChords(List<List<TileHit>> moments, FingeringOptions o)
    {
        var n = moments.Count;
        var focus = new int[n];
        for (var i = 0; i < n; i++)
        {
            focus[i] = -1;
            if (moments[i].Count > 1)
            {
                continue;
            }

            var time = moments[i][0].Time;
            var before = i > 0 && moments[i - 1].Count > 1 ? time - moments[i - 1][0].Time : double.PositiveInfinity;
            var after = i + 1 < n && moments[i + 1].Count > 1 ? moments[i + 1][0].Time - time : double.PositiveInfinity;
            if (after <= o.ChordFocusWindow + Epsilon && after <= before + Epsilon)
            {
                focus[i] = i + 1;
            }
            else if (before <= o.ChordFocusWindow + Epsilon)
            {
                focus[i] = i - 1;
            }
        }

        return focus;
    }

    /// <summary>让手的单押按这只手的食指；食指刚按过（会连按）才用外侧手指。</summary>
    private static int FocusLane(int hand, double time, PlannedNote? last, FingeringOptions o)
    {
        var index = Lanes.IndexLane(hand);
        return Blocked(index, time, last, o) ? Lanes.Partner(index) : index;
    }

    /// <summary>把挨得很近的砖合成一个时刻（双押 / 伪双押）。</summary>
    private static List<List<TileHit>> Moments(IReadOnlyList<TileHit> tiles, double chordWindow)
    {
        var moments = new List<List<TileHit>>();
        foreach (var tile in tiles)
        {
            if (moments.Count > 0 && tile.Time - moments[moments.Count - 1][0].Time < chordWindow)
            {
                moments[moments.Count - 1].Add(tile);
            }
            else
            {
                moments.Add(new List<TileHit> { tile });
            }
        }

        return moments;
    }

    /// <summary>用户：双押优先右手（J K）。右手上一下刚按过、同一只手来不及时才放左手。</summary>
    private static int ChordHand(double time, PlannedNote? last, FingeringOptions o)
    {
        bool HandBlocked(int hand) => Blocked(hand * 2, time, last, o) || Blocked((hand * 2) + 1, time, last, o);
        return HandBlocked(1) && !HandBlocked(0) ? 0 : 1;
    }

    /// <summary>双押只能是同一只手的两根手指：轨道 0+1 或 2+3，绝不跨手。三块以上同时到的砖也只画成一只手的双押。</summary>
    private static PlannedNote BuildChord(List<TileHit> moment, int hand)
    {
        var first = moment[0];
        var note = new PlannedNote(first.TileIndex, first.Time) { LastTileIndex = moment[moment.Count - 1].TileIndex };
        var main = Lanes.IndexLane(hand);
        var partner = Lanes.Partner(main);
        note.Lanes[main] = first.HoldSeconds;
        note.Lanes[partner] = 0;
        for (var k = 1; k < moment.Count; k++)
        {
            note.Lanes[partner] = Math.Max(note.Lanes[partner], moment[k].HoldSeconds);
        }

        return note;
    }

    /// <summary>
    /// 外轮：每个轮指出来的外侧手指和它前一下（同一只手的食指）换位置，外侧手指先落。
    /// J K F D 变成 K J D F。换了会造成连按的不换。
    /// </summary>
    private static void OuterFirst(List<PlannedNote> notes, List<bool> rolled, FingeringOptions o)
    {
        for (var i = 1; i < notes.Count; i++)
        {
            var (prev, cur) = (notes[i - 1], notes[i]);
            if (!rolled[i] || prev.IsChord || cur.Time - prev.Time > o.ResetWindow + Epsilon)
            {
                continue;
            }

            var outer = cur.Lanes.Keys.First();
            var index = prev.Lanes.Keys.First();
            if (Lanes.Partner(outer) != index)
            {
                continue;
            }

            if ((i >= 2 && Jacks(notes[i - 2], outer, prev.Time, o))
                || (i + 1 < notes.Count && Jacks(notes[i + 1], index, cur.Time, o)))
            {
                // 换了会连按（前后夹着让手的单押）：这一下改按另一只手的食指，左右手轮流，
                // 不留「食指→外侧手指」（用户：不要有 21 式）
                var other = Lanes.IndexLane(1 - Lanes.HandOf(outer));
                if (!Jacks(prev, other, cur.Time, o) && !(i + 1 < notes.Count && Jacks(notes[i + 1], other, cur.Time, o)))
                {
                    notes[i] = WithLane(cur, other);
                }

                continue;
            }

            notes[i - 1] = WithLane(prev, outer);
            notes[i] = WithLane(cur, index);
        }
    }

    /// <summary>在 time 改按 lane 会不会和相邻那一下（neighbour）同轨又太近。</summary>
    private static bool Jacks(PlannedNote neighbour, int lane, double time, FingeringOptions o) =>
        neighbour.Lanes.ContainsKey(lane) && Math.Abs(neighbour.Time - time) < o.JackWindow - Epsilon;

    private static PlannedNote WithLane(PlannedNote note, int lane)
    {
        var copy = new PlannedNote(note.TileIndex, note.Time) { LastTileIndex = note.LastTileIndex };
        copy.Lanes[lane] = note.Lanes.Values.First();
        return copy;
    }

    /// <summary>这条轨道上一下刚按过（同一条轨道连续两下间隔不到 JackWindow）。</summary>
    private static bool Blocked(int lane, double time, PlannedNote? last, FingeringOptions o) =>
        last is not null && last.Lanes.ContainsKey(lane) && time - last.Time < o.JackWindow - Epsilon;

    /// <summary>
    /// 单押和上一下同轨又太近（通常紧跟在双押后面）：换到上一下没用的轨道里离上一下最近的那条（一样近时取离原位置近的），
    /// 例如 ##.. 之后本来要按 0，就改按 2，接得上又不连按。
    /// </summary>
    private static int NearestFreeLane(int lane, double time, FingeringOptions o, PlannedNote last)
    {
        var best = lane;
        var bestScore = int.MaxValue;
        for (var c = 0; c < Lanes.Count; c++)
        {
            if (Blocked(c, time, last, o))
            {
                continue;
            }

            var score = (last.Lanes.Keys.Min(p => Math.Abs(p - c)) * 10) + Math.Abs(c - lane);
            if (score < bestScore)
            {
                best = c;
                bestScore = score;
            }
        }

        return best;
    }
}
