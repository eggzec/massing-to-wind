using M2W.Geometry;
using M2W.Repair.Model;

namespace M2W.Repair.Planar;

/// <summary>A 2D point (plan coordinates in metres).</summary>
public readonly record struct P2(double X, double Y)
{
    public static P2 operator +(P2 a, P2 b) => new(a.X + b.X, a.Y + b.Y);
    public static P2 operator -(P2 a, P2 b) => new(a.X - b.X, a.Y - b.Y);
    public static P2 operator *(P2 a, double s) => new(a.X * s, a.Y * s);
    public static double Cross(P2 a, P2 b) => a.X * b.Y - a.Y * b.X;
    public static double Dot(P2 a, P2 b) => a.X * b.X + a.Y * b.Y;
    public double Length => Math.Sqrt(X * X + Y * Y);

    /// <summary>Twice the signed area of (a, b, c); positive when counter-clockwise.</summary>
    public static double Orient(P2 a, P2 b, P2 c) => Cross(b - a, c - a);
}

/// <summary>
/// Ear-clipping triangulation of a simple polygon with holes. Unlike common ear clippers it keeps collinear vertices
/// (they are shared with neighbouring faces, so dropping one would leave a T-junction), and it never emits a
/// zero-area triangle: an ear must be strictly convex and contain no other vertex, even on its boundary.
/// Holes are joined to the outer ring by bridges to a visible vertex.
/// </summary>
public static class Triangulator
{
    private sealed class Node
    {
        public required int Index;
        public required P2 P;
        public Node Prev = null!;
        public Node Next = null!;
    }

    /// <summary>Signed area (positive when counter-clockwise).</summary>
    public static double SignedArea(IReadOnlyList<P2> ring)
    {
        double s = 0;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            s += P2.Cross(ring[j], ring[i]);
        return s * 0.5;
    }

    /// <summary>
    /// Triangulates <paramref name="outer"/> minus <paramref name="holes"/>. Vertex numbers in the result count the
    /// outer ring first, then each hole in order. Triangles are counter-clockwise. Either ring orientation is accepted.
    /// Returns false if no valid triangulation was found (degenerate or self-intersecting input).
    /// </summary>
    public static bool TryTriangulate(IReadOnlyList<P2> outer, IReadOnlyList<IReadOnlyList<P2>>? holes, List<Tri> result)
    {
        result.Clear();
        if (outer.Count < 3) return false;

        var scale = 0.0;
        foreach (var p in outer) scale = Math.Max(scale, Math.Max(Math.Abs(p.X), Math.Abs(p.Y)));
        var eps = Math.Max(1e-12, 1e-15 * scale * scale);

        var head = Ring(outer, 0, counterClockwise: true);
        var offset = outer.Count;
        if (holes is { Count: > 0 })
        {
            var holeHeads = new List<Node>();
            foreach (var h in holes)
            {
                if (h.Count >= 3) holeHeads.Add(Ring(h, offset, counterClockwise: false));
                offset += h.Count;
            }
            // Bridge the hole whose leftmost point is furthest left first (classic order: keeps bridges short).
            holeHeads.Sort((a, b) =>
            {
                var c = Leftmost(a).P.X.CompareTo(Leftmost(b).P.X);
                return c != 0 ? c : Leftmost(a).Index.CompareTo(Leftmost(b).Index);
            });
            var pending = new List<Node>(holeHeads);
            foreach (var hh in holeHeads)
            {
                pending.Remove(hh);
                if (!Bridge(head, hh, pending, eps)) return false;
            }
        }
        var ringNodes = Walk(head).ToList();
        if (ringNodes.Count <= OptimalMaxVertices && TryOptimal(ringNodes, result, eps)) return true;
        result.Clear();
        return Clip(head, result, eps);
    }

    /// <summary>Polygons up to this many vertices (after hole bridging) get the optimal triangulation.</summary>
    public const int OptimalMaxVertices = 150;

