using System;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 独立设置窗口的样式和常用控件（IMGUI）。深色底、橙色强调，和叠加层的配色一致。只能在 OnGUI 里用。
/// 排版：每项一行，左边固定宽度的名字，右边控件；每行一样高、控件上下居中，行与行之间留空。
/// </summary>
internal static class WindowSkin
{
    /// <summary>名字一栏最窄（约 14 个汉字）和最宽的宽度。名字写短，解释放在下面一行。</summary>
    private const float MinLabelWidth = 250;
    private const float MaxLabelWidth = 380;

    private const float RowHeight = 36;
    private const float RowGap = 6;
    private const float ControlHeight = 30;
    private const float SwitchWidth = 50;
    private const float SwitchHeight = 26;

    private static readonly Color Background = new(0.09f, 0.09f, 0.11f, 0.97f);
    private static readonly Color Surface = new(0.15f, 0.15f, 0.18f, 1f);
    private static readonly Color SurfaceHover = new(0.22f, 0.22f, 0.26f, 1f);
    private static readonly Color Text = new(0.9f, 0.9f, 0.93f);
    private static readonly Color Muted = new(0.6f, 0.6f, 0.66f);
    private static readonly Color SwitchOff = new(0.3f, 0.3f, 0.34f);

    private static readonly GUIContent Measured = new();
    private static bool _ready;
    private static float _widest;

    /// <summary>
    /// 名字一栏的宽度：英文、韩文的名字常比中文长，按上一帧量到的最长名字加宽（在最窄和最宽之间），控件一栏跟着右移。
    /// 只在 Layout 事件里改（<see cref="BeginFrame"/>），同一帧的布局和绘制用同一个宽度。
    /// </summary>
    public static float LabelWidth { get; private set; } = MinLabelWidth;

    public static GUIStyle Window { get; private set; } = null!;

    public static GUIStyle Title { get; private set; } = null!;

    public static GUIStyle Tab { get; private set; } = null!;

    public static GUIStyle TabActive { get; private set; } = null!;

    public static GUIStyle Section { get; private set; } = null!;

    public static GUIStyle Label { get; private set; } = null!;

    public static GUIStyle Note { get; private set; } = null!;

    public static GUIStyle Value { get; private set; } = null!;

    public static GUIStyle Button { get; private set; } = null!;

    /// <summary>选中的选项（橙底黑字），和普通按钮一样大。</summary>
    public static GUIStyle Selected { get; private set; } = null!;

    /// <summary>屏幕键盘上已经分给别的按键框的键：普通底色、橙色字。</summary>
    public static GUIStyle KeyInUse { get; private set; } = null!;

    /// <summary>第一次用时创建样式（要在 OnGUI 里，GUI.skin 才可用）。</summary>
    public static void Ensure()
    {
        if (_ready)
        {
            return;
        }

        _ready = true;
        Window = new GUIStyle(GUI.skin.box) { padding = new RectOffset(24, 24, 18, 16) };
        Window.normal.background = Solid(Background);
        Window.onNormal.background = Window.normal.background;

        Title = TextStyle(24, FontStyle.Bold, Text);
        Section = TextStyle(19, FontStyle.Bold, Painter.Orange);
        Section.margin = new RectOffset(0, 0, 22, 8);
        Label = TextStyle(17, FontStyle.Normal, Text);
        Label.alignment = TextAnchor.MiddleLeft;
        Label.margin = new RectOffset(0, 8, 0, 0);
        Note = TextStyle(15, FontStyle.Normal, Muted);
        Note.wordWrap = true;
        Note.margin = new RectOffset(0, 0, 0, 8);
        Value = TextStyle(17, FontStyle.Bold, Text);
        Value.alignment = TextAnchor.MiddleRight;
        Value.margin = new RectOffset(8, 0, 0, 0);

        Tab = Boxy(GUI.skin.button, 18, Surface, SurfaceHover, Muted);
        TabActive = Boxy(GUI.skin.button, 18, Painter.Orange, Painter.Orange, Color.black);
        TabActive.fontStyle = FontStyle.Bold;

        // 行里的按钮固定高度、上下留出相同的边，正好在一行的正中
        Button = RowControl(Boxy(GUI.skin.button, 16, Surface, SurfaceHover, Text));
        Selected = RowControl(Boxy(GUI.skin.button, 16, Painter.Orange, Painter.Orange, Color.black));
        Selected.fontStyle = FontStyle.Bold;
        KeyInUse = RowControl(Boxy(GUI.skin.button, 16, Surface, SurfaceHover, Painter.Orange));
    }

    /// <summary>窗口每帧开头调用：用上一轮量到的最长名字定这一帧的名字栏宽度（换了界面语言后下一帧就对上）。</summary>
    public static void BeginFrame()
    {
        if (Event.current.type != EventType.Layout)
        {
            return;
        }

        LabelWidth = Mathf.Clamp(Mathf.Ceil(_widest) + 12, MinLabelWidth, MaxLabelWidth);
        _widest = 0;
    }

    public static void SectionTitle(string text) => GUILayout.Label(text, Section);

