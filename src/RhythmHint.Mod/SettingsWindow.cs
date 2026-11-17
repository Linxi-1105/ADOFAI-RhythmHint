using System;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 独立的设置窗口（默认按 F8 打开 / 关闭，UMM 页面里也有按钮）。可以拖动，默认靠右放，不挡左侧和顶部的演示。
/// 开着时演示照常播放，改什么都立刻看得到；关窗口时保存。
/// </summary>
internal sealed class SettingsWindow
{
    private const int WindowId = 0x52484D57; // "RHMW"
    private static readonly string[] Tabs = { "预览与校准", "显示", "排键", "状态" };
    private static readonly string[] HotkeyPresets = { "F8", "F9", "F7", "F6", "Home", "Insert" };
    private static readonly string[] HandPresets = { "右手", "左手" };

    private readonly KeyPicker _keyPicker = new();
    private Rect _rect = new(0, 0, 780, 760);
    private bool _placed;
    private int _tab;
    private Vector2 _scroll;
    private int _confirmResetTab = -1; // 「恢复本页默认值」点了一次、等确认的页
    private float _confirmResetUntil;

    public bool Visible { get; private set; }

    /// <summary>窗口在屏幕上的位置（像素，左上角为原点）；没显示时为 null。给 InputShield 挡住这块区域的鼠标用。</summary>
    public Rect? ScreenRect
    {
        get
        {
            if (!Visible || !_placed)
            {
                return null;
            }

            var scale = WindowSkin.ScreenScale;
            return new Rect(_rect.x * scale, _rect.y * scale, _rect.width * scale, _rect.height * scale);
        }
    }

    public void Toggle()
    {
        if (Visible)
        {
            Close();
        }
        else
        {
            Visible = true;
        }
    }

    public void Close()
    {
        Visible = false;
        Main.Settings.Save(Main.Mod);
    }

    public void Draw(Overlay overlay)
    {
        WindowSkin.Ensure();
        var scale = WindowSkin.ScreenScale;
        var saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        var virtualWidth = Screen.width / scale;
        if (!_placed)
        {
            _placed = true;
            _rect.x = Mathf.Max(0, virtualWidth - _rect.width - 40);
            _rect.y = 160;
        }

        _rect = GUI.Window(WindowId, _rect, _ => Contents(overlay), string.Empty, WindowSkin.Window);
        GUI.matrix = saved;
    }

    private void Contents(Overlay overlay)
    {
        var s = Main.Settings;
        GUILayout.BeginHorizontal();
        GUILayout.Label("全节奏提示 · 设置", WindowSkin.Title);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button($"关闭（{s.SettingsHotkey}）", WindowSkin.Button))
        {
            Close();
        }

        GUILayout.EndHorizontal();
        GUILayout.Space(8);

        GUILayout.BeginHorizontal();
        for (var i = 0; i < Tabs.Length; i++)
        {
            if (GUILayout.Button(Tabs[i], i == _tab ? WindowSkin.TabActive : WindowSkin.Tab))
            {
                _tab = i;
                _scroll = Vector2.zero;
                _confirmResetTab = -1;
                _keyPicker.Close();
            }
        }

        GUILayout.EndHorizontal();
        GUILayout.Space(6);

        _scroll = GUILayout.BeginScrollView(_scroll);
        switch (_tab)
        {
            case 0:
                PreviewTab(s);
                ResetRow(s.ResetPreview, "恢复演示、提示音和画面偏移的默认值。");
                break;
            case 1:
                DisplayTab(s);
                ResetRow(
                    () =>
                    {
                        s.ResetDisplay();
                        _keyPicker.Close();
                    },
                    "恢复显示内容、自适应游戏画幅、尺寸与速度和按键框的默认值。");
                break;
            case 2:
                LayoutTab(s, overlay);
                ResetRow(s.ResetFingering, "恢复排键参数的默认值，惯用手保持不变。");
                break;
            default:
                StatusTab(s, overlay);
                ResetRow(s.ResetHotkey, "恢复快捷键的默认值（F8）。");
                break;
        }

