using RhythmHint.Reference;

namespace RhythmHint.Core.Tests;

public class ChartTests
{
    private const string TwoBpmChart = """
        {
          "BPMList": [
            { "bpm": 120.0, "startTime": [0, 0, 1] },
            { "bpm": 240.0, "startTime": [4, 0, 1] }
          ],
          "META": { "name": "Test Song" },
          "judgeLineList": [
            {
              "bpmfactor": 1.0,
              "notes": [
                { "type": 1, "startTime": [1, 0, 1], "endTime": [1, 0, 1], "positionX": -337.5, "isFake": 0 },
                { "type": 1, "startTime": [1, 0, 1], "endTime": [1, 0, 1], "positionX": -135.0, "isFake": 0 },
                { "type": 2, "startTime": [2, 1, 2], "endTime": [3, 0, 1], "positionX": 135.0, "isFake": 0 },
                { "type": 1, "startTime": [6, 0, 1], "endTime": [6, 0, 1], "positionX": 337.5, "isFake": 0 },
                { "type": 1, "startTime": [7, 0, 1], "endTime": [7, 0, 1], "positionX": 0.0, "isFake": 1 }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Parse_ConvertsBeatsWithBpmChanges()
    {
        var chart = RpeChart.Parse(TwoBpmChart, "fallback");

        Assert.Equal("Test Song", chart.Name);
        Assert.Equal(4, chart.Notes.Count); // isFake 的音符被跳过
        Assert.Equal(0.5, chart.Notes[0].Time, 6);   // 第 1 拍 @120
        Assert.Equal(1.25, chart.Notes[2].Time, 6);  // 第 2.5 拍 @120
        Assert.Equal(0.25, chart.Notes[2].HoldSeconds, 6);
        Assert.Equal(2.5, chart.Notes[3].Time, 6);   // 4 拍 2 秒 + 2 拍 @240 0.5 秒
    }

    [Fact]
    public void ToGroups_MergesSameTimeIntoChord()
    {
        var groups = RpeChart.Parse(TwoBpmChart, "x").ToGroups();

        Assert.Equal(3, groups.Count);
        Assert.Equal(2, groups[0].LaneCount);
        Assert.True(groups[0].Holds[0].HasValue);
        Assert.True(groups[0].Holds[1].HasValue);
        Assert.Equal(0.25, groups[1].Holds[2]!.Value, 6);
    }

    [Fact]
    public void Parse_RejectsNonRpeJson()
    {
        Assert.Throws<ChartFormatException>(() => RpeChart.Parse("{\"foo\": 1}", "x"));
        Assert.Throws<ChartFormatException>(() => RpeChart.Parse("not json", "x"));
    }

    [Theory]
    [InlineData(-405.0, 0)]
    [InlineData(-337.5, 0)]
    [InlineData(-135.0, 1)]
    [InlineData(135.0, 2)]
    [InlineData(337.5, 3)]
    public void LaneMapper_SplitsAtLargestGaps(double x, int lane)
    {
        // 用户谱面里出现过的五个位置
        var mapper = LaneMapper.FromPositions(new[] { -405.0, -337.5, -135.0, 135.0, 337.5 });
        Assert.Equal(lane, mapper.LaneOf(x));
    }

    [Fact]
    public void LaneMapper_FewPositionsSpreadOut()
    {
        var mapper = LaneMapper.FromPositions(new[] { -100.0, 100.0 });
        Assert.Equal(0, mapper.LaneOf(-100));
        Assert.Equal(3, mapper.LaneOf(100));
    }

    [Fact]
    public void Load_ReadsPezPackage()
    {
        var dir = Directory.CreateTempSubdirectory("rhythmhint");
        try
        {
            var pez = Path.Combine(dir.FullName, "song.pez");
            using (var zip = System.IO.Compression.ZipFile.Open(pez, System.IO.Compression.ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(zip.CreateEntry("info.txt").Open()))
                {
                    w.Write("#\nName: Test\nChart: 123.json\n");
                }

                using (var w = new StreamWriter(zip.CreateEntry("123.json").Open()))
                {
                    w.Write(TwoBpmChart);
                }
            }

            var chart = RpeChart.Load(pez);
            Assert.Equal(4, chart.Notes.Count);
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
