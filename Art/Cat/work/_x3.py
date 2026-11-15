# 穿模检测渲染：猫体完全不透明，骨骼发光——只有穿出体外的骨骼才会显示
import bpy, bmesh, mathutils, math, sys

MODE = "stand"
BLEND = "D:/unity/NOCTURNE/Art/Cat/work/cat_stand_fit.blend"
TAG = "fin2"

bpy.ops.wm.open_mainfile(filepath=BLEND)
mesh = rig = None
for o in bpy.data.objects:
    if o.type == 'MESH':
        mesh = o
    if o.type == 'ARMATURE':
        rig = o

# 猫体不透明，改成中性灰（便于看粉色）
for slot in mesh.material_slots:
    m = slot.material
    if not m:
        continue
    try:
        m.blend_method = 'OPAQUE'
    except Exception:
        pass
    if m.use_nodes:
        for n in m.node_tree.nodes:
            if n.type == 'BSDF_PRINCIPLED':
                n.inputs['Alpha'].default_value = 1.0
                n.inputs['Base Color'].default_value = (0.30, 0.30, 0.33, 1)
                n.inputs['Roughness'].default_value = 0.85

mat_b = bpy.data.materials.new("DBG_B")
mat_b.use_nodes = True
nt = mat_b.node_tree
for n in list(nt.nodes):
    nt.nodes.remove(n)
em = nt.nodes.new("ShaderNodeEmission")
em.inputs[0].default_value = (1.0, 0.05, 0.75, 1.0)
em.inputs[1].default_value = 5.0
o1 = nt.nodes.new("ShaderNodeOutputMaterial")
nt.links.new(em.outputs[0], o1.inputs[0])

col = bpy.data.collections.new("DBG")
bpy.context.scene.collection.children.link(col)

def cyl(name, p0, p1, r):
    d = p1 - p0
    L = d.length
    if L < 1e-5:
        return
    me = bpy.data.meshes.new(name)
    bmv = bmesh.new()
    bmesh.ops.create_cone(bmv, cap_ends=True, cap_tris=False, segments=10,
                          radius1=r, radius2=r, depth=L)
    bmv.to_mesh(me); bmv.free()
    ob = bpy.data.objects.new(name, me)
    me.materials.append(mat_b)
    col.objects.link(ob)
    ob.location = (p0 + p1) * 0.5
    ob.rotation_mode = 'QUATERNION'
    ob.rotation_quaternion = d.to_track_quat('Z', 'Y')

def sph(name, p, r):
    me = bpy.data.meshes.new(name)
    bmv = bmesh.new()
    bmesh.ops.create_uvsphere(bmv, u_segments=10, v_segments=8, radius=r)
    bmv.to_mesh(me); bmv.free()
    ob = bpy.data.objects.new(name, me)
    me.materials.append(mat_b)
    col.objects.link(ob)
    ob.location = p

for b in rig.data.bones:
    h = rig.matrix_world @ b.head_local
    t = rig.matrix_world @ b.tail_local
    r = 0.0042 if b.name.startswith(("tail", "ear", "toe")) else 0.0062
    cyl("B_" + b.name, h, t, r)
    sph("J_" + b.name, h, r * 1.25)

# 地面/世界
w = bpy.context.scene.world or bpy.data.worlds.new("W")
bpy.context.scene.world = w
w.use_nodes = True
bg = w.node_tree.nodes.get("Background")
if bg:
    bg.inputs[0].default_value = (0.16, 0.17, 0.20, 1)
    bg.inputs[1].default_value = 1.2

def lamp(name, loc, energy, size=1.5):
    ld = bpy.data.lights.new(name, 'AREA')
    ld.energy = energy; ld.size = size
    ob = bpy.data.objects.new(name, ld)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = loc
    ob.rotation_mode = 'QUATERNION'
    ob.rotation_quaternion = (mathutils.Vector((0, 0, 0.15)) - mathutils.Vector(loc)).to_track_quat('-Z', 'Y')

lamp("K", (0.8, -0.8, 0.9), 130, 1.6)
lamp("F", (-0.9, -0.3, 0.5), 60, 2.0)

cd = bpy.data.cameras.new("C"); cd.lens = 55
cam = bpy.data.objects.new("C", cd)
bpy.context.scene.collection.objects.link(cam)
bpy.context.scene.camera = cam

sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x = 900
sc.render.resolution_y = 620
sc.view_settings.view_transform = 'Standard'
try:
    sc.eevee.taa_render_samples = 32
except Exception:
    pass

def shoot(name, loc, look, lens=55):
    cam.location = loc
    cam.data.lens = lens
    cam.rotation_mode = 'QUATERNION'
    cam.rotation_quaternion = (mathutils.Vector(look) - mathutils.Vector(loc)).to_track_quat('-Z', 'Y')
    sc.render.filepath = "D:/unity/NOCTURNE/Art/Cat/work/renders/xray_%s_%s.png" % (TAG, name)
    bpy.ops.render.render(write_still=True)
    print("SHOT", name)

if MODE == "stand":
    shoot("side", (0.95, 0.00, 0.16), (0.0, 0.00, 0.14))
    shoot("top",  (0.00, 0.00, 0.95), (0.0, 0.00, 0.14))
    shoot("front", (-0.10, -0.95, 0.18), (0.0, -0.05, 0.15))
else:
    shoot("side", (0.72, 0.00, 0.18), (0.0, 0.00, 0.16))
    shoot("top",  (0.00, 0.00, 0.75), (0.0, 0.00, 0.16))
    shoot("front", (-0.08, -0.72, 0.20), (0.0, -0.04, 0.17))
print("XRAY_DONE")
