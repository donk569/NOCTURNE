# -*- coding: utf-8 -*-
"""只读：量 Cat_Sleep（开场趴卧）这一帧网格的最低点，用来核对开场自动贴地的位移量。
Unity 里 catRoot.y = 0.712（床面），脚本把 root 下移 delta = 床面 - (root.y + minZ)。
"""
import bpy

SRC = r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend"
bpy.ops.wm.open_mainfile(filepath=SRC)

rig = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
mesh = [o for o in bpy.data.objects if o.type == 'MESH'][0]
scene = bpy.context.scene

for name in ("Cat_Sleep", "Cat_Idle", "Cat_Sit"):
    act = bpy.data.actions.get(name)
    if act is None:
        print("MISSING", name); continue
    rig.animation_data.action = act
    rig.animation_data.action_slot = act.slots[0] if hasattr(act, "slots") and len(act.slots) else None
    scene.frame_set(int(act.frame_range[0]) + 5)
    bpy.context.view_layer.update()

    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    zs = [(mesh.matrix_world @ v.co).z for v in me.vertices]
    ev.to_mesh_clear()

    root = rig.pose.bones["root"]
    print("%-10s mesh minZ=%.4f maxZ=%.4f   root.loc=(%.4f, %.4f, %.4f)  root.head.z=%.4f"
          % (name, min(zs), max(zs), root.location.x, root.location.y, root.location.z,
             (rig.matrix_world @ root.head).z))
    print("            -> 放床上需下移 %.4f m" % (-min(zs)))
