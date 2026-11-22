using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using M2W.Geometry;

namespace M2W.Cli.Render;

/// <summary>Which look to draw. <see cref="Auto"/> picks <see cref="Output"/> when every object is named
/// <c>shell_NNN</c> (the repaired-file naming in AGREEMENT.md), otherwise <see cref="Input"/>.</summary>
public enum RenderStyle { Auto, Input, Output }

public sealed class RenderOptions
{
    /// <summary>Text drawn at the top. None if null or empty.</summary>
    public string? Title { get; init; }

    /// <summary>A second mesh that is not drawn: the view is fitted to the union of both, so a before/after pair
    /// rendered with each other as frame gets the same scale, position and viewBox.</summary>
    public Mesh? Frame { get; init; }

    public RenderStyle Style { get; init; } = RenderStyle.Auto;
}

/// <summary>What was drawn; for the CLI message and for tests.</summary>
public sealed class RenderResult
{
    public required string Svg { get; init; }
    public required RenderStyle Style { get; init; }
    public required string ViewBox { get; init; }
    /// <summary>Edges (after welding) used by exactly one face: open surfaces.</summary>
    public required int FreeEdges { get; init; }
    /// <summary>Faces whose winding disagrees with the faces around them.</summary>
    public required int FlippedFaces { get; init; }
    /// <summary>Polygons drawn (after merging coplanar neighbours and, for the output style, culling back faces).</summary>
    public required int Polygons { get; init; }
    /// <summary>Drawn polygons that face the camera (class "f").</summary>
    public required int FrontPolygons { get; init; }
    public required int Objects { get; init; }
}

/// <summary>
/// Draws a mesh as a deterministic SVG: a fixed axonometric camera (30° elevation, 45° azimuth, from the
/// south-west), polygons painted back to front, flat shading from the face normal with one fixed light, thin edges.
/// <list type="bullet">
/// <item>Input style: one colour per object, semi-transparent (back faces lighter), so stacked floors and overlaps
/// show through.</item>
/// <item>Output style: one colour per shell, opaque, back faces culled.</item>
/// <item>Both styles: free edges in red on top; faces whose winding disagrees with their neighbours in magenta.</item>
/// </list>
/// Determinism: no dictionaries are iterated for output order, numbers use the invariant culture with fixed
/// decimals, line ends are '\n', and nothing depends on time, paths or random IDs.
/// </summary>
public static class SvgRenderer
{
    public const double ElevationDeg = 30;
    public const double AzimuthDeg = 45;
    /// <summary>Vertices closer than this are one vertex for the edge analysis (drawing only; not a repair).</summary>
    public const double WeldTolerance = 0.005;

    public const int Width = 960;
    private const double Margin = 24;
    private const double Header = 64;
    private const double Footer = 40;
    private const double MaxDrawingHeight = 720;

    public const string FreeEdgeColour = "#e3121b";
    public const string FlippedColour = "#c026d3";

    // Muted categorical colours; no red or magenta, which mark defects.
    private static readonly string[] Palette =
    {
        "#4e79a7", "#f28e2b", "#59a14f", "#76b7b2", "#edc948",
        "#9c755f", "#a0cbe8", "#8cd17d", "#bab0ac", "#ffbe7d",
    };

    private static readonly Regex ShellName = new(@"^shell_\d+$", RegexOptions.CultureInvariant);

    public static RenderResult Render(Mesh mesh, RenderOptions? options = null)
    {
        options ??= new RenderOptions();
        var style = options.Style != RenderStyle.Auto ? options.Style
            : mesh.Objects.Count > 0 && mesh.Objects.All(o => ShellName.IsMatch(o.Name)) ? RenderStyle.Output
            : RenderStyle.Input;

        var a = Analyse(mesh);
        var cam = new Camera();

        // ---- fit: union of the referenced vertices of both meshes (symmetric, so a pair gets the same frame)
        var pts = Referenced(mesh).ToList();
        if (options.Frame is not null) pts.AddRange(Referenced(options.Frame));
        var fit = Fit.From(pts, cam);

        // ---- polygons
        var polys = BuildPolygons(a, style, cam);
        var order = Enumerable.Range(0, polys.Count)
            .OrderBy(i => polys[i].Depth).ThenBy(i => polys[i].Key).ToList();

        var sb = new StringBuilder();
        var vb = $"0 0 {Width} {N(fit.Height)}";
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"").Append(vb)
          .Append("\" width=\"").Append(Width).Append("\" height=\"").Append(N(fit.Height)).Append("\">\n");
        sb.Append("<rect width=\"").Append(Width).Append("\" height=\"").Append(N(fit.Height)).Append("\" fill=\"#ffffff\"/>\n");

