using System;
using System.Collections.Generic;
using System.Linq;

namespace RhythmHint.Core;

/// <summary>专属手序套到当前关卡上的结果。</summary>
public sealed class FingeringMatch
{
    internal FingeringMatch(List<PlannedNote> notes, List<bool> fromAuto, int matchedHits, int totalHits)
    {
        Notes = notes;
        FromAuto = fromAuto;
        MatchedHits = matchedHits;
        TotalHits = totalHits;
        for (var i = 0; i < fromAuto.Count; i++)
        {
            if (!fromAuto[i])
            {
                continue;
            }

            FallbackNotes++;
            if (i == 0 || !fromAuto[i - 1])
            {
                FallbackSections++;
            }
        }
    }

    /// <summary>最终要画的音符：对上的用专属手序，对不上的用自动排键。每块砖正好属于一个音符。</summary>
    public List<PlannedNote> Notes { get; }

    /// <summary>和 <see cref="Notes"/> 一一对应：这个音符是不是用自动排键补上的。</summary>
    public List<bool> FromAuto { get; }

    /// <summary>用上专属手序的砖块数。</summary>
    public int MatchedHits { get; }

    public int TotalHits { get; }

    /// <summary>用自动排键补上的音符数、连成几段。</summary>
    public int FallbackNotes { get; }

    public int FallbackSections { get; }

    /// <summary>本关有多少比例的砖用上了专属手序。</summary>
    public double Ratio => TotalHits == 0 ? 0 : (double)MatchedHits / TotalHits;
}

/// <summary>文件夹里的一份手序和它的文件名。</summary>
public sealed record FingeringCandidate(string Name, CustomFingering File);

/// <summary>认出这一关用哪份手序的方式。</summary>
public enum FingeringReason
{
    /// <summary>没有像这一关的手序，用自动排键。</summary>
    None,

    /// <summary>谱面指纹完全一样：就是做手序时的那一版。</summary>
    Exact,

    /// <summary>谱面更新过，按节奏对齐跟上（对不上的地方用自动排键）。</summary>
    Followed,

    /// <summary>玩家之前选过这一关用哪份（或者选了都不用）。</summary>
    Chosen,

    /// <summary>有几份手序都像这一关，或者像但不够确定：不猜，等玩家选。</summary>
    Unsure,
}

/// <summary>从几份手序里给当前关卡挑出来的结果。</summary>
public sealed class FingeringChoice
{
    public FingeringReason Reason { get; internal set; }

    /// <summary>用上的那份；没有能用的为 null。</summary>
    public FingeringCandidate? Used { get; internal set; }

    public FingeringMatch? Match { get; internal set; }

    /// <summary>本关的谱面指纹（玩家选了以后按它记住）。</summary>
    public string Chart { get; internal set; } = string.Empty;

    /// <summary>像这一关的手序和对上比例，从高到低（Unsure 时给玩家选）。</summary>
    public List<(FingeringCandidate Candidate, int MatchedHits, int TotalHits)> Candidates { get; } = new();

    /// <summary>像这一关、但键数和 Mod 不同的手序（以后的六键、八键）：现在不套用，状态里说明。</summary>
    public List<FingeringCandidate> OtherKeys { get; } = new();

    /// <summary>当成一份的重复文件（日志用）：内容一样的几份，或同一位作者给这一版谱面存过好几次。</summary>
    public List<FingeringMerge> Merged { get; } = new();
}

/// <summary>重复的手序文件没参与挑选：<paramref name="Dropped"/> 和 <paramref name="Kept"/> 内容一样（<paramref name="SameContent"/>），或是同一位作者较早存的。</summary>
public sealed record FingeringMerge(string Dropped, string Kept, bool SameContent);

