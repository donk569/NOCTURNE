import bpy
bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend")
sc=bpy.context.scene
rig=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
pb=rig.pose.bones
for actn,f in (("Cat_Sleep",60),("Cat_Sit",48),("Cat_Pounce",15),("Cat_Pounce",26)):
    act=bpy.data.actions.get(actn); rig.animation_data.action=act
    try: rig.animation_data.action_slot=act.slots[0]
    except Exception: pass
    sc.frame_set(f); bpy.context.view_layer.update()
    print("===",actn,f,"root z=%.4f"%pb["root"].location.z)
    for n in ("hand.L","hand.R","toe.L","toe.R","tail.03","head"):
        t = rig.matrix_world @ pb[n].tail
        print("   %-9s tail=(%+.3f,%+.3f,%+.3f)" % (n,t.x,t.y,t.z))
