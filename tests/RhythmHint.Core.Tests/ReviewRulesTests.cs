using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

public sealed class ReviewRulesTests
{
    private static FingeringOptions Experimental(bool leftHanded = false) =>
        new() { Style = FingeringStyle.ReviewPreferenceV1, LeftHanded = leftHanded };

    private static List<TileHit> Chart(params (double Time, bool Chord)[] moments)
    {
        var hits = new List<TileHit>();
        foreach (var moment in moments)
        {
            hits.Add(new TileHit(hits.Count + 1, moment.Time));
            if (moment.Chord) hits.Add(new TileHit(hits.Count + 1, moment.Time + .01));
        }

        return hits;
    }

    private static List<TileHit> Run(int count, double gap, double start = 1) =>
        Enumerable.Range(0, count).Select(i => new TileHit(i + 1, start + i * gap)).ToList();

    private static string[] Shape(IEnumerable<PlannedNote> notes) =>
        notes.Select(note => string.Join("+", note.Lanes.Keys.Select(lane => "DFJK"[lane]))).ToArray();

    private static void SamePlan(IReadOnlyList<PlannedNote> expected, IReadOnlyList<PlannedNote> actual)
    {
        SameStructure(expected, actual);
        for (var i = 0; i < expected.Count; i++)
            Assert.Equal(expected[i].Lanes.ToArray(), actual[i].Lanes.ToArray());
    }

