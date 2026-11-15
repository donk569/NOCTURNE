"""Dump the decimated cat to plain arrays so the UV->3D map can be rasterised
without Blender/Cycles (the Cycles bake leaves holes in some islands).

    blender --background --python dump_mesh.py
"""
import os

import bpy
import numpy as np

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, "base_dec.blend"))
ob = bpy.data.objects["CatBody"]
me = ob.data

# bake object transform into the vertex positions
mw = np.array(ob.matrix_world, dtype=np.float64)
co = np.array([v.co[:] for v in me.vertices], dtype=np.float64)
co = (mw[:3, :3] @ co.T).T + mw[:3, 3]
nrm = np.array([v.normal[:] for v in me.vertices], dtype=np.float64)
nrm = (mw[:3, :3] @ nrm.T).T
ln = np.linalg.norm(nrm, axis=1, keepdims=True)
nrm = nrm / np.maximum(ln, 1e-9)

uvl = me.uv_layers.active.data
tris_uv = []
tris_idx = []
for p in me.polygons:
    li = list(p.loop_indices)
    for i in range(1, len(li) - 1):
        tris_uv.append([uvl[li[0]].uv[:], uvl[li[i]].uv[:], uvl[li[i + 1]].uv[:]])
        tris_idx.append([me.loops[li[0]].vertex_index,
                         me.loops[li[i]].vertex_index,
                         me.loops[li[i + 1]].vertex_index])
tris_uv = np.array(tris_uv, dtype=np.float64)
tris_idx = np.array(tris_idx, dtype=np.int32)
print("[DUMP] verts", len(co), "tris", len(tris_uv))
np.save(os.path.join(OUT, "m_verts.npy"), co.astype(np.float32))
np.save(os.path.join(OUT, "m_norm.npy"), nrm.astype(np.float32))
np.save(os.path.join(OUT, "m_tris.npy"), tris_idx)
np.save(os.path.join(OUT, "m_uv.npy"), tris_uv.astype(np.float32))
print("[DUMP] done")
