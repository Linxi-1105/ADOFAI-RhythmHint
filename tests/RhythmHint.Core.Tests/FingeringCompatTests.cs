using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 手序文件的向前兼容（2026-11-19 评审 A、C）：不认识的字段读了留着、写回去；整数的浮点写法当整数；
/// 同一张谱的重复文件不再永远「不确定」；文件名在 Windows 上一定能用。
/// </summary>
public class FingeringCompatTests
{
    private const string Head = "\"format\":\"rhythmhint-fingering\",\"schemaVersion\":1";
    private const string Body = "\"hits\":[[1,1,0,180],[2,1.5,0,90]],\"notes\":[[1,1,[[2,0]]],[2,2,[[1,0]]]]";

    private static JObject Load(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
        return JObject.Load(reader);
    }

    /// <summary>
    /// 以后的版本加的顶层字段：读的时候原样留下，写回去时跟在已知字段后面、hits 前面，内容和顺序不变。
    /// 复现（评审 A 高1）：原来读完就丢，Mod 重新导出、编辑器再保存一次，新版加的东西就没了。
    /// </summary>
    [Fact]
    public void UnknownTopLevelFieldsAreWrittenBack()
    {
        var json = "{" + Head + ",\"x-future\":{\"a\":[1,2,{\"b\":null}],\"when\":\"2026-11-18T00:00:00Z\"},\"keys\":4,\"license\":\"CC-BY-4.0\"," + Body + ",\"x-after\":[\"z\"]}";

        var text = CustomFingering.Parse(json).ToJson();
        var back = Load(text);

        Assert.Equal(new[] { "format", "schemaVersion", "keys", "song", "artist", "charter", "author", "comment", "leftHanded", "charts", "updatedAt",
            "x-future", "license", "x-after", "hits", "notes" }, back.Properties().Select(p => p.Name));
        Assert.True(JToken.DeepEquals(Load(json)["x-future"], back["x-future"]));
        Assert.Equal("CC-BY-4.0", (string?)back["license"]);
        Assert.Equal(text, CustomFingering.Parse(text).ToJson()); // 再读再写一样
    }

    /// <summary>「编辑本关手序」交给编辑器的会话里，手序也带着这些字段（放在 fingering 的顶层，编辑器原样读、原样写回）。</summary>
    [Fact]
    public void SessionCarriesUnknownTopLevelFields()
    {
        var session = EditorContractTests.Sample();
        session.Fingering = CustomFingering.Parse("{" + Head + ",\"x-future\":{\"a\":1}," + Body + "}");

        var script = session.ToScript();
        var fingering = (JObject)Load(script.Substring("window.RHYTHMHINT_SESSION = ".Length).TrimEnd('\n', ';'))["fingering"]!;

        Assert.Equal(1, (int?)fingering["x-future"]?["a"]);
        var names = fingering.Properties().Select(p => p.Name).ToList();
        Assert.True(names.IndexOf("x-future") > names.IndexOf("updatedAt") && names.IndexOf("x-future") < names.IndexOf("hits"));
    }

    /// <summary>
    /// 「编辑本关手序」为本关重新生成一份手序时，从正在用的文件带上作者、说明、认得的指纹和不认识的字段
    /// （复现：EditorLauncher 原来只拷 Author、Comment、Charts，编辑器再保存时新版加的字段就没了）。
    /// </summary>
    [Fact]
    public void EditingThisLevelKeepsTheUsedFilesExtras()
    {
        var used = CustomFingering.Parse("{" + Head + ",\"author\":\"甲\",\"comment\":\"说明\",\"charts\":[\"2-old\"],\"x-future\":{\"a\":[1]}," + Body + "}");
        var level = used.Hits.ToList();
        var fresh = CustomFingering.From(level, NotePlanner.Plan(level, new FingeringOptions()), false);

        fresh.TakeDetailsFrom(used);
        used.Extra["x-future"]!["a"]![0] = 9; // 拷过来的是一份副本

        Assert.Equal("甲", fresh.Author);
        Assert.Equal("说明", fresh.Comment);
        Assert.Contains("2-old", fresh.Charts);
        Assert.Equal(1, (int?)fresh.Extra["x-future"]?["a"]?[0]);
        Assert.Contains("\"x-future\":{\"a\":[1]}", fresh.ToJson());
    }

    /// <summary>不认识的字段太大（超过 25 万个元素）：不留它，手序照样读（以后的版本加的大字段被丢掉也不出错），记下丢了哪个。</summary>
    [Fact]
    public void HugeUnknownFieldIsDroppedNotRejected()
    {
        var pad = string.Join(",", Enumerable.Repeat("[]", 250_001));
        var file = CustomFingering.Parse("{" + Head + ",\"pad\":[" + pad + "],\"small\":1," + Body + "}");

        Assert.Equal(2, file.Hits.Count);
        Assert.Equal(new[] { "pad" }, file.DroppedFields);
        Assert.Null(file.Extra["pad"]);
        Assert.Equal(1, (int?)file.Extra["small"]);
    }

