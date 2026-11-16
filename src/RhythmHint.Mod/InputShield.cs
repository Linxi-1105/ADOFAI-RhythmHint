using UnityEngine;
using UnityEngine.UI;

namespace RhythmHint;

/// <summary>
/// 设置窗口底下垫的一块透明 uGUI 挡板，让游戏知道「鼠标按在界面上」，在窗口里点击、拖动不影响游戏：
/// <list type="bullet">
/// <item>游戏判断击打（scrPlayer）和编辑器点选时，都会先看 EventSystem.IsPointerOverGameObject()，按在界面上的点击不算</item>
/// <item>挡住窗口后面游戏自己的按钮</item>
/// <item>挂一个没有内容的 ScrollRect：编辑器发现滚轮停在可滚动的界面上时不缩放画面；它没有内容，什么都不滚</item>
/// </list>
/// 设置窗口是 IMGUI 画的，游戏的 uGUI 事件系统看不见它，所以需要这块挡板。挡板是自己建的对象，不改任何游戏对象。
/// </summary>
internal sealed class InputShield
{
    private readonly GameObject _root;
    private readonly RectTransform _area;

    public InputShield(Transform parent)
    {
        _root = new GameObject("RhythmHint InputShield");
        _root.transform.SetParent(parent, false);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; // 不加 CanvasScaler：单位就是屏幕像素
        canvas.sortingOrder = short.MaxValue;              // 盖在游戏所有界面上面（IMGUI 画的窗口本身仍在最上层）
        _root.AddComponent<GraphicRaycaster>();

        var area = new GameObject("Area");
        area.transform.SetParent(_root.transform, false);
        _area = area.AddComponent<RectTransform>();
        _area.anchorMin = _area.anchorMax = _area.pivot = new Vector2(0, 1); // 从左上角量，和 IMGUI 一致
        var image = area.AddComponent<Image>();
        image.color = Color.clear;
        image.raycastTarget = true;
        area.AddComponent<ScrollRect>();
        _root.SetActive(false);
    }

    /// <param name="screenRect">要挡住的区域（屏幕像素，左上角为原点）；null 表示不挡。</param>
    public void Cover(Rect? screenRect)
    {
        if (screenRect is not { } r)
        {
            if (_root.activeSelf)
            {
                _root.SetActive(false);
            }

            return;
        }

        _area.anchoredPosition = new Vector2(r.x, -r.y);
        _area.sizeDelta = new Vector2(r.width, r.height);
        if (!_root.activeSelf)
        {
            _root.SetActive(true);
        }
    }
}
