import bpy, numpy as np, os, sys
try: HERE=SCRIPTS_DIR
except NameError: HERE=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import importlib, paint_cat as PC
importlib.reload(PC)
ob = bpy.data.objects["Eye_R"]
pos, nrm, uv = PC.get_loops(ob)
EP, EN, EM = PC.rasterise(ob, 128, pos, nrm, uv)
f = EP[EM]
print("mask", EM.sum(), "sample pos", f[:3])
ax = np.array(list(ob["eye_axis"]), dtype=np.float32)
vco = np.empty(len(ob.data.vertices)*3, dtype=np.float32)
ob.data.vertices.foreach_get("co", vco); vco=vco.reshape(-1,3)
ec = (vco.min(axis=0)+vco.max(axis=0))*0.5
d = f - ec; d = d/(np.linalg.norm(d,axis=1)[:,None]+1e-9)
ang = np.degrees(np.arccos(np.clip(d@ax,-1,1)))
print("ang pct", np.percentile(ang,[1,5,25,50,75,99]))
col = PC.eye_albedo(f, ax, ec)
print("colmax", float(col.max()), "n_iris", int((ang<40).sum()))
