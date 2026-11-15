# 减面 + 体型测量
import bpy, sys
sys.path.insert(0, r"C:/Users/lenovo/.workbuddy/skills/blender-character-pipeline/scripts")
import measure

# 取网格
mesh = None
for o in bpy.data.objects:
    if o.type == 'MESH':
        mesh = o
        break
print("MESH", mesh.name, "verts", len(mesh.data.vertices), "tris", len(mesh.data.polygons))

# 减面到 ~30k 三角
tri_count = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
target = 30000
ratio = min(1.0, target / max(1, tri_count))
bpy.context.view_layer.objects.active = mesh
bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True)
mod = mesh.modifiers.new("Dec", 'DECIMATE')
mod.decimate_type = 'COLLAPSE'
mod.ratio = ratio
bpy.ops.object.modifier_apply(modifier=mod.name)

tri2 = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
print("DECIMATED", tri2)

m = measure.measure(mesh)
arch, why = measure.guess_archetype(m)
print("ARCHETYPE", arch, why)
try:
    print("MEASURE_REPORT")
    print(measure.report(m))
except Exception as e:
    print("report err", e)
print("RAW", {k: (round(v, 4) if isinstance(v, float) else v) for k, v in m.items() if isinstance(v, (int, float, str, list))})
