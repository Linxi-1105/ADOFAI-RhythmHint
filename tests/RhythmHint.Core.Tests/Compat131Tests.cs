using System.Globalization;
using Newtonsoft.Json.Linq;
using RhythmHint.Core;
using V131 = RhythmHint131.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 和玩家手里的 1.3.0 / 1.3.1 比（2026-11-19 用户：不能再有破坏性更新）。Frozen131/ 里是 1.3.1 原样的 CustomFingering、FloorHits、Models，
/// 只换了命名空间：现在写出的手序、编辑器存的手序，1.3.1 都要读得进来、读出来的一样；取砖规则和谱面指纹和 1.3.1 逐项一样。
/// 复现（2026-11-19 评审的变异测试）：写文件时砖号、轨道写成 1.0（1.3.0 / 1.3.1 读不了），只有可以重新生成的样例测试失败。
/// </summary>
public class Compat131Tests
{
    private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static string Hits(IEnumerable<(int Tile, double Time, double Hold, double Angle)> hits) =>
        string.Join("; ", hits.Select(h => $"{h.Tile},{R(h.Time)},{R(h.Hold)},{R(h.Angle)}"));

    private static string Notes(IEnumerable<(int First, int Last, double Time, IEnumerable<KeyValuePair<int, double>> Lanes)> notes) =>
        string.Join("; ", notes.Select(n => $"{n.First}-{n.Last}@{R(n.Time)}:" + string.Join("+", n.Lanes.Select(l => $"{l.Key}/{R(l.Value)}"))));

    /// <summary>现在的 Mod 读到的和 1.3.1 读到的一样（更新时间见 UpdatedAtAsReadBy131）。</summary>
    private static void AssertSameContent(CustomFingering now, V131.CustomFingering old)
    {
        Assert.Equal(now.Keys, old.Keys);
        Assert.Equal(now.Song, old.Song);
        Assert.Equal(now.Artist, old.Artist);
        Assert.Equal(now.Charter, old.Charter);
        Assert.Equal(now.Author, old.Author);
        Assert.Equal(now.Comment, old.Comment);
        Assert.Equal(now.LeftHanded, old.LeftHanded);
        Assert.Equal(now.Charts, old.Charts);
        Assert.Equal(Hits(now.Hits.Select(h => (h.TileIndex, h.Time, h.HoldSeconds, h.Angle))), Hits(old.Hits.Select(h => (h.TileIndex, h.Time, h.HoldSeconds, h.Angle))));
        Assert.Equal(Notes(now.Notes.Select(n => (n.TileIndex, n.LastTileIndex, n.Time, (IEnumerable<KeyValuePair<int, double>>)n.Lanes))),
            Notes(old.Notes.Select(n => (n.TileIndex, n.LastTileIndex, n.Time, (IEnumerable<KeyValuePair<int, double>>)n.Lanes))));
    }

    /// <summary>
    /// 一份什么都有的手序：像游戏那样累加的时间、读不到的转角、长按，玩家改出的三押、四押、跨手双押、每个键各自的长按，
    /// 富文本、引号、换行、emoji，作者为更新版保存过的旧指纹，以后的版本加的字段。
    /// 不放像日期的文字：1.3.1 把它当成日期、整份拒绝（它自己写的也一样，见共用样例 06-text-like-a-date）。
    /// </summary>
    private static CustomFingering Rich()
    {
        var hits = FingeringFormatLockTests.GameLikeHits();
        var notes = NotePlanner.Plan(hits, new FingeringOptions());
        void Set(int index, params (int Lane, double Hold)[] lanes)
        {
            notes[index].Lanes.Clear();
            foreach (var (lane, hold) in lanes)
            {
                notes[index].Lanes[lane] = hold;
            }
        }

        Set(3, (0, 0), (1, 0), (3, 0));
        Set(5, (0, 0), (1, 0.125), (2, 0.5), (3, 0));
        Set(8, (1, 0), (2, 0));
        var fingering = CustomFingering.From(hits, notes, leftHanded: true);
        fingering.Song = "<color=#ffcc00>冰与火</color> \"Song\" \\ 🎵";
        fingering.Artist = "Artist / 曲师";
        fingering.Charter = "谱师\n第二行";
        fingering.Author = "작가 ✓";
        fingering.Comment = "说明\t<b>富文本</b>";
        fingering.UpdatedAt = new DateTimeOffset(2026, 11, 19, 12, 34, 56, 789, TimeSpan.Zero);
        fingering.Charts.Add("12-0123456789abcdef01234567");
        fingering.Extra["future"] = JObject.Parse("{\"list\":[1,2.5,\"x\",null,true],\"nested\":{\"a\":{}}}");
        fingering.Extra["notice"] = "以后的版本加的字段";
        return fingering;
    }

