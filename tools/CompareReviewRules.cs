#:project ../src/RhythmHint.Core/RhythmHint.Core.csproj
#:property TargetFramework=net10.0
#:property PublishAot=false

// Reproduce the immutable baseline before measuring candidate rules against user edits.
// dotnet run tools/CompareReviewRules.cs -- review.json [more.review.json]
//   [--output summary.json] [--export-dir candidate-reviews]
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using RhythmHint.Core;

var inputs = new List<string>();
string? output = null, exportDirectory = null;
try
{
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--output": output = OptionValue(args, ref i); break;
            case "--export-dir": exportDirectory = OptionValue(args, ref i); break;
            case "--help": PrintUsage(); return 0;
            default:
                if (args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Unknown option: " + args[i]);
                inputs.Add(Path.GetFullPath(args[i]));
                break;
        }
    }
    if (inputs.Count == 0) throw new ArgumentException("At least one review JSON is required.");
    if (output != null && inputs.Contains(output, StringComparer.OrdinalIgnoreCase))
        throw new ArgumentException("The summary must not overwrite an input review.");
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message); PrintUsage(); return 2;
}

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};
var results = new List<object>();
var totals = new Counts();
var failures = 0;
var usedExportPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (var input in inputs)
{
    try
    {
        using var document = JsonDocument.Parse(File.ReadAllText(input));
        var root = document.RootElement;
        Require(Text(root, "format") == ReviewChart.Format && Integer(root, "schemaVersion") == ReviewChart.SchemaVersion,
            "Unsupported review format or schema version.");
        Require(Text(root, "algorithm") == ReviewChart.Algorithm,
            "This comparison requires a current TimeReset baseline. Older review JSON remains editable in the editor, but must not be replanned with a different algorithm. Export a new baseline first.");
        var options = ReadOptions(root.GetProperty("options"));
        Require(options.Style == FingeringStyle.TimeReset, "The baseline must use TimeReset.");
        var hits = ReadHits(root.GetProperty("sourceHits"));
        var originals = ReadNotes(root.GetProperty("notes"));
        var byId = originals.ToDictionary(note => note.Id, StringComparer.Ordinal);
        var review = root.GetProperty("review");
        var edits = ReadEdits(review.GetProperty("edits"), byId);
        var reviewStatus = Text(review, "status");
        Require(reviewStatus is "draft" or "complete", "Unknown review status.");
        Require(reviewStatus != "complete" || edits.Values.Count(edit => edit.Reviewed) == originals.Count,
            "A complete review must mark every note as reviewed.");

        options.Style = FingeringStyle.TimeReset;
        var baseline = NotePlanner.Plan(hits, options);
        Require(baseline.Count == originals.Count,
            $"Baseline note count differs: generated {baseline.Count}, stored {originals.Count}.");
        for (var i = 0; i < originals.Count; i++)
        {
            var expected = originals[i];
            Require(SameIdentity(baseline[i], expected) && SameLanes(baseline[i].Lanes, expected.Lanes),
                $"Baseline mismatch at note {i} (id {expected.Id}). Original times, tile ranges, lanes and holds must reproduce exactly.");
        }

        options.Style = FingeringStyle.ReviewPreferenceV1;
        var candidate = NotePlanner.Plan(hits, options);
        Require(candidate.Count == baseline.Count, "Candidate rules changed the number of notes.");
        for (var i = 0; i < originals.Count; i++)
        {
            Require(SameIdentity(candidate[i], originals[i]), $"Candidate rules changed timing/tile range at note {i}.");
            Require(SameHolds(candidate[i].Lanes.Values, originals[i].Lanes.Values),
                $"Candidate rules changed lane count or hold durations at note {i}.");
        }

        var counts = new Counts { TotalNotes = originals.Count, ReviewEditRecords = edits.Count };
        var effectiveUser = new List<PlannedNote>(originals.Count);
        for (var i = 0; i < originals.Count; i++)
        {
            var original = originals[i];
            edits.TryGetValue(original.Id, out var edit);
            var userLanes = edit?.Lanes ?? original.Lanes;
            effectiveUser.Add(WithLanes(baseline[i], userLanes));
            var userChanged = !SameLanes(original.Lanes, userLanes);
            var rulesChanged = !SameLanes(original.Lanes, candidate[i].Lanes);
            if (edit?.Reviewed == true) counts.ReviewedNotes++;
            if (rulesChanged) counts.TotalNewChanges++;
            if (userChanged)
            {
                counts.UserChangedNotes++;
                if (edit?.Reviewed == true) counts.UserChangedReviewedNotes++;
                if (SameLanes(candidate[i].Lanes, userLanes)) counts.MatchedUserChanges++;
                else counts.UnmatchedUserChanges++;
            }
            else
            {
                if (edit != null) counts.UnchangedEditRecords++;
                if (rulesChanged)
                {
                    counts.ChangedUneditedNotes++;
                    if (edit?.Reviewed == true) counts.ChangedReviewedUneditedNotes++;
                    else counts.ChangedUnreviewedUneditedNotes++;
                }
            }
        }
        string? exportPath = null;
        if (exportDirectory != null)
        {
            var stem = Path.GetFileName(input);
            stem = stem.EndsWith(".review.json", StringComparison.OrdinalIgnoreCase)
                ? stem[..^12] : Path.GetFileNameWithoutExtension(stem);
            exportPath = Path.Combine(exportDirectory, stem + ".rules.review.json");
            Require(!inputs.Contains(exportPath, StringComparer.OrdinalIgnoreCase)
                && !string.Equals(output, exportPath, StringComparison.OrdinalIgnoreCase)
                && usedExportPaths.Add(exportPath), "Output paths collide: " + exportPath);
            var labels = root.GetProperty("keyLabels").EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
            var exported = JsonNode.Parse(ReviewChart.Export(Text(root, "name") + " · 附加规则候选",
                Text(root, "source"), candidate, options, keyLabels: labels, sourceHits: hits))!.AsObject();
            // Keep audio and grid metadata so the exported candidate opens at the same music offset.
            foreach (var field in new[] { "sourceFile", "timing" })
                if (root.TryGetProperty(field, out var value)) exported[field] = JsonNode.Parse(value.GetRawText());
            exported["comparisonSource"] = new JsonObject
            {
                ["chartId"] = Text(root, "chartId"),
                ["name"] = Text(root, "name"),
                ["useReviewRules"] = true,
            };
            Directory.CreateDirectory(exportDirectory);
            File.WriteAllText(exportPath, exported.ToJsonString(jsonOptions), new UTF8Encoding(false));
        }
        totals.Add(counts);
        results.Add(new
        {
            input, chartId = Text(root, "chartId"), name = Text(root, "name"), reviewStatus,
            inputAlgorithm = Text(root, "algorithm"), candidateAlgorithm = ReviewChart.ReviewRulesAlgorithm,
            success = true, baselineVerifiedExactly = true, sourceHits = hits.Count, counts,
            matchedUserChangeRate = counts.UserChangedNotes == 0 ? (double?)null : (double)counts.MatchedUserChanges / counts.UserChangedNotes,
            structure = new { noteCountPreserved = true, tileRangesPreserved = true, timesPreservedExactly = true, holdDurationMultisetsPreservedExactly = true },
            adjacentSharedLanePairs = new
            {
                windowSeconds = options.JackWindow, comparisonEpsilonSeconds = 1e-6,
                baseline = CountJacks(baseline, options.JackWindow),
                candidate = CountJacks(candidate, options.JackWindow),
                effectiveUserReview = CountJacks(effectiveUser, options.JackWindow),
            },
            exportPath,
        });
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
        or InvalidDataException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException)
    {
        failures++;
        results.Add(new { input, success = false, error = error.Message });
    }
}

