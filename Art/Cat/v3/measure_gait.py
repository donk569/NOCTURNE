import bpy

rig = bpy.data.objects["CatRig"]
ad = rig.animation_data
sc = bpy.context.scene

# 采样时必须屏蔽 NLA，否则 9 条轨道一起叠上去，量出来的姿势全是错的
for t in ad.nla_tracks:
    t.mute = True


def sample(action_name, frames, bones):
    act = bpy.data.actions.get(action_name)
    ad.action = act
    try:
        ad.action_slot = act.slots[0]
    except Exception:
        pass
    out = []
    for f in frames:
        sc.frame_set(f)
        bpy.context.view_layer.update()
        row = {}
        for n in bones:
            pb = rig.pose.bones.get(n)
            row[n] = tuple(rig.matrix_world @ pb.tail) if pb else (0.0, 0.0, 0.0)
        out.append((f, row))
    return out


def report(name, nframes, bones):
    rows = sample(name, list(range(1, nframes + 1)), bones)
    print(name, "frames", len(rows))
    for n2 in ("hand.L", "toe.L"):
        ys = [r[1][n2][1] for r in rows]
        zs = [r[1][n2][2] for r in rows]
        print("  %-7s Y span=%.4f (%.4f..%.4f)  Z span=%.4f (%.4f..%.4f)" % (
            n2, max(ys) - min(ys), min(ys), max(ys), max(zs) - min(zs), min(zs), max(zs)))
    hy = [r[1]["hips"][2] for r in rows]
    hz = [r[1]["head"][2] for r in rows]
    print("  hips z %.4f..%.4f span=%.4f | head z %.4f..%.4f span=%.4f" % (
        min(hy), max(hy), max(hy) - min(hy), min(hz), max(hz), max(hz) - min(hz)))
    return rows


bones = ["hand.L", "hand.R", "toe.L", "toe.R", "hips", "head", "root"]
report("Cat_Walk", 25, bones)
report("Cat_Run", 15, bones)
report("Cat_Idle", 20, bones)
