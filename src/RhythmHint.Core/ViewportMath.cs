namespace RhythmHint.Core;

/// <summary>摄像机的显示区域（Unity Camera.rect，归一化坐标，原点在左下角）。</summary>
public readonly record struct ViewRect(double X, double Y, double Width, double Height);

/// <summary>提示面板出现时，把游戏画面缩到面板让出来的区域里（左侧四轨面板右边、顶部条下面）。</summary>
public static class ViewportMath
{
    /// <param name="original">游戏自己设的显示区域。</param>
    /// <param name="left">左侧被面板占掉的宽度占屏幕的比例。</param>
    /// <param name="top">顶部被占掉的高度占屏幕的比例。</param>
    public static ViewRect Shrink(ViewRect original, double left, double top)
    {
        var w = 1 - left;
        var h = 1 - top;
        return new ViewRect(left + (original.X * w), original.Y * h, original.Width * w, original.Height * h);
    }
}
