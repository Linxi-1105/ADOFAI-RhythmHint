using System.Collections.Generic;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 设置窗口的动效：按名字记住每个动画值，每帧向目标靠近。
/// IMGUI 一帧里会调用好几次（布局、绘制、鼠标事件），同一帧只前进一次；第一次用到时直接等于目标，刚出现的东西不从 0 动起来。
/// 用不缩放的时间：游戏暂停（timeScale 为 0）时窗口照常有动画。
/// </summary>
internal static class Motion
{
    /// <summary>
    /// 整体节奏：所有动画的时长乘它、弹簧频率除以它。2026-11-19 用户：动画有些慢，从 1 调到 0.6；要整体调快调慢只改这里。
    /// </summary>
    private const float Pace = 0.6f;

    private static readonly Dictionary<string, Track> Tracks = new();

    /// <summary>
    /// 指数逼近：<paramref name="duration"/> 秒走完约 95%，开头快、结尾慢（悬停、淡入淡出、位置移动）。
    /// </summary>
    public static float Ease(string id, float target, float duration = 0.16f)
    {
        var t = Get(id, target, out var dt);
        if (dt > 0)
        {
            t.Value += (target - t.Value) * (1 - Mathf.Exp(-3f * dt / Mathf.Max(0.001f, duration * Pace)));
            if (Mathf.Abs(target - t.Value) < 0.0005f)
            {
                t.Value = target;
            }
        }

        return t.Value;
    }

    /// <summary>
    /// 弹簧：到位时稍微冲过头再回来（导航的选中条、开关圆点、分段选择的滑块）。frequency 越大越快，damping 越小越弹。
    /// </summary>
    public static float Spring(string id, float target, float frequency = 3.4f, float damping = 0.68f)
    {
        var t = Get(id, target, out var dt);
        if (dt <= 0)
        {
            return t.Value;
        }

        // 分小步积分：帧率低（一帧 50 毫秒）时也不发散
        var omega = 2 * Mathf.PI * frequency / Pace;
        var steps = Mathf.CeilToInt(dt / 0.008f);
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            var accel = (-2 * damping * omega * t.Velocity) - (omega * omega * (t.Value - target));
            t.Velocity += accel * h;
            t.Value += t.Velocity * h;
        }

        if (Mathf.Abs(target - t.Value) < 0.01f && Mathf.Abs(t.Velocity) < 0.05f)
        {
            t.Value = target;
            t.Velocity = 0;
        }

        return t.Value;
    }

    /// <summary>直接设成某个值（之后从这里开始动），比如换页时把内容的透明度设回 0。</summary>
    public static void Set(string id, float value)
    {
        var t = Get(id, value, out _);
        t.Value = value;
        t.Velocity = 0;
    }

    /// <summary>忘掉一个动画值：下次用到时直接等于目标，不从旧值动过去（后台预排页面以后，导航选中条不该从预排的页滑回来）。</summary>
    public static void Forget(string id) => Tracks.Remove(id);

    private static Track Get(string id, float initial, out float dt)
    {
        var frame = Time.frameCount;
        if (!Tracks.TryGetValue(id, out var t))
        {
            t = new Track { Value = initial, Frame = frame };
            Tracks[id] = t;
            dt = 0;
            return t;
        }

        // 一帧最多走 50 毫秒：卡顿或很久没画（换到别的页）回来时不会一下跳完
        dt = frame == t.Frame ? 0 : Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        t.Frame = frame;
        return t;
    }

    private sealed class Track
    {
        public float Value;
        public float Velocity;
        public int Frame;
    }
}
