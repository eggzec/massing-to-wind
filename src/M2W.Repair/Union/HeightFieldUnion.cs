using M2W.Geometry;
using M2W.Repair.Model;
using M2W.Repair.Planar;
using M2W.Repair.Steps;

namespace M2W.Repair.Union;

/// <summary>
/// Rebuilds the massing as a 2.5D height field: the union of all closed solids, filled down to the ground, as one
/// closed shell per connected footprint. This merges stacked floors, overlapping blocks and blocks touching wall to
/// wall in one pass.
///
/// How: the horizontal faces of each solid at each height level are reduced to that level's boundary edges (XOR of
/// triangle edges). All boundary edges of all solids are planarised together; each edge keeps tags (solid, level).
/// Crossing an edge toggles "inside level L of solid s" for each of its tags, so a breadth-first walk from the outside
/// labels every face of the plan with, per solid, which levels lie below it. A solid is solid between alternate
/// toggles; the face height is the highest roof of any solid over it. Edges with equal height on both sides are
/// dropped, collinear chains merged, and shells built from the remaining plan: a ground face, a roof per face and
/// walls on every edge where the height changes. Wall columns are split at every height used around their vertex,
/// so all triangles meet edge to edge.
/// </summary>
public sealed class HeightFieldUnion
{
    public sealed class Result
    {
        public List<OutputShell> Shells { get; } = new();
        public List<string> Problems { get; } = new();

        /// <summary>Roof steps lower than minEdge that were made flush: (from height, to height, plan faces raised).</summary>
        public List<(double from, double to, int faces)> FlushedSteps { get; } = new();

        /// <summary>For each solid (by input order), the shell it ended up in (-1 if none).</summary>
        public int[] SolidShell { get; init; } = Array.Empty<int>();
    }

    private readonly IReadOnlyList<Solid> _solids;
    private readonly double _tol;
    private readonly double _minEdge;

    /// <param name="tolerance">Weld tolerance: points and heights closer than this are the same.</param>
    /// <param name="minEdge">Neighbouring roofs whose heights differ by less than this are made flush (raised to the
    /// higher one), because the step between them would be an edge shorter than minEdge.</param>
    public HeightFieldUnion(IReadOnlyList<Solid> solids, double tolerance, double minEdge = 0)
    {
        _solids = solids;
        _tol = Math.Max(tolerance, 1e-6);
        _minEdge = Math.Max(minEdge, 0);
    }

    private sealed record Tag(int Solid, int Level, double Z);