/// <summary>
/// 把专属手序对到当前关卡上（手序跟着歌走）：关卡更新后砖块可能增删、个别砖改了角度、整体偏移变了，
/// 这里按节奏把两边的砖一一对齐，对上的砖用专属手序，对不上的退回自动排键。
/// <para>
/// 比的是每块砖「和前一块砖的间隔、转角、有没有长按」，不看绝对时间和砖号：改偏移不影响，
/// 某块砖改了角度只影响那一块，中间加删的砖只影响那几块。对齐用最长公共子序列（限制在对角线附近的一条带里算，
/// 四千块砖约几百万次比较）。
/// </para>
/// </summary>
public static class FingeringMatcher
{
    /// <summary>
    /// 谱面指纹对不上时，对上这么多才自动用（谱面小改后照样跟上）。不看曲名：同一首歌常有好几个谱师的版本，
    /// 用户创意工坊里两首 deleted 互相只对上 2%–6%，同一张谱改偏移、改一块砖后仍有 99.7% 以上。
    /// </summary>
    public const double MinRatio = 0.8;

    /// <summary>第一名要比第二名多对上这么多才自动用，否则不猜、让玩家选。</summary>
    public const double MinLead = 0.1;

    /// <summary>对上这么多以上（但不够自动用）才列给玩家选，再低就当不是这一关。</summary>
    public const double MinCandidate = 0.3;

    /// <summary>
    /// 只认连续对上这么多块砖的整段。节奏只有几种取值，两首无关的歌零散地也能「对上」一半以上（最长公共子序列的性质），
    /// 真正同一首歌更新前后是一长段一长段地对上。
    /// </summary>
    public const int MinRun = 8;

    /// <summary>对齐带宽至少这么多块砖（中间整段插入、删掉时路径会偏离对角线）。</summary>
    private const int MinBand = 300;

    /// <summary>带里最多算这么多格，再多就把带收窄（极长的关卡）。</summary>
    private const long MaxCells = 30_000_000;

    /// <summary>当前关卡每块砖对应文件里的第几块砖，对不上为 -1。</summary>
    public static int[] Align(IReadOnlyList<TileHit> current, IReadOnlyList<TileHit> file) => Align(new Rhythm(current), new Rhythm(file));

    /// <summary>
    /// 本关最多能有多少块砖对上这份文件的砖（上限：<see cref="Apply"/> 的 MatchedHits 不会超过它）。很便宜（几毫秒），
    /// 用来在对齐之前跳过不可能对上的文件（BPM、节奏不一样的歌）。做法见 Rhythm.Bound。
    /// </summary>
    public static int MatchBound(IReadOnlyList<TileHit> current, IReadOnlyList<TileHit> file) => Rhythm.Bound(new Rhythm(current), new Rhythm(file), MinRun);

    private static int[] Align(Rhythm current, Rhythm file)
    {
        int n = current.Count, m = file.Count;
        var map = new int[n];
        for (var i = 0; i < n; i++)
        {
            map[i] = -1;
        }

        if (n == 0 || m == 0)
        {
            return map;
        }

        var band = Math.Max(MinBand, Math.Abs(n - m) + 150);
        if ((long)(n + 1) * ((2 * band) + 1) > MaxCells)
        {
            band = Math.Max(1, (int)((MaxCells / (n + 1)) - 1) / 2);
        }

        var width = (2 * band) + 1;
        var dir = new byte[(long)(n + 1) * width]; // 1 对上，2 跳过当前关卡的砖，3 跳过文件里的砖
        var prev = new int[width];
        var cur = new int[width];
        const int Unreachable = int.MinValue / 2;

        int Start(int i) => (int)Math.Round((double)i * m / n) - band;

        // 第 0 行：还没用当前关卡的砖，最长公共子序列是 0
        var s0 = Start(0);
        for (var k = 0; k < width; k++)
        {
            var j = s0 + k;
            prev[k] = j >= 0 && j <= m ? 0 : Unreachable;
        }

        for (var i = 1; i <= n; i++)
        {
            // 上一行同一列在 prev 里的位置 = k + shift（每一行的带子起点随对角线挪动）
            int s = Start(i), shift = s - Start(i - 1);
            var row = (long)i * width;
            var left = Unreachable;
            bool hold = current.Hold[i - 1], first = i == 1;
            double angle = current.Angle[i - 1], gap = current.Gap[i - 1];
            for (var k = 0; k < width; k++)
            {
                var j = s + k;
                if (j < 0 || j > m)
                {
                    cur[k] = left = Unreachable;
                    continue;
                }

                if (j == 0)
                {
                    cur[k] = left = 0;
                    dir[row + k] = 2;
                    continue;
                }

                var u = k + shift;
                var up = u >= 0 && u < width ? prev[u] : Unreachable;
                var diag = u >= 1 && u <= width ? prev[u - 1] : Unreachable;
                var best = up;
                byte d = 2;
                if (diag != Unreachable && diag + 1 >= best && Rhythm.Same(hold, angle, gap, first, file, j - 1))
                {
                    best = diag + 1;
                    d = 1;
                }

                if (left > best)
                {
                    best = left;
                    d = 3;
                }

                cur[k] = left = best;
                dir[row + k] = best == Unreachable ? (byte)0 : d;
            }

            (prev, cur) = (cur, prev);
        }

        // 从右下角往回走，记下对上的砖
        int x = n, y = m;
        while (x > 0 && y > 0)
        {
            var k = y - Start(x);
            if (k < 0 || k >= width)
            {
                break;
            }

            switch (dir[((long)x * width) + k])
            {
                case 1:
                    map[x - 1] = y - 1;
                    x--;
                    y--;
                    break;
                case 2:
                    x--;
                    break;
                case 3:
                    y--;
                    break;
                default:
                    return map;
            }
        }

        return map;
    }

