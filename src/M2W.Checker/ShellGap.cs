using M2W.Geometry;

namespace M2W.Checker;

/// <summary>Smallest distance between the solids of two shells (0 if they touch, cross or one is inside the other).</summary>
public static class ShellGap
{
    public readonly record struct Result(double Distance, int ShellA, int ShellB);

    /// <summary>
    /// The closest pair of shells and their distance, found exactly with bounding-box pruning
    /// (pairs are visited nearest-box first and skipped once their box distance cannot beat the best so far).
    /// Null if there are fewer than two shells.
    /// </summary>
    public static Result? Closest(IReadOnlyList<Shell> shells)
    {
        var pairs = new List<(double Lower, int A, int B)>();
        for (var a = 0; a < shells.Count; a++)
            for (var b = a + 1; b < shells.Count; b++)
                pairs.Add((Box.Distance(shells[a].Bounds, shells[b].Bounds), a, b));
        if (pairs.Count == 0) return null;
        pairs.Sort((x, y) => x.Lower != y.Lower ? x.Lower.CompareTo(y.Lower) : x.A != y.A ? x.A.CompareTo(y.A) : x.B.CompareTo(y.B));

        Result? best = null;
        foreach (var (lower, a, b) in pairs)
        {
            if (best is { } r && lower >= r.Distance) break;
            var d = Distance(shells[a], shells[b], best?.Distance ?? double.PositiveInfinity);
            if (best is null || d < best.Value.Distance) best = new Result(d, a, b);
            if (best.Value.Distance <= 0) break;
        }
        return best;
    }

    /// <summary>Distance between two shells' solids, or a value ≥ <paramref name="upperBound"/> if it is not smaller.</summary>
    public static double Distance(Shell s, Shell t, double upperBound = double.PositiveInfinity)
    {
        if (s.Tris.Count == 0 || t.Tris.Count == 0) return upperBound;

        // One shell inside the other: no surface contact, but the solids overlap.
        if (Box.Distance(s.Bounds, t.Bounds) <= 0)
        {
            if (Math.Abs(s.Winding(t.Tris[0].A)) > 0.5) return 0;
            if (Math.Abs(t.Winding(s.Tris[0].A)) > 0.5) return 0;
        }

        var best = upperBound;
        for (var i = 0; i < s.Tris.Count; i++)
        {
            var bi = s.TriBounds[i];
            if (Box.Distance(bi, t.Bounds) >= best) continue;
            for (var j = 0; j < t.Tris.Count; j++)
            {
                if (Box.Distance(bi, t.TriBounds[j]) >= best) continue;
                var d = Geom.TriangleTriangleDistance(s.Tris[i], t.Tris[j]);
                if (d < best)
                {
                    best = d;
                    if (best <= 0) return 0;
                }
            }
        }
        return best;
    }
}
