"""Print the body cross-section profile so the coat constants are measured,
not guessed."""
import json
import os

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")

pos = np.load(os.path.join(OUT, "POS.npy"))
meta = json.load(open(os.path.join(OUT, "POS_meta.json")))
valid = pos.sum(axis=2) > 1e-6
p = pos[valid]
print("valid texels", p.shape[0], "of", pos.shape[0] * pos.shape[1])
print("bbox", p.min(axis=0).round(4), p.max(axis=0).round(4))

y0, y1 = p[:, 1].min(), p[:, 1].max()
N = 22
print("%6s %7s %7s %7s %7s %7s %8s" % ("y", "zmin", "zmax", "zmid", "xmax", "xmin", "count"))
for i in range(N):
    a = y0 + (y1 - y0) * i / N
    b = y0 + (y1 - y0) * (i + 1) / N
    sel = p[(p[:, 1] >= a) & (p[:, 1] < b)]
    if len(sel) < 20:
        print("%6.3f  (empty)" % a)
        continue
    print("%6.3f %7.4f %7.4f %7.4f %7.4f %7.4f %8d"
          % (a, sel[:, 2].min(), sel[:, 2].max(),
             (sel[:, 2].min() + sel[:, 2].max()) * 0.5,
             sel[:, 0].max(), sel[:, 0].min(), len(sel)))

for e in meta["eyes"]:
    print("eye", [round(v, 4) for v in e["c"]], "r", round(e["r"], 4))
