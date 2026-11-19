using System.Linq;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 自适应游戏画幅：提示面板出现时，把游戏画面（所有直接画到屏幕上的摄像机）缩到面板让出来的区域，面板收起时还原。
/// 具体规则（不越缩越小、游戏改了以游戏为准、改画到贴图的马上还原）见 Core 的 ViewportShrinker。
/// 「屏幕空间 - 覆盖」的界面文字交给 GameCanvases 一起缩。
/// </summary>
internal sealed class GameViewport
{
    private readonly ViewportShrinker<Camera> _shrinker = new(
        cam => new ViewRect(cam.rect.x, cam.rect.y, cam.rect.width, cam.rect.height),
        (cam, r) => cam.rect = ToRect(r),
        cam => cam.targetTexture != null,
        cam => cam); // Unity 里已销毁的对象转成 bool 是 false

    private readonly GameCanvases _canvases;

    /// <param name="own">Mod 自己的对象：界面文字用的摄像机挂在它下面。</param>
    public GameViewport(Transform own) => _canvases = new GameCanvases(own);

    /// <summary>被让出来的四边（像素），用来在面板下面铺黑底。</summary>
    public ScreenMargins Freed { get; private set; }

    /// <param name="shrinkUi">这一帧界面文字跟着缩（在关卡里，或刚离开关卡、面板还在滑出）。</param>
    public void Apply(float presence, Settings settings, bool shrinkUi)
    {
        if (!settings.ShrinkGameView || presence <= 0 || Screen.width <= 0 || Screen.height <= 0)
        {
            Restore();
            return;
        }

        var layout = new OverlayLayout(settings);
        // 让出来的区域取整到整数像素，和面板边缘对齐（不留缝）
        var lanes = Mathf.Round((settings.ShowLanes ? layout.PanelWidth : 0f) * presence);
        var bar = Mathf.Round(layout.StripHeight * presence);
        Freed = new ScreenMargins(layout.LanesRight ? 0 : lanes, layout.LanesRight ? lanes : 0, layout.TaikoBottom ? 0 : bar, layout.TaikoBottom ? bar : 0);
        var f = Freed;
        var cameras = Camera.allCameras.Where(cam => !_canvases.Owns(cam)).ToList();
        _shrinker.Apply(cameras, new ScreenMargins(f.Left / Screen.width, f.Right / Screen.width, f.Top / Screen.height, f.Bottom / Screen.height));

        if (shrinkUi)
        {
            _canvases.Apply(f, cameras);
        }
        else
        {
            _canvases.Restore();
        }
    }

    public void Restore()
    {
        _shrinker.Restore();
        _canvases.Restore();
        Freed = default;
    }

    private static Rect ToRect(ViewRect r) => new((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);
}
