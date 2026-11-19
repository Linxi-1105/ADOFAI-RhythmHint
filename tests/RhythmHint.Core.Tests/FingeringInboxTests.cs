using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 2026-11-19 玩家截图：游戏装在 C 盘（Steam 默认 C:\Program Files (x86)），编辑器点保存到游戏时浏览器弹
/// 「由于此文件夹包含系统文件，无法将其打开」。浏览器不让网页写这类文件夹，编辑器改存到文档里，Mod 搬进游戏的手序文件夹。
/// </summary>
public class FingeringInboxTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rhythmhint-inbox-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly string[] Roots = { @"C:\Program Files", @"C:\Program Files (x86)\", @"C:\Windows", @"C:\Users\A\AppData\Roaming", "", null! };

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice\Mods\RhythmHint\Fingerings", true)]
    [InlineData(@"c:\program files\Steam\steamapps\common\ADOFAI\Mods\RhythmHint\Fingerings", true)]
    [InlineData(@"C:\Program Files", true)]
    [InlineData(@"C:\Users\A\AppData\Roaming\Game\Mods", true)]
    [InlineData(@"E:\Program Files\Steam\steamapps\common\A Dance of Fire and Ice\Mods\RhythmHint\Fingerings", false)] // 别的盘上同名文件夹不算
    [InlineData(@"D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice\Mods\RhythmHint\Fingerings", false)]
    [InlineData(@"C:\Program Files Extra\Game", false)] // 名字只是开头一样
    [InlineData(@"C:\Users\A\Documents\RhythmHint\Fingerings", false)]
    public void KnowsWhichFoldersTheBrowserRefuses(string folder, bool blocked) =>
        Assert.Equal(blocked, FingeringInbox.BrowserBlocks(folder, Roots));

    [Fact]
    public void TheRealProgramFilesFolderIsAmongTheBlockedRoots()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.SkipWhen(string.IsNullOrEmpty(programFiles), "这台机器没有 Program Files");
        Assert.True(FingeringInbox.BrowserBlocks(Path.Combine(programFiles, "Steam", "Mods"), FingeringInbox.WindowsBlockedRoots()));
    }

    [Fact]
    public void MovesSavedFingeringsIntoTheGameFolder()
    {
        var inbox = Path.Combine(_root, "Documents", "RhythmHint", "Fingerings");
        var game = Path.Combine(_root, "Game", "Fingerings");
        Directory.CreateDirectory(inbox);
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "Old.json"), "old");
        File.WriteAllText(Path.Combine(inbox, "Old.json"), "new");
        File.WriteAllText(Path.Combine(inbox, "Song.json"), "song");
        File.WriteAllText(Path.Combine(inbox, "Song.json.crswap"), "half written"); // 浏览器写到一半的临时文件不碰

        var moved = FingeringInbox.MoveInto(inbox, game);

        Assert.Equal(new[] { "Old.json", "Song.json" }, moved.OrderBy(n => n));
        Assert.Equal("new", File.ReadAllText(Path.Combine(game, "Old.json")));
        Assert.Equal("song", File.ReadAllText(Path.Combine(game, "Song.json")));
        Assert.Equal(new[] { "Song.json.crswap" }, Directory.GetFiles(inbox).Select(Path.GetFileName));
    }

    /// <summary>搬走不是复制：玩家在游戏的文件夹里删掉一份手序，下一秒不会又出现。</summary>
    [Fact]
    public void ADeletedFingeringDoesNotComeBack()
    {
        var inbox = Path.Combine(_root, "inbox");
        var game = Path.Combine(_root, "game");
        Directory.CreateDirectory(inbox);
        File.WriteAllText(Path.Combine(inbox, "Song.json"), "song");
        FingeringInbox.MoveInto(inbox, game);
        File.Delete(Path.Combine(game, "Song.json"));

        Assert.Empty(FingeringInbox.MoveInto(inbox, game));
        Assert.False(File.Exists(Path.Combine(game, "Song.json")));
    }

    [Fact]
    public void AFileStillOpenInTheBrowserWaitsForTheNextPoll()
    {
        var inbox = Path.Combine(_root, "inbox");
        var game = Path.Combine(_root, "game");
        Directory.CreateDirectory(inbox);
        var path = Path.Combine(inbox, "Song.json");
        File.WriteAllText(path, "song");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Empty(FingeringInbox.MoveInto(inbox, game));
        }

        Assert.Equal(new[] { "Song.json" }, FingeringInbox.MoveInto(inbox, game));
    }

    [Fact]
    public void NoInboxMeansNothingToDo() =>
        Assert.Empty(FingeringInbox.MoveInto(Path.Combine(_root, "missing"), Path.Combine(_root, "game")));
}
