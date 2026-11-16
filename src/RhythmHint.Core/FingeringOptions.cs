namespace RhythmHint.Core;

/// <summary>排键参数（单位：秒）。对所有关卡一视同仁。</summary>
public sealed class FingeringOptions
{
    /// <summary>
    /// 间隔小于它算同时按，画成一个双押。包括伪双押：官方关卡常用两块相隔约 25–31 ms 的砖表示双押。
    /// 不能大于约 45 ms，否则 320 BPM 的 1/8 连打（47 ms）会被误合成双押。
    /// </summary>
    public double ChordWindow { get; set; } = 0.035;

    /// <summary>
    /// 主手重置间隔 R：和上一下隔得比它久，就回到右手食指（一根手指点得过来）；
    /// R/2 到 R 之间两只手的食指轮流；R/2 以内同一只手两根手指轮指。
    /// 默认 165 ms：原版「全节奏提示」作者交替 / 单指的分界约 160 ms，LaneReader 也用 165。
    /// </summary>
    public double ResetWindow { get; set; } = 0.165;

    /// <summary>
    /// 紧挨着双押（前一下或后一下就是双押）、隔得不超过它的单押放另一只手（双押在右手时放左手），让按双押的手专心按双押。
    /// 用户：右手单押接右手双押，食指、中指配合不过来。默认 450 ms：本机关卡里单押和双押共用一个键的地方多在 167–375 ms。
    /// </summary>
    public double ChordFocusWindow { get; set; } = 0.45;

    /// <summary>同一条轨道连续按两下（中间没有别的音符）至少隔这么久。</summary>
    public double JackWindow { get; set; } = 0.15;

    /// <summary>快到 R/2 以内时同一只手两根手指轮指（四个键都用上）；关掉就只用两根食指交替。</summary>
    public bool UseRoll { get; set; } = true;

    /// <summary>轮指时外侧手指先落（K J D F，用户习惯的 4312）；关掉是食指先落（J K F D）。</summary>
    public bool OuterRoll { get; set; } = true;

    /// <summary>
    /// 左撇子：以左手为主手，整套排法左右镜像（D↔K、F↔J）。慢的左手食指 F 连点、停下回左手、双押优先左手、
    /// 轮指 D F K J；双押同手、不出现食指→外侧手指这些约束镜像后照样成立。
    /// </summary>
    public bool LeftHanded { get; set; }
}
