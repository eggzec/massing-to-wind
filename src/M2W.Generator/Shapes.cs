namespace M2W.Generator;

/// <summary>Size rules shared by the shape and layout generators for one case.</summary>
public sealed record DesignRules(double MinFeature, double MinSeparation);

/// <summary>
/// Footprint types a generative massing tool produces (bar, L, U, courtyard, tower, tower on podium), each extruded
/// floor by floor with realistic floor-to-floor heights and optional setbacks on upper floors. Shapes are built at
/// the origin of the grid frame; layouts move them into place.
/// </summary>
public static class Shapes
{
    public static readonly IReadOnlyList<string> Kinds = new[] { "bar", "L", "U", "courtyard", "tower", "podiumTower" };

    public static Block Make(string kind, Rng r, DesignRules d) => kind switch
    {
        "bar" => Bar(r, d),
        "L" => LShape(r, d),
        "U" => UShape(r, d),
        "courtyard" => Courtyard(r, d),
        "tower" => Tower(r, d),
        "podiumTower" => PodiumTower(r, d),
        _ => throw new ArgumentException($"Unknown kind {kind}"),
    };

    public static Block Random(Rng r, DesignRules d) =>
        Make(r.Pick(new[] { "bar", "bar", "L", "U", "courtyard", "tower", "podiumTower" }), r, d);

    /// <summary>Floor-to-floor heights: a taller ground floor, then typical floors.</summary>
    public static List<double> Floors(Rng r, int n)
    {
        var ground = r.Pick(new[] { 3.5, 4.0, 4.2, 4.5, 5.0 });
        var typical = r.Pick(new[] { 3.0, 3.1, 3.2, 3.3, 3.5 });
        if (r.Chance(0.25)) ground = typical;
        var list = new List<double> { ground };
        for (var i = 1; i < n; i++) list.Add(typical);
        return list;
    }

    public static Block Bar(Rng r, DesignRules d)
    {
        var len = r.Step(24, 72, 0.1);
        var dep = r.Step(10, 18, 0.1);
        var b = new Block("bar", new[] { new Rect(0, 0, len, dep) }, Floors(r, r.IntIn(3, 10)));
        return MaybeSetbacks(b, r, d);
    }

    public static Block LShape(Rng r, DesignRules d)
    {
        var len = r.Step(26, 60, 0.1);
        var dep = r.Step(10, 16, 0.1);
        var armW = r.Step(10, Math.Min(16, len - d.MinFeature - 8), 0.1);
        var armL = r.Step(Math.Max(8, d.MinFeature), 30, 0.1);
        var rects = new[] { new Rect(0, 0, len, dep), new Rect(0, dep, armW, Math.Round(dep + armL, 6)) };
        var b = new Block("L", rects, Floors(r, r.IntIn(3, 9)));
        return MaybeSetbacks(b, r, d);
    }

    public static Block UShape(Rng r, DesignRules d)
    {
        var a1 = r.Step(9, 14, 0.1);
        var a2 = r.Chance(0.5) ? a1 : r.Step(9, 14, 0.1);
        var inner = r.Step(Math.Max(10, 3 * d.MinFeature), 24, 0.1);
        var len = Math.Round(a1 + inner + a2, 6);
        var dep = r.Step(10, 15, 0.1);
        var l2 = r.Step(Math.Max(10, d.MinFeature), 26, 0.1);
        var l3 = r.Chance(0.5) ? l2 : Math.Round(l2 + r.Step(d.MinFeature, 10, 0.1), 6);
        var rects = new[]
        {
            new Rect(0, 0, len, dep),
            new Rect(0, dep, a1, Math.Round(dep + l2, 6)),
            new Rect(Math.Round(len - a2, 6), dep, len, Math.Round(dep + l3, 6)),
        };
        var b = new Block("U", rects, Floors(r, r.IntIn(3, 8)));
        return MaybeSetbacks(b, r, d);
    }

    public static Block Courtyard(Rng r, DesignRules d)
    {
        double ds = r.Step(8, 13, 0.1), dn = r.Chance(0.5) ? ds : r.Step(8, 13, 0.1);
        double dw = r.Chance(0.5) ? ds : r.Step(8, 13, 0.1), de = r.Chance(0.5) ? dw : r.Step(8, 13, 0.1);
        var innerW = r.Step(Math.Max(10, 3 * d.MinFeature), 30, 0.1);
        var innerD = r.Step(Math.Max(10, 3 * d.MinFeature), 26, 0.1);
        var w = Math.Round(dw + innerW + de, 6);
        var h = Math.Round(ds + innerD + dn, 6);
        var rects = new[]
        {
            new Rect(0, 0, w, ds),
            new Rect(0, Math.Round(h - dn, 6), w, h),
            new Rect(0, ds, dw, Math.Round(h - dn, 6)),
            new Rect(Math.Round(w - de, 6), ds, w, Math.Round(h - dn, 6)),
        };
        var b = new Block("courtyard", rects, Floors(r, r.IntIn(3, 8)));
        return MaybeSetbacks(b, r, d);
    }