    /// <summary>
    /// Optimal triangulation by dynamic programming over valid diagonals: maximises the smallest triangle quality
    /// (then the sum). Unlike greedy ear clipping it cannot strand a run of nearly collinear vertices at the end,
    /// which would force near-zero-area triangles.
    /// </summary>
    private static bool TryOptimal(List<Node> ring, List<Tri> result, double eps)
    {
        var n = ring.Count;
        var p = ring.Select(r => r.P).ToArray();
        var ok = new bool[n, n];
        for (var i = 0; i < n; i++)
        {
            ok[i, (i + 1) % n] = ok[(i + 1) % n, i] = true;
            for (var j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue;
                ok[i, j] = ok[j, i] = ValidDiagonal(p, i, j, eps);
            }
        }
        var bestMin = new double[n, n];
        var bestSum = new double[n, n];
        var choice = new int[n, n];
        for (var len = 2; len < n; len++)
            for (var i = 0; i + len < n; i++)
            {
                var j = i + len;
                bestMin[i, j] = double.NegativeInfinity;
                choice[i, j] = -1;
                if (!ok[i, j]) continue;
                for (var k = i + 1; k < j; k++)
                {
                    if (!ok[i, k] || !ok[k, j]) continue;
                    if (k - i >= 2 && choice[i, k] < 0) continue;
                    if (j - k >= 2 && choice[k, j] < 0) continue;
                    if (P2.Orient(p[i], p[k], p[j]) <= eps) continue;
                    var q = Quality(p[i], p[k], p[j]);
                    var m = q;
                    var sum = q;
                    if (k - i >= 2) { m = Math.Min(m, bestMin[i, k]); sum += bestSum[i, k]; }
                    if (j - k >= 2) { m = Math.Min(m, bestMin[k, j]); sum += bestSum[k, j]; }
                    if (m > bestMin[i, j] + 1e-12 || (Math.Abs(m - bestMin[i, j]) <= 1e-12 && sum > bestSum[i, j]))
                    {
                        bestMin[i, j] = m; bestSum[i, j] = sum; choice[i, j] = k;
                    }
                }
            }
        if (choice[0, n - 1] < 0) return false;
        var stack = new Stack<(int, int)>();
        stack.Push((0, n - 1));
        while (stack.Count > 0)
        {
            var (i, j) = stack.Pop();
            if (j - i < 2) continue;
            var k = choice[i, j];
            result.Add(new Tri(ring[i].Index, ring[k].Index, ring[j].Index));
            stack.Push((i, k));
            stack.Push((k, j));
        }
        return true;
    }

    /// <summary>True if the segment between ring vertices i and j lies strictly inside the polygon.</summary>
    private static bool ValidDiagonal(P2[] p, int i, int j, double eps)
    {
        var n = p.Length;
        var a = p[i]; var b = p[j];
        if (a == b) return false;
        if (!InCone(p[(i - 1 + n) % n], a, p[(i + 1) % n], b, eps)) return false;
        if (!InCone(p[(j - 1 + n) % n], b, p[(j + 1) % n], a, eps)) return false;
        for (var k = 0; k < n; k++)
        {
            var c = p[k]; var d = p[(k + 1) % n];
            var o1 = P2.Orient(a, b, c); var o2 = P2.Orient(a, b, d);
            var o3 = P2.Orient(c, d, a); var o4 = P2.Orient(c, d, b);
            if (((o1 > eps && o2 < -eps) || (o1 < -eps && o2 > eps)) && ((o3 > eps && o4 < -eps) || (o3 < -eps && o4 > eps)))
                return false;
            // A vertex on the open diagonal (other than copies of its own ends) blocks it.
            if (c != a && c != b && Math.Abs(o1) <= eps && StrictlyBetween(a, b, c)) return false;
        }
        return true;
    }

    private static bool StrictlyBetween(P2 a, P2 b, P2 c)
    {
        var d = b - a;
        var t = P2.Dot(c - a, d);
        return t > 0 && t < P2.Dot(d, d);
    }

    /// <summary>True if q lies strictly inside the interior angle prev-v-next of a counter-clockwise ring.</summary>
    private static bool InCone(P2 prev, P2 v, P2 next, P2 q, double eps)
    {
        var leftIn = P2.Orient(prev, v, q) > eps;
        var leftOut = P2.Orient(v, next, q) > eps;
        return P2.Orient(prev, v, next) > eps ? leftIn && leftOut : leftIn || leftOut;
    }

    private static Node Ring(IReadOnlyList<P2> pts, int offset, bool counterClockwise)
    {
        var ccw = SignedArea(pts) > 0;
        var forward = ccw == counterClockwise;
        Node? first = null, last = null;
        for (var k = 0; k < pts.Count; k++)
        {
            var i = forward ? k : pts.Count - 1 - k;
            var n = new Node { Index = offset + i, P = pts[i] };
            if (first is null) first = n;
            else { last!.Next = n; n.Prev = last; }
            last = n;
        }
        last!.Next = first!;
        first!.Prev = last;
        return first;
    }

    private static Node Leftmost(Node start)
    {
        var best = start;
        var n = start.Next;
        while (n != start)
        {
            if (n.P.X < best.P.X || (n.P.X == best.P.X && n.P.Y < best.P.Y)) best = n;
            n = n.Next;
        }
        return best;
    }

