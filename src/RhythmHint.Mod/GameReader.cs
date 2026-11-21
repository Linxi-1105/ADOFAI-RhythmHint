using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using RhythmHint.Core;

namespace RhythmHint;

/// <summary>当前关卡的一份快照，Overlay 只通过它读游戏。</summary>
internal readonly struct LevelState
{
    public LevelState(scrController controller, scrConductor conductor, List<scrFloor> floors, Message floorSource)
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
    public Message FloorSource { get; }

    /// <summary>
    /// 游戏状态，直接读状态机（StateEngine.GetState）。scrController.currentState 是它的 Update 里抄过来的，
    /// 关卡编辑器里不更新（1.2.0 实测：编辑器里播放、死亡后都还是 None，死后音符照走）；状态机还没初始化时退回 currentState。
    /// </summary>
    public States State => Controller.stateMachine.GetState() is States state ? state : Controller.currentState;

    /// <summary>玩家失败（死亡）后的状态。</summary>
    public bool Failed => State is States.Fail or States.Fail2;

    /// <summary>通关。</summary>
    public bool Won => State is States.Won;

    /// <summary>
    /// 关卡编辑器里在试玩或自动播放（暂停在播放中也算）。编辑器走自己的启动流程，所以编辑器里以 playMode 为准。
    /// </summary>
    public bool EditorPlaying => ADOBase.isLevelEditor && ADOBase.editor.playMode;

    /// <summary>倒计时、检查点、游玩中、失败、通关时有画面可画；编辑器里只在播放模式时画；选关不画。</summary>
    public bool ShouldDraw => ADOBase.isLevelEditor
        ? EditorPlaying
        : State is States.Countdown or States.Checkpoint
            or States.PlayerControl or States.Fail or States.Fail2 or States.Won;

    /// <summary>游戏状态（英文状态名不翻译），编辑器里再写明是播放中还是编辑中。</summary>
    public Message StateName => ADOBase.isLevelEditor
        ? Lang.M("{0}，编辑器{1}", State, EditorPlaying ? Lang.M("播放中") : Lang.M("编辑中"))
        : new Message(State.ToString());
}

/// <summary>本关的曲名等信息：认「同一首歌」、给编辑器显示和找歌曲文件用。</summary>
/// <param name="SongFile">自定义关卡的歌曲文件完整路径；官方关卡为空。</param>
/// <param name="Offset">音乐文件里的位置 = 砖块时间 + Offset（秒，scrConductor.addoffset）。</param>
internal readonly record struct LevelInfo(string Song, string Artist, string Charter, bool Official, string SongFile, double Offset);

/// <summary>读 ADOFAI 的游戏对象。接口来自反编译 Assembly-CSharp（游戏 Unity 6000.3），游戏大更新后可能要改。</summary>
internal static class GameReader
{
    /// <param name="reason">读不到关卡时的原因，显示在状态页、写进诊断日志。</param>
    public static bool TryGetLevel(out LevelState level, out Message reason)
    {
        level = default;
        var controller = ADOBase.controller;
        if (controller == null)
        {
            reason = Lang.M("未找到游戏控制器（位于主菜单或正在载入）");
            return false;
        }

        if (ADOBase.isLevelSelect || ADOBase.isCLS)
        {
            reason = Lang.M("位于选关界面");
            return false;
        }

        var conductor = ADOBase.conductor;
        if (conductor == null)
        {
            reason = Lang.M("未找到节拍器（scrConductor）");
            return false;
        }

        var floors = FloorList(out var source);
        if (floors == null || floors.Count < 2 || floors[floors.Count - 1] == null)
        {
            reason = Lang.M("砖块列表为空（{0} 块，来源：{1}）", floors?.Count ?? 0, source);
            return false;
        }

        // 不再要求 controller.gameworld：自由移动段等情况下游戏会把它设成 false
        level = new LevelState(controller, conductor, floors, source);
        reason = Message.Empty;
        return true;
    }

