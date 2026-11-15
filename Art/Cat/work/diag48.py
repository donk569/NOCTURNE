import bpy, math
bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend")
sc = bpy.context.scene
rig = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
pb = rig.pose.bones
act = bpy.data.actions.get("Cat_Sit")
rig.animation_data.action = act
try: rig.animation_data.action_slot = act.slots[0]
except Exception as e: print("slot err", e)
for f in (1, 38, 48):
    sc.frame_set(f)
    bpy.context.view_layer.update()
    print("=== Sit frame", f, "root=", tuple(round(v,4) for v in pb["root"].location))
    for n in ["hips","spine","chest","thigh.L","shin.L","foot.L","toe.L","upper_arm.L","forearm.L","hand.L"]:
        e = [math.degrees(a) for a in pb[n].rotation_euler]
        h = pb[n].head; t = pb[n].tail
        print("   %-11s rx=%+7.1f  head=(%+.3f,%+.3f,%+.3f) tail=(%+.3f,%+.3f,%+.3f)" % (n, e[0], h.x,h.y,h.z, t.x,t.y,t.z))
