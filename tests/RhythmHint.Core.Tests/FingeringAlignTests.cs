using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 节奏对齐在「一串一样的砖」里的走法（2026-11-19 评审 A 高1）：最长公共子序列在均匀的连打里有很多条一样长的答案，
/// 原来平手时先选「跳过一块砖」，改一块砖的转角就让前面整段对到文件的下一块（相位反了，接缝处多出同轨连按）。
/// 现在一样长时选断开次数最少的：对不上的砖原地对着，删掉、加进来的砖只在改动的地方错开。
/// </summary>
public class FingeringAlignTests
{
    private const double Gap = 0.125;

    /// <summary>一串均匀的砖：间隔 0.125 秒、转角 45°（8 分音符的旋转）。</summary>
    private static List<TileHit> Stream(int count)
    {
        var hits = new List<TileHit>();
        for (var i = 0; i < count; i++)
        {
            hits.Add(new TileHit(i + 1, 1.0 + (i * Gap), 0, 45));
        }

        return hits;
    }

    /// <summary>作者手排的 F J 交替（自动排键在这么快的连打里不一定这样排）。</summary>
    private static CustomFingering Alternating(List<TileHit> hits)
    {
        var notes = hits.Select((h, i) =>
        {
            var n = new PlannedNote(h.TileIndex, h.Time);
            n.Lanes[i % 2 == 0 ? 1 : 2] = 0;
            return n;
        }).ToList();
        return CustomFingering.Parse(CustomFingering.From(hits, notes, leftHanded: false).ToJson());
    }

    /// <summary>对上的砖都对到「应该对的那块」（<paramref name="expected"/> 给出本关第 i 块真正是文件里的第几块，-1 是新加的砖）。</summary>
    private static void AssertNoShift(int[] map, Func<int, int> expected)
    {
        var wrong = Enumerable.Range(0, map.Length).Where(i => map[i] >= 0 && map[i] != expected(i)).ToList();
        Assert.True(wrong.Count == 0, "错位：" + string.Join(" ", wrong.Select(i => $"{i}->{map[i]}")));
    }

    /// <summary>用上的专属音符都在真正对应的那块砖上，键和文件里那块砖的一样（没有被错位的键顶替）。这里的文件一块砖一个音符。</summary>
    private static void AssertKeysFollowTheFile(CustomFingering file, List<TileHit> level, FingeringMatch match, Func<int, int> expected)
    {
        var levelIndex = level.Select((h, i) => (h.TileIndex, i)).ToDictionary(x => x.TileIndex, x => x.i);
        for (var i = 0; i < match.Notes.Count; i++)
        {
            if (match.FromAuto[i])
            {
                continue;
            }

            var f = expected(levelIndex[match.Notes[i].TileIndex]);
            Assert.True(f >= 0, $"新加的砖用了专属手序：{match.Notes[i].TileIndex}");
            Assert.Equal(string.Join("+", file.Notes[f].Lanes.Keys), string.Join("+", match.Notes[i].Lanes.Keys));
        }
    }

    /// <summary>
    /// 复现（评审 A 高1 最小例）：60 块 45°、间隔 0.125 秒的砖，作者 F J 交替，只把第 30 块的转角改成 90°。
    /// 原来前 30 块全对到文件的下一块（JFJF…），多出一处 125 毫秒的 J 连按，状态照样「跟上」。
    /// </summary>
    [Fact]
    public void OneChangedTurnInAnEvenStreamKeepsThePhase()
    {
        var hits = Stream(60);
        var file = Alternating(hits);
        var level = hits.ToList();
        level[30] = level[30] with { Angle = 90 };

        var map = FingeringMatcher.Align(level, hits);
        var match = FingeringMatcher.Apply(file, level, NotePlanner.Plan(level, new FingeringOptions()), leftHanded: false);

        AssertNoShift(map, i => i);
        Assert.Equal(-1, map[30]);
        Assert.Equal(59, match.MatchedHits);
        Assert.Equal(1, match.FallbackSections);
        AssertKeysFollowTheFile(file, level, match, i => i);
    }

