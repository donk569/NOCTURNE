import bpy
import bmesh
from mathutils import Vector

bpy.ops.wm.open_mainfile(filepath=r"D:\unity\NOCTURNE\Art\Bed\out\quilt.blend")
ob = [o for o in bpy.data.objects if o.type == "MESH"][0]
me = ob.data
print("OBJ", ob.name, "verts", len(me.vertices), "polys", len(me.polygons))
bb = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
xs = [v.x for v in bb]; ys = [v.y for v in bb]; zs = [v.z for v in bb]
print("BBOX x %.3f..%.3f y %.3f..%.3f z %.3f..%.3f" % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
print("DIM  %.3f x %.3f x %.3f" % (max(xs)-min(xs), max(ys)-min(ys), max(zs)-min(zs)))

bm = bmesh.new(); bm.from_mesh(me)
nonman = sum(1 for e in bm.edges if not e.is_manifold)
print("NON_MANIFOLD_EDGES", nonman)
# normals should point away from the mesh centre-ish (up for the top face)
up = 0; down = 0
for f in bm.faces:
    n = (ob.matrix_world.to_3x3() @ f.normal).normalized()
    if n.z > 0.8: up += 1
    elif n.z < -0.8: down += 1
print("FACES up", up, "down", down, "total", len(bm.faces))
bm.free()

# top surface height stats over the sleeping area
top = [v.co.z for v in me.vertices if v.co.z > 0.5]
print("TOP z min %.4f max %.4f span %.4f" % (min(top), max(top), max(top) - min(top)))
print("MATERIALS", [m.name if m else None for m in me.materials])
print("UV_LAYERS", [l.name for l in me.uv_layers])
print("SMOOTH", any(p.use_smooth for p in me.polygons))
# thickness probe: cast straight down from above the top face and measure the hit depth
import mathutils
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
for (x, y) in ((0.0, -0.9), (0.3, -1.2), (-0.4, -0.6), (0.0, -1.5)):
    origin = Vector((x, y, 2.0))
    hit, loc, nor, idx = ob.ray_cast(origin, Vector((0, 0, -1)), depsgraph=dg)
    if hit:
        hit2, loc2, nor2, idx2 = ob.ray_cast(loc + Vector((0, 0, -0.001)), Vector((0, 0, -1)), depsgraph=dg)
        thick = (loc.z - loc2.z) if hit2 else -1
        print("PROBE (%.2f,%.2f) top z=%.4f normal.z=%.2f thickness=%.3f" % (x, y, loc.z, nor.z, thick))
    else:
        print("PROBE (%.2f,%.2f) MISS" % (x, y))
