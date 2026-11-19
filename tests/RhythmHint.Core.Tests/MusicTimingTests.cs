using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 编辑器里音乐和谱面对齐：偏移 = 关卡 offset − 起始一拍 × (1 + 砖块时间里算进去的倒计时拍数)。
/// 用 2026-11-18 用户反馈音画不同步时的真实数据守着（游戏里 4 拍倒计时：砖块时间里多了 3 拍）。
/// </summary>
public class MusicTimingTests
{
    [Fact]
    public void OfficialLevel_ArxLibertas()
    {
        // addoffset 1.5、一拍 0.375；起音对齐实测偏移 0.000（原来按「offset − 一拍」算成 1.125，差 3 拍）
        Assert.Equal(0, MusicTiming.Offset(1.5, 0.375, countdownBeatsInTileTimes: 3), 6);
    }

    [Fact]
    public void WorkshopLevel_Deleted_FirstNoteLandsWhereTheMusicStarts()
    {
        // 工坊 3673206726：offset 1618 毫秒、一拍 0.7947 秒，游戏里第 1 块砖在 3.1788 秒；音乐 1.63 秒开始响
        var offset = MusicTiming.Offset(1.618, 0.794702, countdownBeatsInTileTimes: 3);

        Assert.Equal(-1.5608, offset, 4);
        Assert.Equal(1.618, 3.1788 + offset, 3);
    }

    [Fact]
    public void LevelEditorPlaythrough_MatchesTheGamesOwnMeasurement()
    {
        // 关卡编辑器里试玩时游戏实测偏移 1.747 秒（音乐位置 − 谱面时间）；关卡 offset 3.106 秒、一拍 0.3397 秒
        Assert.Equal(1.747, MusicTiming.Offset(3.106, 0.3397, countdownBeatsInTileTimes: 3), 3);
    }

    [Fact]
    public void OfflineTiming_Avantgarde()
    {
        // 离线读谱不算倒计时：工坊 3296277169（50 BPM，第一步转角 52.5°，第 1 块砖不在一拍上）按起音对齐实测就是「offset − 一拍」
        Assert.Equal(0.047, MusicTiming.Offset(1.247, 1.2, countdownBeatsInTileTimes: 0), 6);
    }
}
