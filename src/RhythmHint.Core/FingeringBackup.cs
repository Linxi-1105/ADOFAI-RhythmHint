using System;
using System.Collections.Generic;
using System.IO;

namespace RhythmHint.Core;

/// <summary>
/// 专属手序的备份。手序文件夹（Mods/RhythmHint/Fingerings）在 Mod 文件夹里，UMM 的卸载、回退（Revert）会把整个 Mod 文件夹删掉
/// （UnityModManagerApp/Mods.cs 的 UninstallMod：Directory.Delete(modInfo.Path, true)），玩家自己做的手序跟着没了（2026-11-19 评审）。
/// Mod 每次看到手序文件夹有变化就同步一份到「文档\RhythmHint\FingeringsBackup」，手序文件夹整个不见了时从那里还原。
/// 以游戏的手序文件夹为准：那里删掉的，备份里也删；只有整个文件夹都没了才还原，玩家删掉的单个手序不会被还原回来。
/// 备份文件夹故意不叫 Fingerings：编辑器选保存位置时只认这个名字，选到备份会提示，不会存进备份里。
/// </summary>
public static class FingeringBackup
{
    /// <summary>
    /// 让 <paramref name="backup"/> 里的 .json 和 <paramref name="folder"/> 一样（新的、改过的复制过去，删掉的删掉）。
    /// 手序文件夹不存在时什么都不做，不会把备份清空。有文件被占用（浏览器正在写）时返回 false，下次再同步。
    /// </summary>
    public static bool Mirror(string folder, string backup)
    {
        if (!Directory.Exists(folder))
        {
            return true;
        }

        Directory.CreateDirectory(backup);
        var complete = true;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in Directory.GetFiles(folder, "*.json"))
        {
            var name = Path.GetFileName(source);
            names.Add(name);
            complete &= TryCopy(source, Path.Combine(backup, name));
        }

        foreach (var old in Directory.GetFiles(backup, "*.json"))
        {
            if (!names.Contains(Path.GetFileName(old)))
            {
                complete &= TryDelete(old);
            }
        }

        return complete;
    }

    /// <summary>
    /// 手序文件夹整个不见了（UMM 卸载、回退删了整个 Mod 文件夹）而备份里有手序时，从备份还原。
    /// 返回还原的文件名；文件夹还在（哪怕是空的）或没有备份时为空，也不建文件夹。
    /// </summary>
    public static List<string> RestoreIfMissing(string folder, string backup)
    {
        var restored = new List<string>();
        if (Directory.Exists(folder) || !Directory.Exists(backup))
        {
            return restored;
        }

        var files = Directory.GetFiles(backup, "*.json");
        if (files.Length == 0)
        {
            return restored;
        }

        Directory.CreateDirectory(folder);
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(folder, name));
            File.SetLastWriteTimeUtc(Path.Combine(folder, name), File.GetLastWriteTimeUtc(file));
            restored.Add(name);
        }

        return restored;
    }

    /// <summary>大小和修改时间都一样的不再复制；复制后修改时间跟原文件一样（下次好比较）。</summary>
    private static bool TryCopy(string source, string target)
    {
        try
        {
            var from = new FileInfo(source);
            var to = new FileInfo(target);
            if (to.Exists && to.Length == from.Length && to.LastWriteTimeUtc == from.LastWriteTimeUtc)
            {
                return true;
            }

            File.Copy(source, target, overwrite: true);
            File.SetLastWriteTimeUtc(target, from.LastWriteTimeUtc);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // 浏览器正在写、被占用、刚被删：这一份下次再同步
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // 被占用：下次再删
        }
    }
}
