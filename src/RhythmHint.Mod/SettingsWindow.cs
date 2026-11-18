using System;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 独立的设置窗口（默认按 F8 打开 / 关闭，UMM 页面里也有按钮）。可以拖动，默认靠右放，不挡左侧和顶部的演示。
/// 开着时演示照常播放，改什么都立刻看得到；关窗口时保存。
/// </summary>
internal sealed class SettingsWindow
{
    private const int WindowId = 0x52484D57; // "RHMW"
    private static readonly string[] HotkeyPresets = { "F8", "F9", "F7", "F6", "Home", "Insert" };
    private const string QqGroup = "1136407588";
    private const int StatusTabIndex = 3; // 「状态」页，页签顺序见 Contents

    private readonly KeyPicker _keyPicker = new();
    private Rect _rect = new(0, 0, 780, 840);
    private bool _placed;
    private int _tab;
    private Vector2 _scroll;
    private int _confirmResetTab = -1; // 「恢复本页默认值」点了一次、等确认的页
    private float _confirmResetUntil;
    private string? _copied; // 刚复制到剪贴板的内容，对应的按钮显示「已复制」一会儿
    private float _copiedUntil;

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
        WindowSkin.BeginFrame();
        GUILayout.BeginHorizontal();
        GUILayout.Label(Lang.T("全节奏提示 · 设置"), WindowSkin.Title);
        GUILayout.FlexibleSpace();
        var newer = UpdateNotice.Newer;
        if (newer != null && GUILayout.Button(Lang.T("有新版本 {0}", newer), WindowSkin.Selected))
        {
            SelectTab(StatusTabIndex);
        }

        if (GUILayout.Button(Lang.T("关闭（{0}）", s.SettingsHotkey), WindowSkin.Button))
        {
            Close();
        }

        GUILayout.EndHorizontal();
        GUILayout.Space(8);

        var tabs = new[] { Lang.T("预览与校准"), Lang.T("显示"), Lang.T("排键"), Lang.T("状态") };
        GUILayout.BeginHorizontal();
        for (var i = 0; i < tabs.Length; i++)
        {
            if (GUILayout.Button(tabs[i], i == _tab ? WindowSkin.TabActive : WindowSkin.Tab))
            {
                SelectTab(i);
            }
        }

        GUILayout.EndHorizontal();
        GUILayout.Space(6);

        _scroll = GUILayout.BeginScrollView(_scroll);
        switch (_tab)
        {
            case 0:
                PreviewTab(s);
                ResetRow(s.ResetPreview);
                break;
            case 1:
                DisplayTab(s);
                ResetRow(() =>
                {
                    s.ResetDisplay();
                    _keyPicker.Close();
                });
                break;
            case 2:
                LayoutTab(s, overlay);
                ResetRow(s.ResetFingering, Lang.T("惯用手和多押用手保持不变。"));
                break;
            default:
                StatusTab(s, overlay);
                ResetRow(s.ResetHotkey, Lang.T("界面语言保持不变。"));
                break;
        }

