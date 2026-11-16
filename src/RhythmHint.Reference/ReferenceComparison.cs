using System.Collections.Generic;
using System.Linq;
using RhythmHint.Core;

namespace RhythmHint.Reference;

/// <summary>自动排键和参考谱的对照结果。</summary>
public sealed class Comparison
{
    public Comparison(AlignResult align, int compared, int sameLanes, int sameHands, int chordAgree)
    {
        Align = align;
        Compared = compared;
        SameLanes = sameLanes;
        SameHands = sameHands;
        ChordAgree = chordAgree;
    }

    public AlignResult Align { get; }

    /// <summary>对上了参考谱、参与比较的音符数。</summary>
    public int Compared { get; }

    /// <summary>轨道完全相同。</summary>
    public int SameLanes { get; }

    /// <summary>用的手相同（左 / 右 / 双手）。手感最看重这一项。</summary>
    public int SameHands { get; }

    /// <summary>单押 / 双押判断一致。</summary>
    public int ChordAgree { get; }
}

/// <summary>
/// 开发用：把自动排键的结果和用户手排的参考谱逐个音符比较，衡量排键规则像不像用户的手法。
/// 只用来调规则的默认值，结果不会写回 Mod，Mod 也不读参考谱。
/// </summary>
public static class ReferenceComparison
{
    public static Comparison Compare(
        IReadOnlyList<TileHit> tiles,
        IReadOnlyList<PlannedNote> notes,
        IReadOnlyList<ChartGroup> reference,
        AlignOptions? options = null)
    {
        var align = ChartAligner.Align(tiles.Select(t => t.Time).ToArray(), reference.Select(g => g.Time).ToArray(), options);
        var groupOfTileIndex = new Dictionary<int, int>();
        for (var i = 0; i < tiles.Count; i++)
        {
            groupOfTileIndex[tiles[i].TileIndex] = align.GroupOfTile[i];
        }

        int compared = 0, sameLanes = 0, sameHands = 0, chordAgree = 0;
        foreach (var note in notes)
        {
            if (!groupOfTileIndex.TryGetValue(note.TileIndex, out var g) || g < 0)
            {
                continue;
            }

            var refLanes = Enumerable.Range(0, Lanes.Count).Where(l => reference[g].Holds[l].HasValue).ToList();
            var autoLanes = note.Lanes.Keys.ToList();
            compared++;
            sameLanes += refLanes.SequenceEqual(autoLanes) ? 1 : 0;
            sameHands += Hands(refLanes) == Hands(autoLanes) ? 1 : 0;
            chordAgree += (refLanes.Count > 1) == note.IsChord ? 1 : 0;
        }

        return new Comparison(align, compared, sameLanes, sameHands, chordAgree);
    }

    /// <summary>1 = 只用左手，2 = 只用右手，3 = 双手。</summary>
    private static int Hands(IEnumerable<int> lanes) => lanes.Aggregate(0, (acc, l) => acc | (1 << Lanes.HandOf(l)));
}
