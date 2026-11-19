using UnityEngine;

namespace RhythmHint;

/// <summary>读编进 DLL 的 PNG（难度徽章、设置窗口的图标）。只能在主线程用。</summary>
internal static class EmbeddedImage
{
    // 用反射取 byte[] 那个重载：Unity 6 的 ImageConversion.LoadImage 还有 ReadOnlySpan 重载，
    // net48 编译环境里没有这个类型，直接调用或转成委托都会编译失败（CS0518）
    private static readonly System.Reflection.MethodInfo? LoadImageMethod =
        typeof(ImageConversion).GetMethod(nameof(ImageConversion.LoadImage), new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });

    /// <summary>读出一张图；资源不存在或解不开时为 null（调用方记日志并退回别的画法）。</summary>
    /// <param name="name">资源名，如 RhythmHint.SettingsIcons.home.png。</param>
    /// <param name="mipmaps">缩小显示时用 mipmap，边缘不发毛。</param>
    public static Texture2D? Load(string name, bool mipmaps = true)
    {
        using var stream = typeof(EmbeddedImage).Assembly.GetManifestResourceStream(name);
        if (stream == null || LoadImageMethod == null)
        {
            return null;
        }

        var bytes = new byte[stream.Length];
        var read = 0;
        while (read < bytes.Length)
        {
            var n = stream.Read(bytes, read, bytes.Length - read);
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipmaps)
        {
            filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        if (LoadImageMethod.Invoke(null, new object[] { texture, bytes, true }) is true)
        {
            return texture;
        }

        Object.Destroy(texture);
        return null;
    }
}
