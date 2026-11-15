"""Contact sheet for this round: Play-Mode evidence + Blender gait filmstrips."""
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
WB = os.path.join(HERE, "..", "..", "..", ".workbuddy")

CELL = 430
COLS = 4
TILES = [
    ("PLAY MODE  opening", os.path.join(WB, "shot_opening.png")),
    ("PLAY MODE  bag", os.path.join(WB, "shot_bag.png")),
    ("PLAY MODE  bowl", os.path.join(WB, "shot_bowl.png")),
    ("PLAY MODE  eating", os.path.join(WB, "shot_eat.png")),
    ("BLENDER  walk 1/2", os.path.join(OUT, "film_walk_00.png")),
    ("BLENDER  walk 2/2", os.path.join(OUT, "film_walk_04.png")),
    ("BLENDER  run 1/2", os.path.join(OUT, "film_run_00.png")),
    ("BLENDER  run 2/2", os.path.join(OUT, "film_run_04.png")),
]


def main():
    tiles = []
    for label, p in TILES:
        if not os.path.exists(p):
            print("MISS", p)
            continue
        im = Image.open(p).convert("RGB")
        im.thumbnail((CELL, CELL), Image.LANCZOS)
        c = Image.new("RGB", (CELL, CELL), (24, 26, 30))
        c.paste(im, ((CELL - im.width) // 2, (CELL - im.height) // 2))
        d = ImageDraw.Draw(c)
        d.text((10, 8), label, fill=(240, 205, 120))
        d.rectangle([0, 0, CELL - 1, CELL - 1], outline=(60, 64, 70))
        tiles.append(c)
    rows = (len(tiles) + COLS - 1) // COLS
    sheet = Image.new("RGB", (COLS * CELL, rows * CELL), (16, 17, 20))
    for i, t in enumerate(tiles):
        sheet.paste(t, ((i % COLS) * CELL, (i // COLS) * CELL))
    dst = os.path.join(OUT, "SHEET_round2.png")
    sheet.save(dst)
    print("SHEET", dst, sheet.size)


main()
