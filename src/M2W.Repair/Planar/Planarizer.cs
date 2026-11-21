namespace M2W.Repair.Planar;

/// <summary>
/// Turns a soup of tagged plan segments into a planar straight-line graph: vertices closer than the tolerance are
/// one vertex, a segment passing within the tolerance of a vertex is split there, and crossing segments are split at
/// their crossing. Coincident pieces are merged and their tags combined by XOR (a tag that appears twice on the same
/// piece cancels, as two coincident boundary edges of one region do). Coordinates are rounded to 1e-6 m, the
/// precision of the output file.
///
/// Coincident segments (the same two points, either direction) are held once with all their tags, repeats included:
/// the baked outlines of the final phase repeat each plan edge once per roof level above it, and splitting each copy
/// separately made the work grow with levels × edges. Splitting a held segment is the same as splitting each copy
/// (the copies have the same geometry), and it keeps the position of the first copy, so the order of crossings and
/// therefore every point number is unchanged.
/// </summary>
public sealed class Planarizer
{
    private readonly double _tol;
    private readonly Dictionary<(long, long), List<int>> _grid = new();
    private List<Seg> _segs = new();
    private Dictionary<(int, int), int> _segAt = new();

    private readonly double _reportAbove;

    /// <summary>A distinct segment u→v (direction of its first copy), the tags of all its copies, and how many input
    /// copies it stands for (for <see cref="Snapped"/>).</summary>
    private sealed record Seg(int U, int V, List<int> Tags)
    {
        public int Weight { get; set; }
    }

    /// <param name="tolerance">Points closer than this are one point; a segment passing closer than this to a point is
    /// split there.</param>
    /// <param name="reportAbove">Merges and splits that move geometry further than this are counted in
    /// <see cref="Snapped"/>.</param>
    public Planarizer(double tolerance, double reportAbove = double.PositiveInfinity)
    {
        _tol = Math.Max(tolerance, 1e-6);
        _reportAbove = reportAbove;
    }

    /// <summary>Number of point merges and vertex splits that moved geometry by more than <c>reportAbove</c>
    /// (a split counts once per copy of the segment).</summary>
    public int Snapped { get; private set; }

    public List<P2> Points { get; } = new();

    /// <summary>Adds a point, or returns the existing point within tolerance (lowest number wins).</summary>
    public int AddPoint(P2 p) => AddPoint(p, 1);

    private int AddPoint(P2 p, int weight)
    {
        var kx = (long)Math.Floor(p.X / _tol);
        var ky = (long)Math.Floor(p.Y / _tol);
        var best = -1;
        for (var dx = -1L; dx <= 1; dx++)
        for (var dy = -1L; dy <= 1; dy++)
            if (_grid.TryGetValue((kx + dx, ky + dy), out var list))
                foreach (var i in list)
                    if ((Points[i] - p).Length <= _tol && (best < 0 || i < best)) best = i;
        if (best >= 0)
        {
            if ((Points[best] - p).Length > _reportAbove) Snapped += weight;
            return best;
        }
        var q = new P2(Math.Round(p.X, 6), Math.Round(p.Y, 6));
        var id = Points.Count;
        Points.Add(q);
        var key = ((long)Math.Floor(q.X / _tol), (long)Math.Floor(q.Y / _tol));
        if (!_grid.TryGetValue(key, out var own)) _grid[key] = own = new List<int>();
        own.Add(id);
        return id;
    }

    public void AddSegment(P2 a, P2 b, int tag) => AddSegment(a, b, new[] { tag }, 1);

    /// <summary>
    /// Adds <paramref name="weight"/> copies of segment a–b at once, carrying <paramref name="tags"/> in all (XOR
    /// combined like the tags of separate copies). The same as adding the copies one by one with those tags between
    /// them, including the <see cref="Snapped"/> count.
    /// </summary>
    public void AddSegment(P2 a, P2 b, IReadOnlyList<int> tags, int weight)
    {
        var u = AddPoint(a, weight);
        var v = AddPoint(b, weight);
        if (u != v) Put(u, v, tags, weight, _segs, _segAt);
    }

