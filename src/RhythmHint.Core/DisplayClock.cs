namespace RhythmHint.Core;

/// <summary>
/// 画面用的时间：平时跟随歌曲进度；玩家失败的那一刻定住，失败画面期间音符不再流动。
/// 重开或回检查点（不再处于失败状态）后恢复跟随。
/// </summary>
public sealed class DisplayClock
{
    private double? _frozenAt;

    public double Advance(double songPosition, bool failed)
    {
        if (!failed)
        {
            _frozenAt = null;
            return songPosition;
        }

        _frozenAt ??= songPosition;
        return _frozenAt.Value;
    }
}
