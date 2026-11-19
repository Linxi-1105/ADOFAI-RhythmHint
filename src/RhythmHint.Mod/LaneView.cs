using System.Collections.Generic;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 左侧四轨下落面板：不透明底色、节拍线、橙色单押 / 蓝色双押 / 紫色三押和四押音符，判定线和按键框。
/// 音符有两种样式：横条（配方形按键框），或圆形（和顶部太鼓条一样的球，配判定圈，判定点在判定圈圆心）。
/// </summary>
internal static class LaneView
{
    private const float BaseSpeed = 1100f;
    private const float FlashSeconds = 0.18f;

    // 圆形样式的尺寸（1080p 像素）：轨道宽 68，判定圈比球大一圈，和太鼓条一样
    private const float BallRadius = 25f;
    private const float JudgeRadius = 29f;
    private const float BallRim = 3f;
    private const float JudgeBottomGap = 18f; // 判定圈下沿到面板底边
    private const float HoldBreath = 6f;      // 圆形长按按住时光晕一呼一吸的角速度（约 1 次/秒），不用横条那种快闪

    private static readonly Color JudgeLine = new(0.28f, 0.28f, 0.32f);

    public static void Draw(OverlayFrame f, OverlayLayout l, Settings s)
    {
        var panel = new Rect(l.PanelX, l.PanelY, l.PanelWidth, l.PanelHeight);
        Painter.Rect(panel, Painter.PanelColor.WithAlpha(Mathf.Clamp01(s.PanelOpacity)));

        GUI.BeginGroup(panel);
        var judgeY = l.JudgeY - l.PanelY;
        var bottom = panel.height;

        // 音符到达的位置：横条是判定线；圆形是判定圈圆心，判定圈下面离面板底边留一点空
        var hitY = s.RoundNotes ? bottom - ((JudgeRadius + JudgeBottomGap) * l.Scale) : judgeY;
        var speed = BaseSpeed * Mathf.Max(0.2f, s.LaneSpeed) * l.Scale;
        var lookAhead = (hitY + (40 * l.Scale)) / speed;

        var holds = Holds.Active(f.Notes, f.NextNote, f.Now);
        DrawLaneBackground(l, s.RoundNotes ? bottom : judgeY); // 圆形：轨道一直通到底，判定圈落在轨道里
        DrawBeats(f, l, hitY, speed, lookAhead);
        if (s.RoundNotes)
        {
            DrawJudgeCircles(f, l, hitY, holds);
            DrawBallHolds(holds, l, hitY, speed);
            DrawBallNotes(f, l, hitY, speed, lookAhead);
        }
        else
        {
            DrawFlashes(f, l, judgeY);
            DrawActiveHolds(holds, l, judgeY, speed);
            DrawNotes(f, l, judgeY, speed, lookAhead);
            DrawJudgeAndKeys(f, l, judgeY, holds);
        }

        GUI.EndGroup();
    }

    private static void DrawLaneBackground(OverlayLayout l, float height)
    {
        var sep = new Color(0.2f, 0.2f, 0.22f);
        var w = 2 * l.Scale;
        for (var i = 0; i <= Lanes.Count; i++)
        {
            // 最外侧两条贴着面板边画在里面（轨道铺满面板，居中画会有一半在面板外被裁掉）
            var x = Mathf.Clamp(l.LaneX(i) - l.Scale, 0, l.PanelWidth - w);
            Painter.Rect(new Rect(x, 0, w, height), sep);
        }
    }

    private static void DrawBeats(OverlayFrame f, OverlayLayout l, float hitY, float speed, double lookAhead)
    {
        var width = l.LaneWidth * Lanes.Count;
        for (var i = BeatGrid.FirstAtOrAfter(f.Beats, f.Now); i < f.Beats.Count; i++)
        {
            var beat = f.Beats[i];
            var dt = beat.Time - f.Now;
            if (dt > lookAhead)
            {
                break;
            }

            var y = hitY - (float)(dt * speed);
            var h = (beat.IsMeasure ? 2f : 1f) * l.Scale;
            var c = beat.IsMeasure ? new Color(0.36f, 0.36f, 0.4f) : new Color(0.2f, 0.2f, 0.23f);
            Painter.Rect(new Rect(l.Padding, y - (h / 2), width, h), c);
        }
    }

    private static void DrawFlashes(OverlayFrame f, OverlayLayout l, float judgeY)
    {
        foreach (var kv in f.Flash)
        {
            var a = 1f - ((Time.unscaledTime - kv.Value.Time) / FlashSeconds);
            if (a <= 0)
            {
                continue;
            }

            // 判定线处最亮，往上渐渐淡出
            var color = kv.Value.Color.WithAlpha(0.45f * a);
            var h = 200 * l.Scale;
            Painter.FadeUp(new Rect(l.LaneX(kv.Key), judgeY - h, l.LaneWidth, h), color);
        }
    }

