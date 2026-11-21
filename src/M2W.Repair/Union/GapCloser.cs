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
/// together; a strip shorter than minEdge along the walls does not count). Components with no such pair (for example
/// two blocks meeting corner to corner) get one convex hull of the parts of their walls that lie within
/// minGap + minEdge of a wall of the other component and in front of it, a small fillet like a disk closing would
/// give, wide enough that merging points closer than minEdge cannot pinch it.
///
/// Gaps inside one component (a narrow inlet of a U shape) are not closed: AGREEMENT.md asks for gaps between blocks
/// to be closed and the blocks merged.
/// </summary>
public static class GapCloser
{
    /// <summary>A polygon to fill from the ground up to <see cref="Z"/>.</summary>
    public sealed record Bridge(IReadOnlyList<P2> Ring, double Z);

    /// <summary>Two components closer than minGap at one level: their faces, the gap width, and whether it was bridged.</summary>
    /// <summary>Two components closer than minGap at one level: their faces, the gap width, whether it was bridged,
    /// and where (the bridges, or the near walls if none).</summary>
    public sealed record Gap(int FaceA, int FaceB, double Width, double Z, bool Bridged, PlanBox Box);

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

    /// <param name="minEdge">Bridges must survive the final plan, where points closer than this merge: a facing
    /// strip shorter than this along the walls does not count, and fillets reach this much further.</param>
    public static Found Find(Dcel d, double[] height, double minGap, double tol, double minEdge = 0)
    {
        var found = new Found();
        if (minGap <= 0) return found;
        var g = minGap - Slack;
        var levels = height.Where(h => h > 0).Distinct().OrderBy(h => h).ToList();
        var perLevel = FullScan ? ScanAll(d, height, levels, g, tol, minEdge) : ScanNear(d, height, levels, g, tol, minEdge);
        foreach (var lf in perLevel)
        {
            if (lf is null) continue;
            found.Gaps.AddRange(lf.Gaps);
            found.Bridges.AddRange(lf.Bridges);
        }
        return found;
    }

    /// <summary>Test switch: scan every level over the whole plan (<see cref="ScanAll"/>) instead of
    /// <see cref="ScanNear"/>; the two must agree.</summary>
    internal static bool FullScan { get; set; }

    /// <summary>
    /// Margin added to the gap width when picking the plan edges that can be part of a gap. A wall is a chain of
    /// nearly collinear plan edges (each vertex within weld of the line through its neighbours) and its distance is
    /// measured along the chord of the chain; the margin covers a chord up to 5 mm off its edges. Only a curved
    /// outline bends further; there a pair of walls is left alone when its edges are truly at least minGap apart
    /// (D-038).
    /// </summary>
    private const double SkipMargin = 0.01;

    /// <summary>Every level: components and walls of the whole slice, then the gaps between them.</summary>
    private static Found?[] ScanAll(Dcel d, double[] height, List<double> levels, double g, double tol, double minEdge)
    {
        var result = new Found?[levels.Count];
        for (var k = 0; k < levels.Count; k++)
        {
            var h = levels[k];
            bool Inside(int f) => height[f] >= h;
            var comp = Components(d, Inside);
            result[k] = GapsAtLevel(Walls(d, Inside, comp, tol), h, g, tol, minEdge);
        }
        return result;
    }

