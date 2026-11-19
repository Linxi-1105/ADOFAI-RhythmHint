using UnityModManagerNet;

namespace RhythmHint;

/// <summary>
/// 保存在 Mods\RhythmHint\Settings.xml。字段、默认值和修正都在 Core 的 SettingsFields.cs（同一个类的另一半，可以单测），
/// 这里只接上 UMM：基类 ModSettings 和保存。读由 Main.Load 自己来（不用 UMM 的 ModSettings.Load，原因见那里）。
/// 这一半不要加公开字段或属性：测试只看得到 Core 那一半，加在这里的不受兼容测试保护。
/// </summary>
public sealed partial class Settings : UnityModManager.ModSettings
{
    /// <summary>
    /// 读设置时出了问题、又没能另存原文件（或者文件打不开）：这次运行不保存，免得把玩家原来的设置覆盖掉（见 Main.Load）。
    /// 静态属性不会被存进设置文件。
    /// </summary>
    internal static bool SaveBlocked { get; set; }

    private static bool _loggedBlocked;

    public override void Save(UnityModManager.ModEntry modEntry)
    {
        if (SaveBlocked)
        {
            if (!_loggedBlocked)
            {
                _loggedBlocked = true;
                modEntry.Logger.Warning("设置：读取时出了问题、原文件没能另存，这次运行不保存设置，以免覆盖原文件");
            }

            return;
        }

        Save(this, modEntry);
    }
}