    public static Block Tower(Rng r, DesignRules d)
    {
        var a = r.Step(15, 32, 0.1);
        var c = r.Chance(0.4) ? a : r.Step(15, 32, 0.1);
        var n = r.IntIn(12, 32);
        var b = new Block("tower", new[] { new Rect(0, 0, a, c) }, Floors(r, n));
        if (r.Chance(0.35))
        {
            // Crown: the top floors step in on every side.
            var inset = r.Step(d.MinFeature, Math.Max(d.MinFeature, Math.Min(a, c) / 4), 0.1);
            var crownFloors = r.IntIn(2, 4);
            b = b.WithTiers(new[]
            {
                new Tier(n - crownFloors, Rect.Everything),
                new Tier(n, new Rect(inset, inset, Math.Round(a - inset, 6), Math.Round(c - inset, 6))),
            });
        }
        return b;
    }

    public static Block PodiumTower(Rng r, DesignRules d)
    {
        var w = r.Step(30, 60, 0.1);
        var h = r.Step(24, 45, 0.1);
        var tw = r.Step(15, Math.Min(28, w - 2 * d.MinFeature), 0.1);
        var td = r.Step(15, Math.Min(26, h - 2 * d.MinFeature), 0.1);
        var u0 = Offset(r, w - tw, d.MinFeature);
        var v0 = Offset(r, h - td, d.MinFeature);
        var podium = r.IntIn(2, 4);
        var n = podium + r.IntIn(8, 24);
        var tiers = new[]
        {
            new Tier(podium, Rect.Everything),
            new Tier(n, new Rect(u0, v0, Math.Round(u0 + tw, 6), Math.Round(v0 + td, 6))),
        };
        return new Block("podiumTower", new[] { new Rect(0, 0, w, h) }, Floors(r, n), tiers);
    }

    /// <summary>Offset of a sub-range inside a range with <paramref name="slack"/> spare: flush at either end, or at
    /// least <paramref name="min"/> from both ends.</summary>
    public static double Offset(Rng r, double slack, double min)
    {
        var options = new List<double> { 0, Math.Round(slack, 6) };
        if (slack >= 2 * min) options.Add(r.Step(min, slack - min, 0.1));
        if (slack >= 2 * min) options.Add(r.Step(min, slack - min, 0.1));
        return r.Pick(options);
    }

    /// <summary>Upper-floor setbacks: 0, 1 or 2 tiers, each cutting a strip off one side.</summary>
    public static Block MaybeSetbacks(Block b, Rng r, DesignRules d)
    {
        var n = b.FloorCount;
        if (n < 4) return b;
        var tiers = r.NextDouble() switch { < 0.55 => 0, < 0.85 => 1, _ => 2 };
        if (tiers == 0) return b;
        var bounds = b.Bounds;
        var list = new List<Tier>();
        var start = r.IntIn(2, Math.Max(2, n - 2));
        var floors = new List<int> { start };
        if (tiers == 2 && start + 1 < n) floors.Add(r.IntIn(start + 1, n - 1));
        var clip = Rect.Everything;
        foreach (var f in floors)
        {
            list.Add(new Tier(f, clip));
            var side = r.Int(0, 4);
            var extent = side % 2 == 0 ? bounds.Width : bounds.Depth;
            var depth = r.Step(d.MinFeature, Math.Max(d.MinFeature, Math.Min(8, extent / 4)), 0.1);
            clip = side switch
            {
                0 => clip with { U0 = Math.Round(Math.Max(clip.U0, bounds.U0) + depth, 6) },
                1 => clip with { V0 = Math.Round(Math.Max(clip.V0, bounds.V0) + depth, 6) },
                2 => clip with { U1 = Math.Round(Math.Min(clip.U1, bounds.U1) - depth, 6) },
                _ => clip with { V1 = Math.Round(Math.Min(clip.V1, bounds.V1) - depth, 6) },
            };
        }
        list.Add(new Tier(n, clip));
        // Tier floors must increase; collapse duplicates if the draw produced any.
        var clean = new List<Tier>();
        foreach (var t in list)
        {
            if (clean.Count > 0 && t.TopFloor <= clean[^1].TopFloor) continue;
            clean.Add(t);
        }
        if (clean[^1].TopFloor != n) return b;
        return b.WithTiers(clean);
    }
}
