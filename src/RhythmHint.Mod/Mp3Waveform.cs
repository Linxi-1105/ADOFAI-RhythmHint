using System.IO;
using MP3Sharp;
using RhythmHint.Core;

namespace RhythmHint;

/// <summary>用游戏 firstpass 程序集中的 MP3Sharp 读取工坊歌曲；不分配整首歌的采样数组。</summary>
internal static class Mp3Waveform
{
    public static bool TryRead(string path, SessionAudio audio, int peaksPerSecond)
    {
        using var file = File.OpenRead(path);
        using var pcm = new MP3Stream(file);
        if (pcm.Frequency <= 0)
        {
            return false;
        }

        // 此版 MP3Sharp 的输出固定是交错的双声道 16 位小端 PCM，单声道输入的右声道为空。
        using var peaks = new AudioDump(null, 2, pcm.Frequency, peaksPerSecond);
        var bytes = new byte[16384];
        var samples = new float[bytes.Length / 2];
        int count;
        while ((count = pcm.Read(bytes, 0, bytes.Length)) > 0)
        {
            for (var i = 0; i < count / 2; i++)
            {
                samples[i] = (short)(bytes[2 * i] | (bytes[(2 * i) + 1] << 8)) / 32768f;
            }

            peaks.Write(samples, count / 2);
        }

        peaks.Finish();
        audio.Peaks = peaks.Peaks;
        audio.PeaksPerSecond = peaks.PeaksPerSecond;
        return audio.Peaks.Length > 0;
    }
}
