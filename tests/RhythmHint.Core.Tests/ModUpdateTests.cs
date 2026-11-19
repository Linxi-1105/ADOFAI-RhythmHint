using System.IO.Compression;
using System.Text;
using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 自动更新（2026-11-19 用户：Mod 可以联网，要全自动更新）：读 update.json、比版本、把发布包装进 Mod 文件夹。
/// </summary>
public class ModUpdateTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rhythmhint-update-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string Sha = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public void ReadsTheManifestThePackagingWrites()
    {
        var text = UpdateManifest.Write("1.3.1", "https://github.com/x/y/releases/download/v1.3.1/RhythmHint-1.3.1.zip", Sha.ToUpperInvariant());
        var manifest = UpdateManifest.Parse(text)!;
        Assert.Equal("1.3.1", manifest.Version);
        Assert.Equal("https://github.com/x/y/releases/download/v1.3.1/RhythmHint-1.3.1.zip", manifest.Url);
        Assert.Equal(Sha, manifest.Sha256);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>login page</html>")] // 被网络劫持成网页
    [InlineData("{\"format\":\"other\",\"version\":\"1.3.1\",\"url\":\"https://a/b.zip\",\"sha256\":\"" + Sha + "\"}")]
    [InlineData("{\"format\":\"rhythmhint-update\",\"version\":\"x\",\"url\":\"https://a/b.zip\",\"sha256\":\"" + Sha + "\"}")]
    [InlineData("{\"format\":\"rhythmhint-update\",\"version\":\"1.3.1\",\"url\":\"http://a/b.zip\",\"sha256\":\"" + Sha + "\"}")] // 只认 https
    [InlineData("{\"format\":\"rhythmhint-update\",\"version\":\"1.3.1\",\"url\":\"https://a/b.zip\",\"sha256\":\"abc\"}")]
    [InlineData("{\"format\":\"rhythmhint-update\",\"version\":\"1.3.1\",\"sha256\":\"" + Sha + "\"}")]
    public void RejectsBrokenManifests(string? text) => Assert.Null(UpdateManifest.Parse(text));

    [Fact]
    public void ALocalFileUrlIsAllowedForTryingItOut() =>
        Assert.NotNull(UpdateManifest.Parse(UpdateManifest.Write("9.9.9", "file:///D:/test/RhythmHint-9.9.9.zip", Sha)));

    [Theory]
    [InlineData("1.3.1", "1.3.0", true)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("2.0", "1.9.9", true)]
    [InlineData("1.3.0", "1.3.0", false)]
    [InlineData("1.2.9", "1.3.0", false)]
    [InlineData("1.3.1", "1.3.0-dev", false)] // 正在试的测试版不换掉
    [InlineData("1.4.0-beta", "1.3.0", false)] // 预发布版不自动装
    [InlineData("garbage", "1.3.0", false)]
    [InlineData("1.3.1", null, false)]
    public void InstallsOnlyARealNewerRelease(string latest, string? current, bool install) =>
        Assert.Equal(install, ModUpdate.ShouldInstall(latest, current));

    [Theory]
    [InlineData("https://github.com/x/y/releases/latest/download/Repository.json", "https://github.com/x/y/releases/latest/download/update.json")]
    [InlineData("file:///D:/test/Repository.json", "file:///D:/test/update.json")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("Repository.json", null)]
    public void TheManifestSitsNextToUmmsRepositoryFile(string? repository, string? expected) =>
        Assert.Equal(expected, ModUpdate.ManifestUrl(repository));

    [Fact]
    public void HashesFiles()
    {
        var path = Path.Combine(_root, "abc.txt");
        File.WriteAllText(path, "abc");
        Assert.Equal(Sha, ModUpdate.Sha256Of(path));
    }

    // ---------- 安装 ----------

    private string Zip(params (string Name, string Text)[] entries)
    {
        var path = Path.Combine(_root, "package-" + Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
            w.Write(text);
        }

        return path;
    }

    private (string Mod, string Work) Installed()
    {
        var mod = Path.Combine(_root, "Mods", "RhythmHint");
        Directory.CreateDirectory(Path.Combine(mod, "Editor"));
        Directory.CreateDirectory(Path.Combine(mod, "Fingerings"));
        File.WriteAllText(Path.Combine(mod, "Info.json"), "old info");
        File.WriteAllText(Path.Combine(mod, "RhythmHint.dll"), "old dll");
        File.WriteAllText(Path.Combine(mod, "Editor", "app.js"), "old app");
        File.WriteAllText(Path.Combine(mod, "Settings.xml"), "my settings");
        File.WriteAllText(Path.Combine(mod, "Fingerings", "Song.json"), "my fingering");
        return (mod, Path.Combine(mod, ".update"));
    }

    private static string Read(string mod, params string[] path) => File.ReadAllText(Path.Combine(new[] { mod }.Concat(path).ToArray()));

    [Fact]
    public void ReplacesOnlyWhatThePackageHasAndBacksUpTheOldFiles()
    {
        var (mod, work) = Installed();
        var zip = Zip(("RhythmHint/Info.json", "new info"), ("RhythmHint/RhythmHint.dll", "new dll"),
            ("RhythmHint/Editor/app.js", "new app"), ("RhythmHint/Editor/new.js", "brand new"));

        var files = ModUpdate.Install(zip, mod, work, "RhythmHint");

        Assert.Equal(4, files.Count);
        Assert.Equal("new info", Read(mod, "Info.json"));
        Assert.Equal("new dll", Read(mod, "RhythmHint.dll"));
        Assert.Equal("new app", Read(mod, "Editor", "app.js"));
        Assert.Equal("brand new", Read(mod, "Editor", "new.js"));
        Assert.Equal("my settings", Read(mod, "Settings.xml"));
        Assert.Equal("my fingering", Read(mod, "Fingerings", "Song.json"));
        Assert.Equal("old dll", Read(work, "backup", "RhythmHint.dll"));
        Assert.Equal("old app", Read(work, "backup", "Editor", "app.js"));
        Assert.False(Directory.Exists(Path.Combine(work, "new")));
    }

    [Theory]
    [InlineData("RhythmHint/../evil.dll")] // 写到 Mod 文件夹外面
    [InlineData("RhythmHint/../../Mods/Other/x.dll")]
    [InlineData("Other/RhythmHint.dll")] // 不是本 Mod 的文件夹
    public void RefusesPathsOutsideTheModFolder(string evil)
    {
        var (mod, work) = Installed();
        var zip = Zip(("RhythmHint/Info.json", "new info"), ("RhythmHint/RhythmHint.dll", "new dll"), (evil, "evil"));

        Assert.Throws<InvalidDataException>(() => ModUpdate.Install(zip, mod, work, "RhythmHint"));
        Assert.Equal("old info", Read(mod, "Info.json"));
        Assert.False(File.Exists(Path.Combine(_root, "Mods", "evil.dll")));
    }

    [Fact]
    public void RefusesAPackageWithoutTheModItself()
    {
        var (mod, work) = Installed();
        var zip = Zip(("RhythmHint/Info.json", "new info"));
        Assert.Throws<InvalidDataException>(() => ModUpdate.Install(zip, mod, work, "RhythmHint"));
        Assert.Equal("old info", Read(mod, "Info.json"));
    }

    /// <summary>覆盖到一半失败（文件被占用）：已换掉的从备份还原，新版才有的删掉，不留半新半旧。</summary>
    [Fact]
    public void AFailureHalfwayPutsTheOldFilesBack()
    {
        var (mod, work) = Installed();
        var zip = Zip(("RhythmHint/Editor/new.js", "brand new"), ("RhythmHint/Info.json", "new info"), ("RhythmHint/RhythmHint.dll", "new dll"), ("RhythmHint/Editor/app.js", "new app"));
        using (new FileStream(Path.Combine(mod, "Editor", "app.js"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            Assert.ThrowsAny<IOException>(() => ModUpdate.Install(zip, mod, work, "RhythmHint"));
        }

        Assert.Equal("old info", Read(mod, "Info.json"));
        Assert.Equal("old dll", Read(mod, "RhythmHint.dll"));
        Assert.Equal("old app", Read(mod, "Editor", "app.js"));
        Assert.False(File.Exists(Path.Combine(mod, "Editor", "new.js")));
    }
}
