"""Paint the tabby coat, eye iris and fur normal map by rasterising UV -> 3D.

Because every texel knows its 3D position, the markings are defined in object
space (stripes perpendicular to the spine, rings on the tail and legs, the M on
the forehead) instead of in a hand-drawn atlas.  That also means UV seams are
invisible: two texels that map to the same 3D point get the same colour.
"""
import math
import os
import sys

import bpy
import numpy as np

try:
    HERE = SCRIPTS_DIR
except NameError:
    HERE = os.path.dirname(os.path.abspath(__file__))

try:
    P = PARAMS
except NameError:
    P = {}

OUT = os.path.join(HERE, "out")
os.makedirs(OUT, exist_ok=True)

RES = int(P.get("res", 2048))
EYE_RES = 512

# ---------------------------------------------------------------- colour space
def srgb(*c):
    return np.array(c, dtype=np.float32)


C_AGOUTI = srgb(0.455, 0.408, 0.345)      # warm grey-brown base
C_AGOUTI_L = srgb(0.560, 0.512, 0.442)    # sun-bleached tips
C_STRIPE = srgb(0.088, 0.082, 0.076)      # near-black tabby stripe
C_WHITE = srgb(0.905, 0.890, 0.862)       # belly / bib / socks
C_CREAM = srgb(0.800, 0.760, 0.690)

HC = np.array([0.0, -0.1475, 0.2555], dtype=np.float32)
SOCKET = {1: np.array([0.0258, -0.1745, 0.2570], dtype=np.float32),
          -1: np.array([-0.0258, -0.1745, 0.2570], dtype=np.float32)}
EYE_AXIS = {1: np.array([0.500, -0.855, 0.135], dtype=np.float32),
            -1: np.array([-0.500, -0.855, 0.135], dtype=np.float32)}
for _k in EYE_AXIS:
    EYE_AXIS[_k] = EYE_AXIS[_k] / np.linalg.norm(EYE_AXIS[_k])


# ---------------------------------------------------------------- noise
def _hash3(ix, iy, iz, seed):
    v = np.sin(ix * 12.9898 + iy * 78.233 + iz * 37.719 + seed * 3.137) * 43758.5453
    return v - np.floor(v)


def vnoise(p, freq, seed=0.0):
    q = p * freq
    i = np.floor(q)
    f = q - i
    f = f * f * (3.0 - 2.0 * f)
    ix, iy, iz = i[:, 0], i[:, 1], i[:, 2]
    c = []
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                c.append(_hash3(ix + dx, iy + dy, iz + dz, seed))
    wx, wy, wz = f[:, 0], f[:, 1], f[:, 2]
    def mix(a, b, t):
        return a + (b - a) * t
    x00 = mix(c[0], c[1], wx)
    x01 = mix(c[2], c[3], wx)
    x10 = mix(c[4], c[5], wx)
    x11 = mix(c[6], c[7], wx)
    y0 = mix(x00, x01, wy)
    y1 = mix(x10, x11, wy)
    return mix(y0, y1, wz)


def fbm(p, freq, octaves=3, seed=0.0):
    tot = np.zeros(len(p), dtype=np.float32)
    amp = 1.0
    norm = 0.0
    for o in range(octaves):
        tot += amp * vnoise(p, freq * (2.0 ** o), seed + o * 7.3)
        norm += amp
        amp *= 0.5
    return tot / norm


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def band(v, period, sharp=0.62):
    """Recurrent dark band 0..1 (1 = full stripe)."""
    ph = np.mod(v / period, 1.0)
    d = np.minimum(ph, 1.0 - ph) * 2.0          # 0 at centre of band, 1 between
    return 1.0 - smoothstep(sharp * 0.55, sharp, d)


