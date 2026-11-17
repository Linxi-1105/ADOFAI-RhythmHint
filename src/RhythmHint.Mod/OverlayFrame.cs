using System;
using System.Collections.Generic;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>一帧的绘制数据：关卡模式和演示模式都往这里填，视图只读它。</summary>
internal sealed class OverlayFrame
{
    public double Now;
    public float Bpm;
    public double EndTime;
    public int NextNote;

    /// <summary>入场 / 出场动画进度，0..1。</summary>
    public float Presence;

    /// <summary>顶部条右侧的小字说明（演示模式用），空表示不显示。</summary>
    public string Caption = string.Empty;

    public IReadOnlyList<PlannedNote> Notes = Array.Empty<PlannedNote>();
    public IReadOnlyList<BeatLine> Beats = Array.Empty<BeatLine>();
    public readonly Dictionary<int, (float Time, bool Chord)> Flash = new();
    public (float Time, bool Chord) TaikoFlash = (-10f, false);
    public readonly bool[] KeysDown = new bool[Lanes.Count];
    public string[] KeyLabels = Array.Empty<string>();

    private int _lastSeq = -1;

    /// <summary>换了一套音符（换关、重开、进出演示）时调用。</summary>
    public void Load(IReadOnlyList<PlannedNote> notes, IReadOnlyList<BeatLine> beats, double endTime)
    {
        Notes = notes;
        Beats = beats;
        EndTime = endTime;
        Flash.Clear();
        _lastSeq = -1;
    }

    /// <summary>
    /// 砖块序号前进时，把刚经过的音符所在轨道点亮；倒退（检查点重来、演示循环）时重新定位。
    /// 伪双押由两块砖组成，两块都按过（LastTileIndex）才算这个提示过去。
    /// </summary>
    public void TrackHits(int seq)
    {
        if (seq < _lastSeq || _lastSeq < 0)
        {
            NextNote = FirstPending(seq);
        }
        else
        {
            while (NextNote < Notes.Count && Notes[NextNote].LastTileIndex <= seq)
            {
                var note = Notes[NextNote];
                foreach (var lane in note.Lanes.Keys)
                {
                    Flash[lane] = (Time.unscaledTime, note.IsChord);
                }

                TaikoFlash = (Time.unscaledTime, note.IsChord);
                NextNote++;
            }
        }

        _lastSeq = seq;
    }

    /// <summary>
    /// 音符离判定还有多少秒，画位置用。伪双押的第一块砖已经按到、还在等约 25 ms 后的第二块时算 0：
    /// 停在判定处，不越过去（越过看起来像漏按了）。
    /// </summary>
    public double TimeTo(PlannedNote note)
    {
        var dt = note.Time - Now;
        return dt < 0 && note.TileIndex <= _lastSeq ? 0 : dt;
    }

    private int FirstPending(int seq)
    {
        int lo = 0, hi = Notes.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (Notes[mid].LastTileIndex <= seq)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}

/// <summary>按屏幕大小算出各部分的位置，基准是 1080p。</summary>
internal readonly struct OverlayLayout
{
    public OverlayLayout(Settings s)
    {
        Scale = Screen.height / 1080f * Mathf.Clamp(s.UiScale, 0.5f, 2f);
        LaneWidth = 68 * Scale;
        Padding = 12 * Scale;
        PanelWidth = (LaneWidth * Lanes.Count) + (Padding * 2);
        TopHeight = s.ShowTaiko || s.ShowBpm ? 128 * Scale : 0;
        KeyBoxHeight = 64 * Scale;
        JudgeY = Screen.height - KeyBoxHeight - (16 * Scale);
    }

    public float Scale { get; }

    public float LaneWidth { get; }

    public float Padding { get; }

    public float PanelWidth { get; }

    public float TopHeight { get; }

    public float KeyBoxHeight { get; }

    public float JudgeY { get; }

    public float LaneX(int lane) => Padding + (lane * LaneWidth);
}
