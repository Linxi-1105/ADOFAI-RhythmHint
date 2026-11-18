using System;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 每帧决定画什么：在关卡里画关卡（换关时重新排键），在设置页里画演示，
/// 并处理入场 / 出场动画。绘制交给 TopBarView 和 LaneView。
/// </summary>
internal sealed class Overlay : MonoBehaviour
{
    /// <summary>最后一个音符过去多久后出场。</summary>
    private const double ExitAfterLastNote = 0.8;

    private readonly DisplayClock _clock = new();
    private readonly PresenceAnimator _presence = new();
    private readonly KeyInput _keys = new();
    private GameViewport _viewport = null!;
    private readonly SettingsWindow _window = new();
    private string _hotkeyText = string.Empty;
    private KeyCode _hotkey = KeyCode.None;
    private bool _hotkeyBroken;
    private DemoPlayer _demo = null!;
    private InputShield _shield = null!;
    private (int, int, int, double)? _fingerprint;
    private int _hitCount;
    private bool _rebuildRequested;
    private bool _demoShowing;
    private bool _uiFollowing;
    private bool _editorWasPlaying;
    private States _editorBaseline = States.None;
    private string _builtFingering = string.Empty;
    private bool _failed;
    private float _previewUntil;
    private string _screenState = string.Empty;
    private string _levelStatus = "尚未进入关卡";

    internal OverlayFrame Frame { get; } = new();

    /// <summary>给设置页显示的当前状态。</summary>
    internal string Status => _failed ? _levelStatus : $"{_levelStatus}\n画面：{_screenState}";

    internal void RequestRebuild() => _rebuildRequested = true;

    /// <summary>设置页每帧调用：设置页开着时播放演示。</summary>
    internal void RequestPreview() => _previewUntil = Time.unscaledTime + 0.6f;

    /// <summary>打开独立设置窗口（UMM 页面的按钮调用）。</summary>
    internal void OpenSettings()
    {
        if (!_window.Visible)
        {
            _window.Toggle();
        }
    }

    private void Awake()
    {
        _demo = new DemoPlayer(gameObject);
        _shield = new InputShield(transform);
        _viewport = new GameViewport(transform);
    }

    private void OnDestroy()
    {
        // Mod 被关掉时把游戏画面还原
        _viewport.Restore();
    }

    private void Update()
    {
        if (_failed)
        {
            return;
        }

        try
        {
            ReadHotkey();
            if (_window.Visible)
            {
                RequestPreview(); // 设置窗口开着时播放演示
            }

            Tick();

            // 设置窗口底下垫透明挡板：在窗口里点击、拖动不会被游戏当成击打或点到后面的按钮
            _shield.Cover(_window.ScreenRect);
        }
        catch (Exception ex)
        {
            // 游戏更新后接口变了之类的意外：记一次完整日志后停用叠加层，避免每帧刷屏、拖慢游戏
            _failed = true;
            _demo.Stop();
            _viewport.Restore();
            _shield.Cover(null);
            Frame.Presence = 0;
            _levelStatus = $"发生错误，已停用：{ex.Message}（详见 Unity Mod Manager 日志）";
            Main.Mod.Logger.LogException(ex);
        }
    }

    /// <summary>设置窗口的快捷键（默认 F8）。按键名在设置里改，写错了就当没有快捷键。</summary>
    private void ReadHotkey()
    {
        if (_hotkeyBroken)
        {
            return;
        }

        if (Main.Settings.SettingsHotkey != _hotkeyText)
        {
            _hotkeyText = Main.Settings.SettingsHotkey;
            _hotkey = KeyCodes.Parse(_hotkeyText.Trim());
        }

        try
        {
            if (_hotkey != KeyCode.None && Input.GetKeyDown(_hotkey))
            {
                _window.Toggle();
            }
        }
        catch (InvalidOperationException ex)
        {
            // 游戏关掉了旧版输入系统时会抛这个：只能从 UMM 页面的按钮打开设置窗口
            _hotkeyBroken = true;
            Main.Mod.Logger.Warning($"无法读取键盘输入，设置窗口只能从 Unity Mod Manager 页面打开：{ex.Message}");
        }
    }