        var fontAttr = "font-family=\"Segoe UI, Helvetica, Arial, sans-serif\"";
        if (!string.IsNullOrEmpty(options.Title))
            sb.Append("<text x=\"").Append(N(Margin)).Append("\" y=\"32\" ").Append(fontAttr)
              .Append(" font-size=\"20\" font-weight=\"600\" fill=\"#1f2933\">").Append(Xml(options.Title)).Append("</text>\n");

        sb.Append("<g stroke=\"#1f2933\" stroke-width=\"0.6\" stroke-linejoin=\"round\">\n");
        var front = 0;
        foreach (var i in order)
        {
            var p = polys[i];
            if (p.Class == "f") front++;
            sb.Append("<path class=\"").Append(p.Class).Append("\" d=\"");
            foreach (var loop in p.Loops)
            {
                for (var k = 0; k < loop.Count; k++)
                {
                    var (x, y) = fit.Map(loop[k]);
                    sb.Append(k == 0 ? "M" : "L").Append(N(x)).Append(' ').Append(N(y));
                }
                sb.Append('Z');
            }
            sb.Append("\" fill=\"").Append(p.Fill).Append('"');
            if (p.Loops.Count > 1) sb.Append(" fill-rule=\"evenodd\"");
            if (p.Opacity < 1) sb.Append(" fill-opacity=\"").Append(N(p.Opacity)).Append('"');
            if (p.Class == "b") sb.Append(" stroke-opacity=\"0.45\"");
            if (p.Class == "x") sb.Append(" stroke=\"").Append(FlippedColour).Append("\" stroke-width=\"1.2\"");
            sb.Append("/>\n");
        }
        sb.Append("</g>\n");

        if (a.FreeEdges.Count > 0)
        {
            sb.Append("<path class=\"free\" d=\"");
            foreach (var (u, v) in a.FreeEdges)
            {
                var (x0, y0) = fit.Map(a.Pos[u]);
                var (x1, y1) = fit.Map(a.Pos[v]);
                sb.Append('M').Append(N(x0)).Append(' ').Append(N(y0)).Append('L').Append(N(x1)).Append(' ').Append(N(y1));
            }
            sb.Append("\" fill=\"none\" stroke=\"").Append(FreeEdgeColour)
              .Append("\" stroke-width=\"2.4\" stroke-linecap=\"round\"/>\n");
        }

        // ---- legend
        var ly = N(fit.Height - 16);
        var objWord = style == RenderStyle.Output ? "shell" : "input object";
        var objCount = mesh.Objects.Count(o => o.Faces.Count > 0);
        sb.Append("<g ").Append(fontAttr).Append(" font-size=\"13\" fill=\"#52606d\">\n");
        sb.Append("<text x=\"").Append(N(Margin)).Append("\" y=\"").Append(ly).Append("\">colour = ").Append(objWord)
          .Append(" (").Append(objCount).Append(')').Append(style == RenderStyle.Input ? ", see-through" : "").Append("</text>\n");
        sb.Append("<line x1=\"360\" y1=\"").Append(N(fit.Height - 20.5)).Append("\" x2=\"390\" y2=\"").Append(N(fit.Height - 20.5))
          .Append("\" stroke=\"").Append(FreeEdgeColour).Append("\" stroke-width=\"2.4\" stroke-linecap=\"round\"/>\n");
        sb.Append("<text x=\"398\" y=\"").Append(ly).Append("\">free edges (open surface): ").Append(a.FreeEdges.Count).Append("</text>\n");
        sb.Append("<rect x=\"640\" y=\"").Append(N(fit.Height - 27)).Append("\" width=\"14\" height=\"13\" fill=\"").Append(FlippedColour)
          .Append("\" fill-opacity=\"0.8\"/>\n");
        sb.Append("<text x=\"662\" y=\"").Append(ly).Append("\">flipped faces: ").Append(a.FlippedCount).Append("</text>\n");
        sb.Append("</g>\n");
        sb.Append("</svg>\n");

