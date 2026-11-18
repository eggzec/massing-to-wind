namespace M2W.Generator;

/// <summary>A point in a plan (metres).</summary>
public readonly record struct P2(double X, double Y)
{
    public static P2 operator +(P2 a, P2 b) => new(a.X + b.X, a.Y + b.Y);
    public static P2 operator -(P2 a, P2 b) => new(a.X - b.X, a.Y - b.Y);
    public static P2 operator *(P2 a, double s) => new(a.X * s, a.Y * s);
    public static double Cross(P2 a, P2 b) => a.X * b.Y - a.Y * b.X;
    public static double Dot(P2 a, P2 b) => a.X * b.X + a.Y * b.Y;
    public double Length => Math.Sqrt(X * X + Y * Y);
}

/// <summary>
/// Axis-aligned rectangle in the grid frame (u east-ish, v north-ish). Used for footprints and for tier clips; a clip
/// side that does not cut is set to ±<see cref="Far"/>.
/// </summary>
public readonly record struct Rect(double U0, double V0, double U1, double V1)
{
    public const double Far = 1e7;

    public static readonly Rect Everything = new(-Far, -Far, Far, Far);

    public double Width => U1 - U0;
    public double Depth => V1 - V0;
    public double Area => Width * Depth;

    public bool Contains(double u, double v) => u > U0 && u < U1 && v > V0 && v < V1;

    /// <summary>True if the interiors overlap (positive area).</summary>
    public bool Overlaps(Rect o) => U0 < o.U1 && o.U0 < U1 && V0 < o.V1 && o.V0 < V1;

    public Rect Intersect(Rect o) => new(Math.Max(U0, o.U0), Math.Max(V0, o.V0), Math.Min(U1, o.U1), Math.Min(V1, o.V1));

    public Rect Translate(double du, double dv) =>
        new(Shift(U0, du), Shift(V0, dv), Shift(U1, du), Shift(V1, dv));

    private static double Shift(double c, double d) => Math.Abs(c) >= Far ? c : Math.Round(c + d, 6);

    /// <summary>Rotates by a multiple of 90° about the origin (exact).</summary>
    public Rect Rotate90(int quarterTurns)
    {
        var r = this;
        for (var q = 0; q < ((quarterTurns % 4) + 4) % 4; q++)
            r = new Rect(-r.V1, r.U0, -r.V0, r.U1); // (u, v) -> (-v, u)
        return r;
    }

    /// <summary>Euclidean distance between two rectangles (0 if they touch or overlap).</summary>
    public double DistanceTo(Rect o)
    {
        var du = Math.Max(0, Math.Max(o.U0 - U1, U0 - o.U1));
        var dv = Math.Max(0, Math.Max(o.V0 - V1, V0 - o.V1));
        return Math.Sqrt(du * du + dv * dv);
    }

    public IEnumerable<P2> Corners()
    {
        yield return new P2(U0, V0);
        yield return new P2(U1, V0);
        yield return new P2(U1, V1);
        yield return new P2(U0, V1);
    }

    public static Rect Bounds(IEnumerable<Rect> rects)
    {
        double u0 = double.PositiveInfinity, v0 = double.PositiveInfinity, u1 = double.NegativeInfinity, v1 = double.NegativeInfinity;
        foreach (var r in rects)
        {
            u0 = Math.Min(u0, r.U0); v0 = Math.Min(v0, r.V0);
            u1 = Math.Max(u1, r.U1); v1 = Math.Max(v1, r.V1);
        }
        return new Rect(u0, v0, u1, v1);
    }
}

/// <summary>Convex polygon helpers for site plots (counter-clockwise vertex order).</summary>
public static class Convex
{
    public static double SignedArea(IReadOnlyList<P2> poly)
    {
        double a = 0;
        for (var i = 0; i < poly.Count; i++) a += P2.Cross(poly[i], poly[(i + 1) % poly.Count]);
        return a / 2;
    }

    public static bool IsConvexCcw(IReadOnlyList<P2> poly)
    {
        if (poly.Count < 3) return false;
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            var c = poly[(i + 2) % poly.Count];
            if (P2.Cross(b - a, c - b) <= 1e-9) return false;
        }
        return true;
    }

    /// <summary>True if <paramref name="p"/> is inside or on the boundary of the convex CCW polygon.</summary>
    public static bool Contains(IReadOnlyList<P2> poly, P2 p, double eps = 1e-9)
    {
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            var e = b - a;
            if (P2.Cross(e, p - a) < -eps * e.Length) return false;
        }
        return true;
    }

    /// <summary>Keeps the part of a convex polygon on the left of the directed line through a with direction d,
    /// moved left by <paramref name="offset"/> (Sutherland–Hodgman against one line).</summary>
    public static List<P2> ClipLeft(IReadOnlyList<P2> poly, P2 a, P2 d, double offset)
    {
        var len = d.Length;
        double Side(P2 p) => P2.Cross(d, p - a) / len - offset;
        var output = new List<P2>();
        for (var i = 0; i < poly.Count; i++)
        {
            var cur = poly[i];
            var next = poly[(i + 1) % poly.Count];
            var sc = Side(cur);
            var sn = Side(next);
            if (sc >= 0) output.Add(cur);
            if ((sc >= 0) != (sn >= 0))
            {
                var t = sc / (sc - sn);
                output.Add(cur + (next - cur) * t);
            }
        }
        return output;
    }

    /// <summary>Inward offset (setback) of a convex CCW polygon: the intersection of every edge's half-plane moved
    /// inward by <paramref name="distance"/>. Short edges at acute corners disappear, as a real setback does.</summary>
    public static List<P2> Setback(IReadOnlyList<P2> poly, double distance)
    {
        var result = poly.ToList();
        for (var i = 0; i < poly.Count && result.Count >= 3; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            result = ClipLeft(result, a, b - a, distance);
        }
        return result;
    }
}
