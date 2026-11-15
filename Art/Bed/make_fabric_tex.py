"""Generate a seamless plain-weave fabric albedo for the duvet.

The bed's original blanket texture is a patchwork, which at 1 m tiling turns a
1.58 m flat slab into what looks like a tiled floor.  This makes a neutral weave
instead: same warm hue family as the bedroom, no seams, no squares.

    python make_fabric_tex.py
"""
import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
DST = os.path.join(HERE, "T_Quilt_Fabric.png")

SIZE = 1024
BASE = np.array([0.46, 0.34, 0.31])      # warm brick, lighter than the old blanket


def periodic_noise(n, freq, seed):
    """低频平滑噪声，用周期三角函数保证四边可无缝平铺。"""
    rng = np.random.default_rng(seed)
    acc = np.zeros((SIZE, SIZE), np.float32)
    for k in range(freq):
        fx = rng.integers(1, 4)
        fy = rng.integers(1, 4)
        ph = rng.uniform(0, 2 * np.pi)
        x = np.linspace(0, 2 * np.pi * fx, SIZE, endpoint=False)
        y = np.linspace(0, 2 * np.pi * fy, SIZE, endpoint=False)
        acc += np.sin(x[None, :] + ph) * np.cos(y[:, None] - ph)
    acc /= max(1, freq)
    return acc


def main():
    y, x = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32)

    # 织纹：经纬线，周期 4px，相位连续所以无缝
    warp = np.sin(x * (2 * np.pi / 4.0)) * 0.5 + 0.5
    weft = np.sin(y * (2 * np.pi / 4.0)) * 0.5 + 0.5
    weave = (warp * 0.5 + weft * 0.5) - 0.5          # -0.5..0.5

    # 更大的皱感（低频），幅度很小
    low = periodic_noise(SIZE, 6, 7) * 0.5
    mid = periodic_noise(SIZE, 8, 21) * 0.25

    shade = 1.0 + weave * 0.055 + low * 0.05 + mid * 0.03

    img = np.clip(BASE[None, None, :] * shade[:, :, None], 0, 1)
    # 轻微色偏：暖处更红，冷处更灰
    img[:, :, 0] *= 1.0 + low * 0.03
    img[:, :, 2] *= 1.0 - low * 0.02

    out = (np.clip(img, 0, 1) * 255).astype(np.uint8)
    Image.fromarray(out, "RGB").save(DST)
    a = out.reshape(-1, 3)
    print("WROTE", DST, out.shape, "mean", a.mean(axis=0).round().astype(int))


main()