    /// <summary>Adds segment u→v with its tags, or adds the tags to the segment already on the same two points.</summary>
    private static void Put(int u, int v, IEnumerable<int> tags, int weight, List<Seg> segs, Dictionary<(int, int), int> at)
    {
        var key = u < v ? (u, v) : (v, u);
        if (at.TryGetValue(key, out var s))
        {
            segs[s].Tags.AddRange(tags);
            segs[s].Weight += weight;
        }
        else
        {
            at[key] = segs.Count;
            segs.Add(new Seg(u, v, new List<int>(tags)) { Weight = weight });
        }
    }

    /// <summary>Splits until stable and returns the edges (u &lt; v) with their XOR-combined, sorted tags.</summary>
    public List<(int u, int v, int[] tags)> Build()
    {
        for (var iter = 0; iter < 32; iter++)
        {
            var a = SplitAtVertices();
            var b = SplitAtCrossings();
            if (!a && !b) break;
        }
        // Each segment is already the only one on its two points; keep the tags that occur an odd number of times.
        var result = new List<(int u, int v, int[] tags)>(_segs.Count);
        foreach (var s in _segs)
        {
            var tags = s.Tags.ToArray();
            Array.Sort(tags);
            var odd = new List<int>(tags.Length);
            for (var i = 0; i < tags.Length;)
            {
                var j = i;
                while (j < tags.Length && tags[j] == tags[i]) j++;
                if (((j - i) & 1) == 1) odd.Add(tags[i]);
                i = j;
            }
            if (odd.Count > 0) result.Add(s.U < s.V ? (s.U, s.V, odd.ToArray()) : (s.V, s.U, odd.ToArray()));
        }
        result.Sort((x, y) => x.u != y.u ? x.u.CompareTo(y.u) : x.v.CompareTo(y.v));
        return result;
    }

    private bool SplitAtVertices()
    {
        var order = Enumerable.Range(0, Points.Count).OrderBy(i => Points[i].X).ThenBy(i => i).ToArray();
        var xs = order.Select(i => Points[i].X).ToArray();
        var splits = new Dictionary<int, List<(double t, int w)>>();
        for (var s = 0; s < _segs.Count; s++)
        {
            var (u, v, _) = _segs[s];
            var weight = _segs[s].Weight;
            var a = Points[u]; var b = Points[v];
            var d = b - a;
            var len2 = P2.Dot(d, d);
            var lo = LowerBound(xs, Math.Min(a.X, b.X) - _tol);
            var maxX = Math.Max(a.X, b.X) + _tol;
            double minY = Math.Min(a.Y, b.Y) - _tol, maxY = Math.Max(a.Y, b.Y) + _tol;
            for (var k = lo; k < order.Length && xs[k] <= maxX; k++)
            {
                var w = order[k];
                if (w == u || w == v) continue;
                var p = Points[w];
                if (p.Y < minY || p.Y > maxY) continue;
                var t = P2.Dot(p - a, d) / len2;
                if (t <= 0 || t >= 1) continue;
                var off = (a + d * t - p).Length;
                if (off > _tol) continue;
                // Never split so close to an end that the piece would collapse.
                if ((p - a).Length <= _tol || (p - b).Length <= _tol) continue;
                if (!splits.TryGetValue(s, out var l)) splits[s] = l = new List<(double, int)>();
                l.Add((t, w));
                if (off > _reportAbove) Snapped += weight;
            }
        }
        return Apply(splits);
    }

