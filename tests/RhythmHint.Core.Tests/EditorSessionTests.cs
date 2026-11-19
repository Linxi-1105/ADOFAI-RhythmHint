using Newtonsoft.Json.Linq;
using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>交给编辑器的数据：能被网页当脚本载入，字段齐全；音乐导出成合法的 WAV、波形长度对。</summary>
public class EditorSessionTests
{
    [Fact]
    public void SessionIsAScriptWithEverythingTheEditorNeeds()
    {
        var hits = new List<TileHit>();
        for (var i = 0; i < 20; i++)
        {
            hits.Add(new TileHit(i + 1, 1 + (i * 0.25), i == 5 ? 0.5 : 0, 90));
        }

        var notes = NotePlanner.Plan(hits, new FingeringOptions());
        var fingering = CustomFingering.From(hits, notes, false);
        fingering.Song = "曲名";
        var session = new EditorSession
        {
            Fingering = fingering,
            FromAuto = notes.ConvertAll(_ => false),
            FileName = fingering.SuggestedFileName(),
            SaveFolder = @"E:\Game\Mods\RhythmHint\Fingerings",
            Language = "ja",
            Standard = notes,
            Alternate = NotePlanner.Plan(hits, new FingeringOptions { Style = FingeringStyle.ReviewPreferenceV1 }),
            Beats = new[] { new BeatLine(1, true), new BeatLine(1.5, false) },
            Tempo = new[] { (1.0, 120.0) },
            Path = new[] { new PathPoint(1, 0, 0, 1, true), new PathPoint(2, 1, 0, 1.25, true) },
            Audio = new SessionAudio { File = "session-audio.wav", Offset = 0.2, Pitch = 1, PeaksPerSecond = 100, Peaks = new byte[] { 1, 2, 3 } },
        };

        var script = session.ToScript();

        Assert.StartsWith("window.RHYTHMHINT_SESSION = ", script);
        var json = JObject.Parse(script.Substring("window.RHYTHMHINT_SESSION = ".Length).TrimEnd(';', '\n'));
        Assert.Equal("ja", (string?)json["language"]);
        Assert.Equal("曲名.json", (string?)json["fileName"]);
        Assert.Equal(20, ((JArray)json["fingering"]!["hits"]!).Count);
        Assert.Equal(notes.Count, ((JArray)json["fingering"]!["notes"]!).Count);
        Assert.Equal(notes.Count, ((JArray)json["references"]!["alternate"]!).Count);
        Assert.Equal("AQID", (string?)json["audio"]!["peaks"]);

        // 编辑器保存的就是 fingering 这一段：Mod 能原样读回
        var saved = CustomFingering.Parse(json["fingering"]!.ToString());
        Assert.Equal(notes.Count, saved.Notes.Count);
    }

    [Fact]
    public void AudioDumpWritesAValidWavAndPeaks()
    {
        using var stream = new MemoryStream();
        var dump = new AudioDump(stream, channels: 2, sampleRate: 1000, peaksPerSecond: 10);
        var chunk = new float[200]; // 100 帧
        for (var i = 0; i < chunk.Length; i++)
        {
            chunk[i] = i % 2 == 0 ? 0.5f : -1f;
        }

        dump.Write(chunk, chunk.Length);
        dump.Write(chunk, chunk.Length);
        dump.Finish();

        var bytes = stream.ToArray();
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(44 + (200 * 2 * 2), bytes.Length);
        Assert.Equal((uint)(200 * 2 * 2), BitConverter.ToUInt32(bytes, 40));
        Assert.Equal(2, dump.Peaks.Length); // 200 帧，每 100 帧（0.1 秒）一个峰值
        Assert.All(dump.Peaks, p => Assert.Equal(255, p));
    }

    [Fact]
    public void HitSoundIsAShortWavWithoutTrailingSilence()
    {
        // 立体声 1000 Hz：前 100 帧有声音，后面 900 帧静音
        var samples = new float[2000];
        for (var i = 0; i < 200; i++)
        {
            samples[i] = 0.5f;
        }

        var wav = AudioDump.ShortWav(samples, channels: 2, sampleRate: 1000, maxSeconds: 1);

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal((short)2, BitConverter.ToInt16(wav, 22));
        Assert.Equal(1000, BitConverter.ToInt32(wav, 24));
        Assert.Equal((uint)(100 * 2 * 2), BitConverter.ToUInt32(wav, 40));
        Assert.Equal(44 + (100 * 2 * 2), wav.Length);
    }

    [Fact]
    public void HitSoundIsCutAtMaxLength()
    {
        var samples = Enumerable.Repeat(0.25f, 3000).ToArray(); // 单声道 3 秒

        var wav = AudioDump.ShortWav(samples, channels: 1, sampleRate: 1000, maxSeconds: 1);

        Assert.Equal((uint)(1000 * 2), BitConverter.ToUInt32(wav, 40));
    }

    [Fact]
    public void SilentHitSoundIsEmpty()
    {
        // 压缩格式的音效 GetData 读出来全是 0：当作没读到
        Assert.Empty(AudioDump.ShortWav(new float[500], channels: 1, sampleRate: 1000, maxSeconds: 1));
    }

    [Fact]
    public void SessionCarriesTheHitSound()
    {
        var withSound = new EditorSession { HitSound = new byte[] { 1, 2, 3 } }.ToScript();
        var without = new EditorSession().ToScript();

        Assert.Equal("AQID", (string?)Json(withSound)["hitSound"]);
        Assert.Equal(JTokenType.Null, Json(without)["hitSound"]!.Type);
    }

    [Fact]
    public void AudioCarriesTheGamesVisualCalibration()
    {
        // 游戏画面用的时间 = 音乐位置 − 偏移 − 画面校准（scrConductor.songposition_minusv）：编辑器照这样补，玩家在游戏里调好的同步编辑器里也对
        var session = new EditorSession { Audio = new SessionAudio { File = "a.ogg", Offset = -1.5608, Calibration = 0.16 } };

        var audio = Json(session.ToScript())["audio"]!;

        Assert.Equal(-1.5608, (double)audio["offset"]!, 6);
        Assert.Equal(0.16, (double)audio["calibration"]!, 6);
    }

    [Fact]
    public void PathCarriesTileDirectionsWhenKnown()
    {
        var session = new EditorSession { Path = new[] { new PathPoint(1, 0, 0, 1, true), new PathPoint(2, 1.5, 0, 1.5, true, Math.PI, 0) } };

        var path = (JArray)Json(session.ToScript())["path"]!;

        Assert.Equal(5, ((JArray)path[0]).Count);
        Assert.Equal(7, ((JArray)path[1]).Count);
        Assert.Equal(3.1416, (double)path[1][5]!);
    }

    private static JObject Json(string script) => JObject.Parse(script.Substring("window.RHYTHMHINT_SESSION = ".Length).TrimEnd(';', '\n'));
}
