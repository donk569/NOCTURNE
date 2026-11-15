"""Animation QA: per-frame clearance plus a few key frames rendered."""
import os
import sys

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
sys.path.insert(0, os.path.join(HERE, "..", "v2"))
import preview  # noqa: E402


def log(*a):
    print("[QA]", *a)
    sys.stdout.flush()


def main():
    bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, "cat_anim.blend"))
    rig = bpy.data.objects["CatRig"]
    mesh = bpy.data.objects["CatBody"]
    ad = rig.animation_data
    ad.action = None
    preview.setup_render(res=560, samples=14)
    preview.add_lights(power=1.0)
    preview.add_ground()

    want = os.environ.get("QA_CLIPS", "Cat_Walk,Cat_Run,Cat_Sit,Cat_Sleep,Cat_Eat,Cat_Idle")
    for name in want.split(","):
        act = bpy.data.actions.get(name)
        if act is None:
            log("MISSING", name)
            continue
        ad.action = act
        try:
            ad.action_slot = act.slots[0]
        except Exception:
            pass
        f0, f1 = int(act.frame_start), int(act.frame_end)
        mins = []
        for f in range(f0, f1 + 1):
            bpy.context.scene.frame_set(f)
            dg = bpy.context.evaluated_depsgraph_get()
            me = mesh.evaluated_get(dg).to_mesh()
            co = np.array([v.co[:] for v in me.vertices], dtype=np.float32)
            mins.append(float(co[:, 2].min()))
            mesh.evaluated_get(dg).to_mesh_clear()
        mins = np.array(mins)
        # loop closure: first vs last frame bone rotations
        log("%-11s frames=%d min_z=%.4f (worst f=%d)  loop_gap=%.4f"
            % (name, f1 - f0 + 1, mins.min(), f0 + int(mins.argmin()),
               abs(mins[-1] - mins[0])))
        if os.environ.get("QA_RENDER_TAG"):
            for k in (0, 0.34, 0.67):
                f = int(f0 + (f1 - f0) * k)
                bpy.context.scene.frame_set(f)
                preview.render_view("k%d" % k, (0.55, 0.0, 0.16), (0, 0, 0.15),
                                    os.path.join(OUT, "%s_%s_f%02d.png"
                                                 % (os.environ["QA_RENDER_TAG"], name, f)),
                                    ortho=0.70)
    ad.action = None
    log("DONE")


main()
