# 渲染"透视骨架"：猫体半透明 + 骨骼实体，检查骨架是否贴合
import bpy, mathutils, math, sys, bmesh

MODE = "__MODE__"
BLEND = "__BLEND__"
TAG = "__TAG__"

bpy.ops.wm.open_mainfile(filepath=BLEND)

mesh = None
rig = None
for o in bpy.data.objects:
    if o.type == 'MESH':
        mesh = o
    if o.type == 'ARMATURE':
        rig = o
if mesh is None or rig is None:
    raise RuntimeError("missing mesh/rig")

# --- 猫体半透明 ---
for slot in mesh.material_slots:
    m = slot.material
    if not m:
        continue
    m.blend_method = 'BLEND' if hasattr(m, "blend_method") else m.blend_method
    try:
        m.use_backface_culling = False
    except Exception:
        pass
    if m.use_nodes:
        for n in m.node_tree.nodes:
            if n.type == 'BSDF_PRINCIPLED':
                n.inputs['Alpha'].default_value = 0.26
print("MESH_MATS", [s.material.name if s.material else None for s in mesh.material_slots])

# --- 骨骼发光材质 ---
bm_bone = bpy.data.materials.new("DBG_Bone")
bm_bone.use_nodes = True
nb = bm_bone.node_tree
for n in list(nb.nodes):
    nb.nodes.remove(n)
em = nb.nodes.new("ShaderNodeEmission")
em.inputs[0].default_value = (1.0, 0.12, 0.85, 1.0)
em.inputs[1].default_value = 4.0
out = nb.nodes.new("ShaderNodeOutputMaterial")
nb.links.new(em.outputs[0], out.inputs[0])

bm_joint = bpy.data.materials.new("DBG_Joint")
bm_joint.use_nodes = True
nj = bm_joint.node_tree
for n in list(nj.nodes):
    nj.nodes.remove(n)
em2 = nj.nodes.new("ShaderNodeEmission")
em2.inputs[0].default_value = (0.15, 1.0, 0.35, 1.0)
em2.inputs[1].default_value = 6.0
out2 = nj.nodes.new("ShaderNodeOutputMaterial")
nj.links.new(em2.outputs[0], out2.inputs[0])

# --- 为每根骨造可见几何 ---
dbg = bpy.data.collections.new("DBG_Bones")
bpy.context.scene.collection.children.link(dbg)

def make_cyl(name, mat, p0, p1, radius):
    d = p1 - p0
    L = d.length
    if L < 1e-5:
        return None
    me = bpy.data.meshes.new(name)
    bmv = bmesh.new()
    bmesh.ops.create_cone(bmv, cap_ends=True, cap_tris=False, segments=8,
                          radius1=radius, radius2=radius * 0.55, depth=L)
    bmv.to_mesh(me)
    bmv.free()
    ob = bpy.data.objects.new(name, me)
    me.materials.append(mat)
    dbg.objects.link(ob)
    ob.location = (p0 + p1) * 0.5
    ob.rotation_mode = 'QUATERNION'
    ob.rotation_quaternion = d.to_track_quat('Z', 'Y')
    return ob

def make_sphere(name, mat, p, radius):
    me = bpy.data.meshes.new(name)
    bmv = bmesh.new()
    bmesh.ops.create_uvsphere(bmv, u_segments=10, v_segments=8, radius=radius)
    bmv.to_mesh(me)
    bmv.free()
    ob = bpy.data.objects.new(name, me)
    me.materials.append(mat)
    dbg.objects.link(ob)
    ob.location = p
    return ob

n = 0
for b in rig.data.bones:
    h = rig.matrix_world @ b.head_local
    t = rig.matrix_world @ b.tail_local
    r = 0.0035 if b.name.startswith(("tail", "ear", "toe")) else 0.0055
    make_cyl("B_" + b.name, bm_bone, h, t, r)
    make_sphere("J_" + b.name, bm_joint, h, r * 1.35)
    n += 1
print("DBG_BONES_BUILT", n)

# --- 地面 ---
me = bpy.data.meshes.new("Ground")
me.from_pydata([(-1, -1, 0), (1, -1, 0), (1, 1, 0), (-1, 1, 0)], [], [(0, 1, 2, 3)])
me.update()
g = bpy.data.objects.new("Ground", me)
bpy.context.scene.collection.objects.link(g)
gm = bpy.data.materials.new("GM")
gm.use_nodes = True
for nn in gm.node_tree.nodes:
    if nn.type == 'BSDF_PRINCIPLED':
        nn.inputs['Base Color'].default_value = (0.05, 0.05, 0.06, 1)
        nn.inputs['Roughness'].default_value = 1.0
g.data.materials.append(gm)

# --- 灯光 ---
def lamp(name, loc, energy, color=(1, 1, 1), size=1.0):
    ld = bpy.data.lights.new(name, 'AREA')
    ld.energy = energy
    ld.color = color
    ld.size = size
    ob = bpy.data.objects.new(name, ld)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = loc
    return ob

k = lamp("K", (0.7, -0.7, 0.7), 90, (1.0, 0.98, 0.92), 1.2)
k.rotation_mode = 'QUATERNION'
k.rotation_quaternion = (mathutils.Vector((0, 0, 0)) - k.location).to_track_quat('-Z', 'Y')
f = lamp("F", (-0.8, -0.4, 0.35), 45, (0.7, 0.8, 1.0), 1.5)
f.rotation_mode = 'QUATERNION'
f.rotation_quaternion = (mathutils.Vector((0, 0, 0.14)) - f.location).to_track_quat('-Z', 'Y')

# --- 相机 ---
cd = bpy.data.cameras.new("Cam")
cd.lens = 52
cam = bpy.data.objects.new("Cam", cd)
bpy.context.scene.collection.objects.link(cam)
bpy.context.scene.camera = cam

scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 900
scene.render.resolution_y = 700
scene.render.film_transparent = False
try:
    scene.eevee.taa_render_samples = 32
except Exception:
    pass
scene.view_settings.view_transform = 'Standard'

def shoot(name, loc, look, lens=52):
    cam.location = loc
    cam.data.lens = lens
    cam.rotation_mode = 'QUATERNION'
    cam.rotation_quaternion = (mathutils.Vector(look) - mathutils.Vector(loc)).to_track_quat('-Z', 'Y')
    scene.render.filepath = "D:/unity/NOCTURNE/Art/Cat/work/renders/dbg_%s_%s.png" % (TAG, name)
    bpy.ops.render.render(write_still=True)
    print("SHOT", name)

if MODE == "stand":
    shoot("side", (1.02, 0.02, 0.17), (0.0, 0.02, 0.14), 55)
    shoot("q34", (0.72, -0.66, 0.40), (0.0, -0.02, 0.13), 55)
else:
    shoot("side", (0.78, 0.02, 0.17), (0.0, 0.02, 0.15), 50)
    shoot("q34", (0.58, -0.54, 0.34), (0.0, -0.02, 0.15), 50)
print("DBG_DONE")