    public Result Run()
    {
        var problems = new List<string>();

        // ---- 1. height levels from horizontal triangles
        var zs = new List<double>();
        var sloped = 0;
        foreach (var s in _solids)
            foreach (var t in s.Triangles)
            {
                var (a, b, c) = (s.Vertices[t.A], s.Vertices[t.B], s.Vertices[t.C]);
                var dz = Math.Max(a.Z, Math.Max(b.Z, c.Z)) - Math.Min(a.Z, Math.Min(b.Z, c.Z));
                if (dz <= _tol) { zs.Add(a.Z); zs.Add(b.Z); zs.Add(c.Z); }
                else if (PlanWidth(a, b, c) > 2 * _tol) sloped++; // vertical walls project to a sliver thinner than the tolerance
            }
        if (sloped > 0) problems.Add($"{sloped} sloped triangles (neither horizontal nor vertical) are not supported; ignored");
        var levels = ClusterLevels(zs);
        int LevelOf(double z)
        {
            int lo = 0, hi = levels.Count - 1;
            while (lo < hi) { var m = (lo + hi) >> 1; if (levels[m].max + _tol < z) lo = m + 1; else hi = m; }
            return lo;
        }

        // ---- 2. boundary edges per (solid, level), tagged
        var tags = new List<Tag>();
        var plan = new Planarizer(_tol);
        for (var si = 0; si < _solids.Count; si++)
        {
            var s = _solids[si];
            var perLevel = new SortedDictionary<int, Dictionary<(int, int), int>>();
            foreach (var t in s.Triangles)
            {
                var (a, b, c) = (s.Vertices[t.A], s.Vertices[t.B], s.Vertices[t.C]);
                var dz = Math.Max(a.Z, Math.Max(b.Z, c.Z)) - Math.Min(a.Z, Math.Min(b.Z, c.Z));
                if (dz > _tol) continue;
                var L = LevelOf(a.Z);
                if (!perLevel.TryGetValue(L, out var edges)) perLevel[L] = edges = new Dictionary<(int, int), int>();
                for (var k = 0; k < 3; k++)
                {
                    var key = Orienter.Key(t[k], t[(k + 1) % 3]);
                    edges[key] = edges.TryGetValue(key, out var n) ? n + 1 : 1;
                }
            }
            foreach (var (L, edges) in perLevel)
            {
                var tagId = tags.Count;
                var any = false;
                foreach (var ((u, v), n) in edges.OrderBy(kv => kv.Key))
                {
                    if (n % 2 == 0) continue;
                    var pu = s.Vertices[u]; var pv = s.Vertices[v];
                    plan.AddSegment(new P2(pu.X, pu.Y), new P2(pv.X, pv.Y), tagId);
                    any = true;
                }
                if (any) tags.Add(new Tag(si, L, levels[L].z));
            }
        }
        var res = new Result { SolidShell = Enumerable.Repeat(-1, _solids.Count).ToArray() };
        res.Problems.AddRange(problems);
        if (tags.Count == 0) { res.Problems.Add("no horizontal faces: nothing to build"); return res; }

        var pedges = plan.Build();
        var pts = plan.Points;
        var dcel = Dcel.Build(pts, pedges.Select(e => (e.u, e.v)).ToList());
        var words = (tags.Count + 63) / 64;

        // ---- 3. label faces by toggling tags across edges, starting from the outside
        var edgeBits = pedges.Select(e =>
        {
            var b = new ulong[words];
            foreach (var t in e.tags) b[t >> 6] ^= 1UL << (t & 63);
            return b;
        }).ToArray();
        var label = new ulong[dcel.FaceCount][];
        label[0] = new ulong[words];
        var queue = new Queue<int>();
        queue.Enqueue(0);
        var inconsistent = 0;
        while (queue.Count > 0)
        {
            var f = queue.Dequeue();
            foreach (var c in FaceCycles(dcel, f))
                foreach (var h in dcel.Cycles[c])
                {
                    var g = dcel.FaceOf(Dcel.Twin(h));
                    var eb = edgeBits[h >> 1];
                    var lg = new ulong[words];
                    for (var w = 0; w < words; w++) lg[w] = label[f][w] ^ eb[w];
                    if (label[g] is null) { label[g] = lg; queue.Enqueue(g); }
                    else if (!label[g].AsSpan().SequenceEqual(lg)) inconsistent++;
                }
        }
        if (inconsistent > 0)
            res.Problems.Add($"plan labelling inconsistent on {inconsistent} edges (a level boundary is not closed)");

        // ---- 4. height of each face; which solids cover it
        var height = new double[dcel.FaceCount];
        var covers = new List<int>[dcel.FaceCount];
        var openSolids = new SortedSet<int>();
        var lowest = new double[dcel.FaceCount];
        var filledUnder = new SortedSet<int>();
        for (var f = 0; f < dcel.FaceCount; f++)
        {
            covers[f] = new List<int>();
            if (label[f] is null) { inconsistent++; continue; }
            var curSolid = -1; var inside = false; var top = 0.0; var hasTop = false; var bottom = 0.0;
            lowest[f] = double.PositiveInfinity;
            void Close()
            {
                if (curSolid < 0) return;
                if (inside) openSolids.Add(curSolid);
                if (hasTop) { covers[f].Add(curSolid); height[f] = Math.Max(height[f], top); lowest[f] = Math.Min(lowest[f], bottom); }
            }
            for (var t = 0; t < tags.Count; t++)
            {
                if ((label[f][t >> 6] & (1UL << (t & 63))) == 0) continue;
                var tg = tags[t];
                if (tg.Solid != curSolid) { Close(); curSolid = tg.Solid; inside = false; hasTop = false; bottom = tg.Z; }
                inside = !inside;
                if (!inside) { top = tg.Z; hasTop = true; }
            }
            Close();
            if (height[f] > 0 && lowest[f] > _tol)
                foreach (var s in covers[f]) filledUnder.Add(s);
            if (height[f] < 0) height[f] = 0;
        }
        foreach (var b in filledUnder.Select(s => _solids[s].Block).Distinct())
            res.Problems.Add($"block {b} has no support below part of it (it floats beyond groundSnap); filled down to the ground");
        foreach (var s in openSolids)
            res.Problems.Add($"object {_solids[s].Name}: horizontal faces do not close (open above or below)");

        // ---- 4b. steps lower than minEdge between neighbouring roofs: raise the lower roof to the higher one
        if (_minEdge > 0) FlushSmallSteps(dcel, height, res);

        // ---- 5. keep edges where the height changes; merge collinear chains
        var kept = new List<KeptEdge>();
        for (var e = 0; e < pedges.Count; e++)
        {
            var h = 2 * e;
            var hl = height[dcel.FaceOf(h)];
            var hr = height[dcel.FaceOf(h ^ 1)];
            if (hl != hr) kept.Add(new KeptEdge(pedges[e].u, pedges[e].v, h));
        }
        MergeCollinear(kept, pts);

        // ---- 6. shells: faces of the first plan connected through positive-height neighbours
        var shellOfFace = ShellsOfFaces(dcel, height);
        var final = Dcel.Build(pts, kept.Select(k => (k.U, k.V)).ToList());
        // Every final half-edge maps to a first-plan half-edge with the same left face.
        var refOf = new int[kept.Count * 2];
        for (var i = 0; i < kept.Count; i++) { refOf[2 * i] = kept[i].Ref; refOf[2 * i + 1] = kept[i].Ref ^ 1; }
        int FirstFace(int finalHalf) => dcel.FaceOf(refOf[finalHalf]);

        var shellIds = new SortedDictionary<int, int>(); // first-plan shell root -> output shell number
        var builders = new List<ShellBuilder>();
        var heightsAt = new Dictionary<int, SortedSet<double>>();
        void AddHeight(int v, double z)
        {
            if (!heightsAt.TryGetValue(v, out var set)) heightsAt[v] = set = new SortedSet<double> { 0 };
            set.Add(z);
        }
        for (var h = 0; h < final.From.Length; h++)
        {
            AddHeight(final.From[h], height[FirstFace(h)]);
        }
        ShellBuilder Builder(int firstFace)
        {
            var root = shellOfFace[firstFace];
            if (!shellIds.TryGetValue(root, out var id))
            {
                id = builders.Count;
                shellIds[root] = id;
                builders.Add(new ShellBuilder(pts));
            }
            return builders[id];
        }

        // Roofs and ground faces.
        for (var f = 1; f < final.FaceCount; f++)
        {
            var outer = final.FaceOuter[f];
            var ff = FirstFace(final.Cycles[outer][0]);
            var h = height[ff];
            foreach (var hc in final.FaceHoles[f])
                if (height[FirstFace(final.Cycles[hc][0])] != h)
                    res.Problems.Add("a hole cycle has a different height from its face");
            if (h <= 0) continue;
            var b = Builder(ff);
            if (!b.AddCap(final.RingIds(outer), final.FaceHoles[f].Select(final.RingIds).ToList(), h))
                res.Problems.Add($"could not triangulate a roof at {h:0.###} m");
        }
        foreach (var hc in final.FaceHoles[0])
            if (height[FirstFace(final.Cycles[hc][0])] != 0)
                res.Problems.Add("an outer boundary has positive height outside");

        // Walls.
        for (var i = 0; i < kept.Count; i++)
        {
            var hl = height[dcel.FaceOf(kept[i].Ref)];
            var hr = height[dcel.FaceOf(kept[i].Ref ^ 1)];
            var (u, v) = (kept[i].U, kept[i].V);
            var higherFace = hl > hr ? dcel.FaceOf(kept[i].Ref) : dcel.FaceOf(kept[i].Ref ^ 1);
            if (hr > hl) (u, v) = (v, u); // solid on the left of u→v
            var lo = Math.Min(hl, hr); var hi = Math.Max(hl, hr);
            Builder(higherFace).AddWall(u, v, Column(heightsAt[u], lo, hi), Column(heightsAt[v], lo, hi));
        }

        // Which shell each solid is in (the shell of the first face it covers, in face order).
        for (var f = 0; f < dcel.FaceCount; f++)
            foreach (var s in covers[f])
                if (res.SolidShell[s] < 0 && height[f] > 0 && shellIds.TryGetValue(shellOfFace[f], out var id))
                    res.SolidShell[s] = id;

        for (var i = 0; i < builders.Count; i++)
        {
            var sh = builders[i].ToShell(_solids.Select((s, k) => (s, k)).Where(p => res.SolidShell[p.k] == i)
                .Select(p => p.s.Block).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList());
            res.Shells.Add(sh);
        }
        return res;
    }

