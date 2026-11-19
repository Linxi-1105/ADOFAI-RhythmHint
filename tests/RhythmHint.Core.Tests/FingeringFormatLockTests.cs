using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 锁住手序文件格式（rhythmhint-fingering 第 1 版，1.3.0 起在玩家手里，2026-11-19 用户：不能再有破坏性更新）：
/// 谱面指纹和取砖规则的金样例、1.3.x 实际写出的文件（src/RhythmHint.Editor/tests/fixtures/frozen/1.3.x，只读，没有重新生成的开关）、
/// 两边共用的接受 / 拒绝样例（fixtures/conformance，编辑器的 conformance.test.cjs 跑同一批）。
/// 这里的期望值改了就是破坏性更新：玩家手里的手序认不出来、对不上。只能加样例；拒绝的样例以后放宽了可以挪进接受，接受的不能挪进拒绝。
/// </summary>
public class FingeringFormatLockTests
{
    private static string Frozen(string name) => Path.Combine(EditorContractTests.Fixtures, "frozen", "1.3.x", name);

    private static string Conformance => Path.Combine(EditorContractTests.Fixtures, "conformance");

    /// <summary>
    /// 谱面指纹（CustomFingering.ChartPrint）。改了就是破坏性更新：文件里存的 charts、设置里玩家的选择（FingeringChoices）、
    /// 编辑器历史的 Id、浏览器草稿的键全按它记。样例特意放了 .5 毫秒的间隔（62.5 → 62，银行家舍入）、22.5° 这类 .5 度的转角、
    /// 读不到的转角（NaN）、长按、同时按的 0 间隔。时间都是 1/128 秒的倍数，浮点运算没有误差；不精确的时间见 ChartPrintOfGameLikeTimesIsFrozen。
    /// </summary>
    [Fact]
    public void ChartPrintIsFrozen()
    {
        var hits = new List<TileHit>
        {
            new(1, 1.0, 0, 180),
            new(2, 1.0625, 0, 22.5),
            new(3, 1.1875, 0, 157.5),
            new(4, 1.1875, 0, 0),
            new(5, 1.25, 0, 112.5),
            new(6, 1.3125, 0, double.NaN),
            new(7, 1.5625, 0.5, 67.5),
            new(8, 2.5625, 0, 360),
            new(10, 2.5703125, 0, 2.8125),
        };

        // 改了就是破坏性更新
        Assert.Equal("9-4ae3b58a876ae199ecc3d8e9", CustomFingering.ChartPrint(hits));

        // 砖号不参与；时间只用相邻两块的差。这组时间平移 8 秒后差值一点不差，所以指纹不变——
        // 一般的时间平移后差值会有浮点误差，指纹不一定不变（见 ChartPrintOfGameLikeTimesIsFrozen）
        Assert.Equal("9-4ae3b58a876ae199ecc3d8e9", CustomFingering.ChartPrint(hits.ConvertAll(h => h with { Time = h.Time + 8, TileIndex = h.TileIndex + 100 })));
    }

    /// <summary>
    /// 像游戏那样一块块累加出来的时间：t += 转角 / 180 × 60 / BPM（游戏的 entryTime、离线读谱都是这样加的），160 BPM 起，再 240、200 BPM 各一段。
    /// 30° 的奇数倍在 160 BPM 下正好是 x.5 毫秒，累加的误差让它们落在 .5 的两边；转角三分之二按游戏的写法从弧度换回来
    /// （angleLength × 180 / π：7.5° 成了 7.499999999999999），其余是精确的 x.5°；有长按、读不到的转角。
    /// </summary>
    internal static List<TileHit> GameLikeHits()
    {
        double[] pattern = { 30, 30, 90, 30, 150, 22.5, 37.5, 30, 210, 7.5, 30, 270, 52.5, 30, 90, 30, 45, 15, 30, 330, 37.5, 30, 30, 150, 67.5, 30, 97.5, 30, 112.5, 127.5, 142.5, 202.5 };
        var hits = new List<TileHit>();
        var t = 1.05;
        foreach (var bpm in new[] { 160.0, 240, 200 })
        {
            foreach (var degrees in pattern)
            {
                t += degrees / 180 * 60 / bpm;
                var k = hits.Count;
                var angle = k % 3 != 0 ? degrees * Math.PI / 180 * (180 / Math.PI) : degrees;
                hits.Add(new TileHit(k + 1, t, k % 29 == 7 ? 0.25 : 0, k % 37 == 17 ? double.NaN : angle));
            }
        }

        return hits;
    }

