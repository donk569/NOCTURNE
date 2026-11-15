"""v3 -- paint a Chinese tabby (狸花猫) coat in object space and write the
albedo + roughness maps for the existing UV atlas.

Run with the managed python (numpy + PIL only, no bpy):
    python paint.py

The object-space position map (POS.npy) was baked once through Cycles, so every
texel knows exactly where it sits on the cat.  Every marking below is therefore
a function of real 3D position: stripes wrap the barrel of the body, leg bars
follow the legs, tail rings follow the tail.

Layout of the coat (top to bottom the layers are applied in this order):
    agouti base -> cream underside -> dorsal spine -> mackerel stripes
    -> leg bars -> tail rings -> head markings -> eyes -> nose
"""
import json
import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")

# ------------------------------------------------------------------ palette
BASE_MID = np.array([124, 112, 92], np.float32)
BASE_LIGHT = np.array([160, 148, 126], np.float32)
BASE_DARK = np.array([86, 74, 60], np.float32)
STRIPE = np.array([50, 42, 33], np.float32)
SPINE = np.array([36, 30, 24], np.float32)
CREAM = np.array([226, 219, 204], np.float32)
BELLY = np.array([206, 196, 178], np.float32)
NOSE = np.array([196, 124, 118], np.float32)
INNER_EAR = np.array([198, 154, 150], np.float32)
IRIS_OUT = np.array([104, 88, 34], np.float32)
IRIS_IN = np.array([198, 170, 74], np.float32)
PUPIL = np.array([12, 10, 9], np.float32)
RIM = np.array([30, 24, 19], np.float32)

# ------------------------------------------------------------------ constants
TAIL_Y = 0.116          # torso -> tail
HEAD_Y = -0.196         # torso -> neck
BELT_X = 0.019          # |x| below this counts as the belly column
LEG_Z = 0.118           # below this and outside BELT_X we are on a leg
STRIPE_P = 0.0345       # mackerel stripe spacing
STRIPE_WARP = 0.0060


def sstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def _hash(ix, iy, iz, seed):
    h = (ix * np.int64(374761393) + iy * np.int64(668265263)
         + iz * np.int64(1274126177) + np.int64(seed) * np.int64(2654435761))
    h = (h ^ (h >> np.int64(13))) * np.int64(1274126177)
    h = h ^ (h >> np.int64(16))
    return (h & np.int64(0xFFFFFF)).astype(np.float32) / np.float32(0xFFFFFF)


def vnoise(x, y, z, seed=0):
    ix = np.floor(x)
    iy = np.floor(y)
    iz = np.floor(z)
    fx = (x - ix).astype(np.float32)
    fy = (y - iy).astype(np.float32)
    fz = (z - iz).astype(np.float32)
    ix = ix.astype(np.int64)
    iy = iy.astype(np.int64)
    iz = iz.astype(np.int64)
    ux = fx * fx * (3 - 2 * fx)
    uy = fy * fy * (3 - 2 * fy)
    uz = fz * fz * (3 - 2 * fz)
    c00 = _hash(ix, iy, iz, seed) * (1 - ux) + _hash(ix + 1, iy, iz, seed) * ux
    c10 = _hash(ix, iy + 1, iz, seed) * (1 - ux) + _hash(ix + 1, iy + 1, iz, seed) * ux
    c01 = _hash(ix, iy, iz + 1, seed) * (1 - ux) + _hash(ix + 1, iy, iz + 1, seed) * ux
    c11 = _hash(ix, iy + 1, iz + 1, seed) * (1 - ux) + _hash(ix + 1, iy + 1, iz + 1, seed) * ux
    c0 = c00 * (1 - uy) + c10 * uy
    c1 = c01 * (1 - uy) + c11 * uy
    return c0 * (1 - uz) + c1 * uz


def fbm(x, y, z, oct=3, seed=0):
    v = np.zeros_like(x)
    amp = 1.0
    tot = 0.0
    f = 1.0
    for o in range(oct):
        v += amp * vnoise(x * f, y * f, z * f, seed + o * 17)
        tot += amp
        amp *= 0.5
        f *= 2.03
    return v / tot


