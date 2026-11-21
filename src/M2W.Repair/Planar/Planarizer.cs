namespace M2W.Repair.Planar;

/// <summary>
/// Turns a soup of tagged plan segments into a planar straight-line graph: vertices closer than the tolerance are
/// one vertex, a segment passing within the tolerance of a vertex is split there, and crossing segments are split at
/// their crossing. Coincident pieces are merged and their tags combined by XOR (a tag that appears twice on the same
/// piece cancels, as two coincident boundary edges of one region do). Coordinates are rounded to 1e-6 m, the
/// precision of the output file.
/// </summary>
public sealed class Planarizer
{
    private readonly double _tol;
    private readonly Dictionary<(long, long), List<int>> _grid = new();
    private readonly List<(int u, int v, int tag)> _segs = new();

    private readonly double _reportAbove;

    /// <param name="tolerance">Points closer than this are one point; a segment passing closer than this to a point is
    /// split there.</param>
    /// <param name="reportAbove">Merges and splits that move geometry further than this are counted in
    /// <see cref="Snapped"/>.</param>
    public Planarizer(double tolerance, double reportAbove = double.PositiveInfinity)
    {
        _tol = Math.Max(tolerance, 1e-6);
        _reportAbove = reportAbove;
    }

    /// <summary>Number of point merges and vertex splits that moved geometry by more than <c>reportAbove</c>.</summary>
    public int Snapped { get; private set; }

    public List<P2> Points { get; } = new();

    /// <summary>Adds a point, or returns the existing point within tolerance (lowest number wins).</summary>
    public int AddPoint(P2 p)
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
            if ((Points[best] - p).Length > _reportAbove) Snapped++;
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

    public void AddSegment(P2 a, P2 b, int tag)
    {
        var u = AddPoint(a);
        var v = AddPoint(b);
        if (u != v) _segs.Add((u, v, tag));
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
        var map = new SortedDictionary<(int, int), SortedSet<int>>();
        foreach (var (u, v, tag) in _segs)
        {
            var key = u < v ? (u, v) : (v, u);
            if (!map.TryGetValue(key, out var set)) map[key] = set = new SortedSet<int>();
            if (!set.Remove(tag)) set.Add(tag);
        }
        return map.Where(kv => kv.Value.Count > 0).Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Value.ToArray())).ToList();
    }

    private bool SplitAtVertices()
    {
        var order = Enumerable.Range(0, Points.Count).OrderBy(i => Points[i].X).ThenBy(i => i).ToArray();
        var xs = order.Select(i => Points[i].X).ToArray();
        var splits = new Dictionary<int, List<(double t, int w)>>();
        for (var s = 0; s < _segs.Count; s++)
        {
            var (u, v, _) = _segs[s];
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
                if (off > _reportAbove) Snapped++;
            }
        }
        return Apply(splits);
    }

    private bool SplitAtCrossings()
    {
        var idx = Enumerable.Range(0, _segs.Count)
            .OrderBy(s => Math.Min(Points[_segs[s].u].X, Points[_segs[s].v].X)).ThenBy(s => s).ToArray();
        var splits = new Dictionary<int, List<(double t, int w)>>();
        var found = new List<(int s1, int s2, P2 x, double t1, double t2)>();
        for (var i = 0; i < idx.Length; i++)
        {
            var s1 = idx[i];
            var (a1, b1, _) = _segs[s1];
            var A = Points[a1]; var B = Points[b1];
            var maxX = Math.Max(A.X, B.X);
            double minY1 = Math.Min(A.Y, B.Y), maxY1 = Math.Max(A.Y, B.Y);
            for (var j = i + 1; j < idx.Length; j++)
            {
                var s2 = idx[j];
                var (c1, d1, _) = _segs[s2];
                var C = Points[c1]; var D = Points[d1];
                if (Math.Min(C.X, D.X) > maxX) break;
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
        var next = new List<(int, int, int)>(_segs.Count + splits.Count * 2);
        for (var s = 0; s < _segs.Count; s++)
        {
            var (u, v, tag) = _segs[s];
            if (!splits.TryGetValue(s, out var l)) { next.Add((u, v, tag)); continue; }
            var chain = new List<int> { u };
            foreach (var (_, w) in l.OrderBy(p => p.t).ThenBy(p => p.w))
                if (chain[^1] != w) chain.Add(w);
            if (chain[^1] != v) chain.Add(v);
            for (var k = 0; k + 1 < chain.Count; k++)
                if (chain[k] != chain[k + 1]) next.Add((chain[k], chain[k + 1], tag));
        }
        _segs.Clear();
        _segs.AddRange(next);
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