    /// <summary>
    /// 把一份手序套到当前关卡：对上的砖用专属手序的键（作者和玩家惯用手不同时左右镜像，长按按比例换成本关的时长），
    /// 其余用 <paramref name="auto"/>（同一关的自动排键）。一个自动音符只有一部分砖对上时，压在它上面的专属音符也退回自动，
    /// 保证每块砖正好属于一个音符。键数不同（以后的六键、八键）时整首用自动。
    /// </summary>
    public static FingeringMatch Apply(CustomFingering file, IReadOnlyList<TileHit> hits, IReadOnlyList<PlannedNote> auto, bool leftHanded) =>
        Apply(file, hits, auto, leftHanded, new Rhythm(hits), null);

    private static FingeringMatch Apply(CustomFingering file, IReadOnlyList<TileHit> hits, IReadOnlyList<PlannedNote> auto, bool leftHanded,
        Rhythm rhythm, Rhythm? fileRhythm)
    {
        var n = hits.Count;
        var curIndex = IndexByTile(hits);
        var autoRange = new List<(int First, int Last)>(auto.Count);
        var autoOf = new int[n];
        for (var a = 0; a < auto.Count; a++)
        {
            var range = (curIndex[auto[a].TileIndex], curIndex[auto[a].LastTileIndex]);
            autoRange.Add(range);
            for (var i = range.Item1; i <= range.Item2; i++)
            {
                autoOf[i] = a;
            }
        }

        var customs = new List<(int First, int Last, PlannedNote Source, int FileFirst)>();
        var claimedBy = new int[n];
        for (var i = 0; i < n; i++)
        {
            claimedBy[i] = -1;
        }

        if (file.Keys == Lanes.Count)
        {
            var map = KeepLongRuns(Align(rhythm, fileRhythm ?? new Rhythm(file.Hits)));
            var toCur = new int[file.Hits.Count];
            for (var j = 0; j < toCur.Length; j++)
            {
                toCur[j] = -1;
            }

            for (var i = 0; i < n; i++)
            {
                if (map[i] >= 0)
                {
                    toCur[map[i]] = i;
                }
            }

            var fileIndex = IndexByTile(file.Hits);
            foreach (var note in file.Notes)
            {
                int fa = fileIndex[note.TileIndex], fb = fileIndex[note.LastTileIndex];
                var first = toCur[fa];
                var ok = first >= 0;
                for (var f = fa + 1; ok && f <= fb; f++)
                {
                    ok = toCur[f] == first + (f - fa);
                }

                if (!ok)
                {
                    continue;
                }

                var c = customs.Count;
                customs.Add((first, first + (fb - fa), note, fa));
                for (var i = first; i <= first + (fb - fa); i++)
                {
                    claimedBy[i] = c;
                }
            }

            // 一个自动音符被认领了一部分：这几块砖的分组两边不一样，压在上面的专属音符退回自动，直到没有这种情况
            for (var changed = true; changed;)
            {
                changed = false;
                foreach (var (first, last) in autoRange)
                {
                    var claimed = 0;
                    for (var i = first; i <= last; i++)
                    {
                        claimed += claimedBy[i] >= 0 ? 1 : 0;
                    }

                    if (claimed == 0 || claimed == last - first + 1)
                    {
                        continue;
                    }

                    for (var i = first; i <= last; i++)
                    {
                        if (claimedBy[i] >= 0)
                        {
                            var (cf, cl, _, _) = customs[claimedBy[i]];
                            for (var h = cf; h <= cl; h++)
                            {
                                claimedBy[h] = -1;
                            }

                            changed = true;
                        }
                    }
                }
            }
        }

        var notes = new List<PlannedNote>(auto.Count);
        var fromAuto = new List<bool>(auto.Count);
        var matched = 0;
        var mirror = file.LeftHanded != leftHanded;
        for (var i = 0; i < n;)
        {
            if (claimedBy[i] >= 0)
            {
                var (first, last, source, fileFirst) = customs[claimedBy[i]];
                notes.Add(Place(source, file, fileFirst, hits, first, last, mirror));
                fromAuto.Add(false);
                matched += last - first + 1;
                i = last + 1;
            }
            else
            {
                var a = autoOf[i];
                notes.Add(auto[a].Clone());
                fromAuto.Add(true);
                i = autoRange[a].Last + 1;
            }
        }

        return new FingeringMatch(notes, fromAuto, matched, n);
    }

