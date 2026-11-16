using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RhythmHint.Core;

namespace RhythmHint;

/// <summary>当前关卡的一份快照，Overlay 只通过它读游戏。</summary>
internal readonly struct LevelState
{
    public LevelState(scrController controller, scrConductor conductor, List<scrFloor> floors, string floorSource)
    {
        Controller = controller;
        Conductor = conductor;
        Floors = floors;
        FloorSource = floorSource;
    }

    public scrController Controller { get; }

    public scrConductor Conductor { get; }

    public List<scrFloor> Floors { get; }

    /// <summary>砖块列表是从哪里读的（诊断用）。</summary>
    public string FloorSource { get; }

    /// <summary>玩家失败（死亡）后的状态。</summary>
    public bool Failed => Controller.currentState is States.Fail or States.Fail2;

    /// <summary>通关。</summary>
    public bool Won => Controller.currentState is States.Won;

    /// <summary>
    /// 关卡编辑器里在试玩或自动播放（暂停在播放中也算）。
    /// 编辑器走自己的启动流程，实测自动播放时 currentState 一直停在 Start，所以编辑器里以 playMode 为准。
    /// </summary>
    public bool EditorPlaying => ADOBase.isLevelEditor && ADOBase.editor.playMode;

    /// <summary>倒计时、检查点、游玩中、失败、通关时有画面可画；编辑器里只在播放模式时画；选关不画。</summary>
    public bool ShouldDraw => ADOBase.isLevelEditor
        ? EditorPlaying
        : Controller.currentState is States.Countdown or States.Checkpoint
            or States.PlayerControl or States.Fail or States.Fail2 or States.Won;

    public string StateName => ADOBase.isLevelEditor
        ? $"{Controller.currentState}，编辑器{(EditorPlaying ? "播放中" : "编辑中")}"
        : Controller.currentState.ToString();
}

/// <summary>读 ADOFAI 的游戏对象。接口来自反编译 Assembly-CSharp（游戏 Unity 6000.3），游戏大更新后可能要改。</summary>
internal static class GameReader
{
    /// <param name="reason">读不到关卡时的原因，写进诊断日志。</param>
    public static bool TryGetLevel(out LevelState level, out string reason)
    {
        level = default;
        var controller = ADOBase.controller;
        if (controller == null)
        {
            reason = "未找到游戏控制器（位于主菜单或正在载入）";
            return false;
        }

        if (ADOBase.isLevelSelect || ADOBase.isCLS)
        {
            reason = "位于选关界面";
            return false;
        }

        var conductor = ADOBase.conductor;
        if (conductor == null)
        {
            reason = "未找到节拍器（scrConductor）";
            return false;
        }

        var floors = FloorList(out var source);
        if (floors == null || floors.Count < 2 || floors[floors.Count - 1] == null)
        {
            reason = $"砖块列表为空（{floors?.Count ?? 0} 块，来源：{source}）";
            return false;
        }

        // 不再要求 controller.gameworld：自由移动段等情况下游戏会把它设成 false
        level = new LevelState(controller, conductor, floors, source);
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// 当前关卡的砖块列表。编辑器里用编辑器自己用的那份（scnEditor.floors，即 customLevel.levelMaker.listFloors）：
    /// 从主菜单直接进编辑器时，ADOBase.lm（scrLevelMaker.instance，按场景缓存）实测会指向另一份列表，
    /// 读出来一块要按的砖都没有、之后也不再变，编辑器自动播放就一直不显示。
    /// 其余场景用 ADOBase.lm，取不到再用 customLevel.levelMaker。都用 Unity 的 == 判断对象是否已销毁（?. 判断不了）。
    /// </summary>
    private static List<scrFloor>? FloorList(out string source)
    {
        var game = ADOBase.customLevel;
        var fromGame = game != null && game.levelMaker != null ? game.levelMaker.listFloors : null;
        if (ADOBase.isLevelEditor && fromGame is { Count: >= 2 })
        {
            source = "编辑器砖块列表";
            return fromGame;
        }

        var lm = ADOBase.lm;
        if (lm != null && lm.listFloors is { Count: >= 2 } fromLm)
        {
            source = "scrLevelMaker.instance";
            return fromLm;
        }

        source = fromGame != null ? "customLevel.levelMaker" : "未找到砖块列表";
        return fromGame;
    }

    /// <summary>关卡的「指纹」：换关、重开、编辑器改图、换了一份砖块列表后会变，用来决定要不要重新排键。</summary>
    public static (int, int, int, double) Fingerprint(LevelState level)
    {
        var floors = level.Floors;
        return (RuntimeHelpers.GetHashCode(floors), floors[0].GetInstanceID(), floors.Count, floors[floors.Count - 1].entryTime);
    }

    /// <summary>一块要按的砖都没有时，说明各类砖各有几块，写进日志查原因。</summary>
    public static string DescribeFloors(LevelState level)
    {
        int missing = 0, auto = 0, midSpin = 0, fake = 0, hold = 0;
        foreach (var f in level.Floors)
        {
            if (f == null)
            {
                missing++;
                continue;
            }

            auto += f.auto ? 1 : 0;
            midSpin += f.midSpin ? 1 : 0;
            fake += f.isFake ? 1 : 0;
            hold += f.holdLength > -1 ? 1 : 0;
        }

        var last = level.Floors[level.Floors.Count - 1];
        return $"{level.FloorSource}共 {level.Floors.Count} 块：已销毁 {missing}，自动 {auto}，中旋 {midSpin}，假砖 {fake}，长按 {hold}；最后一块时间 {(last != null ? last.entryTime : 0):F2} 秒";
    }

    /// <summary>需要按键的砖块（规则见 FloorHits.From）。</summary>
    public static List<TileHit> ReadHits(LevelState level)
    {
        var floors = new List<FloorInfo>(level.Floors.Count);
        foreach (var f in level.Floors)
        {
            floors.Add(f == null
                ? new FloorInfo(-1, 0, false, true)
                : new FloorInfo(f.seqID, f.entryTime, f.holdLength > -1, f.auto || f.midSpin || f.isFake));
        }

        return FloorHits.From(floors);
    }

    /// <summary>变速点：砖块速度倍率（相对关卡起始 BPM）变化的地方各记一个点。</summary>
    public static List<(double Time, double Speed)> ReadSpeeds(LevelState level)
    {
        var speeds = new List<(double, double)>();
        var lastSpeed = float.NaN;
        foreach (var f in level.Floors)
        {
            if (f == null || f.speed <= 0 || f.speed == lastSpeed)
            {
                continue;
            }

            lastSpeed = f.speed;
            speeds.Add((f.entryTime, f.speed));
        }

        return speeds;
    }

    /// <summary>关卡起始 BPM 下一拍的秒数。</summary>
    public static double Crotchet(LevelState level)
    {
        var crotchet = level.Conductor.crotchetAtStart;
        return crotchet > 0 ? crotchet : 60.0 / System.Math.Max(1f, level.Conductor.bpm);
    }

    /// <summary>当前砖块的显示 BPM（起始 BPM × 速度倍率 × 歌曲变速）。</summary>
    public static float CurrentBpm(LevelState level)
    {
        var seq = level.Controller.currentSeqID;
        var floors = level.Floors;
        var floor = floors[System.Math.Max(0, System.Math.Min(floors.Count - 1, seq))];
        var pitch = level.Conductor.song != null ? level.Conductor.song.pitch : 1f;
        return level.Conductor.bpm * (floor != null ? floor.speed : 1f) * pitch;
    }
}
