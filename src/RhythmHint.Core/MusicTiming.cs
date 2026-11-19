namespace RhythmHint.Core;

/// <summary>
/// 手序编辑器里音乐和谱面怎么对齐：音乐文件里的位置 = 砖块时间 + 偏移，
/// 偏移 = 关卡 offset − 起始一拍 × (1 + 砖块时间里算进去的倒计时拍数)。
/// 依据（游戏 scrLevelMaker.CalculateFloorEntryTimes，只看了计算方式）：第 0 块砖时间是 0，
/// 第 1 块砖时间 = 起始一拍 × (adjustedCountdownTicks − 1) + 第 0 块转到第 1 块的时间，后面每块接着加；
/// adjustedCountdownTicks = 倒计时拍数 ÷ 倒计时变速倍率。离线读谱（tools/Preview.cs）不算倒计时，拍数是 0，
/// 这时就是 7 张工坊关卡按起音对齐验证过的「offset − 一拍」。
/// 复现（2026-11-18 用户：编辑器音画不同步、工坊的也对不上）：游戏里也按「offset − 一拍」算，差了倒计时那 3 拍；
/// 中途补的「此刻实测」「起音对齐」都是在补这个错，已撤掉。真实数据见 MusicTimingTests。
/// </summary>
public static class MusicTiming
{
    /// <param name="levelOffset">关卡 offset（秒）。</param>
    /// <param name="crotchetAtStart">起始 BPM 下一拍的秒数。</param>
    /// <param name="countdownBeatsInTileTimes">砖块时间里算进去的倒计时拍数：游戏里是 adjustedCountdownTicks − 1，离线读谱是 0。</param>
    public static double Offset(double levelOffset, double crotchetAtStart, double countdownBeatsInTileTimes) =>
        levelOffset - (crotchetAtStart * (1 + countdownBeatsInTileTimes));
}
