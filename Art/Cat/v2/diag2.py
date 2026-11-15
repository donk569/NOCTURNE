import bpy, bmesh, os, sys
try: HERE=SCRIPTS_DIR
except NameError: HERE=os.path.dirname(os.path.abspath(__file__))
ob = bpy.data.objects.get("CatFur")
bm = bmesh.new(); bm.from_mesh(ob.data); bm.faces.ensure_lookup_table()
seen=set(); shells=[]
for f in bm.faces:
    if f.index in seen: continue
    comp=[]; stack=[f]
    while stack:
        cur=stack.pop()
        if cur.index in seen: continue
        seen.add(cur.index); comp.append(cur)
        for e in cur.edges:
            for nf in e.link_faces:
                if nf.index not in seen: stack.append(nf)
    shells.append(comp)
for i, comp in enumerate(shells):
    vs=set()
    for f in comp:
        for v in f.verts: vs.add(v)
    xs=[v.co.x for v in vs]; ys=[v.co.y for v in vs]; zs=[v.co.z for v in vs]
    print("SHELL %d faces=%d  x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]" % (i, len(comp), min(xs),max(xs),min(ys),max(ys),min(zs),max(zs)))
bm.free()
