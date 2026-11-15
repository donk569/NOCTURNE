# 猫四视图渲染（自定义机位，适配 0.32m 小体型）
import bpy, os, math, mathutils

scene = bpy.context.scene
out = r"D:/unity/NOCTURNE/Art/Cat/work/renders"
os.makedirs(out, exist_ok=True)

mesh = None
for o in bpy.data.objects:
    if o.type == 'MESH':
        mesh = o
        break
if mesh is None:
    print("NO_MESH")
else:
    bb = [mesh.matrix_world @ mathutils.Vector(c) for c in mesh.bound_box]
    xs = [v.x for v in bb]; ys = [v.y for v in bb]; zs = [v.z for v in bb]
    center = mathutils.Vector(((min(xs)+max(xs))/2, (min(ys)+max(ys))/2, (min(zs)+max(zs))/2))
    print("BBOX", [round(v,3) for v in (min(xs),max(xs),min(ys),max(ys),min(zs),max(zs))])

    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 640
    scene.render.resolution_y = 640
    scene.render.film_transparent = False

    def shoot(name, offset, look=None):
        cam_data = bpy.data.cameras.new("cam_" + name)
        cam = bpy.data.objects.new("cam_" + name, cam_data)
        scene.collection.objects.link(cam)
        cam.location = center + mathutils.Vector(offset)
        tgt = center if look is None else mathutils.Vector(look)
        d = tgt - cam.location
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        cam.data.lens = 70
        scene.camera = cam
        scene.render.filepath = os.path.join(out, name + ".png")
        bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(cam, do_unlink=True)

    r = 0.85
    shoot("v_negY", (0, -r, 0.10))
    shoot("v_posY", (0,  r, 0.10))
    shoot("v_posX", ( r, 0, 0.10))
    shoot("v_negX", (-r, 0, 0.10))
    shoot("v_34",   (-0.60, -0.60, 0.30))
    # 脸部特写：往头顶偏上一点
    head = (center.x, center.y, max(zs) - 0.045)
    shoot("v_face", (0, -0.35, 0.02), head)
    print("CAT_RENDER_DONE")
