import os

p = r"D:\unity\NOCTURNE\Art\Cat\v3\export_cat.py"
g = {"__name__": "__main__", "__file__": p}
with open(p, encoding="utf-8") as fh:
    exec(compile(fh.read(), p, "exec"), g)
print("EXPORT DONE")
