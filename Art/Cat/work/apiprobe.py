import bpy
print("VER", bpy.app.version_string)
bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_final.blend")
rig = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
if rig.animation_data is None: rig.animation_data_create()
act = bpy.data.actions.new("PROBE")
rig.animation_data.action = act
pb = rig.pose.bones
pb["head"].rotation_mode='XYZ'
pb["head"].rotation_euler = (0.1,0,0)
pb["head"].keyframe_insert("rotation_euler", frame=1)
print("act attrs:", [a for a in dir(act) if not a.startswith('_')][:40])
print("has layers:", hasattr(act,"layers"), "n=", len(act.layers) if hasattr(act,"layers") else -1)
if hasattr(act,"layers") and len(act.layers):
    L = act.layers[0]
    print("layer attrs:", [a for a in dir(L) if not a.startswith('_')][:25])
    print("strips:", len(L.strips))
    if len(L.strips):
        S = L.strips[0]
        print("strip attrs:", [a for a in dir(S) if not a.startswith('_')][:30])
        print("channelbags:", len(S.channelbags) if hasattr(S,"channelbags") else "n/a")
        if hasattr(S,"channelbags") and len(S.channelbags):
            cb = S.channelbags[0]
            print("cb fcurves:", len(cb.fcurves), "path=", cb.fcurves[0].data_path if len(cb.fcurves) else "")
            print("kp:", len(cb.fcurves[0].keyframe_points))
print("slots:", len(act.slots) if hasattr(act,"slots") else "n/a")
