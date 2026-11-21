namespace M2W.Repair.Planar;

/// <summary>
/// Half-edge structure of a planar straight-line graph (no crossings). Half-edge <c>2e</c> runs u→v of edge e and
/// <c>2e+1</c> runs v→u. Each half-edge has its face on the left. Face 0 is the unbounded face; faces 1.. each have
/// one counter-clockwise outer cycle and any number of clockwise hole cycles (boundaries of other graph components
/// lying inside the face).
/// </summary>
public sealed class Dcel
{
    public required IReadOnlyList<P2> Points { get; init; }
    public required int[] From { get; init; }
    public required int[] To { get; init; }
    public required int[] Next { get; init; }

    /// <summary>Cycle number of each half-edge.</summary>
    public required int[] CycleOf { get; init; }

    /// <summary>Half-edges of each cycle, in order.</summary>
    public required List<List<int>> Cycles { get; init; }

    public required double[] CycleArea { get; init; }

    /// <summary>Face of each cycle (0 = unbounded).</summary>
    public required int[] CycleFace { get; init; }

    /// <summary>Outer cycle of each face (-1 for face 0).</summary>
    public required List<int> FaceOuter { get; init; }

    /// <summary>Hole cycles of each face (for face 0: the outer boundaries of the graph components).</summary>
    public required List<List<int>> FaceHoles { get; init; }

    public int FaceCount => FaceOuter.Count;

    public int FaceOf(int halfEdge) => CycleFace[CycleOf[halfEdge]];

    public static int Twin(int h) => h ^ 1;

    /// <summary>Vertex ring of a cycle (the start vertex of each half-edge).</summary>
    public List<P2> Ring(int cycle) => Cycles[cycle].Select(h => Points[From[h]]).ToList();

    public List<int> RingIds(int cycle) => Cycles[cycle].Select(h => From[h]).ToList();

    public static Dcel Build(IReadOnlyList<P2> points, IReadOnlyList<(int u, int v)> edges)
    {
        var m = edges.Count * 2;
        var from = new int[m];
        var to = new int[m];
        for (var e = 0; e < edges.Count; e++)
        {
            from[2 * e] = edges[e].u; to[2 * e] = edges[e].v;
            from[2 * e + 1] = edges[e].v; to[2 * e + 1] = edges[e].u;
        }
        // Outgoing half-edges per vertex, counter-clockwise by angle.
        var outgoing = new Dictionary<int, List<int>>();
        for (var h = 0; h < m; h++)
        {
            if (!outgoing.TryGetValue(from[h], out var l)) outgoing[from[h]] = l = new List<int>();
            l.Add(h);
        }
        var posInRing = new int[m];
        foreach (var (v, l) in outgoing)
        {
            l.Sort((a, b) =>
            {
                var da = points[to[a]] - points[v]; var db = points[to[b]] - points[v];
                var c = Math.Atan2(da.Y, da.X).CompareTo(Math.Atan2(db.Y, db.X));
                return c != 0 ? c : a.CompareTo(b);
            });
            for (var i = 0; i < l.Count; i++) posInRing[l[i]] = i;
        }
        // next(h: u→v) = the outgoing half-edge at v just clockwise of v→u (face kept on the left).
        var next = new int[m];
        for (var h = 0; h < m; h++)
        {
            var tw = h ^ 1;
            var ring = outgoing[to[h]];
            next[h] = ring[(posInRing[tw] - 1 + ring.Count) % ring.Count];
        }
        var cycleOf = new int[m];
        Array.Fill(cycleOf, -1);
        var cycles = new List<List<int>>();
        var areas = new List<double>();
        for (var h = 0; h < m; h++)
        {
            if (cycleOf[h] >= 0) continue;
            var c = new List<int>();
            var x = h;
            double a2 = 0;
            do
            {
                cycleOf[x] = cycles.Count;
                c.Add(x);
                a2 += P2.Cross(points[from[x]], points[to[x]]);
                x = next[x];
            } while (x != h);
            cycles.Add(c);
            areas.Add(a2 / 2);
        }

        // Graph components (to tell a hole cycle from its own component's faces).
        var parent = new Dictionary<int, int>();
        int Find(int x)
        {
            if (!parent.TryGetValue(x, out var p)) { parent[x] = x; return x; }
            while (p != x) { var g = parent[p]; parent[x] = g; x = p; p = g; }
            return x;
        }
        foreach (var (u, v) in edges)
        {
            int a = Find(u), b = Find(v);
            if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
        }

        var cycleFace = new int[cycles.Count];
        var faceOuter = new List<int> { -1 };
        var faceHoles = new List<List<int>> { new() };
        var positive = new List<int>();
        for (var c = 0; c < cycles.Count; c++)
            if (areas[c] > 1e-12)
            {
                cycleFace[c] = faceOuter.Count;
                faceOuter.Add(c);
                faceHoles.Add(new List<int>());
                positive.Add(c);
            }
        var rings = new Dictionary<int, List<P2>>();
        var boxes = new Dictionary<int, (double x0, double y0, double x1, double y1)>();
        foreach (var c in positive)
        {
            var r = cycles[c].Select(h => points[from[h]]).ToList();
            rings[c] = r;
            boxes[c] = (r.Min(p => p.X), r.Min(p => p.Y), r.Max(p => p.X), r.Max(p => p.Y));
        }
        for (var c = 0; c < cycles.Count; c++)
        {
            if (areas[c] > 1e-12) continue;
            var comp = Find(from[cycles[c][0]]);
            // Test point: the cycle's leftmost (then lowest) vertex.
            var tp = cycles[c].Select(h => points[from[h]]).OrderBy(p => p.X).ThenBy(p => p.Y).First();
            var best = -1;
            foreach (var pc in positive)
            {
                if (Find(from[cycles[pc][0]]) == comp) continue;
                var b = boxes[pc];
                if (tp.X < b.x0 || tp.X > b.x1 || tp.Y < b.y0 || tp.Y > b.y1) continue;
                if (!Contains(rings[pc], tp)) continue;
                if (best < 0 || areas[pc] < areas[best]) best = pc;
            }
            var f = best < 0 ? 0 : cycleFace[best];
            cycleFace[c] = f;
            faceHoles[f].Add(c);
        }
        return new Dcel
        {
            Points = points, From = from, To = to, Next = next, CycleOf = cycleOf, Cycles = cycles,
            CycleArea = areas.ToArray(), CycleFace = cycleFace, FaceOuter = faceOuter, FaceHoles = faceHoles,
        };
    }