    /// <summary>hits 超过 20 万行照旧整份拒绝（1.3.0 起的上限），数行数时还没建对象。</summary>
    [Fact]
    public void TooManyHitsAreStillRejected()
    {
        var rows = string.Join(",", Enumerable.Range(1, 200_001).Select(i => "[" + i + "," + i + ",0,0]"));

        var ex = Assert.Throws<FormatException>(() => CustomFingering.Parse("{" + Head + ",\"hits\":[" + rows + "],\"notes\":[[1,1,[[2,0]]]]}"));
        Assert.Contains("200000", ex.Message);
    }

    /// <summary>
    /// 键数不同的手序（以后的六键文件放进四键的 Mod）不套用，但像这一关的要说出来（评审 A 中8：原来悄悄过滤掉）。
    /// 别的歌的六键手序不提（文件夹里放了很多首也不刷屏）。
    /// </summary>
    [Fact]
    public void OtherKeyCountsAreReportedWhenTheyLookLikeThisLevel()
    {
        var level = Level(300, 5);
        var sixKeys = CustomFingering.Parse(CustomFingering.From(level, NotePlanner.Plan(level, new FingeringOptions()), false).ToJson().Replace("\"keys\":4", "\"keys\":6"));
        var shifted = level.ConvertAll(h => h with { Time = h.Time + 0.2 });
        shifted[100] = shifted[100] with { Angle = shifted[100].Angle + 30 };
        var updatedSix = CustomFingering.Parse(CustomFingering.From(shifted, NotePlanner.Plan(shifted, new FingeringOptions()), false).ToJson().Replace("\"keys\":4", "\"keys\":6"));
        var other = Level(300, 6);
        var otherSix = CustomFingering.Parse(CustomFingering.From(other, NotePlanner.Plan(other, new FingeringOptions()), false).ToJson().Replace("\"keys\":4", "\"keys\":6"));

        var choice = Pick(level, ("six.json", sixKeys), ("six-old.json", updatedSix), ("other-six.json", otherSix));

        Assert.Equal(FingeringReason.None, choice.Reason);
        Assert.Equal(new[] { "six.json", "six-old.json" }, choice.OtherKeys.Select(c => c.Name));
    }

    /// <summary>
    /// 预筛只跳过注定对不上的：随便造的谱面（同一首歌改过、截过、加删过砖，或者别的歌），上限从不低于真正对上的砖数；
    /// BPM 不同的歌上限很低（直接跳过，不做对齐）。
    /// </summary>
    [Fact]
    public void MatchBoundNeverMissesARealMatch()
    {
        var random = new Random(11);
        var realMatches = 0;
        for (var round = 0; round < 60; round++)
        {
            var file = Level(200 + random.Next(400), round);
            var level = (round % 3) switch
            {
                // 间隔整体拉长不到 3%（还算同一块砖，桶号可能差一格）
                0 => file.Skip(random.Next(50)).Take(150 + random.Next(300)).Select(h => h with { Time = h.Time * (1 + (round * 0.0005)) }).ToList(),
                1 => file.Select((h, i) => i % 37 == 5 ? h with { Angle = h.Angle + 20, HoldSeconds = 0.5 } : h).Where((_, i) => i % 53 != 7).ToList(),
                _ => Level(300, 1000 + round),
            };
            var auto = NotePlanner.Plan(level, new FingeringOptions());
            var fingering = CustomFingering.From(file, NotePlanner.Plan(file, new FingeringOptions()), false);

            var matched = FingeringMatcher.Apply(fingering, level, auto, false).MatchedHits;
            var aligned = FingeringMatcher.Align(level, file).Count(i => i >= 0);

            Assert.True(FingeringMatcher.MatchBound(level, file) >= matched, $"第 {round} 轮：上限 {FingeringMatcher.MatchBound(level, file)}，实际对上 {matched}");
            Assert.True(aligned >= matched);
            realMatches += matched >= 0.3 * level.Count ? 1 : 0;
        }

        Assert.True(realMatches >= 30, $"只有 {realMatches} 轮真的对上了，样例太弱");
        var slow = Level(400, 7);
        var fast = slow.ConvertAll(h => h with { Time = h.Time * 0.8 });
        Assert.True(FingeringMatcher.MatchBound(fast, slow) < 0.3 * fast.Count);
    }

