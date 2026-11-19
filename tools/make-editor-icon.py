"""生成手序编辑器的图标 src/RhythmHint.Editor/icon.ico（桌面快捷方式用）。
图形和编辑器网页图标（index.html 里的 SVG）一样：深色圆角方块上四根彩色竖条。先画 32 倍大再缩小，边缘平滑。
运行：python tools/make-editor-icon.py（要 Pillow；只有改图标时才需要跑，生成的 icon.ico 提交进仓库）"""
import pathlib

from PIL import Image, ImageDraw

# 和 index.html 的 SVG 一样，单位是 32×32 的画布
BACKGROUND = ("#16181d", 0, 0, 32, 32, 7)
BARS = [
    ("#f57829", 5.75, 16, 4, 10),
    ("#3d9ee0", 11.25, 9.3, 4, 16.7),
    ("#f57829", 16.75, 6, 4, 20),
    ("#b273f2", 22.25, 12.7, 4, 13.3),
]
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SCALE = 32  # 画在 1024×1024 上


def draw():
    big = Image.new("RGBA", (32 * SCALE, 32 * SCALE), (0, 0, 0, 0))
    g = ImageDraw.Draw(big)
    color, x, y, w, h, r = BACKGROUND
    g.rounded_rectangle([x * SCALE, y * SCALE, (x + w) * SCALE - 1, (y + h) * SCALE - 1], radius=r * SCALE, fill=color)
    for color, x, y, w, h in BARS:
        g.rounded_rectangle([x * SCALE, y * SCALE, (x + w) * SCALE - 1, (y + h) * SCALE - 1], radius=2 * SCALE, fill=color)
    return big


def main():
    out = pathlib.Path(__file__).resolve().parent.parent / "src" / "RhythmHint.Editor" / "icon.ico"
    big = draw()
    big.resize((256, 256), Image.LANCZOS).save(out, format="ICO", sizes=[(s, s) for s in SIZES])
    print(f"已生成 {out}（{out.stat().st_size} 字节）")


if __name__ == "__main__":
    main()
