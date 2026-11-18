using M2W.Geometry;

namespace M2W.Checker;

/// <summary>A triangle by value (three points). Used by the exact geometric tests.</summary>
public readonly record struct Tri(Vec3 A, Vec3 B, Vec3 C)
{
    public Vec3 Normal => Vec3.Cross(B - A, C - A);

    public Box Bounds => Box.Of(A, B, C);
}

/// <summary>Axis-aligned box used for broad-phase pruning.</summary>
public readonly record struct Box(Vec3 Min, Vec3 Max)
{
    public static Box Of(Vec3 a, Vec3 b, Vec3 c) => new(
        new Vec3(Math.Min(a.X, Math.Min(b.X, c.X)), Math.Min(a.Y, Math.Min(b.Y, c.Y)), Math.Min(a.Z, Math.Min(b.Z, c.Z))),
        new Vec3(Math.Max(a.X, Math.Max(b.X, c.X)), Math.Max(a.Y, Math.Max(b.Y, c.Y)), Math.Max(a.Z, Math.Max(b.Z, c.Z))));

    public static Box Union(Box a, Box b) => new(
        new Vec3(Math.Min(a.Min.X, b.Min.X), Math.Min(a.Min.Y, b.Min.Y), Math.Min(a.Min.Z, b.Min.Z)),
        new Vec3(Math.Max(a.Max.X, b.Max.X), Math.Max(a.Max.Y, b.Max.Y), Math.Max(a.Max.Z, b.Max.Z)));

    /// <summary>Euclidean distance between two boxes (0 if they overlap). A lower bound on the distance of their contents.</summary>
    public static double Distance(Box a, Box b)
    {
        var dx = Math.Max(0, Math.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
        var dy = Math.Max(0, Math.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
        var dz = Math.Max(0, Math.Max(a.Min.Z - b.Max.Z, b.Min.Z - a.Max.Z));
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public static double Distance(Box a, Vec3 p)
    {
        var dx = Math.Max(0, Math.Max(a.Min.X - p.X, p.X - a.Max.X));
        var dy = Math.Max(0, Math.Max(a.Min.Y - p.Y, p.Y - a.Max.Y));
        var dz = Math.Max(0, Math.Max(a.Min.Z - p.Z, p.Z - a.Max.Z));
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public bool Contains(Box inner) =>
        inner.Min.X >= Min.X && inner.Min.Y >= Min.Y && inner.Min.Z >= Min.Z &&
        inner.Max.X <= Max.X && inner.Max.Y <= Max.Y && inner.Max.Z <= Max.Z;
}

/// <summary>
/// Exact-in-double geometric primitives (closest points after Ericson, "Real-Time Collision Detection", ch. 5).
/// No acceleration here; callers prune with <see cref="Box"/>.
/// </summary>
public static class Geom
{
    /// <summary>
    /// Distance below which two pieces of geometry count as touching. Far below the 1e-6 m resolution of the
    /// 6-decimal OBJ files, far above double rounding at building scale (coordinates up to ~1e4 m local).
    /// </summary>
    public const double TouchTol = 1e-8;

    public static double PointTriangleDistance(Vec3 p, Tri t) => Vec3.Distance(p, ClosestPointOnTriangle(p, t));

    public static Vec3 ClosestPointOnTriangle(Vec3 p, Tri t)
    {
        Vec3 a = t.A, b = t.B, c = t.C;
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        var d1 = Vec3.Dot(ab, ap);
        var d2 = Vec3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;

        var bp = p - b;
        var d3 = Vec3.Dot(ab, bp);
        var d4 = Vec3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;

        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            var v = d1 / (d1 - d3);
            return a + ab * v;
        }

        var cp = p - c;
        var d5 = Vec3.Dot(ab, cp);
        var d6 = Vec3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;

        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            var w = d2 / (d2 - d6);
            return a + ac * w;
        }

        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        {
            var w = (d4 - d3) / (d4 - d3 + (d5 - d6));
            return b + (c - b) * w;
        }

        var denom = va + vb + vc;
        if (!(Math.Abs(denom) > 0)) return ClosestOnDegenerate(p, t);
        var vv = vb / denom;
        var ww = vc / denom;
        return a + ab * vv + ac * ww;
    }

    private static Vec3 ClosestOnDegenerate(Vec3 p, Tri t)
    {
        var best = ClosestPointOnSegment(p, t.A, t.B);
        foreach (var q in new[] { ClosestPointOnSegment(p, t.B, t.C), ClosestPointOnSegment(p, t.C, t.A) })
            if (Vec3.Distance(p, q) < Vec3.Distance(p, best)) best = q;
        return best;
    }

    public static Vec3 ClosestPointOnSegment(Vec3 p, Vec3 a, Vec3 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared;
        if (!(len2 > 0)) return a;
        var s = Math.Clamp(Vec3.Dot(p - a, ab) / len2, 0, 1);
        return a + ab * s;
    }

    /// <summary>Smallest distance between segments p1q1 and p2q2.</summary>
    public static double SegmentSegmentDistance(Vec3 p1, Vec3 q1, Vec3 p2, Vec3 q2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        var a = d1.LengthSquared;
        var e = d2.LengthSquared;
        var f = Vec3.Dot(d2, r);
        double s, t;
        if (a <= 0 && e <= 0) return r.Length;
        if (a <= 0)
        {
            s = 0;
            t = Math.Clamp(f / e, 0, 1);
        }
        else
        {
            var c = Vec3.Dot(d1, r);
            if (e <= 0)
            {
                t = 0;
                s = Math.Clamp(-c / a, 0, 1);
            }
            else
            {
                var b = Vec3.Dot(d1, d2);
                var denom = a * e - b * b;
                s = denom > 0 ? Math.Clamp((b * f - c * e) / denom, 0, 1) : 0;
                t = (b * s + f) / e;
                if (t < 0)
                {
                    t = 0;
                    s = Math.Clamp(-c / a, 0, 1);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = Math.Clamp((b - c) / a, 0, 1);
                }
            }
        }
        return Vec3.Distance(p1 + d1 * s, p2 + d2 * t);
    }

    /// <summary>
    /// True if segment pq touches triangle t (closed sets, within <paramref name="tol"/>). Handles the coplanar
    /// case and degenerate (zero-area) triangles.
    /// </summary>
    public static bool SegmentTouchesTriangle(Vec3 p, Vec3 q, Tri t, double tol = TouchTol)
    {
        var n = t.Normal;
        var nLen = n.Length;
        if (!(nLen > 0) || nLen < 1e-12 * Math.Max(1, (t.B - t.A).LengthSquared + (t.C - t.A).LengthSquared))
        {
            // Zero-area triangle: it is a segment (or point); test against its three sides.
            return SegmentSegmentDistance(p, q, t.A, t.B) <= tol
                || SegmentSegmentDistance(p, q, t.B, t.C) <= tol
                || SegmentSegmentDistance(p, q, t.C, t.A) <= tol;
        }
        var nn = n / nLen;
        var dp = Vec3.Dot(p - t.A, nn);
        var dq = Vec3.Dot(q - t.A, nn);
        if (dp > tol && dq > tol) return false;
        if (dp < -tol && dq < -tol) return false;

        if (Math.Abs(dp) <= tol && Math.Abs(dq) <= tol)
        {
            // Coplanar: an endpoint inside, or the segment meets a side.
            return PointTriangleDistance(p, t) <= tol
                || PointTriangleDistance(q, t) <= tol
                || SegmentSegmentDistance(p, q, t.A, t.B) <= tol
                || SegmentSegmentDistance(p, q, t.B, t.C) <= tol
                || SegmentSegmentDistance(p, q, t.C, t.A) <= tol;
        }

        Vec3 x;
        if (Math.Abs(dp) <= tol) x = p;
        else if (Math.Abs(dq) <= tol) x = q;
        else x = p + (q - p) * (dp / (dp - dq));
        return PointTriangleDistance(x, t) <= tol;
    }

    /// <summary>
    /// True if the two triangles share at least one point (within <paramref name="tol"/>). Two triangles meet
    /// exactly when an edge of one touches the other (convexity), which also covers containment and coplanarity.
    /// </summary>
    public static bool TrianglesTouch(Tri a, Tri b, double tol = TouchTol) =>
        SegmentTouchesTriangle(a.A, a.B, b, tol) || SegmentTouchesTriangle(a.B, a.C, b, tol) || SegmentTouchesTriangle(a.C, a.A, b, tol) ||
        SegmentTouchesTriangle(b.A, b.B, a, tol) || SegmentTouchesTriangle(b.B, b.C, a, tol) || SegmentTouchesTriangle(b.C, b.A, a, tol);

    /// <summary>Smallest distance between two triangles (0 if they touch).</summary>
    public static double TriangleTriangleDistance(Tri a, Tri b)
    {
        if (TrianglesTouch(a, b)) return 0;
        var d = double.PositiveInfinity;
        d = Math.Min(d, PointTriangleDistance(a.A, b));
        d = Math.Min(d, PointTriangleDistance(a.B, b));
        d = Math.Min(d, PointTriangleDistance(a.C, b));
        d = Math.Min(d, PointTriangleDistance(b.A, a));
        d = Math.Min(d, PointTriangleDistance(b.B, a));
        d = Math.Min(d, PointTriangleDistance(b.C, a));
        Vec3[] ea = { a.A, a.B, a.C };
        Vec3[] eb = { b.A, b.B, b.C };
        for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
                d = Math.Min(d, SegmentSegmentDistance(ea[i], ea[(i + 1) % 3], eb[j], eb[(j + 1) % 3]));
        return d;
    }

    /// <summary>
    /// Signed solid angle of triangle t seen from p, divided by 4π (Van Oosterom–Strackee). Summed over a closed
    /// outward shell this is the winding number: 1 inside, 0 outside (−1 inside an inverted shell).
    /// </summary>
    public static double SolidAngleFraction(Vec3 p, Tri t)
    {
        var a = t.A - p;
        var b = t.B - p;
        var c = t.C - p;
        double la = a.Length, lb = b.Length, lc = c.Length;
        var num = Vec3.Dot(a, Vec3.Cross(b, c));
        var den = la * lb * lc + Vec3.Dot(a, b) * lc + Vec3.Dot(a, c) * lb + Vec3.Dot(b, c) * la;
        return 2 * Math.Atan2(num, den) / (4 * Math.PI);
    }
}
