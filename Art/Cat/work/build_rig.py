# 阶段二：四足骨架 + 包络蒙皮
import bpy, sys, math, mathutils
sys.path.insert(0, r"C:/Users/lenovo/.workbuddy/skills/blender-character-pipeline/scripts")
import measure, archetypes, rig_build, weights

mesh = None
for o in bpy.data.objects:
    if o.type == 'MESH':
        mesh = o
        break

m = measure.measure(mesh)
arch, why = measure.guess_archetype(m)
print("ARCH", arch, why)

spec = archetypes.build_spec(m, arch, drop=())
rig = rig_build.build_armature(spec, name="CatRig")
rig_build.attach_mesh(mesh, rig)
stats = weights.auto_weights(mesh, rig, spec)
print("WEIGHTS", {k: (round(v, 3) if isinstance(v, float) else v) for k, v in stats.items()})

feas = measure.feasibility(m, spec)
print("FEAS", {k: (round(v, 3) if isinstance(v, float) else v) for k, v in feas.items()})

# 骨骼清单
print("BONES", [b.name for b in rig.data.bones])

# 保存阶段文件
bpy.ops.wm.save_as_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_rig.blend")
print("SAVED_RIG_BLEND")
