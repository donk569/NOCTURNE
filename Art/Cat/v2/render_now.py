import os
import sys

try:
    HERE = SCRIPTS_DIR
except NameError:
    HERE = os.path.dirname(os.path.abspath(__file__))

try:
    P = PARAMS
except NameError:
    P = {}

sys.path.insert(0, HERE)
import importlib

import preview
importlib.reload(preview)

preview.setup_render(res=int(P.get("res", 760)), samples=int(P.get("samples", 16)))
preview.add_lights()
preview.add_ground()
if int(P.get("clay", 1)):
    preview.apply_clay()
tag = str(P.get("tag", "clay"))
print("RENDERED", preview.four_views(os.path.join(HERE, "out"), tag))
