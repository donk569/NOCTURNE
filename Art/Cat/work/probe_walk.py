# -*- coding: utf-8 -*-
"""只读：逐帧量 Cat_Walk 时四只爪的世界坐标，算出真实步幅、抬脚高度、相位差。
用来判断"脚底滑动 / 步幅过小 / 顺拐"到底是哪一条。"""
import bpy, math

SRC = r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend"
bpy.ops.wm.open_mainfile(filepath=SRC)

rig = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
scene = bpy.context.scene
fps = scene.render.fps
print("fps", fps)

act = bpy.data.actions.get("Cat_Walk")
if act is None:
    raise RuntimeError("no Cat_Walk action")

rig.animation_data.action = act
try:
    act_slot = rig.animation_data.action_slot
except Exception:
    act_slot = None

f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
print("Cat_Walk frames %d..%d  = %.3fs" % (f0, f1, (f1 - f0) / fps))

names = ["toe.L", "toe.R", "hand.L", "hand.R", "root", "hips", "head"]
data = {n: [] for n in names}

for f in range(f0, f1 + 1):
    scene.frame_set(f)
    bpy.context.view_layer.update()
    for n in names:
        b = rig.pose.bones.get(n)
        # 用骨骼尾端 = 爪尖/头鼻
        p = rig.matrix_world @ b.tail
        data[n].append((f, p.x, p.y, p.z))

for n in names:
    ys = [d[2] for d in data[n]]
    zs = [d[3] for d in data[n]]
    print("%-8s  y %.4f..%.4f (travel %.4f)   z %.4f..%.4f (lift %.4f)"
          % (n, min(ys), max(ys), max(ys) - min(ys), min(zs), max(zs), max(zs) - min(zs)))

# 支撑相：z 接近最小值的那段，看 y 怎么走
for n in ("toe.L", "hand.L"):
    zs = [d[3] for d in data[n]]
    lo, hi = min(zs), max(zs)
    th = lo + (hi - lo) * 0.25
    stance = [d for d in data[n] if d[3] <= th]
    if stance:
        print("%s 支撑相(前25%%高度内) %d 帧  y %.4f..%.4f  平均z %.4f"
              % (n, len(stance), min(d[2] for d in stance), max(d[2] for d in stance),
                 sum(d[3] for d in stance) / len(stance)))

# 相位：每只脚最低点的帧号
for n in ("toe.L", "toe.R", "hand.L", "hand.R"):
    zs = [(d[0], d[3]) for d in data[n]]
    fm = min(zs, key=lambda t: t[1])[0]
    print("%-8s 最低点在第 %d 帧 (相位 %.2f)" % (n, fm, (fm - f0) / float(f1 - f0)))

# 身体上下起伏
hz = [d[3] for d in data["hips"]]
print("hips z %.4f..%.4f  起伏 %.4f" % (min(hz), max(hz), max(hz) - min(hz)))