        GUILayout.EndScrollView();
        GUILayout.Label("拖动标题栏可移动窗口，关闭时自动保存设置。", WindowSkin.Note);
        GUI.DragWindow(new Rect(0, 0, 10000, 48));
    }

    /// <summary>
    /// 每页底部的「恢复本页默认值」。点一次先变成确认按钮，3 秒内再点才恢复，免得误点把调好的设置清掉。
    /// </summary>
    private void ResetRow(Action reset, string hint)
    {
        WindowSkin.SectionTitle("默认值");
        var confirming = _confirmResetTab == _tab && Time.unscaledTime < _confirmResetUntil;
        if (WindowSkin.Buttons(confirming ? "再次点击确认恢复" : "恢复本页默认值") == 0)
        {
            if (confirming)
            {
                reset();
                _confirmResetTab = -1;
            }
            else
            {
                _confirmResetTab = _tab;
                _confirmResetUntil = Time.unscaledTime + 3f;
            }
        }

        WindowSkin.Hint(confirming ? "3 秒内再次点击即恢复，此操作无法撤销。" : hint);
    }

    private static void PreviewTab(Settings s)
    {
        WindowSkin.SectionTitle("演示预览");
        s.ShowPreview = WindowSkin.Check("设置窗口打开时播放演示（仅在关卡外）", s.ShowPreview);
        WindowSkin.Hint("在主菜单或选关界面中，演示与当前音乐的节拍同步；无音乐时使用内置的 160 BPM 节拍，并在每个音符处播放提示音。");
        s.PreviewSound = WindowSkin.Check("无音乐时播放音符提示音", s.PreviewSound);
        s.PreviewVolume = WindowSkin.Slider("提示音音量", s.PreviewVolume, 0f, 1f, "0.00", 0.05f);

        WindowSkin.SectionTitle("画面偏移校准");
        WindowSkin.Hint("观察演示：音符到达判定线的时刻应与节拍或提示音一致。音符偏晚时调高数值，偏早时调低数值。此项仅影响显示，不影响判定。");
        s.VisualOffsetMs = WindowSkin.Slider("画面偏移（毫秒）", s.VisualOffsetMs, -200, 200, 1);
        switch (WindowSkin.Buttons("−10", "−1", "+1", "+10", "归零"))
        {
            case 0: s.VisualOffsetMs -= 10; break;
            case 1: s.VisualOffsetMs -= 1; break;
            case 2: s.VisualOffsetMs += 1; break;
            case 3: s.VisualOffsetMs += 10; break;
            case 4: s.VisualOffsetMs = 0; break;
        }
    }

    private void DisplayTab(Settings s)
    {
        WindowSkin.SectionTitle("显示内容");
        s.ShowLanes = WindowSkin.Check("左侧四轨下落面板", s.ShowLanes);
        s.ShowTaiko = WindowSkin.Check("顶部太鼓节奏条", s.ShowTaiko);
        s.ShowBpm = WindowSkin.Check("BPM 与剩余时间", s.ShowBpm);
        s.ShrinkGameView = WindowSkin.Check("自适应游戏画幅", s.ShrinkGameView);
        WindowSkin.Hint("提示面板显示时，游戏画面与界面文字（连击数、判定、暂停菜单等）缩放至面板以外的区域，面板收起时恢复原样。由 IMGUI 绘制的其他 Mod 文字不受此项影响。");

        WindowSkin.SectionTitle("尺寸与速度");
        s.UiScale = WindowSkin.Slider("面板缩放", s.UiScale, 0.5f, 2f, "0.00", 0.05f);
        s.LaneSpeed = WindowSkin.Slider("下落速度", s.LaneSpeed, 0.3f, 3f, "0.00", 0.05f);
        s.TaikoSpeed = WindowSkin.Slider("太鼓条滚动速度", s.TaikoSpeed, 0.3f, 3f, "0.00", 0.05f);
        s.PanelOpacity = WindowSkin.Slider("面板底色不透明度", s.PanelOpacity, 0.3f, 1f, "0.00", 0.05f);

        WindowSkin.SectionTitle("按键框");
        _keyPicker.Draw(s);
        s.LightKeysOnPress = WindowSkin.Check("按下对应按键时点亮按键框（仅显示，不影响判定）", s.LightKeysOnPress);
    }

    private static void LayoutTab(Settings s, Overlay overlay)
    {
        WindowSkin.Hint("排键仅依据当前关卡的砖块间隔计算。修改后立即重新排键，可在演示中查看效果。");

        WindowSkin.SectionTitle("排键规则");
        s.LeftHanded = WindowSkin.Choice("惯用手", s.LeftHanded ? HandPresets[1] : HandPresets[0], HandPresets) == HandPresets[1];

        // 左撇子是整套排法左右镜像，说明文字里的手和键位跟着换
        var (main, other) = s.LeftHanded ? ("左手", "右手") : ("右手", "左手");
        var chordKeys = s.LeftHanded ? "D F" : "J K";
        var (outerRoll, innerRoll) = s.LeftHanded ? ("D F K J", "F D J K") : ("K J D F", "J K F D");
        WindowSkin.Hint($"间隔较长时由{main}食指连击；间隔较短时左右食指交替；间隔极短时同一只手两指轮指；停顿后从{main}重新开始。双押优先分配给{main}（{chordKeys}），与双押相邻的单押分配给{other}；同一只手不会出现由食指到外侧手指的顺序（F→D、J→K）。");
        // 滑块名字一栏宽 LabelWidth（约 13 个汉字），超出会被后面的按钮盖住：名字写短，解释放在下面一行
        s.ResetWindowMs = WindowSkin.Slider("主手重置间隔（毫秒）", s.ResetWindowMs, 100, 300, 5);
        WindowSkin.Hint($"两次按键的间隔大于此值时，由{main}食指单指连击。");
        s.UseRoll = WindowSkin.Check("极快段落启用同手轮指（关闭后仅使用 F、J）", s.UseRoll);
        s.OuterRoll = WindowSkin.Check($"轮指时外侧手指先按（{outerRoll}；关闭后为 {innerRoll}）", s.OuterRoll);
        s.ChordFocusMs = WindowSkin.Slider("双押换手范围（毫秒）", s.ChordFocusMs, 0, 800, 10);
        WindowSkin.Hint("与双押相隔不超过此值的单押改由另一只手按下；设为 0 则不换手。");

        WindowSkin.SectionTitle("其他参数");
        s.JackWindowMs = WindowSkin.Slider("同键连按最小间隔（毫秒）", s.JackWindowMs, 50, 300, 5);
        s.PseudoChordWindowMs = WindowSkin.Slider("双押合并阈值（毫秒）", s.PseudoChordWindowMs, 0, 45, 1);
        WindowSkin.Hint("间隔小于此值的两次按键显示为双押，相隔约 25 毫秒的伪双押同样合并；双押始终由同一只手完成（D+F 或 J+K）。");
        if (WindowSkin.Buttons("重新排键") == 0)
        {
            overlay.RequestRebuild();
        }
    }

    private static void StatusTab(Settings s, Overlay overlay)
    {
        WindowSkin.SectionTitle("当前状态");
        GUILayout.Label(overlay.Status, WindowSkin.Note);

        WindowSkin.SectionTitle("快捷键");
        s.SettingsHotkey = WindowSkin.Choice("打开 / 关闭设置窗口", s.SettingsHotkey, HotkeyPresets);
        WindowSkin.Hint("也可在 Unity Mod Manager 页面（Ctrl+F10）中打开。如需使用其他按键，请修改 Settings.xml 中的 SettingsHotkey（填写 Unity 按键名）。");

        WindowSkin.SectionTitle("关于");
        GUILayout.Label($"版本 {Main.Mod.Info.Version}", WindowSkin.Note);
        WindowSkin.Hint("本 Mod 仅提供视觉提示，不修改判定。运行日志位于 Unity Mod Manager 的 Log.txt，每次显示或隐藏均会记录原因。");
    }
}
