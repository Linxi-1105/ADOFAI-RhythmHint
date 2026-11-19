using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RhythmHint.Core;

/// <summary>
/// 专属手序的备份。手序文件夹（Mods/RhythmHint/Fingerings）在 Mod 文件夹里，UMM 的卸载、回退（Revert）会把整个 Mod 文件夹删掉
/// （UnityModManagerApp/Mods.cs 的 UninstallMod：Directory.Delete(modInfo.Path, true)），玩家自己做的手序跟着没了（2026-11-19 评审）。
/// Mod 每次看到手序文件夹有变化就同步一份到「文档\RhythmHint\FingeringsBackup」，手序文件夹整个不见了时从那里还原。
/// 以游戏的手序文件夹为准：那里删掉的，备份里也删；只有整个文件夹都没了（或者上次没还原完）才还原，玩家删掉的单个手序不会被还原回来。
/// 备份文件夹故意不叫 Fingerings：编辑器选保存位置时只认这个名字，选到备份会提示，不会存进备份里。
/// <para>
/// 备份只删「这次运行里在手序文件夹见过、后来不见了」的文件（2026-11-19 第二轮评审：还原到一半失败后，下一次同步把没还原的备份全删了；
/// 两份游戏安装共用一个备份时互相删掉对方的）。没见过的不删：别的安装的、游戏关着时删掉的、还没还原回来的都留着。
/// </para>
/// </summary>
public static class FingeringBackup
{
    /// <summary>
    /// 还没从备份还原回来的手序清单，放在手序文件夹旁边（Mods/RhythmHint/Fingerings.restore.txt），一行一个文件名（一行 <see cref="All"/> 表示全部）。
    /// 还原时先写它再建文件夹、复制，有复制不了的（杀毒软件在扫、OneDrive 里只在云端的文件此刻离线）留在清单里，下次启动接着还原；
    /// 全部还原完就删掉。它和手序文件夹一样在 Mod 文件夹里，UMM 删 Mod 文件夹时一起删掉（那时整个手序文件夹也没了，重新完整还原）。
    /// 有它在，手序文件夹之后被建出来（编辑器存了新手序、搬中转文件夹）也不耽误下次接着还原。
    /// 不用「先还原到临时文件夹再改名」：文件夹里有文件正被别的程序打开时（杀毒软件扫刚复制的文件）Windows 不让改名（实测
    /// FileShare.Delete 也不行），那样每次启动都可能还原失败。
    /// </summary>
    public static string PendingList(string folder) =>
        folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".restore.txt";

    /// <summary>
    /// 把 <paramref name="folder"/> 里的 .json 同步到 <paramref name="backup"/>：新的、改过的复制过去；
    /// <paramref name="seen"/> 里记着的（这次运行在手序文件夹见过的）现在不见了，就在备份里也删掉。
    /// 还在待还原清单里的不动：备份里那份是唯一的一份，手序文件夹里同名的新文件等还原完再备份。
    /// 手序文件夹不存在时什么都不做，不会把备份清空。有文件被占用（浏览器正在写）时返回 false，下次再同步。
    /// </summary>
    /// <param name="seen">同一次运行每次都传同一个集合（不分大小写），由这里更新。</param>
    public static bool Mirror(string folder, string backup, HashSet<string> seen)
    {
        if (!Directory.Exists(folder))
        {
            return true;
        }

        var pending = new HashSet<string>(ReadPending(folder), StringComparer.OrdinalIgnoreCase);
        if (pending.Contains(All))
        {
            return true; // 上次连备份都列不出来、还没还原：备份整个不动，等下次启动还原完再同步
        }

        Directory.CreateDirectory(backup);
        var complete = true;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in Directory.GetFiles(folder, "*.json"))
        {
            var name = Path.GetFileName(source);
            names.Add(name);
            if (!pending.Contains(name))
            {
                complete &= TryCopy(source, Path.Combine(backup, name));
            }
        }

        foreach (var name in new List<string>(seen))
        {
            if (names.Contains(name) || pending.Contains(name))
            {
                continue;
            }

            if (TryDelete(Path.Combine(backup, name)))
            {
                seen.Remove(name); // 删掉以后不再管它：别的安装之后再备份同名的手序，不会又被这里删掉
            }
            else
            {
                complete = false;
            }
        }