    [Fact]
    public void FilesWrittenNowAreReadBy131()
    {
        var written = Rich();
        var json = written.ToJson();

        var now = CustomFingering.Parse(json);
        var old = V131.CustomFingering.Parse(json);

        // 1.3.1 读出的就是写进去的（两边一起读错也算不对：比如 leftHanded 写成 1，两边都读成 false）
        Assert.Equal((written.Keys, written.Song, written.Artist, written.Charter, written.Author, written.Comment, written.LeftHanded),
            (old.Keys, old.Song, old.Artist, old.Charter, old.Author, old.Comment, old.LeftHanded));
        Assert.Equal(written.Charts, old.Charts.Take(written.Charts.Count));
        Assert.Equal(written.Hits.Select(h => (h.TileIndex, Math.Round(h.Time, 6), Math.Round(h.HoldSeconds, 6), Math.Round(h.Angle, 3))),
            old.Hits.Select(h => (h.TileIndex, h.Time, h.HoldSeconds, h.Angle)));
        Assert.Equal(Notes(written.Notes.Select(n => (n.TileIndex, n.LastTileIndex, Math.Round(n.Time, 6), (IEnumerable<KeyValuePair<int, double>>)n.Lanes))),
            Notes(old.Notes.Select(n => (n.TileIndex, n.LastTileIndex, n.Time, (IEnumerable<KeyValuePair<int, double>>)n.Lanes))));
        AssertSameContent(now, old);
        Assert.Contains(now.Notes, n => n.Lanes.Count == 4);
        Assert.Contains(now.Hits, h => double.IsNaN(h.Angle));
        Assert.Equal(3, now.Charts.Count); // 游戏里的、旧版的、按文件里 6 位小数的时间算的（这组时间正好有几处舍入不同）
    }

    /// <summary>编辑器存的手序（node 测试生成的 saved.json、1.3.x 编辑器存的冻结样例）：1.3.1 读得进来，读出来的和现在一样。</summary>
    [Theory]
    [InlineData("saved.json")]
    [InlineData("frozen/1.3.x/fingering.json")]
    public void EditorFilesAreReadBy131(string name)
    {
        var json = File.ReadAllText(Path.Combine(EditorContractTests.Fixtures, name));

        AssertSameContent(CustomFingering.Parse(json), V131.CustomFingering.Parse(json));
    }

