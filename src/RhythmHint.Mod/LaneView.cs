using System.Collections.Generic;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>左侧四轨下落面板：不透明底色、节拍线、橙色单押 / 蓝色双押横条、判定线和按键框。</summary>
internal static class LaneView
{
    private const float BaseSpeed = 1100f;
    private const float FlashSeconds = 0.18f;

    public static void Draw(OverlayFrame f, OverlayLayout l, Settings s)
    {
        var panel = new Rect(0, l.TopHeight, l.PanelWidth, Screen.height - l.TopHeight);
        Painter.Rect(panel, Painter.PanelColor.WithAlpha(Mathf.Clamp01(s.PanelOpacity)));

        GUI.BeginGroup(panel);
        var judgeY = l.JudgeY - l.TopHeight;
        var speed = BaseSpeed * Mathf.Max(0.2f, s.LaneSpeed) * l.Scale;
        var lookAhead = (judgeY + (20 * l.Scale)) / speed;

        var holds = Holds.Active(f.Notes, f.NextNote, f.Now);
        DrawLaneBackground(l, judgeY);
        DrawBeats(f, l, judgeY, speed, lookAhead);
        DrawFlashes(f, l, judgeY);
        DrawActiveHolds(holds, l, judgeY, speed);
        DrawNotes(f, l, judgeY, speed, lookAhead);
        DrawJudgeAndKeys(f, l, judgeY, holds);
        GUI.EndGroup();
    }

    private static void DrawLaneBackground(OverlayLayout l, float judgeY)
    {
        var sep = new Color(0.2f, 0.2f, 0.22f);
        for (var i = 0; i <= Lanes.Count; i++)
        {
            Painter.Rect(new Rect(l.LaneX(i) - l.Scale, 0, 2 * l.Scale, judgeY), sep);
        }
    }

    private static void DrawBeats(OverlayFrame f, OverlayLayout l, float judgeY, float speed, double lookAhead)
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

            var y = judgeY - (float)(dt * speed);
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
            var color = (kv.Value.Chord ? Painter.Blue : Painter.Orange).WithAlpha(0.45f * a);
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
            var color = h.Note.IsChord ? Painter.Blue : Painter.Orange;
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
            var dt = note.Time - f.Now;
            if (dt > lookAhead)
            {
                break;
            }

            if (dt < -0.15)
            {
                continue; // 已经晚了很多还没按到（例如在等检查点），不再往按键框里画
            }

            var y = judgeY - (float)(dt * speed);
            var color = note.IsChord ? Painter.Blue : Painter.Orange;
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
                    Painter.Rect(box, (flash.Chord ? Painter.Blue : Painter.Orange).WithAlpha(0.7f * a), 6 * l.Scale);
                }
            }

            var held = holds.FindIndex(h => h.Lane == i);
            if (held >= 0)
            {
                // 长按没松开前按键框一直亮着
                Painter.Rect(box, (holds[held].Note.IsChord ? Painter.Blue : Painter.Orange).WithAlpha(0.55f), 6 * l.Scale);
            }

            Painter.Border(box, f.KeysDown[i] || held >= 0 ? Color.white : new Color(0.35f, 0.35f, 0.4f), 2 * l.Scale, 6 * l.Scale);
            if (i < f.KeyLabels.Length)
            {
                Painter.Text(box, f.KeyLabels[i], Mathf.RoundToInt(22 * l.Scale), new Color(0.85f, 0.85f, 0.9f), TextAnchor.MiddleCenter, true);
            }
        }
    }
}
