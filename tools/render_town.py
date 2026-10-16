#!/usr/bin/env python3
"""Isometric render of a settlement plan's walls and roofs (U5Dump `town <loc> <out.json>`).

Roof pieces are drawn from their corner heights (as their snap points define them), so the
orientation of slopes, outer/inner corners and ridge caps can be checked without the game.
Usage: python3 tools/render_town.py plan.json out.png [yaw_deg_of_view]
"""
import json, math, sys
from PIL import Image, ImageDraw

# local triangles (x, z, y) per roof piece, from the snap points in piece_geometry.json
ROOF = {
    "roof": [((-1, -1, 1), (1, -1, 1), (1, 1, 0)), ((-1, -1, 1), (1, 1, 0), (-1, 1, 0))],
    "ocorner": [((-1, -1, 1), (1, -1, 0), (1, 1, 0)), ((-1, -1, 1), (1, 1, 0), (-1, 1, 0))],
    "icorner": [((1, 1, 0), (1, -1, 1), (-1, -1, 1)), ((1, 1, 0), (-1, -1, 1), (-1, 1, 1))],
    "top": [((-1, -1, 0), (1, -1, 0), (1, 0, .5)), ((-1, -1, 0), (1, 0, .5), (-1, 0, .5)),
            ((-1, 1, 0), (1, 1, 0), (1, 0, .5)), ((-1, 1, 0), (1, 0, .5), (-1, 0, .5))],
}
COLOUR = {"roof": (170, 70, 50), "ocorner": (200, 120, 40), "icorner": (60, 120, 200), "top": (230, 200, 60), "wall": (120, 90, 60), "stone": (170, 170, 175)}

def kind(p):
    if p.endswith("_ocorner"): return "ocorner"
    if p.endswith("_icorner"): return "icorner"
    if p.endswith("_roof_top"): return "top"
    if p.endswith("_roof") or p == "wood_roof": return "roof"
    return None

def rot(x, z, yaw):
    r = math.radians(yaw); c, s = math.cos(r), math.sin(r)
    return x * c + z * s, -x * s + z * c

def main():
    pieces = json.load(open(sys.argv[1]))
    view = float(sys.argv[3]) if len(sys.argv) > 3 else 35
    tris = []
    for p in pieces:
        k = kind(p["p"])
        if k:
            for t in ROOF[k]:
                pts = []
                for (lx, lz, ly) in t:
                    wx, wz = rot(lx, lz, p["yaw"])
                    pts.append((p["x"] + wx, p["y"] + ly, p["z"] + wz))
                tris.append((pts, COLOUR[k]))
        elif "wall" in p["p"] or p["p"].startswith("stone_") or "door" in p["p"]:
            # a wall as a vertical 2x2 (or 1x1) panel through its pivot
            w = 0.5 if ("quarter" in p["p"] or "1x1" in p["p"]) else 1.0
            h0, h1 = (p["y"] - (0 if "quarter" in p["p"] else 1)), (p["y"] + (1 if "quarter" in p["p"] else 1))
            ax, az = rot(w, 0, p["yaw"])
            a = (p["x"] - ax, h0, p["z"] - az); b = (p["x"] + ax, h0, p["z"] + az)
            c = (p["x"] + ax, h1, p["z"] + az); d = (p["x"] - ax, h1, p["z"] - az)
            col = COLOUR["stone"] if p["p"].startswith("stone") else COLOUR["wall"]
            tris += [((a, b, c), col), ((a, c, d), col)]
    # isometric camera
    vr = math.radians(view)
    def proj(pt):
        x, y, z = pt
        rx, rz = x * math.cos(vr) - z * math.sin(vr), x * math.sin(vr) + z * math.cos(vr)
        return rx, -(rz * 0.5) - y * 0.87, rz  # screen x, screen y, depth (bigger = farther)
    S = int(sys.argv[4]) if len(sys.argv) > 4 else 12
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
        projected.append((sum(q[2] for q in pp) / 3, [(q[0], q[1]) for q in pp], tuple(int(c * shade) for c in col)))
    xs = [q[0] for _, pts, _ in projected for q in pts]; ys = [q[1] for _, pts, _ in projected for q in pts]
    W, H = int((max(xs) - min(xs)) * S) + 40, int((max(ys) - min(ys)) * S) + 40
    img = Image.new("RGB", (W, H), (30, 40, 30)); d = ImageDraw.Draw(img)
    for _, pts, col in sorted(projected, key=lambda t: -t[0]):
        d.polygon([((x - min(xs)) * S + 20, (y - min(ys)) * S + 20) for x, y in pts], fill=col, outline=(0, 0, 0))
    img.save(sys.argv[2])

main()
