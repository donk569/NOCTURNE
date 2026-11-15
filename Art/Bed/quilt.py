"""Build a flat, regular duvet for the NOCTURNE bed and export it as FBX.

    blender --background --python quilt.py

Why this exists
---------------
The bed mesh from the environment FBX ships with `PR_Bed_Blanket_Wrinkled`
(1701 tris).  Measured on the real scene its top surface wanders between
y=0.49 and y=0.712 over the sleeping area — 14cm of swell.  The cat is dropped
onto that with a raycast, so it either floats on a crest or sinks into a trough,
and the per-frame settle keeps re-measuring a different height => visible
jitter.  Replacing the covering with a flat slab fixes the root cause instead
of damping the cat's animation.

Geometry
--------
* footprint   X [-0.79 .. 0.79]   Z [0.16 .. 1.74]   (Blender: y = -Z)
* top face    flat at z=0.726, ripple only subtracts (>=0.720) so the top never
              pokes above the collider plane Unity uses
* edges       rounded over a 7cm band, then hang down as a skirt to z=0.44
* thickness   Solidify 0.03 with rim, normals recalculated outward
* uv          cube projection, 1m scale (fabric texture tiles predictably)
"""
import math
import os
import sys

import bpy
import bmesh
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
FBX = os.path.join(OUT, "Quilt.fbx")

# --- footprint (Unity space, converted below) ------------------------------
X0, X1 = -0.79, 0.79
Z0, Z1 = 0.16, 1.74          # Unity Z
Z_TOP = 0.726                # highest point of the quilt
RIPPLE = 0.006               # total downward ripple range (0.720 .. 0.726)
SKIRT_BOTTOM = 0.44
EDGE_BAND = 0.07             # how far in from the rim the rounding starts
NX, NZ = 52, 52
THICKNESS = 0.03


def log(*a):
    print("[QUILT]", *a)
    sys.stdout.flush()


def to_blender(x, z_unity):
    """Unity (x, z) -> Blender (x, y).  Unity +Z == Blender -Y."""
    return x, -z_unity


def smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


def surface_z(x, z):
    """Height of the duvet's outer surface.  0.720 .. 0.726, never above Z_TOP."""
    # distance to the nearest rim, normalised over EDGE_BAND
    d = min(x - X0, X1 - x, z - Z0, Z1 - z)
    if d <= 0.0:
        return SKIRT_BOTTOM
    if d < EDGE_BAND:
        # round the edge over, then fall away to the skirt
        k = smoothstep(d / EDGE_BAND)
        top_here = Z_TOP - RIPPLE * ripple(x, z)
        return SKIRT_BOTTOM + (top_here - SKIRT_BOTTOM) * k
    return Z_TOP - RIPPLE * ripple(x, z)


def ripple(x, z):
    """0..1, very gentle cloth undulation.  Only ever reduces the height."""
    a = math.sin(x * 6.1 + 0.7) * math.sin(z * 4.3 - 0.4)
    b = math.sin((x + z) * 9.7) * 0.35
    v = 0.5 + 0.35 * a + 0.15 * b
    return max(0.0, min(1.0, v))


def build():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)

    me = bpy.data.meshes.new("M_PR_BedQuilt")
    bm = bmesh.new()

    grid = [[None] * (NZ + 1) for _ in range(NX + 1)]
    for i in range(NX + 1):
        x = X0 + (X1 - X0) * i / float(NX)
        for j in range(NZ + 1):
            zu = Z0 + (Z1 - Z0) * j / float(NZ)
            bx, by = to_blender(x, zu)
            grid[i][j] = bm.verts.new((bx, by, surface_z(x, zu)))

    for i in range(NX):
        for j in range(NZ):
            bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))

    bm.normal_update()
    bm.to_mesh(me)
    bm.free()

    ob = bpy.data.objects.new("M_PR_BedQuilt", me)
    bpy.context.collection.objects.link(ob)
    log("grid verts", len(me.vertices), "faces", len(me.polygons))

    # thin shell so it is a real solid, not a single-sided plane
    solid = ob.modifiers.new("Solidify", "SOLIDIFY")
    solid.thickness = THICKNESS
    solid.offset = 1.0
    solid.use_rim = True
    solid.use_even_offset = True

    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.modifier_apply(modifier=solid.name)

    # normals outward
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")

    # simple cube-projected uv, 1m scale
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=1.0, correct_aspect=True)
    bpy.ops.object.mode_set(mode="OBJECT")

    mat = bpy.data.materials.new("MAT_Quilt")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = (0.86, 0.83, 0.78, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.92
    ob.data.materials.append(mat)

    ob.data.update()
    zs = [ (ob.matrix_world @ v.co).z for v in ob.data.vertices ]
    xs = [ (ob.matrix_world @ v.co).x for v in ob.data.vertices ]
    ys = [ (ob.matrix_world @ v.co).y for v in ob.data.vertices ]
    log("bbox x %.4f..%.4f  y %.4f..%.4f  z %.4f..%.4f" %
        (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    log("tris", sum(max(0, len(p.vertices) - 2) for p in ob.data.polygons))
    return ob


def export(ob):
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(
        filepath=FBX,
        use_selection=True,
        object_types={"MESH"},
        apply_scale_options="FBX_SCALE_ALL",
        global_scale=1.0,
        axis_forward="-Z",
        axis_up="Y",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_tspace=False,
        use_custom_props=False,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="STRIP",
        embed_textures=False,
        batch_mode="OFF",
    )
    log("WROTE", FBX, os.path.getsize(FBX))


def main():
    ob = build()
    export(ob)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "quilt.blend"))
    log("DONE")


main()
