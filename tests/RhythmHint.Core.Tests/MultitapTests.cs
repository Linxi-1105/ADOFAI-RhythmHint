using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 双押砖：关卡事件 Multitap 让一块砖要按好几下（scrFloor.tapsNeeded），游戏里没按够不前进。
/// 复现（2026-11-20 用户：RX-J 的双押方块被识别成单键）：原来一块砖只出一个单押，照提示只按一下就漏。
/// 游戏设置能把它改成只按一下（Persistence.multiTapTileBehavior = HitOnce），这时还是单押。
/// </summary>
public class MultitapTests
{
    private static readonly FingeringOptions Options = new();

    /// <summary>每拍一块砖（120 BPM、180°），共 20 块要按的砖，第 2 块是要按 taps 下的双押砖（套用手序要连续 8 块对上才认）。</summary>
    private static List<FloorInfo> Floors(int taps)
    {
        var floors = new List<FloorInfo>();
        for (var i = 0; i <= 20; i++)
        {
            floors.Add(new FloorInfo(i, i * 0.5, false, false, 180, 1, i == 2 ? taps : 1));
        }

        return floors;
    }

    private static int[] Sizes(IEnumerable<PlannedNote> notes) => notes.Select(n => n.Lanes.Count).ToArray();

    [Fact]
    public void MultitapTileNeedsAsManyKeysAsTaps()
    {
        var hits = FloorHits.From(Floors(2), 0.5);
        var notes = NotePlanner.Plan(hits, Options);

        Assert.Equal(new[] { 1, 2 }.Concat(Enumerable.Repeat(1, 18)), Sizes(notes));
        var chord = notes[1];
        Assert.Equal(2, chord.TileIndex);
        Assert.Equal(2, chord.LastTileIndex); // 一块砖一个提示，按够了才消失
        Assert.Equal(new[] { 2, 3 }, chord.Lanes.Keys); // 双押同一只手，默认右手 J K
    }

    [Fact]
    public void HitOnceSettingKeepsItSingle()
    {
        var hits = FloorHits.From(Floors(2), 0.5, multitapOnce: true);

        Assert.All(NotePlanner.Plan(hits, Options), n => Assert.Single(n.Lanes));
        Assert.True(hits[1].Multitap);
        Assert.Equal(1, hits[1].Taps);
    }

    [Fact]
    public void ChartPrintDoesNotDependOnTaps()
    {
        // 谱面指纹冻结：加了双押砖、换了游戏设置，之前做的手序照样认得出来
        var plain = CustomFingering.ChartPrint(FloorHits.From(Floors(1), 0.5));

        Assert.Equal(plain, CustomFingering.ChartPrint(FloorHits.From(Floors(2), 0.5)));
        Assert.Equal(plain, CustomFingering.ChartPrint(FloorHits.From(Floors(2), 0.5, multitapOnce: true)));
    }

    [Theory]
    [InlineData(0, 1)] // 关卡里写 0 下：游戏里按一下就前进
    [InlineData(3, 3)] // 三押：一只手两键 + 另一只手食指
    [InlineData(4, 4)]
    [InlineData(6, 4)] // 只有四个键
    public void TapCountsBecomeChordSizes(int taps, int keys)
    {
        var notes = NotePlanner.Plan(FloorHits.From(Floors(taps), 0.5), Options);

        Assert.Equal(keys, notes[1].Lanes.Count);
        Assert.Equal(20, notes.Count);
    }

    [Fact]
    public void ChordsOnOtherHandAndLeftHandedApply()
    {
        var hits = FloorHits.From(Floors(2), 0.5);

        Assert.Equal(new[] { 0, 1 }, NotePlanner.Plan(hits, new FingeringOptions { ChordsOnOtherHand = true })[1].Lanes.Keys);
        Assert.Equal(new[] { 0, 1 }, NotePlanner.Plan(hits, new FingeringOptions { LeftHanded = true })[1].Lanes.Keys);
    }

    [Fact]
    public void MultitapJoinsAPseudoChord()
    {
        // 双押砖后面紧跟一块 15°、28 ms 的砖：一共按三下，是一个三押
        var hits = new List<TileHit>
        {
            new(1, 1.0, 0, 180),
            new(2, 1.5, 0, 180, 2, true),
            new(3, 1.528, 0, 15),
            new(4, 2.0, 0, 180),
        };
        var notes = NotePlanner.Plan(hits, Options);

        Assert.Equal(new[] { 1, 3, 1 }, Sizes(notes));
        Assert.Equal((2, 3), (notes[1].TileIndex, notes[1].LastTileIndex));
    }

