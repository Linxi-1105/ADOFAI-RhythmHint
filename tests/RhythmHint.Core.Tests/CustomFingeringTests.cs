using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 专属手序：文件读写和校验，以及「手序跟着歌走」——关卡更新后（改偏移、改个别砖的角度、加删砖）
/// 对得上的砖继续用专属手序，对不上的退回自动排键；每块砖始终正好一个提示。
/// </summary>
public class CustomFingeringTests
{
    private const double Spb = 0.5; // 120 BPM

    /// <summary>按「几拍」的间隔造一串砖，转角 = 间隔 × 180°。</summary>
    private static List<TileHit> Chart(IEnumerable<double> beats, double start = 1.0, int firstTile = 1)
    {
        var hits = new List<TileHit>();
        var t = start;
        var tile = firstTile;
        foreach (var b in beats)
        {
            t += b * Spb;
            hits.Add(new TileHit(tile++, t, 0, b * 180));
        }

        return hits;
    }

    /// <summary>不规则的节奏（避免整首一样的间隔让对齐有多种答案）。</summary>
    private static List<double> Rhythm(int count, int seed = 7)
    {
        var random = new Random(seed);
        var choices = new[] { 1.0, 0.5, 0.25, 0.75, 1.5, 0.5, 0.25 };
        return Enumerable.Range(0, count).Select(_ => choices[random.Next(choices.Length)]).ToList();
    }

    private static readonly FingeringOptions Options = new();

    /// <summary>玩家改过的手序：在自动排键的基础上每隔几个音符换一条轨道（换成和自动不同的键）。</summary>
    private static List<PlannedNote> Edited(IReadOnlyList<PlannedNote> auto)
    {
        var result = new List<PlannedNote>();
        for (var i = 0; i < auto.Count; i++)
        {
            var copy = new PlannedNote(auto[i].TileIndex, auto[i].Time) { LastTileIndex = auto[i].LastTileIndex };
            foreach (var kv in auto[i].Lanes)
            {
                copy.Lanes[i % 3 == 0 && auto[i].Lanes.Count == 1 ? (kv.Key + 1) % 4 : kv.Key] = kv.Value;
            }

            result.Add(copy);
        }

        return result;
    }

    private static CustomFingering Made(List<TileHit> hits, out List<PlannedNote> edited)
    {
        edited = Edited(NotePlanner.Plan(hits, Options));
        return CustomFingering.From(hits, edited, leftHanded: false);
    }

    /// <summary>每块砖正好属于一个音符，音符按时间排好，时间取本关的砖。</summary>
    private static void AssertCoversEveryHitOnce(IReadOnlyList<TileHit> hits, IReadOnlyList<PlannedNote> notes)
    {
        var next = 0;
        foreach (var n in notes)
        {
            Assert.Equal(hits[next].TileIndex, n.TileIndex);
            Assert.Equal(hits[next].Time, n.Time, 9);
            while (next < hits.Count && hits[next].TileIndex <= n.LastTileIndex)
            {
                next++;
            }

            Assert.Equal(n.LastTileIndex, hits[next - 1].TileIndex);
            Assert.InRange(n.Lanes.Count, 1, 4);
        }

        Assert.Equal(hits.Count, next);
    }

    private static string Lanes(PlannedNote n) => string.Join("+", n.Lanes.Keys);

    [Fact]
    public void FileRoundTripsThroughJson()
    {
        var hits = Chart(Rhythm(40));
        hits[5] = hits[5] with { HoldSeconds = 0.75 };
        hits[9] = hits[9] with { Angle = double.NaN };
        var file = Made(hits, out var edited);
        file.Song = "<color=#ff0>Song</color>";
        file.Artist = "Artist";
        file.Author = "某位玩家";
        file.Comment = "说明";

        var back = CustomFingering.Parse(file.ToJson());

        Assert.Equal("Song", CustomFingering.PlainText(back.Song));
        Assert.Equal("某位玩家", back.Author);
        Assert.Equal(4, back.Keys);
        Assert.Equal(hits.Count, back.Hits.Count);
        Assert.True(double.IsNaN(back.Hits[9].Angle));
        Assert.Equal(0.75, back.Hits[5].HoldSeconds, 6);
        Assert.Equal(edited.Select(Lanes), back.Notes.Select(Lanes));
        Assert.Equal("Artist - Song.json", back.SuggestedFileName());
    }

