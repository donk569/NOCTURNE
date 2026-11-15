# -*- coding: utf-8 -*-
"""逐帧自检：穿地、根节点位移、循环首尾一致性、帧率。"""
import bpy, sys

BLEND = r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend"
LOOPING = {"Cat_Idle", "Cat_Walk", "Cat_Eat", "Cat_Drink", "Cat_Alert"}
ONESHOT = {"Cat_Sit", "Cat_Sleep", "Cat_Pounce"}
GROUND_TOL = -0.006      # 允许的最大下沉（米）
HEAD_TOL = -0.006

bpy.ops.wm.open_mainfile(filepath=BLEND)
sc = bpy.context.scene
rig = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
pb = rig.pose.bones
if rig.animation_data is None:
    rig.animation_data_create()

print("FPS", sc.render.fps)
print("%-12s %5s %8s %8s %8s %8s %9s" % ("ACTION", "FR", "minPawZ", "minHeadZ", "rootY", "rootX", "loopDelta"))
FAIL = []

for name in ["Cat_Idle", "Cat_Walk", "Cat_Sit", "Cat_Sleep",
             "Cat_Eat", "Cat_Drink", "Cat_Alert", "Cat_Pounce"]:
    act = bpy.data.actions.get(name)
    if act is None:
        FAIL.append(name + ": missing")
        continue
    rig.animation_data.action = act
    try:
        rig.animation_data.action_slot = act.slots[0]
    except Exception:
        pass
    f0, f1 = int(act.frame_start), int(act.frame_end)

    min_paw, min_head = 9e9, 9e9
    worst_paw_f = worst_head_f = -1
    max_root_y = max_root_x = 0.0
    first_pose = last_pose = None

    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        bpy.context.view_layer.update()
        paws = []
        for n in ("toe.L", "toe.R", "hand.L", "hand.R"):
            paws.append((rig.matrix_world @ pb[n].tail).z)
        pz = min(paws)
        hz = (rig.matrix_world @ pb["head"].tail).z
        if pz < min_paw:
            min_paw, worst_paw_f = pz, f
        if hz < min_head:
            min_head, worst_head_f = hz, f
        rl = pb["root"].location
        max_root_y = max(max_root_y, abs(rl.y))
        max_root_x = max(max_root_x, abs(rl.x))
        snap = tuple(round(v, 5) for b in pb for v in b.rotation_euler)
        if f == f0:
            first_pose = snap
        if f == f1:
            last_pose = snap

    loop_delta = 0.0
    if name in LOOPING and first_pose and last_pose:
        loop_delta = max(abs(a - b) for a, b in zip(first_pose, last_pose))

    print("%-12s %5d %8.4f %8.4f %8.4f %8.4f %9.5f" % (
        name, f1 - f0 + 1, min_paw, min_head, max_root_y, max_root_x, loop_delta))

    if min_paw < GROUND_TOL:
        FAIL.append("%s 脚穿地 %.4f @f%d" % (name, min_paw, worst_paw_f))
    if min_head < HEAD_TOL:
        FAIL.append("%s 头穿地 %.4f @f%d" % (name, min_head, worst_head_f))
    if max_root_y > 1e-4 or max_root_x > 1e-4:
        FAIL.append("%s 根节点有水平位移 x=%.4f y=%.4f（应原地）" % (name, max_root_x, max_root_y))
    if name in LOOPING and loop_delta > 0.02:
        FAIL.append("%s 循环首尾不一致 %.5f" % (name, loop_delta))

print("---- 结论 ----")
if FAIL:
    for f in FAIL:
        print("  FAIL:", f)
else:
    print("  全部通过")
print("VERIFY_DONE")