    private bool SplitAtCrossings()
    {
        var idx = Enumerable.Range(0, _segs.Count)
            .OrderBy(s => Math.Min(Points[_segs[s].U].X, Points[_segs[s].V].X)).ThenBy(s => s).ToArray();
        var splits = new Dictionary<int, List<(double t, int w)>>();
        var found = new List<(int s1, int s2, P2 x, double t1, double t2)>();
        // Candidate pairs from a grid, in the order a sweep over idx would meet them (s1 is the earlier one in idx).
        var boxes = idx.Select(s =>
        {
            P2 p = Points[_segs[s].U], q = Points[_segs[s].V];
            return (Math.Min(p.X, q.X), Math.Min(p.Y, q.Y), Math.Max(p.X, q.X), Math.Max(p.Y, q.Y));
        }).ToList();
        foreach (var (i, j) in BoxGrid.Pairs(boxes, 0))
        {
            var s1 = idx[i];
            var (a1, b1, _) = _segs[s1];
            var A = Points[a1]; var B = Points[b1];
            var maxX = Math.Max(A.X, B.X);
            double minY1 = Math.Min(A.Y, B.Y), maxY1 = Math.Max(A.Y, B.Y);
            var s2 = idx[j];
            var (c1, d1, _) = _segs[s2];
            var C = Points[c1]; var D = Points[d1];
            if (Math.Min(C.X, D.X) > maxX) continue;
            if (Math.Max(C.Y, D.Y) < minY1 || Math.Min(C.Y, D.Y) > maxY1) continue;
            if (a1 == c1 || a1 == d1 || b1 == c1 || b1 == d1) continue;
            var o1 = P2.Orient(A, B, C); var o2 = P2.Orient(A, B, D);
            var o3 = P2.Orient(C, D, A); var o4 = P2.Orient(C, D, B);
            if (!(((o1 > 0 && o2 < 0) || (o1 < 0 && o2 > 0)) && ((o3 > 0 && o4 < 0) || (o3 < 0 && o4 > 0)))) continue;
            var r = B - A; var q = D - C;
            var den = P2.Cross(r, q);
            if (den == 0) continue;
            var t = P2.Cross(C - A, q) / den;
            var x = A + r * t;
            var tt = P2.Dot(x - C, q) / P2.Dot(q, q);
            found.Add((s1, s2, x, t, tt));
        }
        foreach (var (s1, s2, x, t1, t2) in found)
        {
            var w = AddPoint(x);
            void Add(int s, double t)
            {
                var (u, v, _) = _segs[s];
                if (w == u || w == v) return;
                if (!splits.TryGetValue(s, out var l)) splits[s] = l = new List<(double, int)>();
                l.Add((t, w));
            }
            Add(s1, t1);
            Add(s2, t2);
        }
        return Apply(splits);
    }

    private bool Apply(Dictionary<int, List<(double t, int w)>> splits)
    {
        if (splits.Count == 0) return false;
        var next = new List<Seg>(_segs.Count + splits.Count * 2);
        var at = new Dictionary<(int, int), int>(_segs.Count + splits.Count * 2);
        for (var s = 0; s < _segs.Count; s++)
        {
            var (u, v, tags) = _segs[s];
            var weight = _segs[s].Weight;
            if (!splits.TryGetValue(s, out var l)) { Put(u, v, tags, weight, next, at); continue; }
            var chain = new List<int> { u };
            foreach (var (_, w) in l.OrderBy(p => p.t).ThenBy(p => p.w))
                if (chain[^1] != w) chain.Add(w);
            if (chain[^1] != v) chain.Add(v);
            for (var k = 0; k + 1 < chain.Count; k++)
                if (chain[k] != chain[k + 1]) Put(chain[k], chain[k + 1], tags, weight, next, at);
        }
        _segs = next;
        _segAt = at;
        return true;
    }

    private static int LowerBound(double[] xs, double x)
    {
        int lo = 0, hi = xs.Length;
        while (lo < hi)
        {
            var m = (lo + hi) >> 1;
            if (xs[m] < x) lo = m + 1; else hi = m;
        }
        return lo;
    }
}
