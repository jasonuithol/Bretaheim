#!/usr/bin/env python3
"""Isometric render of a settlement plan (U5Dump `town <loc> <out.json>`).

Every piece with known geometry is drawn as its scaled, turned collider box; roof pieces are drawn
from their corner heights (as their snap points define them), so the orientation of slopes,
outer/inner corners, ridge caps and grausten triangles can be checked without the game.
Usage: python3 tools/render_town.py plan.json out.png [view_yaw_deg] [px_per_m] [max_height]
max_height cuts away everything above it (look inside buildings).
"""
import json, math, os, sys
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
GEO = json.load(open(os.path.join(HERE, "..", "assets", "mappings", "piece_geometry.json")))

# local triangles (x, z, y) per 26° roof piece (pivot on the low edge), from their snap points
ROOF = {
    "roof": [((-1, -1, 1), (1, -1, 1), (1, 1, 0)), ((-1, -1, 1), (1, 1, 0), (-1, 1, 0))],
    "ocorner": [((-1, -1, 1), (1, -1, 0), (1, 1, 0)), ((-1, -1, 1), (1, 1, 0), (-1, 1, 0))],
    "icorner": [((1, 1, 0), (1, -1, 1), (-1, -1, 1)), ((1, 1, 0), (-1, -1, 1), (-1, 1, 1))],
    "top": [((-1, -1, 0), (1, -1, 0), (1, 0, .5)), ((-1, -1, 0), (1, 0, .5), (-1, 0, .5)),
            ((-1, 1, 0), (1, 1, 0), (1, 0, .5)), ((-1, 1, 0), (1, 0, .5), (-1, 0, .5))],
    # grausten 45°: pivot at mid-height
    "g_slope": [((-1, -1, 1), (1, -1, 1), (1, 1, -1)), ((-1, -1, 1), (1, 1, -1), (-1, 1, -1))],
    "g_corner": [((-1, -0.5, 1), (1, -0.5, 1), (1, 1.5, -1))],
    "g_corner2": [((-1, -0.5, 1), (1, -0.5, 1), (-1, 1.5, -1))],
}
COLOUR = {"roof": (170, 70, 50), "ocorner": (200, 120, 40), "icorner": (60, 120, 200), "top": (230, 200, 60),
          "g_slope": (120, 120, 130), "g_corner": (90, 150, 200), "g_corner2": (200, 120, 160),
          "Wood": (140, 100, 60), "Stone": (175, 175, 180), "Iron": (90, 90, 100), "Marble": (40, 40, 50), "Ashstone": (110, 110, 120)}

def roof_kind(p):
    if p == "piece_grausten_roof_45": return "g_slope"
    if p == "piece_grausten_roof_45_corner": return "g_corner"
    if p == "piece_grausten_roof_45_corner2": return "g_corner2"
    if p.endswith("_ocorner"): return "ocorner"
    if p.endswith("_icorner"): return "icorner"
    if p.endswith("_roof_top"): return "top"
    if p.endswith("_roof"): return "roof"
    return None

def rot(x, z, yaw):
    r = math.radians(yaw); c, s = math.cos(r), math.sin(r)
    return x * c + z * s, -x * s + z * c

def world(p, lx, ly, lz):
    sx, sy, sz = p.get("s", [1, 1, 1])
    wx, wz = rot(lx * sx, lz * sz, p["yaw"])
    return (p["x"] + wx, p["y"] + ly * sy, p["z"] + wz)

def box_tris(p, g):
    (x0, y0, z0), (x1, y1, z1) = g["min"], g["max"]
    c = [world(p, x, y, z) for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)]
    # corners indexed by bits (x, y, z) = (4, 2, 1)
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    out = []
    for a, b, cc, d in faces:
        out += [(c[a], c[b], c[cc]), (c[a], c[cc], c[d])]
    return out

def main():
    pieces = json.load(open(sys.argv[1]))
    view = float(sys.argv[3]) if len(sys.argv) > 3 else 35
    S = int(sys.argv[4]) if len(sys.argv) > 4 else 12
    cut = float(sys.argv[5]) if len(sys.argv) > 5 else 1e9
    tris = []
    for p in pieces:
        if p["y"] > cut: continue
        k = roof_kind(p["p"])
        if k:
            for t in ROOF[k]:
                tris.append(([world(p, lx, ly, lz) for (lx, lz, ly) in t], COLOUR[k]))
            continue
        g = GEO.get(p["p"])
        if not g or "min" not in g or p["p"] == "sign": continue
        col = COLOUR.get(g.get("material"), (150, 150, 150))
        if "blackmarble" in p["p"]: col = COLOUR["Marble"]
        if "door" in p["p"] or "grate" in p["p"]: col = (90, 50, 30)
        tris += [(t, col) for t in box_tris(p, g)]
    vr = math.radians(view)
    def proj(pt):
        x, y, z = pt
        rx, rz = x * math.cos(vr) - z * math.sin(vr), x * math.sin(vr) + z * math.cos(vr)
        return rx, -(rz * 0.5) - y * 0.87, rz - y * 0.3  # screen x, screen y, depth (bigger = farther)
    projected = []
    for pts, col in tris:
        pp = [proj(q) for q in pts]
        (x1, y1, z1), (x2, y2, z2), (x3, y3, z3) = pts
        nx = (y2 - y1) * (z3 - z1) - (z2 - z1) * (y3 - y1)
        ny = (z2 - z1) * (x3 - x1) - (x2 - x1) * (z3 - z1)
        nz = (x2 - x1) * (y3 - y1) - (y2 - y1) * (x3 - x1)
        n = math.sqrt(nx * nx + ny * ny + nz * nz) or 1
        light = abs((nx * 0.4 + ny * 0.8 - nz * 0.45) / n)
        shade = 0.45 + 0.55 * light
        projected.append((max(q[2] for q in pp) + min(q[2] for q in pp), [(q[0], q[1]) for q in pp], tuple(int(c * shade) for c in col)))
    xs = [q[0] for _, pts, _ in projected for q in pts]; ys = [q[1] for _, pts, _ in projected for q in pts]
    W, H = int((max(xs) - min(xs)) * S) + 40, int((max(ys) - min(ys)) * S) + 40
    img = Image.new("RGB", (W, H), (30, 40, 30)); d = ImageDraw.Draw(img)
    for _, pts, col in sorted(projected, key=lambda t: -t[0]):
        d.polygon([((x - min(xs)) * S + 20, (y - min(ys)) * S + 20) for x, y in pts], fill=col, outline=(0, 0, 0))
    img.save(sys.argv[2])

main()