    /// <summary>
    /// 1.3.1 读 updatedAt：编辑器写的「…Z」读对了；Mod 写的「…+00:00」被 Newtonsoft 先转成本地时间、再当成 UTC，差了本机的时区、毫秒也丢了
    /// （1.3.1 自己的老问题，它读自己写的文件也一样；现在的 Mod 不自动转日期，读得对）。这里只锁住「读得进来」、差不过一天。
    /// </summary>
    [Fact]
    public void UpdatedAtAsReadBy131()
    {
        var written = Rich();
        var old = V131.CustomFingering.Parse(written.ToJson());

        Assert.Equal(written.UpdatedAt, CustomFingering.Parse(written.ToJson()).UpdatedAt);
        Assert.InRange((old.UpdatedAt - written.UpdatedAt).Duration(), TimeSpan.Zero, TimeSpan.FromHours(14));
        var editor = V131.CustomFingering.Parse(File.ReadAllText(Path.Combine(EditorContractTests.Fixtures, "saved.json")));
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero), editor.UpdatedAt);
    }

    /// <summary>随机的砖：大多按时间排好，偶尔倒退、同一时间；有起点、长按、自动、自动长按、已销毁的砖（像 GameReader 那样记成 -1、时间 0）。</summary>
    private static List<FloorInfo> RandomFloors(Random rng)
    {
        var floors = new List<FloorInfo>();
        var mode = rng.Next(3); // 转角按离开对得上、按到达对得上、乱写
        var t = 0.5;
        var count = rng.Next(0, 40);
        double[] steps = { 0.25, 0.5, 0.125, 0.0625, 0.375, 0, -0.125 };
        double[] speeds = { 1, 1, 2, 0.5, 1.5 };
        double[] angles = { double.NaN, 0, 15, 30, 45, 90, 180, 360, 7.5 };
        var lastAngle = double.NaN;
        for (var i = 0; i < count; i++)
        {
            if (i > 0 && rng.Next(10) == 0)
            {
                floors.Add(new FloorInfo(-1, 0, false, true));
                continue;
            }

            var speed = speeds[rng.Next(speeds.Length)];
            var step = steps[rng.Next(steps.Length)] * (rng.Next(5) == 0 ? 1 + (rng.NextDouble() * 0.06) - 0.03 : 1);
            var swept = step > 0 ? step / 0.5 * 180 * speed : angles[rng.Next(angles.Length)];
            var angle = mode switch
            {
                0 => swept,
                1 => lastAngle,
                _ => angles[rng.Next(angles.Length)],
            };
            lastAngle = swept;
            floors.Add(new FloorInfo(i, t, rng.Next(5) == 0, rng.Next(7) == 0, angle, speed));
            t += step;
        }

        return floors;
    }

    /// <summary>
    /// 取砖规则和 1.3.1 逐项一样，只有 1.3.1 会算出负长按的那几块不一样（长按链后面紧跟已销毁的砖、时间倒退）：现在不是负数。
    /// 其余的砖号、时间、长按、转角、顺序，以及转角核对的结果，一点不差。
    /// </summary>
    [Fact]
    public void FloorHitsMatch131ExceptNegativeHolds()
    {
        var rng = new Random(20261119);
        int compared = 0, negative = 0;
        for (var round = 0; round < 4000; round++)
        {
            var floors = RandomFloors(rng);
            var old = floors.ConvertAll(f => new V131.FloorInfo(f.SeqId, f.Time, f.IsHold, f.NoPress, f.Angle, f.Speed));
            var oc = V131.FloorHits.CheckAngles(old, 0.5);
            var nc = FloorHits.CheckAngles(floors, 0.5);
            Assert.Equal((oc.Source.ToString(), oc.ExitMatch, oc.ArrivalMatch), (nc.Source.ToString(), nc.ExitMatch, nc.ArrivalMatch));
            foreach (var crotchet in new[] { double.NaN, 0.5 })
            {
                var expected = V131.FloorHits.From(old, crotchet);
                var actual = FloorHits.From(floors, crotchet);
                Assert.Equal(expected.Count, actual.Count);
                for (var i = 0; i < expected.Count; i++)
                {
                    var (e, a) = (expected[i], actual[i]);
                    Assert.Equal((e.TileIndex, e.Time, e.Angle), (a.TileIndex, a.Time, a.Angle));
                    if (e.HoldSeconds >= 0)
                    {
                        Assert.Equal(e.HoldSeconds, a.HoldSeconds);
                    }
                    else
                    {
                        negative++;
                        Assert.True(a.HoldSeconds >= 0, $"第 {round} 组第 {i} 块：长按 {a.HoldSeconds}");
                    }

                    compared++;
                }

                // 现在的结果做得成手序（1.3.1 有负长按时这里抛 FormatException）
                if (actual.Count > 0 && actual.Zip(actual.Skip(1)).All(p => p.First.TileIndex < p.Second.TileIndex))
                {
                    CustomFingering.From(actual, NotePlanner.Plan(actual, new FingeringOptions()), leftHanded: false);
                }
            }
        }

        Assert.True(compared > 50000 && negative > 100, $"比了 {compared} 块，负长按 {negative} 块");
    }

    /// <summary>谱面指纹和 1.3.1 逐项一样：随机的间隔（很多落在 x.5 毫秒附近）、转角（x.5°、从弧度换回来的）、长按。</summary>
    [Fact]
    public void ChartPrintMatches131()
    {
        var rng = new Random(1911);
        double[] beats = { 1, 0.5, 0.25, 1 / 3.0, 0.125, 1 / 6.0, 0.0625, 0 };
        for (var round = 0; round < 2000; round++)
        {
            var hits = new List<TileHit>();
            var bpm = 100 + rng.Next(200);
            var t = rng.NextDouble() * 3;
            var count = rng.Next(1, 60);
            for (var i = 0; i < count; i++)
            {
                var beat = beats[rng.Next(beats.Length)];
                t += beat * 60 / bpm;
                var degrees = beat * 180 + (rng.Next(4) == 0 ? 7.5 : 0);
                var angle = rng.Next(6) == 0 ? double.NaN : rng.Next(2) == 0 ? degrees * Math.PI / 180 * (180 / Math.PI) : degrees;
                hits.Add(new TileHit(i + 1, t, rng.Next(8) == 0 ? rng.NextDouble() : 0, angle));
            }

            Assert.Equal(V131.CustomFingering.ChartPrint(hits.ConvertAll(h => new V131.TileHit(h.TileIndex, h.Time, h.HoldSeconds, h.Angle))), CustomFingering.ChartPrint(hits));
        }
    }
}