    [Theory]
    [InlineData("{}", "format")]
    [InlineData("""{"format":"rhythmhint-fingering","schemaVersion":2,"hits":[[1,1,0,180]],"notes":[[1,1,[[2,0]]]]}""", "新")]
    [InlineData("""{"format":"rhythmhint-fingering","schemaVersion":1,"hits":[[1,1,0,180]],"notes":[[1,1,[[4,0]]]]}""", "轨道")]
    [InlineData("""{"format":"rhythmhint-fingering","schemaVersion":1,"hits":[[1,1,0,180]],"notes":[[1,1,[[2,0],[2,0]]]]}""", "重复")]
    [InlineData("""{"format":"rhythmhint-fingering","schemaVersion":1,"hits":[[1,1,0,180],[2,1.5,0,180]],"notes":[[1,1,[[2,0]]]]}""", "覆盖")]
    [InlineData("""{"format":"rhythmhint-fingering","schemaVersion":1,"hits":[[2,1,0,180],[1,1.5,0,180]],"notes":[[2,2,[[2,0]]],[1,1,[[1,0]]]]}""", "排好")]
    [InlineData("not json", "JSON")]
    public void RejectsBrokenFiles(string json, string because)
    {
        var ex = Assert.Throws<FormatException>(() => CustomFingering.Parse(json));
        Assert.Contains(because, ex.Message);
    }

    /// <summary>
    /// 类型不对、数字超出 long 的文件也只算格式不对（FormatException，读的地方跳过这一份）。
    /// 复现（2026-11-19 评审）：Newtonsoft 强转时抛 ArgumentException / OverflowException，漏过 FingeringSource 的 catch，整个 Mod 停用，每次开游戏都重来。
    /// </summary>
    [Theory]
    [InlineData("""{"format":[],"schemaVersion":1,"hits":[[1,1,0,180]],"notes":[[1,1,[[2,0]]]]}""")]
    [InlineData("""{"format":"rhythmhint-fingering","schemaVersion":1,"updatedAt":{},"hits":[[1,1,0,180]],"notes":[[1,1,[[2,0]]]]}""")]
    [InlineData("""{"format":"rhythmhint-fingering","schemaVersion":99999999999999999999,"hits":[[1,1,0,180]],"notes":[[1,1,[[2,0]]]]}""")]
    [InlineData("""{"format":"rhythmhint-fingering","schemaVersion":1,"hits":[[99999999999999999999,1,0,180]],"notes":[[1,1,[[2,0]]]]}""")]
    [InlineData("""{"format":"rhythmhint-fingering","schemaVersion":1,"keys":[],"hits":[[1,1,0,180]],"notes":[[1,1,[[2,0]]]]}""")]
    public void WrongTypesAreFormatErrors(string json)
    {
        Assert.Throws<FormatException>(() => CustomFingering.Parse(json));
    }

