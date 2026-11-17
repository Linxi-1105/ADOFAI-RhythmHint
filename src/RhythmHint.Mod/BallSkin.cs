using UnityEngine;

namespace RhythmHint;

/// <summary>圆形音符和判定圈的画法：顶部太鼓条和四轨的「圆形」样式共用。只能在 OnGUI 里调用。</summary>
internal static class BallSkin
{
    private static readonly Color Shadow = new(0, 0, 0, 0.75f);
    private static readonly Color JudgeFill = new(0.16f, 0.16f, 0.18f);
    private static readonly Color JudgeFillLit = new(0.3f, 0.3f, 0.34f);
    private static readonly Color JudgeRim = new(0.5f, 0.5f, 0.55f);
    private static readonly Color JudgeInnerRim = new(0.32f, 0.32f, 0.36f);

    /// <summary>一个音符：外面一圈黑色阴影，实心，白边。</summary>
    /// <param name="rim">白边粗细；阴影比球大半个白边。</param>
    public static void Note(Vector2 center, float r, Color color, float rim)
    {
        Painter.Circle(center, r + (rim / 2), Shadow);
        Painter.Circle(center, r, color);
        Painter.Ring(center, r, Color.white, rim);
    }

    /// <summary>按住中的长按头：停在判定圈上，随 pulse（0–1）脉动变亮。</summary>
    public static void HoldHead(Vector2 center, float r, Color color, float rim, float pulse) =>
        Note(center, r, Color.Lerp(color, Color.white, 0.35f * pulse), rim);

    /// <summary>
    /// 四轨（竖直）的长按身体：和球一样宽的胶囊，从按下的位置（球心 headY）通到松开的位置（上端圆头的圆心 endY）。
    /// 实心、比音符颜色暗一档，描一圈音符颜色的边：不透明，节拍线和分隔线不会透过来。
    /// </summary>
    public static void HoldBody(float x, float headY, float endY, float r, Color color, float rim)
    {
        var w = 2 * (r - (rim / 2));
        var body = new Rect(x - (w / 2), endY - (w / 2), w, headY - endY + (w / 2));
        var radius = Mathf.Min(body.width, body.height) / 2; // 很短的长按（快松开时）高度不到宽度，圆角不能超过一半高
        Painter.Rect(body, Color.Lerp(color, Painter.PanelColor, 0.55f), radius);
        Painter.Border(body, color, rim, radius);
    }

    /// <summary>太鼓条（横向）长按的尾巴：圆头的粗条，宽度是球直径的 0.55 倍。</summary>
    public static void Tail(Rect r, Color color) => Painter.Rect(r, color, Mathf.Min(r.width, r.height) / 2);

    /// <summary>
    /// 判定圈：深色底、灰色外圈和内圈。按下对应的键时（lit）底色变亮、外圈变白；
    /// 命中时（flash 从 1 降到 0）整圈染上音符颜色，外圈向外扩散。
    /// </summary>
    public static void Judge(Vector2 center, float r, float rim, bool lit, float flash, Color flashColor)
    {
        Painter.Circle(center, r, lit ? JudgeFillLit : JudgeFill);
        Painter.Ring(center, r, lit ? Color.white : JudgeRim, rim);
        Painter.Ring(center, r * 36f / 46f, JudgeInnerRim, rim * 0.75f);
        if (flash > 0)
        {
            Painter.Circle(center, r, flashColor.WithAlpha(0.55f * flash));
            Painter.Ring(center, r + (r * 14f / 46f * (1 - flash)), flashColor.WithAlpha(flash), rim);
        }
    }
}