    /// <summary>
    /// 当前关卡的砖块列表。编辑器里用编辑器自己用的那份（scnEditor.floors，即 customLevel.levelMaker.listFloors）：
    /// 从主菜单直接进编辑器时，ADOBase.lm（scrLevelMaker.instance，按场景缓存）实测会指向另一份列表，
    /// 读出来一块要按的砖都没有、之后也不再变，编辑器自动播放就一直不显示。
    /// 其余场景用 ADOBase.lm，取不到再用 customLevel.levelMaker。都用 Unity 的 == 判断对象是否已销毁（?. 判断不了）。
    /// </summary>
    private static List<scrFloor>? FloorList(out Message source)
    {
        var game = ADOBase.customLevel;
        var fromGame = game != null && game.levelMaker != null ? game.levelMaker.listFloors : null;
        if (ADOBase.isLevelEditor && fromGame is { Count: >= 2 })
        {
            source = Lang.M("编辑器砖块列表");
            return fromGame;
        }

        var lm = ADOBase.lm;
        if (lm != null && lm.listFloors is { Count: >= 2 } fromLm)
        {
            source = new Message("scrLevelMaker.instance");
            return fromLm;
        }

        source = fromGame != null ? new Message("customLevel.levelMaker") : Lang.M("未找到砖块列表");
        return fromGame;
    }

    /// <summary>
    /// 关卡的「指纹」：换关、重开、编辑器改图、换了一份砖块列表后会变，用来决定要不要重新排键。
    /// 游戏设置里双押砖改成只按一下（或改回来）也算：双押砖要按几下变了。
    /// </summary>
    public static (int, int, int, double, bool) Fingerprint(LevelState level)
    {
        var floors = level.Floors;
        return (RuntimeHelpers.GetHashCode(floors), floors[0].GetInstanceID(), floors.Count, floors[floors.Count - 1].entryTime, MultitapOnce);
    }

    /// <summary>游戏设置：双押砖（关卡事件 Multitap）只按一下就过（scrPlanet 里 multitapTileBehavior = HitOnce 时第一下就前进）。</summary>
    public static bool MultitapOnce => Persistence.multiTapTileBehavior == MultitapTileBehavior.HitOnce;