    /// <summary>作者、说明恰好写成日期时间也照文字读（Newtonsoft 默认会把它认成日期，原来整份拒绝；编辑器照常能存出这样的文件）。</summary>
    [Fact]
    public void TextThatLooksLikeADateStaysText()
    {
        var json = """{"format":"rhythmhint-fingering","schemaVersion":1,"author":"2026-11-18T00:00:00Z","comment":"2020-01-01T12:00:00","updatedAt":"2026-11-18T08:30:00Z","hits":[[1,1,0,180]],"notes":[[1,1,[[2,0]]]]}""";

        var file = CustomFingering.Parse(json);

        Assert.Equal("2026-11-18T00:00:00Z", file.Author);
        Assert.Equal("2020-01-01T12:00:00", file.Comment);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 8, 30, 0, TimeSpan.Zero), file.UpdatedAt);
    }

    [Fact]
    public void SameLevelUsesTheWholeFingering()
    {
        var hits = Chart(Rhythm(300));
        var file = Made(hits, out var edited);

        var match = FingeringMatcher.Apply(file, hits, NotePlanner.Plan(hits, Options), leftHanded: false);

        Assert.Equal(1.0, match.Ratio);
        Assert.Equal(0, match.FallbackNotes);
        Assert.Equal(edited.Select(Lanes), match.Notes.Select(Lanes));
    }

    [Fact]
    public void ChangedOffsetStillFollows()
    {
        var hits = Chart(Rhythm(300));
        var file = Made(hits, out var edited);
        var shifted = hits.ConvertAll(h => h with { Time = h.Time + 0.317 });

        var match = FingeringMatcher.Apply(file, shifted, NotePlanner.Plan(shifted, Options), leftHanded: false);

        Assert.Equal(1.0, match.Ratio);
        Assert.Equal(edited.Select(Lanes), match.Notes.Select(Lanes));
        AssertCoversEveryHitOnce(shifted, match.Notes);
    }

    [Fact]
    public void OneRetimedTileOnlyFallsBackThere()
    {
        var beats = Rhythm(300);
        var hits = Chart(beats);
        var file = Made(hits, out var edited);

        // 谱师改了第 150 块砖的角度：它和后面所有砖的时间都变了，但后面的节奏没变
        beats[150] += 0.5;
        var updated = Chart(beats);
        var auto = NotePlanner.Plan(updated, Options);
        var match = FingeringMatcher.Apply(file, updated, auto, leftHanded: false);

        AssertCoversEveryHitOnce(updated, match.Notes);
        Assert.InRange(match.FallbackNotes, 1, 3);
        Assert.Equal(1, match.FallbackSections);
        Assert.Equal(edited.Take(140).Select(Lanes), match.Notes.Take(140).Select(Lanes));
        Assert.Equal(edited.Skip(160).Select(Lanes), match.Notes.Skip(160).Select(Lanes));
    }

    [Fact]
    public void InsertedAndRemovedTilesFallBackOnlyThere()
    {
        var beats = Rhythm(400);
        var hits = Chart(beats);
        var file = Made(hits, out _);

        var updatedBeats = beats.Take(100).Concat(new[] { 0.25, 0.25, 0.25, 0.25, 0.25 }).Concat(beats.Skip(100).Take(150))
            .Concat(beats.Skip(260)).ToList(); // 中间加了 5 块、后面删了 10 块
        var updated = Chart(updatedBeats);
        var match = FingeringMatcher.Apply(file, updated, NotePlanner.Plan(updated, Options), leftHanded: false);

        AssertCoversEveryHitOnce(updated, match.Notes);
        Assert.True(match.Ratio > 0.95, $"对上 {match.Ratio:P1}");
        Assert.InRange(match.FallbackSections, 1, 3);
    }

    [Fact]
    public void LeftHandedPlayerGetsTheMirror()
    {
        var hits = Chart(Rhythm(60));
        var file = Made(hits, out var edited);

        var match = FingeringMatcher.Apply(file, hits, NotePlanner.Plan(hits, Options), leftHanded: true);

        Assert.Equal(edited.Select(n => string.Join("+", n.Lanes.Keys.Select(l => 3 - l).OrderBy(l => l))), match.Notes.Select(Lanes));
    }

    [Fact]
    public void CrossHandChordsArePlayersChoice()
    {
        var beats = Rhythm(30);
        beats.Insert(15, 0.0);
        var hits = Chart(beats);
        var auto = NotePlanner.Plan(hits, Options);
        var custom = auto.Select(n => n.Clone()).ToList();
        var chord = custom.Single(n => n.IsChord);
        chord.Lanes.Clear();
        chord.Lanes[1] = 0;
        chord.Lanes[2] = 0; // F+J：自动排键不会这样排，玩家可以
        var file = CustomFingering.From(hits, custom, leftHanded: false);

        var match = FingeringMatcher.Apply(file, hits, auto, leftHanded: false);

        Assert.Contains(match.Notes, n => Lanes(n) == "1+2");
    }

    [Fact]
    public void DifferentGroupingNeverSplitsAHitTwice()
    {
        var random = new Random(3);
        for (var round = 0; round < 40; round++)
        {
            var beats = Enumerable.Range(0, 120).Select(_ => random.Next(6) switch
            {
                0 => 0.0, // 同时按
                1 => 0.04,
                2 => 0.25,
                3 => 0.5,
                _ => 1.0,
            }).ToList();
            var hits = Chart(beats);

            // 做手序的人和玩家的多押转角设置不同，分组就不同
            var file = CustomFingering.From(hits, Edited(NotePlanner.Plan(hits, new FingeringOptions { ChordAngle = 5 })), false);
            var auto = NotePlanner.Plan(hits, new FingeringOptions { ChordAngle = 30 });
            beats[random.Next(beats.Count)] += 0.5;
            var updated = Chart(beats);

            var match = FingeringMatcher.Apply(file, updated, NotePlanner.Plan(updated, new FingeringOptions { ChordAngle = 30 }), false);

            AssertCoversEveryHitOnce(updated, match.Notes);
            Assert.Equal(match.Notes.Count, match.FromAuto.Count);
            _ = auto;
        }
    }

    private static FingeringChoice Pick(List<TileHit> level, string? chosen, params (string Name, CustomFingering File)[] files) =>
        FingeringMatcher.Choose(files.Select(f => new FingeringCandidate(f.Name, f.File)).ToList(), level, NotePlanner.Plan(level, Options), false, chosen);

    /// <summary>谱师改偏移、中间一块砖改角度（后面的砖整体后移）。</summary>
    private static List<TileHit> Updated(List<double> beats)
    {
        var changed = beats.ToList();
        changed[changed.Count / 2] += 0.5;
        return Chart(changed, start: 1.3);
    }

    [Fact]
    public void SameChartIsRecognisedByItsTilesNotItsName()
    {
        var hits = Chart(Rhythm(300));
        var file = Made(hits, out _);
        file.Song = "完全不同的名字";

        var choice = Pick(hits.ConvertAll(h => h with { Time = h.Time + 0.5 }), null, ("renamed.json", file));

        Assert.Equal(FingeringReason.Exact, choice.Reason);
        Assert.Equal("renamed.json", choice.Used?.Name);
    }

    /// <summary>复现（用户创意工坊）：两首 deleted，曲名、曲师、音乐都一样，谱师不同。实测互相只对上 2%–6%。</summary>
    [Fact]
    public void SameSongByDifferentChartersNeverMix()
    {
        var a = Chart(Rhythm(300, seed: 11));
        var b = Chart(Rhythm(300, seed: 12));
        var fileA = Made(a, out _);
        var fileB = Made(b, out _);
        fileA.Song = fileB.Song = "deleted";
        fileA.Artist = fileB.Artist = "linear ring";

        Assert.Equal("a.json", Pick(a, null, ("b.json", fileB), ("a.json", fileA)).Used?.Name);
        Assert.Equal("b.json", Pick(b, null, ("a.json", fileA), ("b.json", fileB)).Used?.Name);
        Assert.Equal(FingeringReason.None, Pick(b, null, ("a.json", fileA)).Reason);
    }

    [Fact]
    public void UpdatedChartIsFollowed()
    {
        var beats = Rhythm(300);
        var file = Made(Chart(beats), out _);

        var choice = Pick(Updated(beats), null, ("a.json", file));

        Assert.Equal(FingeringReason.Followed, choice.Reason);
        Assert.True(choice.Match!.Ratio > 0.95);
        Assert.Equal(1, choice.Match.FallbackSections);
    }

    [Fact]
    public void PartialCandidateKeepsExactCountsEvenWhenThePercentageRoundsToOneHundred()
    {
        var hits = Chart(Rhythm(968));
        var saved = Made(hits, out _);
        var tile = hits[51];
        hits[51] = tile with { Angle = tile.Angle + 30 };

        var choice = Pick(hits, null, ("previous.json", saved));
        var candidate = Assert.Single(choice.Candidates);

        Assert.Equal(FingeringReason.Followed, choice.Reason);
        Assert.Equal(967, candidate.MatchedHits);
        Assert.Equal(968, candidate.TotalHits);
    }

    /// <summary>两份不同的手序都像这一关：不猜。（内容完全一样的两份当成一份，见 FingeringCompatTests.IdenticalCopiesAreOneFingering。）</summary>
    [Fact]
    public void TwoLookalikesAreNotGuessed()
    {
        var beats = Rhythm(300);
        var one = Made(Chart(beats), out _);
        var two = CustomFingering.From(Chart(beats), NotePlanner.Plan(Chart(beats), Options), leftHanded: false);

        var choice = Pick(Updated(beats), null, ("one.json", one), ("two.json", two));

        Assert.Equal(FingeringReason.Unsure, choice.Reason);
        Assert.Null(choice.Used);
        Assert.Equal(2, choice.Candidates.Count);
    }

    [Fact]
    public void PlayersChoiceIsUsed()
    {
        var beats = Rhythm(300);
        var one = Made(Chart(beats), out _);
        var two = Made(Chart(beats), out _);
        var level = Updated(beats);

        Assert.Equal("two.json", Pick(level, "two.json", ("one.json", one), ("two.json", two)).Used?.Name);
        var none = Pick(level, string.Empty, ("one.json", one), ("two.json", two));
        Assert.Equal(FingeringReason.Chosen, none.Reason);
        Assert.Null(none.Used);
    }

    [Fact]
    public void ResavedFingeringKnowsBothVersions()
    {
        var beats = Rhythm(300);
        var old = Made(Chart(beats), out _);
        var updated = Updated(beats);

        // 作者在新版谱面上打开、保存：新文件记下新版指纹，并保留旧版的
        var resaved = CustomFingering.From(updated, NotePlanner.Plan(updated, Options), false);
        resaved.Charts.AddRange(old.Charts.Where(c => !resaved.Charts.Contains(c)));
        var back = CustomFingering.Parse(resaved.ToJson());

        Assert.Equal(FingeringReason.Exact, Pick(Chart(beats), null, ("x.json", back)).Reason);
        Assert.Equal(FingeringReason.Exact, Pick(updated, null, ("x.json", back)).Reason);
    }

    [Fact]
    public void UnrelatedLevelsAreNotMatched()
    {
        var file = Made(Chart(Rhythm(200, seed: 4)), out _);

        var choice = Pick(Chart(Rhythm(200, seed: 5)), null, ("x.json", file));

        Assert.Equal(FingeringReason.None, choice.Reason);
        Assert.Empty(choice.Candidates);
    }

    [Fact]
    public void HoldsFollowTheLevel()
    {
        var hits = Chart(Rhythm(30));
        hits[10] = hits[10] with { HoldSeconds = 1.0 };
        var file = Made(hits, out _);
        var longer = hits.ToList();
        longer[10] = longer[10] with { HoldSeconds = 1.5 };

        var match = FingeringMatcher.Apply(file, longer, NotePlanner.Plan(longer, Options), false);

        var hold = match.Notes.Single(n => n.TileIndex == hits[10].TileIndex);
        Assert.Equal(1.5, hold.Lanes.Values.Max(), 6);
    }
}
