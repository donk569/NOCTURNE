import bpy, math, sys
from mathutils import Vector
sys.path.insert(0, r"D:/unity/NOCTURNE/Art/Cat/work")
bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend")
scene = bpy.context.scene
rig = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
pb = rig.pose.bones
if rig.animation_data is None: rig.animation_data_create()
rig.animation_data.action = bpy.data.actions.get("Cat_Idle")
try: rig.animation_data.action_slot = bpy.data.actions.get("Cat_Idle").slots[0]
except Exception: pass
scene.frame_set(1)
bpy.context.view_layer.update()
print("=== Idle f1 pose ===")
for n in ["thigh.L","shin.L","foot.L","toe.L","upper_arm.L","forearm.L","hand.L"]:
    r = [math.degrees(a) for a in pb[n].rotation_euler]
    h = pb[n].head; t = pb[n].tail
    print("  %-12s rx=%+7.2f ry=%+6.2f rz=%+6.2f  head=(%.3f,%.3f,%.3f) tail=(%.3f,%.3f,%.3f)" % (n, r[0],r[1],r[2], h.x,h.y,h.z, t.x,t.y,t.z))
print("  root loc=", tuple(pb["root"].location))
print("  toeL tail z=%.4f  toeR z=%.4f  handL z=%.4f  handR z=%.4f" % (
    (rig.matrix_world @ pb["toe.L"].tail).z, (rig.matrix_world @ pb["toe.R"].tail).z,
    (rig.matrix_world @ pb["hand.L"].tail).z, (rig.matrix_world @ pb["hand.R"].tail).z))

# 原始静止姿态对照
bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_final.blend")
rig2 = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
if rig2.animation_data:
    rig2.animation_data.action = None
for b in rig2.pose.bones:
    b.rotation_euler = (0,0,0); b.location=(0,0,0)
bpy.context.view_layer.update()
print("=== REST pose ===")
for n in ["thigh.L","shin.L","foot.L","toe.L","upper_arm.L","forearm.L","hand.L"]:
    h = rig2.pose.bones[n].head; t = rig2.pose.bones[n].tail
    print("  %-12s head=(%.3f,%.3f,%.3f) tail=(%.3f,%.3f,%.3f)" % (n, h.x,h.y,h.z, t.x,t.y,t.z))