# ---------------------------------------------------------------- rasteriser
def rasterise(ob, res, loop_pos, loop_normal, loop_uv):
    pos = np.zeros((res, res, 3), dtype=np.float32)
    nrm = np.zeros((res, res, 3), dtype=np.float32)
    mask = np.zeros((res, res), dtype=bool)
    me = ob.data
    me.calc_loop_triangles()
    tri = me.loop_triangles
    lo = np.empty((len(tri), 3), dtype=np.int32)
    tri.foreach_get("loops", lo.ravel())
    uvs = loop_uv[lo]                    # (T,3,2)
    ps = loop_pos[lo]
    ns = loop_normal[lo]
    for t in range(len(tri)):
        uv = uvs[t]
        u0 = uv[:, 0] * (res - 1)
        v0 = uv[:, 1] * (res - 1)
        xmin, xmax = int(np.floor(u0.min())), int(np.ceil(u0.max()))
        ymin, ymax = int(np.floor(v0.min())), int(np.ceil(v0.max()))
        xmin = max(xmin, 0); ymin = max(ymin, 0)
        xmax = min(xmax, res - 1); ymax = min(ymax, res - 1)
        if xmax < xmin or ymax < ymin:
            continue
        gx, gy = np.meshgrid(np.arange(xmin, xmax + 1), np.arange(ymin, ymax + 1))
        px = gx.astype(np.float32) + 0.5
        py = gy.astype(np.float32) + 0.5
        d = (v0[1] - v0[2]) * (u0[0] - u0[2]) + (u0[2] - u0[1]) * (v0[0] - v0[2])
        if abs(d) < 1e-12:
            continue
        l0 = ((v0[1] - v0[2]) * (px - u0[2]) + (u0[2] - u0[1]) * (py - v0[2])) / d
        l1 = ((v0[2] - v0[0]) * (px - u0[2]) + (u0[0] - u0[2]) * (py - v0[2])) / d
        l2 = 1.0 - l0 - l1
        inside = (l0 >= -1e-4) & (l1 >= -1e-4) & (l2 >= -1e-4)
        if not inside.any():
            continue
        p3 = (l0[..., None] * ps[t][0] + l1[..., None] * ps[t][1] + l2[..., None] * ps[t][2])
        n3 = (l0[..., None] * ns[t][0] + l1[..., None] * ns[t][1] + l2[..., None] * ns[t][2])
        sub = (slice(ymin, ymax + 1), slice(xmin, xmax + 1))
        m = mask[sub]
        pos[sub][inside] = p3[inside]
        nrm[sub][inside] = n3[inside]
        m[inside] = True
        mask[sub] = m
    return pos, nrm, mask


def get_loops(ob):
    me = ob.data
    n = len(me.loops)
    pos = np.empty(n * 3, dtype=np.float32)
    nrm = np.empty(n * 3, dtype=np.float32)
    me.vertices.foreach_get("co", np.empty(len(me.vertices) * 3, dtype=np.float32))
    vidx = np.empty(n, dtype=np.int32)
    me.loops.foreach_get("vertex_index", vidx)
    vco = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", vco)
    vco = vco.reshape(-1, 3)
    vn = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("normal", vn)
    vn = vn.reshape(-1, 3)
    pos = vco[vidx]
    nrm = vn[vidx]
    uvl = me.uv_layers.active.data
    uv = np.empty(n * 2, dtype=np.float32)
    uvl.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    return pos, nrm, uv


