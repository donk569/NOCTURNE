import bpy, math, sys
from mathutils import Vector
sys.path.insert(0, r"D:/unity/NOCTURNE/Art/Cat/work")
import catik
bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend")
rig = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
if rig.animation_data: rig.animation_data.action = None
pb = rig.pose.bones
for b in pb: b.rotation_mode='XYZ'
REST = catik.RestInfo(rig)

def probe(root_h, hips, spine, chest):
    for b in pb:
        b.rotation_euler=(0,0,0); b.location=(0,0,0)
    for n,v in (("hips",hips),("spine",spine),("chest",chest)):
        pb[n].rotation_euler=(math.radians(v),0,0)
    pb["root"].location=(0,0,root_h)
    bpy.context.view_layer.update()
    sh = pb["upper_arm.L"].head
    hp = pb["thigh.L"].head
    body = pb["hips"].head
    return sh.z, hp.z, body.z, sh.y, hp.y

print("root_h hips spine chest | shoulder_z hip_z body_z")
for rh in (-0.03, -0.05, -0.07, -0.09, -0.11):
    for hx in (0, -10, -20, -30, -40):
        sh, hp, bd, shy, hpy = probe(rh, hx, -6, 0)
        print("%+.3f  %+3d  %+3d   %+3d | sh_z=%.3f hip_z=%.3f body_z=%.3f" % (rh, hx, -6, 0, sh, hp, bd))