    private void FlushSmallSteps(Dcel d, double[] height, Result res)
    {
        var parent = Enumerable.Range(0, d.FaceCount).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        var any = false;
        for (var h = 0; h < d.From.Length; h += 2)
        {
            int f = d.FaceOf(h), g = d.FaceOf(h ^ 1);
            var dh = Math.Abs(height[f] - height[g]);
            if (height[f] <= 0 || height[g] <= 0 || dh == 0 || dh >= _minEdge - 1e-9) continue;
            int a = Find(f), b = Find(g);
            if (a != b) { parent[Math.Max(a, b)] = Math.Min(a, b); any = true; }
        }
        if (!any) return;
        var groups = Enumerable.Range(0, d.FaceCount).GroupBy(Find).Where(g => g.Count() > 1);
        var steps = new SortedDictionary<(double, double), int>();
        foreach (var g in groups)
        {
            var top = g.Max(f => height[f]);
            foreach (var f in g)
                if (height[f] != top)
                {
                    var key = (height[f], top);
                    steps[key] = steps.TryGetValue(key, out var n) ? n + 1 : 1;
                    height[f] = top;
                }
        }
        foreach (var ((from, to), n) in steps) res.FlushedSteps.Add((from, to, n));
    }

    /// <summary>Width of a triangle's plan projection: twice its plan area over its longest plan edge.</summary>
    private static double PlanWidth(Vec3 a, Vec3 b, Vec3 c)
    {
        var area2 = Math.Abs(Vec3.Cross(b - a, c - a).Z);
        double L(Vec3 p, Vec3 q) => Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
        var longest = Math.Max(L(a, b), Math.Max(L(b, c), L(c, a)));
        return longest > 0 ? area2 / longest : 0;
    }

