using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 多押：哪些砖算同时到（看星球转过的角度，读不到才看时间），以及三押、四押按哪几个键。
/// 例子里的节奏照着本机关卡写：Timeline 的三押是 180 BPM 下 15° + 15°（每块隔 27.8 ms），
/// 四押是 360 BPM 下 30° × 3；life 的快速连打是 1816 BPM 下 180°（每块隔 33 ms）。
/// </summary>
public class MultiChordTests
{
    private static readonly FingeringOptions Fingering = new();

    /// <summary>一串砖：每项是（和上一块相隔的毫秒数, 转过的角度）。第一块在 1 秒处，前面隔得很远。</summary>
    private static List<TileHit> Tiles(params (double GapMs, double Angle)[] steps)
    {
        var t = 1.0;
        var tiles = new List<TileHit> { new(1, t, 0, 180) };
        foreach (var (gap, angle) in steps)
        {
            t += gap / 1000;
            tiles.Add(new TileHit(tiles.Count + 1, t, 0, angle));
        }

        return tiles;
    }

    private static int[] Sizes(IEnumerable<PlannedNote> notes) => notes.Select(n => n.Lanes.Count).ToArray();

    [Fact]
    public void SmallAngleTripleIsOneTriple()
    {
        // 复现（用户：Timeline 里有三押）：15° + 15° 跨 55.6 ms，旧规则只和第一块比 35 ms，画成双押 + 单押
        var notes = NotePlanner.Plan(Tiles((333, 180), (27.8, 15), (27.8, 15), (333, 180)), Fingering);

        Assert.Equal(new[] { 1, 3, 1 }, Sizes(notes));
    }

    [Fact]
    public void FloatNoiseAroundThresholdStillMerges()
    {
        // 复现（用户截图，Timeline 360 / 720 BPM）：游戏里方向存成单精度浮点，30° 读出来是 30.000004° 或 29.999996°，
        // 卡在 30° 的门槛上时有的合上有的合不上，三押画成单押 + 双押或者四个单押
        var notes = NotePlanner.Plan(Tiles((333, 180), (27.8, 30.000004), (27.8, 29.999996), (333, 180)), Fingering);

        Assert.Equal(new[] { 1, 3, 1 }, Sizes(notes));
    }

    [Fact]
    public void SmallAngleChordAtLowBpmStillMerges()
    {
        // 120 BPM 下 15° 是 41.7 ms，超过旧的 35 ms 窗口，但谱师画的就是双押
        var notes = NotePlanner.Plan(Tiles((500, 180), (41.7, 15), (500, 180)), Fingering);

        Assert.Equal(new[] { 1, 2, 1 }, Sizes(notes));
    }

    [Fact]
    public void FastRunWithNormalAnglesIsNotAChord()
    {
        // 复现（玩家反馈）：快速轮指段被识别成双押。1816 BPM 下 180° 每块隔 33 ms，是连打不是双押
        var steps = Enumerable.Repeat((33.0, 180.0), 15).ToArray();

        var notes = NotePlanner.Plan(Tiles(steps), Fingering);

        Assert.Equal(16, notes.Count);
        Assert.DoesNotContain(notes, n => n.IsChord);
    }

    [Fact]
    public void SmallAngleQuadLightsAllFourKeys()
    {
        var notes = NotePlanner.Plan(Tiles((167, 180), (27.8, 30), (27.8, 30), (27.8, 30), (83, 180)), Fingering);

        Assert.Equal(new[] { 1, 4, 1 }, Sizes(notes));
        Assert.Equal(new[] { 0, 1, 2, 3 }, notes[1].Lanes.Keys);
    }

    [Fact]
    public void FiveOrMoreSmallAngleTilesAreARun()
    {
        // 4K 只有四个键：小转角连成 5 块以上（life 81.9 s：7 块各隔 11 ms）是连打，全拆开
        var steps = Enumerable.Repeat((11.0, 30.0), 6).Append((127.0, 180.0)).ToArray();

        var notes = NotePlanner.Plan(Tiles(steps), Fingering);

        Assert.Equal(8, notes.Count);
        Assert.DoesNotContain(notes, n => n.IsChord);
    }

    [Fact]
    public void SmallAngleFarApartIsNotAChord()
    {
        // 慢歌里转角小、实际隔得很开（40 BPM 下 15° ≈ 104 ms）：两下分开按
        var notes = NotePlanner.Plan(Tiles((500, 180), (104, 15), (500, 180)), Fingering);

        Assert.Equal(new[] { 1, 1, 1, 1 }, Sizes(notes));
    }

