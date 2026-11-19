using System;
using System.Collections.Generic;
using System.IO;

namespace RhythmHint.Core;

/// <summary>
/// 游戏装在浏览器不让网页写的文件夹里时（Steam 默认装在 C:\Program Files (x86)），编辑器存不进 Mods/RhythmHint/Fingerings：
/// 浏览器弹「由于此文件夹包含系统文件，无法将其打开」（2026-11-19 玩家截图）。这时编辑器改存到「文档\RhythmHint\Fingerings」，
/// Mod 每秒把那里的手序搬进游戏的手序文件夹。手序仍以游戏的文件夹为准，搬走而不是复制：游戏里删掉的不会再被复制回来。
/// </summary>
public static class FingeringInbox
{
    /// <summary>
    /// Windows 上浏览器连同里面的子文件夹都不让网页选的文件夹（Chromium 的 File System Access 黑名单：
    /// 程序目录、系统目录、AppData、ProgramData）。取不到的跳过。
    /// </summary>
    public static IEnumerable<string> WindowsBlockedRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Environment.GetEnvironmentVariable("ProgramW6432") ?? string.Empty;
        yield return Environment.GetEnvironmentVariable("ProgramFiles") ?? string.Empty;
        yield return Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? string.Empty;
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    }

    /// <summary><paramref name="folder"/> 是不是在 <paramref name="blockedRoots"/> 之一里面（或者就是它）。不分大小写。</summary>
    public static bool BrowserBlocks(string folder, IEnumerable<string> blockedRoots)
    {
        var path = Normalize(folder);
        if (path == null)
        {
            return false;
        }

        foreach (var root in blockedRoots)
        {
            var r = Normalize(root);
            if (r != null && path.StartsWith(r, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 把 <paramref name="inbox"/> 里的 .json 搬进 <paramref name="target"/>（同名的覆盖）。
    /// 搬不动的（浏览器正在写、被占用）留到下次。返回搬过去的文件名。
    /// </summary>
    public static List<string> MoveInto(string inbox, string target)
    {
        var moved = new List<string>();
        if (!Directory.Exists(inbox))
        {
            return moved;
        }

        Directory.CreateDirectory(target);
        foreach (var source in Directory.GetFiles(inbox, "*.json"))
        {
            var name = Path.GetFileName(source);
            try
            {
                File.Copy(source, Path.Combine(target, name), overwrite: true);
                File.Delete(source);
                moved.Add(name);
            }
            catch (IOException)
            {
                // 浏览器还在写或者文件被占用：下一秒再搬
            }
            catch (UnauthorizedAccessException)
            {
                // 没有权限：留在原处，下一秒再试（一直不行时编辑器那边的文件也还在）
            }
        }

        return moved;
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null; // 不是合法路径：当作不在黑名单里
        }
    }
}
