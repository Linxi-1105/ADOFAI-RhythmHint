using System.Collections.Generic;

namespace RhythmHint.Core;

/// <summary>四键模式下的轨道数。</summary>
public static class Lanes
{
    public const int Count = 4;

    /// <summary>轨道属于哪只手：0、1 左手，2、3 右手。</summary>
    public static int HandOf(int lane) => lane < 2 ? 0 : 1;

    /// <summary>食指所在的轨道：左手 1，右手 2。</summary>
    public static int IndexLane(int hand) => hand == 0 ? 1 : 2;

    /// <summary>同一只手的另一根手指：0↔1，2↔3。</summary>
    public static int Partner(int lane) => lane switch
    {
        0 => 1,
        1 => 0,
        2 => 3,
        _ => 2,
    };
}

/// <summary>关卡里一次需要按键的砖块。</summary>
/// <param name="TileIndex">砖块序号（scrFloor.seqID）。</param>
/// <param name="Time">到达时间，秒，和游戏的歌曲进度同一时间轴。</param>
/// <param name="HoldSeconds">长按砖的持续时间，普通砖为 0。</param>
public readonly record struct TileHit(int TileIndex, double Time, double HoldSeconds = 0);

/// <summary>最终画到屏幕上的一个音符。</summary>
public sealed class PlannedNote
{
    public PlannedNote(int tileIndex, double time)
    {
        TileIndex = tileIndex;
        LastTileIndex = tileIndex;
        Time = time;
    }

    /// <summary>这个音符的第一块砖。</summary>
    public int TileIndex { get; }

    /// <summary>这个音符的最后一块砖（伪双押由两块挨着的砖组成）。这块砖被按过后音符消失。</summary>
    public int LastTileIndex { get; set; }

    public double Time { get; }

    /// <summary>这一下要按的轨道及各自的长按时长（0 为普通音符）。</summary>
    public SortedDictionary<int, double> Lanes { get; } = new();

    /// <summary>同时按两个及以上轨道，画成蓝色。</summary>
    public bool IsChord => Lanes.Count > 1;
}