    /// <summary>
    /// 不精确的时间下的谱面指纹。复现（2026-11-19 评审的变异测试）：只有 ChartPrintIsFrozen 那种精确样例时，间隔改成「先乘 1000 再相减」测试全过
    /// （时间先按文件的 6 位小数、转角先按 3 位小数取整也一样）；可真实谱 30% 的间隔正好落在 x.5 毫秒，866 张谱里 95 张的指纹会变。
    /// 期望值是用 1.3.1 的 ChartPrint 原样算出来的（和现在的一致）。
    /// </summary>
    [Fact]
    public void ChartPrintOfGameLikeTimesIsFrozen()
    {
        var hits = GameLikeHits();

        // 样例本身：30 个间隔落在 x.5 毫秒上，其中 4 个换个运算顺序就舍入到另一边、2 个先按文件的 6 位小数取整就变；8 个转角先取 3 位小数就变
        var gaps = hits.Zip(hits.Skip(1), (a, b) => (A: a.Time, B: b.Time)).ToList();
        Assert.Equal(30, gaps.Count(g => Math.Abs(((g.B - g.A) * 1000) - Math.Floor((g.B - g.A) * 1000) - 0.5) < 1e-6));
        Assert.Equal(4, gaps.Count(g => Math.Round((g.B - g.A) * 1000) != Math.Round((g.B * 1000) - (g.A * 1000))));
        Assert.Equal(2, gaps.Count(g => Math.Round((g.B - g.A) * 1000) != Math.Round((Math.Round(g.B, 6) - Math.Round(g.A, 6)) * 1000)));
        Assert.Equal(8, hits.Count(h => !double.IsNaN(h.Angle) && Math.Round(h.Angle) != Math.Round(Math.Round(h.Angle, 3))));

        // 改了就是破坏性更新
        Assert.Equal("96-0a191330c0e332b91fcc44b7", CustomFingering.ChartPrint(hits));

        // 砖号不参与
        Assert.Equal("96-0a191330c0e332b91fcc44b7", CustomFingering.ChartPrint(hits.ConvertAll(h => h with { TileIndex = h.TileIndex + 100 })));

        // 指纹只对同一组砖块时间稳定：整体平移后差值的浮点误差变了，落在 x.5 毫秒上的间隔会舍入到另一边
        // （真实谱整体平移 0.4 秒，866 张里 82 张的指纹变了）。认谱不靠这个：指纹对不上时还有节奏对齐
        Assert.Equal("96-cd8f98f498f3c1f15c055244", CustomFingering.ChartPrint(hits.ConvertAll(h => h with { Time = h.Time + 0.4 })));
    }

    /// <summary>
    /// 取砖规则（FloorHits.From）是格式的一部分：hits 收哪些砖、转角怎么累加。改了就是破坏性更新——
    /// 每 4 块多认一块砖、转角记错一块，老文件整份对不上（评审 A 实测 0%）。样例有起点、自动砖、中旋、假砖、
    /// 连续两块长按砖和长按的尾巴、伪双押、已销毁的砖；三种写法（不核对转角、核对后按离开、核对后按到达）结果一样。
    /// </summary>
    [Fact]
    public void FloorHitsRulesAreFrozen()
    {
        var floors = new List<FloorInfo>
        {
            new(0, 0.5, false, false, 180),      // 起点：不按
            new(1, 1.0, false, false, 180),
            new(2, 1.5, false, true, 90),        // 自动砖：不按，转角累加到下一块
            new(3, 1.75, false, false, 90),
            new(4, 2.0, false, true, 0),         // 中旋：不按、不花时间
            new(5, 2.0, false, false, 180),
            new(6, 2.5, false, true, 45),        // 假砖
            new(7, 2.625, true, false, 90),      // 连续两块长按砖合成一条长按
            new(8, 2.875, true, false, 90),
            new(9, 3.125, false, false, 180),    // 长按的尾巴：自动落上，不算一次按键
            new(10, 3.625, false, false, 11.25),
            new(11, 3.65625, false, false, 180), // 伪双押：和上一块隔 31.25 毫秒、转角 11.25°，照样是两块砖（合成一个提示是排键的事）
            new(-1, 0, false, true),             // 已销毁的砖（游戏里 scrFloor 为 null）：读不到转角，下一块的转角是 NaN
            new(13, 4.15625, false, false, 180),
            new(14, 4.65625, false, false, 180),
        };
        var arrival = floors.Select((f, i) => f with { Angle = i == 0 ? double.NaN : floors[i - 1].Angle }).ToList();
        const string expected = "1,1,0,180; 3,1.75,0,270; 5,2,0,90; 7,2.625,0.5,225; 10,3.625,0,360; 11,3.65625,0,11.25; 13,4.15625,0,NaN; 14,4.65625,0,180";

        // 改了就是破坏性更新
        Assert.Equal(expected, Show(FloorHits.From(floors)));
        Assert.Equal(AngleSource.Exit, FloorHits.CheckAngles(floors, 0.5).Source);
        Assert.Equal(expected, Show(FloorHits.From(floors, 0.5)));
        Assert.Equal(AngleSource.Arrival, FloorHits.CheckAngles(arrival, 0.5).Source);
        Assert.Equal(expected, Show(FloorHits.From(arrival, 0.5)));
    }