    private static IEnumerable<Node> Walk(Node start)
    {
        var n = start;
        do { yield return n; n = n.Next; } while (n != start);
    }

    /// <summary>Joins a hole ring into the outer ring with a two-way bridge from the hole's leftmost vertex.</summary>
    private static bool Bridge(Node outer, Node holeHead, List<Node> otherHoles, double eps)
    {
        var p = Leftmost(holeHead);
        Node? best = null;
        var bestD = double.PositiveInfinity;
        // Prefer vertices to the left (the bridge then leaves the hole outward), fall back to any visible vertex.
        for (var pass = 0; pass < 2 && best is null; pass++)
            foreach (var m in Walk(outer))
            {
                if (pass == 0 && !(m.P.X < p.P.X)) continue;
                var d = (m.P - p.P).Length;
                if (d >= bestD || d == 0) continue;
                if (!InWedge(m, p.P, eps)) continue;
                if (!Visible(m, p, outer, holeHead, otherHoles, eps)) continue;
                best = m;
                bestD = d;
            }
        if (best is null) return false;

        // outer: ... best -> p -> (hole ring) -> p' -> best' -> best.Next ...
        var m2 = new Node { Index = best.Index, P = best.P };
        var p2 = new Node { Index = p.Index, P = p.P };
        var bestNext = best.Next;
        var pPrev = p.Prev;
        best.Next = p; p.Prev = best;
        pPrev.Next = p2; p2.Prev = pPrev;
        p2.Next = m2; m2.Prev = p2;
        m2.Next = bestNext; bestNext.Prev = m2;
        return true;
    }

    /// <summary>True if direction m→q points into the polygon interior at m (inside the angle prev-m-next).</summary>
    private static bool InWedge(Node m, P2 q, double eps)
    {
        var a = m.Prev.P; var b = m.P; var c = m.Next.P;
        var convex = P2.Orient(a, b, c) > eps;
        var leftOfIn = P2.Orient(a, b, q) > eps;   // q left of a->b
        var leftOfOut = P2.Orient(b, c, q) > eps;  // q left of b->c
        return convex ? leftOfIn && leftOfOut : leftOfIn || leftOfOut;
    }

    private static bool Visible(Node m, Node p, Node outer, Node hole, List<Node> otherHoles, double eps)
    {
        bool Blocked(Node ring)
        {
            foreach (var e in Walk(ring))
            {
                var f = e.Next;
                if (SegmentsTouch(m.P, p.P, e.P, f.P, eps, m.P, p.P)) return true;
            }
            return false;
        }
        if (Blocked(outer) || Blocked(hole)) return false;
        foreach (var h in otherHoles) if (Blocked(h)) return false;
        return true;
    }

    /// <summary>True if segment ab and segment cd share any point other than the excluded endpoints of ab.</summary>
    private static bool SegmentsTouch(P2 a, P2 b, P2 c, P2 d, double eps, P2 exA, P2 exB)
    {
        // Edges incident to a bridge endpoint are allowed to meet it there.
        var cIsEnd = c == exA || c == exB;
        var dIsEnd = d == exA || d == exB;
        var o1 = P2.Orient(a, b, c);
        var o2 = P2.Orient(a, b, d);
        var o3 = P2.Orient(c, d, a);
        var o4 = P2.Orient(c, d, b);
        if (((o1 > eps && o2 < -eps) || (o1 < -eps && o2 > eps)) && ((o3 > eps && o4 < -eps) || (o3 < -eps && o4 > eps)))
            return true; // proper crossing
        // A vertex of cd lying on ab (other than at the bridge endpoints) blocks the bridge.
        if (!cIsEnd && Math.Abs(o1) <= eps && OnSegment(a, b, c)) return true;
        if (!dIsEnd && Math.Abs(o2) <= eps && OnSegment(a, b, d)) return true;
        return false;
    }

    private static bool OnSegment(P2 a, P2 b, P2 p) =>
        Math.Min(a.X, b.X) <= p.X && p.X <= Math.Max(a.X, b.X) && Math.Min(a.Y, b.Y) <= p.Y && p.Y <= Math.Max(a.Y, b.Y);