    public static void Hint(string text) => GUILayout.Label(text, Note);

    /// <summary>开始一行：左边是名字。和 <see cref="EndRow"/> 配对。</summary>
    public static void BeginRow(string label)
    {
        GUILayout.BeginHorizontal();
        NameCell(label);
    }

    /// <summary>一行左边的名字，顺便量它有多宽。</summary>
    private static void NameCell(string label)
    {
        Measure(label);
        GUILayout.Label(label, Label, GUILayout.Width(LabelWidth), GUILayout.Height(RowHeight));
    }

    /// <summary>记下这一帧最长的名字有多宽（下一帧的名字栏宽度按它定）。</summary>
    private static void Measure(string label)
    {
        Measured.text = label;
        _widest = Mathf.Max(_widest, Label.CalcSize(Measured).x);
    }

    /// <summary>结束一行，控件靠左，下面留一点空。</summary>
    public static void EndRow()
    {
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Space(RowGap);
    }

    /// <summary>一行：名字和开关（橙色、圆点在右为开）。点名字也能切换。</summary>
    public static bool Switch(string label, bool value)
    {
        GUILayout.BeginHorizontal();
        Measure(label);
        var clicked = GUILayout.Button(label, Label, GUILayout.Width(LabelWidth), GUILayout.Height(RowHeight));
        var cell = GUILayoutUtility.GetRect(SwitchWidth, RowHeight, GUILayout.Width(SwitchWidth), GUILayout.Height(RowHeight));
        var track = new Rect(cell.x, cell.y + ((RowHeight - SwitchHeight) / 2), SwitchWidth, SwitchHeight);
        clicked |= GUI.Button(track, GUIContent.none, GUIStyle.none);
        if (Event.current.type == EventType.Repaint)
        {
            Fill(track, value ? Painter.Orange : SwitchOff, SwitchHeight / 2);
            var size = SwitchHeight - 6;
            var knob = new Rect(value ? track.xMax - size - 3 : track.x + 3, track.y + 3, size, size);
            Fill(knob, Color.white, size / 2);
        }

        EndRow();
        return clicked ? !value : value;
    }

    /// <summary>一行：名字、滑块、数值，可选的微调按钮（每次加减 step）。</summary>
    public static float Slider(string label, float value, float min, float max, string format, float step = 0)
    {
        GUILayout.BeginHorizontal();
        NameCell(label);
        if (step > 0 && GUILayout.Button("−", Button, GUILayout.Width(34)))
        {
            value -= step;
        }

        // 滑块本身很矮，放进一行高的竖直组里上下居中
        GUILayout.BeginVertical(GUILayout.Height(RowHeight), GUILayout.ExpandWidth(true), GUILayout.MinWidth(140));
        GUILayout.FlexibleSpace();
        value = GUILayout.HorizontalSlider(value, min, max);
        GUILayout.FlexibleSpace();
        GUILayout.EndVertical();
        if (step > 0 && GUILayout.Button("+", Button, GUILayout.Width(34)))
        {
            value += step;
        }

        GUILayout.Label(value.ToString(format), Value, GUILayout.Width(64), GUILayout.Height(RowHeight));
        GUILayout.EndHorizontal();
        GUILayout.Space(RowGap);
        return Mathf.Clamp(value, min, max);
    }

    public static int Slider(string label, int value, int min, int max, int step = 0) =>
        Mathf.RoundToInt(Slider(label, (float)value, min, max, "0", step));

    /// <summary>一排按钮（和控件一栏对齐），返回被点的那个的下标，没点为 -1。</summary>
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
        GUILayout.Space(RowGap);
        return clicked;
    }

    /// <summary>
    /// 一排选项按钮，当前值高亮；返回新的值。当前值不在选项里（例如手改了设置文件）时也显示出来，保持不变。
    /// 选项多时每 perRow 个换一行，下一行和控件一栏对齐。
    /// </summary>
    public static string Choice(string label, string value, string[] options, int perRow = int.MaxValue)
    {
        BeginRow(label);
        if (Array.IndexOf(options, value) < 0)
        {
            GUILayout.Button(value, Selected, GUILayout.MinWidth(56));
        }

        for (var i = 0; i < options.Length; i++)
        {
            if (i > 0 && i % perRow == 0)
            {
                EndRow();
                GUILayout.BeginHorizontal();
                GUILayout.Space(LabelWidth);
            }

            if (GUILayout.Button(options[i], options[i] == value ? Selected : Button, GUILayout.MinWidth(56)))
            {
                value = options[i];
            }
        }

        EndRow();
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

    /// <summary>行里的按钮：固定高度，上下边距让它落在一行正中。</summary>
    private static GUIStyle RowControl(GUIStyle s)
    {
        var edge = Mathf.RoundToInt((RowHeight - ControlHeight) / 2);
        s.fixedHeight = ControlHeight;
        s.padding = new RectOffset(12, 12, 4, 4);
        s.margin = new RectOffset(3, 3, edge, edge);
        return s;
    }

    private static void Fill(Rect r, Color c, float radius) =>
        GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);

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
