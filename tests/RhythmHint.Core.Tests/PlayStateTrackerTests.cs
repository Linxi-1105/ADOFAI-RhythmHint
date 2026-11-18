using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 关卡编辑器里一次播放的状态是不是这次播放产生的：编辑器不清掉上一次留下的通关 / 失败，
/// 只有这次播放开始后状态变过，现在的状态才当真。
/// </summary>
public class PlayStateTrackerTests
{
    private enum S
    {
        None,
        Start,
        PlayerControl,
        Fail,
        Won,
    }

    [Fact]
    public void DeathDuringThisPlayCounts()
    {
        var t = new PlayStateTracker<S>();

        Assert.False(t.Update(playing: true, S.Start));
        Assert.True(t.Update(playing: true, S.Fail));
    }

    [Fact]
    public void StateLeftOverFromLastPlayDoesNotCount()
    {
        // 上一次通关留下的 Won，这次播放一直没变：不能一开始就当成本关结束
        var t = new PlayStateTracker<S>();

        Assert.False(t.Update(playing: true, S.Won));
        Assert.False(t.Update(playing: true, S.Won));
    }

    [Fact]
    public void DeathAfterLeftoverDeathCountsOnceStateMoved()
    {
        // 上一次死了留下 Fail；这次播放先回到 Start，再死一次：第二次死要认出来
        var t = new PlayStateTracker<S>();

        Assert.False(t.Update(playing: true, S.Fail));
        t.Update(playing: true, S.Start);
        Assert.True(t.Update(playing: true, S.Fail));
    }

    [Fact]
    public void EachPlayStartsOver()
    {
        var t = new PlayStateTracker<S>();
        t.Update(playing: true, S.Start);
        Assert.True(t.Update(playing: true, S.Fail));

        // 回到编辑、再开始播放：上一次的 Fail 是旧的
        Assert.False(t.Update(playing: false, S.Fail));
        Assert.False(t.Update(playing: true, S.Fail));
    }

    [Fact]
    public void ResetForgetsThePlay()
    {
        // 离开关卡（Reset）后再回来播放，按新的一次算
        var t = new PlayStateTracker<S>();
        t.Update(playing: true, S.Start);
        t.Update(playing: true, S.Fail);

        t.Reset();

        Assert.False(t.Update(playing: true, S.Fail));
    }
}
