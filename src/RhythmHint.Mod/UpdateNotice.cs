using System;

namespace RhythmHint;

/// <summary>
/// 有没有新版本。Mod 自己不联网：UMM 按 Info.json 的 Repository 读更新清单（Release 里的 Repository.json），
/// 版本更高就填 ModEntry.NewestVersion。UMM 默认每天只在第一次启动游戏时查一次、查到的结果不存，
/// 所以这里一看到就记进设置（<see cref="Settings.LatestVersion"/>）并保存，当天再开游戏照样提示；装上新版后自然不再提示。
/// </summary>
internal static class UpdateNotice
{
    private static string? _parsedText;
    private static Version? _parsed;

    /// <summary>比当前版本新的版本；没有新版、没联网或 UMM 关了检查更新时为 null。</summary>
    public static Version? Newer { get; private set; }

    /// <summary>每帧调用：UMM 查到新版本时记下来，再算要不要提示。</summary>
    public static void Update(Settings settings)
    {
        var mod = Main.Mod;
        var found = mod.NewestVersion?.ToString();
        if (found != null && found != settings.LatestVersion)
        {
            settings.LatestVersion = found;
            settings.Save(mod);
            mod.Logger.Log($"UMM 查到新版本 {found}（当前 {mod.Version}）");
        }

        if (settings.LatestVersion != _parsedText)
        {
            _parsedText = settings.LatestVersion;
            _parsed = Version.TryParse(_parsedText, out var v) ? v : null;
        }

        Newer = _parsed != null && _parsed > mod.Version ? _parsed : null;
    }
}
