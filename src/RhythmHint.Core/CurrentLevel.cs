namespace RhythmHint.Core;

/// <summary>
/// 「编辑本关手序」用的那一关：只在 Mod 正显示这一关（游玩、暂停、死亡、通关画面还停在关卡里）、
/// 或者关卡已载入还没按键开始时才有，离开关卡就清掉。
/// 复现（2026-11-18 用户）：打完歌退回主菜单按 F8 还能导出上一首——原来导出看的是游戏状态。
/// 按钮的结果提示由 <see cref="ActionFeedback"/> 管，几秒后自己消失，不会挂在上一首上。
/// </summary>
/// <typeparam name="T">这一关的快照（砖块、音符等），由 Mod 定义。</typeparam>
public sealed class CurrentLevel<T>
    where T : class
{
    /// <summary>当前这一关；不在关卡里为 null。</summary>
    public T? Snapshot { get; private set; }

    public void Show(T snapshot) => Snapshot = snapshot;

    public void Leave() => Snapshot = null;
}
