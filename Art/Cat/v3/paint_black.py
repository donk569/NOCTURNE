"""Repaint the tabby coat as the NOCTURNE black cat (design board: 看似普通，却总在暗处注视).

Reuses the existing object-space raster (POS/NRM) and the eye positions measured
from the socket, so the eyes land in exactly the same place as the tabby version.

    python paint_black.py

Outputs
  out/T_Cat_Black_Albedo.png   近黑毛 + 淡黄绿虹膜（直接当 URP BaseMap）
  out/T_Cat_Black_Emit.png     只有虹膜发光的自发光贴图
"""
import json
import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")

# 黑猫配色（sRGB 0~1）。纯黑会把形体吃没，所以取"比黑稍亮一点点的冷黑"，
# 靠高光和眼睛读出轮廓。
COAT = np.array([0.055, 0.052, 0.050])
COAT_WARM = np.array([0.070, 0.056, 0.050])
MUZZLE = np.array([0.105, 0.098, 0.092])
IRIS_IN = np.array([0.95, 0.92, 0.62])
IRIS_OUT = np.array([0.62, 0.68, 0.30])
RIM = np.array([0.015, 0.014, 0.012])
PUPIL = np.array([0.008, 0.008, 0.008])


def lum(rgb):
    return rgb @ np.array([0.2126, 0.7152, 0.0722])


def main():
    pos = np.load(os.path.join(OUT, "POS.npy"))
    nrm = np.load(os.path.join(OUT, "NRM.npy"))
    meta = json.load(open(os.path.join(OUT, "POS_meta.json")))
    old = np.asarray(Image.open(os.path.join(OUT, "T_Cat_Coat_Albedo.png")).convert("RGB")).astype(np.float32) / 255.0

    h, w = pos.shape[:2]
    valid = np.abs(pos).sum(axis=2) > 1e-7
    print("tex", (h, w), "valid", round(float(valid.mean()), 3))

    # ---- 底色：保留原有的明暗结构，但压到极低的对比 ----
    l = lum(old)
    l_norm = np.clip((l - np.percentile(l[valid], 5)) /
                     max(1e-6, np.percentile(l[valid], 95) - np.percentile(l[valid], 5)), 0, 1)

    base = COAT[None, None, :] * (0.80 + 0.42 * l_norm[:, :, None])
    # 背脊一点点暖，腹部更冷 —— 让纯黑也有方向感
    back = np.clip(pos[..., 2] / 0.28, 0, 1)[:, :, None]
    base = base * (1 - back * 0.55) + COAT_WARM[None, None, :] * (0.80 + 0.42 * l_norm[:, :, None]) * back * 0.55
    # 口鼻/下巴：原来的奶油色区域提亮成深灰，黑猫的脸才不会糊成一团
    cream = np.clip((l - 0.62) / 0.25, 0, 1)[:, :, None]
    base = base * (1 - cream * 0.75) + MUZZLE[None, None, :] * cream * 0.75

    albedo = np.clip(base, 0, 1)
    emit = np.zeros((h, w, 3), np.float32)

    # ---- 眼睛：在视线法线的局部坐标里画，保持圆形 ----
    for e in meta["eyes"]:
        c = np.array(e["c"], np.float32)
        r = float(e["r"]) * 1.15
        d = np.linalg.norm(pos - c[None, None, :], axis=2)
        disc = (d < r) & valid
        if not disc.any():
            print("  eye empty", c)
            continue

        g = nrm[disc].mean(axis=0)
        g /= max(1e-6, np.linalg.norm(g))
        up = np.array([0.0, 0.0, 1.0], np.float32)
        if abs(float(g @ up)) > 0.9:
            up = np.array([0.0, 1.0, 0.0], np.float32)
        t1 = np.cross(g, up); t1 /= max(1e-6, np.linalg.norm(t1))
        t2 = np.cross(t1, g)

        rel = pos - c[None, None, :]
        du = rel @ t1
        dv = rel @ t2
        rr = np.sqrt(du * du + dv * dv) / r

        eye = disc
        # 瞳孔（猫是竖缝，这里用窄椭圆）
        pupil = eye & (np.sqrt((du / (r * 0.13)) ** 2 + (dv / (r * 0.58)) ** 2) < 1.0)
        iris = eye & (rr < 0.68) & ~pupil
        rim = eye & (rr >= 0.68)

        k = np.clip((rr - 0.08) / 0.60, 0, 1)[:, :, None]
        iris_col = IRIS_IN[None, None, :] * (1 - k) + IRIS_OUT[None, None, :] * k

        albedo[iris] = iris_col[iris]
        albedo[rim] = RIM
        albedo[pupil] = PUPIL

        # 自发光：虹膜亮、外圈稍弱、瞳孔不发光
        kk = k[:, :, 0][iris].reshape(-1, 1)
        emit[iris] = iris_col[iris] * (0.85 - 0.45 * kk)
        emit[rim] = IRIS_OUT * 0.05
        print("  eye at", np.round(c, 4), "r", round(r, 4), "texels", int(eye.sum()),
              "iris", int(iris.sum()), "pupil", int(pupil.sum()))

    a8 = (np.clip(albedo, 0, 1) * 255).astype(np.uint8)
    e8 = (np.clip(emit, 0, 1) * 255).astype(np.uint8)
    Image.fromarray(a8, "RGB").save(os.path.join(OUT, "T_Cat_Black_Albedo.png"))
    Image.fromarray(e8, "RGB").save(os.path.join(OUT, "T_Cat_Black_Emit.png"))

    vis = albedo[valid]
    print("albedo mean", np.round(vis.mean(axis=0) * 255).astype(int),
          "p50", np.round(np.median(vis, axis=0) * 255).astype(int),
          "p99", np.round(np.percentile(vis, 99, axis=0) * 255).astype(int))
    print("emit max", e8.reshape(-1, 3).max(axis=0))
    print("DONE")


main()
