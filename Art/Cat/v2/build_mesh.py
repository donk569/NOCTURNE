"""Build the NOCTURNE tabby cat mesh.

A ~3.5 kg Chinese tabby (狸花猫): nose->tail base 0.355 m, withers 0.268 m,
head 0.105 m, tail 0.24 m.  Faces -Y, +Z up, paws on z = 0.

Parts are generated as clean parametric lofts / SDF head, then fused by
remesh_cat.py into one continuous skin.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

try:
    HERE = SCRIPTS_DIR
except NameError:
    HERE = os.path.dirname(os.path.abspath(__file__))

sys.path.insert(0, HERE)
import importlib

import catlib as L
importlib.reload(L)

OUT = os.path.join(HERE, "out")
os.makedirs(OUT, exist_ok=True)

HC = Vector((0.0, -0.1475, 0.2555))          # head centre
EYE_DIR = {1: Vector((0.500, -0.855, 0.135)).normalized(),
           -1: Vector((-0.500, -0.855, 0.135)).normalized()}
EAR_BASE = {1: Vector((0.0282, -0.1285, 0.2825)),
            -1: Vector((-0.0282, -0.1285, 0.2825))}

R_BODY = (0.000, 0.505, 1.000, 1.000)
R_HEAD = (0.000, 0.000, 0.340, 0.505)
R_TAIL = (0.340, 0.300, 1.000, 0.505)
R_EARL = (0.340, 0.150, 0.460, 0.300)
R_EARR = (0.460, 0.150, 0.580, 0.300)
R_FL = (0.340, 0.000, 0.505, 0.150)
R_FR = (0.505, 0.000, 0.670, 0.150)
R_HL = (0.670, 0.000, 0.835, 0.150)
R_HR = (0.835, 0.000, 1.000, 0.150)


def remap_uv(uvs, rect, pad=0.006):
    u0, v0, u1, v1 = rect
    du, dv = (u1 - u0), (v1 - v0)
    return [((u0 + pad * du) + u * du * (1 - 2 * pad),
             (v0 + pad * dv) + v * dv * (1 - 2 * pad)) for (u, v) in uvs]


# ---------------------------------------------------------------- torso
# (y, top_z, bottom_z, half_width)
BODY = [
    (0.150, 0.244, 0.198, 0.027),
    (0.138, 0.257, 0.174, 0.045),
    (0.117, 0.264, 0.162, 0.054),
    (0.087, 0.267, 0.158, 0.056),
    (0.051, 0.267, 0.155, 0.055),
    (0.017, 0.265, 0.153, 0.053),
    (-0.017, 0.263, 0.150, 0.054),
    (-0.051, 0.265, 0.142, 0.056),
    (-0.079, 0.269, 0.134, 0.055),
    (-0.097, 0.266, 0.139, 0.048),
    (-0.114, 0.274, 0.176, 0.040),
    (-0.130, 0.283, 0.216, 0.032),
]


def build_body(seg=36):
    secs = []
    for (y, top, bot, hw) in BODY:
        zc, hh = 0.5 * (top + bot), 0.5 * (top - bot)
        secs.append({"center": Vector((0.0, y, zc)), "hw": hw, "ht": hh, "hb": hh})
    v, f, uv, _ = L.loft_rings(secs, seg=seg, expo=2.45, cap_start=True, cap_end=True)
    return v, f, remap_uv(uv, R_BODY)


# ---------------------------------------------------------------- head
def head_ellipsoids():
    E = [(HC, Vector((0.0448, 0.0455, 0.0428)))]                                    # skull
    E.append((Vector((0.0, -0.1745, 0.2665)), Vector((0.0335, 0.0180, 0.0125))))    # brow ridge
    E.append((Vector((0.0, -0.1830, 0.2440)), Vector((0.0205, 0.0168, 0.0135))))    # muzzle base
    E.append((Vector((0.0, -0.1785, 0.2540)), Vector((0.0150, 0.0150, 0.0118))))    # nose bridge
    for sx in (-1, 1):
        E.append((Vector((sx * 0.0112, -0.1888, 0.2348)), Vector((0.0112, 0.0108, 0.0098))))
        E.append((Vector((sx * 0.0330, -0.1622, 0.2395)), Vector((0.0172, 0.0202, 0.0186))))
        E.append((Vector((sx * 0.0215, -0.1750, 0.2272)), Vector((0.0118, 0.0160, 0.0122))))
    E.append((Vector((0.0, -0.1835, 0.2285)), Vector((0.0145, 0.0125, 0.0100))))   # chin
    return E


SOCKET_C = {1: Vector((0.0258, -0.1745, 0.2570)), -1: Vector((-0.0258, -0.1745, 0.2570))}


_ELL = None


def head_sdf(p):
    global _ELL
    if _ELL is None:
        _ELL = head_ellipsoids()
    d = None
    for (c, r) in _ELL:
        q = Vector(((p.x - c.x) / r.x, (p.y - c.y) / r.y, (p.z - c.z) / r.z))
        di = (q.length - 1.0) * min(r.x, r.y, r.z)
        d = di if d is None else L.smin(d, di, 0.0092)
    # carve the eye sockets so the eyeball sits in an orbit instead of on a bump
    for sx in (1, -1):
        c = SOCKET_C[sx]
        rr = Vector((0.0146, 0.0146, 0.0128))
        q = Vector(((p.x - c.x) / rr.x, (p.y - c.y) / rr.y, (p.z - c.z) / rr.z))
        di = (q.length - 1.0) * min(rr.x, rr.y, rr.z)
        d = L.smax(d, -di, 0.0055)
    # mouth slit + philtrum groove
    for (c, r, k) in (
            (Vector((0.0, -0.1905, 0.2308)), Vector((0.0132, 0.0092, 0.0016)), 0.0022),
            (Vector((0.0, -0.1945, 0.2378)), Vector((0.0013, 0.0090, 0.0076)), 0.0018)):
        q = Vector(((p.x - c.x) / r.x, (p.y - c.y) / r.y, (p.z - c.z) / r.z))
        di = (q.length - 1.0) * min(r.x, r.y, r.z)
        d = L.smax(d, -di, k)
    return d


def head_ray(d, lo=0.004, hi=0.14, n=160):
    prev = lo
    for i in range(1, n + 1):
        t = lo + (hi - lo) * i / n
        if head_sdf(HC + d * t) > 0.0:
            a, b = prev, t
            for _ in range(30):
                m = 0.5 * (a + b)
                if head_sdf(HC + d * m) > 0.0:
                    b = m
                else:
                    a = m
            return 0.5 * (a + b)
        prev = t
    return hi


def build_head(rows=48, cols=60):
    verts, uvs = [], []
    for i in range(rows + 1):
        th = math.pi * i / rows
        for j in range(cols):
            ph = 2.0 * math.pi * j / cols
            d = Vector((math.sin(th) * math.sin(ph),
                        -math.sin(th) * math.cos(ph),
                        math.cos(th))).normalized()
            verts.append(HC + d * head_ray(d))
            uvs.append((j / cols, i / rows))

    def vid(i, j):
        return i * cols + (j % cols)

    faces = []
    for i in range(rows):
        for j in range(cols):
            if i == 0:
                faces.append((vid(0, j), vid(1, j), vid(1, j + 1)))
            elif i == rows - 1:
                faces.append((vid(rows - 1, j), vid(rows, j), vid(rows - 1, j + 1)))
            else:
                faces.append((vid(i, j), vid(i + 1, j), vid(i + 1, j + 1), vid(i, j + 1)))
    return verts, faces, remap_uv(uvs, R_HEAD)


def eye_centre(sx, radius=0.0126, expose=0.0008):
    """Socket centre pushed forward along the gaze axis so ~40% of the ball shows."""
    d = EYE_DIR[sx]
    return SOCKET_C[sx] + d * expose, d


# ---------------------------------------------------------------- legs
FRONT_LEG = [
    (0.0330, -0.0690, 0.1930, 0.0285, 0.0335),
    (0.0395, -0.0685, 0.1660, 0.0225, 0.0270),
    (0.0405, -0.0655, 0.1350, 0.0205, 0.0225),
    (0.0410, -0.0620, 0.1000, 0.0175, 0.0190),
    (0.0410, -0.0595, 0.0650, 0.0155, 0.0165),
    (0.0405, -0.0575, 0.0340, 0.0150, 0.0150),
    (0.0400, -0.0550, 0.0128, 0.0186, 0.0126),
]
HIND_LEG = [
    (0.0320, 0.1130, 0.1930, 0.0340, 0.0480),
    (0.0400, 0.1085, 0.1700, 0.0255, 0.0395),
    (0.0425, 0.1010, 0.1480, 0.0242, 0.0325),
    (0.0435, 0.0925, 0.1260, 0.0228, 0.0275),
    (0.0432, 0.0910, 0.1080, 0.0210, 0.0238),
    (0.0428, 0.0962, 0.0900, 0.0190, 0.0208),
    (0.0424, 0.1010, 0.0720, 0.0172, 0.0182),
    (0.0420, 0.1028, 0.0540, 0.0158, 0.0162),
    (0.0416, 0.1000, 0.0340, 0.0152, 0.0152),
    (0.0410, 0.0955, 0.0128, 0.0186, 0.0126),
]


def build_leg(sx, hind, rect, seg=22, sub=5):
    P = HIND_LEG if hind else FRONT_LEG
    pts = [Vector((sx * x, y, z)) for (x, y, z, hw, hh) in P]
    smooth = L.resample_path(pts, (len(P) - 1) * sub + 1)
    secs = []
    n = len(smooth)
    for i in range(n):
        u = (i / (n - 1.0)) * (len(P) - 1)
        k = min(int(u), len(P) - 2)
        f = u - k
        hw = L.lerp(P[k][3], P[k + 1][3], f)
        hh = L.lerp(P[k][4], P[k + 1][4], f)
        secs.append({"center": smooth[i], "hw": hw, "ht": hh, "hb": hh,
                     "cap_r": min(hw, hh)})
    v, f, uv, _ = L.loft_rings(secs, seg=seg, expo=2.1, cap_start=True, cap_end=True)
    return v, f, remap_uv(uv, rect)


# ---------------------------------------------------------------- tail
TAIL = [
    (0.150, 0.230, 0.0225),
    (0.176, 0.242, 0.0218),
    (0.204, 0.251, 0.0208),
    (0.234, 0.256, 0.0195),
    (0.264, 0.255, 0.0180),
    (0.294, 0.248, 0.0163),
    (0.322, 0.236, 0.0143),
    (0.348, 0.219, 0.0120),
    (0.370, 0.199, 0.0094),
    (0.384, 0.181, 0.0066),
]


def build_tail(seg=18):
    cs = L.resample_path([Vector((0.0, y, z)) for (y, z, r) in TAIL], 28)
    secs = []
    n = len(cs)
    for i in range(n):
        u = (i / (n - 1.0)) * (len(TAIL) - 1)
        k = min(int(u), len(TAIL) - 2)
        r = L.lerp(TAIL[k][2], TAIL[k + 1][2], u - k)
        secs.append({"center": cs[i], "hw": r, "ht": r, "hb": r, "cap_r": r})
    v, f, uv, _ = L.loft_rings(secs, seg=seg, expo=2.0)
    return v, f, remap_uv(uv, R_TAIL)


# ---------------------------------------------------------------- ears
def build_ear(sx, rect):
    base = EAR_BASE[sx]
    axis = Vector((sx * 0.340, -0.220, 0.915)).normalized()
    side = Vector((0.0, -1.0, 0.0)).cross(axis).normalized() * sx
    fwd2 = axis.cross(side).normalized()
    h, w = 0.0520, 0.0310
    tip = base + axis * h
    rows, cols = 9, 16
    verts, uvs, faces = [], [], []
    for i in range(rows + 1):
        t = i / rows
        ww = w * (1.0 - t) ** 0.52
        dd = 0.0098 * (1.0 - t) ** 0.38
        for j in range(cols):
            ph = 2.0 * math.pi * j / cols
            p = base.lerp(tip, t) + side * (math.cos(ph) * ww) + fwd2 * (math.sin(ph) * dd)
            cup = (0.0072 * (1.0 - t) ** 0.42) * max(0.0, -math.sin(ph))
            verts.append(p + fwd2 * cup)
            uvs.append((j / cols, i / rows))

    def vid(i, j):
        return i * cols + (j % cols)

    for i in range(rows):
        for j in range(cols):
            faces.append((vid(i, j), vid(i, j + 1), vid(i + 1, j + 1), vid(i + 1, j)))
    ti = len(verts)
    verts.append(tip)
    uvs.append((0.5, 1.0))
    for j in range(cols):
        faces.append((vid(rows, j), vid(rows, j + 1), ti))
    bi = len(verts)
    verts.append(base - axis * 0.006)
    uvs.append((0.5, 0.0))
    for j in range(cols):
        faces.append((vid(0, j + 1), vid(0, j), bi))
    return verts, faces, remap_uv(uvs, rect)


# ---------------------------------------------------------------- primitives
def sphere(centre, radius, rows=14, cols=20, scale=(1, 1, 1)):
    verts, faces, uvs = [], [], []
    for i in range(rows + 1):
        th = math.pi * i / rows
        for j in range(cols):
            ph = 2.0 * math.pi * j / cols
            ph2 = ph + math.pi          # put the UV seam at the back
            d = Vector((math.sin(th) * math.sin(ph2), -math.sin(th) * math.cos(ph2), math.cos(th)))
            verts.append(centre + Vector((d.x * scale[0], d.y * scale[1], d.z * scale[2])) * radius)
            uvs.append((j / cols, i / rows))

    def vid(i, j):
        return i * cols + (j % cols)

    for i in range(rows):
        for j in range(cols):
            if i == 0:
                faces.append((vid(0, j), vid(1, j), vid(1, j + 1)))
            elif i == rows - 1:
                faces.append((vid(rows - 1, j), vid(rows, j), vid(rows - 1, j + 1)))
            else:
                faces.append((vid(i, j), vid(i + 1, j), vid(i + 1, j + 1), vid(i, j + 1)))
    return verts, faces, uvs


def torus(centre, major, minor, axis, rows=8, cols=32, squash=1.0):
    up = Vector(axis).normalized()
    tmp = Vector((1, 0, 0)) if abs(up.x) < 0.9 else Vector((0, 1, 0))
    e1 = up.cross(tmp).normalized()
    e2 = up.cross(e1).normalized()
    verts, faces, uvs = [], [], []
    for i in range(rows):
        a = 2 * math.pi * i / rows
        for j in range(cols):
            b = 2 * math.pi * j / cols
            rad = major + minor * math.cos(a)
            verts.append(centre + e1 * (rad * math.cos(b)) + e2 * (rad * math.sin(b) * squash)
                         + up * (minor * math.sin(a)))
            uvs.append((j / cols, i / rows))

    def vid(i, j):
        return (i % rows) * cols + (j % cols)

    for i in range(rows):
        for j in range(cols):
            faces.append((vid(i, j), vid(i, j + 1), vid(i + 1, j + 1), vid(i + 1, j)))
    return verts, faces, uvs


# ---------------------------------------------------------------- plumbing
def make_obj(name, verts, faces, uvs, smooth=True, uvrect=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], [tuple(f) for f in faces])
    me.validate(verbose=False)
    me.update()
    if uvs:
        uvl = me.uv_layers.new(name="UVMap")
        for poly in me.polygons:
            for li in poly.loop_indices:
                uvl.data[li].uv = uvs[me.loops[li].vertex_index]
    if smooth:
        for p in me.polygons:
            p.use_smooth = True
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def recalc_normals(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    ob.data.name = name
    return ob


def wipe():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.armatures):
        for b in list(coll):
            if b.users == 0:
                coll.remove(b)


def build_all():
    """Fur parts. Muscle masses (shoulder blade / haunch) are separate ellipsoids
    that the remesh unions in, giving a real animal silhouette."""
    fur = []

    def add(v, f, uv, name):
        fur.append(make_obj(name, v, f, uv))

    v, f, uv = build_body()
    add(v, f, uv, "FurBody")
    v, f, uv = build_head()
    add(v, f, uv, "FurHead")
    v, f, uv = build_tail()
    add(v, f, uv, "FurTail")
    for sx, rect, hind, nm in ((-1, R_FL, False, "FL"), (1, R_FR, False, "FR"),
                               (-1, R_HL, True, "HL"), (1, R_HR, True, "HR")):
        v, f, uv = build_leg(sx, hind, rect)
        add(v, f, uv, "FurLeg" + nm)
    for sx, rect, nm in ((-1, R_EARL, "L"), (1, R_EARR, "R")):
        v, f, uv = build_ear(sx, rect)
        add(v, f, uv, "FurEar" + nm)

    # eyelid rims: thin tori around each socket, fur coloured
    for sx in (1, -1):
        d = EYE_DIR[sx]
        c = SOCKET_C[sx] + d * 0.0074
        v, f, uv = torus(c, 0.0112, 0.0026, d, rows=8, cols=26, squash=1.0)
        add(v, f, uv, "Lid%d" % sx)
        # upper lid: an almond wedge that covers the top third of the eye
        up = Vector((0.0, 0.0, 1.0))
        right = d.cross(up).normalized()
        upv = right.cross(d).normalized()
        lc = c + upv * 0.0095 + d * 0.0006
        for k in range(3):
            v, f, uv = sphere(lc + right * (k - 1) * 0.0052, 1.0, rows=10, cols=14,
                              scale=(0.0050, 0.0038, 0.0044))
            add(v, f, uv, "LidUp%d_%d" % (sx, k))

    # muscle masses
    for sx in (-1, 1):
        v, f, uv = sphere(Vector((sx * 0.0350, -0.0600, 0.2010)), 1.0, rows=16, cols=22,
                          scale=(0.0170, 0.0300, 0.0340))
        add(v, f, uv, "MuscleShoulder%d" % sx)
        v, f, uv = sphere(Vector((sx * 0.0385, 0.0985, 0.1700)), 1.0, rows=16, cols=22,
                          scale=(0.0190, 0.0320, 0.0400))
        add(v, f, uv, "MuscleHaunch%d" % sx)

    for ob in fur:
        recalc_normals(ob)
    return join(fur, "CatFur")


def planar_uv(verts, centre, axis, radius):
    """Orthographic UV along the gaze axis: no seam, no pole, perfect for an iris."""
    up = Vector((0.0, 0.0, 1.0))
    right = Vector(axis).cross(up)
    if right.length < 1e-6:
        right = Vector((1.0, 0.0, 0.0))
    right.normalize()
    upv = right.cross(Vector(axis)).normalized()
    uvs = []
    for v in verts:
        dd = (v - centre) / radius
        lx = max(-1.0, min(1.0, dd.dot(right)))
        ly = max(-1.0, min(1.0, dd.dot(upv)))
        uvs.append((0.5 + 0.5 * lx, 0.5 + 0.5 * ly))
    return uvs


def build_extras():
    eyes = []
    for sx, nm in ((-1, "L"), (1, "R")):
        c, d = eye_centre(sx)
        v, f, _ = sphere(c, 0.0107, rows=22, cols=28)
        uv = planar_uv(v, c, d, 0.0107)
        ob = make_obj("Eye_" + nm, v, f, uv)
        ob["eye_axis"] = list(d)
        eyes.append(ob)

    d = Vector((0.0, -0.980, -0.115)).normalized()
    ns = HC + d * head_ray(d)
    v, f, uv = sphere(ns + Vector((0, 0.0022, -0.0006)), 0.0054, rows=10, cols=16,
                      scale=(1.20, 0.44, 0.72))
    nose = make_obj("CatNose", v, f, uv)

    neck_c = Vector((0.0, -0.1185, 0.2465))
    neck_axis = Vector((0.0, -0.42, 0.907)).normalized()
    v, f, uv = torus(neck_c, 0.0432, 0.0038, neck_axis, rows=10, cols=36, squash=1.02)
    collar = make_obj("CatCollar", v, f, uv)

    bell_c = neck_c + Vector((0.0, -0.0400, -0.0245))
    v, f, uv = sphere(bell_c, 0.0096, rows=12, cols=20)
    bell = make_obj("CatBell", v, f, uv)
    v, f, uv = torus(bell_c + Vector((0, 0, 0.0110)), 0.0045, 0.0017,
                     Vector((0, 1, 0)), rows=6, cols=14)
    loop = make_obj("CatBellLoop", v, f, uv)
    return eyes, nose, collar, bell, loop


def main():
    wipe()
    fur = build_all()
    eyes, nose, collar, bell, loop = build_extras()
    allobs = [fur, nose, collar, bell, loop] + eyes

    minz = min(min((ob.matrix_world @ vv.co).z for vv in ob.data.vertices) for ob in allobs)
    print("PRE_GROUND_MIN_Z %.5f" % minz)
    for ob in allobs:
        ob.location.z -= minz
    bpy.context.view_layer.update()
    for ob in allobs:
        bpy.ops.object.select_all(action="DESELECT")
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)

    for ob in bpy.data.objects:
        if ob.type != "MESH":
            continue
        co = [ob.matrix_world @ vv.co for vv in ob.data.vertices]
        print("%-14s v=%-5d f=%-5d x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]" % (
            ob.name, len(ob.data.vertices), len(ob.data.polygons),
            min(p.x for p in co), max(p.x for p in co),
            min(p.y for p in co), max(p.y for p in co),
            min(p.z for p in co), max(p.z for p in co)))

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "cat_mesh.blend"))
    print("SAVED")


main()
