using System.Runtime.Versioning;
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

    /// <summary>一份能读的手序：认得 chart 这份谱面指纹，tiles 块砖（砖数不同，自带的指纹也不同）。</summary>
    private static string Fingering(string chart, int tiles, string author = "") =>
        "{\"format\":\"rhythmhint-fingering\",\"schemaVersion\":1,\"author\":\"" + author + "\",\"charts\":[\"" + chart + "\"],\"hits\":["
        + string.Join(",", Enumerable.Range(1, tiles).Select(i => $"[{i},{i},0,180]")) + "],\"notes\":["
        + string.Join(",", Enumerable.Range(1, tiles).Select(i => $"[{i},{i},[[2,0]]]")) + "]}";

    [Fact]
    public void MovesSavedFingeringsIntoTheGameFolder()
    {
        var inbox = Path.Combine(_root, "Documents", "RhythmHint", "Fingerings");
        var game = Path.Combine(_root, "Game", "Fingerings");
        Directory.CreateDirectory(inbox);
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "Old.json"), Fingering("1-a", 3, "old"));
        File.WriteAllText(Path.Combine(inbox, "Old.json"), Fingering("1-a", 3, "new")); // 同一关重新保存：覆盖
        File.WriteAllText(Path.Combine(inbox, "Song.json"), Fingering("2-b", 4));
        File.WriteAllText(Path.Combine(inbox, "Song.json.crswap"), "half written"); // 浏览器写到一半的临时文件不碰

        var moved = FingeringInbox.MoveInto(inbox, game);

        Assert.Equal(new[] { "Old.json", "Song.json" }, moved.OrderBy(n => n));
        Assert.Contains("\"new\"", File.ReadAllText(Path.Combine(game, "Old.json")));
        Assert.Equal(Fingering("2-b", 4), File.ReadAllText(Path.Combine(game, "Song.json")));
        Assert.Equal(new[] { "Song.json.crswap" }, Directory.GetFiles(inbox).Select(Path.GetFileName));
    }

    /// <summary>
    /// 同名的是另一首谱的手序（同一首歌不同谱师、别人分享的）时不覆盖，另存成「名字 (2).json」；之后再存这一关，覆盖的是 (2) 那份。
    /// 复现（2026-11-19 评审）：建议名只有「曲师 - 曲名」，原来直接覆盖，玩家自己的手序没了。
    /// </summary>
    [Fact]
    public void AnotherChartWithTheSameNameIsKept()
    {
        var inbox = Path.Combine(_root, "inbox");
        var game = Path.Combine(_root, "game");
        Directory.CreateDirectory(inbox);
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "Artist - Song.json"), Fingering("1-a", 3, "charter A"));
        File.WriteAllText(Path.Combine(game, "Broken.json"), "{ not a fingering");

        File.WriteAllText(Path.Combine(inbox, "Artist - Song.json"), Fingering("2-b", 4, "charter B"));
        File.WriteAllText(Path.Combine(inbox, "Broken.json"), Fingering("3-c", 5));
        Assert.Equal(new[] { "Artist - Song (2).json", "Broken (2).json" }, FingeringInbox.MoveInto(inbox, game).OrderBy(n => n));
        Assert.Contains("charter A", File.ReadAllText(Path.Combine(game, "Artist - Song.json")));
        Assert.Equal("{ not a fingering", File.ReadAllText(Path.Combine(game, "Broken.json"))); // 读不出来的也不盖掉

        File.WriteAllText(Path.Combine(inbox, "Artist - Song.json"), Fingering("2-b", 4, "charter B again"));
        Assert.Equal(new[] { "Artist - Song (2).json" }, FingeringInbox.MoveInto(inbox, game));
        Assert.Contains("charter B again", File.ReadAllText(Path.Combine(game, "Artist - Song (2).json")));
        Assert.Contains("charter A", File.ReadAllText(Path.Combine(game, "Artist - Song.json")));
        Assert.False(File.Exists(Path.Combine(game, "Artist - Song (3).json")));
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

    /// <summary>
    /// 建不了游戏的手序文件夹（这里用同名文件挡住）、列不出中转文件夹时不抛异常，说明原因、下一秒再试。
    /// 复现（2026-11-19 第二轮评审）：原来这两步在逐个文件的 try 外面，异常漏到 Overlay 的兜底，整个 Mod 停用。
    /// </summary>
    [Fact]
    public void AGameFolderThatCannotBeCreatedWaitsForTheNextPoll()
    {
        var inbox = Path.Combine(_root, "inbox");
        Directory.CreateDirectory(inbox);
        File.WriteAllText(Path.Combine(inbox, "Song.json"), "song");
        File.WriteAllText(Path.Combine(_root, "Mods"), "not a folder");
        var game = Path.Combine(_root, "Mods", "Fingerings");

        Assert.Empty(FingeringInbox.MoveInto(inbox, game, out var problem));
        Assert.NotEmpty(problem);
        Assert.True(File.Exists(Path.Combine(inbox, "Song.json")));

        File.Delete(Path.Combine(_root, "Mods"));
        Assert.Equal(new[] { "Song.json" }, FingeringInbox.MoveInto(inbox, game, out problem));
        Assert.Empty(problem);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void AnInboxThatCannotBeListedWaitsForTheNextPoll()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "用 Windows 的文件夹权限挡住列目录");
        var inbox = Path.Combine(_root, "inbox");
        Directory.CreateDirectory(inbox);
        File.WriteAllText(Path.Combine(inbox, "Song.json"), "song");
        using (new DenyListing(inbox))
        {
            Assert.Empty(FingeringInbox.MoveInto(inbox, Path.Combine(_root, "game"), out var problem));
            Assert.NotEmpty(problem);
        }

        Assert.False(Directory.Exists(Path.Combine(_root, "game")));
        Assert.Equal(new[] { "Song.json" }, FingeringInbox.MoveInto(inbox, Path.Combine(_root, "game")));
    }

    /// <summary>中转文件夹是空的就不建游戏的手序文件夹，不凭空多出一个空文件夹。</summary>
    [Fact]
    public void AnEmptyInboxCreatesNothing()
    {
        var inbox = Path.Combine(_root, "inbox");
        Directory.CreateDirectory(inbox);

        Assert.Empty(FingeringInbox.MoveInto(inbox, Path.Combine(_root, "game")));
        Assert.False(Directory.Exists(Path.Combine(_root, "game")));
    }

    [Fact]
    public void NoInboxMeansNothingToDo() =>
        Assert.Empty(FingeringInbox.MoveInto(Path.Combine(_root, "missing"), Path.Combine(_root, "game")));
}