    /// <summary>一块要按的砖都没有时，说明各类砖各有几块，写进日志查原因。</summary>
    public static Message DescribeFloors(LevelState level)
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
        return Lang.M("{0}共 {1} 块：已销毁 {2}，自动 {3}，中旋 {4}，假砖 {5}，长按 {6}；最后一块时间 {7:F2} 秒",
            level.FloorSource, level.Floors.Count, missing, auto, midSpin, fake, hold, last != null ? last.entryTime : 0);
    }

    /// <summary>
    /// 需要按键的砖块（规则见 FloorHits.From）。转角读 scrFloor.angleLength（星球在这块砖上转过的角度，按弧度换算成度），
    /// 用来分辨多押和极快的连打。它记在哪一段（离开还是到达这块砖）没有文档，先和砖块时间核对、用对得上的那种，
    /// 都对不上就退回只看时间；核对结果写进日志。双押砖读 scrFloor.tapsNeeded，按游戏设置（MultitapOnce）算要按几下。
    /// </summary>
    public static List<TileHit> ReadHits(LevelState level)
    {
        var floors = new List<FloorInfo>(level.Floors.Count);
        var multitaps = 0;
        foreach (var f in level.Floors)
        {
            floors.Add(f == null
                ? new FloorInfo(-1, 0, false, true)
                : new FloorInfo(f.seqID, f.entryTime, f.holdLength > -1, f.auto || f.midSpin || f.isFake,
                    f.angleLength * (180 / System.Math.PI), f.speed, f.tapsNeeded));
            multitaps += f != null && f.tapsNeeded > 1 ? 1 : 0;
        }

        var once = MultitapOnce;
        if (multitaps > 0)
        {
            Main.Mod.Logger.Log($"双押砖：{multitaps} 块，{(once ? "游戏设置为只按一下，按单押提示" : "按要按的下数提示多押")}");
        }

        var crotchet = Crotchet(level);
        var check = FloorHits.CheckAngles(floors, crotchet);
        Main.Mod.Logger.Log($"转角核对：按离开这块砖对上 {check.ExitMatch:P0}，按到达这块砖对上 {check.ArrivalMatch:P0}；"
            + check.Source switch
            {
                AngleSource.Exit => "按离开对，多押按转角判断",
                AngleSource.Arrival => "按到达对，多押按转角判断",
                _ => "都对不上，多押改按时间判断",
            });
        return FloorHits.From(floors, crotchet, once);
    }

    /// <summary>
    /// 曲名、曲师、谱师、歌曲文件。自定义关卡读 LevelData（编辑器里读编辑器自己的那份）；
    /// 官方关卡没有 LevelData，用关卡编号（ADOBase.currentLevel，比如 1-X）当曲名。
    /// </summary>
    public static LevelInfo ReadInfo(LevelState level)
    {
        var data = LevelData();
        var offset = level.Conductor.addoffset;
        if (data == null)
        {
            return new LevelInfo(ADOBase.currentLevel ?? string.Empty, string.Empty, string.Empty, true, string.Empty, offset);
        }

        var levelPath = ADOBase.levelPath;
        var songFile = !string.IsNullOrEmpty(levelPath) && !string.IsNullOrEmpty(data.songFilename)
            ? Path.Combine(Path.GetDirectoryName(levelPath) ?? string.Empty, data.songFilename)
            : string.Empty;
        return new LevelInfo(data.song ?? string.Empty, data.artist ?? string.Empty, data.author ?? string.Empty, false, songFile, offset);
    }

    /// <summary>这一关的关卡数据（编辑器里读编辑器自己的那份）；游戏自带的老关卡没有为 null。</summary>
    public static ADOFAI.LevelData? LevelData() => ADOBase.isLevelEditor && ADOBase.editor != null ? ADOBase.editor.levelData
        : ADOBase.customLevel != null ? ADOBase.customLevel.levelData : null;

    /// <summary>
    /// 每块砖的初始位置（scrFloor.startPos）、时间和进出方向（entryangle / exitangle，换成数学角，见 PathAngles），
    /// 编辑器画轨道形状用；要按的砖标出来。
    /// </summary>
    public static List<PathPoint> ReadPath(LevelState level, IReadOnlyList<TileHit> hits)
    {
        var hitTiles = new HashSet<int>();
        foreach (var h in hits)
        {
            hitTiles.Add(h.TileIndex);
        }

        var path = new List<PathPoint>(level.Floors.Count);
        foreach (var f in level.Floors)
        {
            if (f != null)
            {
                path.Add(new PathPoint(f.seqID, f.startPos.x, f.startPos.y, f.entryTime, hitTiles.Contains(f.seqID), f.entryangle, f.exitangle));
            }
        }

        var result = PathAngles.Normalize(path);
        Main.Mod.Logger.Log(result.Detected
            ? $"编辑器：砖块方向按游戏的进出角度画（相邻砖对上 {result.Matched}/{result.Pairs}）"
            : $"编辑器：砖块进出角度和位置对不上（{result.Matched}/{result.Pairs}），按相邻砖的位置画");
        return result.Points;
    }

    /// <summary>
    /// 变速点：砖块速度倍率（相对关卡起始 BPM）变化的地方各记一个点。速度不是正的有限数、时间不是有限数的砖跳过
    /// （关卡数据异常时可能有 NaN、无穷大，节拍线推算拿到它们会出错）。
    /// </summary>
    public static List<(double Time, double Speed)> ReadSpeeds(LevelState level)
    {
        var speeds = new List<(double, double)>();
        var lastSpeed = float.NaN;
        foreach (var f in level.Floors)
        {
            if (f == null || !(f.speed > 0) || float.IsInfinity(f.speed) || f.speed == lastSpeed || !IsFinite(f.entryTime))
            {
                continue;
            }

            lastSpeed = f.speed;
            speeds.Add((f.entryTime, f.speed));
        }

        return speeds;
    }

    /// <summary>
    /// 关卡起始 BPM 下一拍的秒数，总是正的有限数：crotchetAtStart 不是正的有限数时按 BPM 算，BPM 也不对（不到 1、NaN、无穷大）时按 1 BPM 算
    /// （和原来 BPM 不到 1 时一样），免得节拍线推算拿到 0 或 NaN 出错。
    /// </summary>
    public static double Crotchet(LevelState level)
    {
        var crotchet = level.Conductor.crotchetAtStart;
        if (crotchet > 0 && IsFinite(crotchet))
        {
            return crotchet;
        }

        var bpm = level.Conductor.bpm;
        return 60.0 / (bpm >= 1 && IsFinite(bpm) ? bpm : 1f);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

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
