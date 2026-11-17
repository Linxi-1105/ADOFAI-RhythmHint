using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>顶部横条：左边 BPM 和剩余时间，右边太鼓式节奏条（圆形音符从右往左滚到判定圈）。</summary>
internal static class TopBarView
{
    private const float BaseSpeed = 650f;
    private const float FlashSeconds = 0.15f;

    public static void Draw(OverlayFrame f, OverlayLayout l, Settings s)
    {
        var bar = new Rect(0, 0, Screen.width, l.TopHeight);
        Painter.Rect(bar, Painter.PanelColor.WithAlpha(Mathf.Clamp01(s.PanelOpacity)));
        var trackLeft = 0f;
        if (s.ShowBpm)
        {
            DrawBpmBox(f, l);
            trackLeft = l.PanelWidth;
        }

        if (s.ShowTaiko)
        {
            DrawTrack(f, l, s, new Rect(trackLeft, 0, Screen.width - trackLeft, l.TopHeight));
        }

        Painter.Rect(new Rect(0, l.TopHeight - (2 * l.Scale), Screen.width, 2 * l.Scale), new Color(0.3f, 0.3f, 0.34f));
        if (!string.IsNullOrEmpty(f.Caption))
        {
            Painter.Text(new Rect(0, l.TopHeight + (6 * l.Scale), Screen.width - (16 * l.Scale), 24 * l.Scale), f.Caption,
                Mathf.RoundToInt(18 * l.Scale), new Color(1f, 0.85f, 0.4f), TextAnchor.MiddleRight, true);
        }
    }

    private static void DrawBpmBox(OverlayFrame f, OverlayLayout l)
    {
        var box = new Rect(0, 0, l.PanelWidth, l.TopHeight);
        Painter.Rect(box, Painter.BpmBoxColor);
        var right = box.width - (18 * l.Scale);
        Painter.Text(new Rect(0, 8 * l.Scale, right, 64 * l.Scale), Mathf.RoundToInt(f.Bpm).ToString(),
            Mathf.RoundToInt(56 * l.Scale), Color.white, TextAnchor.MiddleRight, true);
        Painter.Text(new Rect(0, 66 * l.Scale, right, 20 * l.Scale), "BPM",
            Mathf.RoundToInt(16 * l.Scale), new Color(0.85f, 0.9f, 1f), TextAnchor.MiddleRight, true);

        var left = System.Math.Max(0, f.EndTime - f.Now);
        var text = $"{(int)(left / 60)}:{(int)(left % 60):00}";
        Painter.Text(new Rect(0, 88 * l.Scale, right, 30 * l.Scale), text,
            Mathf.RoundToInt(24 * l.Scale), Color.white, TextAnchor.MiddleRight, true);
    }

    private static void DrawTrack(OverlayFrame f, OverlayLayout l, Settings s, Rect area)
    {
        GUI.BeginGroup(area);
        var cy = area.height / 2;
        var judgeX = 84 * l.Scale;
        var speed = BaseSpeed * Mathf.Max(0.2f, s.TaikoSpeed) * l.Scale;
        var lookAhead = (area.width - judgeX + (60 * l.Scale)) / speed;

        // 节拍线：小节线亮，普通拍暗
        for (var i = BeatGrid.FirstAtOrAfter(f.Beats, f.Now - 0.05); i < f.Beats.Count; i++)
        {
            var dt = f.Beats[i].Time - f.Now;
            if (dt > lookAhead)
            {
                break;
            }

            var x = judgeX + (float)(dt * speed);
            var measure = f.Beats[i].IsMeasure;
            var w = (measure ? 2f : 1f) * l.Scale;
            var c = measure ? new Color(0.38f, 0.38f, 0.42f) : new Color(0.2f, 0.2f, 0.23f);
            Painter.Rect(new Rect(x - (w / 2), 6 * l.Scale, w, area.height - (12 * l.Scale)), c);
        }

        DrawJudgeCircle(f, l, new Vector2(judgeX, cy));
        DrawActiveHolds(f, l, judgeX, cy, speed);
        DrawNotes(f, l, judgeX, cy, speed, lookAhead);

        Painter.Text(new Rect(judgeX - (40 * l.Scale), area.height - (24 * l.Scale), 80 * l.Scale, 20 * l.Scale),
            f.NextNote.ToString(), Mathf.RoundToInt(15 * l.Scale), new Color(0.75f, 0.75f, 0.8f));
        GUI.EndGroup();
    }