    [Fact]
    public void WithoutAnglesFallsBackToTimeWindow()
    {
        // 读不到转角（演示谱、离线工具）：和这一组第一块相隔不到 35 ms 算同时按
        var tiles = new[] { new TileHit(1, 1.0), new TileHit(2, 1.012), new TileHit(3, 1.024), new TileHit(4, 1.5) };

        var notes = NotePlanner.Plan(tiles, Fingering);

        Assert.Equal(new[] { 3, 1 }, Sizes(notes));
    }

    [Theory]
    [InlineData(false, false, new[] { 1, 2, 3 })] // 双押在右手 J K，左手食指补 F
    [InlineData(false, true, new[] { 0, 1, 2 })]  // 双押用另一只手：D F，右手食指补 J
    [InlineData(true, false, new[] { 0, 1, 2 })]  // 左撇子：镜像，D F + J
    [InlineData(true, true, new[] { 1, 2, 3 })]   // 左撇子、双押用另一只手：J K + F
    public void TripleFollowsChordHand(bool leftHanded, bool chordsOnOtherHand, int[] expected)
    {
        // 用户：三押跟着双押用手走，按双押的那只手按两个键，另一只手食指补一个
        var options = new FingeringOptions { LeftHanded = leftHanded, ChordsOnOtherHand = chordsOnOtherHand };

        var notes = NotePlanner.Plan(Tiles((333, 180), (27.8, 15), (27.8, 15)), options);

        Assert.Equal(expected, notes[1].Lanes.Keys);
        Assert.True(notes[1].IsMultiChord);
    }

    [Fact]
    public void TripleUsesOuterFingerWhenThatIndexJustPressed()
    {
        // 左手食指 F 刚按过（100 ms 前）：三押里左手补的那一键改用 D，不连按 F
        var tiles = new List<TileHit>
        {
            new(1, 1.0, 0, 180),
            new(2, 1.1, 0, 180),
            new(3, 1.2, 0, 180),
            new(4, 1.2278, 0, 15),
            new(5, 1.2556, 0, 15),
        };

        var notes = NotePlanner.Plan(tiles, Fingering);

        Assert.Equal(1, notes[1].Lanes.Keys.Single()); // 中速交替：J F
        Assert.Equal(new[] { 0, 2, 3 }, notes[2].Lanes.Keys);
    }

    [Fact]
    public void PairHandIsTheHandWithTwoKeys()
    {
        var triple = new PlannedNote(1, 0);
        foreach (var lane in new[] { 1, 2, 3 })
        {
            triple.Lanes[lane] = 0;
        }

        var leftTriple = new PlannedNote(2, 0);
        foreach (var lane in new[] { 0, 1, 2 })
        {
            leftTriple.Lanes[lane] = 0;
        }

        Assert.Equal(1, triple.PairHand);
        Assert.Equal(0, leftTriple.PairHand);
    }

    [Fact]
    public void FloorHitsAddUpAnglesSincePreviousPress()
    {
        // 转角是从上一块要按的砖算起：中间夹着不用按的砖（中旋、自动砖）时累加
        var floors = new[]
        {
            new FloorInfo(0, 0.0, false, false, 180),
            new FloorInfo(1, 0.5, false, false, 10),
            new FloorInfo(2, 0.52, false, true, 5),
            new FloorInfo(3, 0.53, false, false, 90),
            new FloorInfo(4, 0.78, false, false),
        };

        var hits = FloorHits.From(floors);

        Assert.Equal(new[] { 1, 3, 4 }, hits.Select(h => h.TileIndex));
        Assert.Equal(new[] { 180.0, 15.0, 90.0 }, hits.Select(h => h.Angle));
    }

    /// <summary>
    /// 360 BPM（一拍 1/6 秒）的一段：每项是离开这块砖转过的角度。慢的、中速、三押（30° + 30°）、快一点的轮流出现，
    /// 相邻两段的转角常常不一样，转角记错一块马上和时间对不上。
    /// </summary>
    private static readonly double[] Segments =
        Enumerable.Repeat(new double[] { 180, 180, 90, 90, 30, 30, 120, 180, 90, 60, 60, 180 }, 6).SelectMany(x => x).ToArray();