def bands(coord, period, phase=0.0):
    """0..1 cosine bands; 1 at the centre of a dark stripe."""
    return 0.5 + 0.5 * np.cos(coord / period * 2 * np.pi + phase)


def main():
    pos = np.load(os.path.join(OUT, "POS.npy"))
    nrm = np.load(os.path.join(OUT, "NRM.npy"))
    meta = json.load(open(os.path.join(OUT, "POS_meta.json")))
    # POS.npy / NRM.npy are already stored top-down (rasterize.py flips on save,
    # because Blender's pixel buffer is bottom-up and PIL/PNG is top-down).
    H, W, _ = pos.shape

    # NOTE: never test pos.sum() -- texels in the middle of the torso sit at
    # (0, 0, 0.12) and a plain sum can vanish. Use the L1 norm instead.
    valid = np.abs(pos).sum(axis=2) > 1e-7
    idx = np.flatnonzero(valid.reshape(-1))
    p = pos.reshape(-1, 3)[idx]
    n = nrm.reshape(-1, 3)[idx]
    x, y, z = p[:, 0], p[:, 1], p[:, 2]

    # ------------------------------------------------ measured body profile
    nb = 320
    ylo, yhi = -0.275, 0.28
    bidx = np.clip(((y - ylo) / (yhi - ylo) * nb).astype(np.int32), 0, nb - 1)
    ztop = np.zeros(nb, np.float32)
    zbot = np.zeros(nb, np.float32)
    for i in range(nb):
        s = bidx == i
        if s.sum() < 4:
            ztop[i] = zbot[i] = np.nan
            continue
        ztop[i] = p[s, 2].max()
        col = np.abs(p[s, 0]) < BELT_X
        zbot[i] = (p[s][col, 2].min() if col.sum() > 3
                   else np.percentile(p[s, 2], 3))
    ok = ~np.isnan(ztop)
    ztop = np.interp(np.arange(nb), np.arange(nb)[ok], ztop[ok])
    zbot = np.interp(np.arange(nb), np.arange(nb)[ok], zbot[ok])

    def profile(arr, yy, smooth=11):
        k = np.ones(smooth, np.float32) / smooth
        pad = np.pad(arr, (smooth // 2, smooth // 2), mode="edge")
        sm = np.convolve(pad, k, mode="valid")[:nb]
        t = np.clip((yy - ylo) / (yhi - ylo) * nb - 0.5, 0, nb - 1.001)
        i0 = t.astype(np.int32)
        f = t - i0
        return sm[i0] * (1 - f) + sm[np.minimum(i0 + 1, nb - 1)] * f

    zt = profile(ztop, y)
    zb = profile(zbot, y)
    hgt = np.maximum(zt - zb, 1e-4)
    zc = (zt + zb) * 0.5
    u = np.clip((z - zb) / hgt, 0.0, 1.4)     # 0 at belly, 1 at spine

    # ------------------------------------------------ noise fields
    # note the anisotropic sampling: lower frequency along y -> brushed hairs
    f_hair = fbm(x * 300, y * 55, z * 300, oct=2, seed=3)
    f_fine = fbm(x * 90, y * 90, z * 90, oct=2, seed=7)
    f_mid = fbm(x * 45, y * 45, z * 45, oct=2, seed=13)
    f_warp = fbm(x * 13, y * 13, z * 13, oct=2, seed=23)
    f_patch = fbm(x * 9, y * 9, z * 9, oct=2, seed=41)

    # ------------------------------------------------ base agouti
    t = np.clip(0.38 + (f_hair - 0.5) * 0.95 + (f_fine - 0.5) * 0.55, 0, 1)
    col = BASE_DARK[None, :] * (1 - t)[:, None] + BASE_LIGHT[None, :] * t[:, None]
    col = col * 0.62 + BASE_MID[None, :] * 0.38

    # ------------------------------------------------ masks
    # torso underside sits at z ~ 0.062; nothing below it except legs and tail
    leg = sstep(0.118, 0.090, z) * sstep(0.023, 0.029, np.abs(x)) * (1 - sstep(0.140, 0.160, y))
    head = sstep(-0.186, -0.206, y)
    torso = sstep(HEAD_Y + 0.006, HEAD_Y + 0.046, y) * (1 - sstep(TAIL_Y - 0.040, TAIL_Y, y))
    tail = sstep(TAIL_Y - 0.006, TAIL_Y + 0.020, y)

    # ------------------------------------------------ cream underside
    belly = sstep(0.150, 0.020, u) * torso * (1 - leg) * sstep(0.072, 0.040, z)
    bib = sstep(0.42, 0.18, u) * sstep(-0.208, -0.186, y) * (1 - sstep(-0.150, -0.118, y))
    belly = np.clip(np.maximum(belly, bib) * (0.82 + 0.18 * f_patch), 0, 1)
    col = col * (1 - belly[:, None]) + BELLY[None, :] * belly[:, None]

    # ------------------------------------------------ mackerel stripes
    theta = np.arctan2(z - zc, np.where(np.abs(x) < 1e-5, 1e-5, x))
    s = y + 0.014 * theta + (f_warp - 0.5) * 2 * STRIPE_WARP
    band = bands(s, STRIPE_P)
    lo_e = 0.30 + 0.16 * (f_mid - 0.5)
    hi_e = 0.84 + 0.12 * (f_patch - 0.5)
    stripe = sstep(lo_e, hi_e, band)
    up = sstep(0.16, 0.34, u) * (1 - sstep(0.99, 1.10, u))
    m_stripe = torso * up * (1 - leg) * stripe * (0.55 + 0.45 * f_mid)

    # dorsal spine stripe (drawn on top of the flank stripes)
    spine = (sstep(0.90, 1.00, u) * (1 - sstep(1.04, 1.12, u))
             * (1 - sstep(0.013, 0.026, np.abs(x))) * torso)
    spine = np.maximum(spine, (sstep(0.95, 1.02, u) * (1 - sstep(0.020, 0.034, np.abs(x))))
                       * torso * 0.85)

    # ------------------------------------------------ legs
    lbar = bands(z - 0.006, 0.031, phase=(f_warp - 0.5) * 1.6)
    leg_stripe = sstep(0.30, 0.80, lbar) * leg
    sock = sstep(0.032, 0.014, z) * leg

    # ------------------------------------------------ tail
    rbar = bands(y - TAIL_Y, 0.026, phase=(f_warp - 0.5) * 1.0)
    tail_stripe = sstep(0.40, 0.84, rbar) * tail
    tail_tip = sstep(0.238, 0.252, y) * tail

    # ------------------------------------------------ head
    # muzzle + chin
    muz = head * sstep(-0.241, -0.251, y) * (1 - sstep(0.230, 0.242, z)) \
        * (1 - sstep(0.022, 0.030, np.abs(x)))
    muz = np.maximum(muz, head * sstep(-0.248, -0.256, y) * sstep(0.244, 0.230, z)
                     * (1 - sstep(0.019, 0.026, np.abs(x))))
    col = col * (1 - muz[:, None] * 0.95) + CREAM[None, :] * muz[:, None] * 0.95

    # forehead "M": short bars stepping down the forehead
    fh = head * sstep(-0.200, -0.212, y) * (1 - sstep(-0.240, -0.250, y)) \
        * sstep(0.240, 0.252, z) * (1 - sstep(0.292, 0.302, z))
    mx = bands(x + 0.0030 * np.sign(x), 0.0118)
    col = col * (1 - (fh * sstep(0.30, 0.80, mx) * (1 - sstep(0.024, 0.032, np.abs(x))))[:, None] * 0.9)
    col = col + STRIPE[None, :] * (fh * sstep(0.40, 0.86, mx)
                                   * (1 - sstep(0.021, 0.030, np.abs(x))))[:, None] * 0.9

    # cheek bars: bold diagonals running back from the outer eye corner
    cheek = head * sstep(-0.216, -0.230, y) * (1 - sstep(-0.252, -0.262, y)) \
        * sstep(0.024, 0.016, z) * (1 - sstep(0.222, 0.234, z)) * sstep(0.020, 0.030, np.abs(x))
    q = (z - 0.200) * 4.2 + (y + 0.246) * 1.6
    cheek_m = cheek * sstep(0.34, 0.80, bands(q, 0.017))
    col = col * (1 - cheek_m[:, None] * 0.8) + STRIPE[None, :] * cheek_m[:, None] * 0.8

    # dark line down the nose bridge
    bridge = head * (1 - sstep(0.0035, 0.0075, np.abs(x))) * sstep(0.232, 0.242, z) \
        * (1 - sstep(0.268, 0.276, z))
    col = col * (1 - bridge[:, None] * 0.5) + SPINE[None, :] * bridge[:, None] * 0.5

    # ------------------------------------------------ outer ear + inner ear
    ear = head * sstep(0.272, 0.286, z)
    inner = ear * sstep(-0.22, -0.55, n[:, 1]) * sstep(0.62, 0.30, n[:, 2]) \
        * sstep(0.286, 0.298, z)
    col = col * (1 - inner[:, None] * 0.9) + INNER_EAR[None, :] * inner[:, None] * 0.9

    # ------------------------------------------------ eyes
    # The eye sits in a carved socket, so a plain 3D distance turns the iris into
    # a spiral.  Build a local frame from the gaze normal (mean surface normal of
    # the iris patch) and paint in that plane: the iris stays round no matter how
    # deep the socket is.
    rough = np.full(len(p), 0.62, np.float32) * (0.93 + 0.14 * f_fine)
    eyes = meta["eyes"]
    for e in eyes:
        c = np.array(e["c"], np.float32)
        r = float(e["r"])
        d = np.linalg.norm(p - c[None, :], axis=1)
        near = d < r * 2.4
        g = n[near].mean(axis=0) if near.sum() > 20 else np.array([0, -1, 0], np.float32)
        ln = np.linalg.norm(g)
        g = g / ln if ln > 1e-6 else np.array([0, -1, 0], np.float32)
        if g[1] > 0:                       # always point out of the face
            g = -g
        up = np.array([0, 0, 1], np.float32)
        t1 = np.cross(g, up)
        t1 = t1 / (np.linalg.norm(t1) + 1e-9)
        t2 = np.cross(t1, g)
        t2 = t2 / (np.linalg.norm(t2) + 1e-9)
        rel = p - c[None, :]
        du = rel @ t1
        dv = rel @ t2
        rr = np.sqrt(du * du + dv * dv)
        q = rr / r

        rim = sstep(1.00, 1.10, q) * (1 - sstep(1.20, 1.46, q)) * (d < r * 2.0)
        col = col * (1 - rim[:, None] * 0.85) + RIM[None, :] * rim[:, None] * 0.85

        inside = (q < 1.03) & (d < r * 1.7)
        iris = IRIS_IN[None, :] * (1 - sstep(0.10, 0.98, np.clip(q, 0, 1.05)))[:, None] \
            + IRIS_OUT[None, :] * sstep(0.10, 0.98, np.clip(q, 0, 1.05))[:, None]
        ang = np.arctan2(dv, du)
        fib = 0.90 + 0.10 * np.cos(ang * 38 + f_fine * 9)
        iris = iris * fib[:, None]
        limb = sstep(0.86, 1.02, np.clip(q, 0, 1.05))
        iris = iris * (1 - limb[:, None] * 0.55)
        col = np.where(inside[:, None], iris, col)
        # vertical slit pupil, slightly tapered
        halfw = 0.00092 + 0.00055 * np.clip(1.0 - np.abs(dv) / (r * 0.9), 0, 1)
        pup = inside & (np.abs(du) < halfw) & (np.abs(dv) < r * 0.88)
        col = np.where(pup[:, None], PUPIL[None, :], col)
        rough = np.where(inside, 0.070, rough)
        hl = ((du + 0.0022) ** 2 + (dv - 0.0027) ** 2) < (0.0014 ** 2)
        col = np.where(hl[:, None], np.array([252, 252, 246], np.float32)[None, :], col)

    # ------------------------------------------------ nose
    nose = head * sstep(-0.2585, -0.2635, y) * (1 - sstep(0.2290, 0.2340, z)) \
        * sstep(0.2145, 0.2205, z) * (1 - sstep(0.0064, 0.0098, np.abs(x)))
    col = col * (1 - nose[:, None]) + NOSE[None, :] * nose[:, None]

    # ------------------------------------------------ apply stripe layers last
    def blend(dst, mask, color, k=1.0, dst_rough=None):
        m = np.clip(mask * k, 0, 1)[:, None]
        return dst * (1 - m) + color[None, :] * m

    col = blend(col, m_stripe, STRIPE, 1.0)
    col = blend(col, leg_stripe * (1 - sock * 0.8), STRIPE, 1.0)
    col = blend(col, tail_stripe, STRIPE, 0.92)
    col = blend(col, tail_tip, SPINE, 1.0)
    col = blend(col, spine, SPINE, 0.92)
    col = blend(col, sock, CREAM, 0.85)

    # ------------------------------------------------ assemble
    if os.environ.get("PAINT_GRAD"):
        # 1:1 mapping probe: smooth gradient head->tail (R) and belly->spine (G).
        # Any UV/POS mapping error shows up as a colour break on the model.
        g = np.zeros((len(p), 3), np.float32)
        g[:, 0] = (y - y.min()) / (y.max() - y.min()) * 255.0
        g[:, 1] = np.clip(u, 0, 1) * 255.0
        g[:, 2] = np.abs(x) / 0.053 * 255.0
        col = g

    if os.environ.get("PAINT_DEBUG"):
        def dump(name, r, g, b):
            im = np.zeros((H * W, 3), np.float32)
            for i, ch in enumerate((r, g, b)):
                if ch is not None:
                    im[idx, i] = np.clip(ch, 0, 255)
            im = im.reshape(H, W, 3).astype(np.uint8)
            Image.fromarray(im, "RGB").save(os.path.join(OUT, "dbg_%s.png" % name))

        dump("u", np.clip(u / 1.3, 0, 1) * 255, None, None)
        dump("cream", belly * 255, muz * 255, leg * 255)
        dump("stripe", m_stripe * 255, spine * 255, sock * 255)
        dump("posz", (z - z.min()) / (z.max() - z.min()) * 255,
             (y - y.min()) / (y.max() - y.min()) * 255, None)
        print("debug dumps written")

    alb = np.zeros((H * W, 3), np.float32)
    alb[idx] = np.clip(col, 0, 255)
    alb = alb.reshape(H, W, 3)
    empty = alb.sum(axis=2) <= 1e-4
    fill = np.median(alb[~empty], axis=0)
    alb[empty] = fill

    rgh = np.zeros((H * W,), np.float32)
    rgh[idx] = np.clip(rough, 0.05, 1.0)
    rgh = rgh.reshape(H, W)
    rgh[empty] = 0.62

    a8 = np.clip(alb, 0, 255).astype(np.uint8)
    Image.fromarray(a8, "RGB").save(os.path.join(OUT, "T_Cat_Coat_Albedo.png"))
    Image.fromarray((np.clip(rgh, 0, 1) * 255).astype(np.uint8), "L").save(
        os.path.join(OUT, "T_Cat_Coat_Rough.png"))
    print("wrote albedo/rough", alb.shape, "coverage", round(float(valid.mean()), 3))
    print("mean albedo", a8[~empty].mean(axis=0).round(1))
    print("eyes", [(round(float(e["r"]), 4)) for e in eyes])


main()