        seen.UnionWith(names);
        return complete;
    }

    /// <summary>
    /// 启动时调用：手序文件夹整个不见了（UMM 卸载、回退删了整个 Mod 文件夹）而备份里有手序时，从备份还原；
    /// 文件夹还在、但上次有没还原完的（见 <see cref="PendingList"/>），接着还原那几份。
    /// 文件夹还在（哪怕是空的）又没有待还原的，或者没有备份时什么都不做，也不建文件夹。
    /// 已经有同名文件的（上次没还原完时玩家又存了一份）不覆盖，另存成「名字 (2).json」，两份都留着。
    /// </summary>
    /// <exception cref="IOException">列不出备份、写不了待还原清单或建不了文件夹：手序文件夹保持原样，下次启动再试。</exception>
    /// <exception cref="UnauthorizedAccessException">同上，没有权限。</exception>
    public static RestoreResult Restore(string folder, string backup)
    {
        var restored = new List<string>();
        var list = PendingList(folder);
        List<string> pending;
        if (!Directory.Exists(folder))
        {
            if (!Directory.Exists(backup))
            {
                return new RestoreResult(restored, new List<string>(), string.Empty);
            }

            try
            {
                pending = BackupNames(backup);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 备份此刻列不出来：先记下「备份里的都待还原」再报错。之后手序文件夹被建出来（编辑器存了新手序）也照样接着还原
                WritePending(list, new List<string> { All });
                throw;
            }

            if (pending.Count == 0)
            {
                return new RestoreResult(restored, pending, string.Empty);
            }

            // 先记下要还原哪些、再建文件夹：中途失败或者游戏被关掉，下次启动看到清单接着还原，不会因为文件夹已经在了就不管
            WritePending(list, pending);
            Directory.CreateDirectory(folder);
        }
        else
        {
            pending = ReadPending(folder);
            if (pending.Count == 0)
            {
                return new RestoreResult(restored, pending, string.Empty);
            }

            if (!Directory.Exists(backup))
            {
                // 备份文件夹此刻看不到（文档在网络盘上、离线）：清单原样留着，不当成「备份里已经没有了」
                return new RestoreResult(restored, pending, $"找不到备份文件夹 {backup}");
            }

            if (pending.Contains(All))
            {
                pending = BackupNames(backup); // 列不出来时照旧报错，清单还是「全部」
            }
        }

        var left = new List<string>();
        var error = string.Empty;
        foreach (var name in pending)
        {
            try
            {
                if (RestoreOne(Path.Combine(backup, name), folder, name) is { } target)
                {
                    restored.Add(target);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                left.Add(name); // 这一份暂时读不了：留在清单里，下次启动再还原
                error = error.Length > 0 ? error : ex.Message;
            }
        }

        try
        {
            if (left.Count == 0)
            {
                File.Delete(list);
            }
            else if (left.Count < pending.Count)
            {
                WritePending(list, left);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清单改不了：多列的那几份下次启动会发现已经还原过了（同样的文件已经在），不会重复还原
            error = error.Length > 0 ? error : ex.Message;
        }

        return new RestoreResult(restored, left, error);
    }

    /// <summary>待还原清单里的文件名（<see cref="All"/> = 备份里的全部）；没有清单为空。</summary>
    public static List<string> ReadPending(string folder)
    {
        var names = new List<string>();
        var list = PendingList(folder);
        if (!File.Exists(list))
        {
            return names;
        }

        foreach (var line in File.ReadAllLines(list, Encoding.UTF8))
        {
            var name = line.Trim();
            if (name == All
                || (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0))
            {
                names.Add(name); // 只认 .json 文件名，不带路径（斜杠、冒号都不是文件名里的字）：清单被改坏了也不会还原到别处
            }
        }

        return names;
    }

    /// <summary>待还原清单里的这一行表示「备份里的都待还原」（还原时连备份都列不出来）。</summary>
    public const string All = "*";

    private static List<string> BackupNames(string backup)
    {
        var names = new List<string>();
        foreach (var file in Directory.GetFiles(backup, "*.json"))
        {
            names.Add(Path.GetFileName(file));
        }

        return names;
    }

    private static void WritePending(string list, List<string> names)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(list)!);
        File.WriteAllLines(list, names, new UTF8Encoding(false));
    }

    /// <summary>
    /// 把备份里的一份复制回手序文件夹，修改时间跟备份一样（同步时好比较）。备份里已经没有了（别的安装删掉了）、
    /// 或者同样的文件已经在了（上次复制完没来得及改清单）时返回 null；同名的是另一份时用下一个「名字 (k).json」。
    /// </summary>
    private static string? RestoreOne(string source, string folder, string name)
    {
        var from = new FileInfo(source);
        if (!from.Exists)
        {
            return null;
        }

        for (var k = 1; k <= FingeringInbox.MaxNumbered; k++)
        {
            var candidate = FingeringInbox.Numbered(name, k);
            var to = new FileInfo(Path.Combine(folder, candidate));
            if (to.Exists)
            {
                if (to.Length == from.Length && to.LastWriteTimeUtc == from.LastWriteTimeUtc)
                {
                    return null;
                }

                continue;
            }

            File.Copy(source, to.FullName);
            File.SetLastWriteTimeUtc(to.FullName, from.LastWriteTimeUtc);
            return candidate;
        }

        throw new IOException($"同名的手序已有 {FingeringInbox.MaxNumbered} 份，{name} 没有还原");
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

/// <summary>一次还原的结果。</summary>
/// <param name="Restored">还原到手序文件夹里的文件名（同名时是另存的「名字 (2).json」）。</param>
/// <param name="Pending">暂时读不了、下次启动再还原的文件名（备份里的名字）。</param>
/// <param name="Error">第一份读不了的原因（给日志）；都还原了为空。</param>
public sealed record RestoreResult(List<string> Restored, List<string> Pending, string Error);
