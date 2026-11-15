import bpy, numpy as np, os, sys
try: HERE=SCRIPTS_DIR
except NameError: HERE=os.path.dirname(os.path.abspath(__file__))
ob = bpy.data.objects["Eye_R"]
ax = np.array(list(ob["eye_axis"]), dtype=np.float32)
vco = np.empty(len(ob.data.vertices)*3, dtype=np.float32)
ob.data.vertices.foreach_get("co", vco); vco=vco.reshape(-1,3)
ec = (vco.min(axis=0)+vco.max(axis=0))*0.5
print("axis", ax, "center", ec)
d = vco - ec
d = d/(np.linalg.norm(d,axis=1)[:,None]+1e-9)
ang = np.degrees(np.arccos(np.clip(d@ax,-1,1)))
print("ang min/med/max", ang.min(), np.median(ang), ang.max())
print("frac<40", (ang<40).mean())
print("img", [i.name for i in bpy.data.images])
