using System;
using System.Collections.Generic;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 设置页的演示：循环播放一段内置节奏，所有设置改了立刻能看到效果。
/// <list type="bullet">
/// <item>主菜单 / 选关界面有音乐时，演示跟着菜单音乐的拍子走（用游戏自己的歌曲进度和 BPM），
/// 音符落到判定线时应正好踩在拍子上，可以直接检查校准</item>
/// <item>没有音乐时用内置 160 BPM，每个音符按音频时钟播放一声「嗒」做参照</item>
/// </list>
/// </summary>
internal sealed class DemoPlayer
{
    private const int Loops = 3;
    private const double ScheduleAhead = 0.2;
    private const double FallbackBpm = 160;

    private readonly AudioSource[] _sources;
    private DemoChart _demo = DemoChart.Build(FallbackBpm);
    private double[] _tileTimes = Array.Empty<double>();
    private int _nextSource;
    private string _builtFor = string.Empty;
    private bool _running;
    private bool _followingMusic;
    private double _startDsp;
    private int _clickIndex;
    private int _clickLoop;

    public DemoPlayer(GameObject host)
    {
        var click = MakeClick();
        _sources = new AudioSource[6];
        for (var i = 0; i < _sources.Length; i++)
        {
            var s = host.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.ignoreListenerPause = true; // 游戏暂停时演示也要有声音
            s.clip = click;
            _sources[i] = s;
        }
    }

    /// <summary>当前是否跟着主菜单音乐（日志和状态显示用）。</summary>
    public string Mode => _followingMusic ? $"跟随菜单音乐，{60 / _demo.SecondsPerBeat:0.#} BPM" : $"内置节拍，{FallbackBpm:0} BPM";

    /// <param name="sound">是否放嗒声（演示正在滑出时不放）。</param>
    public void Tick(OverlayFrame frame, Settings settings, bool sound)
    {
        var music = TryMenuMusic(out var songPos, out var bpm);
        if (!_running || music != _followingMusic)
        {
            _running = true;
            _followingMusic = music;
            _startDsp = AudioSettings.dspTime + 0.1;
            _clickIndex = 0;
            _clickLoop = 0;
            _builtFor = string.Empty;
        }

        Rebuild(frame, settings, music ? bpm : FallbackBpm);

        double songNow;
        if (music)
        {
            songNow = songPos;
        }
        else
        {
            // 用音频时钟驱动画面，保证画面和嗒声在同一条时间轴上
            songNow = AudioSettings.dspTime - _startDsp;
            if (sound && settings.PreviewSound)
            {
                ScheduleClicks(songNow, settings.PreviewVolume);
            }
            else
            {
                SkipClicks(songNow);
            }
        }

        // 永远显示三份演示里的中间一份，前后都有音符，循环无缝；第 0 拍对齐歌曲的第 0 拍
        var loop = _demo.LoopSeconds;
        var shown = loop + (((songNow % loop) + loop) % loop);
        frame.Now = shown + (settings.VisualOffsetMs / 1000.0);
        frame.EndTime = shown + loop;
        frame.TrackHits(CountReached(frame.Now));
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        foreach (var s in _sources)
        {
            s.Stop();
        }
    }

    /// <summary>主菜单 / 选关界面正在放音乐时，读游戏的歌曲进度和 BPM。</summary>
    private static bool TryMenuMusic(out double songPosition, out double bpm)
    {
        songPosition = 0;
        bpm = 0;
        var conductor = ADOBase.conductor;
        if (conductor == null || conductor.song == null || !conductor.song.isPlaying || conductor.bpm <= 0)
        {
            return false;
        }

        songPosition = conductor.songposition_minusi;
        bpm = conductor.bpm;
        return true;
    }

    /// <summary>BPM 或排键参数变了就重排演示谱，改完立刻能看到新键位。</summary>
    private void Rebuild(OverlayFrame frame, Settings settings, double bpm)
    {
        var key = $"{bpm:0.###}/{settings.FingeringKey}";
        if (key == _builtFor)
        {
            return;
        }

        _builtFor = key;
        _demo = DemoChart.Build(bpm);
        var tiles = DemoChart.Repeat(_demo, Loops);
        _tileTimes = tiles.ConvertAll(t => t.Time).ToArray();
        var beats = BeatGrid.Build(new[] { (0.0, _demo.SecondsPerBeat) }, 0, _demo.LoopSeconds * Loops);
        var notes = NotePlanner.Plan(tiles, settings.ToFingering());
        frame.Load(notes, beats, _demo.LoopSeconds * Loops);
        frame.Bpm = (float)bpm;
        frame.Caption = _followingMusic
            ? $"演示 · 跟随当前音乐 {bpm:0.#} BPM · 每轮前 4 拍逐拍出现音符，音符到达判定线时应与节拍一致，如有偏差请调整「画面偏移」"
            : $"演示 · {bpm:0} BPM · 请对照提示音调整「画面偏移」";
        _clickIndex = 0;
        _clickLoop = 0;
    }

    /// <summary>已经到达判定线的砖块数（演示里每块砖都「准时被按到」）。</summary>
    private int CountReached(double now)
    {
        var i = Array.BinarySearch(_tileTimes, now);
        return i >= 0 ? i + 1 : ~i;
    }

    private void ScheduleClicks(double elapsed, float volume)
    {
        while (true)
        {
            var at = NextClickTime();
            if (at > elapsed + ScheduleAhead)
            {
                return;
            }

            if (at >= elapsed)
            {
                var source = _sources[_nextSource];
                _nextSource = (_nextSource + 1) % _sources.Length;
                source.volume = Mathf.Clamp01(volume);
                source.PlayScheduled(_startDsp + at);
            }

            Advance();
        }
    }

    private void SkipClicks(double elapsed)
    {
        while (NextClickTime() < elapsed + ScheduleAhead)
        {
            Advance();
        }
    }

    private double NextClickTime() => _demo.Tiles[_clickIndex].Time + (_clickLoop * _demo.LoopSeconds);

    private void Advance()
    {
        _clickIndex++;
        if (_clickIndex >= _demo.Tiles.Count)
        {
            _clickIndex = 0;
            _clickLoop++;
        }
    }

    /// <summary>
    /// 生成一声短促的「嗒」：2 kHz 正弦，指数衰减，30 ms。
    /// 用读取回调填数据，不用 SetData：Unity 6 的 SetData 有 ReadOnlySpan 重载，net48 编译环境里没有这个类型。
    /// </summary>
    private static AudioClip MakeClick()
    {
        const int rate = 44100;
        const int length = (int)(rate * 0.03);
        var position = 0;
        return AudioClip.Create("RhythmHintClick", length, 1, rate, false,
            data =>
            {
                for (var i = 0; i < data.Length; i++)
                {
                    var t = (position + i) / (float)rate;
                    data[i] = position + i < length ? Mathf.Sin(2 * Mathf.PI * 2000 * t) * Mathf.Exp(-t * 160) * 0.8f : 0f;
                }

                position += data.Length;
            },
            newPosition => position = newPosition);
    }
}
