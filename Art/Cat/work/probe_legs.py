# -*- coding: utf-8 -*-
"""只读探针：打印腿部骨骼长度/根位置，算出「腿能伸多远」，用于确定 Walk 的合理步幅。"""
import bpy, math

SRC = r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_final.blend"
bpy.ops.wm.open_mainfile(filepath=SRC)

rig = None
mesh = None
for o in bpy.data.objects:
    if o.type == 'ARMATURE':
        rig = o
    if o.type == 'MESH':
        mesh = o

print("RIG", rig.name, "MESH", mesh.name)
print("mesh scale", tuple(mesh.scale), "rig scale", tuple(rig.scale))

for n in ("root", "hips", "spine", "chest", "neck", "head",
          "thigh.L", "shin.L", "foot.L", "toe.L",
          "upper_arm.L", "forearm.L", "hand.L", "toe.L",
          "tail.01", "tail.02", "tail.03"):
    b = rig.data.bones.get(n)
    if b is None:
        print("  MISSING", n)
        continue
    d = b.tail_local - b.head_local
    print("  %-10s len=%.4f head=(%.4f, %.4f, %.4f) tail=(%.4f, %.4f, %.4f) ang=%.1f"
          % (n, d.length, b.head_local.x, b.head_local.y, b.head_local.z,
             b.tail_local.x, b.tail_local.y, b.tail_local.z,
             math.degrees(math.atan2(d.z, d.y))))

# 现有 walk 的地面高度/腕高
print("GROUND 0.013  PAW_UP 0.040")

# 网格包围盒（绑定姿态）
mw = mesh.matrix_world
zs = [(mw @ v.co).z for v in mesh.data.vertices]
ys = [(mw @ v.co).y for v in mesh.data.vertices]
print("bind mesh z %.4f..%.4f   y %.4f..%.4f" % (min(zs), max(zs), min(ys), max(ys)))

# 腿的可达半径
def reach(a, b):
    return rig.data.bones[a].length + rig.data.bones[b].length

print("HIND reach = %.4f   FRONT reach = %.4f" % (reach("thigh.L", "shin.L"),
                                                   reach("upper_arm.L", "forearm.L")))
print("thigh.L head  y=%.4f z=%.4f" % (rig.data.bones["thigh.L"].head_local.y,
                                      rig.data.bones["thigh.L"].head_local.z))
print("upper_arm.L head y=%.4f z=%.4f" % (rig.data.bones["upper_arm.L"].head_local.y,
                                          rig.data.bones["upper_arm.L"].head_local.z))
print("shin.L tail(hock) y=%.4f z=%.4f" % (rig.data.bones["shin.L"].tail_local.y,
                                           rig.data.bones["shin.L"].tail_local.z))
print("forearm.L tail(wrist) y=%.4f z=%.4f" % (rig.data.bones["forearm.L"].tail_local.y,
                                               rig.data.bones["forearm.L"].tail_local.z))
