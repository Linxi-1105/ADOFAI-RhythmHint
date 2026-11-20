using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

public class SmoothSongClockTests
{
    private const double Frame = 1.0 / 240;      // 240 Hz 显示器
    private const double DspBlock = 1024 / 48000.0; // Unity 在 Windows 上默认的混音缓冲，约 21 毫秒报一次音频时钟

    /// <summary>
    /// 照游戏 scrConductor.Update 的算法（2026-11-19 反编译 Unity 6000.3.25 版核实）出歌曲进度：
    /// 音频线程每混完一块缓冲报一次 AudioSettings.dspTime（报的时刻有几毫秒抖动）；
    /// 游戏每帧看它变了没有，变了就直接对齐，没变就加上这一帧的真实时间。
    /// extrapolate 为 false 时不往前推，就是原始的音频时钟（演示没有菜单音乐时用的）。
    /// </summary>
    private static List<(double Real, double Song)> SimulateGame(double seconds, double pitch = 1, bool extrapolate = true, int seed = 1)
    {
        var random = new Random(seed);
        var frames = new List<(double, double)>();
        var real = 0.0;
        var previousReal = 0.0;
        var reported = double.NaN;
        var dsp = 0.0;
        var block = 0;
        var blockAt = 0.0;
        while (real < seconds)
        {
            while (blockAt <= real)
            {
                block++;
                blockAt = (block * DspBlock) + (random.NextDouble() * 0.003);
            }

            var latest = (block - 1) * DspBlock;
            if (latest != reported)
            {
                reported = latest;
                dsp = latest;
            }
            else if (extrapolate)
            {
                dsp += real - previousReal;
            }

            previousReal = real;
            frames.Add((real, dsp * pitch));
            real += Frame + ((random.NextDouble() - 0.5) * 0.0006);
        }

        return frames;
    }

    /// <summary>每帧走的歌曲时间和这一帧的真实时间差多少（秒，已按播放速度换算），跳过开头 skip 帧。</summary>
    private static double WorstStepError(IReadOnlyList<(double Real, double Song)> frames, double pitch = 1, int skip = 60)
    {
        var worst = 0.0;
        for (var i = skip + 1; i < frames.Count; i++)
        {
            var step = (frames[i].Song - frames[i - 1].Song) / pitch;
            var real = frames[i].Real - frames[i - 1].Real;
            worst = Math.Max(worst, Math.Abs(step - real));
        }

        return worst;
    }

    private static List<(double Real, double Song)> Smooth(IEnumerable<(double Real, double Song)> source, double pitch = 1)
    {
        var clock = new SmoothSongClock();
        return source.Select(f => (f.Real, clock.Advance(f.Song, f.Real, pitch, running: true))).ToList();
    }

    [Fact]
    public void GameSongPosition_StepsUnevenlyOnHighRefreshDisplays()
    {
        // 复现：游戏的歌曲进度每次对齐音频时钟，那一帧多走或少走大半帧，提示看着像没跑满显示器帧数
        var game = SimulateGame(5);

        Assert.True(WorstStepError(game) > Frame * 0.5, $"最大误差 {WorstStepError(game) * 1000:F2} 毫秒");
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(0.6)]
    public void MovesEvenlyEveryFrame(double pitch)
    {
        var smooth = Smooth(SimulateGame(5, pitch), pitch);

        Assert.True(WorstStepError(smooth, pitch) < Frame * 0.1, $"最大误差 {WorstStepError(smooth, pitch) * 1000:F3} 毫秒");
    }

    [Fact]
    public void StaysOnTheGameTimelineOnAverage()
    {
        // 只是把跳动摊平，不改提示和判定的对应关系：平均和游戏进度一致，任何时候差不到几毫秒
        var game = SimulateGame(10);
        var smooth = Smooth(game);
        var diffs = game.Zip(smooth, (g, s) => s.Song - g.Song).Skip(240).ToList();

        Assert.True(Math.Abs(diffs.Average()) < 0.001, $"平均差 {diffs.Average() * 1000:F3} 毫秒");
        Assert.True(diffs.Max(Math.Abs) < 0.006, $"最大差 {diffs.Max(Math.Abs) * 1000:F3} 毫秒");
    }

    [Fact]
    public void SmoothsTheRawAudioClock()
    {
        // 设置页演示没有菜单音乐时直接用音频时钟，它约 21 毫秒才动一下
        var raw = SimulateGame(5, extrapolate: false);
        Assert.True(WorstStepError(raw) > Frame * 0.9);

        Assert.True(WorstStepError(Smooth(raw)) < Frame * 0.25, $"最大误差 {WorstStepError(Smooth(raw)) * 1000:F3} 毫秒");
    }

    [Fact]
    public void JumpsStraightToSeeks()
    {
        // 回检查点、重开：歌曲进度一下跳回去，画面跟着跳，不慢慢滑过去
        var clock = new SmoothSongClock();
        clock.Advance(50, 0, 1, running: true);
        clock.Advance(50 + Frame, Frame, 1, running: true);

        Assert.Equal(30.0, clock.Advance(30, 2 * Frame, 1, running: true));
        Assert.Equal(80.0, clock.Advance(80, 3 * Frame, 1, running: true));
    }

    [Fact]
    public void FollowsTheGameWhenNotRunningOrAfterAHitch()
    {
        var clock = new SmoothSongClock();
        clock.Advance(10, 0, 1, running: true);
        clock.Advance(10 + Frame, Frame, 1, running: true);

        // 暂停：直接用游戏进度
        Assert.Equal(10.003, clock.Advance(10.003, 2 * Frame, 1, running: false));

        // 卡了一下（一帧超过 0.1 秒）：直接对齐，不从卡之前的位置推
        Assert.Equal(10.2, clock.Advance(10.2, 0.3, 1, running: true));
    }

    [Fact]
    public void StopsWithTheGameWhenItsSongPositionStandsStill()
    {
        // 游戏进度停住但没报暂停：最多冲过去一点就回到游戏进度、停在那里，不来回抽动
        var clock = new SmoothSongClock();
        var real = 0.0;
        for (var i = 0; i < 100; i++, real += Frame)
        {
            clock.Advance(real, real, 1, running: true);
        }

        var stoppedAt = real - Frame;
        var shown = new List<double>();
        for (var i = 0; i < 100; i++, real += Frame)
        {
            shown.Add(clock.Advance(stoppedAt, real, 1, running: true));
        }

        Assert.True(shown.Max() - stoppedAt <= SmoothSongClock.MaxDrift);
        Assert.All(shown.Skip(20), s => Assert.Equal(stoppedAt, s));
    }
}
