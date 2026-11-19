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

    private static string[] Names(string dir) => Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(n => n).ToArray()!;

    [Fact]
    public void TheBackupFollowsTheFingeringsFolder()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "A.json"), "a");
        File.WriteAllText(Path.Combine(Folder, "B.json"), "b");
        File.WriteAllText(Path.Combine(Folder, "B.json.crswap"), "half written"); // 浏览器写到一半的不备份

        Assert.True(FingeringBackup.Mirror(Folder, Backup));
        Assert.Equal(new[] { "A.json", "B.json" }, Names(Backup));

        File.WriteAllText(Path.Combine(Folder, "A.json"), "a, edited");
        File.Delete(Path.Combine(Folder, "B.json")); // 玩家在游戏的文件夹里删掉的，备份里也删
        File.WriteAllText(Path.Combine(Folder, "C.json"), "c");
        Assert.True(FingeringBackup.Mirror(Folder, Backup));

        Assert.Equal(new[] { "A.json", "C.json" }, Names(Backup));
        Assert.Equal("a, edited", File.ReadAllText(Path.Combine(Backup, "A.json")));
    }

    [Fact]
    public void AMissingFingeringsFolderNeverEmptiesTheBackup()
    {
        Directory.CreateDirectory(Backup);
        File.WriteAllText(Path.Combine(Backup, "A.json"), "a");

        Assert.True(FingeringBackup.Mirror(Folder, Backup));

        Assert.Equal(new[] { "A.json" }, Names(Backup));
    }

    [Fact]
    public void RestoresWhenTheWholeFolderIsGone()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "A.json"), "a");
        FingeringBackup.Mirror(Folder, Backup);
        Directory.Delete(Path.Combine(_root, "Mods", "RhythmHint"), recursive: true); // UMM 卸载、回退

        Assert.Equal(new[] { "A.json" }, FingeringBackup.RestoreIfMissing(Folder, Backup));
        Assert.Equal("a", File.ReadAllText(Path.Combine(Folder, "A.json")));
    }

    /// <summary>手序文件夹还在（哪怕是空的）就以它为准：玩家删掉的手序不会被还原回来。</summary>
    [Fact]
    public void AnExistingFolderIsNotRestored()
    {
        Directory.CreateDirectory(Backup);
        File.WriteAllText(Path.Combine(Backup, "A.json"), "a");
        Directory.CreateDirectory(Folder);

        Assert.Empty(FingeringBackup.RestoreIfMissing(Folder, Backup));
        Assert.Empty(Directory.GetFiles(Folder));
    }

    [Fact]
    public void NothingToRestoreLeavesNoFolderBehind()
    {
        Assert.Empty(FingeringBackup.RestoreIfMissing(Folder, Backup));
        Assert.False(Directory.Exists(Folder));
    }

    [Fact]
    public void AFileInUseIsBackedUpNextTime()
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, "A.json");
        File.WriteAllText(path, "a");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(FingeringBackup.Mirror(Folder, Backup));
        }

        Assert.True(FingeringBackup.Mirror(Folder, Backup));
        Assert.Equal("a", File.ReadAllText(Path.Combine(Backup, "A.json")));
    }
}