    /// <summary>
    /// 取砖规则：乱序和同一时间的砖、自动的长按砖。复现（2026-11-19 评审的变异测试）：去掉最后的按时间排序、
    /// 自动长按砖后面那块也收，测试全过。期望值是用 1.3.1 的 FloorHits 原样算出来的（和现在的一致）。
    /// 同一时间的几块按原来的顺序排（样例不到 16 块，List.Sort 走插入排序，是稳定的）。
    /// </summary>
    [Fact]
    public void FloorHitsOrderAndAutoHoldsAreFrozen()
    {
        var floors = new List<FloorInfo>
        {
            new(0, 0.5, false, false, 180),      // 起点：不按
            new(1, 1.0, false, false, 90),
            new(2, 1.25, true, true, 90),        // 自动的长按砖：不按；它后面那块是自动落上的长按尾巴，也不收
            new(3, 1.5, false, false, 180),
            new(4, 2.0, false, false, 180),
            new(5, 2.5, false, false, 90),
            new(6, 2.25, false, false, 90),      // 比上一块早（9 版的暂停可以是负的）：最后按时间排到 5 前面
            new(7, 2.25, false, false, 90),      // 同一时间：按原来的顺序
            new(8, 2.25, true, false, 90),       // 同一时间的长按砖
            new(9, 3.0, false, false, 180),      // 长按尾巴
            new(10, 3.5, false, true, 90),       // 自动砖
            new(11, 3.75, false, false, 180),
            new(12, 3.75, false, false, 180),
            new(13, 4.0, true, true, 90),        // 又一块自动的长按砖
            new(14, 4.25, false, false, 180),    // 它的尾巴
            new(15, 4.5, false, false, 180),
        };
        const string expected = "1,1,0,180; 4,2,0,360; 6,2.25,0,90; 7,2.25,0,90; 8,2.25,0.75,90; 5,2.5,0,180; 11,3.75,0,360; 12,3.75,0,180; 15,4.5,0,450";

        // 改了就是破坏性更新
        Assert.Equal(expected, Show(FloorHits.From(floors)));
        Assert.Equal((AngleSource.Exit, 0.7272727272727273, 0.2727272727272727), Check(floors));
        Assert.Equal(expected, Show(FloorHits.From(floors, 0.5)));
    }

    /// <summary>
    /// 每对相邻砖的「间隔 / 转角算出的间隔」按 ratios 给（一拍 0.5 秒）：转角在 90° 和 180° 之间交替（按到达对就对不上），
    /// 后一半速度 ×2、间隔减半（核对时要按速度换算）。
    /// </summary>
    private static List<FloorInfo> Ratios(params double[] ratios)
    {
        var floors = new List<FloorInfo>();
        var t = 1.0;
        for (var i = 0; i <= ratios.Length; i++)
        {
            var angle = i % 2 == 0 ? 90.0 : 180;
            var speed = i < ratios.Length / 2 ? 1.0 : 2;
            floors.Add(new FloorInfo(i, t, false, false, i < ratios.Length ? angle : double.NaN, speed));
            if (i < ratios.Length)
            {
                t += angle / 180 * 0.5 / speed * ratios[i];
            }
        }

        return floors;
    }

