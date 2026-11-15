# 站姿动画预览：Walk 四相位 + Jump 四阶段
import bpy, os, math, mathutils, sys
sys.path.insert(0, r"C:/Users/lenovo/.workbuddy/skills/blender-character-pipeline/scripts")
import ctx

bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_final.blend")
try: ctx.ensure_window()
except Exception: pass

scene = bpy.context.scene
out = r"D:/unity/NOCTURNE/Art/Cat/work/renders"
os.makedirs(out, exist_ok=True)

rig = None; mesh = None
for o in bpy.data.objects:
    if o.type == 'ARMATURE': rig = o
    if o.type == 'MESH': mesh = o

if not bpy.data.objects.get("GroundRef"):
    me = bpy.data.meshes.new("GroundMesh")
    me.from_pydata([(-2,-2,0),(2,-2,0),(2,2,0),(-2,2,0)], [], [(0,1,2,3)])
    me.update()
    g = bpy.data.objects.new("GroundRef", me)
    g.location = (0, 0.1, 0)
    bpy.context.scene.collection.objects.link(g)
    m = bpy.data.materials.new("GroundMat"); m.use_nodes = True
    bsdf = [n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'][0]
    bsdf.inputs['Base Color'].default_value = (0.25, 0.25, 0.27, 1)
    bsdf.inputs['Roughness'].default_value = 0.9
    g.data.materials.append(m)

if not any(o.type == 'LIGHT' for o in bpy.data.objects):
    l = bpy.data.lights.new("K", 'AREA'); l.energy = 60; l.size = 2
    lo = bpy.data.objects.new("K", l); scene.collection.objects.link(lo)
    lo.location = (0.9, -1.1, 1.2); lo.rotation_euler = (math.radians(58), 0, math.radians(35))

scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 720
scene.render.resolution_y = 520

center = mathutils.Vector((0, 0.10, 0.16))
cam = bpy.data.objects.get("posecam_obj")
if cam is None:
    cd = bpy.data.cameras.new("posecam"); cam = bpy.data.objects.new("posecam_obj", cd)
    scene.collection.objects.link(cam)
scene.camera = cam
cam.data.lens = 55
cam.location = center + mathutils.Vector((1.15, -0.55, 0.28))
cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()

if rig.animation_data is None:
    rig.animation_data_create()

shots = [("Walk", 1, "walk_f1"), ("Walk", 9, "walk_f9"), ("Walk", 17, "walk_f17"), ("Walk", 25, "walk_f25"),
         ("Jump", 8, "jump_f8"), ("Jump", 13, "jump_f13"), ("Jump", 18, "jump_f18"), ("Jump", 26, "jump_f26")]
for act_name, frame, fname in shots:
    act = bpy.data.actions.get(act_name)
    rig.animation_data.action = act
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    scene.render.filepath = os.path.join(out, fname + ".png")
    bpy.ops.render.render(write_still=True)
rig.animation_data.action = bpy.data.actions.get("Walk")
scene.frame_set(1)
print("STAND_PREVIEW_DONE")
