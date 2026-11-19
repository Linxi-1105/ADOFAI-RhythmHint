#:project ../src/RhythmHint.Core/RhythmHint.Core.csproj
#:property TargetFramework=net10.0
#:property PublishAot=false

// Synthetic, public fixtures generated from the current C# implementation, not a copied JS oracle.
// dotnet run tools/ExportPlannerParity.cs -- fixtures.json
using System.Text.Json;
using RhythmHint.Core;

if (args.Length != 1) throw new ArgumentException("Usage: ExportPlannerParity.cs <output.json>");
var cases = new List<JsonElement>();
void Add(string name, List<TileHit> hits, FingeringOptions options)
{
    using var document = JsonDocument.Parse(ReviewChart.Export(name, "demo", NotePlanner.Plan(hits, options), options,
        exportedAt: DateTimeOffset.UnixEpoch, sourceHits: hits));
    cases.Add(document.RootElement.Clone());
}
List<TileHit> Run(double gap, int count, double start = 1) =>
    Enumerable.Range(0, count).Select(i => new TileHit(i + 1, start + i * gap)).ToList();
var thresholdGaps = new[] { .035 - 1e-7, .035, .035 + 1e-7, .046875, .060 - 2e-6, .060,
    .060 + 2e-6, 1.0 / 15, .0825 - 2e-6, .0825, .0825 + 2e-6,
    .15 - 2e-6, .15, .15 + 2e-6, .165 - 2e-6, .165, .165 + 2e-6, .45, .6 };
foreach (var style in Enum.GetValues<FingeringStyle>())
{
    foreach (var gap in thresholdGaps)
        foreach (var count in new[] { 2, 3, 4, 5, 6, 24 }) Add($"gap-{gap:R}-{count}-{style}", Run(gap, count), new() { Style = style });
    foreach (var angle in new[] { 0, 29.9, 30, 30.1 - 1e-8, 30.1, 30.1 + 1e-8, 180 })
        foreach (var gap in new[] { .035, .060 - 2e-6, .060, .060 + 1e-6, .060 + 2e-6 })
            foreach (var count in new[] { 2, 3, 4, 5 })
                Add($"angle-{angle:R}-{gap:R}-{count}-{style}", Run(gap, count).Select((h, i) => h with { Angle = i == 0 ? double.NaN : angle }).ToList(), new() { Style = style });
    for (var mask = 0; mask < 16; mask++)
    {
        var options = new FingeringOptions { Style = style, LeftHanded = (mask & 1) != 0, ChordsOnOtherHand = (mask & 2) != 0,
            UseRoll = (mask & 4) != 0, OuterRoll = (mask & 8) != 0 };
        foreach (var holds in new[] { false, true })
        {
            var rng = new Random(1911 + mask);
            var hits = new List<TileHit>(); var time = -.2;
            for (var i = 0; i < 400; i++)
            {
                time += thresholdGaps[rng.Next(thresholdGaps.Length)];
                var angle = rng.Next(3) == 0 ? double.NaN : new[] { 0, 15, 30, 30.1, 31, 180 }[rng.Next(6)];
                hits.Add(new TileHit(i + 1, time, holds && i % 19 == 0 ? .1 + rng.NextDouble() : 0, angle));
            }
            Add($"random-{mask}-{holds}-{style}", hits, options);
        }
        var multi = new List<TileHit>(); var t = 1.0;
        foreach (var count in new[] { 1, 2, 3, 4, 5, 4, 3, 2, 1 })
        {
            for (var j = 0; j < count; j++) multi.Add(new TileHit(multi.Count + 1, t + j * .02, (j + 1) * .125, j == 0 ? 180 : 15));
            t += .16;
        }
        Add($"multi-holds-{mask}-{style}", multi, options);
    }
    var custom = new FingeringOptions { Style = style, ChordWindow = .02, ChordAngle = 45, ChordMaxGap = .075,
        ResetWindow = .2, ChordFocusWindow = .31, JackWindow = .11, UseRoll = true, OuterRoll = true,
        LeftHanded = true, ChordsOnOtherHand = true, ReviewShortMinGap = .055, ReviewShortMaxGap = .09,
        ReviewShortMinNotes = 2, ReviewShortMaxNotes = 7, ReviewTimingTolerance = .004 };
    foreach (var count in new[] { 2, 3, 4, 5, 6, 7, 8 }) Add($"custom-{count}-{style}", Run(.06666666666666667, count), custom);
}
File.WriteAllText(args[0], JsonSerializer.Serialize(cases));
Console.WriteLine($"Generated {cases.Count} synthetic C# parity fixtures.");