    private static (AngleSource, double, double) Check(List<FloorInfo> floors)
    {
        var check = FloorHits.CheckAngles(floors, 0.5);
        return (check.Source, check.ExitMatch, check.ArrivalMatch);
    }

    /// <summary>
    /// 转角核对（FloorHits.CheckAngles）的门槛和容差。复现（2026-11-19 评审的变异测试）：信转角的门槛 0.5 改 0.9、容差 3% 改 1%，测试全过。
    /// 期望值是用 1.3.1 的 FloorHits 原样算出来的（和现在的一致）。
    /// </summary>
    [Fact]
    public void AngleCheckThresholdsAreFrozen()
    {
        // 正好一半对得上：门槛是「不到一半不信」，一半算信
        var half = Ratios(1, 0.7, 1, 1.4, 1, 1.4, 0.7, 1, 1.4, 1);
        Assert.Equal((AngleSource.Exit, 0.5, 0.1111111111111111), Check(half));
        Assert.Equal("1,1.25,0,90; 2,1.6,0,180; 3,1.85,0,90; 4,2.55,0,180; 5,2.8,0,90; 6,3.15,0,180; 7,3.2375,0,90; 8,3.4875,0,180; "
            + "9,3.6624999999999996,0,90; 10,3.9124999999999996,0,180", Show(FloorHits.From(half, 0.5)));

        // 差 2% 的算对上、差 4% 的不算：对上 11 / 21，刚过一半
        var near = Ratios(1, 1.02, 0.7, 1, 1.4, 1.02, 1, 0.7, 1.04, 1, 1.4, 1, 1.02, 0.7, 1.4, 1, 1.4, 1, 0.7, 1.4, 1);
        Assert.Equal((AngleSource.Exit, 0.5238095238095238, 0.1), Check(near));
        Assert.Equal("1,1.25,0,90; 2,1.76,0,180; 3,1.935,0,90; 4,2.435,0,180; 5,2.785,0,90; 6,3.295,0,180; 7,3.545,0,90; 8,3.895,0,180; "
            + "9,4.155,0,90; 10,4.655,0,180; 11,4.83,0,90; 12,5.08,0,180; 13,5.2075000000000005,0,90; 14,5.3825,0,180; 15,5.5575,0,90; "
            + "16,5.8075,0,180; 17,5.9825,0,90; 18,6.2325,0,180; 19,6.32,0,90; 20,6.67,0,180; 21,6.795,0,90", Show(FloorHits.From(near, 0.5)));

        // 单位弄错（转角是弧度）：比值的中位数约 57，整首不信转角
        var radians = Ratios(1, 1, 1, 1, 1, 1, 1, 1).ConvertAll(f => f with { Angle = f.Angle * Math.PI / 180 });
        Assert.Equal((AngleSource.None, 0.0, 0.0), Check(radians));
        Assert.Equal("1,1.25,0,NaN; 2,1.75,0,NaN; 3,2,0,NaN; 4,2.5,0,NaN; 5,2.625,0,NaN; 6,2.875,0,NaN; 7,3,0,NaN; 8,3.25,0,NaN",
            Show(FloorHits.From(radians, 0.5)));
    }

    /// <summary>
    /// 长按链后面紧跟一块已销毁的砖（游戏里 scrFloor 为 null，GameReader 把它的时间记成 0）。复现（2026-11-19 评审）：
    /// 长按 = 0 − 这块砖的时间，是负数，CustomFingering.From 抛 FormatException，这一关「编辑本关手序」打不开。
    /// 现在长按链遇到读不到的砖就停：长按算到链里最后一块读得到的长按砖（只有一块长按砖时是 0，当普通砖）。
    /// 这是取砖规则里唯一改过的地方：1.3.1 会得出负长按的输入才不一样（Compat131Tests 拿 1.3.1 原样的 FloorHits 逐项比）。
    /// </summary>
    [Fact]
    public void HoldBeforeADestroyedTileIsNeverNegative()
    {
        var floors = new List<FloorInfo>
        {
            new(0, 0.5, false, false, 180),
            new(1, 1.0, true, false, 180),       // 长按砖，后面紧跟已销毁的砖
            new(-1, 0, false, true),
            new(3, 2.0, false, false, 180),
            new(4, 2.5, true, false, 90),        // 两块长按砖连着，后面是已销毁的砖
            new(5, 2.75, true, false, 90),
            new(-1, 0, false, true),
            new(7, 3.5, false, false, 180),
        };

        var hits = FloorHits.From(floors);

        Assert.Equal("1,1,0,180; 3,2,0,NaN; 4,2.5,0.25,180; 7,3.5,0,NaN", Show(hits));
        Assert.Equal(hits.Count, CustomFingering.From(hits, NotePlanner.Plan(hits, new FingeringOptions()), leftHanded: false).Hits.Count);
    }

