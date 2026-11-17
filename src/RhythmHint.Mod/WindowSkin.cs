using System;
using UnityEngine;

namespace RhythmHint;

/// <summary>独立设置窗口的样式和常用控件（IMGUI）。深色底、橙色强调，和叠加层的配色一致。只能在 OnGUI 里用。</summary>
internal static class WindowSkin
{
    public const float LabelWidth = 300;

    private static readonly Color Background = new(0.09f, 0.09f, 0.11f, 0.97f);
    private static readonly Color Surface = new(0.15f, 0.15f, 0.18f, 1f);
    private static readonly Color SurfaceHover = new(0.22f, 0.22f, 0.26f, 1f);
    private static readonly Color Text = new(0.9f, 0.9f, 0.93f);
    private static readonly Color Muted = new(0.6f, 0.6f, 0.66f);

    private static bool _ready;

    public static GUIStyle Window { get; private set; } = null!;

    public static GUIStyle Title { get; private set; } = null!;

    public static GUIStyle Tab { get; private set; } = null!;

    public static GUIStyle TabActive { get; private set; } = null!;

    public static GUIStyle Section { get; private set; } = null!;

    public static GUIStyle Label { get; private set; } = null!;

    public static GUIStyle Note { get; private set; } = null!;

    public static GUIStyle Value { get; private set; } = null!;

    public static GUIStyle Button { get; private set; } = null!;

    /// <summary>屏幕键盘上已经分给别的按键框的键：普通底色、橙色字。</summary>
    public static GUIStyle KeyInUse { get; private set; } = null!;

    public static GUIStyle Toggle { get; private set; } = null!;

    /// <summary>第一次用时创建样式（要在 OnGUI 里，GUI.skin 才可用）。</summary>
    public static void Ensure()
    {
        if (_ready)
        {
            return;
        }

        _ready = true;
        Window = new GUIStyle(GUI.skin.box) { padding = new RectOffset(20, 20, 16, 16) };
        Window.normal.background = Solid(Background);
        Window.onNormal.background = Window.normal.background;

        Title = TextStyle(24, FontStyle.Bold, Text);
        Section = TextStyle(19, FontStyle.Bold, Painter.Orange);
        Section.margin = new RectOffset(0, 0, 14, 6);
        Label = TextStyle(17, FontStyle.Normal, Text);
        Note = TextStyle(15, FontStyle.Normal, Muted);
        Note.wordWrap = true;
        Value = TextStyle(17, FontStyle.Bold, Text);
        Value.alignment = TextAnchor.MiddleRight;

        Button = Boxy(GUI.skin.button, 16, Surface, SurfaceHover, Text);
        KeyInUse = Boxy(GUI.skin.button, 16, Surface, SurfaceHover, Painter.Orange);
        Tab = Boxy(GUI.skin.button, 18, Surface, SurfaceHover, Muted);
        TabActive = Boxy(GUI.skin.button, 18, Painter.Orange, Painter.Orange, Color.black);
        TabActive.fontStyle = FontStyle.Bold;

        Toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 17 };
        Toggle.normal.textColor = Text;
        Toggle.onNormal.textColor = Painter.Orange;
        Toggle.hover.textColor = Text;
        Toggle.onHover.textColor = Painter.Orange;
    }

    public static void SectionTitle(string text) => GUILayout.Label(text, Section);

    public static void Hint(string text) => GUILayout.Label(text, Note);

    public static bool Check(string text, bool value) => GUILayout.Toggle(value, "  " + text, Toggle);

    /// <summary>一行：名字、滑块、数值，可选的微调按钮（每次加减 step）。</summary>
    public static float Slider(string label, float value, float min, float max, string format, float step = 0)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, Label, GUILayout.Width(LabelWidth));
        if (step > 0 && GUILayout.Button("−", Button, GUILayout.Width(34)))
        {
            value -= step;
        }

        value = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true), GUILayout.MinWidth(140));
        if (step > 0 && GUILayout.Button("+", Button, GUILayout.Width(34)))
        {
            value += step;
        }

        GUILayout.Label(value.ToString(format), Value, GUILayout.Width(70));
        GUILayout.EndHorizontal();
        return Mathf.Clamp(value, min, max);
    }

    public static int Slider(string label, int value, int min, int max, int step = 0) =>
        Mathf.RoundToInt(Slider(label, (float)value, min, max, "0", step));

    /// <summary>一排按钮，返回被点的那个的下标，没点为 -1。</summary>
    public static int Buttons(params string[] labels)
    {
        var clicked = -1;
        GUILayout.BeginHorizontal();
        GUILayout.Space(LabelWidth);
        for (var i = 0; i < labels.Length; i++)
        {
            if (GUILayout.Button(labels[i], Button, GUILayout.MinWidth(56)))
            {
                clicked = i;
            }
        }

        GUILayout.EndHorizontal();
        return clicked;
    }

    /// <summary>
    /// 一排选项按钮，当前值高亮；返回新的值。当前值不在选项里（例如手改了设置文件）时也显示出来，保持不变。
    /// </summary>
    public static string Choice(string label, string value, string[] options)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, Label, GUILayout.Width(LabelWidth));
        if (System.Array.IndexOf(options, value) < 0)
        {
            GUILayout.Button(value, TabActive, GUILayout.MinWidth(56));
        }

        foreach (var option in options)
        {
            if (GUILayout.Button(option, option == value ? TabActive : Button, GUILayout.MinWidth(56)))
            {
                value = option;
            }
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        return value;
    }

    private static GUIStyle TextStyle(int size, FontStyle style, Color color)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = style, wordWrap = false };
        s.normal.textColor = color;
        return s;
    }

    private static GUIStyle Boxy(GUIStyle from, int size, Color normal, Color hover, Color text)
    {
        var s = new GUIStyle(from) { fontSize = size, padding = new RectOffset(12, 12, 7, 7) };
        s.normal.background = Solid(normal);
        s.hover.background = Solid(hover);
        s.active.background = Solid(hover);
        s.normal.textColor = text;
        s.hover.textColor = text == Muted ? Text : text;
        s.active.textColor = text;
        return s;
    }

    private static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    /// <summary>按 1080p 的设计尺寸缩放到当前屏幕。</summary>
    public static float ScreenScale => Math.Max(0.5f, Screen.height / 1080f);
}
