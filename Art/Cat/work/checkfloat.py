import bpy
for path in (r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_final.blend",
             r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend"):
    bpy.ops.wm.open_mainfile(filepath=path)
    rig = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
    mesh = [o for o in bpy.data.objects if o.type=='MESH'][0]
    if rig.animation_data:
        rig.animation_data.action = None
    for b in rig.pose.bones:
        b.rotation_euler=(0,0,0); b.location=(0,0,0)
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    ws = [ (mesh.matrix_world @ v.co).z for v in me.vertices ]
    print(path.split("/")[-1])
    print("  mesh min z=%.4f max z=%.4f" % (min(ws), max(ws)))
    for n in ("root","hips","thigh.L","toe.L"):
        b = rig.data.bones[n]
        print("  %-9s rest head z=%.4f  tail z=%.4f" % (n, b.head_local.z, b.tail_local.z))
    ev.to_mesh_clear()
