using M2W.Repair.Planar;

namespace M2W.Repair.Union;

/// <summary>
/// Morphological closing of the height field, done per height level. For each roof level <c>h</c> the slice
/// "height ≥ h" falls apart into connected components. Where boundary walls of two different components are closer
/// than <c>minGap</c>, a bridge polygon is filled from the ground up to <c>h</c>. Because slices are nested, a gap
/// between two blocks is filled up to the lower of the two, which is what a closing of the height field gives.
///
/// Bridges: for walls that face each other and are nearly parallel, the strip between them over the stretch where
/// one projects onto the other (a rectangle for parallel walls, so no new vertex appears when the walls end
/// together). Components with no such pair (for example two blocks meeting corner to corner) get, for every pair of
/// walls closer than minGap, the convex hull of the parts of each wall that lie within minGap of the other and in
/// front of it, which is a small fillet like a disk closing would give.
///
/// Gaps inside one component (a narrow inlet of a U shape) are not closed: AGREEMENT.md asks for gaps between blocks
/// to be closed and the blocks merged.
/// </summary>
public static class GapCloser
{
    /// <summary>A polygon to fill from the ground up to <see cref="Z"/>.</summary>
    public sealed record Bridge(IReadOnlyList<P2> Ring, double Z);

    /// <summary>Two components closer than minGap at one level: their faces, the gap width, and whether it was bridged.</summary>
    public sealed record Gap(int FaceA, int FaceB, double Width, double Z, bool Bridged);

    public sealed class Found
    {
        public List<Bridge> Bridges { get; } = new();
        public List<Gap> Gaps { get; } = new();
    }

    /// <summary>Walls within this angle of anti-parallel count as facing (rectangle bridge).</summary>
    private const double ParallelDeg = 10.0;

    /// <summary>
    /// Distances within this of minGap count as minGap (one unit of the 6th decimal, the precision of the output file).
    /// </summary>
    public const double Slack = 1e-6;

    private sealed record Wall(P2 A, P2 B, int Comp, int Face);

    public static Found Find(Dcel d, double[] height, double minGap, double tol)
    {
        var found = new Found();
        if (minGap <= 0) return found;
        var g = minGap - Slack;
        var levels = height.Where(h => h > 0).Distinct().OrderBy(h => h).ToList();
        var cosPar = Math.Cos(ParallelDeg * Math.PI / 180);
        foreach (var h in levels)
        {
            bool Inside(int f) => height[f] >= h;
            var comp = Components(d, Inside);
            var walls = Walls(d, Inside, comp, tol);
            if (walls.Select(w => w.Comp).Distinct().Count() < 2) continue;

            // Pairs of walls of different components closer than g, grouped by component pair.
            var byPair = new SortedDictionary<(int, int), List<(Wall a, Wall b, double dist)>>();
            for (var i = 0; i < walls.Count; i++)
            for (var j = i + 1; j < walls.Count; j++)
            {
                var (a, b) = (walls[i], walls[j]);
                if (a.Comp == b.Comp) continue;
                if (Math.Min(a.A.X, a.B.X) > Math.Max(b.A.X, b.B.X) + g || Math.Min(b.A.X, b.B.X) > Math.Max(a.A.X, a.B.X) + g) continue;
                if (Math.Min(a.A.Y, a.B.Y) > Math.Max(b.A.Y, b.B.Y) + g || Math.Min(b.A.Y, b.B.Y) > Math.Max(a.A.Y, a.B.Y) + g) continue;
                var dist = SegmentDistance(a.A, a.B, b.A, b.B);
                if (dist >= g) continue;
                if (a.Comp > b.Comp) (a, b) = (b, a);
                var key = (a.Comp, b.Comp);
                if (!byPair.TryGetValue(key, out var list)) byPair[key] = list = new();
                list.Add((a, b, dist));
            }

            foreach (var (_, pairs) in byPair)
            {
                var rects = new List<P2[]>();
                foreach (var (a, b, _) in pairs)
                {
                    var ua = Unit(a.B - a.A); var ub = Unit(b.B - b.A);
                    if (P2.Dot(ua, ub) > -cosPar) continue;
                    var r = Strip(a, b, g, tol) ?? Strip(b, a, g, tol);
                    if (r is not null) rects.Add(r);
                }
                var polys = rects;
                if (polys.Count == 0)
                {
                    polys = new List<P2[]>();
                    foreach (var (a, b, _) in pairs)
                    {
                        var hull = Fillet(a, b, g, tol);
                        if (hull is not null) polys.Add(hull);
                    }
                }
                var width = pairs.Min(p => p.dist);
                found.Gaps.Add(new Gap(pairs[0].a.Face, pairs[0].b.Face, width, h, polys.Count > 0));
                foreach (var p in polys) found.Bridges.Add(new Bridge(p, h));
            }
        }
        return found;
    }