    /// <summary>
    /// 从文件夹里的几份手序里挑这一关用哪份，名字不参与判断，只看谱面本身：
    /// <list type="number">
    /// <item>玩家选过（<paramref name="chosen"/>：文件名，空串表示选了都不用）就照玩家的；文件不在了当没选过</item>
    /// <item>谱面指纹完全一样：直接用（同一张谱，换文件名、换电脑都一样）</item>
    /// <item>否则按节奏对齐：对上不低于 <see cref="MinRatio"/> 且比第二名多 <see cref="MinLead"/> 才自动用</item>
    /// <item>有像的但不够确定（不低于 <see cref="MinCandidate"/>）：不猜，列出来让玩家选</item>
    /// </list>
    /// 挑之前先把重复的当成一份（2026-11-19 评审 A：同一张谱两份就永远「不确定」）：内容一样的（浏览器重复下载出的「(1)」）只留一份；
    /// 指纹完全一样的几份里，同一位作者的只留 updatedAt 最新的。键数和 Mod 不同的不套用，像这一关的记进 <see cref="FingeringChoice.OtherKeys"/>。
    /// </summary>
    /// <param name="chosen">玩家给这个谱面指纹选过的文件名；没选过为 null。</param>
    public static FingeringChoice Choose(IReadOnlyList<FingeringCandidate> candidates, IReadOnlyList<TileHit> hits,
        IReadOnlyList<PlannedNote> auto, bool leftHanded, string? chosen = null)
    {
        var chart = CustomFingering.ChartPrint(hits);
        var choice = new FingeringChoice { Chart = chart };
        var rhythm = new Rhythm(hits);
        var usable = new List<FingeringCandidate>();
        foreach (var c in candidates)
        {
            if (c.File.Keys == Lanes.Count)
            {
                usable.Add(c);
            }
            else if (LooksLike(c.File, chart, rhythm))
            {
                choice.OtherKeys.Add(c);
            }
        }

        if (chosen != null)
        {
            if (chosen.Length == 0)
            {
                choice.Reason = FingeringReason.Chosen;
                return choice;
            }

            var picked = usable.FirstOrDefault(c => string.Equals(c.Name, chosen, StringComparison.OrdinalIgnoreCase));
            if (picked != null)
            {
                return Use(choice, picked, Apply(picked.File, hits, auto, leftHanded, rhythm, null), FingeringReason.Chosen);
            }
        }

        usable = WithoutCopies(usable, choice.Merged);
        var exact = NewestOfEachAuthor(usable.FindAll(c => c.File.Charts.Contains(chart)), choice.Merged);
        if (exact.Count == 1)
        {
            return Use(choice, exact[0], Apply(exact[0].File, hits, auto, leftHanded, rhythm, null), FingeringReason.Exact);
        }

        return Score(choice, exact.Count > 1 ? exact : usable, exact.Count > 1, hits, auto, leftHanded, rhythm);
    }

