using System.Runtime.Versioning;
using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 2026-11-19 评审：手序文件夹在 Mod 文件夹里，UMM 的卸载、回退（Revert）会删掉整个 Mod 文件夹（UnityModManagerApp/Mods.cs 的 UninstallMod），
/// 玩家自己做的手序跟着没了。Mod 把手序同步一份到文档里，手序文件夹整个不见了时还原。
/// </summary>
public class FingeringBackupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rhythmhint-backup-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder => Path.Combine(_root, "Mods", "RhythmHint", "Fingerings");

    private string Backup => Path.Combine(_root, "Documents", "RhythmHint", "FingeringsBackup");

    private static string[] Names(string dir) => Sorted(Directory.GetFiles(dir).Select(Path.GetFileName)!);

    private static string[] Sorted(IEnumerable<string> names) => names.OrderBy(n => n, StringComparer.Ordinal).ToArray();

    /// <summary>一次运行（启动游戏到关掉）里 Mod 记着的「见过的手序」。</summary>
    private static HashSet<string> Run() => new(StringComparer.OrdinalIgnoreCase);

    private void InBackup(params string[] names)
    {
        Directory.CreateDirectory(Backup);
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(Backup, name), "old " + name);
        }
    }

    private static FileStream Lock(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.None);

    [Fact]
    public void TheBackupFollowsTheFingeringsFolder()
    {
        var run = Run();
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "A.json"), "a");
        File.WriteAllText(Path.Combine(Folder, "B.json"), "b");
        File.WriteAllText(Path.Combine(Folder, "B.json.crswap"), "half written"); // 浏览器写到一半的不备份

        Assert.True(FingeringBackup.Mirror(Folder, Backup, run));
        Assert.Equal(new[] { "A.json", "B.json" }, Names(Backup));

        File.WriteAllText(Path.Combine(Folder, "A.json"), "a, edited");
        File.Delete(Path.Combine(Folder, "B.json")); // 玩家在游戏的文件夹里删掉的，备份里也删
        File.WriteAllText(Path.Combine(Folder, "C.json"), "c");
        Assert.True(FingeringBackup.Mirror(Folder, Backup, run));

        Assert.Equal(new[] { "A.json", "C.json" }, Names(Backup));
        Assert.Equal("a, edited", File.ReadAllText(Path.Combine(Backup, "A.json")));
    }

    [Fact]
    public void AMissingFingeringsFolderNeverEmptiesTheBackup()
    {
        InBackup("A.json");

        Assert.True(FingeringBackup.Mirror(Folder, Backup, Run()));

        Assert.Equal(new[] { "A.json" }, Names(Backup));
    }

    /// <summary>
    /// 备份里有、这次运行在手序文件夹里从没见过的不删：另一份游戏安装的、游戏关着时删掉的、还没还原回来的。
    /// 复现（2026-11-19 第二轮评审）：两份安装共用一个备份，原来每次同步都把对方的手序删掉。
    /// </summary>
    [Fact]
    public void TwoInstallsSharingTheBackupKeepEachOthersFingerings()
    {
        var other = Path.Combine(_root, "Other", "Mods", "RhythmHint", "Fingerings");
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(Folder, "A.json"), "a");
        File.WriteAllText(Path.Combine(other, "B.json"), "b");
        var mine = Run();
        var theirs = Run();

        FingeringBackup.Mirror(Folder, Backup, mine);
        FingeringBackup.Mirror(other, Backup, theirs);
        FingeringBackup.Mirror(Folder, Backup, mine);
        Assert.Equal(new[] { "A.json", "B.json" }, Names(Backup));

        // 在这份安装里删掉的照样从备份里删；删过以后对方再备份同名的，不会又被这边删掉
        File.Delete(Path.Combine(Folder, "A.json"));
        FingeringBackup.Mirror(Folder, Backup, mine);
        Assert.Equal(new[] { "B.json" }, Names(Backup));
        File.WriteAllText(Path.Combine(other, "A.json"), "their a");
        FingeringBackup.Mirror(other, Backup, theirs);
        FingeringBackup.Mirror(Folder, Backup, mine);
        Assert.Equal(new[] { "A.json", "B.json" }, Names(Backup));
    }

    [Fact]
    public void RestoresWhenTheWholeFolderIsGone()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "A.json"), "a");
        FingeringBackup.Mirror(Folder, Backup, Run());
        Directory.Delete(Path.Combine(_root, "Mods", "RhythmHint"), recursive: true); // UMM 卸载、回退

        var result = FingeringBackup.Restore(Folder, Backup);

        Assert.Equal(new[] { "A.json" }, result.Restored);
        Assert.Empty(result.Pending);
        Assert.Equal("a", File.ReadAllText(Path.Combine(Folder, "A.json")));
        Assert.False(File.Exists(FingeringBackup.PendingList(Folder)));
    }

    /// <summary>手序文件夹还在（哪怕是空的）就以它为准：玩家删掉的手序不会被还原回来。</summary>
    [Fact]
    public void AnExistingFolderIsNotRestored()
    {
        InBackup("A.json");
        Directory.CreateDirectory(Folder);

        Assert.Empty(FingeringBackup.Restore(Folder, Backup).Restored);
        Assert.Empty(Directory.GetFiles(Folder));
    }

    [Fact]
    public void NothingToRestoreLeavesNoFolderBehind()
    {
        Assert.Empty(FingeringBackup.Restore(Folder, Backup).Restored);
        Directory.CreateDirectory(Backup);
        Assert.Empty(FingeringBackup.Restore(Folder, Backup).Restored);

        Assert.False(Directory.Exists(Folder));
        Assert.False(File.Exists(FingeringBackup.PendingList(Folder)));
    }

    /// <summary>
    /// 还原到一半有一份读不了（杀毒软件在扫、OneDrive 里只在云端的文件此刻离线）：能还原的先还原，读不了的记进待还原清单，
    /// 备份里那份这次运行不会被删、也不会被同名的新手序盖掉，下次启动接着还原。
    /// 复现（2026-11-19 第二轮评审）：原来一份出错就整个停下，留下缺几份的文件夹；同一秒同步时把没还原的从备份里删了，下次启动也不再还原。
    /// </summary>
    [Fact]
    public void ARestoreThatFailsHalfwayKeepsTheRest()
    {
        InBackup("a.json", "b.json", "c.json");
        RestoreResult first;
        using (Lock(Path.Combine(Backup, "b.json")))
        {
            first = FingeringBackup.Restore(Folder, Backup);
        }

        Assert.Equal(new[] { "a.json", "c.json" }, Sorted(first.Restored));
        Assert.Equal(new[] { "b.json" }, first.Pending);
        Assert.NotEmpty(first.Error);
        Assert.Equal(new[] { "b.json" }, FingeringBackup.ReadPending(Folder));

        var run = Run();
        FingeringBackup.Mirror(Folder, Backup, run); // 同一秒：文件夹变了 → 同步
        File.WriteAllText(Path.Combine(Folder, "b.json"), "new b"); // 玩家以为丢了，重新做了一份同名的
        File.Delete(Path.Combine(Folder, "a.json"));
        FingeringBackup.Mirror(Folder, Backup, run);
        Assert.Equal(new[] { "b.json", "c.json" }, Names(Backup));
        Assert.Equal("old b.json", File.ReadAllText(Path.Combine(Backup, "b.json")));

        // 下次启动：接着还原 b，同名的新手序不覆盖，另存成 b (2).json；之后两份都备份
        var next = FingeringBackup.Restore(Folder, Backup);
        Assert.Equal(new[] { "b (2).json" }, next.Restored);
        Assert.Empty(next.Pending);
        Assert.False(File.Exists(FingeringBackup.PendingList(Folder)));
        Assert.Equal("new b", File.ReadAllText(Path.Combine(Folder, "b.json")));
        Assert.Equal("old b.json", File.ReadAllText(Path.Combine(Folder, "b (2).json")));
        FingeringBackup.Mirror(Folder, Backup, Run());
        Assert.Equal(new[] { "b (2).json", "b.json", "c.json" }, Names(Backup));
        Assert.Equal("new b", File.ReadAllText(Path.Combine(Backup, "b.json")));
    }

    /// <summary>
    /// 一份都还原不了时文件夹照样建出来（编辑器要存），全部记在待还原清单里，下次启动还原。
    /// 复现（2026-11-19 第二轮评审）：原来第一份就失败时留下空文件夹，以后不再还原，玩家再存任何一份手序，备份就被清空。
    /// </summary>
    [Fact]
    public void ARestoreThatFailsEntirelyIsRetriedNextTime()
    {
        InBackup("a.json");
        using (Lock(Path.Combine(Backup, "a.json")))
        {
            Assert.Empty(FingeringBackup.Restore(Folder, Backup).Restored);
        }

        Assert.Empty(Directory.GetFiles(Folder));
        File.WriteAllText(Path.Combine(Folder, "new.json"), "new");
        FingeringBackup.Mirror(Folder, Backup, Run());
        Assert.Equal(new[] { "a.json", "new.json" }, Names(Backup));

        Assert.Equal(new[] { "a.json" }, FingeringBackup.Restore(Folder, Backup).Restored);
        Assert.Empty(FingeringBackup.ReadPending(Folder));
        Assert.Equal(new[] { "a.json", "new.json" }, Names(Folder));
    }

    /// <summary>上次复制到一半游戏就被关掉（清单还在）：已经复制过的不重复还原，剩下的接着还原。</summary>
    [Fact]
    public void AnInterruptedRestoreContinues()
    {
        InBackup("a.json", "b.json");
        Directory.CreateDirectory(Folder);
        File.WriteAllLines(FingeringBackup.PendingList(Folder), new[] { "a.json", "b.json" });
        File.Copy(Path.Combine(Backup, "a.json"), Path.Combine(Folder, "a.json"));
        File.SetLastWriteTimeUtc(Path.Combine(Folder, "a.json"), File.GetLastWriteTimeUtc(Path.Combine(Backup, "a.json")));

        Assert.Equal(new[] { "b.json" }, FingeringBackup.Restore(Folder, Backup).Restored);
        Assert.Equal(new[] { "a.json", "b.json" }, Names(Folder));
    }

    /// <summary>
    /// 还原时连备份都列不出来（没有权限、网络盘断开）：记下「全部待还原」，文件夹之后被建出来（编辑器存了新手序）也不同步、不删备份，
    /// 下次启动把备份里的都还原回来。
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void ABackupThatCannotBeListedIsRestoredLater()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "用 Windows 的文件夹权限挡住列目录");
        InBackup("a.json");
        using (new DenyListing(Backup))
        {
            Assert.ThrowsAny<UnauthorizedAccessException>(() => FingeringBackup.Restore(Folder, Backup));
        }

        Assert.False(Directory.Exists(Folder));
        Assert.Equal(new[] { FingeringBackup.All }, FingeringBackup.ReadPending(Folder));
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "new.json"), "new");
        Assert.True(FingeringBackup.Mirror(Folder, Backup, Run()));
        Assert.Equal(new[] { "a.json" }, Names(Backup));

        Assert.Equal(new[] { "a.json" }, FingeringBackup.Restore(Folder, Backup).Restored);
        Assert.Equal(new[] { "a.json", "new.json" }, Names(Folder));
        Assert.Empty(FingeringBackup.ReadPending(Folder));
    }

    /// <summary>待还原清单只认 .json 文件名：被改坏了也不会从别处复制、复制到别处。</summary>
    [Fact]
    public void ThePendingListOnlyHoldsFileNames()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Folder)!);
        File.WriteAllLines(FingeringBackup.PendingList(Folder), new[] { @"..\..\evil.json", "sub/x.json", "C:x.json", "notes.txt", " ok.json ", "" });

        Assert.Equal(new[] { "ok.json" }, FingeringBackup.ReadPending(Folder));
    }

    [Fact]
    public void AFileInUseIsBackedUpNextTime()
    {
        var run = Run();
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, "A.json");
        File.WriteAllText(path, "a");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(FingeringBackup.Mirror(Folder, Backup, run));
        }

        Assert.True(FingeringBackup.Mirror(Folder, Backup, run));
        Assert.Equal("a", File.ReadAllText(Path.Combine(Backup, "A.json")));
    }
}
