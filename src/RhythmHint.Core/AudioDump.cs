using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RhythmHint.Core;

/// <summary>
/// 把游戏里正在播的音乐（一块块读出来的浮点采样，多声道交错）写成 16 位 WAV，同时算波形峰值。
/// 官方关卡没有单独的歌曲文件，编辑器要放音乐只能这样导出；自定义关卡直接拷贝歌曲文件，只用这里算波形。
/// </summary>
public sealed class AudioDump : IDisposable
{
    private readonly Stream? _wav;
    private readonly BinaryWriter? _writer;
    private readonly int _channels;
    private readonly int _framesPerPeak;
    private readonly List<byte> _peaks = new();
    private float _peak;
    private int _framesInPeak;
    private long _dataBytes;

    /// <param name="wav">写 WAV 的流；只算波形时为 null。</param>
    public AudioDump(Stream? wav, int channels, int sampleRate, int peaksPerSecond)
    {
        if (channels < 1 || sampleRate < 1 || peaksPerSecond < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), "声道数、采样率、每秒峰值数都要大于 0");
        }

        _channels = channels;
        _framesPerPeak = Math.Max(1, sampleRate / peaksPerSecond);
        PeaksPerSecond = sampleRate / _framesPerPeak;
        if (wav == null)
        {
            return;
        }

        _wav = wav;
        _writer = new BinaryWriter(wav, Encoding.ASCII, leaveOpen: true);
        _writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        _writer.Write(0); // 写完再填
        _writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        _writer.Write(16);
        _writer.Write((short)1); // PCM
        _writer.Write((short)channels);
        _writer.Write(sampleRate);
        _writer.Write(sampleRate * channels * 2);
        _writer.Write((short)(channels * 2));
        _writer.Write((short)16);
        _writer.Write(Encoding.ASCII.GetBytes("data"));
        _writer.Write(0);
    }

    /// <summary>实际每秒几个峰值（采样率不能整除时略有出入）。</summary>
    public int PeaksPerSecond { get; }

    public byte[] Peaks => _peaks.ToArray();

    /// <param name="samples">交错的采样，-1..1。</param>
    /// <param name="count">本块有效采样数（声道数的整数倍）。</param>
    public void Write(float[] samples, int count)
    {
        for (var i = 0; i + _channels <= count; i += _channels)
        {
            for (var c = 0; c < _channels; c++)
            {
                var s = samples[i + c];
                s = s > 1 ? 1 : s < -1 ? -1 : s;
                _writer?.Write((short)Math.Round(s * short.MaxValue));
                var a = Math.Abs(s);
                if (a > _peak)
                {
                    _peak = a;
                }
            }

            if (++_framesInPeak == _framesPerPeak)
            {
                FlushPeak();
            }
        }

        _dataBytes += (count / _channels) * _channels * 2L;
    }

    /// <summary>写完：补上 WAV 头里的长度，收尾最后一个峰值。</summary>
    public void Finish()
    {
        if (_framesInPeak > 0)
        {
            FlushPeak();
        }

        if (_writer == null || _wav == null)
        {
            return;
        }

        _writer.Flush();
        if (_dataBytes > uint.MaxValue - 36)
        {
            throw new InvalidOperationException("音乐太长，WAV 放不下");
        }

        _wav.Position = 4;
        _writer.Write((uint)(36 + _dataBytes));
        _wav.Position = 40;
        _writer.Write((uint)_dataBytes);
        _writer.Flush();
    }

    /// <summary>
    /// 短音效（编辑器的打拍声）写成 WAV：最长 maxSeconds 秒，去掉末尾的静音。全是静音时返回空数组
    /// （压缩格式的音效 GetData 读出来全是 0，当作没读到）。
    /// </summary>
    /// <param name="samples">交错的采样，-1..1。</param>
    public static byte[] ShortWav(float[] samples, int channels, int sampleRate, double maxSeconds)
    {
        const float Silence = 1e-4f;
        var frames = Math.Min(samples.Length / channels, (int)(sampleRate * maxSeconds));
        while (frames > 0 && IsSilent(samples, (frames - 1) * channels, channels, Silence))
        {
            frames--;
        }

        if (frames == 0)
        {
            return Array.Empty<byte>();
        }

        using var stream = new MemoryStream();
        using (var dump = new AudioDump(stream, channels, sampleRate, peaksPerSecond: 1))
        {
            dump.Write(samples, frames * channels);
            dump.Finish();
        }

        return stream.ToArray();
    }

    public void Dispose() => _writer?.Dispose();

    private static bool IsSilent(float[] samples, int start, int channels, float threshold)
    {
        for (var c = 0; c < channels; c++)
        {
            if (Math.Abs(samples[start + c]) > threshold)
            {
                return false;
            }
        }

        return true;
    }

    private void FlushPeak()
    {
        // 开平方让小音量也看得见（波形只是参考）
        _peaks.Add((byte)Math.Round(Math.Sqrt(_peak) * 255));
        _peak = 0;
        _framesInPeak = 0;
    }
}
