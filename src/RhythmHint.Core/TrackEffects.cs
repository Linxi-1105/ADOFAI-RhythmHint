using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RhythmHint.Core;

/// <summary>
/// 关卡里让轨道动起来的事件和相关设置，交给手序编辑器的轨道预览按时间重放
/// （2026-11-18 用户：轨道预览没有动画，效果更差，要复刻游戏的轨道和动画）。
/// 用户定的范围：砖块移动（MoveTrack、PositionTrack）、出现消失动画（AnimateTrack）；不含颜色和轨道样式；
/// 镜头不用谱面自带的（MoveCamera，用户后来说不要），预览里只跟着星球。
/// 关掉的事件（"active": false）不要；每个事件只留重放要用的参数，免得 session.js 太大。
/// </summary>
public static class TrackEffects
{
    private static readonly string[] EventTypes = { "MoveTrack", "PositionTrack", "AnimateTrack" };

    private static readonly string[] EventKeys =
    {
        "floor", "eventType", "startTile", "endTile", "gapLength", "duration", "positionOffset", "rotationOffset", "rotation", "scale",
        "opacity", "angleOffset", "ease", "justThisTile", "editorOnly",
        "trackAnimation", "beatsAhead", "trackDisappearAnimation", "beatsBehind",
    };

    private static readonly string[] SettingKeys = { "trackAnimation", "beatsAhead", "trackDisappearAnimation", "beatsBehind" };

    /// <summary>
    /// 事件换算时间用的「一拍几秒」，写成 <c>"beat": [[时间, 一拍几秒], …]</c>：游戏里事件的拍数按触发那块砖当时的速度算
    /// （起始一拍 ÷ 砖块速度倍率），不能用节拍线的 BeatGrid.MusicalTempo（那个把 ×2、×0.5 当成只改疏密、拍子不变）。
    /// 复现（2026-11-18，工坊 2802147583）：用节拍线的 BPM 换算，8 拍的镜头事件只剩 0.02 秒。
    /// </summary>
    /// <param name="speeds">变速点：从 Time 开始砖块速度倍率为 Speed（相对起始 BPM）。</param>
    /// <param name="crotchetAtStart">起始 BPM 下一拍的秒数。</param>
    public static void AddBeats(JObject effects, IReadOnlyList<(double Time, double Speed)> speeds, double crotchetAtStart)
    {
        var beat = new JArray();
        foreach (var (time, speed) in speeds)
        {
            if (speed > 0)
            {
                beat.Add(new JArray(Math.Round(time, 6), Math.Round(crotchetAtStart / speed, 9)));
            }
        }

        if (beat.Count == 0)
        {
            beat.Add(new JArray(0.0, crotchetAtStart));
        }

        effects["beat"] = beat;
    }

    /// <param name="level">整份关卡（.adofai 的 JSON）。</param>
    /// <returns><c>{ "settings": {…}, "actions": [ … ] }</c>；没有的部分为空。</returns>
    public static JObject Extract(JObject level)
    {
        var settings = new JObject();
        if (level["settings"] is JObject source)
        {
            foreach (var key in SettingKeys)
            {
                if (source[key] is { } value)
                {
                    settings[key] = value.DeepClone();
                }
            }
        }

        var actions = new JArray();
        if (level["actions"] is JArray all)
        {
            foreach (var action in all.OfType<JObject>())
            {
                var type = (string?)action["eventType"];
                if (type == null || Array.IndexOf(EventTypes, type) < 0 || action["active"] is { Type: JTokenType.Boolean } active && !(bool)active)
                {
                    continue;
                }

                var kept = new JObject();
                foreach (var key in EventKeys)
                {
                    if (action[key] is { } value)
                    {
                        kept[key] = value.DeepClone();
                    }
                }

                actions.Add(kept);
            }
        }

        return new JObject { ["settings"] = settings, ["actions"] = actions };
    }
}
