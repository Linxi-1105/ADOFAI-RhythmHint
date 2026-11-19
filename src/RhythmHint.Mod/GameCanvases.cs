using System.Collections.Generic;
using RhythmHint.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RhythmHint;

/// <summary>
/// 自适应游戏画幅的界面部分：让游戏（和其他 Mod）「屏幕空间 - 覆盖」的 uGUI 界面文字也缩进面板让出来的区域。
/// <para>
/// 覆盖模式的画布永远铺满整个屏幕、没法挪，所以面板出现时把根画布临时改成「世界空间」，整体搬到远离场景的地方，
/// 用自己的一台正交摄像机去拍，摄像机的显示区域正好是让出来的区域。
/// 界面按 s = min(区域宽 / 屏幕宽, 区域高 / 屏幕高) 等比缩小（字不变形）；
/// 画布当成一块「区域形状、宽高各除以 s」的虚拟屏幕（至少和真屏幕一样大，只有一个方向略大），
/// 于是贴顶 / 贴底 / 铺满的元素照样贴着区域的边、铺满区域，不会因为屏幕比例和区域比例不同而上下或左右留空。
/// 点击走这台摄像机换算，位置也对得上。
/// </para>
/// <para>
/// 点击谁先接到，要和原来一样（EventSystem 的排法：两边都有事件摄像机时先比摄像机 depth，
/// 再看覆盖模式画布的排序，非覆盖模式的一律排在覆盖模式后面，然后才比 sortingLayer、sortingOrder、远近）：
/// <list type="bullet">
/// <item>排序相同的覆盖模式画布看 Unity 的渲染顺序（renderOrder）；改成世界空间后改看离摄像机的远近。
/// 转换时按原来的渲染顺序给每块画布一个前后距离（后画的离摄像机近）。编辑器右下角的自动播放按钮（ottoCanvas，排序 0）
/// 播放中点不动，同为排序 0 的还有 tipCanvas、Error Meter，这样排后用户复测能点了。</item>
/// <item>覆盖模式的画布原本排在所有非覆盖模式的画布前面；改成世界空间后，要靠摄像机的 depth 比过别的画布的事件摄像机。
/// 所以 depth 不光高过所有开着的摄像机，也高过没转换的画布指定的摄像机（哪怕那台没开、不在 Camera.allCameras 里）。
/// 用户日志（1.2.0）：编辑器播放中 OverlayCanvas 上看不见的 NeoCosmosDLC 盖住全屏右下角 160×80，自动播放按钮只剩上沿（兔耳朵中间）点得到；
/// OverlayCanvas 不是覆盖模式、指定的摄像机不在 allCameras 里，这样算 depth 后整个按钮都能点了。</item>
/// </list>
/// </para>
/// 只动根画布的 renderMode / worldCamera、根节点的位置、大小、缩放，和 CanvasScaler 的 dynamicPixelsPerUnit；
/// 面板收起时改回覆盖模式（覆盖模式会重新接管根节点），dynamicPixelsPerUnit 改回原值。
/// 游戏期间自己改过模式或摄像机的画布保持游戏的值。IMGUI 画的文字（部分 Mod 用）不是画布，管不到。
/// </summary>
internal sealed class GameCanvases
{
    /// <summary>界面搬到的地方：远离场景，游戏的摄像机拍不到。</summary>
    private static readonly Vector3 Origin = new(0, -100000, 0);

    /// <summary>相邻两块画布的前后间距。摄像机在 Origin 前 10、近裁面 1，最多排 MaxLayers 块，最近的也在近裁面后面。</summary>
    private const float LayerGap = 0.05f;

    private const int MaxLayers = 150;

    private readonly Transform _own;
    private readonly Dictionary<Canvas, Converted> _converted = new();
    private readonly List<Canvas> _drop = new();
    private readonly List<Canvas> _fresh = new();
    private readonly List<RaycastResult> _hits = new();
    private readonly HashSet<string> _logged = new(); // 记过日志的画布名，同名的只记一次
    private readonly HashSet<string> _loggedHits = new(); // 记过日志的点击目标
    private Camera? _camera;
    private Vector2Int _screen;
    private Vector2 _virtual; // 虚拟屏幕大小（像素）
    private int _nextLayer;   // 下一块转换的画布排在第几层（越大离摄像机越近）

