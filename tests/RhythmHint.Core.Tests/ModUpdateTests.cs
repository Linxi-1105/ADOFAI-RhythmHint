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
    [InlineData("[1, 2]")] // 不是对象
    // 字段类型不对：Newtonsoft 转字符串时抛 ArgumentException，原来没接住，协程中断、状态一直停在「检查中」
    [InlineData("{\"format\":\"rhythmhint-update\",\"version\":{\"major\":1},\"url\":\"https://a/b.zip\",\"sha256\":\"" + Sha + "\"}")]
    [InlineData("{\"format\":\"rhythmhint-update\",\"version\":\"1.3.1\",\"url\":[\"https://a/b.zip\"],\"sha256\":\"" + Sha + "\"}")]
    [InlineData("{\"format\":[\"rhythmhint-update\"],\"version\":\"1.3.1\",\"url\":\"https://a/b.zip\",\"sha256\":\"" + Sha + "\"}")]
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

    // ---------- 国内加速镜像（2026-11-19 玩家日志：连不上 GitHub，自动更新失败；用户选了加速镜像站） ----------

    private static readonly string[] Mirrors = { "https://ghfast.top/", "https://gh-proxy.com/" };

    [Fact]
    public void AGitHubAddressIsTriedDirectlyFirstThenThroughEachMirror()
    {
        const string url = "https://github.com/x/y/releases/latest/download/update.json";
        Assert.Equal(
            new[] { url, "https://ghfast.top/" + url, "https://gh-proxy.com/" + url },
            ModUpdate.Sources(url, Mirrors));
    }

    [Theory]
    [InlineData("file:///D:/test/update.json")] // 本地试的假清单
    [InlineData("https://example.com/update.json")] // 镜像站只转 GitHub 的地址
    public void OtherAddressesAreOnlyTriedDirectly(string url) =>
        Assert.Equal(new[] { url }, ModUpdate.Sources(url, Mirrors));

    [Fact]
    public void ReadsTheMirrorListFromTheBuildSetting() =>
        Assert.Equal(
            new[] { "https://ghfast.top/", "https://gh-proxy.com/", "https://ghproxy.net/" },
            ModUpdate.ParseMirrors(" https://ghfast.top/ ; https://gh-proxy.com;;https://ghproxy.net/;https://ghfast.top/"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://plain.example/")] // 只认 https
    [InlineData("ghfast.top")] // 不是完整地址
    [InlineData("https://ghfast.top/?a=1")] // 带查询串拼不出地址
    public void IgnoresMirrorsThatCannotBeUsed(string? setting) => Assert.Empty(ModUpdate.ParseMirrors(setting));

    [Fact]
    public void PicksTheNewestManifestBecauseAMirrorMayServeAnOldCachedOne()
    {
        var manifests = new[]
        {
            null, // 直连连不上
            UpdateManifest.Parse(UpdateManifest.Write("1.3.2", "https://github.com/x/y/a.zip", Sha)),
            UpdateManifest.Parse(UpdateManifest.Write("1.3.3", "https://github.com/x/y/b.zip", Sha)),
            UpdateManifest.Parse(UpdateManifest.Write("1.3.3", "https://github.com/x/y/c.zip", Sha)),
        };
        Assert.Equal(2, ModUpdate.Newest(manifests)); // 一样新时取靠前的来源
    }

    [Fact]
    public void NoManifestMeansNoSource() => Assert.Equal(-1, ModUpdate.Newest(new UpdateManifest?[] { null, null }));

    [Fact]
    public void TheSourceThatGaveTheManifestIsTriedFirstForThePackage() =>
        Assert.Equal(new[] { "c", "a", "b", "d" }, ModUpdate.FirstThen(new[] { "a", "b", "c", "d" }, 2));

    [Fact]
    public void AnUnknownFirstSourceKeepsTheOrder() =>
        Assert.Equal(new[] { "a", "b" }, ModUpdate.FirstThen(new[] { "a", "b" }, 5));

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
    [InlineData("RhythmHint/bad\0name.js")] // 非法字符：原来 ArgumentException 漏出去，自动更新卡在「正在下载」
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

    /// <summary>复制到一半失败、目标已经被截断（磁盘满、杀毒软件干扰）：写了一半的文件写进目标，然后抛出。</summary>
    private static Action<string, string> FailHalfwayAt(string name) => (from, to) =>
    {
        if (Path.GetFileName(to) == name)
        {
            File.WriteAllText(to, "half");
            throw new IOException("磁盘空间不足");
        }

        File.Copy(from, to, overwrite: true);
    };

    /// <summary>
    /// 复现：1.3.1 的包里第二个就是 RhythmHint.dll，它复制到一半失败时没被还原（只还原复制完的），
    /// Info.json 却换回了旧的：DLL 是坏的，UMM 下次整个 Mod 都加载不了。
    /// </summary>
    [Fact]
    public void TheFileThatFailedHalfwayIsPutBackToo()
    {
        var (mod, work) = Installed();
        var zip = Zip(("RhythmHint/Info.json", "new info"), ("RhythmHint/RhythmHint.dll", "new dll"), ("RhythmHint/Editor/app.js", "new app"));

        Assert.Throws<IOException>(() => ModUpdate.Install(zip, mod, work, "RhythmHint", FailHalfwayAt("RhythmHint.dll")));

        Assert.Equal("old info", Read(mod, "Info.json"));
        Assert.Equal("old dll", Read(mod, "RhythmHint.dll"));
        Assert.Equal("old app", Read(mod, "Editor", "app.js"));
    }

    [Fact]
    public void ANewFileThatFailedHalfwayIsRemoved()
    {
        var (mod, work) = Installed();
        var zip = Zip(("RhythmHint/Info.json", "new info"), ("RhythmHint/RhythmHint.dll", "new dll"), ("RhythmHint/Editor/brand/new.js", "brand new"));

        Assert.Throws<IOException>(() => ModUpdate.Install(zip, mod, work, "RhythmHint", FailHalfwayAt("new.js")));

        Assert.Equal("old info", Read(mod, "Info.json"));
        Assert.Equal("old dll", Read(mod, "RhythmHint.dll"));
        Assert.False(File.Exists(Path.Combine(mod, "Editor", "brand", "new.js")));
    }

    [Fact]
    public void ALeftoverStagingFolderIsNotAFailedInstall()
    {
        // 复现：全部覆盖成功后删临时文件夹 .update\new 失败（文件被占用），原来会被报成「安装失败」，其实新版已经装好
        var (mod, work) = Installed();
        var zip = Zip(("RhythmHint/Info.json", "new info"), ("RhythmHint/RhythmHint.dll", "new dll"));
        var locks = new List<FileStream>();
        try
        {
            var files = ModUpdate.Install(zip, mod, work, "RhythmHint", (from, to) =>
            {
                File.Copy(from, to, overwrite: true);
                locks.Add(new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read)); // 一直占着，删不掉
            });

            Assert.Equal(2, files.Count);
            Assert.Equal("new info", Read(mod, "Info.json"));
            Assert.Equal("new dll", Read(mod, "RhythmHint.dll"));
        }
        finally
        {
            locks.ForEach(l => l.Dispose());
        }
    }

    // ---------- 已发布的安装代码装现在的包 ----------

    private static readonly string RepoRoot = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根目录（Directory.Build.props）");
    }

    /// <summary>Mod 项目文件里的一个属性（打包用的模板）。</summary>
    private static string Property(System.Xml.Linq.XDocument project, string name) =>
        project.Descendants(name).Select(e => e.Value).FirstOrDefault() ?? throw new InvalidOperationException("项目文件里没有 " + name);

    /// <summary>
    /// 1.3.1 → 下一版这一跳永远由 1.3.1 已经发布的代码执行（Frozen/ModUpdate131.cs 是它的原样副本）：
    /// 照 RhythmHint.Mod.csproj 的 PackageForUmm 打出现在的包和 update.json，交给 1.3.1 的代码读清单、核对、安装，要一路走通。
    /// 改打包方式（包里的文件夹、update.json 的字段、清单地址）时这里会失败：那样已经装了 1.3.1 的玩家就再也自动更新不了。
    /// </summary>
    [Fact]
    public void TheReleasedInstallerStillInstallsTodaysPackage()
    {
        var project = System.Xml.Linq.XDocument.Load(Path.Combine(RepoRoot, "src", "RhythmHint.Mod", "RhythmHint.Mod.csproj"));
        var modId = Property(project, "ModId");
        var repoUrl = Property(project, "RepoUrl");
        const string version = "1.3.2"; // 下一版（比 1.3.1 新就行）

        // 包的结构：RhythmHint/RhythmHint.dll、RhythmHint/Info.json、RhythmHint/Editor/编辑器文件（照 PackageForUmm 的 Copy）
        var targets = project.Descendants("Target").Single(t => (string?)t.Attribute("Name") == "PackageForUmm");
        var copies = targets.Elements("Copy").Select(c => ((string?)c.Attribute("SourceFiles"), (string?)c.Attribute("DestinationFolder"))).ToList();
        Assert.Contains(("$(OutDir)$(AssemblyName).dll;$(OutDir)Info.json", "$(PackageStageDir)$(ModId)"), copies);
        Assert.Contains(("@(EditorFile)", "$(PackageStageDir)$(ModId)\\Editor"), copies);
        Assert.Equal("RhythmHint", modId);

        var stage = Path.Combine(_root, "stage");
        Directory.CreateDirectory(Path.Combine(stage, modId, "Editor"));
        File.WriteAllText(Path.Combine(stage, modId, modId + ".dll"), "new dll");
        File.WriteAllText(Path.Combine(stage, modId, "Info.json"), "new info");
        var editor = Path.Combine(RepoRoot, "src", "RhythmHint.Editor");
        var editorFiles = Directory.GetFiles(editor).Where(f => (f.EndsWith(".html") || f.EndsWith(".css") || f.EndsWith(".js") || Path.GetFileName(f) == "icon.ico")
            && Path.GetFileName(f) != "session.js").ToList();
        Assert.NotEmpty(editorFiles);
        foreach (var file in editorFiles)
        {
            File.Copy(file, Path.Combine(stage, modId, "Editor", Path.GetFileName(file)));
        }

        var zip = Path.Combine(_root, $"{modId}-{version}.zip");
        ZipFile.CreateFromDirectory(stage, zip); // MSBuild 的 ZipDirectory 就是它
        var url = Property(project, "PackageUrl").Replace("$(RepoUrl)", repoUrl).Replace("$(Version)", version).Replace("$(ModId)", modId);
        var manifestText = Property(project, "UpdateJsonText").Replace("$(Version)", version).Replace("$(PackageUrl)", url)
            .Replace("$(PackageSha256.ToLowerInvariant())", Frozen.V131.ModUpdate.Sha256Of(zip));
        var repository = Property(project, "DefaultModRepository").Replace("$(RepoUrl)", repoUrl);

        // 1.3.1 怎么找清单、读清单、决定装不装
        Assert.Equal(repoUrl + "/releases/latest/download/update.json", Frozen.V131.ModUpdate.ManifestUrl(repository));
        var manifest = Frozen.V131.UpdateManifest.Parse(manifestText);
        Assert.NotNull(manifest);
        Assert.Equal(version, manifest!.Version);
        Assert.StartsWith("https://", manifest.Url);
        Assert.Equal(Frozen.V131.ModUpdate.Sha256Of(zip), manifest.Sha256);
        Assert.True(Frozen.V131.ModUpdate.ShouldInstall(manifest.Version, "1.3.1"));

        // 1.3.1 怎么装：只换包里有的文件，设置和手序不动
        var (mod, work) = Installed();
        var files = Frozen.V131.ModUpdate.Install(zip, mod, work, modId);

        Assert.Equal(2 + editorFiles.Count, files.Count);
        Assert.Equal("new info", Read(mod, "Info.json"));
        Assert.Equal("new dll", Read(mod, "RhythmHint.dll"));
        Assert.Equal(File.ReadAllText(editorFiles[0]), Read(mod, "Editor", Path.GetFileName(editorFiles[0])));
        Assert.Equal("my settings", Read(mod, "Settings.xml"));
        Assert.Equal("my fingering", Read(mod, "Fingerings", "Song.json"));

        // 现在的代码读同一份清单、装同一个包也一样
        Assert.Equal(manifest.Sha256, UpdateManifest.Parse(manifestText)!.Sha256);
        var (mod2, work2) = (Path.Combine(_root, "Mods2", "RhythmHint"), Path.Combine(_root, "Mods2", "RhythmHint", ".update"));
        Directory.CreateDirectory(mod2);
        Assert.Equal(files.Count, ModUpdate.Install(zip, mod2, work2, modId).Count);
    }
}
