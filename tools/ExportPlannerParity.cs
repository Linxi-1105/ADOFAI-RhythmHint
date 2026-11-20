#:project ../src/RhythmHint.Core/RhythmHint.Core.csproj
#:property TargetFramework=net10.0
#:property PublishAot=false

// Synthetic, public fixtures generated from the current C# implementation, not a copied JS oracle.
// dotnet run tools/ExportPlannerParity.cs -- fixtures.json
// Each case: { name, algorithm, options, notes, sourceHits }, read by tools/planner-js/planner-parity.test.cjs.
using System.Globalization;
using System.Text.Json;
using RhythmHint.Core;

if (args.Length != 1) throw new ArgumentException("Usage: ExportPlannerParity.cs <output.json>");
// Must match `revision` in tools/planner-js/note-planner.js (algorithmFor).
const string BaselineRevision = "054fe9cd6a8961afbf0c8f9c181bd3dcedb12b18";
var cases = new List<object>();
void Add(string name, List<TileHit> hits, FingeringOptions options)
{
    cases.Add(new
    {
        name,
        algorithm = "NotePlanner/" + BaselineRevision + "/" + options.Style,
        options = new
        {
            options.ChordWindow, options.ChordAngle, options.ChordMaxGap, options.ResetWindow, options.ChordFocusWindow,
            options.JackWindow, options.UseRoll, options.OuterRoll, options.LeftHanded, options.ChordsOnOtherHand,
            Style = options.Style.ToString(), options.ReviewShortMinGap, options.ReviewShortMaxGap,
            options.ReviewShortMinNotes, options.ReviewShortMaxNotes, options.ReviewTimingTolerance,
        },
        notes = NotePlanner.Plan(hits, options).Select(n => new
        {
            id = string.Create(CultureInfo.InvariantCulture, $"{n.TileIndex}:{n.LastTileIndex}:{n.Time:R}"),
            tileIndex = n.TileIndex,
            lastTileIndex = n.LastTileIndex,
            time = n.Time,
            lanes = n.Lanes.Select(l => new { lane = l.Key, holdSeconds = l.Value }),
        }),
        // Unknown angle is null, never JSON-invalid NaN; the JS planner reads null as unknown.
        sourceHits = hits.Select(h => new
        {
            tileIndex = h.TileIndex,
            time = h.Time,
            holdSeconds = h.HoldSeconds,
            angle = double.IsNaN(h.Angle) ? (double?)null : h.Angle,
        }),
    });
}
List<TileHit> Run(double gap, int count, double start = 1) =>
    Enumerable.Range(0, count).Select(i => new TileHit(i + 1, start + i * gap)).ToList();
var thresholdGaps = new[] { .035 - 1e-7, .035, .035 + 1e-7, .046875, .060 - 2e-6, .060,
    .060 + 2e-6, 1.0 / 15, .0825 - 2e-6, .0825, .0825 + 2e-6,
    .065 - 2e-6, .065, .065 + 2e-6, .13 - 2e-6, .13, .13 + 2e-6,
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
        // 连着的长按砖：每块按住到下一块，上一下还按着时换手接；最后两段是长按正好在双押、三押按下时结束
        var chain = new List<TileHit>(); var c = 1.0;
        foreach (var (length, gap) in new[] { (5, .158), (3, .474), (4, .09), (3, .06) })
        {
            for (var j = 0; j < length; j++) chain.Add(new TileHit(chain.Count + 1, c + j * gap, gap, 180));
            c += length * gap + .5;
            chain.Add(new TileHit(chain.Count + 1, c, 0, 180));
            c += .4;
        }
        foreach (var size in new[] { 2, 3 })
        {
            chain.Add(new TileHit(chain.Count + 1, c, .6, 180));
            for (var k = 0; k < size; k++) chain.Add(new TileHit(chain.Count + 1, c + .6 + k * .02, 0, k == 0 ? 180 : 10));
            c += 1.5;
        }
        Add($"hold-chains-{mask}-{style}", chain, options);
        // 外轮换手不能换到接力长按的那只手上（TUF 3303 的 3253–3263）
        Add($"hold-after-roll-{mask}-{style}", new List<TileHit>
        {
            new(3253, 131.0399012125343, 0, 150), new(3254, 131.0719012125343, 0, 180), new(3256, 131.07279010142318, 0, 5),
            new(3257, 131.0772345458676, 0, 25), new(3259, 131.10390121253428, 0, 150), new(3260, 131.1359012125343, 2.2399993690142708, 180),
            new(3261, 133.37590058154856, 0.1279999999999859, 12780), new(3263, 133.50390058154855, 0, 360),
        }, options);
    }
    var custom = new FingeringOptions { Style = style, ChordWindow = .02, ChordAngle = 45, ChordMaxGap = .075,
        ResetWindow = .2, ChordFocusWindow = .31, JackWindow = .11, UseRoll = true, OuterRoll = true,
        LeftHanded = true, ChordsOnOtherHand = true, ReviewShortMinGap = .055, ReviewShortMaxGap = .09,
        ReviewShortMinNotes = 2, ReviewShortMaxNotes = 7, ReviewTimingTolerance = .004 };
    foreach (var count in new[] { 2, 3, 4, 5, 6, 7, 8 }) Add($"custom-{count}-{style}", Run(.06666666666666667, count), custom);
}
File.WriteAllText(args[0], JsonSerializer.Serialize(cases));
Console.WriteLine($"Generated {cases.Count} synthetic C# parity fixtures.");