# ---------------------------------------------------------------- coat
def coat_albedo(p, n):
    """p: (N,3) object-space positions, n: (N,3) normals -> (N,3) sRGB."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    N = len(p)
    warp = (fbm(p, 48.0, 3, 1.0) - 0.5) * 1.6 + (fbm(p, 11.0, 2, 5.0) - 0.5) * 3.0

    # ---------------------------------------------------------------- regions
    # torso: rump -> neck, above the belly line
    torso = (smoothstep(0.118, 0.138, z)
             * smoothstep(0.168, 0.146, y)
             * smoothstep(-0.152, -0.128, y))
    torso = np.clip(torso, 0.0, 1.0)

    # ---- torso: mackerel stripes run round the barrel (bands along y)
    s_body = band(y + 0.011 * warp, 0.0235, 0.70)
    s_body *= smoothstep(0.158, 0.192, z)          # fade under the barrel
    # longitudinal spine lines
    lateral = np.abs(x) / 0.055
    dorsal = smoothstep(0.42, 0.05, lateral) * smoothstep(0.222, 0.248, z)
    s_body = np.maximum(s_body, dorsal * 0.92)

    # ---- legs: rings fading into white socks
    leg = np.clip(smoothstep(0.140, 0.122, z), 0.0, 1.0)
    sock = smoothstep(0.058, 0.030, z)
    s_leg = band(z - 0.004 * warp, 0.0170, 0.72) * (1.0 - sock)

    # ---- tail: rings + dark tip
    tail = smoothstep(0.150, 0.168, y)
    s_tail = band(y + 0.006 * warp, 0.0265, 0.66)
    s_tail = np.maximum(s_tail, smoothstep(0.350, 0.372, y))

    # ---- head
    d = p - HC
    rr = np.linalg.norm(d, axis=1) + 1e-6
    az = np.arctan2(d[:, 0], -d[:, 1])
    el = np.arcsin(np.clip(d[:, 2] / rr, -1.0, 1.0))
    head = smoothstep(0.104, 0.082, np.linalg.norm((p - HC) * np.array([1.0, 0.92, 1.08]), axis=1))

    # forehead "M": vertical bars running up between the eyes
    fh = band(az * 0.185 + 0.06 * warp, 0.34, 0.58)
    fh *= smoothstep(0.10, 0.26, el) * smoothstep(0.85, 0.45, np.abs(az))
    # cheek bars
    ch = band(el * 0.16 + 0.05 * warp, 0.34, 0.62)
    ch *= smoothstep(0.55, 0.85, np.abs(az)) * smoothstep(0.34, 0.06, el)
    # dark stripe from the eye corner toward the ear
    ear_stripe = smoothstep(0.030, 0.014, np.abs(np.abs(az) - 0.82)) * smoothstep(-0.05, 0.30, el)
    s_head = np.clip(np.maximum(np.maximum(fh, ch), ear_stripe * 0.75), 0.0, 1.0)

    stripe = np.maximum(np.maximum(s_body * torso, s_leg * leg),
                        np.maximum(s_tail * tail, s_head * head))
    stripe = np.clip(stripe, 0.0, 1.0)

    # ---------------------------------------------------------------- base
    fleck = fbm(p, 300.0, 2, 11.0)
    grain = fbm(p, 90.0, 2, 3.0)
    base = C_AGOUTI[None, :] * (0.80 + 0.42 * fleck[:, None])
    base = base * (0.70 + 0.30 * grain[:, None])
    base = base * 0.62 + C_AGOUTI_L[None, :] * 0.38

    col = base * (1.0 - stripe[:, None] * 0.95) + C_STRIPE[None, :] * (stripe[:, None] * 0.95)

    # ---------------------------------------------------------------- white
    white = np.zeros(N, dtype=np.float32)
    # belly / lower chest
    belly = smoothstep(0.178, 0.140, z) * smoothstep(0.062, 0.022, np.abs(x))
    belly *= smoothstep(0.160, 0.120, y) * smoothstep(-0.150, -0.110, y)
    white = np.maximum(white, belly * 0.9)
    # chest bib under the chin
    bib = smoothstep(-0.108, -0.088, -y) * smoothstep(0.240, 0.180, z)
    bib *= smoothstep(0.050, 0.018, np.abs(x))
    white = np.maximum(white, bib * 0.92)
    # socks
    white = np.maximum(white, smoothstep(0.055, 0.028, z) * 0.96)
    # muzzle + chin + around the mouth
    muz = np.sqrt((d[:, 0] * 1.85) ** 2 + (d[:, 1] + 0.041) ** 2
                  + ((d[:, 2] + 0.027) * 1.30) ** 2)
    white = np.maximum(white, smoothstep(0.036, 0.018, muz) * head)
    # throat
    white = np.maximum(white, smoothstep(-0.135, -0.155, y) * smoothstep(0.020, 0.045, np.abs(x)) * 0.0)
    white = np.clip(white * (1.0 - s_head * 0.92), 0.0, 1.0)
    col = col * (1.0 - white[:, None]) + C_WHITE[None, :] * white[:, None]

    # ---------------------------------------------------------------- inner ear
    for sx in (1, -1):
        base_e = np.array([sx * 0.0282, -0.1285, 0.2825], dtype=np.float32)
        axis = np.array([sx * 0.340, -0.220, 0.915], dtype=np.float32)
        axis = axis / np.linalg.norm(axis)
        side = np.cross(np.array([0.0, -1.0, 0.0], dtype=np.float32), axis)
        side = side / np.linalg.norm(side) * sx
        fwd2 = np.cross(axis, side)
        rel = p - base_e
        t = rel @ axis
        radial = rel - t[:, None] * axis[None, :]
        rd = np.linalg.norm(radial, axis=1) + 1e-9
        front = (radial / rd[:, None]) @ fwd2
        inside = smoothstep(0.054, 0.042, t) * smoothstep(-0.002, 0.008, t)
        inside *= smoothstep(0.031, 0.021, rd) * smoothstep(-0.05, -0.40, front)
        col = col * (1.0 - inside[:, None]) + np.array([0.68, 0.44, 0.42])[None, :] * inside[:, None]

    # ---------------------------------------------------------------- eyeliner
    for sx in (1, -1):
        dd = np.linalg.norm(p - SOCKET[sx], axis=1)
        ring = smoothstep(0.0212, 0.0150, dd) * smoothstep(0.0116, 0.0144, dd)
        col = col * (1.0 - ring[:, None] * 0.92) + C_STRIPE[None, :] * (ring[:, None] * 0.92)

    return np.clip(col, 0.0, 1.0)


def coat_roughness(p, n):
    base = 0.80 + 0.10 * (fbm(p, 260.0, 2, 21.0) - 0.5)
    return np.clip(base, 0.55, 0.95)


def fur_normal_map(res, strength=1.0):
    """Fine fur grain baked in UV space (kills the plastic look)."""
    yy, xx = np.mgrid[0:res, 0:res].astype(np.float32) / res
    h = np.zeros((res, res), dtype=np.float32)
    for f, amp in ((90.0, 1.0), (190.0, 0.42), (400.0, 0.18)):
        pts = np.stack([xx.ravel() * f, yy.ravel() * f * 0.62,
                        np.zeros(res * res, dtype=np.float32)], axis=1)
        h += amp * vnoise(pts, 1.0, f).reshape(res, res)
    gy, gx = np.gradient(h)
    S = 0.0060 * strength
    nx = -gx * S
    ny = -gy * S * 1.8
    nz = np.ones_like(nx)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    out = np.stack([nx / ln, ny / ln, nz / ln], axis=-1)
    return (out * 0.5 + 0.5).astype(np.float32)


# ---------------------------------------------------------------- eyeliner
    for sx in (1, -1):
        dd = np.linalg.norm(p - SOCKET[sx], axis=1)
        ring = smoothstep(0.0212, 0.0150, dd) * smoothstep(0.0116, 0.0144, dd)
        col = col * (1.0 - ring[:, None] * 0.92) + C_STRIPE[None, :] * (ring[:, None] * 0.92)

    return np.clip(col, 0.0, 1.0)


def coat_roughness(p, n):
    base = 0.80 + 0.10 * (fbm(p, 260.0, 2, 21.0) - 0.5)
    return np.clip(base, 0.55, 0.95)


def fur_normal_map(res, strength=1.0):
    """Fine fur grain baked in UV space (kills the plastic look)."""
    rng = np.random.default_rng(7)
    yy, xx = np.mgrid[0:res, 0:res].astype(np.float32) / res
    h = np.zeros((res, res), dtype=np.float32)
    for freq, amp in ((60.0, 1.0), (130.0, 0.55), (260.0, 0.3)):
        n = vnoise(np.stack([xx.ravel() * freq, yy.ravel() * freq * 0.55,
                             np.zeros(res * res, dtype=np.float32)], axis=1),
                   freq * 0.02, freq)
        h += amp * n.reshape(res, res)
    # stretch along v so the grain reads as hair
    gy, gx = np.gradient(h)
    nx = -gx * 12.0 * strength
    ny = -gy * 26.0 * strength
    nz = np.ones_like(nx)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    out = np.stack([nx / ln, ny / ln, nz / ln], axis=-1)
    return (out * 0.5 + 0.5).astype(np.float32)


# ---------------------------------------------------------------- eye
def eye_texture(res):
    """Analytic iris in the eye's planar UV frame: centre = +v of the pupil axis."""
    yy, xx = np.mgrid[0:res, 0:res].astype(np.float32)
    lx = (xx / (res - 1.0) - 0.5) * 2.0
    ly = (yy / (res - 1.0) - 0.5) * 2.0
    r = np.sqrt(lx * lx + ly * ly)
    ang = np.degrees(np.arcsin(np.clip(r, 0.0, 1.0)))

    sclera = srgb(0.070, 0.058, 0.050)
    col = np.tile(sclera, (res, res, 1))

    # iris disc
    iris = smoothstep(46.0, 41.0, ang)
    ang_around = np.arctan2(ly, lx)
    streak = 0.5 + 0.5 * np.sin(ang_around * 23.0 + 2.2 * np.sin(ang_around * 6.0))
    streak2 = 0.5 + 0.5 * np.sin(ang_around * 51.0 + 1.1)
    iris_col = (np.array([0.66, 0.47, 0.13], dtype=np.float32)[None, None, :]
                * (0.55 + 0.50 * (0.7 * streak + 0.3 * streak2))[..., None])
    col = col * (1.0 - iris[..., None]) + iris_col * iris[..., None]

    # darker limbal ring
    limbal = smoothstep(38.0, 43.5, ang) * smoothstep(48.0, 43.5, ang)
    col = col * (1.0 - limbal[..., None] * 0.85) + np.array([0.10, 0.055, 0.015])[None, None, :] * (limbal[..., None] * 0.85)

    # vertical slit pupil (cat)
    pup = (smoothstep(0.115, 0.075, np.abs(lx))
           * smoothstep(0.62, 0.48, np.abs(ly))
           * smoothstep(40.0, 33.0, ang))
    col = col * (1.0 - pup[..., None]) + np.array([0.010, 0.009, 0.009])[None, None, :] * pup[..., None]

    # soft shadow where the lid meets the ball
    col = col * (1.0 - 0.35 * smoothstep(0.62, 1.0, r)[..., None])
    inside = r <= 1.0
    return np.clip(col, 0.0, 1.0), inside


