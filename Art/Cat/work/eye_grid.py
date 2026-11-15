import bpy, mathutils, math
from mathutils.bvhtree import BVHTree

bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend")
mesh = [o for o in bpy.data.objects if o.type=='MESH'][0]
me = mesh.data; mw = mesh.matrix_world
verts = [mw @ v.co for v in me.vertices]
polys = [tuple(p.vertices) for p in me.polygons]
bvh = BVHTree.FromPolygons(verts, polys, all_triangles=False, epsilon=0.0)
D = mathutils.Vector((0,1,0))

def surf(x, z):
    hit = bvh.ray_cast(mathutils.Vector((x, -1.0, z)), D, 2.0)
    return hit[0] if hit[0] is not None else None

mat = bpy.data.materials.new("MRK"); mat.use_nodes=True
nt = mat.node_tree
for n in list(nt.nodes): nt.nodes.remove(n)
em = nt.nodes.new("ShaderNodeEmission"); em.inputs[0].default_value=(0,1,0.2,1); em.inputs[1].default_value=6
out = nt.nodes.new("ShaderNodeOutputMaterial"); nt.links.new(em.outputs[0], out.inputs[0])

XS = [0.014, 0.022, 0.030]
ZS = [0.238, 0.246, 0.254, 0.262]
for x in XS:
    for z in ZS:
        p = surf(x, z)
        if p is None: continue
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.0035, location=(p.x, p.y-0.002, p.z))
        ob = bpy.context.active_object
        ob.data.materials.append(mat)

# 相机：正前方
cam_d = bpy.data.cameras.new("c"); cam_d.lens = 85
cam = bpy.data.objects.new("c", cam_d); bpy.context.scene.collection.objects.link(cam)
bpy.context.scene.camera = cam
target = mathutils.Vector((0, -0.225, 0.248))
cam.location = (0.0, -0.85, 0.255)
cam.rotation_mode='QUATERNION'
cam.rotation_quaternion = (target - cam.location).to_track_quat('-Z','Y')

ld = bpy.data.lights.new("k",'AREA'); ld.energy=120; ld.size=1.5
lo = bpy.data.objects.new("k", ld); bpy.context.scene.collection.objects.link(lo)
lo.location=(0.5,-1.0,0.8); lo.rotation_mode='QUATERNION'
lo.rotation_quaternion=(target-mathutils.Vector(lo.location)).to_track_quat('-Z','Y')

sc = bpy.context.scene
sc.render.engine='BLENDER_EEVEE'
sc.render.resolution_x=900; sc.render.resolution_y=900
sc.view_settings.view_transform='Standard'
sc.render.filepath = r"D:/unity/NOCTURNE/Art/Cat/work/renders/eye_grid.png"
bpy.ops.render.render(write_still=True)
print("GRID_DONE")
