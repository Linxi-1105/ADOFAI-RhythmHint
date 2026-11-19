using System.Text.Json;
using System.Text.RegularExpressions;
using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 多语言：查表和回退、游戏语言对应到哪种翻译，以及翻译文件和 Mod 代码对不对得上
/// （代码里每句都有译文、没有多余的、占位符一致、Mod 界面代码里没有漏掉没走翻译的中文）。
/// </summary>
public class TranslationTests
{
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Sample = new()
    {
        ["en"] = new Dictionary<string, string> { ["惯用手"] = "Dominant hand", ["当前相当于 {0} BPM"] = "About {0} BPM" },
        ["ko"] = new Dictionary<string, string> { ["惯用手"] = "주로 쓰는 손" },
        ["zh-Hant"] = new Dictionary<string, string> { ["惯用手"] = "慣用手" },
    };

    [Theory]
    [InlineData("zh-Hans", "惯用手")]
    [InlineData("en", "Dominant hand")]
    [InlineData("ko", "주로 쓰는 손")]
    [InlineData("zh-Hant", "慣用手")]
    public void LooksUpTheCurrentLanguage(string language, string expected)
    {
        var t = new Translator(Sample) { Language = language };

        Assert.Equal(expected, t.T("惯用手"));
    }

    [Fact]
    public void MissingTranslationFallsBackToEnglishThenSource()
    {
        // 韩文表里没有这句：先用英文；英文也没有：用中文原文。繁体中文缺句时直接用简体原文，比英文好认
        var ko = new Translator(Sample) { Language = "ko" };
        var hant = new Translator(Sample) { Language = "zh-Hant" };

        Assert.Equal("About 160 BPM", ko.T("当前相当于 {0} BPM", 160));
        Assert.Equal("没有的句子", ko.T("没有的句子"));
        Assert.Equal("当前相当于 160 BPM", hant.T("当前相当于 {0} BPM", 160));
    }

    [Fact]
    public void AllTextCoversWhatTheLanguageShows()
    {
        // 设置窗口打开前先把这些字的字形排好（2026-11-19 用户：每次点选项都卡一下）：当前语言会显示的字都要在里面，缺译文时显示的英文也算
        var ko = new Translator(Sample) { Language = "ko" }.AllText();
        var zh = new Translator(Sample) { Language = "zh-Hans" }.AllText();

        Assert.Contains("주로 쓰는 손", ko);
        Assert.Contains("About {0} BPM", ko);
        Assert.Contains("惯用手", zh);
        Assert.Contains("当前相当于 {0} BPM", zh);
    }

    [Fact]
    public void FormatsNumbersTheSameEverywhere()
    {
        // 占位符按固定格式填，不随系统区域设置把小数点变成逗号
        var t = new Translator(Sample) { Language = "en" };

        Assert.Equal("About 1.5 BPM", t.T("当前相当于 {0} BPM", 1.5));
    }

    [Fact]
    public void MessagesTranslateWhenShownAndStayChineseInLogs()
    {
        // 同一句状态既显示在设置窗口里（翻译）、又写进日志（原文）；参数里的消息也跟着翻译
        var tables = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en"] = new Dictionary<string, string> { ["隐藏（{0}）"] = "Hidden ({0})", ["位于选关界面"] = "In level select" },
        };
        var t = new Translator(tables) { Language = "en" };
        var message = new Message("隐藏（{0}）", new Message("位于选关界面"));

