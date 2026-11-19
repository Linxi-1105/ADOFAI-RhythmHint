using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>四轨面板放右边、太鼓条放下面时，游戏画面缩进剩下的那块（用户要求位置可选）。</summary>
public class ViewportMarginsTests
{
    [Fact]
    public void LanesOnTheRightAndTaikoAtTheBottom()
    {
        var r = ViewportMath.Shrink(new ViewRect(0, 0, 1, 1), new ScreenMargins(Left: 0, Right: 0.15, Top: 0, Bottom: 0.12));

        Assert.Equal(0, r.X, 6);
        Assert.Equal(0.12, r.Y, 6); // Unity 从左下角算：下面让出来就是往上挪
        Assert.Equal(0.85, r.Width, 6);
        Assert.Equal(0.88, r.Height, 6);
    }

    [Fact]
    public void OldLeftTopCallIsTheSameAsMargins()
    {
        var original = new ViewRect(0, 0.1, 1, 0.8);
        Assert.Equal(ViewportMath.Shrink(original, 0.2, 0.1), ViewportMath.Shrink(original, new ScreenMargins(0.2, 0, 0.1, 0)));
    }

    [Fact]
    public void ShrinkerUsesAllFourSides()
    {
        var rects = new Dictionary<string, ViewRect> { ["main"] = new ViewRect(0, 0, 1, 1) };
        var shrinker = new ViewportShrinker<string>(c => rects[c], (c, r) => rects[c] = r, _ => false, _ => true);

        shrinker.Apply(new[] { "main" }, new ScreenMargins(0.1, 0.2, 0.05, 0.15));

        Assert.Equal(new ViewRect(0.1, 0.15, 0.7, 0.8), rects["main"] with { Width = Math.Round(rects["main"].Width, 6), Height = Math.Round(rects["main"].Height, 6) });
        shrinker.Restore();
        Assert.Equal(new ViewRect(0, 0, 1, 1), rects["main"]);
    }
}
