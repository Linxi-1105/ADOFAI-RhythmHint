using System;
using System.ComponentModel;
using System.Diagnostics;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 用系统默认浏览器打开网页。Windows 上不直接用 Application.OpenURL：浏览器原本没开着时，它会成为游戏的子进程，
/// Steam 把游戏启动的程序也算作游戏，浏览器不关，Steam 就一直显示游戏在运行（2026-11-17 实测：关掉浏览器 Steam 才停）。
/// 交给 explorer.exe 转给系统外壳去开，这个 explorer 马上退出，浏览器不挂在游戏下面。
/// </summary>
internal static class Browser
{
    public static void Open(string url)
    {
        // 只开网页地址：explorer.exe 也能打开本地文件和程序，地址不对就不开
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            Main.Mod.Logger.Warning($"不打开非 https 的地址：{url}");
            return;
        }

        if (Application.platform != RuntimePlatform.WindowsPlayer)
        {
            Application.OpenURL(url);
            return;
        }

        try
        {
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{url}\"") { UseShellExecute = false });
        }
        catch (Win32Exception ex)
        {
            Main.Mod.Logger.Warning($"用 explorer.exe 打开网页失败，改用 Application.OpenURL：{ex.Message}");
            Application.OpenURL(url);
        }
    }
}
