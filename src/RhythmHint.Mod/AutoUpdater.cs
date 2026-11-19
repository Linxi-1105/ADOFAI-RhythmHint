using System;
using System.Collections;
using System.IO;
using RhythmHint.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace RhythmHint;

internal enum UpdateState
{
    /// <summary>还没开始查（启动后先等几秒）。</summary>
    Idle,

    /// <summary>设置里关了自动更新。</summary>
    Off,

    /// <summary>带后缀的测试版：不自动更新，免得把正在试的版本换掉。</summary>
    TestBuild,
    Checking,
    UpToDate,
    Downloading,

    /// <summary>新版已经装进 Mod 文件夹，重启游戏后生效。</summary>
    Installed,
    Failed,
}

/// <summary>
/// 全自动更新（2026-11-19 用户：Mod 可以联网，要全自动更新；安全不用担心，不做签名）：
/// 启动游戏后读最新 Release 里的 update.json（地址从 Info.json 的 Repository 推出来，见 <see cref="ModUpdate.ManifestUrl"/>），
/// 有新版就在后台下载压缩包、核对 SHA-256、装进 Mod 文件夹（只换压缩包里的文件），下次启动游戏生效。
/// 不靠 UMM 查更新：UMM 查之前要先 ping 通 8.8.8.8，国内很多网络不通，玩家一直收不到提示。
/// 下载不了（国内连不上 GitHub）时提示从群文件或网盘更新，和以前一样。
/// </summary>
internal static class AutoUpdater
{
    /// <summary>启动后等几秒再查：让游戏先载入完，不和开场抢网络（有意的延时）。</summary>
    private const float StartDelaySeconds = 5;

    private const int ManifestTimeoutSeconds = 20;

    /// <summary>压缩包约 1 MB；国内连 GitHub 慢时给足时间，后台下载不影响游戏。</summary>
    private const int DownloadTimeoutSeconds = 300;

    private static bool _started;

    public static UpdateState State { get; private set; } = UpdateState.Idle;

    /// <summary>正在下载、已经装好的版本。</summary>
    public static string Version { get; private set; } = string.Empty;

    /// <summary>失败的原因（给玩家看的，翻译后显示）。</summary>
    public static Message Error { get; private set; } = Message.Empty;

    public static bool Busy => State is UpdateState.Checking or UpdateState.Downloading;

    private static string WorkFolder => Path.Combine(Main.Mod.Path, ".update");

    /// <summary>启动游戏后查一次；<paramref name="now"/> 为 true 是玩家点了「重试」：马上查，不管设置里关没关。</summary>
    public static void Start(MonoBehaviour host, bool now = false)
    {
        if (Busy || State == UpdateState.Installed || (_started && !now))
        {
            return;
        }

        _started = true;
        host.StartCoroutine(Run(now));
    }

    private static IEnumerator Run(bool now)
    {
        var current = Main.Mod.Info.Version;
        if (!now && !Main.Settings.AutoUpdate)
        {
            State = UpdateState.Off;
            yield break;
        }

        if (ModUpdate.IsTestBuild(current))
        {
            State = UpdateState.TestBuild;
            Main.Mod.Logger.Log($"自动更新：测试版 {current} 不自动更新");
            yield break;
        }

        var url = ModUpdate.ManifestUrl(Main.Mod.Info.Repository);
        if (url == null)
        {
            Fail(Lang.M("Info.json 里没有更新地址"), string.Empty);
            yield break;
        }

        if (!now)
        {
            yield return new WaitForSecondsRealtime(StartDelaySeconds); // 有意的延时，见 StartDelaySeconds
        }

        State = UpdateState.Checking;
        string text;
        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = ManifestTimeoutSeconds;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail(Lang.M("连不上更新服务器"), url + "：" + request.error);
                yield break;
            }

            text = request.downloadHandler.text;
        }

        var manifest = UpdateManifest.Parse(text);
        if (manifest == null)
        {
            Fail(Lang.M("更新清单格式不对"), url);
            yield break;
        }

        UpdateNotice.Found(Main.Settings, manifest.Version, fromUmm: false);
        if (!ModUpdate.ShouldInstall(manifest.Version, current))
        {
            State = UpdateState.UpToDate;
            Main.Mod.Logger.Log($"自动更新：已是最新（当前 {current}，最新 {manifest.Version}）");
            yield break;
        }

        Version = manifest.Version;
        State = UpdateState.Downloading;
        Main.Mod.Logger.Log($"自动更新：下载 {manifest.Version}（{manifest.Url}）");
        Directory.CreateDirectory(WorkFolder);
        var zip = Path.Combine(WorkFolder, "download.zip");
        using (var request = new UnityWebRequest(manifest.Url, UnityWebRequest.kHttpVerbGET, new DownloadHandlerFile(zip) { removeFileOnAbort = true }, null))
        {
            request.timeout = DownloadTimeoutSeconds;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail(Lang.M("下载失败"), manifest.Url + "：" + request.error);
                yield break;
            }
        }

        Install(zip, manifest);
    }

    /// <summary>核对 SHA-256，装进 Mod 文件夹（约 1 MB，在主线程上一下就完）。</summary>
    private static void Install(string zip, UpdateManifest manifest)
    {
        try
        {
            var sha = ModUpdate.Sha256Of(zip);
            if (sha != manifest.Sha256)
            {
                Fail(Lang.M("下载的文件不完整"), $"sha256 {sha} != {manifest.Sha256}");
                return;
            }

            var files = ModUpdate.Install(zip, Main.Mod.Path, WorkFolder, Main.Mod.Info.Id);
            State = UpdateState.Installed;
            Main.Mod.Logger.Log($"自动更新：已装好 {manifest.Version}（{files.Count} 个文件），重启游戏后生效；旧文件备份在 {Path.Combine(WorkFolder, "backup")}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Fail(Lang.M("安装失败"), ex.Message);
        }
        finally
        {
            TryDelete(zip);
        }
    }

    private static void Fail(Message reason, string detail)
    {
        State = UpdateState.Failed;
        Error = reason;
        Main.Mod.Logger.Warning($"自动更新失败：{reason}{(detail.Length > 0 ? "（" + detail + "）" : string.Empty)}");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // 删不掉（被占用）：下次下载时覆盖
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }
    }
}
