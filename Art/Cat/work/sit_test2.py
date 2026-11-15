# 坐姿重试：正确符号 + 地面参考
import bpy, os, math, mathutils

scene = bpy.context.scene
out = r"D:/unity/NOCTURNE/Art/Cat/work/renders"
rig = None; mesh = None
for o in bpy.data.objects:
    if o.type == 'ARMATURE': rig = o
    if o.type == 'MESH': mesh = o

# 地面
if not bpy.data.objects.get("GroundRef"):
    bpy.ops.mesh.primitive_plane_add(size=3, location=(0, 0.1, 0))
    g = bpy.context.active_object
    g.name = "GroundRef"
    m = bpy.data.materials.new("GroundMat")
    m.use_nodes = True
    bsdf = [n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'][0]
    bsdf.inputs['Base Color'].default_value = (0.28, 0.28, 0.30, 1)
    bsdf.inputs['Roughness'].default_value = 0.9
    g.data.materials.append(m)

scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 640
scene.render.resolution_y = 640

center = mathutils.Vector((0, 0.05, 0.12))
cam = bpy.data.objects.get("posecam_obj")
scene.camera = cam
cam.data.lens = 50
cam.location = center + mathutils.Vector((1.05, -0.25, 0.16))
cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()

pb = rig.pose.bones
for b in pb: b.rotation_mode = 'XYZ'

def reset():
    for b in pb:
        b.rotation_euler = (0, 0, 0); b.location = (0, 0, 0)

def rot(n, x=0, y=0, z=0):
    if n in pb: pb[n].rotation_euler = (math.radians(x), math.radians(y), math.radians(z))

def sit(name, thigh, shin, foot, rootz):
    reset()
    pb["root"].location = (0, 0, rootz)
    for s in (".L", ".R"):
        rot("thigh" + s, thigh, 0, 0)
        rot("shin" + s, shin, 0, 0)
        rot("foot" + s, foot, 0, 0)
        rot("upper_arm" + s, 6, 0, 0)
    rot("spine", -6)
    rot("chest", -3)
    rot("neck", 12)
    rot("head", -8)
    rot("tail.01", -30, 0, -20)
    rot("tail.02", -15, 0, -35)
    rot("tail.03", -5, 0, -35)
    bpy.context.view_layer.update()
    scene.render.filepath = os.path.join(out, name + ".png")
    bpy.ops.render.render(write_still=True)

sit("sitD", -70, 110, -40, -0.085)
sit("sitE", -55, 95, -35, -0.075)
reset()
print("SIT2_DONE")
