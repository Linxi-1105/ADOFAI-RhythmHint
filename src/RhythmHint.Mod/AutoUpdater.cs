using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
/// 国内很多玩家连不上 GitHub（2026-11-19 玩家日志），清单和压缩包都同时经国内加速镜像去取（csproj 的 UpdateMirrors，见 <see cref="ModUpdate.Sources"/>）；
/// 都连不上时提示从群文件或网盘更新，和以前一样。
/// </summary>
internal static class AutoUpdater
{
    /// <summary>启动后等几秒再查：让游戏先载入完，不和开场抢网络（有意的延时）。</summary>
    private const float StartDelaySeconds = 5;

    private const int ManifestTimeoutSeconds = 20;

    /// <summary>
    /// 每个来源下压缩包的时限：约 1 MB，10 KB/s 也能在 100 秒内下完；一个来源卡住时不用等太久就换下一个（后台下载不影响游戏）。
    /// </summary>
    private const int DownloadTimeoutSeconds = 120;

    private static bool _started;

    /// <summary>正在进行的网络请求（清单同时问几个来源）：Mod 关掉时协程跟着停，要靠它停掉请求（见 <see cref="Abort"/>）。</summary>
    private static readonly List<UnityWebRequest> Requests = new();

    private static List<string>? _mirrors;

