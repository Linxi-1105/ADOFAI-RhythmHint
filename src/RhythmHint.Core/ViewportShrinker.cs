using System;
using System.Collections.Generic;

namespace RhythmHint.Core;

/// <summary>
/// 把直接画到屏幕上的摄像机缩到面板让出来的区域，面板收起时还原（游戏画面自适应缩放）。
/// 不依赖 Unity：摄像机怎么读写显示区域、是不是画到贴图上，由调用方用委托告诉它，方便单测。
/// <list type="bullet">
/// <item>每次都从「原始区域」算，不会越缩越小；游戏自己改了显示区域（例如加上下黑边）时以游戏的新值为原始值</item>
/// <item>画到贴图上的摄像机不缩；缩过之后游戏让它改画到贴图（关卡里的「先画到贴图再整体显示」）就马上还原，
/// 否则贴图里只有缩小的那块有画面，其余是没清过的显存，显示出来是彩色花屏</item>
/// </list>
/// </summary>
/// <typeparam name="TCamera">摄像机类型（游戏里是 UnityEngine.Camera）。</typeparam>
public sealed class ViewportShrinker<TCamera>
    where TCamera : class
{
    private readonly Func<TCamera, ViewRect> _getRect;
    private readonly Action<TCamera, ViewRect> _setRect;
    private readonly Func<TCamera, bool> _rendersToTexture;
    private readonly Func<TCamera, bool> _isAlive;
    private readonly Dictionary<TCamera, (ViewRect Original, ViewRect Applied)> _cameras = new();

    /// <param name="isAlive">摄像机还在（Unity 里已销毁的对象要跳过）。</param>
    public ViewportShrinker(Func<TCamera, ViewRect> getRect, Action<TCamera, ViewRect> setRect, Func<TCamera, bool> rendersToTexture, Func<TCamera, bool> isAlive)
    {
        _getRect = getRect;
        _setRect = setRect;
        _rendersToTexture = rendersToTexture;
        _isAlive = isAlive;
    }

    /// <param name="cameras">当前在用的摄像机（可以含画到贴图上的，会被跳过）。</param>
    /// <param name="left">左侧让出来的宽度占屏幕的比例。</param>
    /// <param name="top">顶部让出来的高度占屏幕的比例。</param>
    public void Apply(IEnumerable<TCamera> cameras, double left, double top)
    {
        Forget(cam => !_isAlive(cam));
        Forget(cam => _rendersToTexture(cam), restore: true);

        foreach (var cam in cameras)
        {
            if (!_isAlive(cam) || _rendersToTexture(cam))
            {
                continue;
            }

            var current = _getRect(cam);
            var original = _cameras.TryGetValue(cam, out var known) && known.Applied == current
                ? known.Original
                : current; // 第一次见到，或游戏刚改过它：以现在的值为原始值
            _setRect(cam, ViewportMath.Shrink(original, left, top));
            _cameras[cam] = (original, _getRect(cam)); // 读回来存：引擎可能把数值换成它自己的精度
        }
    }

    /// <summary>全部还原（面板收起、Mod 关掉时）。游戏在这期间改过的摄像机保持游戏的值。</summary>
    public void Restore() => Forget(_ => true, restore: true);

    private void Forget(Func<TCamera, bool> which, bool restore = false)
    {
        List<TCamera>? drop = null;
        foreach (var kv in _cameras)
        {
            if (!which(kv.Key))
            {
                continue;
            }

            if (restore && _isAlive(kv.Key) && _getRect(kv.Key) == kv.Value.Applied)
            {
                _setRect(kv.Key, kv.Value.Original);
            }

            (drop ??= new List<TCamera>()).Add(kv.Key);
        }

        if (drop is null)
        {
            return;
        }

        foreach (var cam in drop)
        {
            _cameras.Remove(cam);
        }
    }
}
