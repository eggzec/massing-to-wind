"""ParaView scene for the Massing-to-Wind demo: input and repaired output side by side.

Owner demo tooling (D-053): runs in ParaView's own Python. Not part of the build, the tests or CI.

    pvpython demo/paraview_scene.py BEFORE.obj AFTER.obj OUTDIR [--title-before T] [--title-after T]

ParaView's OBJ reader ignores `o` lines, so this script reads the OBJ itself and writes legacy VTK files with one
id per input object (`object`) or output shell (`shell`). It marks defects with the same rules as `m2w render`
(src/M2W.Cli/Render/SvgRenderer.cs, D-043):
  - vertices within 5 mm are one vertex (drawing only);
  - a free edge is an edge with exactly one face of its object (open surface), drawn red;
  - a flipped face disagrees in winding with its neighbours, or belongs to an inside-out object, drawn magenta.
Both files are shifted by the same origin, so they line up and far coordinates stay precise in float32.

Writes OUTDIR/{before,after}.vtk, {before,after}-free-edges.vtk, scene.pvsm (open with `paraview --state=...`),
scene.png, and prints one summary line per file.
"""
import argparse
import math
import os
from collections import defaultdict, deque

WELD = 0.005


# ---------------------------------------------------------------- reading and analysis

def read_obj(path):
    verts, objs, cur = [], [], None
    with open(path, encoding="utf-8") as f:
        for line in f:
            p = line.split()
            if not p:
                continue
            if p[0] == "v":
                verts.append((float(p[1]), float(p[2]), float(p[3])))
            elif p[0] == "o":
                cur = (" ".join(p[1:]), [])
                objs.append(cur)
            elif p[0] == "f":
                if cur is None:
                    cur = ("object", [])
                    objs.append(cur)
                ids = []
                for t in p[1:]:
                    i = int(t.split("/")[0])
                    ids.append(i - 1 if i > 0 else len(verts) + i)
                cur[1].append(ids)
    return verts, [o for o in objs if o[1]]


def weld(verts):
    grid, pos, mapping = {}, [], []
    t2 = WELD * WELD
    for v in verts:
        c = tuple(math.floor(x / WELD) for x in v)
        found = -1
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    for r in grid.get((c[0] + dx, c[1] + dy, c[2] + dz), ()):
                        q = pos[r]
                        if (q[0] - v[0]) ** 2 + (q[1] - v[1]) ** 2 + (q[2] - v[2]) ** 2 <= t2 and (found < 0 or r < found):
                            found = r
        if found < 0:
            found = len(pos)
            pos.append(v)
            grid.setdefault(c, []).append(found)
        mapping.append(found)
    return mapping, pos


def newell(ids, pos):
    x = y = z = 0.0
    o = pos[ids[0]]
    for k in range(len(ids)):
        a = pos[ids[k]]
        b = pos[ids[(k + 1) % len(ids)]]
        a = (a[0] - o[0], a[1] - o[1], a[2] - o[2])
        b = (b[0] - o[0], b[1] - o[1], b[2] - o[2])
        x += (a[1] - b[1]) * (a[2] + b[2])
        y += (a[2] - b[2]) * (a[0] + b[0])
        z += (a[0] - b[0]) * (a[1] + b[1])
    return (x, y, z)


