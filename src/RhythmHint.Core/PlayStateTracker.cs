using System.Collections.Generic;

namespace RhythmHint.Core;

/// <summary>
/// 关卡编辑器里一次播放的游戏状态是不是这次播放产生的。编辑器不会清掉上一次留下的「通关 / 失败」，
/// 所以记下每次开始播放时的状态，这次播放中状态变过一次以后，现在的状态才当真
/// （否则第二次播放一开始就被当成本关结束；上一次死了留下失败、这次先回到开始再死，也要认出这一次的死亡）。
/// </summary>
/// <typeparam name="T">游戏的状态（Mod 里是 States）。</typeparam>
public sealed class PlayStateTracker<T>
    where T : struct
{
    private bool _wasPlaying;
    private bool _changed;
    private T _baseline;

    /// <summary>每帧调用。</summary>
    /// <param name="playing">编辑器在播放中。</param>
    /// <param name="state">现在的游戏状态。</param>
    /// <returns>现在的状态是不是这次播放中变出来的；没在播放时为 false。</returns>
    public bool Update(bool playing, T state)
    {
        if (playing && !_wasPlaying)
        {
            _baseline = state;
            _changed = false;
        }

        _wasPlaying = playing;
        if (playing && !EqualityComparer<T>.Default.Equals(state, _baseline))
        {
            _changed = true;
        }

        return playing && _changed;
    }

    /// <summary>离开关卡时调用：下次播放按新的一次算。</summary>
    public void Reset() => _wasPlaying = false;
}