    /// <summary>按节奏对齐给候选打分；不可能对上 <see cref="MinCandidate"/> 的（砖数差太多、节奏片段对不上）先跳过，不做对齐。</summary>
    private static FingeringChoice Score(FingeringChoice choice, List<FingeringCandidate> pool, bool allExact, IReadOnlyList<TileHit> hits,
        IReadOnlyList<PlannedNote> auto, bool leftHanded, Rhythm rhythm)
    {
        var scored = new List<(FingeringCandidate Candidate, FingeringMatch Match)>();
        foreach (var c in pool)
        {
            var fileRhythm = CouldMatch(c.File, rhythm);
            if (fileRhythm != null)
            {
                var match = Apply(c.File, hits, auto, leftHanded, rhythm, fileRhythm);
                if (match.Ratio >= MinCandidate)
                {
                    scored.Add((c, match));
                }
            }
        }

        scored = scored.OrderByDescending(x => x.Match.Ratio).ThenByDescending(x => x.Candidate.File.UpdatedAt).ToList();
        foreach (var x in scored)
        {
            choice.Candidates.Add((x.Candidate, x.Match.MatchedHits, x.Match.TotalHits));
        }

        if (scored.Count == 0)
        {
            choice.Reason = FingeringReason.None;
            return choice;
        }

        var best = scored[0];
        var lead = scored.Count > 1 ? best.Match.Ratio - scored[1].Match.Ratio : 1;
        if (!allExact && best.Match.Ratio >= MinRatio && lead >= MinLead)
        {
            return Use(choice, best.Candidate, best.Match, FingeringReason.Followed);
        }

        choice.Reason = FingeringReason.Unsure;
        return choice;
    }

    /// <summary>
    /// 这份文件有没有可能对上本关至少 <see cref="MinCandidate"/>：砖数差太多的、节奏片段对不上的（<see cref="MatchBound"/>）不可能。
    /// 只是跳过注定对不上的，不改结果。有可能时返回文件的节奏（对齐接着用），不可能为 null。
    /// </summary>
    private static Rhythm? CouldMatch(CustomFingering file, Rhythm rhythm)
    {
        var n = rhythm.Count;
        if (Math.Min(file.Hits.Count, n) < MinCandidate * n)
        {
            return null;
        }

        var fileRhythm = new Rhythm(file.Hits);
        return Rhythm.Bound(rhythm, fileRhythm, MinRun) >= MinCandidate * n ? fileRhythm : null;
    }

    /// <summary>键数不同的文件像不像这一关：谱面指纹一样，或者按节奏对齐能对上 <see cref="MinCandidate"/> 以上。</summary>
    private static bool LooksLike(CustomFingering file, string chart, Rhythm rhythm)
    {
        if (file.Charts.Contains(chart))
        {
            return true;
        }

        var fileRhythm = CouldMatch(file, rhythm);
        return fileRhythm != null && KeepLongRuns(Align(rhythm, fileRhythm)).Count(i => i >= 0) >= MinCandidate * rhythm.Count;
    }

    /// <summary>
    /// 内容一样的几份（键数、惯用手、砖块、每个音符的键和长按都一样，套到关卡上结果一定一样）只留一份：updatedAt 新的，再是名字短的
    /// （「Song.json」而不是浏览器重复下载出的「Song (1).json」）。保留原来的顺序。
    /// </summary>
    private static List<FingeringCandidate> WithoutCopies(List<FingeringCandidate> candidates, List<FingeringMerge> merged)
    {
        var kept = new List<FingeringCandidate>();
        foreach (var c in candidates.OrderByDescending(c => c.File.UpdatedAt).ThenBy(c => c.Name.Length).ThenBy(c => c.Name, StringComparer.Ordinal))
        {
            var same = kept.Find(k => SamePlay(k.File, c.File));
            if (same == null)
            {
                kept.Add(c);
            }
            else
            {
                merged.Add(new FingeringMerge(c.Name, same.Name, SameContent: true));
            }
        }

        return candidates.FindAll(kept.Contains);
    }