var summary = new
{
    format = "rhythmhint-rule-comparison", schemaVersion = 1,
    generatedAt = DateTimeOffset.UtcNow, success = failures == 0,
    chartsCompared = inputs.Count - failures, chartsFailed = failures,
    interpretation = "User changes count only edits whose lanes differ from the immutable original. Untouched or unreviewed notes are not approvals; additional candidate changes are measurements, not automatically errors. Adjacent shared-lane pairs use gap < JackWindow - 1e-6, matching the planner threshold.",
    totals, charts = results,
};
var summaryJson = JsonSerializer.Serialize(summary, jsonOptions);
if (output != null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, summaryJson, new UTF8Encoding(false));
}
Console.WriteLine(summaryJson);
return failures == 0 ? 0 : 1;

static void PrintUsage() => Console.Error.WriteLine("dotnet run tools/CompareReviewRules.cs -- <review.json> [more.review.json] [--output summary.json] [--export-dir directory]");
static string OptionValue(string[] arguments, ref int index)
{
    if (++index >= arguments.Length || arguments[index].StartsWith("--", StringComparison.Ordinal))
        throw new ArgumentException("Missing option value.");
    return Path.GetFullPath(arguments[index]);
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}
static string Text(JsonElement value, string name)
{
    var result = value.GetProperty(name).GetString();
    Require(!string.IsNullOrEmpty(result), "Missing or empty " + name + ".");
    return result!;
}
static int Integer(JsonElement value, string name) => value.GetProperty(name).GetInt32();
static double Number(JsonElement value, string name, bool nonnegative = false)
{
    var result = value.GetProperty(name).GetDouble();
    Require(double.IsFinite(result) && (!nonnegative || result >= 0), "Invalid " + name + ".");
    return result;
}
static FingeringOptions ReadOptions(JsonElement value)
{
    var result = new FingeringOptions
    {
        ChordWindow = Number(value, "ChordWindow", true),
        ChordAngle = Number(value, "ChordAngle", true),
        ChordMaxGap = Number(value, "ChordMaxGap", true),
        ResetWindow = Number(value, "ResetWindow", true),
        ChordFocusWindow = Number(value, "ChordFocusWindow", true),
        JackWindow = Number(value, "JackWindow", true),
        UseRoll = value.GetProperty("UseRoll").GetBoolean(),
        OuterRoll = value.GetProperty("OuterRoll").GetBoolean(),
        LeftHanded = value.GetProperty("LeftHanded").GetBoolean(),
        ChordsOnOtherHand = value.GetProperty("ChordsOnOtherHand").GetBoolean(),
        Style = Text(value, "Style") switch
        {
            "TimeReset" => FingeringStyle.TimeReset,
            "ReviewPreferenceV1" => FingeringStyle.ReviewPreferenceV1,
            _ => throw new InvalidDataException("Unknown fingering style."),
        },
        ReviewShortMinGap = Number(value, "ReviewShortMinGap", true),
        ReviewShortMaxGap = Number(value, "ReviewShortMaxGap", true),
        ReviewShortMinNotes = Integer(value, "ReviewShortMinNotes"),
        ReviewShortMaxNotes = Integer(value, "ReviewShortMaxNotes"),
        ReviewTimingTolerance = Number(value, "ReviewTimingTolerance", true),
    };
    Require(result.ResetWindow > 0, "ResetWindow must be positive.");
    Require(result.ReviewShortMinNotes >= 1 && result.ReviewShortMaxNotes >= result.ReviewShortMinNotes
        && result.ReviewShortMaxGap >= result.ReviewShortMinGap, "Invalid short phrase parameters.");
    return result;
}
static List<TileHit> ReadHits(JsonElement values)
{
    var result = new List<TileHit>();
    var tiles = new HashSet<int>();
    double previous = double.NegativeInfinity;
    foreach (var value in values.EnumerateArray())
    {
        var tile = Integer(value, "tileIndex");
        var time = Number(value, "time");
        Require(tile >= 0 && tiles.Add(tile) && time >= previous, "sourceHits must have unique tiles and nondecreasing times.");
        var angle = value.TryGetProperty("angle", out var angleValue) && angleValue.ValueKind != JsonValueKind.Null
            ? Number(value, "angle", true) : double.NaN;
        result.Add(new TileHit(tile, time, Number(value, "holdSeconds", true), angle));
        previous = time;
    }
    Require(result.Count > 0, "sourceHits is empty; a raw hit snapshot is required.");
    return result;
}
static SortedDictionary<int, double> ReadLanes(JsonElement values)
{
    var result = new SortedDictionary<int, double>();
    foreach (var value in values.EnumerateArray())
    {
        var lane = Integer(value, "lane");
        Require(lane is >= 0 and < 4 && !result.ContainsKey(lane), "Invalid or duplicate lane.");
        result.Add(lane, Number(value, "holdSeconds", true));
    }
    Require(result.Count is >= 1 and <= 4, "Every note must contain 1–4 lanes.");
    return result;
}
static List<OriginalNote> ReadNotes(JsonElement values)
{
    var result = new List<OriginalNote>();
    var ids = new HashSet<string>(StringComparer.Ordinal);
    double previous = double.NegativeInfinity;
    foreach (var value in values.EnumerateArray())
    {
        var note = new OriginalNote(Text(value, "id"), Integer(value, "tileIndex"), Integer(value, "lastTileIndex"),
            Number(value, "time"), ReadLanes(value.GetProperty("lanes")));
        Require(note.TileIndex >= 0 && note.LastTileIndex >= note.TileIndex && note.Time >= previous && ids.Add(note.Id),
            "Invalid note identity, ordering or duplicate ID.");
        result.Add(note); previous = note.Time;
    }
    Require(result.Count > 0, "Original notes are empty.");
    return result;
}
static Dictionary<string, UserEdit> ReadEdits(JsonElement values, Dictionary<string, OriginalNote> byId)
{
    var result = new Dictionary<string, UserEdit>(StringComparer.Ordinal);
    foreach (var value in values.EnumerateArray())
    {
        var id = Text(value, "noteId");
        Require(byId.ContainsKey(id) && !result.ContainsKey(id), "Unknown or duplicate edited note ID: " + id);
        var lanes = ReadLanes(value.GetProperty("lanes"));
        Require(SameHolds(lanes.Values, byId[id].Lanes.Values), "User edit changes note count or hold durations: " + id);
        result.Add(id, new UserEdit(lanes, value.GetProperty("reviewed").GetBoolean()));
    }
    return result;
}
static bool SameIdentity(PlannedNote actual, OriginalNote expected) =>
    actual.TileIndex == expected.TileIndex && actual.LastTileIndex == expected.LastTileIndex && actual.Time == expected.Time;
