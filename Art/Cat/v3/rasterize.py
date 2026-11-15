"""Rasterise UV -> 3D so every texel of the atlas knows its position and normal.

Pure numpy, no Cycles: a bake that silently skips islands is the one thing we
cannot tolerate here, because unpainted texels show up as flat tan patches on
the muzzle and chest.
"""
import os

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
RES = 2048

V = np.load(os.path.join(OUT, "m_verts.npy"))
N = np.load(os.path.join(OUT, "m_norm.npy"))
T = np.load(os.path.join(OUT, "m_tris.npy"))
UV = np.load(os.path.join(OUT, "m_uv.npy")).astype(np.float64)

pos = np.zeros((RES, RES, 3), np.float32)
nrm = np.zeros((RES, RES, 3), np.float32)
cov = np.zeros((RES, RES), np.uint8)

px = (UV * RES).astype(np.float64)      # texel space, row 0 = bottom (flip later)

n_written = 0
for t in range(len(T)):
    a, b, c = px[t]
    x0 = int(np.floor(min(a[0], b[0], c[0]) - 0.5))
    x1 = int(np.ceil(max(a[0], b[0], c[0]) + 0.5))
    y0 = int(np.floor(min(a[1], b[1], c[1]) - 0.5))
    y1 = int(np.ceil(max(a[1], b[1], c[1]) + 0.5))
    x0 = max(0, x0)
    y0 = max(0, y0)
    x1 = min(RES - 1, x1)
    y1 = min(RES - 1, y1)
    if x1 < x0 or y1 < y0:
        continue
    e1 = b - a
    e2 = c - a
    den = e1[0] * e2[1] - e1[1] * e2[0]
    if abs(den) < 1e-12:
        continue
    xs = np.arange(x0, x1 + 1) + 0.5
    ys = np.arange(y0, y1 + 1) + 0.5
    gx, gy = np.meshgrid(xs, ys)
    vx = gx - a[0]
    vy = gy - a[1]
    w1 = (vx * e2[1] - vy * e2[0]) / den
    w2 = (e1[0] * vy - e1[1] * vx) / den
    w0 = 1.0 - w1 - w2
    m = (w0 >= -0.02) & (w1 >= -0.02) & (w2 >= -0.02)
    if not m.any():
        continue
    i = T[t]
    p = (w0[m, None] * V[i[0]] + w1[m, None] * V[i[1]] + w2[m, None] * V[i[2]])
    nn = (w0[m, None] * N[i[0]] + w1[m, None] * N[i[1]] + w2[m, None] * N[i[2]])
    ln = np.linalg.norm(nn, axis=1, keepdims=True)
    nn = nn / np.maximum(ln, 1e-9)
    yy = gy[m].astype(np.int32)
    xx = gx[m].astype(np.int32)
    pos[yy, xx] = p
    nrm[yy, xx] = nn
    cov[yy, xx] = 1
    n_written += m.sum()

print("covered texels", int(cov.sum()), "of", RES * RES,
      "(", round(float(cov.mean()), 4), ")")
# sanity: any texel without a position -> small holes we can fix later
np.save(os.path.join(OUT, "POS.npy"), pos[::-1])
np.save(os.path.join(OUT, "NRM.npy"), nrm[::-1])

v = pos[cov > 0]
meta = {
    "res": RES,
    "lo": [float(x) for x in v.min(axis=0)],
    "hi": [float(x) for x in v.max(axis=0)],
    "coverage": float(cov.mean()),
}
print("bbox", np.round(v.min(axis=0), 4), np.round(v.max(axis=0), 4))
print("NOTE: eye anchors are kept from POS_meta.json")
import json
old = json.load(open(os.path.join(OUT, "POS_meta.json")))
meta["eyes"] = old.get("eyes", [])
json.dump(meta, open(os.path.join(OUT, "POS_meta.json"), "w"), indent=1)
print("wrote POS.npy / NRM.npy")
