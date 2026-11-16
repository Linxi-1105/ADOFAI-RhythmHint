using UnityEngine;

namespace RhythmHint;

/// <summary>IMGUI 画图的小工具：圆角矩形、圆、描边、渐变、文字。只能在 OnGUI 里调用。</summary>
internal static class Painter
{
    private static GUIStyle? _text;
    private static Texture2D? _fadeUp;

    public static readonly Color Orange = new(0.96f, 0.47f, 0.16f);
    public static readonly Color Blue = new(0.24f, 0.62f, 0.88f);
    public static readonly Color PanelColor = new(0.07f, 0.07f, 0.08f);
    public static readonly Color BpmBoxColor = new(0.11f, 0.30f, 0.58f);

    /// <summary>整体透明度（入场 / 出场动画用），乘到每次绘制的颜色上。</summary>
    public static float Alpha { get; set; } = 1f;

    public static void Rect(Rect r, Color c, float radius = 0f)
    {
        GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Fade(c), 0f, radius);
    }

    public static void Border(Rect r, Color c, float width, float radius = 0f)
    {
        GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Fade(c), width, radius);
    }

    /// <summary>下面实、往上渐渐透明的矩形（打击闪光用）。</summary>
    public static void FadeUp(Rect r, Color c)
    {
        GUI.DrawTexture(r, FadeUpTexture(), ScaleMode.StretchToFill, true, 0f, Fade(c), 0f, 0f);
    }

    public static void Circle(Vector2 center, float radius, Color c)
    {
        Rect(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), c, radius);
    }

    public static void Ring(Vector2 center, float radius, Color c, float width)
    {
        Border(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), c, width, radius);
    }

    public static void Text(Rect r, string text, int size, Color c, TextAnchor anchor = TextAnchor.MiddleCenter, bool bold = false)
    {
        _text ??= new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow };
        _text.fontSize = Mathf.Max(1, size);
        _text.alignment = anchor;
        _text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        _text.normal.textColor = Fade(c);
        GUI.Label(r, text, _text);
    }

    public static Color WithAlpha(this Color c, float a) => new(c.r, c.g, c.b, a);

    private static Color Fade(Color c) => new(c.r, c.g, c.b, c.a * Alpha);

    /// <summary>1×64 的竖向渐变贴图：底部不透明，往上按平方曲线淡出。Unity 贴图第 0 行在底部。</summary>
    private static Texture2D FadeUpTexture()
    {
        if (_fadeUp != null)
        {
            return _fadeUp;
        }

        const int h = 64;
        _fadeUp = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (var y = 0; y < h; y++)
        {
            var a = 1f - (y / (float)(h - 1));
            _fadeUp.SetPixel(0, y, new Color(1, 1, 1, a * a));
        }

        _fadeUp.Apply();
        return _fadeUp;
    }
}
