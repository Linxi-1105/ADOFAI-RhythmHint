"""生成设置窗口（F8）的图标 src/RhythmHint.Mod/SettingsIcons/<名字>.png。
图标源是同目录的 icons.svg（24×24 线条图标）；每个画成 64×64 的白色透明底 PNG，编进 DLL，绘制时再染色（图标颜色跟着状态变）。
用 Edge 渲染 SVG（Pillow 画不了 SVG 的描边和曲线）。
运行：python tools/make-settings-icons.py（要 playwright、Pillow 两个包和本机的 Edge；只有改图标时才需要跑，生成的 PNG 提交进仓库）"""
import pathlib
import re

from PIL import Image
from playwright.sync_api import sync_playwright

ROOT = pathlib.Path(__file__).resolve().parent.parent
FOLDER = ROOT / "src" / "RhythmHint.Mod" / "SettingsIcons"
SIZE = 64


def symbols():
    text = (FOLDER / "icons.svg").read_text(encoding="utf-8")
    return re.findall(r'<symbol id="([^"]+)" viewBox="0 0 24 24">(.*?)</symbol>', text, re.S)


def page(body):
    # 描边 1.8 是 24 格里的宽度；64 像素时约 4.8 像素，缩到 18–22 像素显示时和网页上一样粗
    return f"""<!doctype html><html><head><style>
html, body {{ margin: 0; background: transparent; }}
svg {{ display: block; width: {SIZE}px; height: {SIZE}px; fill: none; stroke: #fff; stroke-width: 1.8;
       stroke-linecap: round; stroke-linejoin: round; }}
svg .fill {{ fill: #fff; stroke: none; }}
</style></head><body><svg viewBox="0 0 24 24">{body}</svg></body></html>"""


def main():
    found = symbols()
    with sync_playwright() as p:
        browser = p.chromium.launch(channel="msedge")
        tab = browser.new_page(viewport={"width": SIZE, "height": SIZE}, device_scale_factor=1)
        for name, body in found:
            tab.set_content(page(body))
            tab.locator("svg").screenshot(path=str(FOLDER / f"{name}.png"), omit_background=True)
        browser.close()

    # 透明像素的颜色也改成白色、只留透明度：游戏里缩小（mipmap）时边缘不会混进黑色发暗
    for name, _ in found:
        path = FOLDER / f"{name}.png"
        alpha = Image.open(path).getchannel("A")
        white = Image.new("RGBA", alpha.size, (255, 255, 255, 0))
        white.putalpha(alpha)
        white.save(path, optimize=True)

    # 删掉 icons.svg 里已经没有的旧图标
    names = {name for name, _ in found}
    for png in FOLDER.glob("*.png"):
        if png.stem not in names:
            png.unlink()
    print(f"已生成 {len(found)} 个图标到 {FOLDER}")


if __name__ == "__main__":
    main()