    private static void DrawJudgeCircle(OverlayFrame f, OverlayLayout l, Vector2 c)
    {
        Painter.Circle(c, 46 * l.Scale, new Color(0.16f, 0.16f, 0.18f));
        Painter.Ring(c, 46 * l.Scale, new Color(0.5f, 0.5f, 0.55f), 4 * l.Scale);
        Painter.Ring(c, 36 * l.Scale, new Color(0.32f, 0.32f, 0.36f), 3 * l.Scale);
        var a = 1f - ((Time.unscaledTime - f.TaikoFlash.Time) / FlashSeconds);
        if (a > 0)
        {
            var color = f.TaikoFlash.Chord ? Painter.Blue : Painter.Orange;
            Painter.Circle(c, 46 * l.Scale, color.WithAlpha(0.55f * a));
            Painter.Ring(c, (46 + (14 * (1 - a))) * l.Scale, color.WithAlpha(a), 4 * l.Scale);
        }
    }

    /// <summary>按住中的长按：圆停在判定圈上脉动，尾巴从右往左缩短，松开时消失。</summary>
    private static void DrawActiveHolds(OverlayFrame f, OverlayLayout l, float judgeX, float cy, float speed)
    {
        PlannedNote? drawn = null;
        var pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 18f));
        foreach (var h in Holds.Active(f.Notes, f.NextNote, f.Now))
        {
            if (ReferenceEquals(h.Note, drawn))
            {
                continue; // 双押的两条长按画成一个
            }

            drawn = h.Note;
            var r = (h.Note.IsChord ? 40 : 34) * l.Scale;
            var color = h.Note.IsChord ? Painter.Blue : Painter.Orange;
            var len = (float)(h.Remaining * speed);
            Painter.Rect(new Rect(judgeX, cy - (r * 0.55f), len, r * 1.1f), color.WithAlpha(0.8f), r * 0.55f);
            var center = new Vector2(judgeX, cy);
            Painter.Circle(center, r + (2 * l.Scale), new Color(0, 0, 0, 0.75f));
            Painter.Circle(center, r, Color.Lerp(color, Color.white, 0.35f * pulse));
            Painter.Ring(center, r, Color.white, 4 * l.Scale);
        }
    }

    private static void DrawNotes(OverlayFrame f, OverlayLayout l, float judgeX, float cy, float speed, double lookAhead)
    {
        // 先找出可见范围，再从远到近画，近的音符压在上面
        var last = f.NextNote;
        while (last < f.Notes.Count && f.Notes[last].Time - f.Now <= lookAhead)
        {
            last++;
        }

        for (var i = last - 1; i >= f.NextNote; i--)
        {
            var note = f.Notes[i];
            var dt = f.TimeTo(note);
            if (dt < -0.15)
            {
                continue;
            }

            var x = judgeX + (float)(dt * speed);
            var r = (note.IsChord ? 40 : 34) * l.Scale;
            var color = note.IsChord ? Painter.Blue : Painter.Orange;
            var hold = 0.0;
            foreach (var h in note.Lanes.Values)
            {
                hold = System.Math.Max(hold, h);
            }

            if (hold > 0)
            {
                var len = (float)(hold * speed);
                Painter.Rect(new Rect(x, cy - (r * 0.55f), len, r * 1.1f), color.WithAlpha(0.6f), r * 0.55f);
            }

            var center = new Vector2(x, cy);
            Painter.Circle(center, r + (2 * l.Scale), new Color(0, 0, 0, 0.75f));
            Painter.Circle(center, r, color);
            Painter.Ring(center, r, Color.white, 4 * l.Scale);
        }
    }
}
