using Newtonsoft.Json.Linq;
using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>轨道预览要重放的关卡事件：只挑砖块移动、出现消失动画两类和相关设置，关掉的事件和谱面自带的镜头不要。</summary>
public class TrackEffectsTests
{
    private const string Level = """
        {
          "settings": { "song": "x", "relativeTo": "Player", "position": [0, 0], "rotation": 0, "zoom": 100,
                        "trackAnimation": "Fade", "beatsAhead": 3, "trackDisappearAnimation": "None", "beatsBehind": 4, "bpm": 160 },
          "actions": [
            { "floor": 1, "eventType": "SetSpeed", "beatsPerMinute": 200 },
            { "floor": 2, "eventType": "MoveTrack", "startTile": [0, "ThisTile"], "endTile": [3, "ThisTile"], "duration": 1,
              "positionOffset": [1, null], "scale": [200, 50], "opacity": 0, "angleOffset": 90, "ease": "OutQuad", "eventTag": "a" },
            { "floor": 3, "eventType": "MoveTrack", "active": false, "startTile": [0, "ThisTile"], "endTile": [0, "ThisTile"], "duration": 1 },
            { "floor": 4, "eventType": "AnimateTrack", "trackAnimation": "Grow", "beatsAhead": 2 },
            { "floor": 5, "eventType": "MoveCamera", "duration": 2, "relativeTo": "Tile", "position": [1, 2], "zoom": 150 },
            { "floor": 6, "eventType": "PositionTrack", "positionOffset": [1, 0], "rotation": 45 },
            { "floor": 7, "eventType": "AddDecoration", "tag": "x" }
          ]
        }
        """;

    [Fact]
    public void KeepsOnlyTrackMovementAndAnimationEvents()
    {
        var fx = TrackEffects.Extract(JObject.Parse(Level));

        var actions = (JArray)fx["actions"]!;
        Assert.Equal(new[] { "MoveTrack", "AnimateTrack", "PositionTrack" }, actions.Select(a => (string?)a["eventType"])); // 镜头事件不要
        Assert.Equal(2, (int)actions[0]["floor"]!);
        Assert.Null(actions[0]["eventTag"]); // 只留重放要用的参数
        Assert.Equal(JTokenType.Null, actions[0]["positionOffset"]![1]!.Type);
    }

    [Fact]
    public void KeepsTrackAnimationSettingsOnly()
    {
        var settings = (JObject)TrackEffects.Extract(JObject.Parse(Level))["settings"]!;

        Assert.Equal("Fade", (string?)settings["trackAnimation"]);
        Assert.Null(settings["zoom"]); // 谱面自带的镜头设置不要
        Assert.Null(settings["song"]);
        Assert.Null(settings["bpm"]);
    }

    [Fact]
    public void MissingSectionsGiveEmptyEffects()
    {
        var fx = TrackEffects.Extract(new JObject());

        Assert.Empty((JArray)fx["actions"]!);
        Assert.Empty((JObject)fx["settings"]!);
    }

    [Fact]
    public void BeatLengthFollowsTheTileSpeedNotTheBeatGrid()
    {
        // 复现（工坊 2802147583）：按节拍线的 BPM 换算，8 拍的镜头事件只剩 0.02 秒。事件的拍数按砖块当时的速度算
        var fx = TrackEffects.Extract(new JObject());
        TrackEffects.AddBeats(fx, new[] { (0.0, 1.0), (10.0, 2.0), (20.0, 0.5) }, 0.3);

        var beat = (JArray)fx["beat"]!;
        Assert.Equal(new[] { 0.3, 0.15, 0.6 }, beat.Select(b => (double)b[1]!));
        Assert.Equal(10.0, (double)beat[1][0]!);
    }

    [Fact]
    public void WithoutSpeedChangesTheStartingBeatIsUsed()
    {
        var fx = TrackEffects.Extract(new JObject());
        TrackEffects.AddBeats(fx, Array.Empty<(double, double)>(), 0.25);

        Assert.Equal(0.25, (double)((JArray)fx["beat"]!)[0][1]!);
    }

    [Fact]
    public void SessionCarriesTheEffects()
    {
        var session = new EditorSession { Effects = TrackEffects.Extract(JObject.Parse(Level)) };
        var json = JObject.Parse(session.ToScript().Substring("window.RHYTHMHINT_SESSION = ".Length).TrimEnd(';', '\n'));

        Assert.Equal(3, ((JArray)json["fx"]!["actions"]!).Count);
        Assert.Equal(JTokenType.Null, JObject.Parse(new EditorSession().ToScript().Substring("window.RHYTHMHINT_SESSION = ".Length).TrimEnd(';', '\n'))["fx"]!.Type);
    }
}