        Assert.Equal("Hidden (In level select)", t.T(message));
        Assert.Equal("隐藏（位于选关界面）", message.ToString());
    }

    [Theory]
    [InlineData("ChineseSimplified", "zh-Hans")]
    [InlineData("Chinese", "zh-Hans")]
    [InlineData("ChineseTraditional", "zh-Hant")]
    [InlineData("English", "en")]
    [InlineData("Korean", "ko")]
    [InlineData("Japanese", "ja")]
    [InlineData("Spanish", "en")] // 还没有翻译的语言用英文
    [InlineData("Unknown", "en")]
    public void GameLanguageMapsToATranslation(string systemLanguage, string expected) =>
        Assert.Equal(expected, Languages.FromGame(systemLanguage));

    [Theory]
    [InlineData("", "Korean", "ko")]          // 跟随游戏
    [InlineData("ja", "Korean", "ja")]        // 手动选了日语
    [InlineData("xx", "Japanese", "ja")]      // 设置文件里写了不认识的值：当作跟随游戏
    public void SettingOverridesGameLanguage(string setting, string game, string expected) =>
        Assert.Equal(expected, Languages.Resolve(setting, game));

    [Fact]
    public void EveryLanguageHasANativeName()
    {
        Assert.Equal(new[] { "zh-Hans", "zh-Hant", "en", "ko", "ja" }, Languages.All.Select(l => l.Code));
        Assert.All(Languages.All, l => Assert.False(string.IsNullOrWhiteSpace(l.Name)));
    }

    // ---------- 翻译文件和 Mod 代码 ----------

    private static readonly string Root = FindRoot();

    private static string ModDir => Path.Combine(Root, "src", "RhythmHint.Mod");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根目录（Directory.Build.props）");
    }

    /// <summary>Mod 代码里 Lang.T("…")（马上翻译）和 Lang.M("…")（消息，显示时翻译）用到的原文（C# 转义还原）。</summary>
    private static HashSet<string> UsedSources()
    {
        var used = new HashSet<string>();
        foreach (var file in Directory.GetFiles(ModDir, "*.cs"))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"Lang\.[TM]\(\s*""((?:[^""\\]|\\.)*)"""))
            {
                used.Add(Regex.Unescape(m.Groups[1].Value));
            }
        }

        return used;
    }

    private static Dictionary<string, string> Table(string code) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(ModDir, "Languages", code + ".json")))!;

    private static string[] Placeholders(string text) =>
        Regex.Matches(text, @"\{\d+[^}]*\}").Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    public static TheoryData<string> Translated => new(Languages.All.Select(l => l.Code).Where(c => c != Languages.Source));

    [Theory]
    [MemberData(nameof(Translated))]
    public void EverySentenceIsTranslated(string code)
    {
        var table = Table(code);
        var missing = UsedSources().Where(s => !table.TryGetValue(s, out var v) || string.IsNullOrWhiteSpace(v)).ToList();

        Assert.True(missing.Count == 0, $"{code}.json 缺 {missing.Count} 句：{string.Join(" | ", missing.Take(8))}");
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void NoUnusedSentences(string code)
    {
        var used = UsedSources();
        var unused = Table(code).Keys.Where(k => !used.Contains(k)).ToList();

        Assert.True(unused.Count == 0, $"{code}.json 里有代码用不到的句子：{string.Join(" | ", unused.Take(8))}");
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void PlaceholdersMatchTheSource(string code)
    {
        var wrong = Table(code).Where(kv => !Placeholders(kv.Key).SequenceEqual(Placeholders(kv.Value))).Select(kv => kv.Key).ToList();

        Assert.True(wrong.Count == 0, $"{code}.json 占位符和原文不一致：{string.Join(" | ", wrong.Take(8))}");
    }

    /// <summary>界面代码里本来就该写全角符号、不用翻译的字符串：设置窗口预先排字形用的符号表（WindowSkin.Extra）。</summary>
    private static readonly string[] NotSentences = { "×—…→←↑↓·−%°：，。、；（）「」！？" };

    [Fact]
    public void ModCodeHasNoUntranslatedChinese()
    {
        // 界面代码里写死的中文都要经过 Lang.T / Lang.M；注释、日志（Logger.Log / Warning / Error）不算。
        // 中文的全角标点也算（2026-11-19 第二轮评审：文件名列表写死顿号，英、韩、日界面里也是「、」）
        var problems = new List<string>();
        foreach (var file in Directory.GetFiles(ModDir, "*.cs"))
        {
            var text = File.ReadAllText(file);
            text = Regex.Replace(text, @"//[^\n]*", string.Empty); // 注释（含 ///）
            text = Regex.Replace(text, @"Logger\.(?:Log|LogException|Warning|Error)\((?:[^;]|;(?!\s*\n))*?\);", string.Empty, RegexOptions.Singleline); // 日志语句
            foreach (Match m in Regex.Matches(text, @"(Lang\.[TM]\(\s*)?\$?""((?:[^""\\]|\\.)*)"""))
            {
                if (m.Groups[1].Success || NotSentences.Contains(m.Groups[2].Value)
                    || !Regex.IsMatch(m.Groups[2].Value, @"[\p{IsCJKUnifiedIdeographs}　-〿＀-￯]"))
                {
                    continue;
                }

                problems.Add($"{Path.GetFileName(file)}：{m.Value}");
            }
        }

        Assert.True(problems.Count == 0, $"没走翻译的中文 {problems.Count} 处：{string.Join(" | ", problems.Take(10))}");
    }

    [Fact]
    public void TranslatedSentencesAreNotInterpolated()
    {
        // Lang.T($"…{x}…") 每次的原文都不一样，查不到表：要写成 Lang.T("…{0}…", x)；Lang.M 同理
        var bad = Directory.GetFiles(ModDir, "*.cs").Where(f => Regex.IsMatch(File.ReadAllText(f), @"Lang\.[TM]\(\s*\$")).ToList();

        Assert.Empty(bad);
    }
}
