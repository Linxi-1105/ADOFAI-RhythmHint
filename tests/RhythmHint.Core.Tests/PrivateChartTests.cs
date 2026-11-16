using RhythmHint.Reference;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 用作者本机的真实谱面做检查。谱面不进仓库：设置环境变量 RHYTHMHINT_PRIVATE_PEZ 指向
/// 「新第二版.pez」才会运行，否则明确跳过。
/// </summary>
public class PrivateChartTests
{
    [Fact]
    public void CeTxPhiChart_ParsesIntoExpectedGroups()
    {
        var path = Environment.GetEnvironmentVariable("RHYTHMHINT_PRIVATE_PEZ");
        Assert.SkipWhen(string.IsNullOrEmpty(path) || !File.Exists(path), "需要私有谱面：未设置 RHYTHMHINT_PRIVATE_PEZ");

        var chart = RpeChart.Load(path!);
        var groups = chart.ToGroups();

        Assert.Equal(1138, chart.Notes.Count);
        Assert.Equal(817, groups.Count);
        // 301 个双押，加上 10 个「同轨 hold + drag + 另一轨」合并后也是两轨
        Assert.Equal(311, groups.Count(g => g.LaneCount == 2));
        Assert.Equal(0, groups.Count(g => g.LaneCount > 2)); // 同轨叠放的 hold + drag 已合并
    }
}