    /// <summary>值是整数的浮点写法（1.0、1e0）当整数（评审 A 中7：编辑器收、Mod 拒）。</summary>
    [Theory]
    [InlineData("{\"format\":\"rhythmhint-fingering\",\"schemaVersion\":1.0," + Body + "}")]
    [InlineData("{" + Head + ",\"keys\":4.0," + Body + "}")]
    [InlineData("{" + Head + ",\"hits\":[[1.0,1,0,180],[2e0,1.5,0,90]],\"notes\":[[1,1,[[2,0]]],[2,2,[[1,0]]]]}")]
    [InlineData("{" + Head + ",\"hits\":[[1,1,0,180],[2,1.5,0,90]],\"notes\":[[1.0,1e0,[[2.0,0]]],[2,2,[[1e0,0]]]]}")]
    public void IntegersWrittenAsFloatsAreIntegers(string json)
    {
        var file = CustomFingering.Parse(json);

        Assert.Equal(4, file.Keys);
        Assert.Equal(new[] { 1, 2 }, file.Hits.Select(h => h.TileIndex));
        Assert.Equal(new[] { "2", "1" }, file.Notes.Select(n => string.Join("+", n.Lanes.Keys)));
    }

    /// <summary>不是整数的照旧拒绝。</summary>
    [Theory]
    [InlineData("{\"format\":\"rhythmhint-fingering\",\"schemaVersion\":1.5," + Body + "}")]
    [InlineData("{" + Head + ",\"keys\":4.5," + Body + "}")]
    [InlineData("{" + Head + ",\"hits\":[[1.5,1,0,180],[2,1.5,0,90]],\"notes\":[[1,1,[[2,0]]],[2,2,[[1,0]]]]}")]
    [InlineData("{" + Head + ",\"hits\":[[1,1,0,180],[2,1.5,0,90]],\"notes\":[[1,1,[[2.5,0]]],[2,2,[[1,0]]]]}")]
    [InlineData("{" + Head + ",\"hits\":[[1,1,0,180],[2,1.5,0,90]],\"notes\":[[1,1,[[2,0]]],[2,2,[[1e-1,0]]]]}")]
    [InlineData("{" + Head + ",\"hits\":[[3e9,1,0,180]],\"notes\":[[3e9,3e9,[[2,0]]]]}")]
    public void NonIntegersAreStillRejected(string json)
    {
        Assert.Throws<FormatException>(() => CustomFingering.Parse(json));
    }

    /// <summary>同名字段取最后一个（和 1.3.x 一样）：前面那个写坏了也不影响。</summary>
    [Fact]
    public void RepeatedNamesTakeTheLastOne()
    {
        var file = CustomFingering.Parse("{" + Head + ",\"song\":\"A\",\"x\":1,\"hits\":[[\"bad\"]],\"charts\":[\"2-a\"]," + Body
            + ",\"song\":\"B\",\"x\":2,\"y\":{\"a\":1,\"a\":2},\"charts\":\"not a list\"}");

        Assert.Equal("B", file.Song);
        Assert.Equal(2, file.Hits.Count);
        Assert.Equal(2, (int?)Load(file.ToJson())["x"]);
        Assert.Equal(2, (int?)file.Extra["y"]?["a"]);
        Assert.DoesNotContain("2-a", file.Charts);
    }

    private static List<TileHit> Level(int count, int seed, double start = 1.0)
    {
        var random = new Random(seed);
        var choices = new[] { 1.0, 0.5, 0.25, 0.75, 1.5, 0.5, 0.25 };
        var hits = new List<TileHit>();
        var t = start;
        for (var i = 0; i < count; i++)
        {
            var b = choices[random.Next(choices.Length)];
            t += b * 0.5;
            hits.Add(new TileHit(i + 1, t, 0, b * 180));
        }

        return hits;
    }

    private static FingeringChoice Pick(List<TileHit> level, params (string Name, CustomFingering File)[] files) =>
        FingeringMatcher.Choose(files.Select(f => new FingeringCandidate(f.Name, f.File)).ToList(), level, NotePlanner.Plan(level, new FingeringOptions()), false);

    private static CustomFingering Copy(CustomFingering file) => CustomFingering.Parse(file.ToJson());

    /// <summary>
    /// 浏览器重复下载出来的「(1)」：内容一样的几份当一份，不再永远「不确定」（评审 A 低中11）。名字短的那份（没有「(1)」的）算用上的。
    /// </summary>
    [Fact]
    public void IdenticalCopiesAreOneFingering()
    {
        var level = Level(300, 1);
        var file = CustomFingering.From(level, NotePlanner.Plan(level, new FingeringOptions()), false);

        var choice = Pick(level, ("Artist - Song (1).json", Copy(file)), ("Artist - Song.json", Copy(file)));

        Assert.Equal(FingeringReason.Exact, choice.Reason);
        Assert.Equal("Artist - Song.json", choice.Used?.Name);
    }