static bool SameLanes(SortedDictionary<int, double> first, SortedDictionary<int, double> second) => first.SequenceEqual(second);
static bool SameHolds(IEnumerable<double> first, IEnumerable<double> second) => first.Order().SequenceEqual(second.Order());
static PlannedNote WithLanes(PlannedNote note, SortedDictionary<int, double> lanes)
{
    var result = new PlannedNote(note.TileIndex, note.Time) { LastTileIndex = note.LastTileIndex };
    foreach (var lane in lanes) result.Lanes.Add(lane.Key, lane.Value);
    return result;
}
static int CountJacks(IReadOnlyList<PlannedNote> notes, double window)
{
    var count = 0;
    for (var i = 1; i < notes.Count; i++)
        if (notes[i].Time - notes[i - 1].Time < window - 1e-6
            && notes[i].Lanes.Keys.Any(lane => notes[i - 1].Lanes.ContainsKey(lane))) count++;
    return count;
}

sealed record OriginalNote(string Id, int TileIndex, int LastTileIndex, double Time, SortedDictionary<int, double> Lanes);
sealed record UserEdit(SortedDictionary<int, double> Lanes, bool Reviewed);
sealed class Counts
{
    public int TotalNotes { get; set; }
    public int ReviewEditRecords { get; set; }
    public int ReviewedNotes { get; set; }
    public int UserChangedNotes { get; set; }
    public int UserChangedReviewedNotes { get; set; }
    public int UnchangedEditRecords { get; set; }
    public int MatchedUserChanges { get; set; }
    public int UnmatchedUserChanges { get; set; }
    public int ChangedUneditedNotes { get; set; }
    public int ChangedReviewedUneditedNotes { get; set; }
    public int ChangedUnreviewedUneditedNotes { get; set; }
    public int TotalNewChanges { get; set; }
    public void Add(Counts other)
    {
        TotalNotes += other.TotalNotes;
        ReviewEditRecords += other.ReviewEditRecords;
        ReviewedNotes += other.ReviewedNotes;
        UserChangedNotes += other.UserChangedNotes;
        UserChangedReviewedNotes += other.UserChangedReviewedNotes;
        UnchangedEditRecords += other.UnchangedEditRecords;
        MatchedUserChanges += other.MatchedUserChanges;
        UnmatchedUserChanges += other.UnmatchedUserChanges;
        ChangedUneditedNotes += other.ChangedUneditedNotes;
        ChangedReviewedUneditedNotes += other.ChangedReviewedUneditedNotes;
        ChangedUnreviewedUneditedNotes += other.ChangedUnreviewedUneditedNotes;
        TotalNewChanges += other.TotalNewChanges;
    }
}
