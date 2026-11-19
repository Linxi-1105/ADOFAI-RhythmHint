using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>复现（2026-11-18 用户）：设置页按「创建桌面快捷方式」没有任何反馈。按钮的结果显示几秒、失败的更久，只在那排按钮下面。</summary>
public class ActionFeedbackTests
{
    [Fact]
    public void SuccessShowsUnderItsButtonsAndFadesAway()
    {
        var feedback = new ActionFeedback();
        feedback.Show("editor", new Message("已在桌面创建快捷方式。"), isError: false, now: 100);

        Assert.Equal(0, feedback.Opacity("editor", 100)); // 淡入的第一帧
        Assert.Equal(1, feedback.Opacity("editor", 101));
        Assert.Equal(0, feedback.Opacity("rules", 101)); // 别的地方不显示
        Assert.InRange(feedback.Opacity("editor", 100 + ActionFeedback.OkSeconds - 0.3), 0.01, 0.99); // 快到时间时淡出
        Assert.Equal(0, feedback.Opacity("editor", 100 + ActionFeedback.OkSeconds));
    }

    [Fact]
    public void ErrorsStayLongerToReadTheReason()
    {
        var feedback = new ActionFeedback();
        feedback.Show("editor", new Message("无法创建桌面快捷方式：{0}", "拒绝访问"), isError: true, now: 0);

        Assert.True(feedback.IsError);
        Assert.Equal(1, feedback.Opacity("editor", ActionFeedback.OkSeconds + 1));
        Assert.Equal(0, feedback.Opacity("editor", ActionFeedback.ErrorSeconds));
    }

    [Fact]
    public void TheLatestResultReplacesTheOldOne()
    {
        var feedback = new ActionFeedback();
        feedback.Show("editor", new Message("已打开手序文件夹。"), isError: false, now: 0);
        feedback.Show("rules", new Message("已按当前设置重新排键。"), isError: false, now: 1);

        Assert.Equal(0, feedback.Opacity("editor", 2));
        Assert.Equal(1, feedback.Opacity("rules", 2));
        Assert.Equal("已按当前设置重新排键。", feedback.Message.Source);
    }

    [Fact]
    public void NothingShownBeforeAnyAction() => Assert.Equal(0, new ActionFeedback().Opacity(string.Empty, 0));
}
