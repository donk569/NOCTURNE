import bpy, numpy as np, os, sys
try: HERE=SCRIPTS_DIR
except NameError: HERE=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import importlib, paint_cat as PC
importlib.reload(PC)
ob = bpy.data.objects["Eye_R"]
pos, nrm, uv = PC.get_loops(ob)
print("uv range", uv.min(axis=0), uv.max(axis=0), "loops", len(uv))
me = ob.data
me.calc_loop_triangles()
print("tris", len(me.loop_triangles))
lo = np.empty((len(me.loop_triangles),3), dtype=np.int32)
me.loop_triangles.foreach_get("loops", lo.ravel())
print("loop idx max", lo.max(), "nloops", len(me.loops))
tuv = uv[lo]
areas = []
for t in range(0, len(tuv), max(1,len(tuv)//8)):
    a = tuv[t]
    print(t, a.round(3).tolist())
