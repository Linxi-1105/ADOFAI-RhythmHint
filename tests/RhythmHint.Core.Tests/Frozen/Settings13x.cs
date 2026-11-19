// 冻结副本，不能改：v1.3.0、v1.3.1 标签里 src/RhythmHint.Mod/Settings.cs 的公开字段（XmlSerializer 读写的就是这些），
// 用来测「现在写出的 Settings.xml，玩家降级回 1.3.x 也能整份读」。1.3.x 已经发布，它们的读法不会再变，这里也不跟着改。
// 只抄了字段：方法、注释、UMM 的基类不影响读写；排法枚举也抄成冻结的（1.3.x 只认这两个值，不跟着 Core 的 FingeringStyle 加值）；
// 交替优先参数的默认值在 1.3.x 里取自 FingeringOptions，这里写成当时的数。
using System.Xml.Serialization;

namespace RhythmHint.Core.Tests.Frozen;

/// <summary>1.3.x 的 RhythmHint.Core.FingeringStyle。</summary>
public enum FingeringStyle13x
{
    TimeReset,
    ReviewPreferenceV1,
}

/// <summary>v1.3.1 的 Settings（40 个字段）。</summary>
[XmlRoot("Settings")]
public sealed class Settings131
{
    public bool ShowPreview = true;
    public bool PreviewSound = true;
    public float PreviewVolume = 0.6f;
    public bool ShrinkGameView = true;
    public bool ShowLanes = true;
    public bool RoundNotes;
    public bool ShowTaiko = true;
    public bool ShowBpm = true;
    public bool ShowDifficulty = true;
    public bool LanesOnRight;
    public bool TaikoAtBottom;
    public float UiScale = 1f;
    public float LaneSpeed = 1f;
    public float TaikoSpeed = 1f;
    public float PanelOpacity = 0.92f;
    public int VisualOffsetMs;
    public string KeyLabels = "D F J K";
    public bool LightKeysOnPress = true;
    public string SettingsHotkey = "F8";
    public string Language = string.Empty;
    public string LatestVersion = string.Empty;
    public bool AutoUpdate = true;
    public int GuideVersion;
    public int ChordAngleDeg = 30;
    public int ResetWindowMs = 130;
    public int ChordFocusMs = 450;
    public int JackWindowMs = 150;
    public bool UseRoll = true;
    public bool OuterRoll = true;
    public FingeringStyle13x Style = FingeringStyle13x.TimeReset;
    public int DefaultsRevision;
    public int ReviewShortMinGapMs = 60;
    public float ReviewShortMaxGapMs = 82.5f;
    public int ReviewShortMinNotes = 4;
    public int ReviewShortMaxNotes = 5;
    public float ReviewTimingToleranceMs = 1f;
    public bool UseCustomFingering = true;
    public string FingeringChoices = string.Empty;
    public bool LeftHanded;
    public bool ChordsOnOtherHand;
}

/// <summary>v1.3.0 的 Settings（39 个字段：比 1.3.1 少 AutoUpdate）。</summary>
[XmlRoot("Settings")]
public sealed class Settings130
{
    public bool ShowPreview = true;
    public bool PreviewSound = true;
    public float PreviewVolume = 0.6f;
    public bool ShrinkGameView = true;
    public bool ShowLanes = true;
    public bool RoundNotes;
    public bool ShowTaiko = true;
    public bool ShowBpm = true;
    public bool ShowDifficulty = true;
    public bool LanesOnRight;
    public bool TaikoAtBottom;
    public float UiScale = 1f;
    public float LaneSpeed = 1f;
    public float TaikoSpeed = 1f;
    public float PanelOpacity = 0.92f;
    public int VisualOffsetMs;
    public string KeyLabels = "D F J K";
    public bool LightKeysOnPress = true;
    public string SettingsHotkey = "F8";
    public string Language = string.Empty;
    public string LatestVersion = string.Empty;
    public int GuideVersion;
    public int ChordAngleDeg = 30;
    public int ResetWindowMs = 130;
    public int ChordFocusMs = 450;
    public int JackWindowMs = 150;
    public bool UseRoll = true;
    public bool OuterRoll = true;
    public FingeringStyle13x Style = FingeringStyle13x.TimeReset;
    public int DefaultsRevision;
    public int ReviewShortMinGapMs = 60;
    public float ReviewShortMaxGapMs = 82.5f;
    public int ReviewShortMinNotes = 4;
    public int ReviewShortMaxNotes = 5;
    public float ReviewTimingToleranceMs = 1f;
    public bool UseCustomFingering = true;
    public string FingeringChoices = string.Empty;
    public bool LeftHanded;
    public bool ChordsOnOtherHand;
}
