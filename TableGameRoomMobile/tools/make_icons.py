#!/usr/bin/env python3
"""
生成 TABLE 游戏厅 的安卓图标资源。

设计完全沿用 Windows 版 app.ico（白底圆 + 青色外环 + 灰色内环 + 黑色 T），
但原始 .ico 只有 64x64，放大后边缘全是锯齿。这里用矢量参数重画，
4 倍超采样后再降采样，得到干净的高分辨率版本。

几何参数是从 64x64 原图逐像素扫描反推出来的（单位：原始像素）：
    T 字形      x 26..38 (13 宽)   y 24..41 (18 高)
    T 顶横      高 3，竖笔宽 3
    灰色内环    中心半径 22，线宽 2.8
    青色外环    中心半径 29，线宽 3
    白色圆盘    半径 31

用法：
    python tools/make_icons.py
产物写到 Assets/Icons/ 下。
"""

import os
import sys
from PIL import Image, ImageDraw

# ---------------------------------------------------------------- 设计参数

CENTER = 32.0          # 64x64 原始网格的中心
OUTER_R = 31.0         # 白色圆盘半径（也是青色环外沿）

TEAL_R, TEAL_W = 29.0, 3.0      # 青色外环：中心半径 / 线宽
GRAY_R, GRAY_W = 22.0, 2.8      # 灰色内环：中心半径 / 线宽

T_W, T_H = 13.0, 18.0           # T 总宽 / 总高
T_BAR_H, T_STEM_W = 3.0, 3.0    # T 横画高度 / 竖笔宽度

TEAL = (0, 128, 128, 255)
GRAY = (128, 128, 128, 255)
WHITE = (255, 255, 255, 255)
BLACK = (0, 0, 0, 255)

SS = 4                 # 超采样倍数


def draw_logo(size, outer_r, bg=None, disc=True):
    """
    按设计画一枚图标。

    size     : 输出边长（像素）
    outer_r  : 白色圆盘半径（像素），其余元素按比例缩放
    bg       : 底色；None 表示透明
    disc     : 是否画白色圆盘（自适应前景用 True，纯背景层用 False）
    """
    big = size * SS
    img = Image.new("RGBA", (big, big), bg if bg else (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    cx = cy = big / 2.0
    k = outer_r * SS / OUTER_R        # 原始单位 → 像素

    # 白色圆盘
    if disc:
        d.ellipse([cx - OUTER_R * k, cy - OUTER_R * k,
                   cx + OUTER_R * k, cy + OUTER_R * k], fill=WHITE)

    # 环：PIL 的 width 从外接矩形向内收，所以外扩半个线宽即可让中心落在目标半径上
    def ring(radius, width, color):
        r = radius * k
        w = max(1, round(width * k))
        d.ellipse([cx - r - w / 2, cy - r - w / 2,
                   cx + r + w / 2, cy + r + w / 2], outline=color, width=w)

    ring(GRAY_R, GRAY_W, GRAY)
    ring(TEAL_R, TEAL_W, TEAL)

    # T 字形：横画 + 竖笔，居中
    tw, th = T_W * k, T_H * k
    bar = T_BAR_H * k
    stem = T_STEM_W * k

    x0 = cx - tw / 2.0
    y0 = cy - th / 2.0

    d.rectangle([x0, y0, x0 + tw, y0 + bar], fill=BLACK)                    # 横画
    d.rectangle([cx - stem / 2.0, y0, cx + stem / 2.0, y0 + th], fill=BLACK)  # 竖笔

    return img.resize((size, size), Image.LANCZOS)


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out = os.path.join(root, "Assets", "Icons")
    os.makedirs(out, exist_ok=True)

    # ---------- 传统图标（Unity 会自动缩放到各密度）----------
    # 1024 里圆盘半径取 496，四周留一点透明边，符合各启动器的习惯
    legacy = draw_logo(1024, 496)
    legacy.save(os.path.join(out, "app_icon.png"))
    print("app_icon.png")

    # ---------- 自适应图标 ----------
    # 自适应图标分两层，Unity 顺序是 [背景, 前景]。
    # 安全区只有 108dp 里的 66dp（61%），前景图形必须缩进去，
    # 否则圆形/圆角遮罩会把外圈切掉。
    #
    # 背景层：纯白铺满，遮罩怎么裁都是白的。
    # 前景层：青色外环 + 灰色内环 + T（不画白色圆盘，白底由背景层提供）。
    bg = Image.new("RGBA", (1024, 1024), WHITE)
    bg.save(os.path.join(out, "app_icon_bg.png"))
    print("app_icon_bg.png")

    # 可见区是 108dp 里的 72dp（66.7%），图形直径取到这个比例刚好铺满又不被裁。
    fg = draw_logo(1024, 341, disc=False)
    fg.save(os.path.join(out, "app_icon_fg.png"))
    print("app_icon_fg.png")

    # ---------- 开屏用的方形 logo（透明底，运行时叠在深色背景上）----------
    # 圆盘半径取 1024 的 40%，留出足够留白，竖屏上不会顶到左右边缘。
    splash = draw_logo(1024, 410)
    splash.save(os.path.join(out, "splash_logo.png"))
    print("splash_logo.png")

    # ---------- 顺手更新 Windows 的 .ico（多尺寸，解决桌面图标模糊）----------
    win = os.path.join(os.path.dirname(root), "xin")
    if os.path.isdir(win):
        sizes = [16, 24, 32, 48, 64, 128, 256]
        frames = [draw_logo(s * 4, s * 4 * 496 / 1024).resize((s, s), Image.LANCZOS)
                  for s in sizes]
        ico = os.path.join(win, "app.ico")
        frames[-1].save(ico, format="ICO",
                        sizes=[(s, s) for s in sizes])
        print("xin/app.ico  (%d sizes)" % len(sizes))


if __name__ == "__main__":
    main()
