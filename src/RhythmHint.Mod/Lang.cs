using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 界面文字的翻译。代码里写中文原文：马上要显示的用 <see cref="T(string, object[])"/>；
/// 既要显示又要写日志的用 <see cref="M"/> 做成消息，显示时翻译、日志里是原文。
/// 翻译表在 Languages/*.json（编进 DLL），新加的句子要在每个表里补上译文（测试会检查）。
/// </summary>
internal static class Lang
{
    private static Translator? _translator;
    private static SystemLanguage? _gameLanguage;
    private static string? _setting;
    private static bool _loggedLanguages;

    private static Translator Translator => _translator ??= new Translator(LoadTables());

    /// <summary>当前语言代码（见 <see cref="Languages.All"/>）。</summary>
    public static string Current => Translator.Language;

    public static string T(string source, params object[] args) => Translator.T(source, args);

    /// <summary>当前语言会显示的所有文字（设置窗口预先排字形用）。</summary>
    public static string AllText() => Translator.AllText();

    public static string T(Message message) => Translator.T(message);

    /// <summary>一句先不翻译的话（状态、原因这类既显示又写日志的）。</summary>
    public static Message M(string source, params object[] args) => new(source, args);

    /// <summary>
    /// 按设置和游戏当前语言（RDString.language，只读）更新界面语言。每帧调用，只有变了才重新算：
    /// 玩家在游戏设置里换语言、或者在本 Mod 里改界面语言，马上生效。
    /// </summary>
    public static void Update(Settings settings)
    {
        var game = RDString.language;
        if (game == _gameLanguage && settings.Language == _setting)
        {
            return;
        }

        _gameLanguage = game;
        _setting = settings.Language;
        Translator.Language = Languages.Resolve(settings.Language, game.ToString());
        if (!_loggedLanguages)
        {
            _loggedLanguages = true;
            Main.Mod.Logger.Log($"游戏语言 {game}，游戏支持 [{string.Join(", ", RDString.AvailableLanguages)}]；界面语言 {Translator.Language}");
        }
    }

    /// <summary>读编进 DLL 的翻译表；某个表坏了只记日志、那种语言退回英文 / 原文，不影响 Mod 运行。</summary>
    private static Dictionary<string, IReadOnlyDictionary<string, string>> LoadTables()
    {
        var tables = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        var assembly = typeof(Lang).Assembly;
        foreach (var (code, _) in Languages.All)
        {
            if (code == Languages.Source)
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream($"RhythmHint.Languages.{code}.json");
            if (stream == null)
            {
                Main.Mod.Logger.Warning($"缺少翻译表 {code}.json");
                continue;
            }

            try
            {
                using var reader = new StreamReader(stream);
                tables[code] = JsonConvert.DeserializeObject<Dictionary<string, string>>(reader.ReadToEnd())
                    ?? new Dictionary<string, string>();
            }
            catch (JsonException ex)
            {
                Main.Mod.Logger.Warning($"翻译表 {code}.json 读不了，这种语言改用英文：{ex.Message}");
            }
        }

        return tables;
    }
}
