import bpy, os
out_dir = r"D:/unity/NOCTURNE/Assets/Art/Cat/Textures"
os.makedirs(out_dir, exist_ok=True)
saved = []
for img in bpy.data.images:
    if img.size[0] == 0: continue
    name = img.name.replace("/", "_").replace("\\", "_")
    path = os.path.join(out_dir, "T_Cat_" + name + ".png")
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    saved.append((name, img.size[0], img.size[1], path))
print("TEX_SAVED", saved)
