using System;

namespace RhythmHint.Core;

/// <summary>
/// 入场 / 出场动画的进度：0 = 完全不在画面上，1 = 完全显示。
/// 入场、出场都是缓出（一开始就走得快，快到位时慢下来）：按 Esc 暂停时面板马上开始滑走，不在原地停一下。
/// 中途反向（刚暂停又马上继续）时从当前位置重新起一段，所需时间按剩下的距离折算，不跳。
/// </summary>
public sealed class PresenceAnimator
{
    private readonly double _seconds;
    private double _value;
    private double _from;
    private double _target;
    private double _progress = 1; // 当前这一段走了多少，0..1

    /// <param name="seconds">从 0 走到 1（或反过来）要多久。</param>
    public PresenceAnimator(double seconds = 0.45)
    {
        _seconds = Math.Max(0.01, seconds);
    }

    /// <summary>缓动后的进度，0..1。</summary>
    public double Value => _value;

    public bool IsVisible => _value > 0;

    /// <param name="show">这一帧是否应该显示。</param>
    /// <param name="deltaSeconds">距上一帧的真实时间（不受游戏变速影响）。</param>
    public double Update(bool show, double deltaSeconds)
    {
        var target = show ? 1.0 : 0.0;
        if (target != _target)
        {
            _from = _value;
            _target = target;
            _progress = 0;
        }

        if (_progress < 1)
        {
            var duration = _seconds * Math.Abs(_target - _from);
            _progress = duration <= 0 ? 1 : Math.Min(1, _progress + (Math.Max(0, deltaSeconds) / duration));
            var eased = 1 - Math.Pow(1 - _progress, 3);
            _value = _progress >= 1 ? _target : _from + ((_target - _from) * eased);
        }

        return _value;
    }
}
