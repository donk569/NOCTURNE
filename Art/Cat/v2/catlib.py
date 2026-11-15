"""Procedural cat geometry helpers (pure python + mathutils, no bmesh needed).

Conventions (Blender):
  +X = cat's LEFT,  -Y = FORWARD (cat faces -Y),  +Z = UP
  Paws sit on z = 0.  Units = meters.
"""
import math

from mathutils import Vector, Matrix


# --------------------------------------------------------------------- small math
def lerp(a, b, t):
    return a + (b - a) * t


def smoothstep(e0, e1, x):
    if e1 == e0:
        return 0.0 if x < e0 else 1.0
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3.0 - 2.0 * t)


def smin(a, b, k):
    """Polynomial smooth minimum (k = blend radius)."""
    if k <= 0.0:
        return a if a < b else b
    h = max(0.0, min(1.0, 0.5 + 0.5 * (b - a) / k))
    return lerp(b, a, h) - k * h * (1.0 - h)


def smax(a, b, k):
    """Polynomial smooth maximum (used for SDF subtraction / creases)."""
    return -smin(-a, -b, k)


def superellipse(theta, n):
    """Unit superellipse sample; n=2 -> circle, larger -> boxier."""
    c, s = math.cos(theta), math.sin(theta)
    e = 2.0 / n
    cx = math.copysign(abs(c) ** e, c)
    sy = math.copysign(abs(s) ** e, s)
    return cx, sy


# --------------------------------------------------------------------- frames
def frame_from_tangent(tan, hint_up=Vector((0.0, 0.0, 1.0))):
    t = tan.normalized()
    up = hint_up
    if abs(t.dot(up)) > 0.97:
        up = Vector((0.0, 1.0, 0.0))
    side = t.cross(up).normalized()      # points to +X for +Y tangent
    up2 = side.cross(t).normalized()
    return side, up2


def resample_path(points, samples):
    """Catmull-Rom resample of a polyline (list of Vector) into `samples` points."""
    pts = [Vector(p) for p in points]
    if len(pts) < 2:
        return pts
    ext = [pts[0] - (pts[1] - pts[0])] + pts + [pts[-1] + (pts[-1] - pts[-2])]
    segs = len(pts) - 1
    out = []
    for i in range(samples):
        u = i / (samples - 1.0) * segs
        k = min(int(u), segs - 1)
        f = u - k
        p0, p1, p2, p3 = ext[k], ext[k + 1], ext[k + 2], ext[k + 3]
        f2 = f * f
        f3 = f2 * f
        out.append(0.5 * ((2 * p1) + (-p0 + p2) * f +
                          (2 * p0 - 5 * p1 + 4 * p2 - p3) * f2 +
                          (-p0 + 3 * p1 - 3 * p2 + p3) * f3))
    return out


def tangents_of(path, closed=False):
    n = len(path)
    out = []
    for i in range(n):
        if i == 0:
            t = path[1] - path[0]
        elif i == n - 1:
            t = path[-1] - path[-2]
        else:
            t = path[i + 1] - path[i - 1]
        out.append(t.normalized())
    return out


# --------------------------------------------------------------------- loft
def loft_rings(sections, seg=24, expo=2.2, cap_start=True, cap_end=True):
    """Build a tube through `sections`.

    section = dict(center=Vector, hw=float, ht=float, hb=float, expo=optional)
      hw = half width (x), ht = half height above center, hb = half height below.
    theta starts at the TOP of the ring and goes down the +X side:
      u = theta / 2pi, u=0 top, u=0.5 belly.
    Returns (verts, faces, uvs, axis_params) with axis_params = v along the tube.
    """
    n = len(sections)
    centers = [s["center"] for s in sections]
    tans = tangents_of(centers)

    rings = []
    for i, s in enumerate(sections):
        side, up = frame_from_tangent(tans[i])
        c = s["center"]
        hw, ht, hb = s["hw"], s["ht"], s["hb"]
        ex = s.get("expo", expo)
        ring = []
        for j in range(seg):
            th = 2.0 * math.pi * j / seg
            cx, sy = superellipse(th, ex)
            zz = sy * (ht if sy >= 0 else hb)
            ring.append(c + side * (cx * hw) + up * zz)
        rings.append(ring)

    verts = []
    uvs = []
    vparam = []
    for i in range(n):
        for j in range(seg):
            verts.append(rings[i][j])
            uvs.append((j / seg, i / (n - 1.0)))
        vparam.append(i / (n - 1.0))

    faces = []
    for i in range(n - 1):
        for j in range(seg):
            a = i * seg + j
            b = i * seg + (j + 1) % seg
            c2 = (i + 1) * seg + (j + 1) % seg
            d = (i + 1) * seg + j
            faces.append((a, b, c2, d))

    def add_cap(ring_idx, direction, v0):
        """Rounded hemispherical cap at one end."""
        s = sections[ring_idx]
        t = tans[ring_idx] * direction
        side, up = frame_from_tangent(tans[ring_idx])
        base_c = s["center"]
        rr = max(s["hw"], s["ht"], s["hb"])
        steps = 4
        prev = ring_idx * seg
        for k in range(1, steps + 1):
            ang = (math.pi / 2.0) * k / steps
            rr_k = math.cos(ang)
            off = math.sin(ang) * rr * 0.75
            idx0 = len(verts)
            for j in range(seg):
                th = 2.0 * math.pi * j / seg
                cx, sy = superellipse(th, s.get("expo", expo))
                zz = sy * ((s["ht"] if sy >= 0 else s["hb"]) * rr_k)
                p = base_c + side * (cx * s["hw"] * rr_k) + up * zz + t * off
                verts.append(p)
                uvs.append((j / seg, v0))
            for j in range(seg):
                if direction > 0:
                    a = prev + j
                    b = prev + (j + 1) % seg
                    c2 = idx0 + (j + 1) % seg
                    d = idx0 + j
                else:
                    a = prev + (j + 1) % seg
                    b = prev + j
                    c2 = idx0 + j
                    d = idx0 + (j + 1) % seg
                faces.append((a, b, c2, d))
            prev = idx0
        # final pole vertex
        pole = base_c + t * (rr * 0.75)
        pi_ = len(verts)
        verts.append(pole)
        uvs.append((0.5, v0))
        for j in range(seg):
            if direction > 0:
                faces.append((prev + j, prev + (j + 1) % seg, pi_))
            else:
                faces.append((prev + (j + 1) % seg, prev + j, pi_))

    if cap_end:
        add_cap(n - 1, +1.0, 1.0)
    if cap_start:
        add_cap(0, -1.0, 0.0)

    return verts, faces, uvs, vparam