    private void Tick()
    {
        var settings = Main.Settings;
        var inLevel = GameReader.TryGetLevel(out var level, out var reason);
        var levelShown = inLevel && TickLevel(level, settings, out reason);
        if (!inLevel)
        {
            _fingerprint = null;
            _editorWasPlaying = false;
        }

        var preview = !levelShown && settings.ShowPreview && Time.unscaledTime < _previewUntil;

        // 设置页关掉后演示还在滑出：音符照常走（不再放嗒声），完全滑出后才停
        var demoRunning = !levelShown && (preview || (_demoShowing && Frame.Presence > 0));
        if (demoRunning)
        {
            _demo.Tick(Frame, settings, sound: preview);
            _demoShowing = true;
            _fingerprint = null; // 演示用过 Frame，回到关卡时要重新装入关卡的音符
        }
        else
        {
            _demo.Stop();
            _demoShowing = false;
        }

        // 按 Esc 暂停时面板收起、游戏画面还原，不挡暂停菜单；暂停中打开本 Mod 的设置窗口时照常显示，方便看改设置的效果
        var paused = levelShown && level.Controller.paused;
        var pausedHide = paused && !_window.Visible;
        Frame.Presence = (float)_presence.Update((levelShown && !pausedHide) || preview, Time.unscaledDeltaTime);

        // 界面文字在关卡里始终跟着面板一起缩放（暂停、开关本 Mod 设置窗口、本关结束都一样）：
        // 面板滑出时一起放大回全屏，完全收起时正好和原样重合，这时才改回覆盖模式；中途不切换，否则会先跳一下再动。
        // 离开关卡后面板还在滑出时也继续跟着，滑完为止
        _uiFollowing = levelShown || (_uiFollowing && Frame.Presence > 0);
        _viewport.Apply(Frame.Presence, settings, _uiFollowing);
        Report(pausedHide ? "已暂停，面板收起"
            : levelShown ? $"显示关卡（{level.StateName}）"
            : preview ? $"显示演示（{_demo.Mode}）"
            : $"隐藏（{reason}）");
        _keys.Read(settings, Frame);
    }