    [Fact]
    public void MoreThanFourPressesSplitByTile()
    {
        // 四块连着的砖再加一块双押砖是五下，4K 按不了，照旧按砖拆开；双押砖自己还是双押，每块砖正好一个提示
        var hits = new List<TileHit>
        {
            new(1, 1.0, 0, 180),
            new(2, 1.5, 0, 180),
            new(3, 1.52, 0, 10, 2, true),
            new(4, 1.54, 0, 10),
            new(5, 1.56, 0, 10),
            new(6, 2.5, 0, 180),
        };
        var notes = NotePlanner.Plan(hits, Options);

        Assert.Equal(new[] { 1, 1, 2, 1, 1, 1 }, Sizes(notes));
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, notes.Select(n => n.TileIndex));
        Assert.All(notes, n => Assert.Equal(n.TileIndex, n.LastTileIndex));
    }

    /// <summary>专属手序：按「只按一下」做的（双押砖上单押）拿到要按两下的游戏里，补上同一只手的另一根手指。</summary>
    [Fact]
    public void CustomFingeringGetsMissingKeyOnSameHand()
    {
        var once = FloorHits.From(Floors(2), 0.5, multitapOnce: true);
        var file = CustomFingering.From(once, NotePlanner.Plan(once, Options), leftHanded: false);
        file.Notes[1].Lanes.Clear();
        file.Notes[1].Lanes[1] = 0; // 作者在双押砖上按 F

        var hits = FloorHits.From(Floors(2), 0.5);
        var match = FingeringMatcher.Apply(file, hits, NotePlanner.Plan(hits, Options), leftHanded: false);

        Assert.Equal(0, match.FallbackNotes);
        Assert.Equal(new[] { 0, 1 }, match.Notes[1].Lanes.Keys); // F 加 D，不跨手
    }

    /// <summary>反过来：按要按两下做的（双押砖上双押）拿到「只按一下」的游戏里，只留食指，免得多按的那下打到下一块砖。</summary>
    [Fact]
    public void CustomFingeringDropsExtraKeyWhenHitOnce()
    {
        var normal = FloorHits.From(Floors(2), 0.5);
        var file = CustomFingering.From(normal, NotePlanner.Plan(normal, Options), leftHanded: false);

        var once = FloorHits.From(Floors(2), 0.5, multitapOnce: true);
        var match = FingeringMatcher.Apply(file, once, NotePlanner.Plan(once, Options), leftHanded: false);

        Assert.Equal(0, match.FallbackNotes);
        Assert.Equal(new[] { 2 }, match.Notes[1].Lanes.Keys); // J K 留 J
    }

    [Fact]
    public void LeftHandedPlayerKeepsOwnIndexFinger()
    {
        var normal = FloorHits.From(Floors(2), 0.5);
        var file = CustomFingering.From(normal, NotePlanner.Plan(normal, Options), leftHanded: false);
        var once = FloorHits.From(Floors(2), 0.5, multitapOnce: true);

        // 作者右手 J K，左撇子玩家镜像成 D F，只按一下时留 F
        var match = FingeringMatcher.Apply(file, once, NotePlanner.Plan(once, new FingeringOptions { LeftHanded = true }), leftHanded: true);

        Assert.Equal(new[] { 1 }, match.Notes[1].Lanes.Keys);
    }

    [Fact]
    public void OfflineReaderReadsMultitapEvents()
    {
        // 离线读谱（试验台、难度校准）也认双押砖：事件放在哪块砖，落到那块砖要按几下；最后一块砖也能放
        const string text = """
            { "angleData": [0, 0, 0, 0],
              "settings": { "version": 13, "bpm": 120, "pitch": 100 },
              "actions": [ { "floor": 2, "eventType": "Multitap", "taps": 2 }, { "floor": 4, "eventType": "Multitap", "taps": 3 } ] }
            """;
        var hits = Reference.AdofaiLevelReader.Parse(text).Hits();

        Assert.Equal(new[] { 1, 2, 1, 3 }, hits.Select(h => h.Taps));
        Assert.Equal(new[] { 1, 2, 1, 3 }, NotePlanner.Plan(hits, Options).Select(n => n.Lanes.Count));
    }

    [Fact]
    public void NotesWithoutMultitapTilesAreUntouched()
    {
        // 第三方工具写的：普通砖上按两个键。不是双押砖的地方照文件来，和以前一样
        var hits = FloorHits.From(Floors(1), 0.5);
        var file = CustomFingering.From(hits, NotePlanner.Plan(hits, Options), leftHanded: false);
        file.Notes[3].Lanes[3] = 0;

        var match = FingeringMatcher.Apply(file, hits, NotePlanner.Plan(hits, Options), leftHanded: false);

        Assert.Equal(file.Notes[3].Lanes.Keys, match.Notes[3].Lanes.Keys);
    }
}
