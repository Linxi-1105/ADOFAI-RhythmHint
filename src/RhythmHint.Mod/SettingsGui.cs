using UnityEngine;

namespace RhythmHint;

/// <summary>
/// UMM 设置页（Ctrl+F10 → RhythmHint 展开）：只放状态和「打开设置窗口」按钮，具体设置在独立的设置窗口里。
/// </summary>
internal static class SettingsGui
{
    public static void Draw(Settings s, Overlay? overlay)
    {
        Lang.Update(s); // Mod 没启用时 Overlay 不跑，这里也要跟上游戏语言
        GUILayout.Label(Lang.T("状态：{0}", overlay?.Status ?? Lang.T("Mod 未启用")));
        if (Main.SettingsNotice.Source.Length > 0)
        {
            // 读设置文件出了问题（见 Main.Load）：这里一直显示，设置窗口概览页里点「知道了」后不再显示
            GUILayout.Label(Lang.T("设置文件读取出错：{0}", Main.SettingsNotice));
        }

        GUILayout.Space(6);
        if (overlay == null)
        {
            GUILayout.Label(Lang.T("请先启用 Mod，再打开设置窗口。"));
            return;
        }

        if (GUILayout.Button(Lang.T("打开设置窗口（游戏中按 {0}）", s.SettingsHotkey), GUILayout.Width(360)))
        {
            overlay.OpenSettings();
        }

        GUILayout.Label(Lang.T("设置窗口打开时会播放演示，修改即时生效。"));
    }
}
