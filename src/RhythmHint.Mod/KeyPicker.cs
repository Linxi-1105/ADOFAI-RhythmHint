using System;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 设置窗口「按键框」的键位：默认的 D F J K，或「自定义」用屏幕键盘点选任意四个键。
/// 全部用鼠标点，不放输入框、也不录按键：在窗口里按键盘会被游戏当成击打。
/// </summary>
internal sealed class KeyPicker
{
    private const float KeyWidth = 46;
    private const float WideKeyWidth = 84;
    private const float SpaceWidth = 160;

    /// <summary>只留默认键位，其他组合都走自定义（用户：能自定义了就不用这么多预设）。</summary>
    private static readonly string[] Presets = { "D F J K" };

    /// <summary>屏幕键盘：每行开头的缩进和按键。按键名存进设置，要能被 <see cref="KeyCodes.Parse"/> 认出来。</summary>
    private static readonly (float Indent, string[] Keys)[] Rows =
    {
        (0, new[] { "`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" }),
        (24, new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]", "\\" }),
        (36, new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'" }),
        (60, new[] { "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/" }),
        (0, new[] { "LeftShift", "LeftControl", "LeftAlt", "Space", "RightAlt", "RightControl", "RightShift" }),
        (0, new[] { "Tab", "Return", "LeftArrow", "UpArrow", "DownArrow", "RightArrow" }),
    };

    private bool _custom;   // 点了「自定义」：当前键位正好是某套预设时也显示自定义
    private int _slot = -1; // 正在指定的按键框（0–3），-1 表示没选、不显示屏幕键盘

    /// <summary>收起自定义（切换页签、恢复默认值时）。当前键位不是预设时仍会显示自定义。</summary>
    public void Close()
    {
        _custom = false;
        _slot = -1;
    }

    public void Draw(Settings s)
    {
        // 这一帧画什么在开头定好：点击发生在画到一半时，改的是字段，下一帧才生效，
        // 否则同一帧里控件数量变了，IMGUI 会报「控件位置对不上」
        var custom = _custom || Array.IndexOf(Presets, s.KeyLabels) < 0;
        var slot = custom ? _slot : -1;

        WindowSkin.BeginRow(Lang.T("键位"));
        foreach (var preset in Presets)
        {
            if (GUILayout.Button(preset, !custom && preset == s.KeyLabels ? WindowSkin.Selected : WindowSkin.Button, GUILayout.MinWidth(56)))
            {
                s.KeyLabels = preset;
                Close();
            }
        }

        if (GUILayout.Button(Lang.T("自定义"), custom ? WindowSkin.Selected : WindowSkin.Button, GUILayout.MinWidth(56)) && !custom)
        {
            _custom = true;
            _slot = 0; // 直接从第 1 个键开始点
        }

        WindowSkin.EndRow();

        if (custom)
        {
            DrawCustom(s, slot);
        }
    }

    private void DrawCustom(Settings s, int slot)
    {
        var keys = KeyBindings.Split(s.KeyLabels);
        WindowSkin.BeginRow(Lang.T("自定义键位"));
        for (var i = 0; i < keys.Length; i++)
        {
            var shown = KeyBindings.Display(keys[i]);
            if (GUILayout.Button(shown.Length > 0 ? shown : "—", i == slot ? WindowSkin.Selected : WindowSkin.Button, GUILayout.Width(78)))
            {
                _slot = i == slot ? -1 : i;
            }
        }

        WindowSkin.EndRow();

        if (slot < 0)
        {
            WindowSkin.Hint(Lang.T("点击按键框可重新指定按键。"));
            return;
        }

        WindowSkin.Hint(Lang.T("为第 {0} 个按键框选择按键；已被占用的键会互换。", slot + 1));
        string? picked = null;
        foreach (var (indent, row) in Rows)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(indent);
            foreach (var key in row)
            {
                var style = KeyBindings.Same(key, keys[slot]) ? WindowSkin.Selected
                    : Array.Exists(keys, k => KeyBindings.Same(k, key)) ? WindowSkin.KeyInUse
                    : WindowSkin.Button;
                var shown = KeyBindings.Display(key);
                var width = key == "Space" ? SpaceWidth : shown.Length > 1 ? WideKeyWidth : KeyWidth;
                if (GUILayout.Button(shown, style, GUILayout.Width(width)))
                {
                    picked = key;
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        if (picked != null)
        {
            s.KeyLabels = KeyBindings.Assign(s.KeyLabels, slot, picked);
            _custom = true;
            _slot = slot + 1 < keys.Length ? slot + 1 : -1; // 四个都点完就收起键盘
        }
    }
}
