using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

public class PlannerTests
{
    private const double Beat = 60.0 / 160;
    private static readonly FingeringOptions Fingering = new();

    private static List<TileHit> Run(double start, int count, double gap, int firstIndex = 1) =>
        Enumerable.Range(0, count).Select(i => new TileHit(firstIndex + i, start + (i * gap))).ToList();

    private static int[] LanesOf(IEnumerable<PlannedNote> notes) => notes.Select(n => n.Lanes.Keys.First()).ToArray();

    private static List<TileHit> Gaps(double start, params double[] gaps)
    {
        var t = start;
        var tiles = new List<TileHit> { new(1, t) };
        foreach (var g in gaps)
        {
            tiles.Add(new TileHit(tiles.Count + 1, t += g));
        }

        return tiles;
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.3)]
    [InlineData(0.6)]
    public void SlowNotesStayOnRightIndex(double gap)
    {
        // 原版「全节奏提示」作者和 LaneReader：间隔超过主手重置间隔（165 ms）时一根手指就点得过来，右手食指连点
        var lanes = LanesOf(NotePlanner.Plan(Run(1.0, 12, gap), Fingering));

        Assert.All(lanes, l => Assert.Equal(2, l));
    }

    [Theory]
    [InlineData(0.09)]
    [InlineData(0.12)]
    [InlineData(0.165)]
    public void MediumSpeedAlternatesIndexFingers(double gap)
    {
        // 83–165 ms：两只手的食指轮流，从右手开始 J F J F
        var lanes = LanesOf(NotePlanner.Plan(Run(1.0, 12, gap), Fingering));

        Assert.Equal(Enumerable.Range(0, 12).Select(i => i % 2 == 0 ? 2 : 1), lanes);
    }

    [Fact]
    public void VeryFastRunRollsOuterFingerFirst()
    {
        // 82 ms 以内一根手指来不及：同一只手两根手指轮流，外侧手指先落，K J D F（用户习惯的 4312）
        var lanes = LanesOf(NotePlanner.Plan(Run(1.0, 12, 0.06), Fingering));

        Assert.Equal(Enumerable.Range(0, 12).Select(i => new[] { 3, 2, 0, 1 }[i % 4]), lanes);
    }

    [Fact]
    public void InnerRollPutsIndexFingerFirst()
    {
        var lanes = LanesOf(NotePlanner.Plan(Run(1.0, 8, 0.06), new FingeringOptions { OuterRoll = false }));

        Assert.Equal(Enumerable.Range(0, 8).Select(i => new[] { 2, 3, 1, 0 }[i % 4]), lanes);
    }

    [Fact]
    public void RollCanBeTurnedOff()
    {
        // 关掉轮指：再快也只用两根食指交替
        var lanes = LanesOf(NotePlanner.Plan(Run(1.0, 12, 0.04), new FingeringOptions { UseRoll = false }));

        Assert.Equal(Enumerable.Range(0, 12).Select(i => i % 2 == 0 ? 2 : 1), lanes);
    }

    [Fact]
    public void BurstInsideSlowRunAlternatesInsteadOfRolling()
    {
        // 复现：Machinegun 30.0s，188 ms 一下中间夹着两下 94 ms，旧规则排成「右左左右」。
        // 现在 94 ms 只是两根食指轮流：慢的单指、快的交替、停下回右手
        var lanes = LanesOf(NotePlanner.Plan(Gaps(1.0, 0.188, 0.094, 0.094, 0.188, 0.188), Fingering));

        Assert.Equal(new[] { 2, 2, 1, 2, 2, 2 }, lanes);
    }

    [Fact]
    public void EveryPauseReturnsToRightHand()
    {
        // 用户：优先右手起手。停顿之后（超过主手重置间隔）不管上一下是谁，都回到右手
        // （轮指段外侧手指先落，所以是 K J …；其余都是右手食指 J）
        var tiles = new List<TileHit>();
        var starts = new List<int>();
        var t = 1.0;
        foreach (var (count, gap) in new[] { (2, 0.12), (3, 0.12), (4, 0.06), (5, 0.06), (6, 0.094), (7, 0.045), (3, 0.05) })
        {
            starts.Add(tiles.Count);
            tiles.AddRange(Run(t, count, gap, tiles.Count + 1));
            t += ((count - 1) * gap) + 0.4;
        }

        var lanes = LanesOf(NotePlanner.Plan(tiles, Fingering));

        Assert.All(starts, s => Assert.Equal(1, Lanes.HandOf(lanes[s])));
        Assert.All(starts.Take(2).Append(starts[4]), s => Assert.Equal(2, lanes[s]));
    }

    [Fact]
    public void IsolatedNotesStartOnRightHand()
    {
        var tiles = new[] { new TileHit(1, 0.0), new TileHit(2, 0.75 * 4), new TileHit(3, (0.75 * 4) + (Beat * 1.5)) };

        Assert.Equal(new[] { 2, 2, 2 }, LanesOf(NotePlanner.Plan(tiles, Fingering)));
    }

    [Fact]
    public void SameRhythmGetsSameShape()
    {
        // 只看间隔：节奏相同的两段排出来完全一样，方便记
        var first = Gaps(1.0, 0.1, 0.05, 0.05, 0.2, 0.1, 0.06);
        var second = Gaps(5.0, 0.1, 0.05, 0.05, 0.2, 0.1, 0.06).Select(x => x with { TileIndex = x.TileIndex + 7 });
        var notes = NotePlanner.Plan(first.Concat(second).ToList(), Fingering);

        Assert.Equal(LanesOf(notes.Take(7)), LanesOf(notes.Skip(7)));
    }

    [Fact]
    public void NoSameLaneTwiceInARowWhenFast()
    {
        // 同一条轨道连续按两下（中间没有别的音符）至少隔 JackWindow
        var rng = new Random(11);
        for (var round = 0; round < 50; round++)
        {
            var t = 0.0;
            var tiles = new List<TileHit>();
            for (var i = 0; i < 150; i++)
            {
                t += 0.04 + (rng.NextDouble() * 0.35);
                tiles.Add(new TileHit(tiles.Count + 1, t));
                if (rng.Next(5) == 0)
                {
                    tiles.Add(new TileHit(tiles.Count + 1, t + 0.01)); // 夹一些双押
                }
            }

            var notes = NotePlanner.Plan(tiles, Fingering);

            for (var i = 1; i < notes.Count; i++)
            {
                var shared = notes[i].Lanes.Keys.Intersect(notes[i - 1].Lanes.Keys).Any();
                Assert.False(shared && notes[i].Time - notes[i - 1].Time < Fingering.JackWindow,
                    $"第 {round} 轮，{notes[i].Time:F3}s 和上一下用了同一条轨道");
            }
        }
    }

    [Fact]
    public void ChordsAreAlwaysOneHand()
    {
        // 复现：出现过左右手各一个的双押（轨道 0 + 3）。冰与火玩家的双押只能是同一只手：12 或 34（轨道 0+1 或 2+3）
        var rng = new Random(5);
        for (var round = 0; round < 50; round++)
        {
            var t = 0.0;
            var tiles = new List<TileHit>();
            for (var i = 0; i < 150; i++)
            {
                t += 0.045 + (rng.NextDouble() * 0.3);
                tiles.Add(new TileHit(tiles.Count + 1, t));
                for (var extra = rng.Next(4) == 0 ? 1 + rng.Next(2) : 0; extra > 0; extra--)
                {
                    tiles.Add(new TileHit(tiles.Count + 1, t + 0.012 * extra)); // 双押 / 三押（伪双押）
                }
            }

            foreach (var n in NotePlanner.Plan(tiles, Fingering).Where(n => n.IsChord))
            {
                var lanes = n.Lanes.Keys.ToArray();
                Assert.True(lanes.SequenceEqual(new[] { 0, 1 }) || lanes.SequenceEqual(new[] { 2, 3 }),
                    $"第 {round} 轮 {n.Time:F3}s 双押在轨道 {string.Join("+", lanes)}");
            }
        }
    }

    [Fact]
    public void ChordsPreferRightHand()
    {
        // 用户：有双押的歌优先右手双押（J K），不要总是左右换
        foreach (var (gap, every) in new[] { (0.25, 4), (0.2, 3), (0.1, 4), (0.15, 2) })
        {
            var tiles = new List<TileHit>();
            for (var i = 0; i < 24; i++)
            {
                var time = 1.0 + (i * gap);
                tiles.Add(new TileHit(tiles.Count + 1, time));
                if (i % every == 0)
                {
                    tiles.Add(new TileHit(tiles.Count + 1, time + 0.01));
                }
            }

            var notes = NotePlanner.Plan(tiles, Fingering);

            Assert.All(notes.Where(n => n.IsChord), n => Assert.Equal(new[] { 2, 3 }, n.Lanes.Keys.ToArray()));
        }
    }

    [Fact]
    public void SinglesNextToRightChordMoveToLeftHand()
    {
        // 复现（用户截图）：J 单押 → J+K 双押 → J 单押，右手食指、中指配合不过来。
        // 双押前后的单押放左手，让右手专心按双押
        var tiles = new List<TileHit> { new(1, 1.0), new(2, 1.11), new(3, 1.135), new(4, 1.33) };

        var notes = NotePlanner.Plan(tiles, Fingering);

        Assert.Equal(new[] { 2, 3 }, notes[1].Lanes.Keys.ToArray());
        Assert.Equal(1, notes[0].Lanes.Keys.Single());
        Assert.Equal(1, notes[2].Lanes.Keys.Single());
    }

    /// <summary>同一只手先食指后外侧手指（F→D、J→K，用户说的「21 式」「从内向外的楼梯」），间隔不超过 ResetWindow 的。</summary>
    private static List<string> IndexToOuterSteps(List<PlannedNote> notes) =>
        notes.Zip(notes.Skip(1), (a, b) => (a, b))
            .Where(p => !p.a.IsChord && !p.b.IsChord && p.b.Time - p.a.Time <= Fingering.ResetWindow + 1e-6)
            .Where(p => (p.a.Lanes.Keys.Single(), p.b.Lanes.Keys.Single()) is (1, 0) or (2, 3))
            .Select(p => $"{p.b.Time:F3}s {p.a.Lanes.Keys.Single()}→{p.b.Lanes.Keys.Single()}")
            .ToList();

    [Theory]
    [InlineData(0.047)]
    [InlineData(0.1)]
    [InlineData(0.15)]
    [InlineData(0.2)]
    public void ChordNeighboursMoveToOtherHand(double gap)
    {
        // 双押 单 单 单 双押 …：紧挨着右手双押的单押放左手，不和双押共用一个键；中间的照常排，
        // 不要整串都压在左手 F D 轮流（用户：不要有 21 式、从内向外的楼梯）
        var tiles = new List<TileHit>();
        for (var i = 0; i < 17; i++)
        {
            var time = 1.0 + (i * gap);
            tiles.Add(new TileHit(tiles.Count + 1, time));
            if (i % 4 == 0)
            {
                tiles.Add(new TileHit(tiles.Count + 1, time + 0.01));
            }
        }

        var notes = NotePlanner.Plan(tiles, Fingering);

        Assert.All(notes.Where(n => n.IsChord), n => Assert.Equal(new[] { 2, 3 }, n.Lanes.Keys.ToArray()));
        for (var i = 1; i < notes.Count; i++)
        {
            if (notes[i].IsChord != notes[i - 1].IsChord)
            {
                var single = notes[i].IsChord ? notes[i - 1] : notes[i];
                Assert.Equal(0, Lanes.HandOf(single.Lanes.Keys.Single()));
            }
        }

        Assert.Empty(IndexToOuterSteps(notes));
    }

    [Fact]
    public void StreamBetweenChordsHasNoLoneNotes()
    {
        // 复现：Machinegun 71.97s，两个双押之间一长串 94 ms 的连打，中间冒出一下右手 J，成了孤立音符
        var tiles = new List<TileHit> { new(1, 1.0), new(2, 1.01) };
        tiles.AddRange(Run(1.094, 13, 0.094, firstIndex: 3));
        tiles.Add(new TileHit(16, 2.316));
        tiles.Add(new TileHit(17, 2.326));

        var notes = NotePlanner.Plan(tiles, Fingering);

        Assert.Empty(LayoutStats.LoneNotes(notes, LayoutStats.DefaultLinkGap));
        Assert.Empty(IndexToOuterSteps(notes));
    }

    [Fact]
    public void NoIndexToOuterStepsInRandomCharts()
    {
        // 用户：不要有 21 式（F→D），尽量避免从内向外的楼梯（J→K 同理）
        var rng = new Random(23);
        for (var round = 0; round < 30; round++)
        {
            var t = 0.0;
            var tiles = new List<TileHit>();
            for (var i = 0; i < 200; i++)
            {
                t += new[] { 0.047, 0.06, 0.094, 0.12, 0.188, 0.3 }[rng.Next(6)];
                tiles.Add(new TileHit(tiles.Count + 1, t));
                if (rng.Next(8) == 0)
                {
                    tiles.Add(new TileHit(tiles.Count + 1, t + 0.01));
                }
            }

            var steps = IndexToOuterSteps(NotePlanner.Plan(tiles, Fingering));
            Assert.True(steps.Count == 0, $"第 {round} 轮：{string.Join("；", steps.Take(5))}");
        }
    }

    [Fact]
    public void SinglesFarFromChordsKeepTheirUsualFinger()
    {
        // 离双押超过 ChordFocusWindow 的单押照常排：慢的右手食指连点
        var tiles = new List<TileHit> { new(1, 1.0), new(2, 1.01) };
        tiles.AddRange(Run(1.6, 4, 0.6, firstIndex: 3));

        var lanes = LanesOf(NotePlanner.Plan(tiles, Fingering));

        Assert.All(lanes.Skip(1), l => Assert.Equal(2, l));
    }

    [Fact]
    public void FastChordStreamStillAlternatesWhenItMust()
    {
        // 双押一个接一个且快到同一只手来不及（< JackWindow）：只能左右手轮流，不能连按
        var tiles = new List<TileHit>();
        for (var i = 0; i < 8; i++)
        {
            tiles.Add(new TileHit(tiles.Count + 1, 1.0 + (i * 0.1)));
            tiles.Add(new TileHit(tiles.Count + 1, 1.0 + (i * 0.1) + 0.01));
        }

        var notes = NotePlanner.Plan(tiles, Fingering);

        Assert.Equal(new[] { 2, 3 }, notes[0].Lanes.Keys.ToArray());
        Assert.True(notes.Zip(notes.Skip(1), (a, b) => Lanes.HandOf(a.Lanes.Keys.First()) != Lanes.HandOf(b.Lanes.Keys.First())).All(x => x));
    }

    [Fact]
    public void Planner_MergesSimultaneousTilesIntoChord()
    {
        var tiles = new[] { new TileHit(1, 1.0), new TileHit(2, 1.005), new TileHit(3, 1.5) };

        var notes = NotePlanner.Plan(tiles, Fingering);

        Assert.Equal(2, notes.Count);
        Assert.True(notes[0].IsChord);
        Assert.Equal(2, notes[0].Lanes.Count);
    }

    [Fact]
    public void Planner_DrawsPseudoDoubleAsOneChord()
    {
        // 官方 CE-TX 的伪双押是两块相隔约 25 ms 的砖，要画成一个双押，两块都按过后才消失
        var tiles = new[] { new TileHit(1, 1.0), new TileHit(2, 1.025), new TileHit(3, 1.2125), new TileHit(4, 1.4) };

        var notes = NotePlanner.Plan(tiles, Fingering);

        Assert.Equal(3, notes.Count);
        Assert.True(notes[0].IsChord);
        Assert.Equal(1.0, notes[0].Time, 6);
        Assert.Equal(1, notes[0].TileIndex);
        Assert.Equal(2, notes[0].LastTileIndex);
    }

    [Fact]
    public void Planner_EveryTileIsCoveredByExactlyOneNote()
    {
        var rng = new Random(3);
        var t = 0.0;
        var tiles = Enumerable.Range(1, 300).Select(i => new TileHit(i, t += 0.01 + (rng.NextDouble() * 0.4))).ToList();

        var notes = NotePlanner.Plan(tiles, Fingering);

        var covered = notes.SelectMany(n => Enumerable.Range(n.TileIndex, n.LastTileIndex - n.TileIndex + 1)).ToList();
        Assert.Equal(tiles.Select(x => x.TileIndex), covered);
    }

    [Fact]
    public void DisplayClock_FreezesWhenPlayerFails()
    {
        // 复现：死后谱面还在继续流动
        var clock = new DisplayClock();

        Assert.Equal(10.0, clock.Advance(10.0, failed: false));
        Assert.Equal(10.5, clock.Advance(10.5, failed: true));
        Assert.Equal(10.5, clock.Advance(11.5, failed: true));
        Assert.Equal(10.5, clock.Advance(12.0, failed: true));

        // 重开或回检查点后恢复跟随歌曲进度
        Assert.Equal(3.0, clock.Advance(3.0, failed: false));
    }

    [Fact]
    public void BeatGrid_MusicalTempoIgnoresPowerOfTwoSpeedChanges()
    {
        // ×2、×0.5 只是砖块疏密变化，拍子不变；×1.5 才是真的换了 BPM
        var tempo = BeatGrid.MusicalTempo(new[] { (0.0, 1.0), (5.0, 2.0), (8.0, 1.0), (10.0, 1.5), (12.0, 3.0) }, 0.375);

        Assert.Equal(new[] { (0.0, 0.375), (10.0, 0.25) }, tempo);
    }

    [Fact]
    public void BeatGrid_DetectsOnBeatTimes()
    {
        var lines = BeatGrid.Build(new[] { (0.0, 0.375) }, 0, 3);

        Assert.True(BeatGrid.IsOnBeat(lines, 0.75));
        Assert.True(BeatGrid.IsOnBeat(lines, 0.755));
        Assert.False(BeatGrid.IsOnBeat(lines, 0.9375)); // 半拍
        Assert.False(BeatGrid.IsOnBeat(lines, 0.84));   // 1/4 拍
    }

    [Fact]
    public void BeatGrid_RestartsAtTempoChange()
    {
        var lines = BeatGrid.Build(new[] { (0.0, 0.5), (1.25, 0.25) }, 0, 2);

        Assert.Equal(new[] { 0.0, 0.5, 1.0, 1.25, 1.5, 1.75, 2.0 }, lines.Select(l => Math.Round(l.Time, 6)));
        Assert.True(lines[0].IsMeasure);
        Assert.True(lines[3].IsMeasure);
    }

    [Fact]
    public void LeftHandedUsesLeftIndexForSlowNotesAndLeftHandForChords()
    {
        // 用户：加左右撇子的设置。左撇子以左手为主手：慢的左手食指 F 连点，双押优先左手 D+F
        var leftHanded = new FingeringOptions { LeftHanded = true };
        var slow = Run(1.0, 8, 0.3);
        slow.Add(new TileHit(9, 4.0));
        slow.Add(new TileHit(10, 4.01));

        var notes = NotePlanner.Plan(slow, leftHanded);

        Assert.All(notes.Take(8), n => Assert.Equal(1, n.Lanes.Keys.Single()));
        Assert.Equal(new[] { 0, 1 }, notes[8].Lanes.Keys);
    }

    [Fact]
    public void LeftHandedIsTheMirrorOfRightHanded()
    {
        // 左撇子就是整套排法左右镜像（D↔K、F↔J）：轮指变成 D F K J，双押旁边的单押换到右手，
        // 双押同手、不出现食指→外侧手指这些约束镜像后照样成立
        var rng = new Random(31);
        for (var round = 0; round < 30; round++)
        {
            var t = 0.0;
            var tiles = new List<TileHit>();
            for (var i = 0; i < 200; i++)
            {
                t += new[] { 0.047, 0.06, 0.094, 0.12, 0.188, 0.3, 0.5 }[rng.Next(7)];
                tiles.Add(new TileHit(tiles.Count + 1, t, rng.Next(10) == 0 ? 0.2 : 0));
                if (rng.Next(6) == 0)
                {
                    tiles.Add(new TileHit(tiles.Count + 1, t + 0.01));
                }
            }

            var right = NotePlanner.Plan(tiles, new FingeringOptions());
            var left = NotePlanner.Plan(tiles, new FingeringOptions { LeftHanded = true });

            Assert.Equal(right.Count, left.Count);
            for (var i = 0; i < right.Count; i++)
            {
                Assert.Equal(right[i].TileIndex, left[i].TileIndex);
                Assert.Equal(right[i].LastTileIndex, left[i].LastTileIndex);
                Assert.Equal(
                    right[i].Lanes.Select(kv => (Lane: Lanes.Count - 1 - kv.Key, Hold: kv.Value)).OrderBy(x => x.Lane),
                    left[i].Lanes.Select(kv => (Lane: kv.Key, Hold: kv.Value)));
            }
        }
    }
}