    /// <summary>
    /// The same gaps as <see cref="ScanAll"/>, with work only where walls of different components come close. Plan
    /// edges where the height changes bound the slices of a run of levels; the pairs of such edges closer than
    /// minGap (plus <see cref="SkipMargin"/>) are found once, with a grid. The levels are then swept from the top down
    /// with one union-find of the slice's faces (slices only grow downward, so faces once joined stay joined, and the
    /// root of a component is its lowest face number, as <see cref="Components"/> numbers it). At each level only the
    /// walls through an edge of a close pair still in two components are built, each exactly as <see cref="Walls"/>
    /// would chain it (the chaining at a vertex depends only on the edges at that vertex), and kept in its order.
    /// Any wall closer than minGap to a wall of another component holds such an edge, so the gaps found are the same.
    /// </summary>
    private static Found?[] ScanNear(Dcel d, double[] height, List<double> levels, double g, double tol, double minEdge)
    {
        var n = levels.Count;
        var result = new Found?[n];
        var reach = g + SkipMargin;
        int Above(double h) // index of the first level higher than h
        {
            int lo = 0, hi = n;
            while (lo < hi) { var m = (lo + hi) >> 1; if (levels[m] <= h) lo = m + 1; else hi = m; }
            return lo;
        }
        // Plan edges where the height changes, as the half-edge with the higher face on its left: walls of the
        // slices of levels [from, to).
        var edges = new List<(int half, int from, int to)>();
        for (var h = 0; h < d.From.Length; h += 2)
        {
            double hl = height[d.FaceOf(h)], hr = height[d.FaceOf(h ^ 1)];
            if (hl == hr) continue;
            edges.Add((hl > hr ? h : h ^ 1, Above(Math.Min(hl, hr)), Above(Math.Max(hl, hr))));
        }
        var pts = d.Points;
        var boxes = edges.Select(e =>
        {
            P2 a = pts[d.From[e.half]], b = pts[d.To[e.half]];
            return (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }).ToList();
        // Close pairs, listed at the highest level where both are walls; they count down to `bottom`.
        var startingAt = new List<(int x, int y, int bottom)>?[n];
        foreach (var (i, j) in BoxGrid.Pairs(boxes, reach / 2))
        {
            var (x, y) = (edges[i], edges[j]);
            if (d.FaceOf(x.half) == d.FaceOf(y.half)) continue;
            int bottom = Math.Max(x.from, y.from), top = Math.Min(x.to, y.to) - 1;
            if (top < bottom) continue;
            if (SegmentDistance(pts[d.From[x.half]], pts[d.To[x.half]], pts[d.From[y.half]], pts[d.To[y.half]]) >= reach) continue;
            (startingAt[top] ??= new()).Add((x.half, y.half, bottom));
        }
        if (startingAt.All(s => s is null)) return result;

        var parent = Enumerable.Range(0, d.FaceCount).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        var facesAt = new List<int>?[n];
        for (var f = 1; f < d.FaceCount; f++)
            if (height[f] > 0) (facesAt[Above(height[f]) - 1] ??= new()).Add(f);
        // Half-edges leaving each vertex, ascending (the order Walls meets them in).
        var outgoing = new Dictionary<int, List<int>>();
        for (var h = 0; h < d.From.Length; h++)
        {
            if (!outgoing.TryGetValue(d.From[h], out var l)) outgoing[d.From[h]] = l = new List<int>();
            l.Add(h);
        }
        var open = new List<(int x, int y, int bottom)>();
        var memo = new Dictionary<List<Wall>, Found?>(WallsComparer.Instance);
        for (var k = n - 1; k >= 0; k--)
        {
            var z = levels[k];
            if (facesAt[k] is { } add)
                foreach (var f in add)
                    foreach (var c in FaceCycles(d, f))
                        foreach (var he in d.Cycles[c])
                        {
                            var o = d.FaceOf(he ^ 1);
                            if (height[o] < z) continue;
                            int a = Find(f), b = Find(o);
                            if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
                        }
            if (startingAt[k] is { } start) open.AddRange(start);
            var hot = new SortedSet<int>();
            for (var i = open.Count - 1; i >= 0; i--)
            {
                var (x, y, bottom) = open[i];
                if (k < bottom || Find(d.FaceOf(x)) == Find(d.FaceOf(y)))
                {
                    open[i] = open[^1];
                    open.RemoveAt(open.Count - 1);
                }
                else { hot.Add(x); hot.Add(y); }
            }
            if (hot.Count == 0) continue;
            var ww = WallsThrough(d, height, z, hot, outgoing, Find, tol);
            // A gap below the lower of two roofs is found again on every level down to the ground, with the same
            // walls; what is found depends only on the walls (the level only labels it).
            if (!memo.TryGetValue(ww, out var same)) memo[ww] = same = GapsAtLevel(ww, 0, g, tol, minEdge);
            result[k] = same is null ? null : AtLevel(same, z);
        }
        return result;
    }

    /// <summary>The gaps and bridges of <paramref name="f"/> moved to level z.</summary>
    private static Found AtLevel(Found f, double z)
    {
        var r = new Found();
        r.Gaps.AddRange(f.Gaps.Select(x => x with { Z = z }));
        r.Bridges.AddRange(f.Bridges.Select(x => x with { Z = z }));
        return r;
    }

    /// <summary>Compares wall lists element by element.</summary>
    private sealed class WallsComparer : IEqualityComparer<List<Wall>>
    {
        public static readonly WallsComparer Instance = new();
        public bool Equals(List<Wall>? x, List<Wall>? y) => ReferenceEquals(x, y) || (x is not null && y is not null && x.SequenceEqual(y));
        public int GetHashCode(List<Wall> walls)
        {
            var h = new HashCode();
            foreach (var w in walls) h.Add(w);
            return h.ToHashCode();
        }
    }

    /// <summary>
    /// The walls of the slice "height ≥ z" that contain one of the <paramref name="hot"/> half-edges, as
    /// <see cref="Walls"/> builds them (same chains, same order), with components from <paramref name="comp"/>.
    /// </summary>
    private static List<Wall> WallsThrough(Dcel d, double[] height, double z, SortedSet<int> hot,
        Dictionary<int, List<int>> outgoing, Func<int, int> comp, double tol)
    {
        var pts = d.Points;
        bool Boundary(int h) => height[d.FaceOf(h)] >= z && height[d.FaceOf(h ^ 1)] < z;
        int Comp(int h) => comp(d.FaceOf(h));
        bool Collinear(int e, int f)
        {
            var a = pts[d.From[e]]; var b = pts[d.To[e]]; var c = pts[d.To[f]];
            var ab = b - a;
            return P2.Dot(ab, c - b) > 0 && Math.Abs(P2.Cross(ab, c - a)) / (c - a).Length <= tol;
        }
        // The only collinear successor of e in its component, or -1 (Walls' candidate list).
        var candidates = new Dictionary<int, int>();
        var preds = new Dictionary<int, int>();
        int Candidate(int e)
        {
            if (candidates.TryGetValue(e, out var known)) return known;
            return candidates[e] = FindCandidate(e);
        }
        int FindCandidate(int e)
        {
            var only = -1;
            foreach (var f in outgoing[d.To[e]])
            {
                if (!Boundary(f) || Comp(f) != Comp(e) || !Collinear(e, f)) continue;
                if (only >= 0) return -1;
                only = f;
            }
            return only;
        }
        // Walls gives f to the first boundary half-edge (ascending) into f's start whose only candidate it is.
        int Pred(int f)
        {
            if (preds.TryGetValue(f, out var known)) return known;
            var p = -1;
            foreach (var e in outgoing[d.From[f]].Select(h => h ^ 1).OrderBy(h => h))
                if (Boundary(e) && Candidate(e) == f) { p = e; break; }
            return preds[f] = p;
        }
        int Succ(int e)
        {
            var f = Candidate(e);
            return f >= 0 && Pred(f) == e ? f : -1;
        }
        var starts = new SortedSet<int>();
        var leftover = new SortedSet<int>();
        var startOf = new Dictionary<int, int>(); // half-edge -> start of its chain (-1: on a closed cycle)
        foreach (var x in hot)
        {
            var path = new List<int> { x };
            var seen = new HashSet<int> { x };
            var s = x;
            while (true)
            {
                if (startOf.TryGetValue(s, out var known)) { s = known; break; }
                var p = Pred(s);
                if (p < 0) break;
                if (!seen.Add(p)) { s = -1; break; } // a closed cycle of collinear edges: each is its own wall
                path.Add(p);
                s = p;
            }
            foreach (var e in path) startOf[e] = s;
            if (s >= 0) starts.Add(s); else leftover.Add(x);
        }
        var walls = new List<Wall>();
        foreach (var s in starts)
        {
            var last = s;
            var used = new HashSet<int> { s };
            while (Succ(last) is var nx && nx >= 0 && used.Add(nx)) last = nx;
            walls.Add(new Wall(pts[d.From[s]], pts[d.To[last]], Comp(s), d.FaceOf(s)));
        }
        foreach (var e in leftover) walls.Add(new Wall(pts[d.From[e]], pts[d.To[e]], Comp(e), d.FaceOf(e)));
        return walls;
    }

    /// <summary>The gaps and bridges at level h between walls of different components.</summary>
    private static Found? GapsAtLevel(List<Wall> walls, double h, double g, double tol, double minEdge)
    {
        if (walls.Select(w => w.Comp).Distinct().Count() < 2) return null;
        var found = new Found();
        var cosPar = Math.Cos(ParallelDeg * Math.PI / 180);

        // Pairs of walls of different components closer than g, grouped by component pair.
        var byPair = new SortedDictionary<(int, int), List<(Wall a, Wall b, double dist)>>();
        var boxes = walls.Select(w => (Math.Min(w.A.X, w.B.X), Math.Min(w.A.Y, w.B.Y), Math.Max(w.A.X, w.B.X), Math.Max(w.A.Y, w.B.Y))).ToList();
        foreach (var (i, j) in BoxGrid.Pairs(boxes, g / 2))
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
                if (r is not null && Math.Max((r[3] - r[0]).Length, (r[2] - r[1]).Length) >= Math.Max(minEdge, tol)) rects.Add(r);
            }
            var polys = rects;
            if (polys.Count == 0)
            {
                // One convex hull of every near part, not one hull per pair of walls: overlapping small hulls
                // leave clusters of points closer than minEdge, which snapping would pinch to a point contact.
                polys = new List<P2[]>();
                var near = new List<P2>();
                foreach (var (a, b, _) in pairs)
                {
                    if (NearPart(a, b, g + minEdge) is { } pa && NearPart(b, a, g + minEdge) is { } pb)
                        near.AddRange(new[] { pa.p, pa.q, pb.p, pb.q });
                }
                var hull = Thick(ConvexHull(near), tol);
                if (hull is not null) polys.Add(hull);
            }
            var width = pairs.Min(p => p.dist);
            var box = polys.Count > 0 ? PlanBox.Of(polys.SelectMany(p => p))
                : PlanBox.Of(pairs.SelectMany(p => new[] { p.a.A, p.a.B, p.b.A, p.b.B }));
            found.Gaps.Add(new Gap(pairs[0].a.Face, pairs[0].b.Face, width, h, polys.Count > 0, box));
            foreach (var p in polys) found.Bridges.Add(new Bridge(p, h));
        }
        return found;
    }

    private static IEnumerable<int> FaceCycles(Dcel d, int f)
    {
        if (d.FaceOuter[f] >= 0) yield return d.FaceOuter[f];
        foreach (var h in d.FaceHoles[f]) yield return h;
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

    /// <summary>The hull as a polygon, or null if it is thinner than the tolerance.</summary>
    private static P2[]? Thick(List<P2> hull, double tol)
    {
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