    /// <returns>这一帧关卡是否应该显示；不显示时 reason 写明原因。</returns>
    private bool TickLevel(LevelState level, Settings settings, out string reason)
    {
        // 编辑器每次开始播放时记下当时的游戏状态：编辑器不会清掉上一次留下的「通关 / 失败」，
        // 只有这次播放中状态真的变了才算通关或死亡（否则第二次播放一开始就被当成「本关结束」）
        var editorPlaying = level.EditorPlaying;
        if (editorPlaying && !_editorWasPlaying)
        {
            _editorBaseline = level.Controller.currentState;
        }

        _editorWasPlaying = editorPlaying;
        var stale = ADOBase.isLevelEditor && level.Controller.currentState == _editorBaseline;
        var won = level.Won && !stale;
        var failed = level.Failed && !stale;

        if (!level.ShouldDraw)
        {
            reason = $"游戏状态：{level.StateName}";
            return false;
        }

        if (level.Floors[level.Floors.Count - 1].entryTime <= 0)
        {
            reason = "砖块时间尚未计算完成";
            return false;
        }

        var fp = GameReader.Fingerprint(level);
        if (_rebuildRequested || _fingerprint != fp || settings.FingeringKey != _builtFingering)
        {
            _rebuildRequested = false;
            _fingerprint = fp;
            Rebuild(level);
        }

        if (_hitCount == 0)
        {
            // 一块要按的砖都没有：不显示（否则一出来就判成「本关结束」），各类砖的数量已在 Rebuild 里写进日志
            reason = "本关没有需要按键的砖块";
            return false;
        }

        // 失败画面期间定住，不让音符继续流动；重开或回检查点后恢复
        Frame.Now = _clock.Advance(level.Conductor.songposition_minusi + (settings.VisualOffsetMs / 1000.0), failed);
        Frame.Bpm = GameReader.CurrentBpm(level);
        Frame.TrackHits(level.Controller.currentSeqID);

        if (won || (!failed && Frame.NextNote >= Frame.Notes.Count && Frame.Now > Frame.EndTime + ExitAfterLastNote))
        {
            reason = "本关结束";
            return false; // 出场动画
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>只按这一关自己的砖块排键，不读任何外部谱面。</summary>
    private void Rebuild(LevelState level)
    {
        var hits = GameReader.ReadHits(level);
        _hitCount = hits.Count;
        var end = hits.Count > 0 ? hits[hits.Count - 1].Time : 0;

        // 节拍线从起点砖（第 0 块）开始算，只用来画；排键只看砖块之间的间隔
        var tempo = BeatGrid.MusicalTempo(GameReader.ReadSpeeds(level), GameReader.Crotchet(level));
        var beats = BeatGrid.Build(tempo, level.Floors[0].entryTime, end + 2);
        _builtFingering = Main.Settings.FingeringKey;
        var notes = NotePlanner.Plan(hits, Main.Settings.ToFingering());
        Frame.Load(notes, beats, end);
        Frame.Caption = string.Empty;

        var chords = new int[Lanes.Count + 1]; // 按几个键：双押、三押、四押各几个
        var laneUse = new int[Lanes.Count];
        foreach (var n in notes)
        {
            chords[n.Lanes.Count]++;
            foreach (var lane in n.Lanes.Keys)
            {
                laneUse[lane]++;
            }
        }

        var byAngle = hits.Exists(h => !double.IsNaN(h.Angle));
        _levelStatus = $"本关共 {hits.Count} 次按键，生成 {notes.Count} 个提示（双押 {chords[2]}、三押 {chords[3]}、四押 {chords[4]} 个，"
            + $"{(byAngle ? "按转角判断" : "按时间判断")}），各轨道音符数 [{string.Join(", ", laneUse)}]"
            + (hits.Count == 0 ? $"；{GameReader.DescribeFloors(level)}" : string.Empty);
        Main.Mod.Logger.Log(_levelStatus);
    }

    /// <summary>画面状态（显示什么 / 为什么隐藏）变化时记一行日志，方便查「为什么没显示」。</summary>
    private void Report(string state)
    {
        if (state == _screenState)
        {
            return;
        }

        _screenState = state;
        Main.Mod.Logger.Log($"画面：{state}");
    }

    private void OnGUI()
    {
        GUI.depth = 100; // 让 UMM 设置窗口等其它界面盖在上面
        if (Frame.Presence > 0 && Event.current.type == EventType.Repaint)
        {
            DrawOverlay();
        }

        // 设置窗口要处理鼠标、键盘等所有事件，不能只在 Repaint 时画
        if (_window.Visible && !_failed)
        {
            _window.Draw(this);
        }
    }

    private void DrawOverlay()
    {
        // 游戏画面缩进去之后，让出来的区域没有摄像机在画，会残留旧画面：先铺纯黑，再画面板
        var freed = _viewport.Freed;
        if (freed.x > 0)
        {
            GUI.DrawTexture(new Rect(0, 0, freed.x, Screen.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f, Color.black, 0f, 0f);
        }

        if (freed.y > 0)
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, freed.y), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f, Color.black, 0f, 0f);
        }

        var settings = Main.Settings;
        var layout = new OverlayLayout(settings);
        var p = Frame.Presence;
        Painter.Alpha = p;
        var saved = GUI.matrix;
        try
        {
            if (settings.ShowTaiko || settings.ShowBpm)
            {
                // 顶部条从上方滑入 / 滑出
                GUI.matrix = saved * Matrix4x4.Translate(new Vector3(0, -(1 - p) * (layout.TopHeight + 8), 0));
                TopBarView.Draw(Frame, layout, settings);
            }

            if (settings.ShowLanes)
            {
                // 四轨面板从左侧滑入 / 滑出
                GUI.matrix = saved * Matrix4x4.Translate(new Vector3(-(1 - p) * (layout.PanelWidth + 8), 0, 0));
                LaneView.Draw(Frame, layout, settings);
            }
        }
        finally
        {
            GUI.matrix = saved;
            Painter.Alpha = 1f;
        }
    }
}
