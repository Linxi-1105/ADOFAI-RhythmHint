using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

public sealed class AlternatingChordRunTests
{
    [Theory]
    [InlineData(false, "RLRLRLRLRL")]
    [InlineData(true, "LRLRLRLRLR")]
    public void DoublesEveryThirdAttackKeepTheSameAlternationAsTheSingles(bool mirror, string expected)
    {
        var notes = Plan(1.0 / 3, mirror: mirror);
        Assert.Equal(expected, string.Concat(notes.Select(n => n.PairHand == 1 ? 'R' : 'L')));
        Assert.Equal(new[] { 2, 1, 1, 2, 1, 1, 2, 1, 1, 2 }, notes.Select(n => n.Lanes.Count));
    }

    [Theory]
    [InlineData(.5, false, 1)]
    [InlineData(1.0 / 3, true, 0)]
    public void SlowPulseOrExplicitChordHandKeepsThePreferredDoubles(double beat, bool otherChordHand, int hand)
    {
        var notes = Plan(beat, otherChordHand: otherChordHand);
        Assert.All(notes.Where(n => n.Lanes.Count == 2), n => Assert.Equal(hand, n.PairHand));
    }

    private static List<PlannedNote> Plan(double beat, bool mirror = false, bool otherChordHand = false)
    {
        // Four doubles fall on successive half-beat offsets, with two equally
        // spaced singles between them. The double counts as one hand attack.
        var hits = new List<TileHit>();
        for (var i = 0; i < 10; i++)
        {
            var time = beat * (.5 + i / 3.0);
            hits.Add(new TileHit(2 * i + 1, time, Angle: i % 3 == 1 ? 210 : 240));
            if (i % 3 == 0) hits.Add(new TileHit(2 * i + 2, time + .01, Angle: 15));
        }
        var rhythm = new FingeringRhythm(0, new[] { (0.0, beat) });
        return NotePlanner.Plan(hits, new FingeringOptions
            { Style = FingeringStyle.SteadyHands, LeftHanded = mirror, ChordsOnOtherHand = otherChordHand }, rhythm);
    }
}
