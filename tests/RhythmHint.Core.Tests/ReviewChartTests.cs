using System.Globalization;
using System.Text.Json;

namespace RhythmHint.Core.Tests;

public sealed class ReviewChartTests
{
    [Fact]
    public void Export_RecordsEveryPlannerParameterAndBindsEachOneToIdentity()
    {
        var notes = new[] { Note(1, 2, 0) };
        string Export(FingeringOptions options) => ReviewChart.Export("x", "demo", notes, options);
        using var original = JsonDocument.Parse(Export(new FingeringOptions()));
        var properties = typeof(FingeringOptions).GetProperties();
        Assert.Equal(properties.Length, original.RootElement.GetProperty("options").EnumerateObject().Count());
        foreach (var property in properties)
        {
            var options = new FingeringOptions();
            var initial = property.GetValue(options)!;
            object changed = initial switch
            {
                bool value => !value,
                double value => value + .001,
                int value => value + 1,
                FingeringStyle => FingeringStyle.ReviewPreferenceV1,
                _ => throw new InvalidOperationException(property.Name),
            };
            property.SetValue(options, changed);
            using var candidate = JsonDocument.Parse(Export(options));
            Assert.True(candidate.RootElement.GetProperty("options").TryGetProperty(property.Name, out _), property.Name);
            Assert.NotEqual(original.RootElement.GetProperty("chartId").GetString(), candidate.RootElement.GetProperty("chartId").GetString());
        }
    }

    [Fact]
    public void AngleAndThreeFourKeyHoldsSurviveThePortableSnapshot()
    {
        var hits = new[] { new TileHit(1, 1, .1), new TileHit(2, 1.02, .2, 30),
            new TileHit(3, 1.04, .3, 15), new TileHit(4, 1.06, .4, 30) };
        var options = new FingeringOptions { ChordsOnOtherHand = true };
        var notes = NotePlanner.Plan(hits, options);
        Assert.Single(notes);
        Assert.Equal(4, notes[0].Lanes.Count);
        using var document = JsonDocument.Parse(ReviewChart.Export("x", "game", notes, options, sourceHits: hits));
        var raw = document.RootElement.GetProperty("sourceHits");
        Assert.False(raw[0].TryGetProperty("angle", out _));
        Assert.Equal(30, raw[1].GetProperty("angle").GetDouble());
        var changed = hits.ToArray(); changed[1] = changed[1] with { Angle = 20 };
        using var alternate = JsonDocument.Parse(ReviewChart.Export("x", "game", notes, options, sourceHits: changed));
        Assert.NotEqual(document.RootElement.GetProperty("chartId").GetString(), alternate.RootElement.GetProperty("chartId").GetString());
        Assert.Equal(new[] { .1, .2, .3, .4 }, document.RootElement.GetProperty("notes")[0].GetProperty("lanes")
            .EnumerateArray().Select(lane => lane.GetProperty("holdSeconds").GetDouble()).Order());
    }

    [Fact]
    public void ExperimentalExport_RecordsItsVersionAndCannotShareTheLegacyIdentity()
    {
        // Even a chart with identical lanes must not restore a draft from another algorithm.
        var hits = new[] { new TileHit(1, 1) };
        var legacy = new FingeringOptions();
        var experimental = new FingeringOptions { Style = FingeringStyle.ReviewPreferenceV1 };
        using var original = JsonDocument.Parse(ReviewChart.Export("x", "game", NotePlanner.Plan(hits, legacy), legacy, sourceHits: hits));
        using var revised = JsonDocument.Parse(ReviewChart.Export("x", "game", NotePlanner.Plan(hits, experimental), experimental, sourceHits: hits));
        Assert.Equal(original.RootElement.GetProperty("notes").GetRawText(), revised.RootElement.GetProperty("notes").GetRawText());
        Assert.Equal(ReviewChart.ReviewRulesAlgorithm, revised.RootElement.GetProperty("algorithm").GetString());
        Assert.Equal("ReviewPreferenceV1", revised.RootElement.GetProperty("options").GetProperty("Style").GetString());
        Assert.Equal("TimeReset", original.RootElement.GetProperty("options").GetProperty("Style").GetString());
        Assert.NotEqual(original.RootElement.GetProperty("chartId").GetString(), revised.RootElement.GetProperty("chartId").GetString());
    }

    [Fact]
    public void Export_PreservesPlannerOutputIncludingPseudoChordsAndHolds()
    {
        var options = new FingeringOptions();
        var hits = new[] { new TileHit(1, 0), new TileHit(2, 0.025), new TileHit(3, 1, 0.75) };
        var notes = NotePlanner.Plan(hits, options);
        using var json = JsonDocument.Parse(ReviewChart.Export("测试", "game", notes, options));
        var root = json.RootElement;
        Assert.Equal("rhythmhint-review", root.GetProperty("format").GetString());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("game", root.GetProperty("source").GetString());
        Assert.Equal(ReviewChart.Algorithm, root.GetProperty("algorithm").GetString());
        Assert.Equal(notes.Count, root.GetProperty("notes").GetArrayLength());
        for (var i = 0; i < notes.Count; i++)
        {
            var actual = root.GetProperty("notes")[i];
            Assert.Equal(notes[i].TileIndex, actual.GetProperty("tileIndex").GetInt32());
            Assert.Equal(notes[i].LastTileIndex, actual.GetProperty("lastTileIndex").GetInt32());
            Assert.Equal(notes[i].Time, actual.GetProperty("time").GetDouble());
            Assert.Equal(notes[i].Lanes.Count, actual.GetProperty("lanes").GetArrayLength());
            foreach (var lane in actual.GetProperty("lanes").EnumerateArray())
            {
                Assert.Equal(notes[i].Lanes[lane.GetProperty("lane").GetInt32()], lane.GetProperty("holdSeconds").GetDouble());
            }
        }

        Assert.Equal(2, root.GetProperty("notes")[0].GetProperty("lastTileIndex").GetInt32());
        Assert.Contains(root.GetProperty("notes")[1].GetProperty("lanes").EnumerateArray(), lane => lane.GetProperty("holdSeconds").GetDouble() > 0);
        Assert.Equal(0, root.GetProperty("review").GetProperty("edits").GetArrayLength());
    }

