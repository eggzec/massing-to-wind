using M2W.Geometry;

namespace M2W.Checker;

/// <summary>
/// Counts pairs of triangles in one shell that meet anywhere other than along the vertices/edge they share.
/// Broad phase: sweep and prune on X over triangle bounding boxes (deterministic, exact pruning).
/// Narrow phase, by how many vertex indices the pair shares:
/// <list type="bullet">
/// <item>0 — any contact at all (exact segment/triangle tests, <see cref="Geom.TouchTol"/>).</item>
/// <item>1 — the edge opposite the shared vertex touches the other triangle, or an edge leaving the shared vertex
/// lies in the other triangle's plane and points into (or along a side of) its corner at that vertex. Together these
/// cover every way two such triangles can meet beyond the vertex.</item>
/// <item>2 — the two faces fold onto each other: coplanar with both third vertices on the same side of the edge.</item>
/// <item>3 — duplicate face.</item>
/// </list>
/// </summary>
public static class SelfIntersection
{
    /// <summary>Angle (radians, as a sine) below which directions count as parallel or in-plane: double rounding only.</summary>
    public const double AngleTol = 1e-10;

    public static int CountIntersectingPairs(Shell shell, int stopAfter = int.MaxValue)
    {
        var n = shell.Triangles.Count;
        var order = Enumerable.Range(0, n).OrderBy(i => shell.TriBounds[i].Min.X).ThenBy(i => i).ToArray();
        var count = 0;
        for (var oi = 0; oi < n; oi++)
        {
            var i = order[oi];
            var bi = shell.TriBounds[i];
            for (var oj = oi + 1; oj < n; oj++)
            {
                var j = order[oj];
                var bj = shell.TriBounds[j];
                if (bj.Min.X > bi.Max.X + Geom.TouchTol) break;
                if (Box.Distance(bi, bj) > Geom.TouchTol) continue;
                if (PairIntersects(shell, i, j))
                {
                    count++;
                    if (count >= stopAfter) return count;
                }
            }
        }
        return count;
    }

    public static bool PairIntersects(Shell shell, int i, int j)
    {
        var ti = shell.Triangles[i];
        var tj = shell.Triangles[j];
        int[] vi = { ti.A, ti.B, ti.C };
        int[] vj = { tj.A, tj.B, tj.C };
        var shared = vi.Where(v => vj.Contains(v)).Distinct().ToArray();
        var P = shell.Vertices;

        switch (shared.Length)
        {
            case 0:
                return Geom.TrianglesTouch(shell.Tris[i], shell.Tris[j]);

            case 1:
            {
                var s = shared[0];
                var oi = vi.Where(v => v != s).ToArray();
                var oj = vj.Where(v => v != s).ToArray();
                var tri = shell.Tris;
                if (Geom.SegmentTouchesTriangle(P[oi[0]], P[oi[1]], tri[j])) return true;
                if (Geom.SegmentTouchesTriangle(P[oj[0]], P[oj[1]], tri[i])) return true;
                foreach (var o in oi) if (EdgeEntersCorner(P[o] - P[s], P[oj[0]] - P[s], P[oj[1]] - P[s])) return true;
                foreach (var o in oj) if (EdgeEntersCorner(P[o] - P[s], P[oi[0]] - P[s], P[oi[1]] - P[s])) return true;
                return false;
            }

            case 2:
            {
                Vec3 u = P[shared[0]], w = P[shared[1]];
                var a = P[vi.First(v => v != shared[0] && v != shared[1])];
                var b = P[vj.First(v => v != shared[0] && v != shared[1])];
                var axis = w - u;
                var na = Vec3.Cross(axis, a - u);
                var nb = Vec3.Cross(axis, b - u);
                var la = na.Length;
                var lb = nb.Length;
                if (!(la > 0) || !(lb > 0)) return true; // degenerate face lying on the shared edge
                // Fold: both faces on the same side of the edge and (numerically) in the same plane.
                var cos = Vec3.Dot(na, nb) / (la * lb);
                var distB = Math.Abs(Vec3.Dot(b - u, na / la));
                return cos > 0 && distB <= Geom.TouchTol;
            }

            default:
                return true; // the same three vertices twice
        }
    }

    /// <summary>
    /// Does an edge leaving the shared vertex (direction <paramref name="u"/>) run into the other triangle, whose
    /// sides leave the same vertex along <paramref name="p"/> and <paramref name="q"/>? Only possible if the edge lies
    /// in that triangle's plane; then it does if its direction is inside the corner between p and q or along one of
    /// them (an overlap). Purely angular, so a near-collinear but valid fan is not mistaken for contact.
    /// </summary>
    private static bool EdgeEntersCorner(Vec3 u, Vec3 p, Vec3 q)
    {
        double lu = u.Length, lp = p.Length, lq = q.Length;
        if (!(lu > 0) || !(lp > 0) || !(lq > 0)) return true; // zero-length edge at a shared vertex: degenerate
        var uh = u / lu;
        var ph = p / lp;
        var qh = q / lq;
        var n = Vec3.Cross(ph, qh);
        var ln = n.Length;
        if (ln <= AngleTol)
            // The other triangle is a needle: overlap only along its sides.
            return Vec3.Cross(uh, ph).Length <= AngleTol && Vec3.Dot(uh, ph) > 0
                || Vec3.Cross(uh, qh).Length <= AngleTol && Vec3.Dot(uh, qh) > 0;
        var nh = n / ln;
        if (Math.Abs(Vec3.Dot(uh, nh)) > AngleTol) return false; // leaves the plane: meets it only at the vertex
        return Vec3.Dot(Vec3.Cross(ph, uh), nh) >= -AngleTol && Vec3.Dot(Vec3.Cross(uh, qh), nh) >= -AngleTol;
    }
}
