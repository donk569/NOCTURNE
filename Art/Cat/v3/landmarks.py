"""Measure every landmark the skeleton needs, straight from the rasterised
position map (no eyeballing proportions)."""
import json
import os

import numpy as np

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
pos = np.load(os.path.join(OUT, "POS.npy"))
valid = np.abs(pos).sum(axis=2) > 1e-7
p = pos[valid]
x, y, z = p[:, 0], p[:, 1], p[:, 2]
res = {}

# ---------------------------------------------------------------- body spine
BODY = (-0.20, 0.145)
rows = []
for yy in np.arange(BODY[0], BODY[1], 0.01):
    s = (y >= yy) & (y < yy + 0.01) & (np.abs(x) < 0.012)   # centre slice
    if s.sum() < 20:
        continue
    rows.append((yy + 0.005, float(z[s].max()), float(z[s].min())))
print("y      top    belly")
for r in rows:
    print("%6.3f %6.3f %6.3f" % r)
res["spine_rows"] = rows

# ------------------------------------------------------------------- legs
low = p[z < 0.10]
for sx in (1, -1):
    side = low[low[:, 0] * sx > 0]
    for yy0, yy1, nm in ((-0.16, -0.01, "front"), (0.01, 0.135, "hind")):
        seg = side[(side[:, 1] > yy0) & (side[:, 1] < yy1)]
        if len(seg) < 50:
            continue
        # paw print = the lowest 15mm
        paw = seg[seg[:, 2] < 0.016]
        c = paw.mean(axis=0) if len(paw) else seg.mean(axis=0)
        print("LEG %s side %+d  paw=(%+.4f,%+.4f,%.4f)  n=%d  x[%.3f,%.3f] y[%.3f,%.3f]"
              % (nm, sx, c[0], c[1], c[2], len(paw),
                 seg[:, 0].min(), seg[:, 0].max(), seg[:, 1].min(), seg[:, 1].max()))
        res["leg_%s_%s" % (nm, "L" if sx > 0 else "R")] = [float(v) for v in c]

# ------------------------------------------------------------------- head
hd = p[y < -0.205]
print("HEAD bbox", np.round(hd.min(axis=0), 4), np.round(hd.max(axis=0), 4))
res["head"] = [float(v) for v in hd.mean(axis=0)]
# ears: everything above z 0.285 in the head region
for sx in (1, -1):
    e = hd[(hd[:, 0] * sx > 0.004) & (hd[:, 2] > 0.288)]
    if len(e) < 20:
        continue
    print("EAR side %+d n=%d base=(%+.4f,%+.4f,%.4f) tip=(%+.4f,%+.4f,%.4f)"
          % (sx, len(e),
             e[:, 0].min() * sx, e[:, 1].mean(), e[:, 2].min(),
             e[:, 0].max() * sx, e[:, 1].mean(), e[:, 2].max()))
    res["ear_%s" % ("L" if sx > 0 else "R")] = [
        float(e[:, 0].min() * sx), float(e[:, 1].mean()), float(e[:, 2].min()),
        float(e[:, 0].max() * sx), float(e[:, 1].mean()), float(e[:, 2].max())]

# ------------------------------------------------------------------- tail
tl = p[y > 0.135]
print("TAIL n=%d" % len(tl))
print("y      zmid   xmid  r")
for yy in np.arange(0.135, 0.28, 0.02):
    s = tl[(tl[:, 1] >= yy) & (tl[:, 1] < yy + 0.02)]
    if len(s) < 20:
        continue
    r = (s[:, 2].max() - s[:, 2].min()) * 0.5
    print("%6.3f %6.4f %+.4f %.4f" % (yy + 0.01, s[:, 2].mean(), s[:, 0].mean(), r))

with open(os.path.join(OUT, "landmarks.json"), "w") as f:
    json.dump(res, f, indent=1)
print("saved landmarks.json")
