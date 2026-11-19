using System.Text;
using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 自制关卡的歌曲另存成脚本交给编辑器（2026-11-19 用户录屏：编辑器里暂停、滚动后音画不同步）。
/// 原因是浏览器在 OGG、VBR MP3 里跳播放位置不准；编辑器拿到原始字节后整首解码再播就准。
/// </summary>
public class AudioScriptTests
{
    private static string Script(byte[] bytes, string key)
    {
        using var target = new MemoryStream();
        AudioScript.Write(new MemoryStream(bytes), target, key);
        return Encoding.ASCII.GetString(target.ToArray());
    }

    private static byte[] Decode(string script, string key)
    {
        var prefix = "(window.RHYTHMHINT_AUDIO = window.RHYTHMHINT_AUDIO || {})[\"" + key + "\"] = \"";
        Assert.StartsWith(prefix, script);
        Assert.EndsWith("\";\n", script);
        return Convert.FromBase64String(script.Substring(prefix.Length, script.Length - prefix.Length - 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData((3 * 64 * 1024) - 1)]
    [InlineData(3 * 64 * 1024)]
    [InlineData((3 * 64 * 1024) + 1)]
    [InlineData(500_000)]
    public void EncodesEveryByteAcrossChunkBoundaries(int length)
    {
        var bytes = new byte[length];
        new Random(length).NextBytes(bytes);

        var script = Script(bytes, "recent/1-ab-20261119-120000-000.ogg");

        Assert.Equal(bytes, Decode(script, "recent/1-ab-20261119-120000-000.ogg"));
        Assert.Equal(Convert.ToBase64String(bytes), script.Substring(script.IndexOf("= \"", StringComparison.Ordinal) + 3).TrimEnd('\n', ';', '"'));
    }

    /// <summary>文件流一次可能只给几个字节：拼出来的仍然和一次编码一样。</summary>
    [Fact]
    public void ShortReadsDoNotBreakTheEncoding()
    {
        var bytes = Encoding.ASCII.GetBytes("a song that arrives seven bytes at a time");
        using var target = new MemoryStream();
        AudioScript.Write(new TrickleStream(bytes, 7), target, "k");
        Assert.Equal(bytes, Decode(Encoding.ASCII.GetString(target.ToArray()), "k"));
    }

    [Fact]
    public void TheKeyIsAJsonStringSoQuotesCannotEscapeIt()
    {
        var script = Script(new byte[] { 1 }, "recent/a\"b\\c.ogg");
        Assert.StartsWith("(window.RHYTHMHINT_AUDIO = window.RHYTHMHINT_AUDIO || {})[\"recent/a\\\"b\\\\c.ogg\"] = \"AQ==\";", script);
    }

    [Theory]
    [InlineData(".ogg", 1000, true)]
    [InlineData(".MP3", 1000, true)]
    [InlineData(".wav", 1000, false)] // 浏览器在 WAV 里跳得准，不用另存
    [InlineData(".WAV", 1000, false)]
    [InlineData(".ogg", 0, false)]
    [InlineData(".ogg", AudioScript.MaxBytes + 1, false)]
    public void OnlyCompressedSongsOfSensibleSizeAreSaved(string extension, long length, bool wanted) =>
        Assert.Equal(wanted, AudioScript.Wanted(extension, length));

    [Fact]
    public void TheScriptSitsNextToItsSong() =>
        Assert.Equal("recent/968-abc-20261119-120000-000.ogg.js", AudioScript.PathFor("recent/968-abc-20261119-120000-000.ogg"));

    private sealed class TrickleStream(byte[] data, int step) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(Math.Min(step, count), data.Length - _position);
            Array.Copy(data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
