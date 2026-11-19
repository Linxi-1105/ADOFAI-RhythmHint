using System;

namespace RhythmHint.Core;

/// <summary>
/// TUF（The Universal Forums）的难度档位：P1–P20、G1–G20、U1–U20，从易到难编成 1–60。
/// 本 Mod 只借用这套档位的写法来显示估算结果，不是 TUF 的官方评级。
/// </summary>
public static class DifficultyTiers
{
    public const int Count = 60;
    private const string Letters = "PGU";

    /// <summary>1–60 → 「P1」…「U20」；超出范围的夹到两端。</summary>
    public static string Name(int tier)
    {
        tier = Clamp(tier);
        return $"{Letters[(tier - 1) / 20]}{((tier - 1) % 20) + 1}";
    }

    /// <summary>「P1」…「U20」（不分大小写）→ 1–60；Q 档、特殊档和写错的返回 0。</summary>
    public static int Parse(string? name)
    {
        if (name == null || name.Length < 2)
        {
            return 0;
        }

        var letter = Letters.IndexOf(char.ToUpperInvariant(name[0]));
        if (letter < 0 || !int.TryParse(name.Substring(1), out var number) || number is < 1 or > 20)
        {
            return 0;
        }

        return (letter * 20) + number;
    }

    /// <summary>大档：0 = P（Planetary），1 = G（Galactic），2 = U（Universal）。</summary>
    public static int Group(int tier) => (Clamp(tier) - 1) / 20;

    /// <summary>档内的数字 1–20。</summary>
    public static int Number(int tier) => ((Clamp(tier) - 1) % 20) + 1;

    public static int Clamp(int tier) => Math.Max(1, Math.Min(Count, tier));
}
