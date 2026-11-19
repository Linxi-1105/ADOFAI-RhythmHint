using System.Collections.Generic;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// BPM 框左半边的难度徽章：TUF 的圆形档位图标（P1–U20），下面一行小字「估算」。
/// 图标来自 TUF 的 T21C-assets（CC BY 4.0，缩到 192 像素），编进 DLL；难度是本 Mod 按砖块估的，不是 TUF 官方评级。
/// </summary>
internal static class DifficultyBadge
{
    private static readonly Dictionary<int, Texture2D?> Icons = new();

    /// <summary>在 box（BPM 框）左半边画；tier 为 0 时不画。只能在 OnGUI 里调用。</summary>
    public static void Draw(Rect box, float scale, int tier)
    {
        if (tier <= 0)
        {
            return;
        }

        var icon = Icon(tier);
        var size = 84 * scale;
        var r = new Rect(box.x + (18 * scale), box.y + (10 * scale), size, size);
        if (icon != null)
        {
            GUI.DrawTexture(r, icon, ScaleMode.ScaleToFit, true, 0f, new Color(1, 1, 1, Painter.Alpha), 0f, 0f);
        }
        else
        {
            // 图标读不出来（资源缺失）时退回文字，免得空着
            Painter.FitText(r, DifficultyTiers.Name(tier), Mathf.RoundToInt(30 * scale), Color.white, true);
        }

        var label = new Rect(r.x - (6 * scale), r.yMax + (2 * scale), size + (12 * scale), 18 * scale);
        Painter.FitText(label, Lang.T("估算"), Mathf.RoundToInt(14 * scale), new Color(0.78f, 0.84f, 0.95f), false);
    }

    private static Texture2D? Icon(int tier)
    {
        if (Icons.TryGetValue(tier, out var cached))
        {
            return cached;
        }

        var name = $"RhythmHint.DifficultyIcons.{DifficultyTiers.Name(tier)}.png";
        var texture = EmbeddedImage.Load(name); // 带 mipmap：192 像素缩到 1080p 下 84 像素时边缘不发毛
        if (texture == null)
        {
            Main.Mod.Logger.Warning($"难度图标缺失：{name}");
        }

        Icons[tier] = texture;
        return texture;
    }
}
