using System.Collections.Generic;

namespace RhythmHint.Core;

/// <summary>
/// 设置页演示用的一小段节奏：16 拍一循环。开头 4 拍是每拍一下（第一拍双押），像节拍器一样方便对拍子校准；
/// 后面慢的、中速的、很快的、双押、长按都有一点，改参数时能马上看出效果。
/// </summary>
public sealed class DemoChart
{
    private DemoChart(double secondsPerBeat, IReadOnlyList<TileHit> tiles)
    {
        SecondsPerBeat = secondsPerBeat;
        Tiles = tiles;
    }

    public double SecondsPerBeat { get; }

    public double LoopSeconds => SecondsPerBeat * 16;

    public IReadOnlyList<TileHit> Tiles { get; }

    public static DemoChart Build(double bpm)
    {
        var spb = 60.0 / bpm;
        var tiles = new List<TileHit>();

        void Add(double beat, double holdBeats = 0) =>
            tiles.Add(new TileHit(tiles.Count + 1, beat * spb, holdBeats * spb));

        void Chord(double beat)
        {
            Add(beat);
            tiles.Add(new TileHit(tiles.Count + 1, (beat * spb) + 0.025, 0)); // 伪双押：两块砖相隔 25 ms
        }

        // 第 0–3 拍：每拍一下，第一拍双押（校准用）
        Chord(0);
        for (var b = 1; b < 4; b++)
        {
            Add(b);
        }

        // 第 4–5 拍：1/4 拍连打（中速，两只手食指轮流）
        for (var i = 0; i < 8; i++)
        {
            Add(4 + (i * 0.25));
        }

        // 第 6–7 拍：半拍
        for (var i = 0; i < 4; i++)
        {
            Add(6 + (i * 0.5));
        }

        // 第 8–9 拍：附点节奏
        foreach (var b in new[] { 8, 8.75, 9.5 })
        {
            Add(b);
        }

        // 第 10–11 拍：每半拍一个双押（右手）
        for (var i = 0; i < 4; i++)
        {
            Chord(10 + (i * 0.5));
        }

        // 第 12–13 拍：1/8 拍连打（够快，同一只手两根手指轮指 K J D F，四条轨道都用到）
        for (var i = 0; i < 16; i++)
        {
            Add(12 + (i * 0.125));
        }

        // 第 15 拍起长按大半拍（和前面隔开一拍多，回到右手）
        Add(15, holdBeats: 0.75);
        return new DemoChart(spb, tiles);
    }

    /// <summary>把演示节奏首尾相接重复几遍，序号连续，用来无缝循环播放。</summary>
    public static List<TileHit> Repeat(DemoChart demo, int times)
    {
        var all = new List<TileHit>(demo.Tiles.Count * times);
        for (var k = 0; k < times; k++)
        {
            foreach (var t in demo.Tiles)
            {
                all.Add(new TileHit(all.Count + 1, t.Time + (k * demo.LoopSeconds), t.HoldSeconds));
            }
        }

        return all;
    }
}
