"""Build the quadruped skeleton, skin the mesh and validate the deformation.

Bone names `root` / `neck` / `head` are contractual (CatBones in Unity).
Everything else is free, but tail.01..05 drive the tail sway.
"""
import math
import os
import sys

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

try:
    HERE = SCRIPTS_DIR
except NameError:
    HERE = os.path.dirname(os.path.abspath(__file__))

try:
    P = PARAMS
except NameError:
    P = {}

OUT = os.path.join(HERE, "out")

EAR_AXIS = Vector((0.340, -0.220, 0.915)).normalized()


def mirror(v):
    return Vector((-v.x, v.y, v.z))


def build_bones():
    """(name, head, tail, parent, connect)"""
    B = []
    B.append(("root", Vector((0, 0.02, 0.035)), Vector((0, -0.06, 0.035)), None, False))
    B.append(("hips", Vector((0, 0.112, 0.206)), Vector((0, 0.055, 0.210)), "root", False))
    B.append(("spine", Vector((0, 0.055, 0.210)), Vector((0, -0.005, 0.207)), "hips", True))
    B.append(("spine2", Vector((0, -0.005, 0.207)), Vector((0, -0.060, 0.210)), "spine", True))
    B.append(("chest", Vector((0, -0.060, 0.210)), Vector((0, -0.098, 0.216)), "spine2", True))
    B.append(("neck", Vector((0, -0.098, 0.216)), Vector((0, -0.132, 0.240)), "chest", True))
    B.append(("head", Vector((0, -0.132, 0.240)), Vector((0, -0.190, 0.246)), "neck", True))
    B.append(("jaw", Vector((0, -0.168, 0.240)), Vector((0, -0.200, 0.230)), "head", False))

    for sx, nm in ((1, "L"), (-1, "R")):
        base = Vector((sx * 0.0282, -0.1285, 0.2825))
        ax = Vector((sx * EAR_AXIS.x, EAR_AXIS.y, EAR_AXIS.z))
        B.append(("ear." + nm, base, base + ax * 0.050, "head", False))

    tail = [Vector((0, 0.148, 0.232)), Vector((0, 0.196, 0.248)),
            Vector((0, 0.243, 0.256)), Vector((0, 0.289, 0.252)),
            Vector((0, 0.334, 0.234)), Vector((0, 0.375, 0.200))]
    for i in range(5):
        B.append(("tail.%02d" % (i + 1), tail[i], tail[i + 1],
                  "hips" if i == 0 else "tail.%02d" % i, i > 0))

    # front legs
    sh = [(0.030, -0.062, 0.206), (0.040, -0.068, 0.152),
          (0.041, -0.062, 0.100), (0.041, -0.058, 0.052), (0.040, -0.053, 0.012)]
    hd = [(0.040, 0.110, 0.190), (0.043, 0.094, 0.128),
          (0.043, 0.100, 0.072), (0.041, 0.098, 0.028), (0.041, 0.092, 0.008)]
    names_f = ["shoulder", "upper_arm", "forearm", "hand"]
    names_h = ["thigh", "shin", "foot", "toe"]
    for sx, nm in ((1, "L"), (-1, "R")):
        for i, bn in enumerate(names_f):
            a = Vector((sx * sh[i][0], sh[i][1], sh[i][2]))
            b = Vector((sx * sh[i + 1][0], sh[i + 1][1], sh[i + 1][2]))
            par = "chest" if i == 0 else "%s.%s" % (names_f[i - 1], nm)
            B.append(("%s.%s" % (bn, nm), a, b, par, i > 0))
        for i, bn in enumerate(names_h):
            a = Vector((sx * hd[i][0], hd[i][1], hd[i][2]))
            b = Vector((sx * hd[i + 1][0], hd[i + 1][1], hd[i + 1][2]))
            par = "hips" if i == 0 else "%s.%s" % (names_h[i - 1], nm)
            B.append(("%s.%s" % (bn, nm), a, b, par, i > 0))
    return B


