using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace RhythmHint.Core;

/// <summary>
/// 自制关卡的歌曲另存一份成脚本交给编辑器：
/// <c>(window.RHYTHMHINT_AUDIO = window.RHYTHMHINT_AUDIO || {})["recent/…ogg"] = "base64";</c>。
/// 编辑器从 file:// 打开，读不了同目录的音乐文件（fetch、XHR 都被浏览器拦住），只能用 &lt;script&gt; 载入；
/// 拿到原始字节后整首解码、用 Web Audio 播。浏览器自己在 OGG、VBR MP3 里跳播放位置不准（实测差到 0.9–2.3 秒，见 docs/验证记录.md），
/// 整首解码后从采样里直接跳就准。WAV 跳得准，不用另存。
/// </summary>
public static class AudioScript
{
    /// <summary>编辑器里放歌曲数据的全局对象，键是会话里的音乐文件路径。</summary>
    public const string Registry = "window.RHYTHMHINT_AUDIO";

    /// <summary>大于这个的歌曲不另存：整首解码太占内存，编辑器退回用 &lt;audio&gt; 播。</summary>
    public const long MaxBytes = 64L * 1024 * 1024;

    private static readonly Encoding Ascii = new ASCIIEncoding();

    /// <summary>音乐文件对应的脚本：同一个路径后面加 .js（recent/968-…-123.ogg → recent/968-…-123.ogg.js）。</summary>
    public static string PathFor(string audioFile) => audioFile + ".js";

    /// <summary>这种音乐要不要另存：WAV 浏览器跳得准；空文件、太大的不存。</summary>
    public static bool Wanted(string extension, long length) =>
        length > 0 && length <= MaxBytes && !string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase);

    /// <summary>把 <paramref name="source"/> 的全部字节写成脚本，<paramref name="key"/> 是会话里的音乐文件路径。</summary>
    public static void Write(Stream source, Stream target, string key)
    {
        WriteAscii(target, "(" + Registry + " = " + Registry + " || {})[" + JsonConvert.ToString(key) + "] = \"");
        // 每块都是 3 的倍数个字节，拼起来和一次编码完全一样
        var buffer = new byte[3 * 64 * 1024];
        int filled;
        while ((filled = ReadFull(source, buffer)) > 0)
        {
            WriteAscii(target, Convert.ToBase64String(buffer, 0, filled));
        }

        WriteAscii(target, "\";\n");
    }

    private static int ReadFull(Stream source, byte[] buffer)
    {
        var total = 0;
        int read;
        while (total < buffer.Length && (read = source.Read(buffer, total, buffer.Length - total)) > 0)
        {
            total += read;
        }

        return total;
    }

    private static void WriteAscii(Stream target, string text)
    {
        var bytes = Ascii.GetBytes(text);
        target.Write(bytes, 0, bytes.Length);
    }
}
