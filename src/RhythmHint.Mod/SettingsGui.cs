using UnityEngine;

namespace RhythmHint;

/// <summary>
/// UMM 设置页（Ctrl+F10 → RhythmHint 展开）：只放状态和「打开设置窗口」按钮，具体设置在独立的设置窗口里。
/// </summary>
internal static class SettingsGui
{
    public static void Draw(Settings s, Overlay? overlay)
    {
        GUILayout.Label($"状态：{overlay?.Status ?? "Mod 未启用"}");
        GUILayout.Space(6);
        if (overlay == null)
        {
            GUILayout.Label("请先启用 Mod，再打开设置窗口。");
            return;
        }

        if (GUILayout.Button($"打开设置窗口（游戏中按 {s.SettingsHotkey}）", GUILayout.Width(360)))
        {
            overlay.OpenSettings();
        }

        GUILayout.Label("设置窗口打开时会播放演示，修改即时生效。");
    }
}