    /// <param name="own">自己的对象（挡板画布等），它下面的画布不动。</param>
    public GameCanvases(Transform own) => _own = own;

    /// <summary>是不是自己建的那台摄像机（游戏画面缩放时要跳过它）。</summary>
    public bool Owns(Camera cam) => _camera && cam == _camera;

    /// <param name="freed">四边被面板占掉的像素（四轨面板在左或右，太鼓条在上或下）。</param>
    /// <param name="gameCameras">游戏在用的摄像机：自己的摄像机要排在它们全部之后画（用 List 而不是接口，每帧遍历不装箱）。</param>
    public void Apply(ScreenMargins freed, List<Camera> gameCameras)
    {
        var screen = new Vector2Int(Screen.width, Screen.height);
        if (screen != _screen)
        {
            Restore(); // 分辨率变了：画布按新尺寸重新铺好再转
            _screen = screen;
            return;
        }

        float w = screen.x, h = screen.y;
        float areaW = w - (float)(freed.Left + freed.Right), areaH = h - (float)(freed.Top + freed.Bottom);
        var s = Mathf.Min(areaW / w, areaH / h);
        var size = new Vector2(areaW / s, areaH / s);

        var cam = EnsureCamera();
        cam.rect = new Rect((float)freed.Left / w, (float)freed.Bottom / h, areaW / w, areaH / h);
        cam.orthographicSize = size.y / 2;
        cam.transform.position = Origin + new Vector3(size.x / 2, size.y / 2, -10);

        if (size != _virtual)
        {
            // 面板滑入 / 滑出时每帧都变；停下来以后不再改，免得界面每帧重新排版
            _virtual = size;
            foreach (var kv in _converted)
            {
                if (kv.Key)
                {
                    Place(kv.Key, kv.Value);
                }
            }
        }

        var otherEventDepth = Scan(cam);

        // 和覆盖模式一样在所有摄像机之后画，压在它们画的内容上面；点击也排在别的画布前面
        var depth = otherEventDepth + 1;
        foreach (var other in gameCameras)
        {
            depth = Mathf.Max(depth, other.depth + 1);
        }

        cam.depth = Mathf.Max(depth, 0f);
        LogClickTarget();
    }

    public void Restore()
    {
        foreach (var kv in _converted)
        {
            var canvas = kv.Key;
            var scaler = kv.Value.Scaler;
            if (scaler && scaler!.dynamicPixelsPerUnit == kv.Value.Scale)
            {
                scaler.dynamicPixelsPerUnit = kv.Value.OriginalDynamicPixels;
            }

            if (StillOurs(canvas, _camera))
            {
                // 覆盖模式要到下一次画画面时才重新摆根节点；先手动摆回屏幕中间，免得这期间有人读到搬远后的位置
                canvas.transform.position = new Vector3(Screen.width / 2f, Screen.height / 2f, 0);
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
            }
        }

        _converted.Clear();
        _virtual = Vector2.zero;
        _nextLayer = 0;
        if (_camera)
        {
            _camera!.enabled = false;
        }
    }