    private static IEnumerable<int> FaceCycles(Dcel d, int f)
    {
        if (d.FaceOuter[f] >= 0) yield return d.FaceOuter[f];
        foreach (var h in d.FaceHoles[f]) yield return h;
    }

    private static List<double> Column(SortedSet<double> heights, double lo, double hi) =>
        heights.Where(z => z >= lo && z <= hi).ToList();

    private List<(double z, double max)> ClusterLevels(List<double> zs)
    {
        zs.Sort();
        var result = new List<(double z, double max)>();
        var start = 0;
        for (var i = 1; i <= zs.Count; i++)
        {
            if (i < zs.Count && zs[i] - zs[i - 1] <= _tol) continue;
            var median = zs[(start + i - 1) / 2];
            var z = Math.Round(median, 6);
            if (Math.Abs(z) < 1e-9) z = 0;
            result.Add((z, zs[i - 1]));
            start = i;
        }
        return result;
    }

    private sealed class KeptEdge(int u, int v, int @ref)
    {
        public int U = u, V = v;

        /// <summary>A first-plan half-edge running in the U→V direction along this edge (same left and right faces).</summary>
        public int Ref = @ref;
    }

    /// <summary>Removes degree-2 vertices whose two edges are collinear within tolerance and bound the same faces.</summary>
    private void MergeCollinear(List<KeptEdge> kept, IReadOnlyList<P2> pts)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            var at = new Dictionary<int, List<KeptEdge>>();
            foreach (var k in kept)
            {
                if (!at.TryGetValue(k.U, out var a)) at[k.U] = a = new List<KeptEdge>();
                a.Add(k);
                if (!at.TryGetValue(k.V, out var b)) at[k.V] = b = new List<KeptEdge>();
                b.Add(k);
            }
            var existing = new HashSet<(int, int)>(kept.Select(k => Orienter.Key(k.U, k.V)));
            var removed = new HashSet<KeptEdge>();
            foreach (var (v, list) in at.OrderBy(kv => kv.Key))
            {
                if (list.Count != 2 || removed.Contains(list[0]) || removed.Contains(list[1])) continue;
                // Orient as a→v (e1) and v→b (e2).
                var e1 = list[0]; var e2 = list[1];
                int a, b, ref1, ref2;
                if (e1.V == v) { a = e1.U; ref1 = e1.Ref; } else { a = e1.V; ref1 = e1.Ref ^ 1; }
                if (e2.U == v) { b = e2.V; ref2 = e2.Ref; } else { b = e2.U; ref2 = e2.Ref ^ 1; }
                if (a == b || existing.Contains(Orienter.Key(a, b))) continue;
                var pa = pts[a]; var pb = pts[b]; var pv = pts[v];
                var d = pb - pa;
                var len = d.Length;
                if (len == 0) continue;
                if (Math.Abs(P2.Cross(d, pv - pa)) / len > _tol) continue;
                if (P2.Dot(pv - pa, d) <= 0 || P2.Dot(pv - pb, d) >= 0) continue; // v must lie between a and b
                _ = ref2;
                removed.Add(e1); removed.Add(e2);
                kept.Add(new KeptEdge(a, b, ref1));
                existing.Add(Orienter.Key(a, b));
                changed = true;
            }
            if (removed.Count > 0) kept.RemoveAll(removed.Contains);
        }
        kept.Sort((x, y) => x.U != y.U ? x.U.CompareTo(y.U) : x.V.CompareTo(y.V));
    }

    /// <summary>Union-find of first-plan faces with positive height that share an edge. Returns each face's root.</summary>
    private static int[] ShellsOfFaces(Dcel d, double[] height)
    {
        var parent = Enumerable.Range(0, d.FaceCount).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (var h = 0; h < d.From.Length; h += 2)
        {
            int f = d.FaceOf(h), g = d.FaceOf(h ^ 1);
            if (height[f] <= 0 || height[g] <= 0) continue;
            int a = Find(f), b = Find(g);
            if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
        }
        return Enumerable.Range(0, d.FaceCount).Select(Find).ToArray();
    }
}
