"""v3 -- build the quadruped skeleton for the AI base mesh.

Every joint below is a measured landmark (see landmarks.py), not a proportion
template: the skill's docs are explicit that archetype templates are wrong for
long-bodied quadrupeds.

Contract with the Unity side: bones `root` / `neck` / `head` keep their names
(CatBones), `tail.01..05` drive the tail sway.
"""
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
sys.path.insert(0, os.path.join(HERE, "..", "v2"))
import preview  # noqa: E402


def log(*a):
    print("[RIG]", *a)
    sys.stdout.flush()


# --------------------------------------------------------------- bone layout
def build_bones():
    B = []   # (name, head, tail, parent, connected, deform)

    B.append(("root", (0, 0.030, 0.170), (0, -0.030, 0.170), None, False, False))
    B.append(("hips", (0, 0.120, 0.178), (0, 0.055, 0.176), "root", False, True))
    B.append(("spine", (0, 0.055, 0.176), (0, -0.005, 0.174), "hips", True, True))
    B.append(("spine2", (0, -0.005, 0.174), (0, -0.060, 0.172), "spine", True, True))
    B.append(("chest", (0, -0.060, 0.172), (0, -0.110, 0.176), "spine2", True, True))
    B.append(("neck", (0, -0.110, 0.176), (0, -0.170, 0.196), "chest", True, True))
    B.append(("head", (0, -0.170, 0.196), (0, -0.245, 0.212), "neck", True, True))
    B.append(("jaw", (0, -0.212, 0.206), (0, -0.258, 0.196), "head", False, True))

    for sx, nm in ((1, "L"), (-1, "R")):
        B.append(("ear." + nm, (sx * 0.030, -0.234, 0.268),
                  (sx * 0.045, -0.236, 0.303), "head", False, True))

    tail = [(0, 0.150, 0.156), (0, 0.178, 0.118), (0, 0.202, 0.082),
            (0, 0.226, 0.063), (0, 0.252, 0.060), (0, 0.280, 0.068)]
    for i in range(5):
        B.append(("tail.%02d" % (i + 1), tail[i], tail[i + 1],
                  "hips" if i == 0 else "tail.%02d" % i, i > 0, True))

    # front leg: scapula runs down-forward, then a near-vertical column
    fr = [(0.031, -0.104, 0.192), (0.030, -0.132, 0.152),
          (0.028, -0.146, 0.106), (0.026, -0.149, 0.050), (0.026, -0.152, 0.008)]
    # hind leg: thigh, shin back to the hock, then the foot angles forward
    hd = [(0.034, 0.062, 0.186), (0.042, 0.086, 0.120),
          (0.039, 0.099, 0.058), (0.037, 0.082, 0.014), (0.037, 0.108, 0.006)]
    names_f = ["shoulder", "upper_arm", "forearm", "hand"]
    names_h = ["thigh", "shin", "foot", "toe"]
    for sx, nm in ((1, "L"), (-1, "R")):
        for i, bn in enumerate(names_f):
            a = (sx * fr[i][0], fr[i][1], fr[i][2])
            b = (sx * fr[i + 1][0], fr[i + 1][1], fr[i + 1][2])
            par = "chest" if i == 0 else "%s.%s" % (names_f[i - 1], nm)
            B.append(("%s.%s" % (bn, nm), a, b, par, i > 0, True))
        for i, bn in enumerate(names_h):
            a = (sx * hd[i][0], hd[i][1], hd[i][2])
            b = (sx * hd[i + 1][0], hd[i + 1][1], hd[i + 1][2])
            par = "hips" if i == 0 else "%s.%s" % (names_h[i - 1], nm)
            B.append(("%s.%s" % (bn, nm), a, b, par, i > 0, True))
    return B


def create_armature(name="CatRig"):
    if name in bpy.data.objects:
        bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)
    arm = bpy.data.armatures.new(name)
    ob = bpy.data.objects.new(name, arm)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.edit_bones
    for (nm, h, t, par, conn, deform) in build_bones():
        b = eb.new(nm)
        b.head = Vector(h)
        b.tail = Vector(t)
        b.use_deform = deform
        if par:
            b.parent = eb[par]
            b.use_connect = bool(conn)
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in ob.pose.bones:
        pb.rotation_mode = "XYZ"
    return ob


BONE_R = {          # influence radius per bone family, metres
    "hips": 0.050, "spine": 0.048, "spine2": 0.046, "chest": 0.044,
    "neck": 0.034, "head": 0.032, "jaw": 0.018,
    "shoulder": 0.026, "upper_arm": 0.018, "forearm": 0.014, "hand": 0.015,
    "thigh": 0.028, "shin": 0.019, "foot": 0.014, "toe": 0.015,
    "ear": 0.013, "tail": 0.016, "root": 0.0,
}


