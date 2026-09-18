# 生成启动图标：深蓝底 + 金色圆桌剑徽记
# 用法：python make_icons.py
import pathlib
from PIL import Image, ImageDraw

root = pathlib.Path(__file__).parent
res = root / "avalon-android" / "app" / "src" / "main" / "res"

BG = (16, 21, 42, 255)       # #10152A
BG_EDGE = (27, 35, 64, 255)  # #1B2340
GOLD = (212, 175, 55, 255)   # #D4AF37
GOLD_L = (240, 217, 140, 255)
SILVER = (127, 176, 255, 255)

def draw_icon(size: int) -> Image.Image:
    s = size
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # 圆角方形底
    r = int(s * 0.22)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=r, fill=BG, outline=BG_EDGE, width=max(1, s // 48))
    # 金色外环（圆桌）
    cx = cy = s / 2
    ring_r = s * 0.40
    ring_w = max(2, int(s * 0.045))
    d.ellipse([cx - ring_r, cy - ring_r, cx + ring_r, cy + ring_r], outline=GOLD, width=ring_w)
    # 中央宝剑（竖直）：剑尖朝上
    blade_w = s * 0.075
    blade_top = s * 0.14
    blade_bot = s * 0.62
    d.polygon([
        (cx, blade_top),                      # 剑尖
        (cx - blade_w / 2, blade_bot),
        (cx + blade_w / 2, blade_bot),
    ], fill=GOLD_L)
    # 护手
    guard_w = s * 0.34
    guard_h = s * 0.055
    d.rounded_rectangle(
        [cx - guard_w / 2, blade_bot, cx + guard_w / 2, blade_bot + guard_h],
        radius=guard_h / 2, fill=GOLD)
    # 剑柄
    hilt_w = s * 0.05
    hilt_bot = s * 0.80
    d.rectangle([cx - hilt_w / 2, blade_bot + guard_h, cx + hilt_w / 2, hilt_bot], fill=GOLD_L)
    # 柄尾圆球
    pr = s * 0.055
    d.ellipse([cx - pr, hilt_bot - pr, cx + pr, hilt_bot + pr], fill=GOLD)
    return img

SIZES = {
    "mipmap-mdpi": 48,
    "mipmap-hdpi": 72,
    "mipmap-xhdpi": 96,
    "mipmap-xxhdpi": 144,
    "mipmap-xxxhdpi": 192,
}

for folder, px in SIZES.items():
    out = res / folder
    out.mkdir(parents=True, exist_ok=True)
    draw_icon(px).save(out / "ic_launcher.png")
    print("生成", out / "ic_launcher.png")
