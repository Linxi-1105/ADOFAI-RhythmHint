// 冻结副本，不能改：v1.3.2 标签里 src/RhythmHint.Core/SettingsFields.cs 的公开字段（XmlSerializer 读写的就是这些），
// 用来测「以后写出的 Settings.xml，玩家降级回 1.3.2 也能整份读」。写法同 Settings13x.cs：只抄字段，排法枚举用 1.3.x 冻结的那个，
// 交替优先参数的默认值写成当时的数。1.3.2 只比 1.3.1 多一项 ShowChordCount。
using System.Xml.Serialization;

namespace RhythmHint.Core.Tests.Frozen;

/// <summary>v1.3.2 的 Settings（41 个字段：比 1.3.1 多 ShowChordCount）。</summary>
[XmlRoot("Settings")]
public sealed class Settings132
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
    public bool ShowChordCount = true;
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
