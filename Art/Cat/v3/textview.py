"""把截图压成灰度字符网格 + 区域色块统计，让纯文本模型也能"看一眼"。"""
import sys

import numpy as np
from PIL import Image

RAMP = " .:-=+*#%@"


def ascii_view(path, cols=64, rows=30, crop=None):
    im = Image.open(path).convert("RGB")
    if crop:
        w, h = im.size
        im = im.crop((int(crop[0] * w), int(crop[1] * h), int(crop[2] * w), int(crop[3] * h)))
    small = im.resize((cols, rows), Image.LANCZOS)
    a = np.asarray(small).astype(np.float32) / 255.0
    lum = a @ np.array([0.2126, 0.7152, 0.0722])
    lo, hi = np.percentile(lum, 3), np.percentile(lum, 97)
    n = np.clip((lum - lo) / max(1e-6, hi - lo), 0, 1)
    print("=== %s  (%dx%d, crop=%s)" % (path.split("\\")[-1], im.size[0], im.size[1], crop))
    print("    lum p3=%.2f p50=%.2f p97=%.2f" % (np.percentile(lum, 3), np.percentile(lum, 50), np.percentile(lum, 97)))
    for r in range(rows):
        line = "".join(RAMP[min(len(RAMP) - 1, int(n[r, c] * (len(RAMP) - 1) + 0.5))] for c in range(cols))
        print("   |" + line + "|")
    return a


def regions(path):
    im = Image.open(path).convert("RGB")
    a = np.asarray(im).astype(np.float32) / 255.0
    h, w, _ = a.shape
    print("=== REGIONS %s" % path.split("\\")[-1])
    boxes = {
        "center 30%": (0.35, 0.35, 0.65, 0.65),
        "center 50%": (0.25, 0.25, 0.75, 0.75),
        "top strip": (0.0, 0.0, 1.0, 0.18),
        "bottom strip": (0.0, 0.82, 1.0, 1.0),
        "left strip": (0.0, 0.0, 0.15, 1.0),
        "right strip": (0.85, 0.0, 1.0, 1.0),
    }
    for k, (x0, y0, x1, y1) in boxes.items():
        sub = a[int(y0 * h):int(y1 * h), int(x0 * w):int(x1 * w)]
        m = sub.reshape(-1, 3).mean(axis=0) * 255
        sat = (sub.max(axis=2) - sub.min(axis=2)).mean() * 255
        print("  %-12s rgb=(%3.0f,%3.0f,%3.0f)  sat=%4.1f  lum=%5.1f" %
              (k, m[0], m[1], m[2], sat, (m @ np.array([0.2126, 0.7152, 0.0722]))))


if __name__ == "__main__":
    for p in sys.argv[1:]:
        ascii_view(p)
        regions(p)
        print()