    /// <summary>编译时写进程序集的加速镜像列表（csproj 的 UpdateMirrors）。</summary>
    private static List<string> Mirrors => _mirrors ??= ModUpdate.ParseMirrors(
        typeof(AutoUpdater).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "UpdateMirrors")?.Value);

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
        host.StartCoroutine(Guarded(Run(now)));
    }

    /// <summary>
    /// Overlay 销毁（玩家在 UMM 里关掉 Mod）时调用：协程跟着停了，停掉没下完的请求（下了一半的文件随之删掉），
    /// 把卡在「检查中」「正在下载」的状态复位，重新启用后照常再查，「重试」也点得动。
    /// </summary>
    public static void Abort()
    {
        foreach (var request in Requests)
        {
            request.Abort();
            request.Dispose();
        }

        Requests.Clear();

        if (Busy)
        {
            State = UpdateState.Idle;
            _started = false;
            Main.Mod.Logger.Log("自动更新：Mod 已关闭，中断了检查或下载，重新启用后再查");
        }
    }

    /// <summary>
    /// 一步一步代跑 <paramref name="steps"/>：协程里没接住的异常只会让 Unity 记一条日志、停掉协程，
    /// 状态就一直停在「检查中」「正在下载」，「重试」也因为 Busy 点不动。哪一步抛出都走 <see cref="Fail"/>。
    /// 这是协程最外层的兜底，所以接住所有异常（完整记进日志），和 Overlay.Update 的兜底一样。
    /// </summary>
    private static IEnumerator Guarded(IEnumerator steps)
    {
        try
        {
            while (true)
            {
                object? current;
                try
                {
                    if (!steps.MoveNext())
                    {
                        break;
                    }

                    current = steps.Current;
                }
                catch (Exception ex)
                {
                    Fail(Lang.M("发生意外错误，详见 Unity Mod Manager 日志"), ex.Message);
                    Main.Mod.Logger.LogException(ex);
                    break;
                }

                yield return current;
            }
        }
        finally
        {
            (steps as IDisposable)?.Dispose();
            Requests.Clear();
        }
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
        var sources = ModUpdate.Sources(url, Mirrors);
        var manifests = new UpdateManifest?[sources.Count];
        var notes = new string[sources.Count];
        var reached = false;
        var requests = sources.Select(source => UnityWebRequest.Get(source)).ToArray();
        try
        {
            // 同时问每个来源，等它们都结束（各自有时限）：镜像站可能还缓存着旧清单，挑版本最高的
            var operations = new List<UnityWebRequestAsyncOperation>();
            foreach (var request in requests)
            {
                request.timeout = ManifestTimeoutSeconds;
                Requests.Add(request);
                operations.Add(request.SendWebRequest());
            }

            foreach (var operation in operations)
            {
                yield return operation;
            }

            for (var i = 0; i < requests.Length; i++)
            {
                if (requests[i].result != UnityWebRequest.Result.Success)
                {
                    notes[i] = $"{SourceName(sources[i])} {requests[i].error}";
                    continue;
                }

                reached = true;
                manifests[i] = UpdateManifest.Parse(requests[i].downloadHandler.text);
                notes[i] = $"{SourceName(sources[i])} {manifests[i]?.Version ?? Lang.M("更新清单格式不对").ToString()}";
            }
        }
        finally
        {
            foreach (var request in requests)
            {
                Requests.Remove(request);
                request.Dispose();
            }
        }

        var summary = string.Join("; ", notes);
        var best = ModUpdate.Newest(manifests);
        if (best < 0)
        {
            Fail(reached ? Lang.M("更新清单格式不对") : Lang.M("连不上更新服务器"), summary);
            yield break;
        }

        var manifest = manifests[best]!;
        Main.Mod.Logger.Log($"自动更新：查到 {manifest.Version}（{summary}）");
        UpdateNotice.Found(Main.Settings, manifest.Version, fromUmm: false);
        if (!ModUpdate.ShouldInstall(manifest.Version, current))
        {
            State = UpdateState.UpToDate;
            Main.Mod.Logger.Log($"自动更新：已是最新（当前 {current}，最新 {manifest.Version}）");
            yield break;
        }

        Version = manifest.Version;
        State = UpdateState.Downloading;
        try
        {
            Directory.CreateDirectory(WorkFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail(Lang.M("安装失败"), WorkFolder + ": " + ex.Message);
            yield break;
        }

        // 先从给出清单的来源下（对这个玩家已经证明能连上），下不了或者文件不对再换下一个
        var zip = Path.Combine(WorkFolder, "download.zip");
        var failures = new List<string>();
        var corrupt = false;
        foreach (var source in ModUpdate.FirstThen(ModUpdate.Sources(manifest.Url, Mirrors), best))
        {
            Main.Mod.Logger.Log($"自动更新：下载 {manifest.Version}（{source}）");
            using (var request = new UnityWebRequest(source, UnityWebRequest.kHttpVerbGET, new DownloadHandlerFile(zip) { removeFileOnAbort = true }, null))
            {
                request.timeout = DownloadTimeoutSeconds;
                Requests.Add(request);
                yield return request.SendWebRequest();
                Requests.Remove(request);
                if (request.result != UnityWebRequest.Result.Success)
                {
                    failures.Add($"{SourceName(source)} {request.error}");
                    continue;
                }
            }

            var sha = HashOf(zip);
            if (sha == manifest.Sha256)
            {
                Install(zip, manifest);
                yield break;
            }

            corrupt = true;
            failures.Add($"{SourceName(source)} sha256 {sha} != {manifest.Sha256}");
            TryDelete(zip);
        }

        Fail(corrupt ? Lang.M("下载的文件不完整") : Lang.M("下载失败"), string.Join("; ", failures));
    }

    /// <summary>日志里的来源名：GitHub、镜像站写主机名，本地试的 file:// 写 file。</summary>
    private static string SourceName(string source) =>
        !Uri.TryCreate(source, UriKind.Absolute, out var uri) ? source : uri.Host.Length > 0 ? uri.Host : uri.Scheme;

    /// <summary>下回来的文件的 SHA-256；读不了时为空（当作文件不对，换下一个来源）。</summary>
    private static string HashOf(string path)
    {
        try
        {
            return ModUpdate.Sha256Of(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Main.Mod.Logger.Warning($"自动更新：读不了下载的文件 {path}：{ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>装进 Mod 文件夹（SHA-256 已经核对过；约 1 MB，在主线程上一下就完）。</summary>
    private static void Install(string zip, UpdateManifest manifest)
    {
        try
        {
            var files = ModUpdate.Install(zip, Main.Mod.Path, WorkFolder, Main.Mod.Info.Id);
            State = UpdateState.Installed;
            Main.Mod.Logger.Log($"自动更新：已装好 {manifest.Version}（{files.Count} 个文件），重启游戏后生效；旧文件备份在 {Path.Combine(WorkFolder, "backup")}");
            var staging = Path.Combine(WorkFolder, "new");
            if (Directory.Exists(staging))
            {
                Main.Mod.Logger.Warning($"自动更新：临时文件夹 {staging} 没删掉（被占用），不影响新版，下次更新时会重建");
            }
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
