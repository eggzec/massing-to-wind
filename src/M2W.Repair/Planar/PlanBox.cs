using System.Globalization;

namespace M2W.Repair.Planar;

/// <summary>A plan bounding box [xmin, ymin, xmax, ymax] in local coordinates, for locating report entries.</summary>
public readonly record struct PlanBox(double X0, double Y0, double X1, double Y1)
{
    public static readonly PlanBox Empty = new(double.PositiveInfinity, double.PositiveInfinity,
        double.NegativeInfinity, double.NegativeInfinity);

    public bool IsEmpty => X0 > X1 || Y0 > Y1;

    public static PlanBox Of(IEnumerable<P2> points)
    {
        var b = Empty;
        foreach (var p in points) b = b.With(p);
        return b;
    }

    public PlanBox With(P2 p) => new(Math.Min(X0, p.X), Math.Min(Y0, p.Y), Math.Max(X1, p.X), Math.Max(Y1, p.Y));

    public PlanBox Union(PlanBox o) =>
        new(Math.Min(X0, o.X0), Math.Min(Y0, o.Y0), Math.Max(X1, o.X1), Math.Max(Y1, o.Y1));

    /// <summary>The box for report.json, to 3 decimals (millimetres).</summary>
    public double[] ToArray() => new[] { R(X0), R(Y0), R(X1), R(Y1) };

    /// <summary>The box as text, e.g. <c>[12.5, 3, 18.25, 9]</c>.</summary>
    public override string ToString() =>
        "[" + string.Join(", ", ToArray().Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))) + "]";

    private static double R(double v)
    {
        var r = Math.Round(v, 3, MidpointRounding.ToEven);
        return r == 0 ? 0 : r;
    }
}