def _radius(name):
    base = name.split(".")[0]
    return BONE_R.get(base, 0.02)


def skin(mesh, rig):
    """Envelope weights, computed here rather than with bone heat.

    Heat weights silently produced zero groups on this mesh (30002 unweighted
    vertices), and the skill docs already warn that AI meshes break heat.  The
    envelope below only looks at the distance to each bone segment, so it cannot
    fail -- it just has to be tuned.
    """
    me = mesh.data
    n = len(me.vertices)
    V = np.array([v.co[:] for v in me.vertices], dtype=np.float64)

    bones = [b for b in rig.data.bones if b.use_deform]
    names = [b.name for b in bones]
    W = np.zeros((n, len(bones)), dtype=np.float64)
    for j, b in enumerate(bones):
        a = np.array(b.head_local, dtype=np.float64)
        c = np.array(b.tail_local, dtype=np.float64)
        ab = c - a
        L2 = float(ab @ ab)
        if L2 < 1e-12:
            continue
        t = np.clip(((V - a) @ ab) / L2, 0.0, 1.0)
        d = np.linalg.norm(V - (a + t[:, None] * ab), axis=1)
        r = _radius(b.name)
        fall = np.clip(1.0 - (d / max(r, 1e-4)) ** 1.4, 0.0, 1.0) ** 2
        W[:, j] = fall / (d + 0.006) ** 2

    # keep the four strongest bones per vertex
    keep = 4
    if W.shape[1] > keep:
        cut = np.partition(W, -keep, axis=1)[:, -keep][:, None]
        W = np.where(W >= cut, W, 0.0)
    tot = W.sum(axis=1)
    dead = tot <= 1e-12
    if dead.any():                      # nothing in range: snap to the nearest bone
        for i in np.flatnonzero(dead):
            best, bd = 0, 1e9
            for j, b in enumerate(bones):
                a = np.array(b.head_local)
                c = np.array(b.tail_local)
                ab = c - a
                L2 = max(float(ab @ ab), 1e-12)
                t = min(1.0, max(0.0, float((V[i] - a) @ ab) / L2))
                d = float(np.linalg.norm(V[i] - (a + t * ab)))
                if d < bd:
                    bd, best = d, j
            W[i, best] = 1.0
        tot = W.sum(axis=1)
    W /= tot[:, None]

    # relax across edges so the shoulder / hip do not crease
    edges = np.array([e.vertices[:] for e in me.edges], dtype=np.int32)
    if len(edges):
        deg = np.zeros(n)
        np.add.at(deg, edges[:, 0], 1.0)
        np.add.at(deg, edges[:, 1], 1.0)
        deg = np.maximum(deg, 1.0)
        for _ in range(3):
            acc = np.zeros_like(W)
            np.add.at(acc, edges[:, 0], W[edges[:, 1]])
            np.add.at(acc, edges[:, 1], W[edges[:, 0]])
            W = 0.45 * W + 0.55 * (acc / deg[:, None])
            W /= np.maximum(W.sum(axis=1, keepdims=True), 1e-12)

    for g in list(mesh.vertex_groups):
        mesh.vertex_groups.remove(g)
    groups = [mesh.vertex_groups.new(name=nm) for nm in names]
    for j, g in enumerate(groups):
        col = W[:, j]
        idx = np.flatnonzero(col > 1e-4)
        # add() is slow one by one; batch in chunks of identical weight is worse,
        # so just push them individually
        for i in idx:
            g.add([int(i)], float(col[i]), "REPLACE")

    for m in list(mesh.modifiers):
        if m.type == "ARMATURE":
            mesh.modifiers.remove(m)
    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    mod.use_vertex_groups = True
    mesh.parent = rig
    mesh.matrix_parent_inverse = rig.matrix_world.inverted()
    log("SKIN bones=%d verts=%d" % (len(bones), n))


def weight_report(mesh):
    vg = {g.index: g.name for g in mesh.vertex_groups}
    unw = 0
    maxw = {}
    for v in mesh.data.vertices:
        tot = 0.0
        for g in v.groups:
            tot += g.weight
            nm = vg.get(g.group)
            if nm:
                maxw[nm] = max(maxw.get(nm, 0.0), g.weight)
        if tot < 0.01:
            unw += 1
    weak = sorted([n for n, w in maxw.items() if w < 0.15])
    log("WEIGHTS unweighted=%d weak=%s" % (unw, weak[:10]))
    return unw, weak


def ground_check(mesh, rig):
    """Lowest deformed vertex per frame later; here just the rest pose."""
    dg = bpy.context.evaluated_depsgraph_get()
    me = mesh.evaluated_get(dg).to_mesh()
    zmin = min(v.co.z for v in me.vertices)
    mesh.evaluated_get(dg).to_mesh_clear()
    log("REST min_z", round(zmin, 5))


