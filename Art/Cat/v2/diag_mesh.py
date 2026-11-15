import bpy, bmesh, os, sys
try: HERE=SCRIPTS_DIR
except NameError: HERE=os.path.dirname(os.path.abspath(__file__))
ob = bpy.data.objects.get("CatFur")
bm = bmesh.new(); bm.from_mesh(ob.data)
nm = [e for e in bm.edges if not e.is_manifold]
# shell count via flood fill
seen=set(); shells=0
for f in bm.faces:
    if f.index in seen: continue
    shells+=1; stack=[f]
    while stack:
        cur=stack.pop()
        if cur.index in seen: continue
        seen.add(cur.index)
        for e in cur.edges:
            for nf in e.link_faces:
                if nf.index not in seen: stack.append(nf)
print("TRIS", len(bm.faces), "VERTS", len(bm.verts), "NONMANIFOLD_EDGES", len(nm), "SHELLS", shells)
print("OBJECTS", [o.name for o in bpy.data.objects if o.type=="MESH"])
bm.free()