        return new RenderResult
        {
            Svg = sb.ToString(), Style = style, ViewBox = vb, FreeEdges = a.FreeEdges.Count,
            FlippedFaces = a.FlippedCount, Polygons = polys.Count, FrontPolygons = front, Objects = objCount,
        };
    }

    // ================================================================ analysis

    private sealed class FaceInfo
    {
        public required int Object;
        public required int Index;          // position in file order, the tie-breaker everywhere
        public required int[] V;            // welded vertex ids, consecutive repeats removed
        public required Vec3 Normal;        // unit, from the stored winding
        public required double Area;
        public required Vec3 Centroid;
        public bool Flipped;
        public bool Degenerate;             // zero area: topology only
    }

    private sealed class Analysis
    {
        public required List<Vec3> Pos;
        public required List<FaceInfo> Faces;
        public required List<(int, int)> FreeEdges;
        /// <summary>Undirected edge → incident (face, forward) pairs; forward means traversed low id → high id.</summary>
        public required Dictionary<(int, int), List<(int Face, bool Forward)>> Edges;
        public required List<(int, int)> EdgeOrder; // sorted keys of Edges
        public int FlippedCount => Faces.Count(f => f.Flipped);
    }

    private static IEnumerable<Vec3> Referenced(Mesh m)
    {
        var used = new bool[m.Vertices.Count];
        foreach (var f in m.AllFaces) foreach (var i in f) used[i] = true;
        for (var i = 0; i < used.Length; i++) if (used[i]) yield return m.Vertices[i];
    }

    private static Analysis Analyse(Mesh mesh)
    {
        var (weld, pos) = Weld(mesh);
        var faces = new List<FaceInfo>();
        var index = 0;
        for (var o = 0; o < mesh.Objects.Count; o++)
            foreach (var f in mesh.Objects[o].Faces)
            {
                var ids = new List<int>(f.Count);
                foreach (var i in f)
                {
                    var w = weld[i];
                    if (ids.Count == 0 || ids[^1] != w) ids.Add(w);
                }
                while (ids.Count > 1 && ids[0] == ids[^1]) ids.RemoveAt(ids.Count - 1);
                var n = Newell(ids, pos);
                var area = n.Length / 2;
                // Zero-area faces (slivers with the point exactly on the edge) stay in the edge topology, or their
                // neighbours would show false free edges; they are never drawn, weighed or marked.
                var degenerate = area <= 1e-12;
                if (ids.Count >= 3)
                    faces.Add(new FaceInfo
                    {
                        Object = o, Index = index, V = ids.ToArray(), Normal = degenerate ? Vec3.Zero : n / n.Length,
                        Area = degenerate ? 0 : area, Degenerate = degenerate,
                        Centroid = ids.Aggregate(Vec3.Zero, (s, k) => s + pos[k]) / ids.Count,
                    });
                index++;
            }

        var edges = new Dictionary<(int, int), List<(int, bool)>>();
        var edgeOrder = new List<(int, int)>();
        for (var fi = 0; fi < faces.Count; fi++)
        {
            var v = faces[fi].V;
            for (var k = 0; k < v.Length; k++)
            {
                int p = v[k], q = v[(k + 1) % v.Length];
                var key = p < q ? (p, q) : (q, p);
                if (!edges.TryGetValue(key, out var list))
                {
                    edges[key] = list = new List<(int, bool)>(2);
                    edgeOrder.Add(key);
                }
                list.Add((fi, p < q));
            }
        }
        edgeOrder.Sort();
        // Free per object: an object with exactly one face on the edge has a hole there, even where another object's
        // faces happen to lie along the same line (a floor missing its slab above the floor below).
        var free = edgeOrder.Where(e => ByObject(edges[e], faces).Any(g => g.Count == 1)).ToList();

        MarkFlipped(faces, edges, edgeOrder);
        return new Analysis { Pos = pos, Faces = faces, FreeEdges = free, Edges = edges, EdgeOrder = edgeOrder };
    }

    /// <summary>The incidences on one edge, split by object (ascending object index, file order inside).</summary>
    private static IEnumerable<List<(int Face, bool Forward)>> ByObject(List<(int Face, bool Forward)> l, List<FaceInfo> faces)
    {
        if (l.Count == 1 || l.All(x => faces[x.Face].Object == faces[l[0].Face].Object)) { yield return l; yield break; }
        foreach (var obj in l.Select(x => faces[x.Face].Object).Distinct().OrderBy(o => o))
            yield return l.Where(x => faces[x.Face].Object == obj).ToList();
    }

    /// <summary>
    /// Marks flipped faces. Winding is propagated inside each object across edges where that object has exactly two
    /// faces; two faces agree when they run the shared edge in opposite directions. Each connected patch splits into
    /// two classes. The class kept is the one that makes the patch enclose a positive volume:
    /// Σ area·n·(c − p) over the faces, with p the patch centre (exactly 3 × volume for a closed patch, whatever p),
    /// so one wrong face and an object that is inside out as a whole are both marked. A flat or degenerate patch
    /// (|Σ| tiny) falls back to the larger area (ties: the class of the patch's first face).
    /// </summary>
    private static void MarkFlipped(List<FaceInfo> faces, Dictionary<(int, int), List<(int Face, bool Forward)>> edges, List<(int, int)> edgeOrder)
    {
        var adj = new List<(int Other, bool Same)>[faces.Count];
        for (var i = 0; i < faces.Count; i++) adj[i] = new();
        foreach (var e in edgeOrder)
            foreach (var l in ByObject(edges[e], faces))
            {
                if (l.Count != 2 || l[0].Face == l[1].Face) continue;
                var same = l[0].Forward != l[1].Forward; // opposite traversal = consistent winding
                adj[l[0].Face].Add((l[1].Face, same));
                adj[l[1].Face].Add((l[0].Face, same));
            }

        var parity = new int[faces.Count];
        Array.Fill(parity, -1);
        var queue = new Queue<int>();
        var patch = new List<int>();
        for (var s = 0; s < faces.Count; s++)
        {
            if (parity[s] >= 0) continue;
            patch.Clear();
            parity[s] = 0;
            queue.Enqueue(s);
            while (queue.Count > 0)
            {
                var f = queue.Dequeue();
                patch.Add(f);
                foreach (var (g, same) in adj[f])
                {
                    if (parity[g] >= 0) continue;
                    parity[g] = same ? parity[f] : 1 - parity[f];
                    queue.Enqueue(g);
                }
            }
            double a0 = 0, a1 = 0, total = 0;
            var centre = Vec3.Zero;
            foreach (var f in patch)
            {
                if (parity[f] == 0) a0 += faces[f].Area; else a1 += faces[f].Area;
                total += faces[f].Area;
                centre += faces[f].Centroid * faces[f].Area;
            }
            if (total <= 0) continue; // only zero-area faces: nothing to mark
            centre /= total;
            double vol = 0, extent = 0; // vol: class 0 as stored, class 1 reversed
            foreach (var f in patch)
            {
                var d = faces[f].Centroid - centre;
                var w = faces[f].Area * Vec3.Dot(faces[f].Normal, d);
                vol += parity[f] == 0 ? w : -w;
                extent = Math.Max(extent, d.Length);
            }
            int wrong;
            if (Math.Abs(vol) > 1e-9 * total * Math.Max(extent, 1e-9)) wrong = vol > 0 ? 1 : 0;
            else wrong = a1 <= a0 ? 1 : 0;
            foreach (var f in patch) faces[f].Flipped = parity[f] == wrong && !faces[f].Degenerate;
        }
    }

    /// <summary>Clusters vertices within <see cref="WeldTolerance"/>, in index order; the first one of a cluster
    /// gives the position. Grid hashing over the 27 neighbouring cells.</summary>
    private static (int[] map, List<Vec3> pos) Weld(Mesh mesh)
    {
        var map = new int[mesh.Vertices.Count];
        var pos = new List<Vec3>();
        var grid = new Dictionary<(long, long, long), List<int>>();
        var t2 = WeldTolerance * WeldTolerance;
        for (var i = 0; i < mesh.Vertices.Count; i++)
        {
            var v = mesh.Vertices[i];
            var c = Cell(v);
            var found = -1;
            for (var dx = -1; dx <= 1 && found < 0; dx++)
            for (var dy = -1; dy <= 1 && found < 0; dy++)
            for (var dz = -1; dz <= 1 && found < 0; dz++)
            {
                if (!grid.TryGetValue((c.Item1 + dx, c.Item2 + dy, c.Item3 + dz), out var list)) continue;
                foreach (var r in list)
                    if ((pos[r] - v).LengthSquared <= t2 && (found < 0 || r < found)) found = r;
            }
            if (found < 0)
            {
                found = pos.Count;
                pos.Add(v);
                if (!grid.TryGetValue(c, out var list)) grid[c] = list = new List<int>(1);
                list.Add(found);
            }
            map[i] = found;
        }
        return (map, pos);
    }

    private static (long, long, long) Cell(Vec3 v) =>
        ((long)Math.Floor(v.X / WeldTolerance), (long)Math.Floor(v.Y / WeldTolerance), (long)Math.Floor(v.Z / WeldTolerance));

    private static Vec3 Newell(IReadOnlyList<int> ids, List<Vec3> pos)
    {
        double x = 0, y = 0, z = 0;
        var o = pos[ids[0]];
        for (var k = 0; k < ids.Count; k++)
        {
            var a = pos[ids[k]] - o;
            var b = pos[ids[(k + 1) % ids.Count]] - o;
            x += (a.Y - b.Y) * (a.Z + b.Z);
            y += (a.Z - b.Z) * (a.X + b.X);
            z += (a.X - b.X) * (a.Y + b.Y);
        }
        return new Vec3(x, y, z);
    }

    // ================================================================ polygons

    private sealed class Poly
    {
        public required List<List<Vec3>> Loops;
        public required double Depth;
        public required int Key;
        public required string Class;
        public required string Fill;
        public required double Opacity;
    }

    private static List<Poly> BuildPolygons(Analysis a, RenderStyle style, Camera cam)
    {
        var faces = a.Faces;
        // Union-find over coplanar neighbours of the same object with agreeing winding, neither flipped. Counted per
        // object: where stacked floors meet, the roof of one floor and the floor of the next share their edges, and
        // each must still merge with itself.
        var parent = Enumerable.Range(0, faces.Count).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        foreach (var e in a.EdgeOrder)
        {
            var l = a.Edges[e];
            foreach (var mine in ByObject(l, faces))
            {
                if (mine.Count != 2 || mine[0].Forward == mine[1].Forward || mine[0].Face == mine[1].Face) continue;
                FaceInfo f = faces[mine[0].Face], g = faces[mine[1].Face];
                if (f.Flipped || g.Flipped) continue;
                if (Vec3.Dot(f.Normal, g.Normal) < 1 - 1e-9) continue;
                if (Math.Abs(Vec3.Dot(f.Normal, g.Centroid - f.Centroid)) > 1e-5) continue;
                int rf = Find(mine[0].Face), rg = Find(mine[1].Face);
                if (rf != rg) { if (rf < rg) parent[rg] = rf; else parent[rf] = rg; } // smallest index is the root
            }
        }
        var groups = new SortedDictionary<int, List<int>>();
        for (var i = 0; i < faces.Count; i++)
        {
            var r = Find(i);
            if (!groups.TryGetValue(r, out var list)) groups[r] = list = new List<int>();
            list.Add(i);
        }

        var light = Normalize(new Vec3(-0.15, -0.45, 1.0));
        var result = new List<Poly>();
        foreach (var (root, members) in groups)
        {
            var f0 = faces[root];
            if (f0.Degenerate) continue; // never merged (no normal), never drawn
            var n = f0.Flipped ? -f0.Normal : f0.Normal; // the corrected (outward) normal
            var facing = Vec3.Dot(n, cam.Eye) > 1e-9;
            if (style == RenderStyle.Output && !facing && !f0.Flipped) continue;

            var loops = members.Count == 1 ? new List<List<int>> { f0.V.ToList() } : Boundary(members, faces, a.Pos);
            if (loops is null) // could not chain the outline: draw the faces one by one
            {
                foreach (var m in members) result.Add(Make(new List<int>(faces[m].V), new[] { m }));
                continue;
            }
            result.Add(Make(null, members, loops));

            Poly Make(List<int>? single, IReadOnlyList<int> ms, List<List<int>>? ls = null)
            {
                ls ??= new List<List<int>> { single! };
                double area = 0;
                var c = Vec3.Zero;
                foreach (var m in ms) { area += faces[m].Area; c += faces[m].Centroid * faces[m].Area; }
                c /= area;
                var shadeN = Vec3.Dot(n, cam.Eye) >= 0 ? n : -n; // the side we look at
                var intensity = 0.42 + 0.58 * Math.Max(0, Vec3.Dot(shadeN, light));
                var cls = f0.Flipped ? "x" : facing ? "f" : "b";
                var baseColour = f0.Flipped ? FlippedColour : Palette[f0.Object % Palette.Length];
                double opacity = style == RenderStyle.Output ? 1 : cls == "x" ? 0.8 : cls == "f" ? 0.5 : 0.22;
                return new Poly
                {
                    Loops = ls.Select(l => l.Select(k => a.Pos[k]).ToList()).ToList(),
                    Depth = Vec3.Dot(c, cam.Eye), Key = faces[ms[0]].Index, Class = cls,
                    Fill = Shade(baseColour, intensity), Opacity = opacity,
                };
            }
        }
        return result;
    }

    /// <summary>Outline loops of a group of coplanar, consistently wound faces: the directed edges whose reverse is
    /// not in the group, chained; collinear points dropped. Null if the chain does not close.</summary>
    private static List<List<int>>? Boundary(List<int> members, List<FaceInfo> faces, List<Vec3> pos)
    {
        var directed = new HashSet<(int, int)>();
        foreach (var m in members)
        {
            var v = faces[m].V;
            for (var k = 0; k < v.Length; k++) directed.Add((v[k], v[(k + 1) % v.Length]));
        }
        var outgoing = new SortedDictionary<int, List<int>>();
        var count = 0;
        foreach (var m in members)
        {
            var v = faces[m].V;
            for (var k = 0; k < v.Length; k++)
            {
                int p = v[k], q = v[(k + 1) % v.Length];
                if (directed.Contains((q, p))) continue;
                if (!outgoing.TryGetValue(p, out var l)) outgoing[p] = l = new List<int>();
                l.Add(q);
                count++;
            }
        }
        var loops = new List<List<int>>();
        while (count > 0)
        {
            var start = outgoing.First(kv => kv.Value.Count > 0).Key;
            var loop = new List<int> { start };
            var at = start;
            while (true)
            {
                if (!outgoing.TryGetValue(at, out var l) || l.Count == 0) return null;
                var next = l[0];
                l.RemoveAt(0);
                count--;
                if (next == start) break;
                loop.Add(next);
                at = next;
                if (loop.Count > 100000) return null;
            }
            if (loop.Count >= 3) loops.Add(loop);
        }
        var simplified = loops.Select(l => DropCollinear(l, pos)).Where(l => l.Count >= 3).ToList();
        return simplified.Count == 0 ? null : simplified;
    }

    /// <summary>Removes points that lie on the straight line between their neighbours (T-junction points, triangle
    /// fan corners), so merged outlines stay small.</summary>
    private static List<int> DropCollinear(List<int> loop, List<Vec3> pos)
    {
        var l = new List<int>(loop);
        var changed = true;
        while (changed && l.Count > 3)
        {
            changed = false;
            for (var k = 0; k < l.Count && l.Count > 3; k++)
            {
                var p = pos[l[(k + l.Count - 1) % l.Count]];
                var q = pos[l[k]];
                var r = pos[l[(k + 1) % l.Count]];
                Vec3 u = q - p, v = r - q;
                var len = u.Length * v.Length;
                if (len <= 0 || (Vec3.Cross(u, v).Length <= 1e-9 * len && Vec3.Dot(u, v) > 0))
                {
                    l.RemoveAt(k);
                    changed = true;
                    k--;
                }
            }
        }
        return l;
    }

    // ================================================================ camera, fit, formatting

    private sealed class Camera
    {
        public readonly Vec3 Eye;   // unit vector from the scene towards the camera
        public readonly Vec3 Right;
        public readonly Vec3 Up;

        public Camera()
        {
            var el = ElevationDeg * Math.PI / 180;
            var az = AzimuthDeg * Math.PI / 180;
            Eye = new Vec3(-Math.Cos(el) * Math.Sin(az), -Math.Cos(el) * Math.Cos(az), Math.Sin(el));
            Right = Normalize(new Vec3(-Eye.Y, Eye.X, 0));
            Up = Vec3.Cross(Eye, Right);
        }
    }

    private sealed class Fit
    {
        private Camera _cam = null!;
        private Vec3 _centre;
        private double _scale, _minX, _minY, _offX, _offY;
        public double Height { get; private set; }

        public static Fit From(List<Vec3> pts, Camera cam)
        {
            var f = new Fit { _cam = cam };
            if (pts.Count == 0) { f._scale = 1; f.Height = Header + Footer + 2 * Margin + 100; return f; }
            double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X);
            double y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
            double z0 = pts.Min(p => p.Z), z1 = pts.Max(p => p.Z);
            f._centre = new Vec3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2);
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in pts)
            {
                var (sx, sy) = f.Project(p);
                minX = Math.Min(minX, sx); maxX = Math.Max(maxX, sx);
                minY = Math.Min(minY, sy); maxY = Math.Max(maxY, sy);
            }
            double w = Math.Max(maxX - minX, 1e-9), h = Math.Max(maxY - minY, 1e-9);
            f._scale = Math.Min((Width - 2 * Margin) / w, MaxDrawingHeight / h);
            f._minX = minX; f._minY = minY;
            f._offX = (Width - w * f._scale) / 2;
            f._offY = Header;
            f.Height = Math.Round(Header + h * f._scale + Margin + Footer);
            return f;
        }

        private (double, double) Project(Vec3 p)
        {
            var d = p - _centre;
            return (Vec3.Dot(d, _cam.Right), -Vec3.Dot(d, _cam.Up));
        }

        public (double, double) Map(Vec3 p)
        {
            var (sx, sy) = Project(p);
            return (_offX + (sx - _minX) * _scale, _offY + (sy - _minY) * _scale);
        }
    }

    private static Vec3 Normalize(Vec3 v) => v / v.Length;

    private static string Shade(string hex, double intensity)
    {
        int r = Convert.ToInt32(hex.Substring(1, 2), 16), g = Convert.ToInt32(hex.Substring(3, 2), 16), b = Convert.ToInt32(hex.Substring(5, 2), 16);
        int C(int c) => Math.Clamp((int)Math.Round(c * intensity + 255 * 0.12 * (1 - intensity)), 0, 255);
        return string.Create(CultureInfo.InvariantCulture, $"#{C(r):x2}{C(g):x2}{C(b):x2}");
    }

    /// <summary>Two decimals, invariant culture, no negative zero.</summary>
    private static string N(double v)
    {
        var s = v.ToString("F2", CultureInfo.InvariantCulture);
        return s == "-0.00" ? "0.00" : s;
    }

    private static string Xml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
