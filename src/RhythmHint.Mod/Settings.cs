using RhythmHint.Core;
using UnityModManagerNet;

namespace RhythmHint;

/// <summary>保存在 Mods\RhythmHint\Settings.xml。字段必须是 public 才会被 UMM 存下来。</summary>
public sealed class Settings : UnityModManager.ModSettings
{
    /// <summary>设置页打开时播放演示。</summary>
    public bool ShowPreview = true;

    /// <summary>演示时每个音符到达播放一声「嗒」，用来校准画面偏移。</summary>
    public bool PreviewSound = true;

    public float PreviewVolume = 0.6f;

    /// <summary>
    /// 自适应游戏画幅：提示面板出现时，游戏画面和界面文字（连击数、判定、暂停菜单等）一起缩到面板以外的区域，面板收起时还原。
    /// 试验阶段界面文字曾是单独的 ShrinkGameUi 开关，合并后旧设置文件里的那个字段读的时候会被忽略。
    /// </summary>
    public bool ShrinkGameView = true;

    public bool ShowLanes = true;
    public bool ShowTaiko = true;
    public bool ShowBpm = true;

    /// <summary>整体缩放（在按屏幕高度 1080 换算的基础上再乘）。</summary>
    public float UiScale = 1f;

    /// <summary>下落速度倍率，1 = 每秒 1100 像素（1080p）。</summary>
    public float LaneSpeed = 1f;

    /// <summary>太鼓条滚动速度倍率，1 = 每秒 650 像素（1080p）。</summary>
    public float TaikoSpeed = 1f;

    /// <summary>面板底色不透明度，越高越不受游戏背景影响。</summary>
    public float PanelOpacity = 0.92f;

    /// <summary>画面整体提前（正数）或推后（负数），毫秒。只影响显示，不影响判定。</summary>
    public int VisualOffsetMs;

    /// <summary>四个按键框上显示的字，用空格隔开；也用来点亮按键框。</summary>
    public string KeyLabels = "D F J K";

    public bool LightKeysOnPress = true;

    /// <summary>打开 / 关闭独立设置窗口的按键（Unity 按键名）。</summary>
    public string SettingsHotkey = "F8";

    /// <summary>间隔小于它的两块砖画成一个双押（含伪双押）。</summary>
    /// <remarks>0.1.0 时叫 ChordWindowMs、默认 20，漏掉了官方关卡约 25 ms 的伪双押；改名让旧设置不再生效。</remarks>
    public int PseudoChordWindowMs = 35;

    // 1.0.0 起排键改成「时间重置法」，旧设置文件里的 UltraWindowMs、PhraseBreakMs、PatternRoll1243 等字段读的时候会被忽略

    /// <summary>主手重置间隔：比它慢右手食指连点，一半到它之间左右食指轮流，一半以内同手轮指。</summary>
    public int ResetWindowMs = 165;

    /// <summary>紧挨着双押、隔得不超过它的单押放另一只手，让按双押的手专心按双押。</summary>
    public int ChordFocusMs = 450;

    /// <summary>同一条轨道连续按两下至少隔这么久。</summary>
    public int JackWindowMs = 150;

    /// <summary>很快的地方同一只手两根手指轮指（四个键都用上）；关掉只用两根食指。</summary>
    public bool UseRoll = true;

    /// <summary>轮指时外侧手指先落（K J D F）；关掉是食指先落（J K F D）。</summary>
    public bool OuterRoll = true;

    /// <summary>左撇子：以左手为主手，整套排法左右镜像（D↔K、F↔J）。</summary>
    public bool LeftHanded;

    public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

    public FingeringOptions ToFingering() => new()
    {
        ChordWindow = PseudoChordWindowMs / 1000.0,
        ResetWindow = ResetWindowMs / 1000.0,
        ChordFocusWindow = ChordFocusMs / 1000.0,
        JackWindow = JackWindowMs / 1000.0,
        UseRoll = UseRoll,
        OuterRoll = OuterRoll,
        LeftHanded = LeftHanded,
    };

    /// <summary>排键相关设置的指纹，变了就要重排。</summary>
    public string FingeringKey =>
        $"{PseudoChordWindowMs}/{ResetWindowMs}/{ChordFocusMs}/{JackWindowMs}/{UseRoll}{OuterRoll}{LeftHanded}";

    // 设置窗口每一页的「恢复本页默认值」：默认值只写在上面的字段初始值里，这里从一个新实例抄回来

    /// <summary>「预览与校准」页：演示、提示音、画面偏移。</summary>
    public void ResetPreview()
    {
        var d = new Settings();
        ShowPreview = d.ShowPreview;
        PreviewSound = d.PreviewSound;
        PreviewVolume = d.PreviewVolume;
        VisualOffsetMs = d.VisualOffsetMs;
    }

    /// <summary>「显示」页：显示内容、自适应游戏画幅、尺寸与速度、按键框。</summary>
    public void ResetDisplay()
    {
        var d = new Settings();
        ShowLanes = d.ShowLanes;
        ShowTaiko = d.ShowTaiko;
        ShowBpm = d.ShowBpm;
        ShrinkGameView = d.ShrinkGameView;
        UiScale = d.UiScale;
        LaneSpeed = d.LaneSpeed;
        TaikoSpeed = d.TaikoSpeed;
        PanelOpacity = d.PanelOpacity;
        KeyLabels = d.KeyLabels;
        LightKeysOnPress = d.LightKeysOnPress;
    }

    /// <summary>「排键」页的参数。惯用手不动：那是玩家自己的习惯，不是调出来的参数。</summary>
    public void ResetFingering()
    {
        var d = new Settings();
        PseudoChordWindowMs = d.PseudoChordWindowMs;
        ResetWindowMs = d.ResetWindowMs;
        ChordFocusMs = d.ChordFocusMs;
        JackWindowMs = d.JackWindowMs;
        UseRoll = d.UseRoll;
        OuterRoll = d.OuterRoll;
    }

    /// <summary>「状态」页：打开设置窗口的快捷键。</summary>
    public void ResetHotkey() => SettingsHotkey = new Settings().SettingsHotkey;
}