    private static bool Clip(Node head, List<Tri> result, double eps)
    {
        var count = Walk(head).Count();
        var cur = head;
        while (count > 3)
        {
            Node? ear = null;
            if (count <= 64)
            {
                // Best ear: the one whose smallest angle is largest (fewer thin triangles).
                var bestQ = double.NegativeInfinity;
                var n = cur;
                for (var k = 0; k < count; k++, n = n.Next)
                {
                    if (!IsEar(n, eps)) continue;
                    var q = Quality(n.Prev.P, n.P, n.Next.P);
                    if (q > bestQ) { bestQ = q; ear = n; }
                }
            }
            else
            {
                var n = cur;
                for (var k = 0; k < count; k++, n = n.Next)
                    if (IsEar(n, eps)) { ear = n; break; }
            }
            if (ear is null) return false;
            result.Add(new Tri(ear.Prev.Index, ear.Index, ear.Next.Index));
            ear.Prev.Next = ear.Next;
            ear.Next.Prev = ear.Prev;
            cur = ear.Next;
            count--;
        }
        if (P2.Orient(cur.Prev.P, cur.P, cur.Next.P) <= eps) return false;
        result.Add(new Tri(cur.Prev.Index, cur.Index, cur.Next.Index));
        return true;
    }

    private static bool IsEar(Node b, double eps)
    {
        var a = b.Prev; var c = b.Next;
        if (P2.Orient(a.P, b.P, c.P) <= eps) return false;
        for (var p = c.Next; p != a; p = p.Next)
        {
            var q = p.P;
            if (q == a.P || q == b.P || q == c.P) continue; // bridge copies of the ear's own corners
            if (P2.Orient(a.P, b.P, q) >= -eps && P2.Orient(b.P, c.P, q) >= -eps && P2.Orient(c.P, a.P, q) >= -eps)
                return false;
        }
        return true;
    }

    /// <summary>Sine of the smallest angle, a cheap triangle quality measure in [0, ~0.87].</summary>
    private static double Quality(P2 a, P2 b, P2 c)
    {
        var area2 = Math.Abs(P2.Orient(a, b, c));
        var ab = (b - a).Length; var bc = (c - b).Length; var ca = (a - c).Length;
        var m = Math.Max(ab * bc, Math.Max(bc * ca, ca * ab));
        return m > 0 ? area2 / m : 0;
    }

    /// <summary>
    /// Triangulates a planar 3D polygon (indices into <paramref name="vertices"/>), keeping its orientation.
    /// Returns false for degenerate polygons.
    /// </summary>
    public static bool TryTriangulate3D(IReadOnlyList<Vec3> vertices, IReadOnlyList<int> polygon, List<Tri> result)
    {
        result.Clear();
        if (polygon.Count == 3)
        {
            var n3 = Vec3.Cross(vertices[polygon[1]] - vertices[polygon[0]], vertices[polygon[2]] - vertices[polygon[0]]);
            if (n3.LengthSquared == 0) return false;
            result.Add(new Tri(polygon[0], polygon[1], polygon[2]));
            return true;
        }
        var n = Newell(vertices, polygon);
        if (n.LengthSquared == 0) return false;
        var ax = Math.Abs(n.X); var ay = Math.Abs(n.Y); var az = Math.Abs(n.Z);
        var pts = new List<P2>(polygon.Count);
        // Drop the dominant axis; keep a right-handed (u, v) pair so a counter-clockwise ring stays counter-clockwise
        // when seen along the normal.
        foreach (var i in polygon)
        {
            var v = vertices[i];
            if (az >= ax && az >= ay) pts.Add(n.Z > 0 ? new P2(v.X, v.Y) : new P2(v.Y, v.X));
            else if (ax >= ay) pts.Add(n.X > 0 ? new P2(v.Y, v.Z) : new P2(v.Z, v.Y));
            else pts.Add(n.Y > 0 ? new P2(v.Z, v.X) : new P2(v.X, v.Z));
        }
        var local = new List<Tri>();
        if (!TryTriangulate(pts, null, local)) return false;
        var reverse = SignedArea(pts) < 0;
        foreach (var t in local)
        {
            var tt = new Tri(polygon[t.A], polygon[t.B], polygon[t.C]);
            result.Add(reverse ? tt.Reversed() : tt);
        }
        return true;
    }

    /// <summary>Newell normal of a polygon: twice its area times its unit normal.</summary>
    public static Vec3 Newell(IReadOnlyList<Vec3> vertices, IReadOnlyList<int> polygon)
    {
        var c = vertices[polygon[0]];
        var s = Vec3.Zero;
        for (var k = 0; k < polygon.Count; k++)
        {
            var a = vertices[polygon[k]] - c;
            var b = vertices[polygon[(k + 1) % polygon.Count]] - c;
            s += Vec3.Cross(a, b);
        }
        return s;
    }
}
