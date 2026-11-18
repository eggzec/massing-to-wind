namespace M2W.Generator;

/// <summary>
/// A plot: convex site polygon (rectangle, irregular quadrilateral, acute-angled trapezoid or corner-cut pentagon),
/// its inward setback, and the grid frame the massing is aligned to (the direction of the longest site edge, as a
/// street frontage would set it). All in local plan coordinates (metres).
/// </summary>
public sealed class Site
{
    public required string Shape { get; init; }
    public required List<P2> Polygon { get; init; }
    public required double SetbackDistance { get; init; }
    public required List<P2> Buildable { get; init; }

    /// <summary>Unit vector of the grid frame's u axis in plan coordinates; v is u turned 90° anticlockwise.</summary>
    public required P2 Eu { get; init; }

    public P2 Ev => new(-Eu.Y, Eu.X);

    public P2 ToPlan(double u, double v) => new(u * Eu.X + v * Ev.X, u * Eu.Y + v * Ev.Y);

    public P2 ToGrid(P2 p) => new(P2.Dot(p, Eu), P2.Dot(p, Ev));

    public bool Aligned => Eu.Y == 0 || Eu.X == 0;

    /// <summary>Makes a site of roughly <paramref name="w"/> × <paramref name="h"/> (before shaping).</summary>
    public static Site Make(Rng r, double w, double h, bool aligned)
    {
        var shape = r.Pick(new[] { "rectangle", "rectangle", "irregular", "acute", "cornerCut" });
        List<P2> poly;
        switch (shape)
        {
            case "irregular":
            {
                // Corners pushed outward by up to 15 %: no right angles, still convex.
                poly = new List<P2>
                {
                    new(-r.Step(0, 0.15 * w, 0.1), -r.Step(0, 0.15 * h, 0.1)),
                    new(w + r.Step(0, 0.15 * w, 0.1), -r.Step(0, 0.15 * h, 0.1)),
                    new(w + r.Step(0, 0.15 * w, 0.1), h + r.Step(0, 0.15 * h, 0.1)),
                    new(-r.Step(0, 0.15 * w, 0.1), h + r.Step(0, 0.15 * h, 0.1)),
                };
                break;
            }
            case "acute":
            {
                // Trapezoid with one acute corner (about 30° to 60°) on the long street edge.
                var a = r.Step(0.6 * h, 1.7 * h, 0.1);
                poly = r.Chance(0.5)
                    ? new List<P2> { new(0, 0), new(w + a, 0), new(w, h), new(0, h) }
                    : new List<P2> { new(-a, 0), new(w, 0), new(w, h), new(0, h) };
                break;
            }
            case "cornerCut":
            {
                var c1 = r.Step(0.2 * w, 0.4 * w, 0.1);
                var c2 = r.Step(0.2 * h, 0.4 * h, 0.1);
                w = Math.Round(w * 1.15, 1);
                h = Math.Round(h * 1.15, 1);
                poly = new List<P2> { new(0, 0), new(w, 0), new(w, h - c2), new(w - c1, h), new(0, h) };
                break;
            }
            default:
                poly = new List<P2> { new(0, 0), new(w, 0), new(w, h), new(0, h) };
                break;
        }
        if (!Convex.IsConvexCcw(poly))
        {
            shape = "rectangle";
            poly = new List<P2> { new(0, 0), new(w, 0), new(w, h), new(0, h) };
        }

        // Turn the plot (unless aligned) and move it off the origin.
        var (c, s) = aligned ? (1.0, 0.0) : r.Direction();
        var t = new P2(r.Step(-40, 40, 0.1), r.Step(-40, 40, 0.1));
        poly = poly.Select(p => new P2(p.X * c - p.Y * s, p.X * s + p.Y * c) + t).ToList();

        // Grid frame: direction of the longest edge.
        var best = 0;
        double bestLen = -1;
        for (var i = 0; i < poly.Count; i++)
        {
            var len = (poly[(i + 1) % poly.Count] - poly[i]).Length;
            if (len > bestLen + 1e-9) { bestLen = len; best = i; }
        }
        var e = poly[(best + 1) % poly.Count] - poly[best];
        var eu = new P2(e.X / bestLen, e.Y / bestLen);
        if (Math.Abs(e.Y) == 0) eu = new P2(Math.Sign(e.X), 0);
        else if (Math.Abs(e.X) == 0) eu = new P2(0, Math.Sign(e.Y));

        var setback = r.Step(2, 6, 0.5);
        return new Site
        {
            Shape = shape,
            Polygon = poly,
            SetbackDistance = setback,
            Buildable = Convex.Setback(poly, setback),
            Eu = eu,
        };
    }

    /// <summary>
    /// Places groups inside the buildable area, at least <paramref name="minSeparation"/> apart, by seeded rejection
    /// sampling on a 0.1 m grid. Returns the offsets (grid frame) or null if they do not fit.
    /// </summary>
    public List<(double U, double V)>? Place(Rng r, IReadOnlyList<Group> groups, double minSeparation)
    {
        if (Buildable.Count < 3) return null;
        var inner = Buildable.Select(ToGrid).ToList();
        double u0 = inner.Min(p => p.X), u1 = inner.Max(p => p.X), v0 = inner.Min(p => p.Y), v1 = inner.Max(p => p.Y);
        var placed = new List<List<Rect>>();
        var offsets = new (double, double)[groups.Count];
        // Largest first: it is the hardest to fit.
        var order = Enumerable.Range(0, groups.Count).OrderByDescending(i => groups[i].Bounds.Area).ThenBy(i => i).ToList();
        foreach (var gi in order)
        {
            var g = groups[gi];
            var b = g.Bounds;
            var rects = g.Expected.SelectMany(x => x.Rects).ToList();
            var ok = false;
            for (var attempt = 0; attempt < 400 && !ok; attempt++)
            {
                if (u1 - u0 < b.Width || v1 - v0 < b.Depth) return null;
                var du = r.Step(u0, u1 - b.Width, 0.1);
                var dv = r.Step(v0, v1 - b.Depth, 0.1);
                var moved = rects.Select(x => x.Translate(du, dv)).ToList();
                if (!moved.All(x => x.Corners().All(p => Convex.Contains(inner, p)))) continue;
                if (placed.Any(other => other.Any(o => moved.Any(m => m.DistanceTo(o) < minSeparation)))) continue;
                placed.Add(moved);
                offsets[gi] = (du, dv);
                ok = true;
            }
            if (!ok) return null;
        }
        return offsets.ToList();
    }
}