    private (double x0, double y0, double cell, Dictionary<(long, long), List<int>> cells, List<int> big)? _faceIndex;

    /// <summary>
    /// The first bounded face (lowest number) whose outer ring contains q and none of whose holes does; 0 if none.
    /// Candidates come from a grid of the faces' bounding boxes, built on first use.
    /// </summary>
    public int Locate(P2 q)
    {
        var (x0, y0, cell, cells, big) = _faceIndex ??= BuildFaceIndex();
        var key = ((long)Math.Floor((q.X - x0) / cell), (long)Math.Floor((q.Y - y0) / cell));
        var cand = cells.TryGetValue(key, out var list) ? list.Concat(big).OrderBy(f => f) : big.AsEnumerable();
        foreach (var g in cand)
            if (Contains(Ring(FaceOuter[g]), q) && !FaceHoles[g].Any(c => Contains(Ring(c), q)))
                return g;
        return 0;
    }

    private (double, double, double, Dictionary<(long, long), List<int>>, List<int>) BuildFaceIndex()
    {
        var boxes = new (double x0, double y0, double x1, double y1)[FaceCount];
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, sum = 0;
        for (var f = 1; f < FaceCount; f++)
        {
            var r = Ring(FaceOuter[f]);
            boxes[f] = (r.Min(p => p.X), r.Min(p => p.Y), r.Max(p => p.X), r.Max(p => p.Y));
            minX = Math.Min(minX, boxes[f].x0); minY = Math.Min(minY, boxes[f].y0);
            sum += Math.Max(boxes[f].x1 - boxes[f].x0, boxes[f].y1 - boxes[f].y0);
        }
        var cell = Math.Max(FaceCount > 1 ? sum / (FaceCount - 1) : 1, 1e-3);
        if (double.IsInfinity(minX)) { minX = 0; minY = 0; }
        var cells = new Dictionary<(long, long), List<int>>();
        var big = new List<int>();
        for (var f = 1; f < FaceCount; f++)
        {
            long cx0 = (long)Math.Floor((boxes[f].x0 - minX) / cell), cx1 = (long)Math.Floor((boxes[f].x1 - minX) / cell);
            long cy0 = (long)Math.Floor((boxes[f].y0 - minY) / cell), cy1 = (long)Math.Floor((boxes[f].y1 - minY) / cell);
            if ((cx1 - cx0 + 1) * (cy1 - cy0 + 1) > 256) { big.Add(f); continue; }
            for (var cx = cx0; cx <= cx1; cx++)
            for (var cy = cy0; cy <= cy1; cy++)
            {
                if (!cells.TryGetValue((cx, cy), out var l)) cells[(cx, cy)] = l = new List<int>();
                l.Add(f);
            }
        }
        return (minX, minY, cell, cells, big);
    }

    /// <summary>Even-odd point in polygon (point assumed not on the boundary).</summary>
    public static bool Contains(IReadOnlyList<P2> ring, P2 q)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i]; var b = ring[j];
            if ((a.Y > q.Y) != (b.Y > q.Y) && q.X < (b.X - a.X) * (q.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
}
