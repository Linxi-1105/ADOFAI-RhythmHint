namespace RhythmHint.Core;

/// <summary>摄像机的显示区域（Unity Camera.rect，归一化坐标，原点在左下角）。</summary>
public readonly record struct ViewRect(double X, double Y, double Width, double Height);

/// <summary>面板占掉的屏幕边距（占屏幕宽、高的比例）：四轨面板在左边或右边，太鼓条在上面或下面。</summary>
public readonly record struct ScreenMargins(double Left, double Right, double Top, double Bottom);

/// <summary>提示面板出现时，把游戏画面缩到面板让出来的区域里（四轨面板旁边、太鼓条上面或下面）。</summary>
public static class ViewportMath
{
    /// <param name="original">游戏自己设的显示区域。</param>
    /// <param name="left">左侧被面板占掉的宽度占屏幕的比例。</param>
    /// <param name="top">顶部被占掉的高度占屏幕的比例。</param>
    public static ViewRect Shrink(ViewRect original, double left, double top) => Shrink(original, new ScreenMargins(left, 0, top, 0));

    /// <param name="original">游戏自己设的显示区域（Unity Camera.rect，原点在左下角）。</param>
    /// <param name="margins">四边被面板占掉的比例。</param>
    public static ViewRect Shrink(ViewRect original, ScreenMargins margins)
    {
        var w = 1 - margins.Left - margins.Right;
        var h = 1 - margins.Top - margins.Bottom;
        return new ViewRect(margins.Left + (original.X * w), margins.Bottom + (original.Y * h), original.Width * w, original.Height * h);
    }
}