def analyse(verts, objs):
    """Faces as dicts {obj, ids, n, area, c, flipped}; free edges as welded vertex pairs."""
    mapping, pos = weld(verts)
    faces = []
    for oi, (_, flist) in enumerate(objs):
        for f in flist:
            ids = []
            for i in f:
                w = mapping[i]
                if not ids or ids[-1] != w:
                    ids.append(w)
            while len(ids) > 1 and ids[0] == ids[-1]:
                ids.pop()
            if len(ids) < 3:
                continue
            n = newell(ids, pos)
            ln = math.sqrt(n[0] ** 2 + n[1] ** 2 + n[2] ** 2)
            area = ln / 2
            c = tuple(sum(pos[k][j] for k in ids) / len(ids) for j in range(3))
            faces.append({"obj": oi, "ids": ids, "n": (n[0] / ln, n[1] / ln, n[2] / ln) if area > 1e-12 else (0.0, 0.0, 0.0),
                          "area": area if area > 1e-12 else 0.0, "c": c, "flipped": False})

    edges = defaultdict(list)  # (obj, lo, hi) -> [(face, forward)]
    for fi, f in enumerate(faces):
        v = f["ids"]
        for k in range(len(v)):
            p, q = v[k], v[(k + 1) % len(v)]
            edges[(f["obj"], min(p, q), max(p, q))].append((fi, p < q))
    free = sorted((e[1], e[2]) for e, l in edges.items() if len(l) == 1)

    adj = defaultdict(list)
    for l in edges.values():
        if len(l) == 2 and l[0][0] != l[1][0]:
            same = l[0][1] != l[1][1]
            adj[l[0][0]].append((l[1][0], same))
            adj[l[1][0]].append((l[0][0], same))
    parity = [-1] * len(faces)
    for s in range(len(faces)):
        if parity[s] >= 0:
            continue
        parity[s] = 0
        patch, queue = [], deque([s])
        while queue:
            f = queue.popleft()
            patch.append(f)
            for g, same in adj[f]:
                if parity[g] < 0:
                    parity[g] = parity[f] if same else 1 - parity[f]
                    queue.append(g)
        total = sum(faces[f]["area"] for f in patch)
        if total <= 0:
            continue
        centre = tuple(sum(faces[f]["c"][j] * faces[f]["area"] for f in patch) / total for j in range(3))
        vol, extent, a0, a1 = 0.0, 0.0, 0.0, 0.0
        for f in patch:
            d = tuple(faces[f]["c"][j] - centre[j] for j in range(3))
            w = faces[f]["area"] * sum(faces[f]["n"][j] * d[j] for j in range(3))
            vol += w if parity[f] == 0 else -w
            extent = max(extent, math.sqrt(sum(x * x for x in d)))
            if parity[f] == 0:
                a0 += faces[f]["area"]
            else:
                a1 += faces[f]["area"]
        if abs(vol) > 1e-9 * total * max(extent, 1e-9):
            wrong = 1 if vol > 0 else 0
        else:
            wrong = 1 if a1 <= a0 else 0
        for f in patch:
            faces[f]["flipped"] = parity[f] == wrong and faces[f]["area"] > 0
    return pos, faces, free


# ---------------------------------------------------------------- VTK output

def write_polys(path, pos, faces, array, origin):
    drawn = [f for f in faces if f["area"] > 0]
    with open(path, "w", encoding="ascii", newline="\n") as w:
        w.write("# vtk DataFile Version 3.0\nm2w demo\nASCII\nDATASET POLYDATA\n")
        w.write("POINTS %d double\n" % len(pos))
        for p in pos:
            w.write("%.6f %.6f %.6f\n" % (p[0] - origin[0], p[1] - origin[1], p[2] - origin[2]))
        w.write("POLYGONS %d %d\n" % (len(drawn), sum(len(f["ids"]) + 1 for f in drawn)))
        for f in drawn:
            w.write("%d %s\n" % (len(f["ids"]), " ".join(map(str, f["ids"]))))
        w.write("CELL_DATA %d\n" % len(drawn))
        w.write("SCALARS %s int 1\nLOOKUP_TABLE default\n" % array)
        for f in drawn:
            w.write("%d\n" % f["obj"])
        w.write("SCALARS flipped int 1\nLOOKUP_TABLE default\n")
        for f in drawn:
            w.write("%d\n" % (1 if f["flipped"] else 0))


def write_lines(path, pos, free, origin):
    used = sorted({i for e in free for i in e})
    index = {v: k for k, v in enumerate(used)}
    with open(path, "w", encoding="ascii", newline="\n") as w:
        w.write("# vtk DataFile Version 3.0\nm2w demo free edges\nASCII\nDATASET POLYDATA\n")
        w.write("POINTS %d double\n" % len(used))
        for i in used:
            p = pos[i]
            w.write("%.6f %.6f %.6f\n" % (p[0] - origin[0], p[1] - origin[1], p[2] - origin[2]))
        w.write("LINES %d %d\n" % (len(free), 3 * len(free)))
        for a, b in free:
            w.write("2 %d %d\n" % (index[a], index[b]))


