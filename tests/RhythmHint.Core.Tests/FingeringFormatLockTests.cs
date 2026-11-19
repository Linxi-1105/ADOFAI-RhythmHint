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
    /// 读不到的转角（NaN）、长按、同时按的 0 间隔。
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

        // 绝对时间、砖号不参与
        Assert.Equal("9-4ae3b58a876ae199ecc3d8e9", CustomFingering.ChartPrint(hits.ConvertAll(h => h with { Time = h.Time + 8, TileIndex = h.TileIndex + 100 })));
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

    private static string Show(IEnumerable<TileHit> hits) => string.Join("; ", hits.Select(h => string.Join(",",
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