    /// <summary>
    /// 每帧找一次：暂停菜单这类界面是按下按键时才显示的，晚一帧就会先全屏闪一下。所以不降频；
    /// FindObjectsByType 每次新建一个数组（几十个引用，一两百字节），Unity 没有不分配的版本，只能留着。
    /// 返回没转换的画布指定的摄像机里最高的 depth（没有时很小），自己的摄像机要高过它。
    /// </summary>
    private float Scan(Camera cam)
    {
        _drop.Clear();
        foreach (var canvas in _converted.Keys)
        {
            if (!StillOurs(canvas, cam))
            {
                _drop.Add(canvas); // 已销毁，或游戏自己改走了
            }
        }

        foreach (var canvas in _drop)
        {
            _converted.Remove(canvas);
        }

        _fresh.Clear();
        var otherEventDepth = float.MinValue;
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (_converted.ContainsKey(canvas) || !canvas.isRootCanvas || canvas.transform.IsChildOf(_own))
            {
                continue;
            }

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                _fresh.Add(canvas);
            }
            else if (canvas.worldCamera && canvas.worldCamera != cam)
            {
                otherEventDepth = Mathf.Max(otherEventDepth, canvas.worldCamera.depth);
            }
        }

        // 按覆盖模式下的渲染顺序依次排层；这次新出现的排在已转换的前面（离摄像机更近）
        _fresh.Sort((a, b) => a.renderOrder.CompareTo(b.renderOrder));
        foreach (var canvas in _fresh)
        {
            Convert(canvas, cam, Mathf.Min(_nextLayer++, MaxLayers));
        }

        var mask = 0;
        foreach (var canvas in _converted.Keys)
        {
            mask |= 1 << canvas.gameObject.layer;
        }

        cam.cullingMask = mask;
        return otherEventDepth;
    }

    private void Convert(Canvas canvas, Camera cam, int layer)
    {
        // 覆盖模式下根节点的缩放就是画布缩放，宽高乘缩放等于屏幕宽高。
        // 位置不从根节点读：覆盖模式要到画画面时才摆好它，刚还原或刚出现的画布读到的可能是旧值
        // （试验时就因为读到上次搬远后的位置又加了一次 Origin，暂停一次之后整层界面跑出镜头、字全没了）
        var rt = (RectTransform)canvas.transform;
        var scale = rt.localScale.x;
        if (scale <= 0 || Mathf.Abs((rt.sizeDelta.x * scale) - Screen.width) > 1)
        {
            scale = canvas.scaleFactor > 0 ? canvas.scaleFactor : 1f; // 根节点还没按屏幕摆过：用画布自己的缩放
        }

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam; // 点击用这台摄像机换算

        // 世界空间下 CanvasScaler 每帧把画布的 scaleFactor 设成 dynamicPixelsPerUnit（默认 1），
        // 旧式 Text 按它生成字形贴图：2560×1600 屏上本来按约 1.5 倍生成，变成 1 倍再放大显示就糊了（试验时暂停那一下字发虚）。
        // 所以暂时把它设成覆盖模式下的缩放，字照原来的清晰度生成
        var scaler = canvas.GetComponent<CanvasScaler>();
        var original = scaler ? scaler.dynamicPixelsPerUnit : 0f;
        if (scaler)
        {
            scaler.dynamicPixelsPerUnit = scale;
        }

        canvas.scaleFactor = scale; // CanvasScaler 下一帧才更新，这一帧先手动设好
        var converted = new Converted(scale, scaler ? scaler : null, original, layer);
        _converted[canvas] = converted;
        Place(canvas, converted);

        // 诊断：每种界面记一次，显示不对时对照日志查
        if (_logged.Add(canvas.name))
        {
            Main.Mod.Logger.Log($"自适应游戏画幅：缩放界面「{canvas.name}」（层 {canvas.gameObject.layer}，排序 {canvas.sortingLayerName}/{canvas.sortingOrder}，第 {layer} 层，缩放 {scale:0.###}）");
        }
    }

    /// <summary>把根节点摆成铺满虚拟屏幕：世界坐标 = 虚拟屏幕像素，搬到 Origin 那边，按层往摄像机挪一点。</summary>
    private void Place(Canvas canvas, Converted converted)
    {
        var rt = (RectTransform)canvas.transform;
        var scale = converted.Scale;

        // 根画布可能挂在别的对象下面：世界空间里父节点的缩放会乘上去，要除掉
        var parentScale = rt.parent ? rt.parent.lossyScale.x : 1f;
        var local = parentScale > 0 ? scale / parentScale : scale;

        var position = Origin + (Vector3)(_virtual / 2) + new Vector3(0, 0, -converted.Layer * LayerGap);
        rt.SetPositionAndRotation(position, Quaternion.identity);
        rt.localScale = new Vector3(local, local, local);
        rt.sizeDelta = _virtual / scale;
    }

    /// <param name="Scale">画布在覆盖模式下的缩放。</param>
    /// <param name="Scaler">画布上的 CanvasScaler（没有就是 null）。</param>
    /// <param name="OriginalDynamicPixels">CanvasScaler 原来的 dynamicPixelsPerUnit，还原用。</param>
    /// <param name="Layer">排在第几层，越大离摄像机越近（画在上面、先接点击）。</param>
    private readonly record struct Converted(float Scale, CanvasScaler? Scaler, float OriginalDynamicPixels, int Layer);

    /// <summary>
    /// 诊断：界面缩着时按下鼠标，记下点到的最上层界面元素、它所在的画布、最终响应这次点击的对象、两者在屏幕上的范围，
    /// 以及游戏输入模块记的鼠标位置（同一组「点到 → 响应」只记一次）。
    /// 按钮点不动、或要点偏一点才有反应时，对照日志看是被挡住了，还是点击范围 / 坐标对不上。
    /// </summary>
    private void LogClickTarget()
    {
        var events = EventSystem.current;
        if (!events || !Input.GetMouseButtonDown(0))
        {
            return;
        }

        var mouse = (Vector2)Input.mousePosition;
        _hits.Clear();
        events.RaycastAll(new PointerEventData(events) { position = mouse }, _hits);
        if (_hits.Count == 0)
        {
            return;
        }

        var hit = _hits[0].gameObject;
        var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
        if (!_loggedHits.Add($"{PathOf(hit)}→{PathOf(handler)}"))
        {
            return;
        }

        var module = events.currentInputModule;
        var pointer = module is CustomStandaloneInputModule standalone ? standalone.GetPointerData() : null;
        Main.Mod.Logger.Log(
            $"自适应游戏画幅：点击 {mouse}，落在「{PathOf(hit)}」（屏幕范围 {ScreenRectOf(hit)}，画布 {CanvasOf(hit)}），"
            + $"响应点击的是「{PathOf(handler)}」（屏幕范围 {ScreenRectOf(handler)}）；"
            + $"输入模块 {(module ? module.GetType().Name : "-")}，它记的鼠标位置 {(pointer != null ? pointer.position.ToString() : "?")}");
    }

    /// <summary>诊断用：「根画布/父对象/对象」，没有时是 -（只写日志，不翻译）。</summary>
    private static string PathOf(GameObject? go)
    {
        if (!go)
        {
            return "-";
        }

        var canvas = go!.GetComponentInParent<Canvas>();
        var root = canvas ? canvas.rootCanvas.transform : null;
        var parent = go.transform.parent;
        var middle = parent && parent != root ? $"{parent!.name}/" : string.Empty;
        return $"{(root ? root!.name : "?")}/{middle}{go.name}";
    }

    /// <summary>诊断用：对象所在根画布的模式、事件摄像机（名字和 depth）、排序（只写日志，不翻译）。</summary>
    private static string CanvasOf(GameObject go)
    {
        var canvas = go.GetComponentInParent<Canvas>();
        if (!canvas)
        {
            return "-";
        }

        var root = canvas.rootCanvas;
        var cam = root.worldCamera;
        return $"[{root.renderMode}, camera {(cam ? $"{cam!.name} depth {cam.depth}{(cam.enabled ? string.Empty : " off")}" : "-")}, "
            + $"order {root.sortingLayerName}/{root.sortingOrder}]";
    }

    /// <summary>诊断用：对象在屏幕上的范围 [x 左-右, y 下-上]（像素，左下角为原点），按它所在画布的摄像机换算。</summary>
    private static string ScreenRectOf(GameObject? go)
    {
        if (!go || go!.transform is not RectTransform rt)
        {
            return string.Empty;
        }

        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        var canvas = go.GetComponentInParent<Canvas>();
        var cam = canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        Vector2 min = cam ? cam!.WorldToScreenPoint(corners[0]) : corners[0];
        Vector2 max = cam ? cam!.WorldToScreenPoint(corners[2]) : corners[2];
        return $"[x {min.x:0}-{max.x:0}, y {min.y:0}-{max.y:0}]";
    }

    /// <summary>画布还在，而且还是我们改的样子（Unity 里已销毁的对象转成 bool 是 false）。</summary>
    private static bool StillOurs(Canvas canvas, Camera? cam) =>
        canvas && canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == cam;

    private Camera EnsureCamera()
    {
        if (!_camera)
        {
            var go = new GameObject("RhythmHint UI Camera");
            go.transform.SetParent(_own, false);
            _camera = go.AddComponent<Camera>();

            // 只清深度、不清颜色：世界空间的界面要做深度测试（覆盖模式不做），
            // 不清的话深度缓冲里还留着游戏画场景时写的值，界面会被判成「被挡住」整个不画（试验时打关时的字、准度条全没了）
            _camera.clearFlags = CameraClearFlags.Depth;
            _camera.orthographic = true;
            _camera.nearClipPlane = 1f;
            _camera.farClipPlane = 20f;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.useOcclusionCulling = false;
            _camera.cullingMask = 0;
        }

        _camera!.enabled = true;
        return _camera;
    }
}