    /// <summary>
    /// 指纹完全一样的几份里，同一位作者（去掉标签、首尾空白，不分大小写）的只留 updatedAt 最新的那份。
    /// 没写作者的不算同一位；最新的有好几份一样新时分不出来，这一位的都留着（照旧让玩家选）。
    /// </summary>
    private static List<FingeringCandidate> NewestOfEachAuthor(List<FingeringCandidate> exact, List<FingeringMerge> merged)
    {
        var result = new List<FingeringCandidate>(exact);
        foreach (var group in exact.GroupBy(c => CustomFingering.PlainText(c.File.Author).ToLowerInvariant()).Where(g => g.Key.Length > 0))
        {
            var newest = group.Max(c => c.File.UpdatedAt);
            var winners = group.Where(c => c.File.UpdatedAt == newest).ToList();
            if (winners.Count != 1)
            {
                continue;
            }

            foreach (var older in group.Where(c => !ReferenceEquals(c, winners[0])))
            {
                result.Remove(older);
                merged.Add(new FingeringMerge(older.Name, winners[0].Name, SameContent: false));
            }
        }

        return result;
    }

    /// <summary>套到关卡上结果一定一样：键数、惯用手、每块砖（砖号、时间、长按、转角）、每个音符的砖和键、长按都一样。</summary>
    private static bool SamePlay(CustomFingering a, CustomFingering b)
    {
        if (a.Keys != b.Keys || a.LeftHanded != b.LeftHanded || a.Hits.Count != b.Hits.Count || a.Notes.Count != b.Notes.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Hits.Count; i++)
        {
            if (!a.Hits[i].Equals(b.Hits[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < a.Notes.Count; i++)
        {
            PlannedNote x = a.Notes[i], y = b.Notes[i];
            if (x.TileIndex != y.TileIndex || x.LastTileIndex != y.LastTileIndex || !x.Lanes.SequenceEqual(y.Lanes))
            {
                return false;
            }
        }

        return true;
    }

    private static FingeringChoice Use(FingeringChoice choice, FingeringCandidate candidate, FingeringMatch match, FingeringReason reason)
    {
        choice.Used = candidate;
        choice.Match = match;
        choice.Reason = reason;
        return choice;
    }

    /// <summary>去掉短于 <see cref="MinRun"/> 的零散对应（两边都连续的才算一段）。</summary>
    private static int[] KeepLongRuns(int[] map)
    {
        for (var start = 0; start < map.Length;)
        {
            if (map[start] < 0)
            {
                start++;
                continue;
            }

            var end = start;
            while (end + 1 < map.Length && map[end + 1] == map[end] + 1)
            {
                end++;
            }

            if (end - start + 1 < MinRun)
            {
                for (var i = start; i <= end; i++)
                {
                    map[i] = -1;
                }
            }

            start = end + 1;
        }

        return map;
    }

    private static PlannedNote Place(PlannedNote source, CustomFingering file, int fileFirst, IReadOnlyList<TileHit> hits, int first, int last, bool mirror)
    {
        var note = new PlannedNote(hits[first].TileIndex, hits[first].Time) { LastTileIndex = hits[last].TileIndex };

        // 长按：文件里这几块砖的长按总时长换算到本关（谱面变速后长按跟着变）
        double fileHold = 0, curHold = 0;
        for (var f = fileFirst; f < file.Hits.Count && file.Hits[f].TileIndex <= source.LastTileIndex; f++)
        {
            fileHold += file.Hits[f].HoldSeconds;
        }

        for (var i = first; i <= last; i++)
        {
            curHold += hits[i].HoldSeconds;
        }

        foreach (var kv in source.Lanes)
        {
            var lane = mirror ? Lanes.Count - 1 - kv.Key : kv.Key;
            note.Lanes[lane] = kv.Value > 0 && fileHold > 0 ? kv.Value * curHold / fileHold : 0;
        }

        return note;
    }

    private static Dictionary<int, int> IndexByTile(IReadOnlyList<TileHit> hits)
    {
        var index = new Dictionary<int, int>(hits.Count);
        for (var i = 0; i < hits.Count; i++)
        {
            index[hits[i].TileIndex] = i;
        }

        return index;
    }
}