    internal static string Show(IEnumerable<TileHit> hits) => string.Join("; ", hits.Select(h => string.Join(",",
        h.TileIndex.ToString(CultureInfo.InvariantCulture), h.Time.ToString("R", CultureInfo.InvariantCulture),
        h.HoldSeconds.ToString("R", CultureInfo.InvariantCulture), double.IsNaN(h.Angle) ? "NaN" : h.Angle.ToString("R", CultureInfo.InvariantCulture))));

    /// <summary>1.3.x 的编辑器存出的手序（作者是左撇子；三押、四押、跨手双押、长按、读不到转角的砖）：读得出来、认得出是这一关、整首用上。</summary>
    [Fact]
    public void FrozenEditorFileStillWorks()
    {
        var file = CustomFingering.Parse(File.ReadAllText(Frozen("fingering.json")));

        // 指纹按文件里的 hits 算出来要等于文件里存的那个字面值（改了就是破坏性更新）
        Assert.Equal("20-9deb47fa32ba25f93b1477fc", file.Charts[0]);
        Assert.Equal("20-9deb47fa32ba25f93b1477fc", CustomFingering.ChartPrint(file.Hits));
        Assert.True(file.LeftHanded);
        Assert.Equal("左手作者", file.Author);
        Assert.Equal(20, file.Hits.Count);
        Assert.True(double.IsNaN(file.Hits[14].Angle));
        Assert.Contains(file.Notes, n => n.Lanes.Count == 3);
        Assert.Contains(file.Notes, n => n.Lanes.Count == 4);
        Assert.Contains(file.Notes, n => n.Lanes.Values.Any(h => h > 0));

        // 原关卡就是文件里的这些砖：右手玩家整首镜像着用，左手玩家原样用
        var level = file.Hits.ToList();
        var auto = NotePlanner.Plan(level, new FingeringOptions());
        var right = FingeringMatcher.Choose(new[] { new FingeringCandidate("frozen.json", file) }, level, auto, leftHanded: false);
        var left = FingeringMatcher.Choose(new[] { new FingeringCandidate("frozen.json", file) }, level, auto, leftHanded: true);

        Assert.Equal(FingeringReason.Exact, right.Reason);
        Assert.Equal(1.0, right.Match!.Ratio);
        Assert.Equal(file.Notes.Select(n => Keys(n.Lanes.Keys.Select(l => 3 - l))), right.Match.Notes.Select(n => Keys(n.Lanes.Keys)));
        Assert.Equal(file.Notes.Select(n => Keys(n.Lanes.Keys)), left.Match!.Notes.Select(n => Keys(n.Lanes.Keys)));
    }

    private static string Keys(IEnumerable<int> lanes) => string.Join("+", lanes.OrderBy(l => l));

    /// <summary>1.3.x 的 Mod 写给编辑器的会话：里面的 fingering 段是一份完整的手序，读得出来，指纹等于会话 Id。</summary>
    [Fact]
    public void FrozenSessionStillWorks()
    {
        var session = Load(File.ReadAllText(Frozen("session.json")));
        var fingering = CustomFingering.Parse(session["fingering"]!.ToString(Formatting.None));

        Assert.Equal(EditorSession.Format, (string?)session["format"]);
        Assert.Equal("16-5cea7782a36abca967cf2ecf", (string?)session["id"]);
        Assert.Equal("16-5cea7782a36abca967cf2ecf", CustomFingering.ChartPrint(fingering.Hits));
        Assert.Equal("2026-11-18T00:00:00.0000000+00:00", (string?)session["fingering"]!["updatedAt"]);
        Assert.Equal(fingering.Notes.Count, ((JArray)session["fromAuto"]!).Count);
        var match = FingeringMatcher.Apply(fingering, fingering.Hits, NotePlanner.Plan(fingering.Hits, new FingeringOptions()), leftHanded: false);
        Assert.Equal(1.0, match.Ratio);
    }