    [Fact]
    public void Identity_DistinguishesRawChordTimingEvenWhenPlannedNotesMatch()
    {
        var options = new FingeringOptions();
        var first = new[] { new TileHit(1, 0), new TileHit(2, 0.025) };
        var second = new[] { new TileHit(1, 0), new TileHit(2, 0.030) };
        using var original = JsonDocument.Parse(ReviewChart.Export("x", "game", NotePlanner.Plan(first, options), options, sourceHits: first));
        using var alternate = JsonDocument.Parse(ReviewChart.Export("x", "game", NotePlanner.Plan(second, options), options, sourceHits: second));
        Assert.Equal(original.RootElement.GetProperty("notes").GetRawText(), alternate.RootElement.GetProperty("notes").GetRawText());
        Assert.NotEqual(original.RootElement.GetProperty("chartId").GetString(), alternate.RootElement.GetProperty("chartId").GetString());
        using var repeated = JsonDocument.Parse(ReviewChart.Export("renamed", "game", NotePlanner.Plan(first, options), options, sourceHits: first));
        Assert.Equal(original.RootElement.GetProperty("chartId").GetString(), repeated.RootElement.GetProperty("chartId").GetString());
    }

    [Fact]
    public void Export_PreservesEverySourceHitAtFullPrecisionBeforeChordMerging()
    {
        var hits = new[]
        {
            new TileHit(8, 0.1234567890123456, 0.3333333333333333),
            new TileHit(9, 0.1484567890123456),
            new TileHit(12, 1.9876543210987654, 0.7654321098765432),
        };
        var options = new FingeringOptions();
        var notes = NotePlanner.Plan(hits, options);
        Assert.Equal(2, notes.Count);
        using var json = JsonDocument.Parse(ReviewChart.Export("x", "game", notes, options, sourceHits: hits));
        var actual = json.RootElement.GetProperty("sourceHits");
        Assert.Equal(hits.Length, actual.GetArrayLength());
        for (var i = 0; i < hits.Length; i++)
        {
            Assert.Equal(hits[i].TileIndex, actual[i].GetProperty("tileIndex").GetInt32());
            Assert.Equal(hits[i].Time, actual[i].GetProperty("time").GetDouble());
            Assert.Equal(hits[i].HoldSeconds, actual[i].GetProperty("holdSeconds").GetDouble());
        }
    }

    [Fact]
    public void Export_EscapesNamesAndKeepsFullPrecisionAcrossCultures()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            var note = Note(1.2345678901234567, 2, 0.1234567890123456);
            const string name = "名称\"\\\n\t\0🎵";
            using var json = JsonDocument.Parse(ReviewChart.Export(name, "demo", new[] { note }, new FingeringOptions()));
            Assert.Equal(name, json.RootElement.GetProperty("name").GetString());
            Assert.Equal(note.Time, json.RootElement.GetProperty("notes")[0].GetProperty("time").GetDouble());
            Assert.Equal(note.Lanes[2], json.RootElement.GetProperty("notes")[0].GetProperty("lanes")[0].GetProperty("holdSeconds").GetDouble());
            Assert.Equal(0.165, json.RootElement.GetProperty("options").GetProperty("ResetWindow").GetDouble());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Identity_IsStableAcrossExportsButChangesWhenTimingLanesHoldsOrOptionsChange()
    {
        var options = new FingeringOptions();
        var notes = new[] { Note(1, 2, 0.5) };
        string Id(PlannedNote[] input, FingeringOptions parameters, string name = "A")
        {
            using var json = JsonDocument.Parse(ReviewChart.Export(name, "demo", input, parameters));
            return json.RootElement.GetProperty("chartId").GetString()!;
        }

        var original = Id(notes, options);
        Assert.Equal(original, Id(notes, options, "B"));
        Assert.Equal(64, original.Length);
        Assert.NotEqual(original, Id(new[] { Note(1.0000000001, 2, 0.5) }, options));
        Assert.NotEqual(original, Id(new[] { Note(1, 1, 0.5) }, options));
        Assert.NotEqual(original, Id(new[] { Note(1, 2, 0.6) }, options));
        options.JackWindow = 0.16;
        Assert.NotEqual(original, Id(notes, options));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Export_RejectsNonfiniteTime(double time) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ReviewChart.Export("x", "demo", new[] { Note(time, 2, 0) }, new FingeringOptions()));

    [Fact]
    public void Export_RejectsInvalidLanesHoldsAndDuplicateNotes()
    {
        var options = new FingeringOptions();
        Assert.Throws<ArgumentException>(() => ReviewChart.Export("x", "demo", new[] { Note(0, 4, 0) }, options));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReviewChart.Export("x", "demo", new[] { Note(0, 2, -1) }, options));
        Assert.Throws<ArgumentException>(() => ReviewChart.Export("x", "demo", new[] { Note(0, 2, 0), Note(0, 3, 0) }, options));
    }

    private static PlannedNote Note(double time, int lane, double hold)
    {
        var note = new PlannedNote(1, time);
        note.Lanes[lane] = hold;
        return note;
    }
}