# ---------------------------------------------------------------- bpy helpers
def make_image(name, arr, path, non_color=False):
    h, w = arr.shape[:2]
    if name in bpy.data.images:
        bpy.data.images.remove(bpy.data.images[name])
    img = bpy.data.images.new(name, width=w, height=h, alpha=False,
                              float_buffer=False)
    if arr.ndim == 2:
        rgba = np.ones((h, w, 4), dtype=np.float32)
        rgba[..., 0] = arr
        rgba[..., 1] = arr
        rgba[..., 2] = arr
    else:
        rgba = np.ones((h, w, 4), dtype=np.float32)
        rgba[..., :3] = arr
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    if non_color:
        img.colorspace_settings.name = "Non-Color"
    return img


def dilate(arr, mask, iters=6, bg=None):
    """Grow painted texels outward so mip-mapping never samples the empty gutter."""
    out = arr.copy()
    m = mask.copy()
    if bg is not None:
        out[~m] = bg
    for _ in range(iters):
        if m.all():
            break
        for shift in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            src_m = np.roll(m, shift, axis=(0, 1))
            src_v = np.roll(out, shift, axis=(0, 1))
            fill = (~m) & src_m
            out[fill] = src_v[fill]
            m = m | fill
    return out


def main():
    fur = bpy.data.objects["CatFur"]
    bpy.context.view_layer.objects.active = fur
    # rename the UV channel so Unity picks it up predictably
    if fur.data.uv_layers.active:
        fur.data.uv_layers.active.name = "UVMap"

    pos, nrm, uv = get_loops(fur)
    P3, N3, mask = rasterise(fur, RES, pos, nrm, uv)
    print("COVERAGE %.1f%%" % (100.0 * mask.mean()))

    flat = P3[mask]
    flatn = N3[mask]
    alb = np.zeros((RES, RES, 3), dtype=np.float32)
    alb[mask] = coat_albedo(flat, flatn)
    alb = dilate(alb, mask, 22, bg=np.array([0.36, 0.32, 0.27], dtype=np.float32))

    rough = np.zeros((RES, RES, 3), dtype=np.float32)
    rough[mask] = coat_roughness(flat, flatn)[:, None].repeat(3, axis=1)
    rough = dilate(rough, mask, 22, bg=np.array([0.80, 0.80, 0.80], dtype=np.float32))

    alb_img = make_image("T_Cat_Fur_Albedo", alb, os.path.join(OUT, "T_Cat_Fur_Albedo.png"))
    rgh_img = make_image("T_Cat_Fur_Roughness", rough, os.path.join(OUT, "T_Cat_Fur_Roughness.png"),
                         non_color=True)
    nmap = fur_normal_map(RES)
    nrm_img = make_image("T_Cat_Fur_Normal", nmap, os.path.join(OUT, "T_Cat_Fur_Normal.png"),
                         non_color=True)

    # ---- eye texture (one image, both eyes share it)
    earr, einside = eye_texture(EYE_RES)
    earr = dilate(earr, einside, 6)
    make_image("T_Cat_Eye", earr, os.path.join(OUT, "T_Cat_Eye.png"))

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "cat_tex.blend"))
    print("SAVED tex")


main()
