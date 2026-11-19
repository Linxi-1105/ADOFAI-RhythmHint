using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>复现：打完歌退回主菜单按 F8 还能导出上一首。</summary>
public class CurrentLevelTests
{
    [Fact]
    public void LeavingTheLevelForgetsIt()
    {
        var current = new CurrentLevel<string>();
        current.Show("上一首");

        current.Leave(); // 回到主菜单

        Assert.Null(current.Snapshot);
    }

    [Fact]
    public void AnotherLevelReplacesTheOldOne()
    {
        var current = new CurrentLevel<string>();
        current.Show("第一首");

        current.Show("第二首");

        Assert.Equal("第二首", current.Snapshot);
    }
}
