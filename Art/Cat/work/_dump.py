import bpy, mathutils
bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_fit.blend")
rig = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
print("=== FINAL BONES ===")
for b in rig.data.bones:
    h = rig.matrix_world @ b.head_local
    t = rig.matrix_world @ b.tail_local
    print("  %-12s (%+.4f,%+.4f,%+.4f) -> (%+.4f,%+.4f,%+.4f) len=%.4f" % (b.name,h.x,h.y,h.z,t.x,t.y,t.z,(t-h).length))