        GUILayout.EndScrollView();
        GUILayout.Label(Lang.T("拖动标题栏可移动窗口，关闭时自动保存设置。"), WindowSkin.Note);
        GUI.DragWindow(new Rect(0, 0, 10000, 48));
    }

    private void SelectTab(int tab)
    {
        _tab = tab;
        _scroll = Vector2.zero;
        _confirmResetTab = -1;
        _keyPicker.Close();
    }

    /// <summary>
    /// 每页底部的「恢复本页默认值」。点一次先变成确认按钮，3 秒内再点才恢复，免得误点把调好的设置清掉。
    /// </summary>
    private void ResetRow(Action reset, string? note = null)
    {
        WindowSkin.SectionTitle(Lang.T("默认值"));
        var confirming = _confirmResetTab == _tab && Time.unscaledTime < _confirmResetUntil;
        if (WindowSkin.Buttons(confirming ? Lang.T("再次点击确认恢复") : Lang.T("恢复本页默认值")) == 0)
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

        var hint = confirming ? Lang.T("3 秒内再次点击即恢复，无法撤销。") : note;
        if (hint != null)
        {
            WindowSkin.Hint(hint);
        }
    }

    /// <summary>两个选项（前一个对应 false、后一个对应 true）的选择行。</summary>
    private static bool Choice(string label, bool value, string no, string yes) =>
        WindowSkin.Choice(label, value ? yes : no, new[] { no, yes }) == yes;

    // 排版：每项都是「左边名字、右边控件」，开关、选项、滑块左右对齐；说明只在名字说不清时写一句，放在控件下面

    private static void PreviewTab(Settings s)
    {
        WindowSkin.SectionTitle(Lang.T("演示预览"));
        s.ShowPreview = WindowSkin.Switch(Lang.T("关卡外播放演示"), s.ShowPreview);
        s.PreviewSound = WindowSkin.Switch(Lang.T("无音乐时播放提示音"), s.PreviewSound);
        s.PreviewVolume = WindowSkin.Slider(Lang.T("提示音音量"), s.PreviewVolume, 0f, 1f, "0.00", 0.05f);

        WindowSkin.SectionTitle(Lang.T("画面偏移校准"));
        s.VisualOffsetMs = WindowSkin.Slider(Lang.T("画面偏移（毫秒）"), s.VisualOffsetMs, -200, 200, 1);
        switch (WindowSkin.Buttons("−10", "−1", "+1", "+10", Lang.T("归零")))
        {
            case 0: s.VisualOffsetMs -= 10; break;
            case 1: s.VisualOffsetMs -= 1; break;
            case 2: s.VisualOffsetMs += 1; break;
            case 3: s.VisualOffsetMs += 10; break;
            case 4: s.VisualOffsetMs = 0; break;
        }

        WindowSkin.Hint(Lang.T("对照演示的节拍：音符偏晚时调高，偏早时调低。仅影响显示。"));
    }

    private void DisplayTab(Settings s)
    {
        WindowSkin.SectionTitle(Lang.T("显示内容"));
        s.ShowLanes = WindowSkin.Switch(Lang.T("四轨下落面板"), s.ShowLanes);
        s.ShowTaiko = WindowSkin.Switch(Lang.T("顶部太鼓条"), s.ShowTaiko);
        s.ShowBpm = WindowSkin.Switch(Lang.T("BPM 与剩余时间"), s.ShowBpm);

        WindowSkin.SectionTitle(Lang.T("四轨面板"));
        s.RoundNotes = Choice(Lang.T("音符样式"), s.RoundNotes, Lang.T("横条"), Lang.T("圆形"));
        _keyPicker.Draw(s);
        s.LightKeysOnPress = WindowSkin.Switch(Lang.T("按下时点亮按键框"), s.LightKeysOnPress);

        WindowSkin.SectionTitle(Lang.T("尺寸与速度"));
        s.UiScale = WindowSkin.Slider(Lang.T("面板缩放"), s.UiScale, 0.5f, 2f, "0.00", 0.05f);
        s.LaneSpeed = WindowSkin.Slider(Lang.T("下落速度"), s.LaneSpeed, 0.3f, 3f, "0.00", 0.05f);
        s.TaikoSpeed = WindowSkin.Slider(Lang.T("太鼓条滚动速度"), s.TaikoSpeed, 0.3f, 3f, "0.00", 0.05f);
        s.PanelOpacity = WindowSkin.Slider(Lang.T("面板底色不透明度"), s.PanelOpacity, 0.3f, 1f, "0.00", 0.05f);

        WindowSkin.SectionTitle(Lang.T("游戏画面"));
        s.ShrinkGameView = WindowSkin.Switch(Lang.T("自适应游戏画幅"), s.ShrinkGameView);
        WindowSkin.Hint(Lang.T("面板显示时，游戏画面和界面文字缩到面板以外的区域。"));
    }

    private static void LayoutTab(Settings s, Overlay overlay)
    {
        WindowSkin.SectionTitle(Lang.T("排键规则"));
        var resetStyle = Lang.T("时间重置法");
        var reviewStyle = Lang.T("审核偏好 v1");
        s.Style = WindowSkin.Choice(Lang.T("排法"), s.Style == FingeringStyle.ReviewPreferenceV1 ? reviewStyle : resetStyle,
            new[] { resetStyle, reviewStyle }) == reviewStyle ? FingeringStyle.ReviewPreferenceV1 : FingeringStyle.TimeReset;
        if (s.Style == FingeringStyle.ReviewPreferenceV1)
        {
            WindowSkin.Hint(Lang.T("实验排法：恢复后减少让手，中速和短串优先交替；含长按关卡暂用时间重置法。"));
        }

        var (right, left) = (Lang.T("右手"), Lang.T("左手"));
        s.LeftHanded = Choice(Lang.T("惯用手"), s.LeftHanded, right, left);
        s.ChordsOnOtherHand = Choice(Lang.T("多押用手"), s.ChordsOnOtherHand, Lang.T("同惯用手"), Lang.T("另一只手"));

        // 左撇子是整套排法左右镜像，说明文字里的手和键位跟着换；多押用手是相对惯用手的
        var main = s.LeftHanded ? left : right;
        var (chordHand, chordKeys) = s.LeftHanded != s.ChordsOnOtherHand ? (left, "D F") : (right, "J K");
        var (outerRoll, innerRoll) = s.LeftHanded ? ("D F K J", "F D J K") : ("K J D F", "J K F D");
        WindowSkin.Hint(Lang.T("只看本关的砖块间隔：慢时{0}食指单点，稍快时左右食指交替，极快时同手轮指；多押优先{1}（{2}），三押由另一只手补一个键。",
            main, chordHand, chordKeys));

        WindowSkin.SectionTitle(Lang.T("节奏分界"));
        s.ResetWindowMs = WindowSkin.Slider(Lang.T("主手重置间隔（毫秒）"), s.ResetWindowMs, 100, 300, 5);
        WindowSkin.Hint(Lang.T("间隔更长时{0}食指单点；当前相当于 {1} BPM 的直线。", main, StraightBpm(s.ResetWindowMs)));
        s.ChordFocusMs = WindowSkin.Slider(Lang.T("多押换手范围（毫秒）"), s.ChordFocusMs, 0, 800, 10);
        WindowSkin.Hint(s.Style == FingeringStyle.ReviewPreferenceV1
            ? Lang.T("审核偏好的有效让手范围不超过主手重置间隔；0 为不换手。")
            : Lang.T("与多押相隔在此范围内的单押改用另一只手；0 为不换手。"));

        WindowSkin.SectionTitle(Lang.T("轮指"));
        s.UseRoll = WindowSkin.Switch(Lang.T("极快时同手轮指"), s.UseRoll);
        s.OuterRoll = WindowSkin.Switch(Lang.T("外侧手指先按"), s.OuterRoll);
        WindowSkin.Hint(s.UseRoll ? Lang.T("开启为 {0}，关闭为 {1}。", outerRoll, innerRoll) : Lang.T("轮指关闭时只用 F、J。"));

        if (s.Style == FingeringStyle.ReviewPreferenceV1)
        {
            WindowSkin.SectionTitle(Lang.T("短串实验参数"));
            s.ReviewShortMinGapMs = WindowSkin.Slider(Lang.T("短串最小间隔（毫秒）"), s.ReviewShortMinGapMs, 35, 150, 1);
            s.ReviewShortMaxGapMs = WindowSkin.Slider(Lang.T("短串最大间隔（毫秒）"), s.ReviewShortMaxGapMs, s.ReviewShortMinGapMs, 165f, "0.0", 0.5f);
            s.ReviewShortMinNotes = WindowSkin.Slider(Lang.T("短串最少音符数"), s.ReviewShortMinNotes, 2, 8, 1);
            s.ReviewShortMaxNotes = WindowSkin.Slider(Lang.T("短串最多音符数"), s.ReviewShortMaxNotes, s.ReviewShortMinNotes, 16, 1);
            s.ReviewTimingToleranceMs = WindowSkin.Slider(Lang.T("等间隔容差（毫秒）"), s.ReviewTimingToleranceMs, 0f, 10f, "0.0", 0.5f);
        }

        WindowSkin.SectionTitle(Lang.T("其他参数"));
        s.JackWindowMs = WindowSkin.Slider(Lang.T("同键连按最小间隔（毫秒）"), s.JackWindowMs, 50, 300, 5);
        WindowSkin.Hint(Lang.T("当前相当于 {0} BPM 的直线，更快时不连按同一个键。", StraightBpm(s.JackWindowMs)));
        s.ChordAngleDeg = WindowSkin.Slider(Lang.T("多押转角上限（度）"), s.ChordAngleDeg, 0, 60, 1);
        WindowSkin.Hint(Lang.T("相邻两块砖转角不超过此值、且相隔不超过 {0} 毫秒时合为多押；连续 5 块以上视为连打。", MaxChordGapMs));
        if (WindowSkin.Buttons(Lang.T("重新排键")) == 0)
        {
            overlay.RequestRebuild();
        }
    }

    /// <summary>多押里相邻两块最多隔多久（毫秒），只读 FingeringOptions 的默认值，不在设置里调。</summary>
    private static readonly int MaxChordGapMs = Mathf.RoundToInt((float)(new FingeringOptions().ChordMaxGap * 1000));

    /// <summary>间隔（毫秒）相当于多少 BPM 的直线：180° 直线每块砖一拍，BPM = 60000 / 毫秒。</summary>
    private static int StraightBpm(int ms) => Mathf.RoundToInt(60000f / Mathf.Max(1, ms));

    private void StatusTab(Settings s, Overlay overlay)
    {
        var newer = UpdateNotice.Newer;
        var home = Main.Mod.Info.HomePage;
        if (newer != null)
        {
            WindowSkin.SectionTitle(Lang.T("更新"));
            var buttons = string.IsNullOrEmpty(home) ? Array.Empty<string>() : new[] { Lang.T("打开下载页") };
            if (WindowSkin.Info(Lang.T("最新版本"), newer.ToString(), buttons) == 0)
            {
                Browser.Open(home + "/releases/latest");
            }

            WindowSkin.Hint(Lang.T("可在 UMM 安装器的 Mods 页右键本 Mod，选择 Update 一键更新；也可从交流 QQ 群的群文件下载。"));
        }

        WindowSkin.SectionTitle(Lang.T("当前状态"));
        GUILayout.Label(overlay.Status, WindowSkin.Note);

        WindowSkin.SectionTitle(Lang.T("排键审核"));
        if (WindowSkin.Buttons(Lang.T("导出当前关卡排键（JSON）")) == 0)
        {
            ReviewExport.CurrentLevel();
        }

        WindowSkin.Hint(ReviewExport.Status);
        WindowSkin.Hint(Lang.T("在本地网页编辑器中打开导出的 JSON，调整轨道后保存审核文件。"));

        // 语言名用它自己的文字写（不翻译），「跟随游戏」翻译；一行放不下六个，三个一行
        WindowSkin.SectionTitle(Lang.T("界面语言"));
        var follow = Lang.T("跟随游戏");
        var names = new string[Languages.All.Count + 1];
        names[0] = follow;
        var current = follow;
        for (var i = 0; i < Languages.All.Count; i++)
        {
            names[i + 1] = Languages.All[i].Name;
            current = Languages.All[i].Code == s.Language ? Languages.All[i].Name : current;
        }

        var picked = Array.IndexOf(names, WindowSkin.Choice(Lang.T("语言"), current, names, perRow: 3));
        s.Language = picked <= 0 ? string.Empty : Languages.All[picked - 1].Code;
        WindowSkin.Hint(Lang.T("跟随游戏时使用游戏的语言；没有对应翻译的语言显示英文。"));

        WindowSkin.SectionTitle(Lang.T("快捷键"));
        s.SettingsHotkey = WindowSkin.Choice(Lang.T("打开 / 关闭设置窗口"), s.SettingsHotkey, HotkeyPresets);
        WindowSkin.Hint(Lang.T("也可在 Unity Mod Manager（Ctrl+F10）中打开；其他按键请修改 Settings.xml。"));

        // 作者和仓库地址取自 Info.json（RhythmHint.Mod.csproj 生成），群号写在这里
        WindowSkin.SectionTitle(Lang.T("关于"));
        var info = Main.Mod.Info;
        WindowSkin.Info(Lang.T("作者"), info.Author);
        if (WindowSkin.Info(Lang.T("交流 QQ 群"), QqGroup, CopyLabel(QqGroup, Lang.T("复制"))) == 0)
        {
            Copy(QqGroup);
        }

        if (!string.IsNullOrEmpty(info.HomePage))
        {
            switch (WindowSkin.Info(Lang.T("GitHub 仓库"), string.Empty, Lang.T("在浏览器中打开"), CopyLabel(info.HomePage, Lang.T("复制链接"))))
            {
                case 0: Browser.Open(info.HomePage); break;
                case 1: Copy(info.HomePage); break;
            }

            WindowSkin.Hint(info.HomePage);
        }

        GUILayout.Label(Lang.T("版本 {0}。仅提供视觉提示，不修改判定；运行日志见 UMM 的 Log.txt。", info.Version), WindowSkin.Note);
    }

    private void Copy(string text)
    {
        GUIUtility.systemCopyBuffer = text;
        _copied = text;
        _copiedUntil = Time.unscaledTime + 2f;
    }

    /// <summary>复制按钮的文字：刚复制过这一项的 2 秒内显示「已复制」。</summary>
    private string CopyLabel(string text, string label) =>
        _copied == text && Time.unscaledTime < _copiedUntil ? Lang.T("已复制") : label;
}
