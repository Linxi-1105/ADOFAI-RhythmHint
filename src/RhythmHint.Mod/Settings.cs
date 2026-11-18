using System;
using RhythmHint.Core;
using UnityModManagerNet;

namespace RhythmHint;

/// <summary>保存在 Mods\RhythmHint\Settings.xml。字段必须是 public 才会被 UMM 存下来。</summary>
public sealed class Settings : UnityModManager.ModSettings
{
    private static readonly FingeringOptions ReviewDefaults = new();

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

    /// <summary>四轨的音符画成和顶部太鼓条一样的圆球，按键框换成判定圈；关掉是横条和方形按键框。</summary>
    public bool RoundNotes;

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

    /// <summary>
    /// 四个按键框对应的键，用空格隔开：Unity 按键名或符号本身（如 D、;、LeftShift、Space），没指定的写 None。
    /// 按键框上显示它（长名字缩写），按下时点亮。设置窗口里可选预设，或用屏幕键盘自定义。
    /// </summary>
    public string KeyLabels = "D F J K";

    public bool LightKeysOnPress = true;

    /// <summary>打开 / 关闭独立设置窗口的按键（Unity 按键名）。</summary>
    public string SettingsHotkey = "F8";

    /// <summary>界面语言代码（见 Core 的 Languages.All）；空 = 跟随游戏的语言。</summary>
    public string Language = string.Empty;

    /// <summary>UMM 查到过的最新版本号（见 UpdateNotice）；空 = 没查到过。设置窗口里不显示，恢复默认值也不清。</summary>
    public string LatestVersion = string.Empty;

    /// <summary>多押转角上限（度）：相邻两块砖转角不超过它、又挨得近，就合成一个多押（见 FingeringOptions.ChordAngle）。</summary>
    /// <remarks>
    /// 1.1.0 之前按时间合并（PseudoChordWindowMs，毫秒；更早叫 ChordWindowMs），分不清多押和极快的连打，
    /// 旧设置文件里的这两个字段读的时候会被忽略。
    /// </remarks>
    public int ChordAngleDeg = 30;

    // 1.0.0 起排键改成「时间重置法」，旧设置文件里的 UltraWindowMs、PhraseBreakMs、PatternRoll1243 等字段读的时候会被忽略

    /// <summary>主手重置间隔：比它慢右手食指连点，一半到它之间左右食指轮流，一半以内同手轮指。</summary>
    public int ResetWindowMs = 165;

    /// <summary>紧挨着多押、隔得不超过它的单押放另一只手，让按多押的手专心按多押。</summary>
    public int ChordFocusMs = 450;

    /// <summary>同一条轨道连续按两下至少隔这么久。</summary>
    public int JackWindowMs = 150;

    /// <summary>很快的地方同一只手两根手指轮指（四个键都用上）；关掉只用两根食指。</summary>
    public bool UseRoll = true;

    /// <summary>轮指时外侧手指先落（K J D F）；关掉是食指先落（J K F D）。</summary>
    public bool OuterRoll = true;

    /// <summary>排法。未配置时继续使用原时间重置法；旧实验开关不迁移成默认启用。</summary>
    public FingeringStyle Style = FingeringStyle.TimeReset;

    /// <summary>审核偏好短串的实验参数（毫秒、音符数），仅审核偏好排法使用。</summary>
    public int ReviewShortMinGapMs = (int)Math.Round(ReviewDefaults.ReviewShortMinGap * 1000);
    public float ReviewShortMaxGapMs = (float)(ReviewDefaults.ReviewShortMaxGap * 1000);
    public int ReviewShortMinNotes = ReviewDefaults.ReviewShortMinNotes;
    public int ReviewShortMaxNotes = ReviewDefaults.ReviewShortMaxNotes;
    public float ReviewTimingToleranceMs = (float)(ReviewDefaults.ReviewTimingTolerance * 1000);

    /// <summary>左撇子：以左手为主手，整套排法左右镜像（D↔K、F↔J）。</summary>
    public bool LeftHanded;

    /// <summary>
    /// 多押优先另一只手（玩家反馈：双押用手和惯用手分开设置；三押跟着它走）。存的是相对惯用手的选择：默认跟惯用手，
    /// 老设置文件读进来不变；换惯用手时多押跟着换边。
    /// </summary>
    public bool ChordsOnOtherHand;

    public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

    public FingeringOptions ToFingering() => new()
    {
        ChordAngle = ChordAngleDeg,
        ResetWindow = ResetWindowMs / 1000.0,
        ChordFocusWindow = ChordFocusMs / 1000.0,
        JackWindow = JackWindowMs / 1000.0,
        UseRoll = UseRoll,
        OuterRoll = OuterRoll,
        Style = Style,
        ReviewShortMinGap = ReviewShortMinGapMs / 1000.0,
        ReviewShortMaxGap = ReviewShortMaxGapMs / 1000.0,
        ReviewShortMinNotes = ReviewShortMinNotes,
        ReviewShortMaxNotes = ReviewShortMaxNotes,
        ReviewTimingTolerance = ReviewTimingToleranceMs / 1000.0,
        LeftHanded = LeftHanded,
        ChordsOnOtherHand = ChordsOnOtherHand,
    };

    /// <summary>排键相关设置的指纹，变了就要重排。</summary>
    public string FingeringKey =>
        $"{ChordAngleDeg}/{ResetWindowMs}/{ChordFocusMs}/{JackWindowMs}/{UseRoll}{OuterRoll}{LeftHanded}{ChordsOnOtherHand}"
        + $"/{Style}/{ReviewShortMinGapMs}/{ReviewShortMaxGapMs}/{ReviewShortMinNotes}/{ReviewShortMaxNotes}/{ReviewTimingToleranceMs}";

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
        RoundNotes = d.RoundNotes;
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

    /// <summary>「排键」页的参数。惯用手和多押用手不动：那是玩家自己的习惯，不是调出来的参数。</summary>
    public void ResetFingering()
    {
        var d = new Settings();
        ChordAngleDeg = d.ChordAngleDeg;
        ResetWindowMs = d.ResetWindowMs;
        ChordFocusMs = d.ChordFocusMs;
        JackWindowMs = d.JackWindowMs;
        UseRoll = d.UseRoll;
        OuterRoll = d.OuterRoll;
        Style = d.Style;
        ReviewShortMinGapMs = d.ReviewShortMinGapMs;
        ReviewShortMaxGapMs = d.ReviewShortMaxGapMs;
        ReviewShortMinNotes = d.ReviewShortMinNotes;
        ReviewShortMaxNotes = d.ReviewShortMaxNotes;
        ReviewTimingToleranceMs = d.ReviewTimingToleranceMs;
    }

    /// <summary>「状态」页：打开设置窗口的快捷键。</summary>
    public void ResetHotkey() => SettingsHotkey = new Settings().SettingsHotkey;
}
