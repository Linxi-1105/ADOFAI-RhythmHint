using System;

namespace RhythmHint.Core;

/// <summary>
/// 让画面上的歌曲时间每帧均匀地往前走（2026-11-19 用户：提示像是没跑满显示器帧数）。
/// 游戏的歌曲进度（scrConductor.songposition_minusi）在两次音频时钟之间按帧时间往前推，音频时钟一报新值
/// （Windows 上约 10–21 毫秒一次）就直接对齐过去，那一帧多走或少走最多一帧；拿它直接画，
/// 高刷新率显示器上每隔几帧就有一帧停住或多跳一下。这里按真实帧间隔 × 播放速度往前走，
/// 再把和游戏进度的差慢慢收回来（时间常数 <see cref="Settle"/>），平均下来和游戏进度一致。
/// 差太多（回检查点、跳转）、一帧太久（卡顿）、游戏没在走（暂停、进度停住）时直接用游戏进度。
/// </summary>
public sealed class SmoothSongClock
{
    /// <summary>和游戏进度差超过这么多（秒）就直接对齐。正常的跳动在一帧加几毫秒以内。</summary>
    public const double MaxDrift = 0.05;

    /// <summary>一帧超过这么久（秒，卡顿、切出去）就直接对齐，不从卡之前的位置往前推。</summary>
    public const double MaxFrame = 0.1;

    /// <summary>和游戏进度的差多快收回来（时间常数，秒）：大了摊得更平，小了跟得更紧。</summary>
    public const double Settle = 0.1;

    private bool _started;
    private double _value;
    private double _lastReal;
    private double _lastSource;
    private double _sourceMovedAt;

    /// <param name="songPosition">游戏这一帧的歌曲进度（秒）。</param>
    /// <param name="realTime">这一帧的真实时间（秒，不受游戏变速和暂停影响，如 Time.unscaledTimeAsDouble）。</param>
    /// <param name="rate">歌曲进度相对真实时间走多快（音乐的播放速度，正常是 1）。</param>
    /// <param name="running">游戏是不是在放（暂停时传 false）。</param>
    /// <returns>画面用的歌曲时间（秒）。</returns>
    public double Advance(double songPosition, double realTime, double rate, bool running)
    {
        var dt = realTime - _lastReal;
        if (_started && dt == 0)
        {
            return _value; // 同一帧再问一次
        }

        _lastReal = realTime;
        if (!_started || songPosition != _lastSource)
        {
            _lastSource = songPosition;
            _sourceMovedAt = realTime;
        }

        // 进度停住超过 MaxDrift 就当游戏停了（没报暂停也一样），否则会一直往前推、隔一会儿被拉回来一次
        var standing = realTime - _sourceMovedAt > MaxDrift;
        if (!_started || !running || standing || !(rate > 0) || dt < 0 || dt > MaxFrame)
        {
            _started = true;
            _value = songPosition;
            return _value;
        }

        var predicted = _value + (dt * rate);
        var drift = songPosition - predicted;
        _value = Math.Abs(drift) > MaxDrift ? songPosition : predicted + (drift * (1 - Math.Exp(-dt / Settle)));
        return _value;
    }
}