    /// <summary>旧版谱面的两份一样的文件：照样跟着歌走（原来两份一样像，判不确定）。</summary>
    [Fact]
    public void IdenticalCopiesOfAnOlderChartAreFollowed()
    {
        var old = Level(300, 2);
        var file = CustomFingering.From(old, NotePlanner.Plan(old, new FingeringOptions()), false);
        var level = old.ConvertAll(h => h with { Time = h.Time + 0.3 });
        level[150] = level[150] with { Angle = level[150].Angle + 30 };

        var choice = Pick(level, ("a.json", Copy(file)), ("a (1).json", Copy(file)));

        Assert.Equal(FingeringReason.Followed, choice.Reason);
        Assert.Equal("a.json", choice.Used?.Name);
    }

    /// <summary>同一位作者给同一张谱存过两次（改了名字另存、或者老文件没删）：用 updatedAt 新的那份。</summary>
    [Fact]
    public void SameAuthorsNewerSaveWins()
    {
        var level = Level(300, 3);
        var auto = NotePlanner.Plan(level, new FingeringOptions());
        var older = CustomFingering.From(level, auto, false);
        older.Author = "某位玩家";
        older.UpdatedAt = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
        var newer = CustomFingering.From(level, auto.Select(n => n.Clone()).ToList(), false);
        newer.Notes[0].Lanes.Clear();
        newer.Notes[0].Lanes[0] = 0;
        newer.Author = " 某位玩家 ";
        newer.UpdatedAt = older.UpdatedAt.AddHours(5);

        var choice = Pick(level, ("old.json", Copy(older)), ("new.json", Copy(newer)));

        Assert.Equal(FingeringReason.Exact, choice.Reason);
        Assert.Equal("new.json", choice.Used?.Name);
    }

    /// <summary>不同作者（或者没写作者）的两份照旧让玩家选：分不出谁的更好。</summary>
    [Theory]
    [InlineData("甲", "乙")]
    [InlineData("", "")]
    public void DifferentAuthorsStillAsk(string authorA, string authorB)
    {
        var level = Level(300, 4);
        var auto = NotePlanner.Plan(level, new FingeringOptions());
        var a = CustomFingering.From(level, auto, false);
        a.Author = authorA;
        a.UpdatedAt = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
        var b = CustomFingering.From(level, auto.Select(n => n.Clone()).ToList(), false);
        b.Notes[0].Lanes.Clear();
        b.Notes[0].Lanes[0] = 0;
        b.Author = authorB;
        b.UpdatedAt = a.UpdatedAt.AddHours(5);

        var choice = Pick(level, ("a.json", Copy(a)), ("b.json", Copy(b)));

        Assert.Equal(FingeringReason.Unsure, choice.Reason);
        Assert.Equal(2, choice.Candidates.Count);
    }

    /// <summary>
    /// 建议的文件名在 Windows 上一定能用（评审 A 低12）：去掉控制字符、避开保留名（CON、PRN、AUX、NUL、COM1–9、LPT1–9，
    /// 后面带扩展名也算）、截到 80 个字时不把 emoji 这类代理对切成两半。编辑器 model.js 用同样的规则。
    /// </summary>
    [Theory]
    [InlineData("CON", "", "_CON.json")]
    [InlineData("nul", "", "_nul.json")]
    [InlineData("Com1", "", "_Com1.json")]
    [InlineData("lpt9.remix", "", "_lpt9.remix.json")]
    [InlineData("AUX  .x", "", "_AUX  .x.json")]
    [InlineData("CONSOLE", "", "CONSOLE.json")]
    [InlineData("COM0", "", "COM0.json")]
    [InlineData("Song", "CON", "CON - Song.json")]
    [InlineData("a\u0001b\u007f\u0085c\td", "", "abcd.json")]
    [InlineData("<b>So:ng</b>", "Art/ist", "Art_ist - So_ng.json")]
    [InlineData("\u0001\u0002", "", "fingering.json")]
    [InlineData("...", "", "fingering.json")]
    public void SuggestedFileNamesAreSafe(string song, string artist, string expected)
    {
        Assert.Equal(expected, new CustomFingering { Song = song, Artist = artist }.SuggestedFileName());
    }

    [Fact]
    public void LongFileNamesDoNotSplitAnEmoji()
    {
        var name = new CustomFingering { Song = new string('x', 79) + "\U0001F600tail" }.SuggestedFileName();

        Assert.Equal(new string('x', 79) + ".json", name);
        Assert.Equal(new string('x', 78) + "\U0001F600.json", new CustomFingering { Song = new string('x', 78) + "\U0001F600tail" }.SuggestedFileName());
    }
}
