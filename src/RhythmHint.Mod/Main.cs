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

    public static bool Load(UnityModManager.ModEntry modEntry)
    {
        Mod = modEntry;
        Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
        modEntry.OnToggle = OnToggle;
        modEntry.OnGUI = entry => SettingsGui.Draw(Settings, Overlay);
        modEntry.OnSaveGUI = entry => Settings.Save(entry);
        return true;
    }

    private static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled)
    {
        if (enabled)
        {
            if (_host == null)
            {
                _host = new GameObject("RhythmHint");
                Object.DontDestroyOnLoad(_host);
                Overlay = _host.AddComponent<Overlay>();
            }
        }
        else if (_host != null)
        {
            Object.Destroy(_host);
            _host = null;
            Overlay = null;
        }

        return true;
    }
}
