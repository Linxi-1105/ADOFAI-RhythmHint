using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RhythmHint.Core;

/// <summary>
/// 自动更新的清单 update.json（2026-11-19 用户：Mod 可以联网，要全自动更新）：和 UMM 的 Repository.json 放在同一个 Release 里，
/// 写明最新版本、压缩包地址和 SHA-256。不往 Repository.json 里加字段，免得 UMM 或它的安装器读不了。
/// </summary>
public sealed class UpdateManifest
{
    public const string Format = "rhythmhint-update";

    private static readonly Regex Sha256Pattern = new("^[0-9a-fA-F]{64}$");

    private UpdateManifest(string version, string url, string sha256)
    {
        Version = version;
        Url = url;
        Sha256 = sha256.ToLowerInvariant();
    }

    public string Version { get; }

    /// <summary>压缩包地址：https（GitHub Release），本地试的时候也可以是 file://。</summary>
    public string Url { get; }

    /// <summary>压缩包的 SHA-256（小写十六进制）。</summary>
    public string Sha256 { get; }

    /// <summary>读清单；格式不对、字段缺了、地址不是 https / file 时为 null。</summary>
    public static UpdateManifest? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        JObject json;
        try
        {
            json = JObject.Parse(text!);
        }
        catch (JsonException)
        {
            return null; // 下回来的不是 JSON（比如被网络劫持成网页）：当作没有清单
        }

        string? format, version, url, sha;
        try
        {
            format = (string?)json["format"];
            version = (string?)json["version"];
            url = (string?)json["url"];
            sha = (string?)json["sha256"];
        }
        catch (ArgumentException)
        {
            return null; // 字段是对象、数组（转不成字符串时 Newtonsoft 抛这个）：当作清单格式不对
        }

        if (format != Format || !ModUpdate.TryParseVersion(version, out _) || url == null || sha == null || !Sha256Pattern.IsMatch(sha))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeFile))
        {
            return null;
        }

        return new UpdateManifest(version!, url, sha);
    }

    /// <summary>写成清单文本（打包时生成，测试里也用）。</summary>
    public static string Write(string version, string url, string sha256) => new JObject
    {
        ["format"] = Format,
        ["version"] = version,
        ["url"] = url,
        ["sha256"] = sha256.ToLowerInvariant(),
    }.ToString(Formatting.Indented);
}

/// <summary>自动更新：比版本、校验、把压缩包装进 Mod 文件夹（下次启动游戏生效）。</summary>
public static class ModUpdate
{
    /// <summary>清单的文件名：和 Info.json 里 Repository 指向的 Repository.json 放在同一个地方。</summary>
    public const string ManifestFile = "update.json";

