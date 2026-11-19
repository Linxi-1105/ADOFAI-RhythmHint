using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>给一关挑出来的手序：要画的音符、哪些是自动排键补的、用了哪份文件、给玩家看的说明。</summary>
internal sealed class FingeringResult
{
    public FingeringResult(List<PlannedNote> notes, List<bool> fromAuto, string usedFile, CustomFingering? used, Message status, Message caption)
        : this(notes, fromAuto, usedFile, used, status, caption, string.Empty, new List<(string, int, int)>(), false)
    {
    }

    public FingeringResult(List<PlannedNote> notes, List<bool> fromAuto, string usedFile, CustomFingering? used, Message status, Message caption,
        string chart, List<(string Name, int MatchedHits, int TotalHits)> candidates, bool chosenByPlayer)
    {
        Chart = chart;
        Candidates = candidates;
        ChosenByPlayer = chosenByPlayer;
        Notes = notes;
        FromAuto = fromAuto;
        UsedFile = usedFile;
        Used = used;
        Status = status;
        Caption = caption;
    }

    public List<PlannedNote> Notes { get; }

    /// <summary>和 Notes 一一对应；没用专属手序时为空。</summary>
    public List<bool> FromAuto { get; }

    /// <summary>用上的手序文件名；没有为空。</summary>
    public string UsedFile { get; }

    public CustomFingering? Used { get; }

    /// <summary>设置窗口里显示的说明。</summary>
    public Message Status { get; }

    /// <summary>开局在面板上提醒一下（对不上、分不清）；不用提醒为空。</summary>
    public Message Caption { get; }

    /// <summary>本关的谱面指纹（玩家选了以后按它记住）。</summary>
    public string Chart { get; }

    /// <summary>像这一关的手序（文件名、匹配砖数和总砖数），给玩家选。</summary>
    public List<(string Name, int MatchedHits, int TotalHits)> Candidates { get; }

    /// <summary>这一关用哪份（或都不用）是玩家自己选的。</summary>
    public bool ChosenByPlayer { get; }
}

