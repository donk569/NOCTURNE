"""Compose a QA contact sheet from the v3 renders (PIL only)."""
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")

CELLS = [
    ("c9_side.png", "SIDE"),
    ("c9_q34.png", "3/4 FRONT"),
    ("c9_front.png", "FRONT"),
    ("c9_back.png", "BACK"),
    ("c9_face.png", "FACE"),
    ("cal_loaf.png", "SIT / LOAF"),
]
COLS = 3
CELL = 620


def main():
    tiles = []
    for fn, label in CELLS:
        p = os.path.join(OUT, fn)
        if not os.path.exists(p):
            print("MISS", fn)
            continue
        im = Image.open(p).convert("RGB")
        im.thumbnail((CELL, CELL), Image.LANCZOS)
        canvas = Image.new("RGB", (CELL, CELL), (22, 24, 28))
        canvas.paste(im, ((CELL - im.width) // 2, (CELL - im.height) // 2))
        d = ImageDraw.Draw(canvas)
        d.text((12, 10), label, fill=(230, 200, 130))
        tiles.append(canvas)
    rows = (len(tiles) + COLS - 1) // COLS
    sheet = Image.new("RGB", (COLS * CELL, rows * CELL), (16, 17, 20))
    for i, t in enumerate(tiles):
        sheet.paste(t, ((i % COLS) * CELL, (i // COLS) * CELL))
    dst = os.path.join(OUT, "SHEET_v3.png")
    sheet.save(dst)
    print("SHEET", dst, sheet.size)


main()
