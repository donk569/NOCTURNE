import bpy
bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_sit_final.blend")
# 坐姿动画关键帧预览
import bpy, os, math, mathutils

scene = bpy.context.scene
out = r"D:/unity/NOCTURNE/Art/Cat/work/renders"
rig = None; mesh = None
for o in bpy.data.objects:
    if o.type == 'ARMATURE': rig = o
    if o.type == 'MESH': mesh = o

if not bpy.data.objects.get("GroundRef"):
    bpy.ops.mesh.primitive_plane_add(size=3, location=(0, 0, 0))
    g = bpy.context.active_object; g.name = "GroundRef"
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
scene.render.resolution_x = 640
scene.render.resolution_y = 720

center = mathutils.Vector((0, -0.02, 0.16))
cam = bpy.data.objects.get("posecam_obj")
if cam is None:
    cd = bpy.data.cameras.new("posecam"); cam = bpy.data.objects.new("posecam_obj", cd)
    scene.collection.objects.link(cam)
scene.camera = cam
cam.data.lens = 55
cam.location = center + mathutils.Vector((0.55, -0.95, 0.16))
cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()

if rig.animation_data is None:
    rig.animation_data_create()

shots = [("Idle_Stare", 60, "anim_idle_f60"), ("Idle_Stare", 1, "anim_idle_f1"),
         ("Alert", 16, "anim_alert"), ("Sleep", 60, "anim_sleep")]
for act_name, frame, fname in shots:
    act = bpy.data.actions.get(act_name)
    rig.animation_data.action = act
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    scene.render.filepath = os.path.join(out, fname + ".png")
    bpy.ops.render.render(write_still=True)
rig.animation_data.action = bpy.data.actions.get("Idle_Stare")
scene.frame_set(1)
print("ANIM_PREVIEW_DONE")