    /// <summary>谱师改了一块砖的角度，这块砖和后面的时间都变了（和 CustomFingeringTests 一样的改法），落在均匀的连打里。</summary>
    [Fact]
    public void OneRetimedTileInAnEvenStreamKeepsThePhase()
    {
        var hits = Stream(80);
        var file = Alternating(hits);
        var level = hits.Select((h, i) => i < 40 ? h : h with { Time = h.Time + Gap, Angle = i == 40 ? 90 : h.Angle }).ToList();

        var map = FingeringMatcher.Align(level, hits);
        var match = FingeringMatcher.Apply(file, level, NotePlanner.Plan(level, new FingeringOptions()), leftHanded: false);

        AssertNoShift(map, i => i);
        Assert.Equal(79, match.MatchedHits);
        AssertKeysFollowTheFile(file, level, match, i => i);
    }

    /// <summary>连打中间加了一块砖（夹在两块中间）：前后两段都按原来的键，只有加进来的砖和它后面那块退回自动。</summary>
    [Fact]
    public void InsertedTileInAnEvenStreamOnlyShiftsAfterIt()
    {
        var hits = Stream(60);
        var file = Alternating(hits);
        var level = hits.Take(30).Append(new TileHit(31, hits[29].Time + (Gap / 2), 0, 15))
            .Concat(hits.Skip(30).Select(h => h with { TileIndex = h.TileIndex + 1 })).ToList();
        Func<int, int> truth = i => i < 30 ? i : i == 30 ? -1 : i - 1;

        var map = FingeringMatcher.Align(level, hits);
        var match = FingeringMatcher.Apply(file, level, NotePlanner.Plan(level, new FingeringOptions()), leftHanded: false);

        AssertNoShift(map, truth);
        Assert.Equal(59, match.MatchedHits);
        AssertKeysFollowTheFile(file, level, match, truth);
    }

    /// <summary>连打中间删掉一块砖（后面的砖时间不变，接缝处的间隔变成两倍）：前面那段不跟着错开。</summary>
    [Fact]
    public void RemovedTileInAnEvenStreamOnlyShiftsAfterIt()
    {
        var hits = Stream(60);
        var file = Alternating(hits);
        var level = hits.Where((_, i) => i != 30).ToList();
        Func<int, int> truth = i => i < 30 ? i : i + 1;

        var map = FingeringMatcher.Align(level, hits);
        var match = FingeringMatcher.Apply(file, level, NotePlanner.Plan(level, new FingeringOptions()), leftHanded: false);

        AssertNoShift(map, truth);
        Assert.Equal(58, match.MatchedHits);
        AssertKeysFollowTheFile(file, level, match, truth);
    }

    /// <summary>不规则的节奏中间夹着一段连打，连打里改了一块砖：连打前后的不规则段本来就认得出，连打也不能错开。</summary>
    [Fact]
    public void ChangedTurnInAStreamBetweenOtherRhythms()
    {
        var random = new Random(5);
        var beats = Enumerable.Range(0, 40).Select(_ => new[] { 0.5, 0.25, 0.75, 1.0 }[random.Next(4)])
            .Concat(Enumerable.Repeat(0.25, 50))
            .Concat(Enumerable.Range(0, 40).Select(_ => new[] { 0.5, 0.25, 0.75, 1.0 }[random.Next(4)])).ToList();
        var hits = new List<TileHit>();
        var t = 1.0;
        foreach (var b in beats)
        {
            t += b * 0.5;
            hits.Add(new TileHit(hits.Count + 1, t, 0, b * 180));
        }

        var file = Alternating(hits);
        var level = hits.ToList();
        level[70] = level[70] with { Angle = 120 };

        var map = FingeringMatcher.Align(level, hits);
        var match = FingeringMatcher.Apply(file, level, NotePlanner.Plan(level, new FingeringOptions()), leftHanded: false);

        AssertNoShift(map, i => i);
        Assert.Equal(hits.Count - 1, match.MatchedHits);
        AssertKeysFollowTheFile(file, level, match, i => i);
    }

