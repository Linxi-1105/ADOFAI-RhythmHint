using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 轨道预览按游戏里每块砖自己的进出方向画（不是把相邻砖的中心连起来）。
/// 复现（2026-11-18 用户，官方曲目 AR-X Libertas 第 321–337 块）：关卡用移动轨道把一些砖挪开了位置，
/// 连中心画出斜线和方框，星球绕行的圈也被撑大。
/// </summary>
public class PathAnglesTests
{
    private const double Step = 1.5;

    /// <summary>沿给定的方向（数学角）一块块往前走；moved 里的砖额外挪开一段（像移动轨道那样）。</summary>
    private static List<PathPoint> Walk(double[] directions, Func<double, double> toGame, ISet<int>? moved = null)
    {
        var points = new List<PathPoint>();
        double x = 0, y = 0;
        for (var i = 0; i <= directions.Length; i++)
        {
            var exit = i < directions.Length ? directions[i] : directions[i - 1];
            var entry = i > 0 ? directions[i - 1] + Math.PI : exit + Math.PI;
            var shift = moved != null && moved.Contains(i) ? 1.1 : 0;
            points.Add(new PathPoint(i, x + shift, y - shift, i * 0.25, true, toGame(entry), toGame(exit)));
            if (i < directions.Length)
            {
                x += Math.Cos(directions[i]) * Step;
                y += Math.Sin(directions[i]) * Step;
            }
        }

        return points;
    }

    private static double[] Turns()
    {
        var directions = new double[40];
        var angle = 0.0;
        for (var i = 0; i < directions.Length; i++)
        {
            angle += (i % 5) switch { 0 => Math.PI / 2, 2 => -Math.PI / 2, 3 => Math.PI / 4, _ => 0 };
            directions[i] = angle;
        }

        return directions;
    }

    private static double Diff(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 2 * Math.PI));

    [Fact]
    public void DetectsTheGamesAngleConventionAndKeepsMovedTilesShape()
    {
        var directions = Turns();
        var moved = new HashSet<int> { 7, 8, 9, 21 };
        var raw = Walk(directions, a => (Math.PI / 2) - a, moved); // 0 朝上、顺时针为正

        var result = PathAngles.Normalize(raw);

        Assert.True(result.Detected);
        for (var i = 0; i < directions.Length; i++)
        {
            Assert.True(Diff(result.Points[i].Exit, directions[i]) < 1e-6, $"第 {i} 块出方向");
            Assert.True(Diff(result.Points[i + 1].Entry, directions[i] + Math.PI) < 1e-6, $"第 {i + 1} 块进方向");
        }

        // 被挪开的砖位置不变，方向照游戏里的
        Assert.Equal(raw[8].X, result.Points[8].X);
    }

    [Fact]
    public void KeepsAnglesAlreadyInMathConvention()
    {
        var directions = Turns();
        var result = PathAngles.Normalize(Walk(directions, a => a));

        Assert.True(result.Detected);
        Assert.True(Diff(result.Points[3].Exit, directions[3]) < 1e-6);
    }

    [Fact]
    public void DropsAnglesThatDoNotMatchThePositions()
    {
        var random = new Random(5);
        var raw = Walk(Turns(), _ => random.NextDouble() * 2 * Math.PI);

        var result = PathAngles.Normalize(raw);

        Assert.False(result.Detected);
        Assert.All(result.Points, p => Assert.True(double.IsNaN(p.Entry) && double.IsNaN(p.Exit)));
    }

    [Fact]
    public void WithoutAnglesNothingChanges()
    {
        var plain = new List<PathPoint> { new(1, 0, 0, 0, true), new(2, 1.5, 0, 0.5, true), new(3, 3, 0, 1, true) };

        var result = PathAngles.Normalize(plain);

        Assert.False(result.Detected);
        Assert.Equal(plain, result.Points);
    }
}