    /// <summary>
    /// 按住中的长按：长条下端钉在判定线上、从下往上被逐渐吃掉，轨道持续发亮并随时间轻微脉动，
    /// 判定线上留一个发光的头，松开时自然消失。
    /// </summary>
    private static void DrawActiveHolds(List<ActiveHold> holds, OverlayLayout l, float judgeY, float speed)
    {
        var inset = 6 * l.Scale;
        var pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 18f));
        foreach (var h in holds)
        {
            var color = Painter.NoteColor(h.Note);
            var x = l.LaneX(h.Lane);
            var glowH = 200 * l.Scale;
            Painter.FadeUp(new Rect(x, judgeY - glowH, l.LaneWidth, glowH), color.WithAlpha(0.28f + (0.12f * pulse)));

            var w = l.LaneWidth - (inset * 2);
            var tail = (float)(h.Remaining * speed);
            Painter.Rect(new Rect(x + inset + (w * 0.25f), judgeY - tail, w * 0.5f, tail), color.WithAlpha(0.85f), 3 * l.Scale);
            Painter.Rect(new Rect(x + inset + (w * 0.42f), judgeY - tail, w * 0.16f, tail), Color.white.WithAlpha(0.55f), 2 * l.Scale);

            var headH = 16 * l.Scale;
            var head = new Rect(x + inset, judgeY - (headH / 2), w, headH);
            Painter.Rect(head, Color.Lerp(color, Color.white, 0.35f * pulse), 4 * l.Scale);
            Painter.Border(head, Color.white, 2 * l.Scale, 4 * l.Scale);
        }
    }

    private static void DrawNotes(OverlayFrame f, OverlayLayout l, float judgeY, float speed, double lookAhead)
    {
        var barH = 16 * l.Scale;
        var inset = 6 * l.Scale;
        var outline = new Color(1f, 1f, 1f, 0.85f);
        for (var i = f.NextNote; i < f.Notes.Count; i++)
        {
            var note = f.Notes[i];
            var dt = f.TimeTo(note);
            if (dt > lookAhead)
            {
                break;
            }

            if (dt < -0.15)
            {
                continue; // 已经晚了很多还没按到（例如在等检查点），不再往按键框里画
            }

            var y = judgeY - (float)(dt * speed);
            var color = Painter.NoteColor(note);
            foreach (var kv in note.Lanes)
            {
                var x = l.LaneX(kv.Key) + inset;
                var w = l.LaneWidth - (inset * 2);
                if (kv.Value > 0)
                {
                    var tail = (float)(kv.Value * speed);
                    Painter.Rect(new Rect(x + (w * 0.25f), y - tail, w * 0.5f, tail), color.WithAlpha(0.6f), 3 * l.Scale);
                }

                var bar = new Rect(x, y - (barH / 2), w, barH);
                Painter.Rect(bar, color, 4 * l.Scale);
                Painter.Border(bar, outline, 2 * l.Scale, 4 * l.Scale);
            }
        }
    }

    private static void DrawJudgeAndKeys(OverlayFrame f, OverlayLayout l, float judgeY, List<ActiveHold> holds)
    {
        var width = l.LaneWidth * Lanes.Count;
        Painter.Rect(new Rect(l.Padding, judgeY - (4 * l.Scale), width, 8 * l.Scale), new Color(0, 0, 0, 0.7f));
        Painter.Rect(new Rect(l.Padding, judgeY - (2 * l.Scale), width, 4 * l.Scale), Color.white);

        for (var i = 0; i < Lanes.Count; i++)
        {
            var box = new Rect(l.LaneX(i) + (4 * l.Scale), judgeY + (10 * l.Scale), l.LaneWidth - (8 * l.Scale), l.KeyBoxHeight - (12 * l.Scale));
            var fill = f.KeysDown[i] ? new Color(0.3f, 0.3f, 0.34f) : new Color(0.14f, 0.14f, 0.16f);
            Painter.Rect(box, fill, 6 * l.Scale);
            if (f.Flash.TryGetValue(i, out var flash))
            {
                var a = 1f - ((Time.unscaledTime - flash.Time) / FlashSeconds);
                if (a > 0)
                {
                    Painter.Rect(box, flash.Color.WithAlpha(0.7f * a), 6 * l.Scale);
                }
            }

            var held = holds.FindIndex(h => h.Lane == i);
            if (held >= 0)
            {
                // 长按没松开前按键框一直亮着
                Painter.Rect(box, (Painter.NoteColor(holds[held].Note)).WithAlpha(0.55f), 6 * l.Scale);
            }

            Painter.Border(box, f.KeysDown[i] || held >= 0 ? Color.white : new Color(0.35f, 0.35f, 0.4f), 2 * l.Scale, 6 * l.Scale);
            if (i < f.KeyLabels.Length)
            {
                Painter.FitText(box, f.KeyLabels[i], Mathf.RoundToInt(22 * l.Scale), new Color(0.85f, 0.85f, 0.9f), true);
            }
        }
    }

    /// <summary>
    /// 圆形样式的判定区（代替判定线和按键框）：一条细线串起四个判定圈；命中、长按时判定圈周围亮一圈柔光（不用横条那种往上的光柱）；
    /// 判定圈按下对应键时变亮，命中时闪一下，圈里写键名。
    /// </summary>
    private static void DrawJudgeCircles(OverlayFrame f, OverlayLayout l, float hitY, List<ActiveHold> holds)
    {
        var r = JudgeRadius * l.Scale;
        var rim = BallRim * l.Scale;
        Painter.Rect(new Rect(l.Padding, hitY - l.Scale, l.LaneWidth * Lanes.Count, 2 * l.Scale), JudgeLine);

        // 光晕先全部画完再画判定圈，免得后一条轨道的光盖住前一个判定圈
        var breath = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * HoldBreath));
        foreach (var h in holds)
        {
            var color = Painter.NoteColor(h.Note);
            Painter.Glow(new Vector2(l.LaneCenter(h.Lane), hitY), r * 2, color.WithAlpha(0.3f + (0.2f * breath)));
        }

        foreach (var kv in f.Flash)
        {
            var a = 1f - ((Time.unscaledTime - kv.Value.Time) / FlashSeconds);
            if (a > 0)
            {
                var color = kv.Value.Color;
                Painter.Glow(new Vector2(l.LaneCenter(kv.Key), hitY), r * 2.2f, color.WithAlpha(0.7f * a));
            }
        }

        for (var i = 0; i < Lanes.Count; i++)
        {
            var c = new Vector2(l.LaneCenter(i), hitY);
            var flash = f.Flash.TryGetValue(i, out var hit) ? 1f - ((Time.unscaledTime - hit.Time) / FlashSeconds) : 0f;
            BallSkin.Judge(c, r, rim, f.KeysDown[i], flash, hit.Color);
            if (i < f.KeyLabels.Length)
            {
                var label = new Rect(c.x - (r * 0.8f), c.y - (r * 0.8f), r * 1.6f, r * 1.6f);
                Painter.FitText(label, f.KeyLabels[i], Mathf.RoundToInt(20 * l.Scale), new Color(0.85f, 0.85f, 0.9f), true);
            }
        }
    }

    /// <summary>
    /// 圆形样式按住中的长按：胶囊下端钉在判定圈上、上端随时间往下缩，松开时消失；
    /// 球停在判定圈上、稳定地亮一档（不闪），表示正按着。按下前后胶囊颜色不变，不会突然一跳。
    /// </summary>
    private static void DrawBallHolds(List<ActiveHold> holds, OverlayLayout l, float hitY, float speed)
    {
        var r = BallRadius * l.Scale;
        var rim = BallRim * l.Scale;
        foreach (var h in holds)
        {
            var color = Painter.NoteColor(h.Note);
            var x = l.LaneCenter(h.Lane);
            BallSkin.HoldBody(x, hitY, hitY - (float)(h.Remaining * speed), r, color, rim);
            BallSkin.Note(new Vector2(x, hitY), r, Color.Lerp(color, Color.white, 0.3f), rim);
        }
    }

    /// <summary>圆形样式的音符（同太鼓条）：从远到近画，近的压在上面；长按是和球一样宽的胶囊。</summary>
    private static void DrawBallNotes(OverlayFrame f, OverlayLayout l, float hitY, float speed, double lookAhead)
    {
        var r = BallRadius * l.Scale;
        for (var i = f.VisibleEnd(lookAhead) - 1; i >= f.NextNote; i--)
        {
            var note = f.Notes[i];
            var dt = f.TimeTo(note);
            if (dt < -0.15)
            {
                continue; // 已经晚了很多还没按到（例如在等检查点），不再往判定圈里画
            }

            var y = hitY - (float)(dt * speed);
            var color = Painter.NoteColor(note);
            foreach (var kv in note.Lanes)
            {
                var x = l.LaneCenter(kv.Key);
                if (kv.Value > 0)
                {
                    BallSkin.HoldBody(x, y, y - (float)(kv.Value * speed), r, color, BallRim * l.Scale);
                }

                BallSkin.Note(new Vector2(x, y), r, color, BallRim * l.Scale);
            }
        }
    }
}
