using System;

namespace RhythmHint.Core;

/// <summary>
/// 设置窗口里按钮的结果（2026-11-18 用户：设置页面缺少反馈，比如按「创建桌面快捷方式」没反应）：
/// 按下后在那排按钮下面显示一行，成功的几秒后淡出，失败的留得久一些；只显示最近一次，换了地方的按钮就换地方显示。
/// 到时间自己消失，所以不会像以前那样回到主菜单还挂着上一首的结果（见 <see cref="CurrentLevel{T}"/> 的复现）。
/// 时间由调用方给（Mod 用 Time.unscaledTime），方便测试。
/// </summary>
public sealed class ActionFeedback
{
    /// <summary>成功的显示多久（秒）。</summary>
    public const double OkSeconds = 6;

    /// <summary>失败的显示多久（秒）：要留时间读原因。</summary>
    public const double ErrorSeconds = 15;

    public const double FadeInSeconds = 0.15;

    public const double FadeOutSeconds = 0.6;

    private double _shownAt = double.NegativeInfinity;
    private double _until = double.NegativeInfinity;

    public Message Message { get; private set; } = Message.Empty;

    public bool IsError { get; private set; }

    /// <summary>哪一排按钮的结果（设置窗口里按区域显示）。</summary>
    public string Area { get; private set; } = string.Empty;

    public void Show(string area, Message message, bool isError, double now)
    {
        Area = area;
        Message = message;
        IsError = isError;
        _shownAt = now;
        _until = now + (isError ? ErrorSeconds : OkSeconds);
    }

    /// <summary>这一区域现在显示多清楚：0 不显示，1 完全显示；刚出现时淡入，快到时间时淡出。</summary>
    public double Opacity(string area, double now)
    {
        if (area != Area || Message.Source.Length == 0 || now < _shownAt || now >= _until)
        {
            return 0;
        }

        return Math.Min(1, Math.Min((now - _shownAt) / FadeInSeconds, (_until - now) / FadeOutSeconds));
    }
}