    /// <summary>Component number of each face inside the slice (-1 outside), by shared edges.</summary>
    private static int[] Components(Dcel d, Func<int, bool> inside)
    {
        var parent = Enumerable.Range(0, d.FaceCount).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (var h = 0; h < d.From.Length; h += 2)
        {
            int f = d.FaceOf(h), k = d.FaceOf(h ^ 1);
            if (!inside(f) || !inside(k)) continue;
            int a = Find(f), b = Find(k);
            if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
        }
        return Enumerable.Range(0, d.FaceCount).Select(f => inside(f) ? Find(f) : -1).ToArray();
    }

    /// <summary>
    /// Boundary half-edges of the slice (inside on the left), with consecutive collinear ones of the same component
    /// chained into one wall.
    /// </summary>
    private static List<Wall> Walls(Dcel d, Func<int, bool> inside, int[] comp, double tol)
    {
        var boundary = new List<int>();
        for (var h = 0; h < d.From.Length; h++)
            if (inside(d.FaceOf(h)) && !inside(d.FaceOf(h ^ 1))) boundary.Add(h);
        var startingAt = boundary.GroupBy(h => d.From[h]).ToDictionary(gr => gr.Key, gr => gr.ToList());
        var pts = d.Points;
        int Comp(int h) => comp[d.FaceOf(h)];
        bool Collinear(int e, int f)
        {
            var a = pts[d.From[e]]; var b = pts[d.To[e]]; var c = pts[d.To[f]];
            var ab = b - a;
            return P2.Dot(ab, c - b) > 0 && Math.Abs(P2.Cross(ab, c - a)) / (c - a).Length <= tol;
        }
        var succ = new Dictionary<int, int>();
        var hasPred = new HashSet<int>();
        foreach (var e in boundary)
        {
            if (!startingAt.TryGetValue(d.To[e], out var outs)) continue;
            var cand = outs.Where(f => Comp(f) == Comp(e) && Collinear(e, f)).ToList();
            if (cand.Count != 1 || hasPred.Contains(cand[0])) continue;
            succ[e] = cand[0];
            hasPred.Add(cand[0]);
        }
        var walls = new List<Wall>();
        var used = new HashSet<int>();
        foreach (var e in boundary)
        {
            if (hasPred.Contains(e) || used.Contains(e)) continue;
            var last = e;
            used.Add(e);
            while (succ.TryGetValue(last, out var n) && used.Add(n)) last = n;
            walls.Add(new Wall(pts[d.From[e]], pts[d.To[last]], Comp(e), d.FaceOf(e)));
        }
        // Collinear cycles cannot occur, but keep any half-edge not reached as its own wall.
        foreach (var e in boundary)
            if (used.Add(e)) walls.Add(new Wall(pts[d.From[e]], pts[d.To[e]], Comp(e), d.FaceOf(e)));
        return walls;
    }

    /// <summary>
    /// The strip between wall a and a nearly anti-parallel wall b in front of it: in a's frame, over the stretch
    /// where b projects onto a and b is nearer than g. Null if empty.
    /// </summary>
    private static P2[]? Strip(Wall a, Wall b, double g, double tol)
    {
        var la = (a.B - a.A).Length;
        var u = (a.B - a.A) * (1 / la);
        var n = new P2(u.Y, -u.X); // outward: the inside is on the left of a
        double S(P2 p) => P2.Dot(p - a.A, u);
        double W(P2 p) => P2.Dot(p - a.A, n);
        double s1 = S(b.A), w1 = W(b.A), s2 = S(b.B), w2 = W(b.B);
        if (Math.Abs(s2 - s1) <= tol) return null;
        var lo = Math.Max(0, Math.Min(s1, s2));
        var hi = Math.Min(la, Math.Max(s1, s2));
        double Wat(double s) => w1 + (s - s1) * (w2 - w1) / (s2 - s1);
        // Clip to 0 ≤ w(s) < g; w is linear in s.
        (lo, hi) = ClipLinear(lo, hi, Wat, 0, g);
        if (hi - lo <= tol) return null;
        var wl = Wat(lo); var wh = Wat(hi);
        if (Math.Max(wl, wh) <= tol) return null;
        P2 At(double s, double w) => a.A + u * s + n * w;
        return new[] { At(lo, 0), At(lo, wl), At(hi, wh), At(hi, 0) };
    }