    /// <summary>
    /// 清单地址：把 Repository.json 的地址换成同目录的 update.json
    /// （GitHub 的 releases/latest/download/ 会跳到最新 Release；本地试时 ModRepository 指向 file:// 也一样换）。
    /// </summary>
    public static string? ManifestUrl(string? repositoryUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl))
        {
            return null;
        }

        var slash = repositoryUrl!.LastIndexOf('/');
        return slash <= 0 ? null : repositoryUrl.Substring(0, slash + 1) + ManifestFile;
    }

    /// <summary>带后缀的测试版（1.3.0-dev）：不自动更新，免得把正在试的版本换掉。</summary>
    public static bool IsTestBuild(string? version) => version != null && version.IndexOf('-') >= 0;

    /// <summary>
    /// <paramref name="latest"/> 比 <paramref name="current"/> 新、而且该自动装：当前是测试版、最新是带后缀的预发布版、
    /// 版本号读不出来时都不装。
    /// </summary>
    public static bool ShouldInstall(string? latest, string? current)
    {
        if (IsTestBuild(current) || IsTestBuild(latest))
        {
            return false;
        }

        return TryParseVersion(latest, out var a) && TryParseVersion(current, out var b) && a > b;
    }

    /// <summary>读「主.次.修订」（可带 -后缀，后缀不参与比较）。</summary>
    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var dash = text!.IndexOf('-');
        var core = dash >= 0 ? text.Substring(0, dash) : text;
        if (!System.Version.TryParse(core, out var parsed) || parsed == null)
        {
            return false;
        }

        version = parsed;
        return true;
    }

    /// <summary>文件的 SHA-256（小写十六进制）。</summary>
    public static string Sha256Of(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha.ComputeHash(stream);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 把发布包装进 Mod 文件夹：先全部解到 <paramref name="workFolder"/>/new（只认 <paramref name="modId"/>/ 下面的文件，防路径穿越，
    /// 必须有 Info.json 和 DLL），再把要换掉的旧文件备份到 <paramref name="workFolder"/>/backup，最后覆盖过去。
    /// 中途失败就从备份还原再抛出。只换压缩包里有的文件：设置、手序、编辑器历史不动。返回换掉的文件（相对 Mod 文件夹）。
    /// </summary>
    public static List<string> Install(string zipPath, string modFolder, string workFolder, string modId) =>
        Install(zipPath, modFolder, workFolder, modId, (from, to) => File.Copy(from, to, overwrite: true));

    /// <param name="overwrite">
    /// 把一个新文件复制到 Mod 文件夹（from, to；目标所在的文件夹已经建好）。平时就是 File.Copy；测试用它模拟复制到一半失败（磁盘满、杀毒软件干扰）。
    /// </param>
    public static List<string> Install(string zipPath, string modFolder, string workFolder, string modId, Action<string, string> overwrite)
    {
        var staging = Path.Combine(workFolder, "new");
        var backup = Path.Combine(workFolder, "backup");
        var files = Extract(zipPath, staging, modId);
        if (!files.Contains("Info.json") || !files.Contains(modId + ".dll"))
        {
            throw new InvalidDataException("压缩包里没有 Info.json 或 " + modId + ".dll，不是本 Mod 的发布包");
        }

        Recreate(backup);
        foreach (var file in files)
        {
            var current = Path.Combine(modFolder, file);
            if (File.Exists(current))
            {
                CopyFile(current, Path.Combine(backup, file));
            }
        }

        // 动手之前先记下：复制到一半失败时，目标可能已经被截断（1.3.1 的包里第二个就是 DLL，没还原的话 UMM 下次整个 Mod 都加载不了），
        // 所以失败的那一个也要还原（原来没有的删掉）
        var touched = new List<string>();
        try
        {
            foreach (var file in files)
            {
                var target = Path.Combine(modFolder, file);
                touched.Add(file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                overwrite(Path.Combine(staging, file), target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Restore(touched, modFolder, backup);
            throw;
        }

        try
        {
            Directory.Delete(staging, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 新版已经全部装好，临时文件夹删不掉（被占用）不算失败：下次更新解包前会整个重建；调用方看到它还在会记日志
        }

        return files;
    }

    private static List<string> Extract(string zipPath, string staging, string modId)
    {
        Recreate(staging);
        var root = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var prefix = modId + "/";
        var files = new List<string>();
        using var stream = File.OpenRead(zipPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith("/", StringComparison.Ordinal))
            {
                continue; // 文件夹本身
            }

            if (!name.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException("压缩包里有 " + prefix + " 以外的文件：" + name);
            }

            var relative = name.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar);
            string target;
            try
            {
                target = Path.GetFullPath(Path.Combine(staging, relative));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                // .NET Framework / Mono 遇到路径里有非法字符（| < > " 或盘符那种冒号）抛这两种：当作坏包，和别的坏包一样报「安装失败」
                throw new InvalidDataException("压缩包里有非法的路径：" + name, ex);
            }

            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("压缩包里的路径会写到 Mod 文件夹外面：" + name);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var input = entry.Open())
            using (var output = File.Create(target))
            {
                input.CopyTo(output);
            }

            files.Add(relative);
        }

        return files;
    }

    /// <summary>把动过的文件换回备份；备份里没有的（新版才有的文件，可能只写了一半）删掉。</summary>
    private static void Restore(List<string> touched, string modFolder, string backup)
    {
        foreach (var file in touched)
        {
            var saved = Path.Combine(backup, file);
            try
            {
                if (File.Exists(saved))
                {
                    CopyFile(saved, Path.Combine(modFolder, file));
                }
                else
                {
                    File.Delete(Path.Combine(modFolder, file)); // 新版才有的文件
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 还原不了的这一个留着新版的，其余的照样还原；调用方会把原来的错误告诉玩家
            }
        }
    }

    private static void CopyFile(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to, overwrite: true);
    }

    private static void Recreate(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
    }
}
