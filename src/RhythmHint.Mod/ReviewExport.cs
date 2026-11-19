using System;
using System.Globalization;
using System.IO;
using System.Text;
using RhythmHint.Core;

namespace RhythmHint;

/// <summary>用户点击时读取游戏的实际砖块时间并导出；不依赖可能正在播放演示的 OverlayFrame。</summary>
internal static class ReviewExport
{
    private static Message _status = Lang.M("进入关卡或编辑器试玩后，可导出当前排键到 Mod 目录的 Reviews 文件夹。");
    internal static string Status => Lang.T(_status);

    internal static void CurrentLevel()
    {
        try
        {
            if (!GameReader.TryGetLevel(out var level, out var reason))
            {
                _status = Lang.M("无法导出：{0}", reason);
                return;
            }

            if (!level.ShouldDraw)
            {
                _status = Lang.M("无法导出：请先进入关卡或开始编辑器试玩（可以暂停后导出）。");
                return;
            }

            var hits = GameReader.ReadHits(level);
            if (hits.Count == 0)
            {
                _status = Lang.M("无法导出：当前关卡没有可按的砖块。");
                return;
            }

            var settings = Main.Settings;
            var options = settings.ToFingering();
            var notes = NotePlanner.Plan(hits, options);
            var labels = settings.KeyLabels.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var now = DateTimeOffset.UtcNow;
            var json = ReviewChart.Export(Lang.T("当前关卡 · {0} 次按键", hits.Count),
                "game", notes, options, labels.Length == Lanes.Count ? labels : null, now, sourceHits: hits);
            var directory = Path.Combine(Main.Mod.Path, "Reviews");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "review-" + now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
            }

            _status = Lang.M("已导出 {0} 个提示：\n{1}", notes.Count, path);
            Main.Mod.Logger.Log(_status.ToString());
        }
        catch (Exception ex)
        {
            // 文件权限等导出错误不应该停用提示面板，也不能在每帧重复抛出。
            _status = Lang.M("导出失败：{0}", ex.Message);
            Main.Mod.Logger.LogException(ex);
        }
    }
}
