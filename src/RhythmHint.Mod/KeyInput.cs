using System;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>读设置里那四个键是否按下，用来点亮按键框（只是显示，不影响判定）。</summary>
internal sealed class KeyInput
{
    private KeyCode[] _keys = Array.Empty<KeyCode>();
    private string _parsedLabels = string.Empty;
    private bool _broken;

    public void Read(Settings settings, OverlayFrame frame)
    {
        if (settings.KeyLabels != _parsedLabels)
        {
            _parsedLabels = settings.KeyLabels;
            frame.KeyLabels = settings.KeyLabels.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            _keys = new KeyCode[Lanes.Count];
            for (var i = 0; i < _keys.Length && i < frame.KeyLabels.Length; i++)
            {
                _keys[i] = KeyCodes.Parse(frame.KeyLabels[i]);
            }
        }

        if (!settings.LightKeysOnPress || _broken)
        {
            Array.Clear(frame.KeysDown, 0, frame.KeysDown.Length);
            return;
        }

        try
        {
            for (var i = 0; i < _keys.Length; i++)
            {
                frame.KeysDown[i] = _keys[i] != KeyCode.None && Input.GetKey(_keys[i]);
            }
        }
        catch (InvalidOperationException ex)
        {
            // 游戏关掉了旧版输入系统时会抛这个：按键框改为只随命中闪烁
            _broken = true;
            Main.Mod.Logger.Warning($"无法读取键盘状态，按键框仅在命中时闪烁：{ex.Message}");
        }
    }
}