# ---------------------------------------------------------------- scene

PALETTE = [(0.306, 0.475, 0.655), (0.949, 0.557, 0.169), (0.349, 0.631, 0.310), (0.463, 0.718, 0.698),
           (0.929, 0.788, 0.282), (0.612, 0.459, 0.373), (0.627, 0.796, 0.910), (0.549, 0.820, 0.490),
           (0.729, 0.690, 0.675), (1.000, 0.745, 0.490)]  # as in m2w render; no red or magenta
RED = (0.890, 0.071, 0.106)
MAGENTA = (0.753, 0.149, 0.827)


def build_scene(outdir, sides):
    from paraview import simple as pv

    pv._DisableFirstRenderCameraReset()
    layout = pv.CreateLayout("Massing-to-Wind demo")
    layout.SplitHorizontal(0, 0.5)
    views = []
    for k, side in enumerate(sides):
        view = pv.CreateView("RenderView")
        pv.AssignViewToLayout(view=view, layout=layout, hint=k + 1)
        view.UseColorPaletteForBackground = 0
        view.Background = [1, 1, 1]
        view.OrientationAxesVisibility = 1
        view.EnableRenderOnInteraction = 1
        views.append(view)

        reader = pv.LegacyVTKReader(registrationName=side["name"], FileNames=[side["vtk"]])
        disp = pv.Show(reader, view)
        pv.ColorBy(disp, ("CELLS", side["array"]))
        lut = pv.GetColorTransferFunction(side["array"])
        lut.InterpretValuesAsCategories = 1
        n = max(side["count"], 1)
        lut.Annotations = [x for i in range(n) for x in (str(i), side["labels"][i] if i < len(side["labels"]) else str(i))]
        lut.IndexedColors = [c for i in range(n) for c in PALETTE[i % len(PALETTE)]]
        disp.SetScalarBarVisibility(view, False)
        disp.Opacity = side["opacity"]
        disp.Specular = 0.1

        outline = pv.FeatureEdges(registrationName=side["name"] + " outline", Input=reader)
        outline.BoundaryEdges = 0
        outline.FeatureEdges = 1
        outline.NonManifoldEdges = 0
        outline.ManifoldEdges = 0
        outline.FeatureAngle = 20
        od = pv.Show(outline, view)
        od.ColorArrayName = ["POINTS", ""]
        od.AmbientColor = od.DiffuseColor = [0.12, 0.16, 0.2]
        od.Opacity = 0.6 if side["opacity"] < 1 else 1.0

        if side["flipped"] > 0:
            flipped = pv.Threshold(registrationName=side["name"] + " flipped faces", Input=reader)
            flipped.Scalars = ["CELLS", "flipped"]
            flipped.LowerThreshold = 0.5
            flipped.UpperThreshold = 1.5
            fd = pv.Show(flipped, view)
            fd.ColorArrayName = ["POINTS", ""]
            fd.AmbientColor = fd.DiffuseColor = list(MAGENTA)
            fd.Opacity = 0.85

        if side["free"] > 0:
            edges = pv.LegacyVTKReader(registrationName=side["name"] + " free edges", FileNames=[side["edges_vtk"]])
            ed = pv.Show(edges, view)
            ed.ColorArrayName = ["POINTS", ""]
            ed.AmbientColor = ed.DiffuseColor = list(RED)
            ed.LineWidth = 5
            ed.RenderLinesAsTubes = 1

        title = pv.Text(registrationName=side["name"] + " title", Text=side["title"])
        td = pv.Show(title, view)
        td.WindowLocation = "Upper Left Corner"
        td.FontSize = 16
        td.Color = [0.12, 0.16, 0.2]
        legend = pv.Text(registrationName=side["name"] + " legend", Text=side["legend"])
        ld = pv.Show(legend, view)
        ld.WindowLocation = "Lower Right Corner"
        ld.Justification = "Right"
        ld.FontSize = 11
        ld.Color = [0.32, 0.38, 0.43]

    # Same camera for both: axonometric direction of m2w render (30 deg elevation, from the south-west).
    pv.AddCameraLink(views[0], views[1], "m2w demo camera")
    el, az = math.radians(30), math.radians(45)
    d = (-math.cos(el) * math.sin(az), -math.cos(el) * math.cos(az), math.sin(el))
    b = sides[0]["bounds"]
    centre = [(b[0] + b[1]) / 2, (b[2] + b[3]) / 2, (b[4] + b[5]) / 2]
    for view in views:
        cam = view.GetActiveCamera()
        cam.SetFocalPoint(*centre)
        cam.SetPosition(centre[0] + d[0], centre[1] + d[1], centre[2] + d[2])
        cam.SetViewUp(0, 0, 1)
    pv.ResetCamera(views[0])
    views[0].GetActiveCamera().Dolly(0.85)  # margin above and below for the title and legend
    views[0].CameraParallelProjection = 0
    pv.Render(views[0])
    pv.Render(views[1])

    pv.SaveState(os.path.join(outdir, "scene.pvsm"))
    pv.SaveScreenshot(os.path.join(outdir, "scene.png"), layout, ImageResolution=[1920, 900])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("before")
    ap.add_argument("after")
    ap.add_argument("outdir")
    ap.add_argument("--title-before", default="Before: input")
    ap.add_argument("--title-after", default="After: repaired")
    a = ap.parse_args()
    os.makedirs(a.outdir, exist_ok=True)

    data = []
    for path in (a.before, a.after):
        verts, objs = read_obj(path)
        pos, faces, free = analyse(verts, objs)
        data.append((objs, pos, faces, free))
    allpts = [p for _, pos, _, _ in data for p in pos]
    origin = (min(p[0] for p in allpts), min(p[1] for p in allpts), 0.0)

    sides = []
    for k, (objs, pos, faces, free) in enumerate(data):
        name = "before" if k == 0 else "after"
        array = "object" if k == 0 else "shell"
        vtk = os.path.join(a.outdir, name + ".vtk")
        edges_vtk = os.path.join(a.outdir, name + "-free-edges.vtk")
        write_polys(vtk, pos, faces, array, origin)
        if free:
            write_lines(edges_vtk, pos, free, origin)
        nflip = sum(1 for f in faces if f["flipped"])
        used = {i for f in faces for i in f["ids"]}
        xs = [pos[i][0] - origin[0] for i in used]
        ys = [pos[i][1] - origin[1] for i in used]
        zs = [pos[i][2] - origin[2] for i in used]
        word = ("input object" if k == 0 else "closed shell") + ("" if len(objs) == 1 else "s")
        legend = "colour = %s (%d)\nred = free edges (open surface): %d\nmagenta = flipped faces: %d" % (
            "input object" if k == 0 else "shell", len(objs), len(free), nflip)
        print("%s: %d %s, %d faces, %d free edges, %d flipped faces" % (name, len(objs), word, len(faces), len(free), nflip))
        sides.append({"name": name, "array": array, "vtk": vtk, "edges_vtk": edges_vtk, "count": len(objs),
                      "labels": [o[0] for o in objs], "free": len(free), "flipped": nflip,
                      "opacity": 0.5 if k == 0 else 1.0,
                      "title": a.title_before if k == 0 else a.title_after, "legend": legend,
                      "bounds": (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs))})
    # Frame on the union of both, like `m2w render --frame`.
    b0, b1 = sides[0]["bounds"], sides[1]["bounds"]
    sides[0]["bounds"] = tuple(min(b0[i], b1[i]) if i % 2 == 0 else max(b0[i], b1[i]) for i in range(6))
    build_scene(a.outdir, sides)
    print("scene: %s" % os.path.join(a.outdir, "scene.pvsm"))


if __name__ == "__main__":
    main()
