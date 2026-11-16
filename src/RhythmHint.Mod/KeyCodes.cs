using System;
using System.Collections.Generic;
using UnityEngine;

namespace RhythmHint;

/// <summary>把设置里写的按键文字（如 "D"、";"、"Space"）转成 Unity 的 KeyCode。</summary>
internal static class KeyCodes
{
    private static readonly Dictionary<string, KeyCode> Symbols = new()
    {
        [";"] = KeyCode.Semicolon,
        [","] = KeyCode.Comma,
        ["."] = KeyCode.Period,
        ["/"] = KeyCode.Slash,
        ["'"] = KeyCode.Quote,
        ["["] = KeyCode.LeftBracket,
        ["]"] = KeyCode.RightBracket,
        ["\\"] = KeyCode.Backslash,
        ["-"] = KeyCode.Minus,
        ["="] = KeyCode.Equals,
        ["`"] = KeyCode.BackQuote,
    };

    public static KeyCode Parse(string label)
    {
        if (Symbols.TryGetValue(label, out var symbol))
        {
            return symbol;
        }

        if (label.Length == 1 && char.IsDigit(label[0]))
        {
            return KeyCode.Alpha0 + (label[0] - '0');
        }

        return Enum.TryParse<KeyCode>(label, true, out var key) ? key : KeyCode.None;
    }
}
