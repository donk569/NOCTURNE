"""Contact sheet: reference photos (top-left) next to the Unity renders."""
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
REF = os.path.join(HERE, "..", "work", "ref")

CELL = 620
COLS = 3
ORDER = [
    (os.path.join(REF, "cat_side.png"), "REF SIDE"),
    (os.path.join(REF, "cat_front.png"), "REF FRONT"),
    (os.path.join(OUT, "show_Cat_Idle.png"), "UNITY  IDLE"),
    (os.path.join(OUT, "show_Cat_Walk.png"), "UNITY  WALK"),
    (os.path.join(OUT, "show_Cat_Sit.png"), "UNITY  SIT (loaf)"),
    (os.path.join(OUT, "show_Cat_Sleep.png"), "UNITY  SLEEP"),
    (os.path.join(OUT, "show_Cat_Eat.png"), "UNITY  EAT"),
    (os.path.join(OUT, "show_Cat_Alert.png"), "UNITY  ALERT"),
    (os.path.join(OUT, "unity_Cat_Sit.png"), "IN SCENE (bed)"),
]


def main():
    tiles = []
    for p, label in ORDER:
        if not os.path.exists(p):
            print("MISS", p)
            continue
        im = Image.open(p).convert("RGB")
        im.thumbnail((CELL, CELL), Image.LANCZOS)
        c = Image.new("RGB", (CELL, CELL), (24, 26, 30))
        c.paste(im, ((CELL - im.width) // 2, (CELL - im.height) // 2))
        ImageDraw.Draw(c).text((14, 12), label, fill=(240, 205, 120))
        tiles.append(c)
    rows = (len(tiles) + COLS - 1) // COLS
    sheet = Image.new("RGB", (COLS * CELL, rows * CELL), (16, 17, 20))
    for i, t in enumerate(tiles):
        sheet.paste(t, ((i % COLS) * CELL, (i // COLS) * CELL))
    dst = os.path.join(OUT, "SHEET_v3_unity.png")
    sheet.save(dst)
    print("SHEET", dst, sheet.size)


main()
