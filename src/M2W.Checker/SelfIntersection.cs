using M2W.Geometry;

namespace M2W.Checker;

/// <summary>
/// Counts pairs of triangles in one shell that meet anywhere other than along the vertices/edge they share.
/// Broad phase: sweep and prune on X over triangle bounding boxes (deterministic, exact pruning).
/// Narrow phase, by how many vertex indices the pair shares:
/// <list type="bullet">
/// <item>0 — any contact at all (exact segment/triangle tests, <see cref="Geom.TouchTol"/>).</item>
/// <item>1 — the edge opposite the shared vertex touches the other triangle, or an edge leaving the shared vertex
/// runs into the other triangle (tested on the edge with a short piece next to the shared vertex removed).</item>
/// <item>2 — the two faces fold onto each other: coplanar with both third vertices on the same side of the edge.</item>
/// <item>3 — duplicate face.</item>
/// </list>
/// </summary>
public static class SelfIntersection
{
    /// <summary>Length cut from an edge at a shared vertex before testing it (metres, or half the edge if shorter).</summary>
    public const double SharedVertexCut = 1e-4;

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
                foreach (var o in oi) if (Geom.SegmentTouchesTriangle(CutStart(P[s], P[o]), P[o], tri[j])) return true;
                foreach (var o in oj) if (Geom.SegmentTouchesTriangle(CutStart(P[s], P[o]), P[o], tri[i])) return true;
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

    private static Vec3 CutStart(Vec3 shared, Vec3 other)
    {
        var d = other - shared;
        var len = d.Length;
        var cut = Math.Min(SharedVertexCut, 0.5 * len);
        return len > 0 ? shared + d * (cut / len) : shared;
    }
}
