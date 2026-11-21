using M2W.Geometry;

namespace M2W.Checker;

/// <summary>
/// One output object (<c>o shell_NNN</c>) as a list of triangles over the mesh's shared vertices.
/// Topology is by vertex index: two vertices at the same position but with different indices are different
/// vertices (welding is the solver's job; see DECISIONS.md).
/// </summary>
public sealed class Shell
{
    public Shell(string name, IReadOnlyList<Vec3> vertices, IReadOnlyList<(int A, int B, int C)> triangles)
    {
        Name = name;
        Vertices = vertices;
        Triangles = triangles;
        Tris = triangles.Select(t => new Tri(vertices[t.A], vertices[t.B], vertices[t.C])).ToArray();
        TriBounds = Tris.Select(t => t.Bounds).ToArray();
        Bounds = TriBounds.Count == 0 ? new Box(Vec3.Zero, Vec3.Zero) : TriBounds.Aggregate(Box.Union);
    }

    public string Name { get; }
    public IReadOnlyList<Vec3> Vertices { get; }
    public IReadOnlyList<(int A, int B, int C)> Triangles { get; }
    public IReadOnlyList<Tri> Tris { get; }
    public IReadOnlyList<Box> TriBounds { get; }
    public Box Bounds { get; }

    public IEnumerable<int> UsedVertices => Triangles.SelectMany(t => new[] { t.A, t.B, t.C }).Distinct();

    public double SignedVolume
    {
        get
        {
            if (Tris.Count == 0) return 0;
            var c = (Bounds.Min + Bounds.Max) * 0.5; // keeps precision away from the origin
            double six = 0;
            foreach (var t in Tris) six += Vec3.Dot(t.A - c, Vec3.Cross(t.B - c, t.C - c));
            return six / 6.0;
        }
    }

    /// <summary>
    /// Winding number of the shell around <paramref name="p"/>: ≈1 inside a closed outward shell, ≈0 outside.
    /// </summary>
    public double Winding(Vec3 p)
    {
        double w = 0;
        foreach (var t in Tris) w += Geom.SolidAngleFraction(p, t);
        return w;
    }

    /// <summary>Distance from <paramref name="p"/> to the shell's surface.</summary>
    public double SurfaceDistance(Vec3 p, double stopBelow = 0)
    {
        var best = double.PositiveInfinity;
        for (var i = 0; i < Tris.Count; i++)
        {
            if (Box.Distance(TriBounds[i], p) >= best) continue;
            best = Math.Min(best, Geom.PointTriangleDistance(p, Tris[i]));
            if (best <= stopBelow) break;
        }
        return best;
    }

    /// <summary>
    /// Smallest barycentric weight that still counts as inside a triangle in <see cref="ContainsInPlan"/>. A point
    /// exactly on an edge two triangles share (e.g. a rectangle's centre on the diagonal of its split) can get a
    /// weight of about −1e-17 in both from rounding, so "≥ 0" would miss it in both (Q-S1, D-038). Weights have no
    /// units, so the bound is absolute; in length it is 1e-9 × the triangle's height over that edge.
    /// </summary>
    public const double PlanWeightTol = 1e-9;

    /// <summary>
    /// True if the plan point (x, y) lies under or over the shell: some triangle's projection onto the ground
    /// covers it (edges included, within <see cref="PlanWeightTol"/>). For a closed shell this is "inside its plan
    /// outline".
    /// </summary>
    public bool ContainsInPlan(double x, double y)
    {
        for (var i = 0; i < Tris.Count; i++)
        {
            var b = TriBounds[i];
            // Prune by box, padded so it never rejects a point the weight test accepts: with weights >= −t (at most
            // two negative), a point lies at most 2t × the box extent outside the box on each axis.
            var px = 2 * PlanWeightTol * (b.Max.X - b.Min.X);
            var py = 2 * PlanWeightTol * (b.Max.Y - b.Min.Y);
            if (x < b.Min.X - px || x > b.Max.X + px || y < b.Min.Y - py || y > b.Max.Y + py) continue;
            var t = Tris[i];
            var d = (t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X);
            if (d == 0) continue; // vertical triangle (wall): covers no plan area
            var u = ((t.B.X - x) * (t.C.Y - y) - (t.B.Y - y) * (t.C.X - x)) / d;
            var v = ((t.C.X - x) * (t.A.Y - y) - (t.C.Y - y) * (t.A.X - x)) / d;
            var w = 1 - u - v;
            if (u >= -PlanWeightTol && v >= -PlanWeightTol && w >= -PlanWeightTol) return true;
        }
        return false;
    }

    /// <summary>Distance from <paramref name="p"/> to the solid the shell encloses (0 on or inside it).</summary>
    public double SolidDistance(Vec3 p)
    {
        var d = SurfaceDistance(p, Geom.TouchTol);
        if (d <= Geom.TouchTol) return 0;
        if (Box.Distance(Bounds, p) > 0) return d; // outside the box, so outside the solid
        return Math.Abs(Winding(p)) > 0.5 ? 0 : d;
    }
}
