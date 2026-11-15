import bpy, mathutils
bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_final.blend")
rig = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
names = ["root","hips","spine","chest","neck","head","ear.L","upper_arm.L","forearm.L","hand.L","thigh.L","shin.L","foot.L","toe.L","tail.01","tail.02","tail.03"]
for n in names:
    b = rig.data.bones.get(n)
    if not b: 
        print(n, "MISSING"); continue
    m = b.matrix_local.to_3x3()
    x = m.col[0]; y = m.col[1]; z = m.col[2]
    d = (b.tail_local - b.head_local).normalized()
    print("%-12s dir=(%+.2f,%+.2f,%+.2f) X=(%+.2f,%+.2f,%+.2f) Y=(%+.2f,%+.2f,%+.2f) Z=(%+.2f,%+.2f,%+.2f) len=%.3f" % (
        n, d.x,d.y,d.z, x.x,x.y,x.z, y.x,y.y,y.z, z.x,z.y,z.z, b.length))
