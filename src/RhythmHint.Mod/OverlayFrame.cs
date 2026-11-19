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

    /// <summary>本关的估算难度档位（1–60，见 DifficultyTiers）；0 表示不画。Load 时清零，换关后重新估。</summary>
    public int DifficultyTier;

    /// <summary>顶部条右侧的小字说明（演示模式用），画的时候按当前界面语言翻译；空表示不显示。</summary>
    public Message Caption = Message.Empty;

    public IReadOnlyList<PlannedNote> Notes = Array.Empty<PlannedNote>();
    public IReadOnlyList<BeatLine> Beats = Array.Empty<BeatLine>();
    public readonly Dictionary<int, (float Time, Color Color)> Flash = new();
    public (float Time, Color Color) TaikoFlash = (-10f, Painter.Orange);
    public readonly bool[] KeysDown = new bool[Lanes.Count];
    public string[] KeyLabels = Array.Empty<string>();

    private int _lastSeq = -1;

    /// <summary>换了一套音符（换关、重开、进出演示）时调用。</summary>
    public void Load(IReadOnlyList<PlannedNote> notes, IReadOnlyList<BeatLine> beats, double endTime)
    {
        Notes = notes;
        Beats = beats;
        EndTime = endTime;
        DifficultyTier = 0;
        Flash.Clear();
        _lastSeq = -1;
    }

    /// <summary>
    /// 砖块序号前进时，把刚经过的音符所在轨道点亮；倒退（检查点重来、演示循环）时重新定位。
    /// 伪多押由几块挨着的砖组成，都按过（LastTileIndex）才算这个提示过去。
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
                    Flash[lane] = (Time.unscaledTime, Painter.NoteColor(note));
                }

                TaikoFlash = (Time.unscaledTime, Painter.NoteColor(note));
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

    /// <summary>从 NextNote 往后，到达时间在 lookAhead 秒以内的音符到哪里为止（不含）。从远往近画时用。</summary>
    public int VisibleEnd(double lookAhead)
    {
        var end = NextNote;
        while (end < Notes.Count && Notes[end].Time - Now <= lookAhead)
        {
            end++;
        }

        return end;
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

/// <summary>
/// 按屏幕大小算出各部分的位置，基准是 1080p。四轨面板在左边或右边、太鼓条在上面或下面（设置里选，用户要求）；
/// 面板占满太鼓条以外的整个高度。坐标是 IMGUI 的：原点在左上角。
/// </summary>
internal readonly struct OverlayLayout
{
    public OverlayLayout(Settings s)
    {
        Scale = Screen.height / 1080f * Mathf.Clamp(s.UiScale, 0.5f, 2f);
        // 面板宽、太鼓条高取整到整数像素：边缘和游戏画面让出来的区域正好对齐，中间不留一列没人画的像素
        // （用户反馈「一直以来都漏画了几个像素」：小数宽度时摄像机视口取整，和面板之间空出一列）。
        // 轨道铺满整个面板，不再两侧各留 12 像素空边（同一次反馈：看着像轨道没画满）
        PanelWidth = Mathf.Round(68 * Lanes.Count * Scale);
        LaneWidth = PanelWidth / Lanes.Count;
        Padding = 0;
        TopHeight = s.ShowTaiko || s.ShowBpm ? Mathf.Round(128 * Scale) : 0;
        // 关掉太鼓条时横条整个收起（2026-11-18 用户），BPM 框只占四轨面板那一列的顶端（或底端）
        StripHeight = s.ShowTaiko ? TopHeight : 0;
        KeyBoxHeight = 64 * Scale;
        LanesRight = s.LanesOnRight;
        TaikoBottom = s.TaikoAtBottom;
        PanelX = LanesRight ? Screen.width - PanelWidth : 0;
        PanelY = TaikoBottom ? 0 : TopHeight;
        PanelHeight = Screen.height - TopHeight;
        BarY = TaikoBottom ? Screen.height - TopHeight : 0;
        JudgeY = PanelY + PanelHeight - KeyBoxHeight - (16 * Scale);
    }

    /// <summary>四轨面板在右边。</summary>
    public bool LanesRight { get; }

    /// <summary>太鼓条在下面。</summary>
    public bool TaikoBottom { get; }

    /// <summary>四轨面板左上角和高度。</summary>
    public float PanelX { get; }

    public float PanelY { get; }

    public float PanelHeight { get; }

    /// <summary>太鼓条（横条）的上沿；高度是 TopHeight。</summary>
    public float BarY { get; }

    public float Scale { get; }

    public float LaneWidth { get; }

    public float Padding { get; }

    public float PanelWidth { get; }

    /// <summary>太鼓条那一行的高度（太鼓条或 BPM 框开着时有）；四轨面板让出这么高。</summary>
    public float TopHeight { get; }

    /// <summary>横贯整个屏幕的太鼓条的高度（太鼓条关掉时为 0）；游戏画面只让出这么高。</summary>
    public float StripHeight { get; }

    public float KeyBoxHeight { get; }

    public float JudgeY { get; }

    public float LaneX(int lane) => Padding + (lane * LaneWidth);

    public float LaneCenter(int lane) => LaneX(lane) + (LaneWidth / 2);
}
