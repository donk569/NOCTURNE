import bpy
try:
    import numpy
    nv = numpy.__version__
except Exception as e:
    nv = "MISSING:%r" % e
print("BLENDER", bpy.app.version_string)
print("NUMPY", nv)
print("ENGINE_NOW", bpy.context.scene.render.engine)