def test_pose(mesh, rig):
    """One pose that exercises every chain: if weights are wrong, it explodes."""
    pose = {"forearm.L": (1.1, 0, 0), "thigh.R": (-0.7, 0, 0),
            "head": (0.25, 0, 0.35), "tail.01": (-0.5, 0, 0.3),
            "tail.02": (-0.4, 0, 0.25), "ear.L": (0, 0, 0.4)}
    for pb in rig.pose.bones:
        pb.rotation_euler = (0, 0, 0)
    for nm, r in pose.items():
        pb = rig.pose.bones.get(nm)
        if pb:
            pb.rotation_euler = r
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    me = mesh.evaluated_get(dg).to_mesh()
    co = np.array([v.co[:] for v in me.vertices], dtype=np.float32)
    log("POSED bbox", co.min(axis=0).round(3), co.max(axis=0).round(3))
    mesh.evaluated_get(dg).to_mesh_clear()


def xray_render(mesh, rig, tag):
    """Opaque body + emissive bones: any bone outside the skin lights up."""
    bone_mat = bpy.data.materials.new("PV_Bone")
    bone_mat.use_nodes = True
    nt = bone_mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs[0].default_value = (1.0, 0.15, 0.05, 1)
    em.inputs[1].default_value = 12.0
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    nt.links.new(em.outputs[0], out.inputs["Surface"])

    rig.show_in_front = True
    rig.data.display_type = "OCTAHEDRAL"
    # real geometry so the bones show up in a render
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    rig.data.display_type = "STICK"
    render_bones = []
    for b in rig.data.bones:
        a = rig.matrix_world @ b.head_local
        c = rig.matrix_world @ b.tail_local
        d = c - a
        if d.length < 1e-5:
            continue
        me = bpy.data.meshes.new("BN_" + b.name)
        r = 0.0035
        seg = 6
        verts = []
        for k in range(2):
            base = a if k == 0 else c
            verts += [(base.x + r * math.cos(2 * math.pi * i / seg),
                       base.y + r * math.sin(2 * math.pi * i / seg), base.z) for i in range(seg)]
        faces = []
        for i in range(seg):
            j = (i + 1) % seg
            faces.append((i, j, seg + j, seg + i))
        me.from_pydata(verts, [], faces)
        me.update()
        ob = bpy.data.objects.new("PV_Bone_" + b.name, me)
        bpy.context.scene.collection.objects.link(ob)
        ob.data.materials.append(bone_mat)
        render_bones.append(ob)

    # translucent body so the bones read through it
    body_mat = mesh.data.materials[0] if mesh.data.materials else None
    if body_mat and body_mat.use_nodes:
        bsdf = next((n for n in body_mat.node_tree.nodes
                     if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf:
            bsdf.inputs["Alpha"].default_value = 0.28
        try:
            body_mat.blend_method = "BLEND"
            body_mat.show_transparent_back = False
        except Exception:
            pass

    preview.setup_render(res=760, samples=16)
    preview.add_lights(power=1.0)
    tgt = Vector((0, 0, 0.15))
    preview.render_view("side", (0.55, 0.0, 0.16), tgt,
                        os.path.join(OUT, "%s_side.png" % tag), ortho=0.68)
    preview.render_view("top", (0.0, 0.0, 0.80), tgt,
                        os.path.join(OUT, "%s_top.png" % tag), ortho=0.68)
    preview.render_view("head", (0.16, -0.34, 0.30), (0, -0.22, 0.245),
                        os.path.join(OUT, "%s_head.png" % tag), focal=62.0)
    preview.render_view("back", (0.0, 0.60, 0.20), tgt,
                        os.path.join(OUT, "%s_back.png" % tag), ortho=0.55)
    for ob in render_bones:
        bpy.data.objects.remove(ob, do_unlink=True)
    if body_mat and body_mat.use_nodes:
        bsdf = next((n for n in body_mat.node_tree.nodes
                     if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf:
            bsdf.inputs["Alpha"].default_value = 1.0


def main():
    bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, "cat_look.blend"))
    mesh = bpy.data.objects["CatBody"]
    for ob in bpy.data.objects:
        if ob.name.startswith("PV_") and ob.type == "MESH":
            bpy.data.objects.remove(ob, do_unlink=True)
    rig = create_armature()
    skin(mesh, rig)
    weight_report(mesh)
    ground_check(mesh, rig)
    xray_render(mesh, rig, "rig1")
    test_pose(mesh, rig)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "cat_rigged.blend"))
    log("BONES", len(rig.data.bones))
    log("DONE")


main()
