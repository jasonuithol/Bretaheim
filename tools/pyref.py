#!/usr/bin/env python3
"""Golden values from the SCUM terrain generator's reader (generate_terrain.read_u5), which the C#
WorldMap/Location readers must reproduce. Prints only hashes and coordinates.

Usage: python3 tools/pyref.py [U5 dir] [path/to/generate_terrain.py] > tests/Bretaheim.Tests/golden/pyref.tsv
"""
import importlib.util, sys
from pathlib import Path

u5 = Path(sys.argv[1] if len(sys.argv) > 1 else Path.home() / "Projects/Ultima 5")
gt = Path(sys.argv[2] if len(sys.argv) > 2 else
          "/media/jason/FABA03B0BA036889/Users/jason/Desktop/Projects/SCUM-Mods/Mods/Britannia/tools/generate_terrain.py")
spec = importlib.util.spec_from_file_location("generate_terrain", gt)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

def fnv1a64(data: bytes) -> int:
    h = 0xcbf29ce484222325
    for b in data:
        h ^= b
        h = (h * 0x100000001b3) & 0xFFFFFFFFFFFFFFFF
    return h

tiles, named, _ = mod.read_u5(u5)
raw = tiles.tobytes()
print(f"brit\ttiles\t{len(raw)}\t{fnv1a64(raw):016x}")
for i, (_, x, y) in enumerate(named):
    print(f"location\t{i + 1}\t{x}\t{y}")
