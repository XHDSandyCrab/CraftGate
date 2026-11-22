# -*- coding: utf-8 -*-
"""生成启动加载动画 GIF：螺旋从左侧滚入 -> 展开成凹凸线条 -> 浮现'正在加载' -> 卷回从右侧滚出。
透明背景，循环播放。运行前需 pip install Pillow。"""
import math
import os
from PIL import Image, ImageDraw, ImageFont

W, H = 160, 44
GREEN = (61, 220, 132, 255)
GREEN_DIM = (61, 220, 132, 150)
TEXT = (242, 243, 245, 255)
CY = H / 2

# 找中文字体
def find_font(size):
    candidates = [
        r"C:\Windows\Fonts\msyh.ttc",
        r"C:\Windows\Fonts\msyhbd.ttc",
        r"C:\Windows\Fonts\simhei.ttf",
        r"C:\Windows\Fonts\arialuni.ttf",
    ]
    for p in candidates:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()

FONT = find_font(13)

def ease(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)

def lerp(a, b, t):
    return a + (b - a) * t

# 一条由 N 个点组成的线：参数 u in [0,1]
# coil=1 紧密螺旋，coil=0 展开为带凹凸波动的横线
def line_points(coil, center_x, phase):
    N = 120
    pts = []
    turns = 3.2
    for i in range(N):
        u = i / (N - 1)
        # 展开态：横线，带轻微凹凸波动
        flat_x = lerp(18, W - 18, u)
        flat_y = CY + 5 * math.sin(u * math.pi * 6 + phase) * math.sin(u * math.pi)
        # 卷曲态：阿基米德螺旋，半径随 u 增大
        theta = u * turns * 2 * math.pi + phase
        rad = 3 + u * 13
        coil_x = center_x + rad * math.cos(theta)
        coil_y = CY + rad * math.sin(theta)
        x = lerp(flat_x, coil_x, coil)
        y = lerp(flat_y, coil_y, coil)
        pts.append((x, y))
    return pts

def draw_frame(coil, center_x, text_alpha, phase, width=2):
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    pts = line_points(coil, center_x, phase)
    # 描边
    draw.line(pts, fill=GREEN, width=width, joint="curve")
    # 头部亮点
    if pts:
        hx, hy = pts[-1]
        draw.ellipse([hx - 2.5, hy - 2.5, hx + 2.5, hy + 2.5], fill=GREEN_DIM)
    if text_alpha > 0:
        txt = "正在加载"
        box = draw.textbbox((0, 0), txt, font=FONT)
        tw = box[2] - box[0]
        tx = (W - tw) / 2
        ty = CY - (box[3] - box[1]) / 2 - box[1]
        draw.text((tx, ty), txt, font=FONT, fill=(242, 243, 245, int(text_alpha)))
    return img

frames = []
# 阶段1：螺旋从左滚入 (coil=1, center_x: -30 -> 40)
for f in range(10):
    t = ease(f / 9)
    frames.append(draw_frame(1.0, lerp(-30, 40, t), 0, f * 0.4))
# 阶段2：螺旋展开成凹凸线 (coil: 1 -> 0, center_x 过渡到中心)
for f in range(14):
    t = ease(f / 13)
    frames.append(draw_frame(lerp(1.0, 0.0, t), lerp(40, W / 2, t), 0, f * 0.3))
# 阶段3：文字浮现 + 线条轻微波动
for f in range(10):
    t = ease(f / 9)
    frames.append(draw_frame(0.0, W / 2, 255 * t, f * 0.5))
# 停留
for f in range(6):
    frames.append(draw_frame(0.0, W / 2, 255, f * 0.5))
# 阶段4：文字淡出 + 开始卷回
for f in range(8):
    t = ease(f / 7)
    frames.append(draw_frame(lerp(0.0, 1.0, t), lerp(W / 2, W - 40, t), 255 * (1 - t), f * 0.4))
# 阶段5：螺旋从右滚出 (coil=1, center_x: W-40 -> W+30)
for f in range(10):
    t = ease(f / 9)
    frames.append(draw_frame(1.0, lerp(W - 40, W + 30, t), 0, f * 0.4))

durations = [70] * len(frames)
out = r"D:\project\CraftGate\Assets\loading.gif"
os.makedirs(os.path.dirname(out), exist_ok=True)
frames[0].save(out, save_all=True, append_images=frames[1:], duration=durations,
               loop=0, disposal=2, optimize=True)
print("saved:", out, "frames:", len(frames), "size:", os.path.getsize(out))
