using System.Collections.Generic;
using System.Globalization;

namespace RhythmHint.Core;

/// <summary>界面语言：有哪些、游戏语言对应哪种、设置怎么覆盖。</summary>
public static class Languages
{
    /// <summary>原文（简体中文）：代码里写的就是它，不用翻译表。</summary>
    public const string Source = "zh-Hans";

    /// <summary>缺译文、或者游戏语言还没有翻译时用英文。</summary>
    public const string Fallback = "en";

    /// <summary>支持的语言（代码、用它自己的文字写的名字），设置里按这个顺序列出。</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> All = new[]
    {
        (Source, "简体中文"),
        ("zh-Hant", "繁體中文"),
        (Fallback, "English"),
        ("ko", "한국어"),
        ("ja", "日本語"),
    };

    /// <summary>游戏的语言（UnityEngine.SystemLanguage 的名字）用哪种翻译；还没有翻译的语言用英文。</summary>
    public static string FromGame(string systemLanguage) => systemLanguage switch
    {
        "ChineseSimplified" or "Chinese" => Source,
        "ChineseTraditional" => "zh-Hant",
        "Korean" => "ko",
        "Japanese" => "ja",
        _ => Fallback,
    };

    /// <summary>设置里的界面语言（空 = 跟随游戏；不认识的值也当作跟随游戏）加上游戏语言，得出实际用哪种。</summary>
    public static string Resolve(string setting, string systemLanguage)
    {
        foreach (var (code, _) in All)
        {
            if (code == setting)
            {
                return code;
            }
        }

        return FromGame(systemLanguage);
    }
}

/// <summary>
/// 一句给玩家看的话：中文原文和参数，用到时才决定怎么出。显示时按当前语言翻译（<see cref="Translator.T(Message)"/>），
/// 写日志时用原文（<see cref="ToString"/>）：同一句状态既显示在设置窗口里、又写进日志，日志保持中文。
/// 参数里也可以放 Message，跟着一起翻译。
/// </summary>
public sealed class Message
{
    public Message(string source, params object[] args)
    {
        Source = source;
        Args = args;
    }

    /// <summary>没有内容（例如「没有隐藏原因」）。</summary>
    public static Message Empty { get; } = new(string.Empty);

    /// <summary>中文原文，翻译表的键。</summary>
    public string Source { get; }

    public object[] Args { get; }

    /// <summary>原文（日志用）。</summary>
    public override string ToString() => Translator.Format(Source, Args);
}

/// <summary>
/// 按当前语言查译文：键是中文原文，表是「语言代码 → 原文 → 译文」。缺译文时先用英文、再用原文；
/// 繁体中文缺译文直接用原文（简体比英文好认）。占位符 {0} {1} 按固定区域格式填，小数点不会变成逗号。
/// </summary>
public sealed class Translator
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _tables;

    public Translator(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> tables) => _tables = tables;

    /// <summary>当前语言代码（见 <see cref="Languages.All"/>）。</summary>
    public string Language { get; set; } = Languages.Source;

    public string T(string source, params object[] args) => Format(Lookup(source), Translate(args));

    /// <summary>翻译一句话；参数里的 Message 也跟着翻译。</summary>
    public string T(Message message) => T(message.Source, message.Args);

    /// <summary>不翻译、只填占位符（日志用原文；参数里的 Message 也是原文）。</summary>
    public static string Format(string text, params object[] args) =>
        args.Length == 0 ? text : string.Format(CultureInfo.InvariantCulture, text, args);

    private object[] Translate(object[] args)
    {
        var copy = args;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is Message message)
            {
                copy = copy == args ? (object[])args.Clone() : copy;
                copy[i] = T(message);
            }
        }

        return copy;
    }

    private string Lookup(string source)
    {
        if (Language == Languages.Source)
        {
            return source;
        }

        if (Find(Language, source) is { } text)
        {
            return text;
        }

        return Language != "zh-Hant" && Find(Languages.Fallback, source) is { } fallback ? fallback : source;
    }

    private string? Find(string language, string source) =>
        _tables.TryGetValue(language, out var table) && table.TryGetValue(source, out var text) && !string.IsNullOrEmpty(text)
            ? text
            : null;
}