    /// <summary>1.3.x 写的编辑器历史列表（recent/index.js 的内容）照样读得出来。</summary>
    [Fact]
    public void FrozenRecentListStillReads()
    {
        var editor = Path.Combine(Path.GetTempPath(), "rhythmhint-frozen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(editor, SessionLibrary.FolderName));
        try
        {
            File.WriteAllText(Path.Combine(editor, SessionLibrary.FolderName, "index.js"),
                "window.RHYTHMHINT_RECENT = " + File.ReadAllText(Frozen("recent.json")).Trim() + ";\n");

            var items = SessionLibrary.Read(editor);

            Assert.Equal(new[] { "8-0123456789abcdef01234567", "16-5cea7782a36abca967cf2ecf" }, items.Select(i => i.Id));
            Assert.Equal("16-5cea7782a36abca967cf2ecf-20261118-090000-000.ogg", items[1].Audio);
            Assert.Equal(13, items[1].Notes);
        }
        finally
        {
            Directory.Delete(editor, recursive: true);
        }
    }

    private static JObject Load(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
        return JObject.Load(reader);
    }

    public static TheoryData<string> Accepted => Samples("accept");

    public static TheoryData<string> Rejected => Samples("reject");

    private static TheoryData<string> Samples(string folder) =>
        new(Directory.GetFiles(Path.Combine(Conformance, folder), "*.json").Select(p => Path.GetFileName(p)).OrderBy(n => n, StringComparer.Ordinal));

    /// <summary>两边都要接受的写法，读出来的内容和 expected.json 一致。</summary>
    [Theory]
    [MemberData(nameof(Accepted))]
    public void ConformanceSampleIsAccepted(string name)
    {
        var file = CustomFingering.Parse(File.ReadAllText(Path.Combine(Conformance, "accept", name)));
        var expected = (JObject?)Load(File.ReadAllText(Path.Combine(Conformance, "expected.json")))[name];
        if (expected == null)
        {
            return;
        }

        foreach (var p in expected.Properties())
        {
            var first = file.Hits[0];
            JToken actual = p.Name switch
            {
                "keys" => file.Keys,
                "leftHanded" => file.LeftHanded,
                "hits" => file.Hits.Count,
                "notes" => file.Notes.Count,
                "song" => file.Song,
                "artist" => file.Artist,
                "charter" => file.Charter,
                "author" => file.Author,
                "comment" => file.Comment,
                "authorLength" => file.Author.Length,
                "commentLength" => file.Comment.Length,
                "firstHit" => new JArray(first.TileIndex, first.Time, first.HoldSeconds, double.IsNaN(first.Angle) ? JValue.CreateNull() : first.Angle),
                "noteLanes" => new JArray(file.Notes.Select(n => new JArray(n.Lanes.Keys))),
                _ => throw new InvalidOperationException("expected.json 里有不认识的项 " + p.Name),
            };
            Assert.True(Canon(p.Value) == Canon(actual), $"{name} 的 {p.Name}：期望 {Canon(p.Value)}，实际 {Canon(actual)}");
        }
    }

    /// <summary>两边的数字写法不同（1 和 1.0）：数字一律按 double 比。</summary>
    private static string Canon(JToken t) => t switch
    {
        JArray a => "[" + string.Join(",", a.Select(Canon)) + "]",
        JValue { Type: JTokenType.Integer or JTokenType.Float } v => Convert.ToDouble(v.Value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
        JValue { Type: JTokenType.Null } => "null",
        JValue { Type: JTokenType.Boolean } v => (bool)v ? "true" : "false",
        _ => t.ToString(Formatting.None),
    };

    /// <summary>两边都要拒绝的写法（整份不读，读的一方跳过这一份并说明原因）。</summary>
    [Theory]
    [MemberData(nameof(Rejected))]
    public void ConformanceSampleIsRejected(string name)
    {
        Assert.Throws<FormatException>(() => CustomFingering.Parse(File.ReadAllText(Path.Combine(Conformance, "reject", name))));
    }
}
