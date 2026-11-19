using System;
using System.IO;
using RhythmHint.Core;
using UnityEngine;
using UnityModManagerNet;

namespace RhythmHint;

/// <summary>UMM 入口（Info.json 的 EntryMethod）。</summary>
public static class Main
{
    private static GameObject? _host;

    internal static UnityModManager.ModEntry Mod { get; private set; } = null!;

    internal static Settings Settings { get; private set; } = null!;

    internal static Overlay? Overlay { get; private set; }

    /// <summary>读设置文件时出的问题（给玩家看，设置窗口概览页显示到玩家点「知道了」为止）；没出问题为 Empty。</summary>
    internal static Message SettingsNotice { get; set; } = Message.Empty;

    public static bool Load(UnityModManager.ModEntry modEntry)
    {
        Mod = modEntry;
        Settings = LoadSettings(modEntry);

        // 读失败时 LoadSettings 已经先把原文件另存好（另存不了就不让保存），这里保存不会覆盖掉玩家原来的设置
        if (Settings.MigrateDefaults())
        {
            Settings.Save(modEntry);
            modEntry.Logger.Log($"设置：默认值已更新（主手重置间隔 {Settings.ResetWindowMs} 毫秒）");
        }

        modEntry.OnToggle = OnToggle;
        modEntry.OnGUI = entry => SettingsGui.Draw(Settings, Overlay);
        modEntry.OnSaveGUI = entry => Settings.Save(entry);
        return true;
    }

    /// <summary>
    /// 自己读 Settings.xml，不用 UMM 的 ModSettings.Load：它读失败时只记一句「Can't read …」就返回全默认的设置，
    /// 接着迁移默认值那次保存就把玩家原来的文件覆盖掉（连同各谱选过的手序）。这里读失败时先另存原文件、再逐项读回能读的值（见 Core 的 SettingsFile），
    /// 读完把数值夹回设置窗口的范围（Settings.Normalize）。
    /// </summary>
    private static Settings LoadSettings(UnityModManager.ModEntry modEntry)
    {
        var settings = new Settings();
        var path = settings.GetPath(modEntry);
        var load = SettingsFile.Load<Settings>(path, DateTime.Now);
        settings = load.Value;
        Settings.SaveBlocked = !load.CanSave;
        switch (load.Result)
        {
            case SettingsReadResult.Recovered:
                modEntry.Logger.Warning($"设置：{path} 整份读取失败（{load.Error}）；"
                    + (load.BadCopy.Length > 0 ? $"原文件已另存为 {load.BadCopy}" : $"原文件另存失败（{load.BackupError}），这次运行不保存设置")
                    + $"；逐项读回，没读进来、改用默认值的：{(load.Unread.Count > 0 ? string.Join("、", load.Unread) : "无")}"
                    + (load.Truncated ? "，以及文件损坏处之后的所有设置" : string.Empty));
                SettingsNotice = load.BadCopy.Length == 0
                    ? Lang.M("已尽量读回原有设置，但无法另存原文件（{0}）。为免覆盖原有设置，本次运行期间不保存设置。", load.BackupError)
                    : load.Unread.Count > 0 && !load.Truncated
                        ? Lang.M("已尽量读回原有设置，有 {0} 项改用默认值（具体项目见日志）。原文件已另存为 {1}。", load.Unread.Count, Path.GetFileName(load.BadCopy))
                        : Lang.M("已尽量读回原有设置，无法读取的项目改用默认值。原文件已另存为 {0}。", Path.GetFileName(load.BadCopy));
                break;
            case SettingsReadResult.Unreadable:
                modEntry.Logger.Warning($"设置：{path} 打不开（{load.Error}），先用默认设置，这次运行不保存，以免覆盖原文件");
                SettingsNotice = Lang.M("无法读取设置文件（{0}）。为免覆盖原有设置，本次运行期间不保存设置。", load.Error);
                break;
        }

        var fixedFields = settings.Normalize();
        if (fixedFields.Count > 0)
        {
            modEntry.Logger.Warning($"设置：这些值超出设置窗口的范围或不合法，已修正：{string.Join("、", fixedFields)}");
        }

        return settings;
    }

    private static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled)
    {
        if (enabled)
        {
            if (_host == null)
            {
                _host = new GameObject("RhythmHint");
                UnityEngine.Object.DontDestroyOnLoad(_host);
                Overlay = _host.AddComponent<Overlay>();
            }
        }
        else if (_host != null)
        {
            UnityEngine.Object.Destroy(_host);
            _host = null;
            Overlay = null;
        }

        return true;
    }
}