    /// <summary>按 Segments 排出的砖。arrival = true 时每块砖记的是到达它转过的角度（和游戏字段的另一种可能含义一样）。</summary>
    private static List<FloorInfo> Floors(bool arrival, double scale = 1)
    {
        const double crotchet = 1.0 / 6;
        var floors = new List<FloorInfo>();
        var t = 0.0;
        for (var k = 0; k <= Segments.Length; k++)
        {
            var exit = k < Segments.Length ? Segments[k] : double.NaN;
            var angle = arrival ? (k == 0 ? double.NaN : Segments[k - 1]) : exit;
            floors.Add(new FloorInfo(k, t, false, false, angle * scale, 1));
            t += k < Segments.Length ? exit / 180 * crotchet : 0;
        }

        return floors;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnglesAreLinedUpWithTimesEitherWay(bool arrival)
    {
        // 游戏的 scrFloor.angleLength 没有文档：记的可能是离开这块砖的转角，也可能是到达这块砖的转角。
        // 复现（用户截图）：三押第一块拿到了前一段的转角、画成单押 + 双押。两种对法都和时间核对，用对得上的那种
        var floors = Floors(arrival);

        var check = FloorHits.CheckAngles(floors, 1.0 / 6);
        var notes = NotePlanner.Plan(FloorHits.From(floors, 1.0 / 6), Fingering);

        Assert.Equal(arrival ? AngleSource.Arrival : AngleSource.Exit, check.Source);
        Assert.Equal(6, notes.Count(n => n.Lanes.Count == 3));
    }

    [Theory]
    [InlineData(57.29578)] // 转角其实是度、被当成弧度又换算了一次
    [InlineData(0.0174533)] // 反过来
    public void AnglesThatDoNotMatchTimesFallBackToTime(double wrongScale)
    {
        // 转角和砖块时间对不上（单位理解错、游戏改了字段含义）：不信转角，退回只看时间，至少和旧版一样
        var floors = Floors(arrival: false, scale: wrongScale);

        var hits = FloorHits.From(floors, crotchet: 1.0 / 6);

        Assert.Equal(AngleSource.None, FloorHits.CheckAngles(floors, 1.0 / 6).Source);
        Assert.All(hits, h => Assert.True(double.IsNaN(h.Angle)));
    }

    [Fact]
    public void FloorHitsWithoutAnglesLeaveAngleUnknown()
    {
        var floors = new[] { new FloorInfo(0, 0.0, false, false), new FloorInfo(1, 0.5, false, false) };

        Assert.True(double.IsNaN(FloorHits.From(floors)[0].Angle));
    }

    [Fact]
    public void RandomChartsKeepEveryRule()
    {
        // 随机节奏，夹着小转角的双押、三押、四押和大转角的快连打：每块砖一个提示、双押同手、三押是一只手两键 + 另一只手一键、
        // 同一条轨道不在 JackWindow 内连按两下
        var rng = new Random(17);
        foreach (var chordsOnOtherHand in new[] { false, true })
        {
            var options = new FingeringOptions { ChordsOnOtherHand = chordsOnOtherHand };
            for (var round = 0; round < 40; round++)
            {
                var t = 0.0;
                var tiles = new List<TileHit>();
                for (var i = 0; i < 150; i++)
                {
                    var gap = new[] { 0.033, 0.06, 0.094, 0.15, 0.2, 0.3 }[rng.Next(6)];
                    t += gap;
                    tiles.Add(new TileHit(tiles.Count + 1, t, 0, 180));
                    for (var extra = rng.Next(5) == 0 ? 1 + rng.Next(3) : 0; extra > 0; extra--)
                    {
                        t += 0.0278;
                        tiles.Add(new TileHit(tiles.Count + 1, t, 0, 15));
                    }
                }

                var notes = NotePlanner.Plan(tiles, options);

                var covered = notes.SelectMany(n => Enumerable.Range(n.TileIndex, n.LastTileIndex - n.TileIndex + 1));
                Assert.Equal(tiles.Select(x => x.TileIndex), covered);
                foreach (var n in notes.Where(n => n.Lanes.Count is 2 or 3))
                {
                    var perHand = n.Lanes.Keys.GroupBy(Lanes.HandOf).Select(g => g.Count()).OrderBy(c => c).ToArray();
                    Assert.True(n.Lanes.Count == 2 ? perHand.SequenceEqual(new[] { 2 }) : perHand.SequenceEqual(new[] { 1, 2 }),
                        $"第 {round} 轮 {n.Time:F3}s 按 {string.Join("+", n.Lanes.Keys)}");
                }

                for (var i = 1; i < notes.Count; i++)
                {
                    if (notes[i].Lanes.Count + notes[i - 1].Lanes.Count > Lanes.Count)
                    {
                        continue; // 四个键不够分（四押后面紧跟一下、三押挨着双押）：只能重复一个键
                    }

                    var shared = notes[i].Lanes.Keys.Intersect(notes[i - 1].Lanes.Keys).Any();
                    Assert.False(shared && notes[i].Time - notes[i - 1].Time < options.JackWindow - 1e-6,
                        $"第 {round} 轮 {notes[i].Time:F3}s 和上一下用了同一条轨道");
                }
            }
        }
    }
}