    /// <summary>The sub-interval of [lo, hi] where min ≤ f(s) ≤ max, for linear f.</summary>
    private static (double, double) ClipLinear(double lo, double hi, Func<double, double> f, double min, double max)
    {
        double flo = f(lo), fhi = f(hi);
        if (flo == fhi) return flo >= min && flo <= max ? (lo, hi) : (lo, lo);
        double SAt(double v) => lo + (v - flo) * (hi - lo) / (fhi - flo);
        double a = SAt(min), b = SAt(max);
        if (a > b) (a, b) = (b, a);
        return (Math.Max(lo, a), Math.Min(hi, b));
    }

    /// <summary>Convex hull of the parts of a and b within g of each other and in front of each other; null if thin.</summary>
    private static P2[]? Fillet(Wall a, Wall b, double g, double tol)
    {
        var pa = NearPart(a, b, g);
        var pb = NearPart(b, a, g);
        if (pa is null || pb is null) return null;
        var hull = ConvexHull(new[] { pa.Value.p, pa.Value.q, pb.Value.p, pb.Value.q });
        if (hull.Count < 3) return null;
        var area = Math.Abs(Triangulator.SignedArea(hull));
        var diam = 0.0;
        foreach (var x in hull) foreach (var y in hull) diam = Math.Max(diam, (x - y).Length);
        if (diam <= 0 || 2 * area / diam <= tol) return null;
        return hull.ToArray();
    }

    /// <summary>Part of wall a that is within g of wall b and on b's outer side.</summary>
    private static (P2 p, P2 q)? NearPart(Wall a, Wall b, double g)
    {
        P2 At(double t) => a.A + (a.B - a.A) * t;
        double F(double t) => PointSegmentDistance(At(t), b.A, b.B);
        // F is convex in t: golden-section search for its minimum, then bisect each side for F = g.
        double lo = 0, hi = 1;
        const double phi = 0.6180339887498949;
        for (var i = 0; i < 100; i++)
        {
            var m1 = hi - phi * (hi - lo); var m2 = lo + phi * (hi - lo);
            if (F(m1) <= F(m2)) hi = m2; else lo = m1;
        }
        var tm = (lo + hi) / 2;
        if (F(tm) >= g) return null;
        double Edge(double inside, double outside)
        {
            if (F(outside) < g) return outside;
            for (var i = 0; i < 100; i++)
            {
                var m = (inside + outside) / 2;
                if (F(m) < g) inside = m; else outside = m;
            }
            return inside;
        }
        var t0 = Edge(tm, 0); var t1 = Edge(tm, 1);
        // In front of b: the outward side is the right of b's direction.
        var ub = b.B - b.A;
        var nb = new P2(ub.Y, -ub.X) * (1 / ub.Length);
        double Front(double t) => P2.Dot(At(t) - b.A, nb);
        (t0, t1) = ClipLinear(t0, t1, Front, -1e-9, double.PositiveInfinity);
        if (t1 < t0) return null;
        return (At(t0), At(t1));
    }

    private static List<P2> ConvexHull(IEnumerable<P2> points)
    {
        var p = points.Distinct().OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
        if (p.Count < 3) return p;
        var hull = new List<P2>();
        for (var pass = 0; pass < 2; pass++)
        {
            var start = hull.Count;
            foreach (var q in p)
            {
                while (hull.Count >= start + 2 && P2.Orient(hull[^2], hull[^1], q) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(q);
            }
            hull.RemoveAt(hull.Count - 1);
            p.Reverse();
        }
        return hull;
    }

    private static P2 Unit(P2 v) => v * (1 / v.Length);

    public static double PointSegmentDistance(P2 p, P2 a, P2 b)
    {
        var ab = b - a;
        var l2 = P2.Dot(ab, ab);
        var t = l2 == 0 ? 0 : Math.Clamp(P2.Dot(p - a, ab) / l2, 0, 1);
        return (p - (a + ab * t)).Length;
    }

    public static double SegmentDistance(P2 a, P2 b, P2 c, P2 d)
    {
        double o1 = P2.Orient(a, b, c), o2 = P2.Orient(a, b, d), o3 = P2.Orient(c, d, a), o4 = P2.Orient(c, d, b);
        if (((o1 > 0 && o2 < 0) || (o1 < 0 && o2 > 0)) && ((o3 > 0 && o4 < 0) || (o3 < 0 && o4 > 0))) return 0;
        return Math.Min(Math.Min(PointSegmentDistance(a, c, d), PointSegmentDistance(b, c, d)),
            Math.Min(PointSegmentDistance(c, a, b), PointSegmentDistance(d, a, b)));
    }
}