/// <summary>
/// 专属手序文件夹（Mods/RhythmHint/Fingerings）：玩家把手序文件放进来就不用管了，进到对应的歌时自动使用（认歌见 FingeringMatcher）。
/// 每秒看一次文件夹有没有变化（编辑器保存后关卡里马上换上）；读过的文件按修改时间缓存，同一关重开不重新对齐。
/// </summary>
internal sealed class FingeringSource
{
    private readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);
    private string _signature = string.Empty;
    private float _nextPoll;
    private string _lastKey = string.Empty;
    private FingeringResult? _last;

    public static string Folder => Path.Combine(Main.Mod.Path, "Fingerings");

    /// <summary>
    /// 游戏装在浏览器不让网页写的文件夹里时（Steam 默认的 C:\Program Files (x86)），编辑器存到这里，每秒搬进 <see cref="Folder"/>；
    /// 否则为 null（编辑器直接存进 <see cref="Folder"/>）。见 <see cref="FingeringInbox"/>。
    /// </summary>
    public static string? Inbox => _inbox.Value;

    private static readonly Lazy<string?> _inbox = new(() =>
    {
        if (!FingeringInbox.BrowserBlocks(Folder, FingeringInbox.WindowsBlockedRoots()))
        {
            return null;
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents))
        {
            Main.Mod.Logger.Warning("编辑器：游戏在浏览器不让网页写的文件夹里，但找不到文档文件夹，编辑器只能另存为文件");
            return null;
        }

        var inbox = Path.Combine(documents, "RhythmHint", "Fingerings");
        Main.Mod.Logger.Log($"编辑器：游戏在浏览器不让网页写的文件夹里，编辑器改存到 {inbox}，Mod 自动搬进手序文件夹");
        return inbox;
    });

    /// <summary>每秒最多看一次；文件加了、删了、改了就返回 true。</summary>
    public bool Poll()
    {
        if (Time.unscaledTime < _nextPoll)
        {
            return false;
        }

        _nextPoll = Time.unscaledTime + 1;
        if (Inbox != null)
        {
            foreach (var name in FingeringInbox.MoveInto(Inbox, Folder))
            {
                Main.Mod.Logger.Log($"专属手序：把编辑器保存的 {name} 搬进手序文件夹");
            }
        }
        var signature = Signature();
        if (signature == _signature)
        {
            return false;
        }

        _signature = signature;
        return true;
    }

    /// <summary>给这一关挑手序。没开专属手序、没有对得上的文件时用自动排键。</summary>
    public FingeringResult Choose(List<TileHit> hits, List<PlannedNote> auto, Settings settings, LevelInfo info)
    {
        if (!settings.UseCustomFingering)
        {
            return new FingeringResult(auto, new List<bool>(), string.Empty, null, Lang.M("专属手序已关闭，使用自动排键。"), Message.Empty);
        }

        var chart = CustomFingering.ChartPrint(hits);
        var chosen = ChosenFor(settings, chart);
        var key = $"{Signature()}/{settings.FingeringKey}/{chart}/{chosen ?? "-"}";
        if (key == _lastKey && _last != null)
        {
            return _last;
        }

        var candidates = Load(out var broken);
        var choice = FingeringMatcher.Choose(candidates, hits, auto, settings.LeftHanded, chosen);
        var brokenNote = broken.Count == 0 ? Message.Empty
            : Lang.M("\n有 {0} 个文件无法读取：{1}（原因见日志）", broken.Count, string.Join("、", broken));
        var list = choice.Candidates.ConvertAll(c => (c.Candidate.Name, c.MatchedHits, c.TotalHits));
        var byPlayer = choice.Reason == FingeringReason.Chosen;
        FingeringResult result;
        if (choice.Used != null && choice.Match != null)
        {
            var m = choice.Match;
            var status = m.FallbackNotes == 0
                ? Lang.M("正在使用专属手序：{0}{1}", choice.Used.Name, brokenNote)
                : Lang.M("正在使用专属手序：{0}。谱面和手序有 {1} 处对不上（共 {2} 个音符），这些地方使用自动排键。{3}",
                    choice.Used.Name, m.FallbackSections, m.FallbackNotes, brokenNote);
            var caption = m.FallbackNotes == 0 ? Message.Empty
                : Lang.M("专属手序有 {0} 处和这一版谱面对不上，已用自动排键补上", m.FallbackSections);
            result = new FingeringResult(m.Notes, m.FromAuto, choice.Used.Name, choice.Used.File, status, caption, chart, list, byPlayer);
            Main.Mod.Logger.Log($"专属手序：{choice.Used.Name}（{choice.Reason}），对上 {m.MatchedHits}/{m.TotalHits} 块砖，自动补 {m.FallbackNotes} 个音符（{m.FallbackSections} 处）");
        }
        else if (choice.Reason == FingeringReason.Unsure)
        {
            result = new FingeringResult(auto, new List<bool>(), string.Empty, null,
                Lang.M("有 {0} 份专属手序像这一关，但不能确定是哪一份，暂用自动排键。请在下面选择要用的手序。{1}", list.Count, brokenNote),
                Lang.M("有专属手序像这一关但不能确定，请在 F8 → 专属手序里选择"), chart, list, false);
            Main.Mod.Logger.Log("专属手序：分不清，候选 " + string.Join("、", list.ConvertAll(c => $"{c.Name} {c.MatchedHits}/{c.TotalHits}")));
        }
        else if (byPlayer)
        {
            result = new FingeringResult(auto, new List<bool>(), string.Empty, null,
                Lang.M("这一关已按你的选择不用专属手序，使用自动排键。{0}", brokenNote), Message.Empty, chart, list, true);
        }
        else
        {
            result = new FingeringResult(auto, new List<bool>(), string.Empty, null,
                Lang.M("本关没有专属手序，使用自动排键。{0}", brokenNote), Message.Empty, chart, list, false);
        }

        _lastKey = key;
        _last = result;
        return result;
    }

    /// <summary>玩家给这个谱面选过的文件名（空串 = 选了都不用）；没选过为 null。存在 Settings.FingeringChoices，一行「指纹 Tab 文件名」。</summary>
    public static string? ChosenFor(Settings settings, string chart)
    {
        foreach (var line in settings.FingeringChoices.Split('\n'))
        {
            var tab = line.IndexOf('\t');
            if (tab > 0 && line.Substring(0, tab) == chart)
            {
                return line.Substring(tab + 1);
            }
        }

        return null;
    }

    /// <summary>记住玩家给这个谱面的选择；name 为 null 时忘掉（重新自动识别）。</summary>
    public static void Remember(Settings settings, string chart, string? name)
    {
        var lines = new List<string>();
        foreach (var line in settings.FingeringChoices.Split('\n'))
        {
            if (line.Length > 0 && !line.StartsWith(chart + "\t", StringComparison.Ordinal))
            {
                lines.Add(line);
            }
        }

        if (name != null)
        {
            lines.Add(chart + "\t" + name);
        }

        settings.FingeringChoices = string.Join("\n", lines);
    }

    /// <summary>读一份文件（编辑器打开时带上作者、说明）；读不了为 null。</summary>
    public CustomFingering? Get(string name)
    {
        Load(out _);
        return _files.TryGetValue(Path.Combine(Folder, name), out var entry) ? entry.File : null;
    }

    /// <summary>文件夹里所有 .json 的名字、修改时间、大小，用来判断变没变。</summary>
    private static string Signature()
    {
        if (!Directory.Exists(Folder))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var path in Directory.GetFiles(Folder, "*.json"))
        {
            var info = new FileInfo(path);
            sb.Append(info.Name).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('|').Append(info.Length).Append(';');
        }

        return sb.ToString();
    }

    private List<FingeringCandidate> Load(out List<string> broken)
    {
        var result = new List<FingeringCandidate>();
        broken = new List<string>();
        if (!Directory.Exists(Folder))
        {
            _files.Clear();
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(Folder, "*.json"))
        {
            seen.Add(path);
            var info = new FileInfo(path);
            if (!_files.TryGetValue(path, out var entry) || entry.Time != info.LastWriteTimeUtc || entry.Size != info.Length)
            {
                entry = Read(info);
                _files[path] = entry;
            }

            if (entry.File != null)
            {
                result.Add(new FingeringCandidate(info.Name, entry.File));
            }
            else
            {
                broken.Add(info.Name);
            }
        }

        foreach (var stale in new List<string>(_files.Keys))
        {
            if (!seen.Contains(stale))
            {
                _files.Remove(stale);
            }
        }

        return result;
    }

    private static Entry Read(FileInfo info)
    {
        if (info.Length > CustomFingering.MaxFileBytes)
        {
            Main.Mod.Logger.Warning($"手序文件 {info.Name} 有 {info.Length / 1024 / 1024} MB，太大，不读");
            return new Entry(info.LastWriteTimeUtc, info.Length, null);
        }

        try
        {
            var file = CustomFingering.Parse(File.ReadAllText(info.FullName, Encoding.UTF8));
            return new Entry(info.LastWriteTimeUtc, info.Length, file);
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            // 坏文件只跳过这一份（设置窗口里列出来），不影响别的手序和自动排键
            Main.Mod.Logger.Warning($"手序文件 {info.Name} 无法读取：{ex.Message}");
            return new Entry(info.LastWriteTimeUtc, info.Length, null);
        }
    }

    private readonly record struct Entry(DateTime Time, long Size, CustomFingering? File);
}
