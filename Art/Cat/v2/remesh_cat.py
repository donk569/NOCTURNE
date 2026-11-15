"""Fuse the cat parts into one continuous organic surface via voxel remesh.

The separate lofts (torso, legs, head, tail, ears) intersect with hard creases.
Voxel remesh unions them into a single watertight skin with soft fillets at every
junction — that is what makes it read as a real animal instead of table legs.

Pipeline: join -> voxel remesh -> relax -> decimate -> smart UV -> save
"""
import os
import sys

import bmesh
import bpy

try:
    HERE = SCRIPTS_DIR
except NameError:
    HERE = os.path.dirname(os.path.abspath(__file__))

try:
    P = PARAMS
except NameError:
    P = {}

OUT = os.path.join(HERE, "out")
VOXEL = float(P.get("voxel", 0.0028))
TARGET_TRIS = int(P.get("tris", 20000))


def only_mesh(name):
    ob = bpy.data.objects.get(name)
    if ob is None:
        raise RuntimeError("missing object %s" % name)
    return ob


def activate(ob):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def main():
    fur = only_mesh("CatFur")
    activate(fur)
    ob = fur

    bpy.ops.object.mode_set(mode="OBJECT")
    # keep the pre-remesh surface around for debugging
    print("BEFORE v=%d f=%d tris=%d" % (len(ob.data.vertices), len(ob.data.polygons), tri_count(ob)))

    m = ob.modifiers.new("Remesh", "REMESH")
    m.mode = "VOXEL"
    m.voxel_size = VOXEL
    m.adaptivity = 0.0
    m.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier=m.name)
    print("AFTER_REMESH v=%d f=%d tris=%d" % (len(ob.data.vertices), len(ob.data.polygons), tri_count(ob)))

    # relax the voxel staircase
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    for _ in range(6):
        bmesh.ops.smooth_vert(bm, verts=bm.verts, factor=0.5,
                              use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.to_mesh(me)
    bm.free()
    me.update()

    # decimate to a game-friendly budget
    tris = tri_count(ob)
    if tris > TARGET_TRIS:
        d = ob.modifiers.new("Decimate", "DECIMATE")
        d.decimate_type = "COLLAPSE"
        d.ratio = float(TARGET_TRIS) / float(tris)
        d.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=d.name)
    print("AFTER_DECIMATE v=%d f=%d tris=%d" % (len(ob.data.vertices), len(ob.data.polygons), tri_count(ob)))

    # a second, gentler relax so the decimation facets disappear
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.smooth_vert(bm, verts=bm.verts, factor=0.35,
                          use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.to_mesh(me)
    bm.free()
    me.update()

    # drop stray slivers left over from the voxel union
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_area() < 1e-9], context="FACES")
    bm.to_mesh(me)
    bm.free()
    me.update()

    for p in me.polygons:
        p.use_smooth = True

    # fresh UVs -- the texture is painted from 3D position, so any unwrap works
    activate(ob)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=1.30, island_margin=0.004,
                             area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    try:
        bpy.ops.uv.pack_islands(rotate=True, margin=0.0025)
    except Exception as e:
        print("pack_islands skipped:", e)
    bpy.ops.object.mode_set(mode="OBJECT")

    zs = [(ob.matrix_world @ v.co).z for v in me.vertices]
    print("MINZ %.5f MAXZ %.5f" % (min(zs), max(zs)))
    print("UV_LAYERS", [l.name for l in me.uv_layers])

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "cat_remesh.blend"))
    print("SAVED")


main()