def create_armature(name="CatRig"):
    if name in bpy.data.objects:
        bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)
    arm = bpy.data.armatures.new(name)
    ob = bpy.data.objects.new(name, arm)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.edit_bones
    for (nm, h, t, par, conn) in build_bones():
        b = eb.new(nm)
        b.head = h
        b.tail = t
        if par:
            b.parent = eb[par]
            b.use_connect = bool(conn)
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in ob.pose.bones:
        pb.rotation_mode = "XYZ"
    return ob


def skin(fur, rig):
    bpy.ops.object.select_all(action="DESELECT")
    fur.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    ok = True
    try:
        bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    except Exception as e:
        print("AUTO_WEIGHTS_FAILED", e)
        ok = False
    return ok


def rigid_bind(ob, rig, bone):
    """Bind a small rigid part 100% to one bone."""
    for m in list(ob.modifiers):
        ob.modifiers.remove(m)
    if ob.parent != rig:
        ob.parent = rig
        ob.matrix_parent_inverse = rig.matrix_world.inverted()
    groups = list(ob.vertex_groups)
    for g in groups:
        ob.vertex_groups.remove(g)
    g = ob.vertex_groups.new(name=bone)
    g.add(list(range(len(ob.data.vertices))), 1.0, "REPLACE")
    m = ob.modifiers.new("Armature", "ARMATURE")
    m.object = rig
    m.use_vertex_groups = True


def weight_report(fur, rig):
    me = fur.data
    vg = {g.index: g.name for g in fur.vertex_groups}
    bone_names = [b.name for b in rig.data.bones]
    missing = [b for b in bone_names if b not in fur.vertex_groups]
    # unweighted verts
    unw = 0
    maxw = {n: 0.0 for n in vg.values()}
    for v in me.vertices:
        tot = 0.0
        for g in v.groups:
            tot += g.weight
            nm = vg.get(g.group)
            if nm and g.weight > maxw.get(nm, 0.0):
                maxw[nm] = g.weight
        if tot < 0.01:
            unw += 1
    weak = [n for n, w in maxw.items() if w < 0.15]
    print("WEIGHTS unweighted=%d  no_group_bones=%s  weak_bones=%s"
          % (unw, missing[:8], weak[:8]))
    return unw, missing, weak


def check_pose(fur, rig):
    """Deform a test pose and make sure the mesh does not explode."""
    for pb in rig.pose.bones:
        pb.rotation_euler = (0, 0, 0)
    pb = rig.pose.bones.get("forearm.L")
    if pb:
        pb.rotation_euler = (0.9, 0, 0)
    pb = rig.pose.bones.get("head")
    if pb:
        pb.rotation_euler = (0.3, 0, 0.25)
    dg = bpy.context.evaluated_depsgraph_get()
    ev = fur.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.array([v.co[:] for v in me.vertices], dtype=np.float32)
    print("POSED bbox min", co.min(axis=0).round(3), "max", co.max(axis=0).round(3))
    ev.to_mesh_clear()
    for pb in rig.pose.bones:
        pb.rotation_euler = (0, 0, 0)


def main():
    fur = bpy.data.objects["CatFur"]
    rig = create_armature()
    ok = skin(fur, rig)
    print("AUTO_WEIGHTS", ok)
    weight_report(fur, rig)

    for nm, bone in (("Eye_L", "head"), ("Eye_R", "head"), ("CatNose", "head"),
                     ("CatCollar", "neck"), ("CatBell", "neck"), ("CatBellLoop", "neck")):
        ob = bpy.data.objects.get(nm)
        if ob:
            rigid_bind(ob, rig, bone)

    check_pose(fur, rig)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "cat_rig.blend"))
    print("BONES", len(rig.data.bones), [b.name for b in rig.data.bones][:8])
    print("SAVED rig")


main()