    private static void SameStructure(IReadOnlyList<PlannedNote> expected, IReadOnlyList<PlannedNote> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].TileIndex, actual[i].TileIndex);
            Assert.Equal(expected[i].LastTileIndex, actual[i].LastTileIndex);
            Assert.Equal(expected[i].Time, actual[i].Time);
            Assert.Equal(expected[i].Lanes.Values.Order(), actual[i].Lanes.Values.Order());
        }
    }

    [Fact]
    public void DefaultOffRetainsLegacyFocusAndShortRoll()
    {
        Assert.Equal(FingeringStyle.TimeReset, new FingeringOptions().Style);
        Assert.Equal(new[] { "J+K", "F", "J" }, Shape(NotePlanner.Plan(
            Chart((1, true), (1.234375, false), (1.46875, false)), new FingeringOptions())));
        Assert.Equal(new[] { "K", "J", "D", "F" }, Shape(NotePlanner.Plan(
            Run(4, 1.0 / 15), new FingeringOptions { Style = FingeringStyle.TimeReset })));
    }

    [Fact]
    public void RecoveredHandIsNotForcedAwayFromSlowChords()
    {
        var notes = NotePlanner.Plan(Chart((1, true), (1.234375, false), (1.46875, false), (1.703125, true)), Experimental());
        Assert.Equal(new[] { "J+K", "J", "J", "J+K" }, Shape(notes));
    }

    [Fact]
    public void CloseChordStillGetsAFreeHand()
    {
        var notes = NotePlanner.Plan(Chart((1, true), (1.09375, false), (1.1875, true)), Experimental());
        Assert.Equal(new[] { "J+K", "F", "J+K" }, Shape(notes));
    }

    [Fact]
    public void MediumPhraseReplacesStateInducedDfWithAlternatingIndices()
    {
        // A reduced review example: the legacy look-ahead produces J D F at 117.188 ms.
        var hits = Chart((1, true), (1.234375, false), (1.46875, false),
            (1.5859375, false), (1.703125, false), (1.9375, true));
        var legacy = NotePlanner.Plan(hits, new FingeringOptions());
        var revised = NotePlanner.Plan(hits, Experimental());
        Assert.Equal(new[] { "J", "D", "F" }, Shape(legacy.Skip(2).Take(3)));
        Assert.Equal(new[] { "J", "F", "J" }, Shape(revised.Skip(2).Take(3)));
        SameStructure(legacy, revised);
    }

    [Fact]
    public void RecoveredMediumPhraseChoosesMainHandForTheWholeRun()
    {
        var moments = new List<(double, bool)> { (1, true) };
        moments.AddRange(Enumerable.Range(0, 29).Select(i => (1.1875 + i * .09375, false)));
        var notes = NotePlanner.Plan(Chart(moments.ToArray()), Experimental());
        Assert.Equal(Enumerable.Range(0, 29).Select(i => i % 2 == 0 ? "J" : "F"), Shape(notes.Skip(1)));
    }

    [Fact]
    public void FourNoteBurstAtSixtySevenMillisecondsUsesIndicesAndKeepsChordBoundary()
    {
        var moments = Enumerable.Range(0, 4).Select(i => (1 + i / 15.0, false)).Append((1 + 4 / 15.0, true)).ToArray();
        var notes = NotePlanner.Plan(Chart(moments), Experimental());
        Assert.Equal(new[] { "J", "F", "J", "F", "J+K" }, Shape(notes));
    }

    [Fact]
    public void FiveNoteBurstBetweenRightChordsUsesTheOtherPhase()
    {
        var moments = new List<(double, bool)> { (1, true) };
        moments.AddRange(Enumerable.Range(1, 5).Select(i => (1 + i / 15.0, false)));
        moments.Add((1 + 6 / 15.0, true));
        var notes = NotePlanner.Plan(Chart(moments.ToArray()), Experimental());
        Assert.Equal(new[] { "J+K", "F", "J", "F", "J", "F", "J+K" }, Shape(notes));
    }

    [Fact]
    public void NeitherSafePhaseLeavesTheCompleteBurstUnchanged()
    {
        // Four alternating singles cannot start and end on the left. Do not repair
        // only the middle and create a J→JK jack at the far boundary.
        var moments = new List<(double, bool)> { (1, true) };
        moments.AddRange(Enumerable.Range(1, 4).Select(i => (1 + i / 15.0, false)));
        moments.Add((1 + 5 / 15.0, true));
        var hits = Chart(moments.ToArray());
        SamePlan(NotePlanner.Plan(hits, new FingeringOptions()), NotePlanner.Plan(hits, Experimental()));
    }

    [Theory]
    [InlineData(4, .046875)]
    [InlineData(5, .046875)]
    [InlineData(6, .06666666666666667)]
    [InlineData(32, .06666666666666667)]
    [InlineData(4, .059)]
    public void VeryFastOrLongPhrasesKeepTheirRolls(int count, double gap)
    {
        var hits = Run(count, gap);
        SamePlan(NotePlanner.Plan(hits, new FingeringOptions()), NotePlanner.Plan(hits, Experimental()));
        Assert.Contains(NotePlanner.Plan(hits, Experimental()), n => n.Lanes.ContainsKey(0) || n.Lanes.ContainsKey(3));
    }

    [Fact]
    public void MixedFastGapsCannotPassTheShortPhraseTestByAveraging()
    {
        var hits = Chart((1, false), (1.06, false), (1.1, false), (1.18, false), (1.24, false));
        SamePlan(NotePlanner.Plan(hits, new FingeringOptions()), NotePlanner.Plan(hits, Experimental()));
    }

    [Fact]
    public void ExplicitInnerRollPreferenceIsNotReplacedByTheOuterRollExperiment()
    {
        var hits = Run(4, 1.0 / 15);
        var legacy = new FingeringOptions { OuterRoll = false };
        var experimental = new FingeringOptions { OuterRoll = false, Style = FingeringStyle.ReviewPreferenceV1 };
        Assert.Equal(new[] { "J", "K", "F", "D" }, Shape(NotePlanner.Plan(hits, experimental)));
        SamePlan(NotePlanner.Plan(hits, legacy), NotePlanner.Plan(hits, experimental));
    }

    [Fact]
    public void ShortPhraseLimitsAreConfigurableRatherThanHiddenConstants()
    {
        var four = Run(4, 1.0 / 15);
        foreach (var option in new[]
        {
            new FingeringOptions { Style = FingeringStyle.ReviewPreferenceV1, ReviewShortMinGap = .070 },
            new FingeringOptions { Style = FingeringStyle.ReviewPreferenceV1, ReviewShortMaxGap = .065 },
            new FingeringOptions { Style = FingeringStyle.ReviewPreferenceV1, ReviewShortMinNotes = 5 },
            new FingeringOptions { Style = FingeringStyle.ReviewPreferenceV1, ReviewShortMaxNotes = 3, ReviewShortMinNotes = 3 }
        })
            SamePlan(NotePlanner.Plan(four, new FingeringOptions()), NotePlanner.Plan(four, option));

        var seven = NotePlanner.Plan(Run(7, 1.0 / 15), new FingeringOptions
        {
            Style = FingeringStyle.ReviewPreferenceV1, ReviewShortMinNotes = 7, ReviewShortMaxNotes = 7
        });
        Assert.Equal(new[] { "J", "F", "J", "F", "J", "F", "J" }, Shape(seven));

        var jittered = Chart((1, false), (1.0666, false), (1.1336, false), (1.2002, false));
        Assert.Equal(new[] { "J", "F", "J", "F" }, Shape(NotePlanner.Plan(jittered, Experimental())));
        SamePlan(NotePlanner.Plan(jittered, new FingeringOptions()), NotePlanner.Plan(jittered,
            new FingeringOptions { Style = FingeringStyle.ReviewPreferenceV1, ReviewTimingTolerance = .0001 }));
    }

    [Fact]
    public void InvalidReviewConfigurationIsRejectedOnlyForThatStyle()
    {
        var hits = Run(4, 1.0 / 15);
        var invalid = new[]
        {
            new FingeringOptions { ReviewShortMinGap = -1 },
            new FingeringOptions { ReviewShortMaxGap = double.PositiveInfinity },
            new FingeringOptions { ReviewTimingTolerance = double.NaN },
            new FingeringOptions { ReviewShortMinGap = .1, ReviewShortMaxGap = .05 },
            new FingeringOptions { ReviewShortMinNotes = 0 },
            new FingeringOptions { ReviewShortMinNotes = 1 },
            new FingeringOptions { ReviewShortMaxNotes = -1 },
            new FingeringOptions { ReviewShortMinNotes = 8, ReviewShortMaxNotes = 4 }
        };
        var baseline = NotePlanner.Plan(hits, new FingeringOptions());
        foreach (var options in invalid)
        {
            SamePlan(baseline, NotePlanner.Plan(hits, options));
            options.Style = (FingeringStyle)999;
            SamePlan(baseline, NotePlanner.Plan(hits, options));
            options.Style = FingeringStyle.ReviewPreferenceV1;
            Assert.ThrowsAny<ArgumentException>(() => NotePlanner.Plan(hits, options));
        }
    }

    [Theory]
    [InlineData(false, 1, "F+J+K", "D")]
    [InlineData(true, 0, "D+F+J", "K")]
    public void TripleBoundaryUsesPairHandButChecksItsExtraLane(bool otherHand, int pairHand, string chord, string freeKey)
    {
        var hits = new List<TileHit> { new(1, 1, Angle: 180), new(2, 1.02, Angle: 30), new(3, 1.04, Angle: 30) };
        hits.AddRange(Run(5, 1.0 / 15, 1 + 1.0 / 15).Select(h => h with { TileIndex = h.TileIndex + 10, Angle = 180 }));
        var baseline = new FingeringOptions { ChordsOnOtherHand = otherHand };
        var reviewed = new FingeringOptions { Style = FingeringStyle.ReviewPreferenceV1, ChordsOnOtherHand = otherHand };
        var expected = NotePlanner.Plan(hits, baseline);
        var actual = NotePlanner.Plan(hits, reviewed);
        Assert.Equal(pairHand, actual[0].PairHand);
        Assert.Equal(chord, Shape(actual.Take(1))[0]);
        Assert.Equal(freeKey, Shape(actual.Skip(1).Take(1))[0]);
        Assert.Empty(actual[0].Lanes.Keys.Intersect(actual[1].Lanes.Keys));
        // Both index phases collide with one of the three real lanes. Leave the
        // original outer-finger escape intact, even though PairHand names one hand.
        SamePlan(expected, actual);
    }

    [Fact]
    public void FourKeyBoundaryKeepsTheBaselineWhenNoPhaseCanAvoidTheCollision()
    {
        var hits = Enumerable.Range(0, 4).Select(i => new TileHit(i + 1, 1 + i * .01, Angle: i == 0 ? 180 : 30)).ToList();
        hits.AddRange(Run(5, 1.0 / 15, 1 + 1.0 / 15).Select(h => h with { TileIndex = h.TileIndex + 10, Angle = 180 }));
        var baseline = NotePlanner.Plan(hits, new FingeringOptions());
        var actual = NotePlanner.Plan(hits, Experimental());
        Assert.Equal(new[] { 0, 1, 2, 3 }, actual[0].Lanes.Keys);
        Assert.NotEmpty(actual[0].Lanes.Keys.Intersect(actual[1].Lanes.Keys));
        SamePlan(baseline, actual);
    }

    [Fact]
    public void AnyHoldKeepsTheWholeLegacyPlanIncludingMirroredFingerDurations()
    {
        var hits = Chart((1, true), (1.234375, false), (1.46875, false));
        hits[0] = hits[0] with { HoldSeconds = .8 };
        hits[1] = hits[1] with { HoldSeconds = .3 };
        hits.AddRange(Run(4, 1.0 / 15, 3).Select(h => h with { TileIndex = h.TileIndex + 10 }));
        var before = hits.ToArray();
        foreach (var left in new[] { false, true })
            SamePlan(NotePlanner.Plan(hits, new FingeringOptions { LeftHanded = left }), NotePlanner.Plan(hits, Experimental(left)));
        Assert.Equal(before, hits);
    }

    [Fact]
    public void FinalExperimentalPlanMirrorsExactly()
    {
        var hits = Chart((1, true), (1.234375, false), (1.46875, false),
            (2, false), (2 + 1.0 / 15, false), (2 + 2.0 / 15, false), (2 + 3.0 / 15, false), (2 + 4.0 / 15, true));
        var right = NotePlanner.Plan(hits, Experimental());
        var left = NotePlanner.Plan(hits, Experimental(true));
        SameStructure(right, left);
        for (var i = 0; i < right.Count; i++)
            Assert.Equal(right[i].Lanes.Select(x => new KeyValuePair<int, double>(3 - x.Key, x.Value)).OrderBy(x => x.Key),
                left[i].Lanes);
    }

    [Fact]
    public void MixedPhrasesRetainMetadataAndNeverIntroduceBoundaryJacksOrOutwardSteps()
    {
        var random = new Random(1172026);
        var gaps = new[] { .046875, .06666666666666667, .09375, .1171875, .1875, .234375 };
        foreach (var left in new[] { false, true })
        for (var round = 0; round < 50; round++)
        {
            var moments = new List<(double, bool)>();
            var t = 0.0;
            for (var part = 0; part < 25; part++)
            {
                var gap = gaps[random.Next(gaps.Length)];
                var count = random.Next(1, 10);
                for (var i = 0; i < count; i++)
                {
                    t += gap;
                    moments.Add((t, random.Next(5) == 0));
                }
                t += .2;
            }
            var hits = Chart(moments.ToArray());
            var options = Experimental(left);
            var notes = NotePlanner.Plan(hits, options);
            SameStructure(NotePlanner.Plan(hits, new FingeringOptions { LeftHanded = left }), notes);
            for (var i = 1; i < notes.Count; i++)
            {
                var gap = notes[i].Time - notes[i - 1].Time;
                if (gap < options.JackWindow - .000001)
                    Assert.Empty(notes[i].Lanes.Keys.Intersect(notes[i - 1].Lanes.Keys));
                if (notes[i].IsChord || notes[i - 1].IsChord || gap > options.ResetWindow + .000001) continue;
                var from = notes[i - 1].Lanes.Keys.Single();
                var to = notes[i].Lanes.Keys.Single();
                Assert.False((from == 1 && to == 0) || (from == 2 && to == 3),
                    $"Unexpected outward step at round {round}, tile {notes[i].TileIndex}");
            }
        }
    }
}