    /// <summary>随便造的谱：不规则的间隔、连打、同时按、长按、读不到转角的砖都有。</summary>
    private static List<TileHit> Mixed(int count, int seed)
    {
        var random = new Random(seed);
        var hits = new List<TileHit>();
        var t = 2.0;
        for (var i = 0; i < count; i++)
        {
            var beat = i % 50 < 20 ? 0.25 : new[] { 0.0, 0.25, 0.5, 0.75, 1.0, 1.5 }[random.Next(6)];
            t += beat * 0.4;
            var angle = random.Next(15) == 0 ? double.NaN : beat * 180;
            hits.Add(new TileHit(i + 1, t, random.Next(12) == 0 ? 0.3 : 0, angle));
        }

        return hits;
    }

    /// <summary>
    /// 谱面的节奏没改过（指纹一样，或者逐块都在容差里）时不做带状对齐，直接一一对应（评审 A 中低2：3.5 万块的谱指纹一样也要算 140 毫秒，
    /// 同一首歌 5 份手序 0.67 秒）。结果要和对齐完全一样：公开的 Align 一直走带状对齐，它给的答案就是一一对应；
    /// 套用后整首都是专属手序、键和文件一样。不到 MinRun 块的谱一一对应也只有一段，照旧全部退回自动。
    /// </summary>
    [Theory]
    [InlineData(300, 1)]
    [InlineData(2000, 2)]
    [InlineData(60, 3)]
    [InlineData(7, 4)]
    public void UnchangedRhythmIsOneToOneLikeTheAlignment(int count, int seed)
    {
        var hits = Mixed(count, seed);
        var notes = NotePlanner.Plan(hits, new FingeringOptions());
        var file = CustomFingering.Parse(CustomFingering.From(hits, notes, leftHanded: false).ToJson());
        var random = new Random(seed);

        // 改偏移（指纹一样）；每块砖的间隔、转角各抖不到半毫秒、半度（指纹不一定一样，但都算同一块砖）
        var offset = hits.ConvertAll(h => h with { Time = h.Time + 0.317 });
        var t = 2.0;
        var jitter = new List<TileHit>();
        for (var i = 0; i < hits.Count; i++)
        {
            t += (i == 0 ? 0 : hits[i].Time - hits[i - 1].Time) + ((random.NextDouble() - 0.5) * 0.0008);
            jitter.Add(hits[i] with { Time = t, Angle = hits[i].Angle + ((random.NextDouble() - 0.5) * 0.8) });
        }

        Assert.Equal(CustomFingering.ChartPrint(hits), CustomFingering.ChartPrint(offset));
        foreach (var level in new[] { hits, offset, jitter })
        {
            var auto = NotePlanner.Plan(level, new FingeringOptions());
            var match = FingeringMatcher.Apply(file, level, auto, leftHanded: false);

            Assert.Equal(Enumerable.Range(0, count), FingeringMatcher.Align(level, file.Hits));
            if (count >= FingeringMatcher.MinRun)
            {
                Assert.Equal(count, match.MatchedHits);
                Assert.Equal(0, match.FallbackNotes);
                Assert.Equal(notes.Select(n => (n.TileIndex, n.LastTileIndex, string.Join("+", n.Lanes.Keys))),
                    match.Notes.Select(n => (n.TileIndex, n.LastTileIndex, string.Join("+", n.Lanes.Keys))));
            }
            else
            {
                Assert.Equal(0, match.MatchedHits);
            }
        }

        var choice = FingeringMatcher.Choose(new[] { new FingeringCandidate("x.json", file) }, offset, NotePlanner.Plan(offset, new FingeringOptions()), false);
        Assert.Equal(FingeringReason.Exact, choice.Reason);
    }
}
