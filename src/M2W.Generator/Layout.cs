namespace M2W.Generator;

/// <summary>How the blocks of a group relate. Decides the expected merge result.</summary>
public enum Relation
{
    Single,
    Overlap,
    Touching,

    /// <summary>Gap narrower than minGap: closed, the blocks merge.</summary>
    GapBelow,

    /// <summary>Gap wider than minGap: left open, the blocks stay separate.</summary>
    GapAbove,

    /// <summary>Touching or overlapping blocks, or a block and its own set-back tier, whose roofs differ by a small
    /// step (<see cref="Group.Step"/>). Blocks merge; below minEdge the lower roof is raised to the higher one.</summary>
    RoofStep,
}

/// <summary>
/// Blocks placed together. <see cref="Clean"/> is the designed model (what is emitted before faults);
/// <see cref="Expected"/> is the corrected model (differs only where a gap below minGap is closed).
/// </summary>
public sealed class Group
{
    public Group(Relation relation, IEnumerable<Block> clean, IEnumerable<Block>? expected = null)
    {
        Relation = relation;
        Clean = clean.ToList();
        Expected = (expected ?? Clean).ToList();
    }

    public Relation Relation { get; }
    public List<Block> Clean { get; private set; }
    public List<Block> Expected { get; private set; }
    public double Gap { get; init; }

    /// <summary>roofStep: the designed height difference between the two touching roofs (always &gt; 0).</summary>
    public double Step { get; init; }

    /// <summary>roofStep: the lower and the higher of the two roofs.</summary>
    public (double Low, double High) Roofs { get; init; }
    public string Variant { get; init; } = "";

    public Rect Bounds => Rect.Bounds(Expected.SelectMany(b => b.Rects));

    public void Transform(Func<Block, Block> f)
    {
        Clean = Clean.Select(f).ToList();
        Expected = Expected.Select(f).ToList();
    }

    /// <summary>Moves the group so its bounding box starts at (0, 0).</summary>
    public void Normalise()
    {
        var b = Bounds;
        Transform(x => x.Translate(-b.U0, -b.V0));
    }
}

/// <summary>Builds groups of blocks with a deliberate relation (overlap, touching walls, gaps either side of minGap).</summary>
public static class Layouts
{
    public static Group Single(Rng r, DesignRules d, string? kind = null)
    {
        var b = kind is null ? Shapes.Random(r, d) : Shapes.Make(kind, r, d);
        var g = new Group(Relation.Single, new[] { b }) { Variant = b.Kind };
        var quarter = r.Int(0, 4);
        g.Transform(x => x.Rotate90(quarter));
        g.Normalise();
        return g;
    }

    /// <summary>Snaps a coordinate to a reference if it is closer than the minimum feature size (flush instead of a
    /// tiny step), so overlapping blocks do not create edges shorter than a real design would have.</summary>
    private static double Snap(double x, double min, params double[] refs)
    {
        foreach (var c in refs)
            if (Math.Abs(x - c) < min) return c;
        return x;
    }

    /// <summary>Two blocks that interpenetrate: tower through a podium, crossing bars, or a corner overlap.</summary>
    public static Group Overlap(Rng r, DesignRules d)
    {
        var mf = d.MinFeature;
        var variant = r.Pick(new[] { "towerThroughPodium", "crossingBars", "cornerOverlap" });
        Block a, b;
        switch (variant)
        {
            case "towerThroughPodium":
            {
                var w = r.Step(30, 60, 0.1);
                var h = r.Step(22, 40, 0.1);
                a = new Block("bar", new[] { new Rect(0, 0, w, h) }, Shapes.Floors(r, r.IntIn(2, 5)));
                var tw = r.Step(15, Math.Min(26, w - mf), 0.1);
                var td = r.Step(15, Math.Min(24, h), 0.1);
                // Anywhere from fully inside to half outside, on a 0.1 m grid.
                var u0 = r.Step(-tw / 2, w - tw / 2, 0.1);
                var v0 = r.Step(-td / 3, h - td * 2 / 3, 0.1);
                u0 = Snap(u0, mf, 0, Math.Round(w - tw, 6));
                v0 = Snap(v0, mf, 0, Math.Round(h - td, 6));
                b = new Block("tower", new[] { new Rect(u0, v0, Math.Round(u0 + tw, 6), Math.Round(v0 + td, 6)) },
                    Shapes.Floors(r, r.IntIn(10, 28)));
                break;
            }
            case "crossingBars":
            {
                var len = r.Step(30, 70, 0.1);
                var dep = r.Step(10, 16, 0.1);
                a = new Block("bar", new[] { new Rect(0, 0, len, dep) }, Shapes.Floors(r, r.IntIn(3, 9)));
                var bw = r.Step(10, 16, 0.1);
                var u0 = r.Step(mf, len - bw - mf, 0.1);
                var below = r.Chance(0.6) ? r.Step(mf, 20, 0.1) : 0;
                var above = r.Step(mf, 24, 0.1);
                b = new Block("bar", new[] { new Rect(u0, Math.Round(-below, 6), Math.Round(u0 + bw, 6), Math.Round(dep + above, 6)) },
                    Shapes.Floors(r, r.IntIn(3, 12)));
                b = Shapes.MaybeSetbacks(b, r, d);
                break;
            }
            default:
            {
                a = r.Chance(0.5) ? Shapes.Bar(r, d) : Shapes.Tower(r, d);
                var ab = a.Bounds;
                var bw = r.Step(12, 30, 0.1);
                var bd = r.Step(10, 24, 0.1);
                var ou = r.Step(mf, Math.Min(bw, ab.Width) - mf, 0.1);
                var ov = r.Step(mf, Math.Min(bd, ab.Depth) - mf, 0.1);
                var u0 = Math.Round(ab.U1 - ou, 6);
                var v0 = Math.Round(ab.V1 - ov, 6);
                b = new Block("bar", new[] { new Rect(u0, v0, Math.Round(u0 + bw, 6), Math.Round(v0 + bd, 6)) },
                    Shapes.Floors(r, r.IntIn(3, 14)));
                break;
            }
        }
        var g = new Group(Relation.Overlap, r.Chance(0.5) ? new[] { a, b } : new[] { b, a }) { Variant = variant };
        var quarter = r.Int(0, 4);
        g.Transform(x => x.Rotate90(quarter));
        g.Normalise();
        return g;
    }

    /// <summary>
    /// The east-facing straight wall segments of a block at its bounding box edge: the v-ranges of rectangles whose
    /// east edge is the block's east edge and that no tier cuts back (so the wall is full height there).
    /// </summary>
    private static List<(double V0, double V1)> EastFaces(Block a)
    {
        var u1 = a.Bounds.U1;
        var list = new List<(double, double)>();
        foreach (var rect in a.Rects)
            if (rect.U1 == u1) list.Add((rect.V0, rect.V1));
        return list;
    }

    /// <summary>A block whose east side is one straight wall: bar, tower or L (east wall of the bar).</summary>
    private static Block FacingBlock(Rng r, DesignRules d, bool allowSetbacks)
    {
        var kind = r.Pick(new[] { "bar", "bar", "tower", "L" });
        var a = Shapes.Make(kind, r, d);
        if (!allowSetbacks) a = a.WithTiers(new[] { new Tier(a.FloorCount, Rect.Everything) });
        return a;
    }

    /// <summary>Blocks built wall to wall (shared, coincident wall planes): a pair, or a terrace of three.</summary>
    public static Group Touching(Rng r, DesignRules d)
    {
        var mf = d.MinFeature;
        var terrace = r.Chance(0.3);
        var a = FacingBlock(r, d, allowSetbacks: true);
        var blocks = new List<Block> { a };
        var count = terrace ? 3 : 2;
        var sameHeight = r.Chance(0.35);
        for (var k = 1; k < count; k++)
        {
            var prev = blocks[^1];
            var faces = EastFaces(prev);
            var (f0, f1) = r.Pick(faces);
            var depth = r.Step(10, 24, 0.1);
            var width = r.Step(12, 36, 0.1);
            var v0 = r.Step(f0 - depth + mf, f1 - mf, 0.1);
            v0 = Snap(v0, mf, f0, Math.Round(f1 - depth, 6));
            if (Math.Abs(v0 + depth - f0) < mf || Math.Abs(v0 + depth - f1) < mf) v0 = f0;
            var u0 = prev.Bounds.U1;
            var floors = sameHeight ? prev.FloorHeights : Shapes.Floors(r, r.IntIn(3, 12));
            var b = new Block("bar", new[] { new Rect(u0, v0, Math.Round(u0 + width, 6), Math.Round(v0 + depth, 6)) }, floors);
            if (!sameHeight) b = Shapes.MaybeSetbacks(b, r, d);
            blocks.Add(b);
        }
        var g = new Group(Relation.Touching, blocks) { Variant = terrace ? "terrace" : "pair" };
        var quarter = r.Int(0, 4);
        g.Transform(x => x.Rotate90(quarter));
        g.Normalise();
        return g;
    }

    /// <summary>
    /// Two blocks with parallel facing walls a gap apart. Below minGap the gap is closed in the expected model by
    /// extending the smaller block (B) up to the larger one (A); to keep that answer unambiguous B's facing wall lies
    /// within A's and is no taller. Above minGap the blocks stay separate.
    /// </summary>
    public static Group Gap(Rng r, DesignRules d, double gap, bool below)
    {
        var mf = d.MinFeature;
        for (var attempt = 0; ; attempt++)
        {
            var a = FacingBlock(r, d, allowSetbacks: !below);
            var faces = EastFaces(a);
            var (f0, f1) = r.Pick(faces);
            var span = f1 - f0;
            var u0 = Math.Round(a.Bounds.U1 + gap, 6);
            Block b;
            if (below)
            {
                var depth = r.Chance(0.4) ? span : r.Step(Math.Min(10, span), span, 0.1);
                var v0 = Math.Round(f0 + Shapes.Offset(r, Math.Round(span - depth, 6), mf), 6);
                var width = r.Step(10, 30, 0.1);
                // A's wall height along the face (A has no setbacks here, so it is A's height).
                var maxFloors = 0;
                var floors = Shapes.Floors(r, r.IntIn(2, 12));
                double z = 0;
                foreach (var h in floors)
                {
                    if (Math.Round(z + h, 6) > a.Height + 1e-9) break;
                    z = Math.Round(z + h, 6);
                    maxFloors++;
                }
                if (maxFloors < 2)
                {
                    if (attempt > 50) throw new InvalidOperationException("Could not build a gap pair.");
                    continue;
                }
                b = new Block("bar", new[] { new Rect(u0, v0, Math.Round(u0 + width, 6), Math.Round(v0 + depth, 6)) },
                    floors.Take(maxFloors));
                b = Shapes.MaybeSetbacks(b, r, d);
                var expectedB = b.MoveEdge(0, u0, a.Bounds.U1);
                var g = new Group(Relation.GapBelow, new[] { a, b }, new[] { a, expectedB }) { Gap = gap, Variant = "below" };
                var quarter = r.Int(0, 4);
                g.Transform(x => x.Rotate90(quarter));
                g.Normalise();
                return g;
            }
            else
            {
                var depth = r.Step(10, 24, 0.1);
                var v0 = r.Step(f0 - depth + mf, f1 - mf, 0.1);
                var width = r.Step(10, 30, 0.1);
                b = new Block("bar", new[] { new Rect(u0, v0, Math.Round(u0 + width, 6), Math.Round(v0 + depth, 6)) },
                    Shapes.Floors(r, r.IntIn(2, 12)));
                b = Shapes.MaybeSetbacks(b, r, d);
                var g = new Group(Relation.GapAbove, new[] { a, b }) { Gap = gap, Variant = "above" };
                var quarter = r.Int(0, 4);
                g.Transform(x => x.Rotate90(quarter));
                g.Normalise();
                return g;
            }
        }
    }

    private static Block NoSetbacks(Block b) => b.WithTiers(new[] { new Tier(b.FloorCount, Rect.Everything) });

    public static readonly IReadOnlyList<string> RoofStepKinds = new[] { "touching", "overlap", "tier" };

    /// <summary>
    /// roofStep (D-038): two roofs that touch in plan and differ by exactly <paramref name="step"/>.
    /// "touching": a second block built wall to wall with the first, its floors chosen so its roof is higher or
    /// lower by the step (a different ground-floor height). "overlap": crossing bars or a corner overlap with the same
    /// kind of height difference. "tier": one block whose top tier is a set-back roof deck only <paramref name="step"/>
    /// tall (a plant deck or upstand), cut back on one side or on every side. No other setbacks, so the group has
    /// exactly two roof heights.
    /// </summary>
    public static Group RoofStep(Rng r, DesignRules d, double step, string kind)
    {
        var mf = d.MinFeature;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            Group? g = null;
            var higher = r.Chance(0.5);
            switch (kind)
            {
                case "touching":
                {
                    var a = FacingBlock(r, d, allowSetbacks: false);
                    var (f0, f1) = r.Pick(EastFaces(a));
                    var depth = r.Step(10, 24, 0.1);
                    var width = r.Step(12, 36, 0.1);
                    var v0 = r.Step(f0 - depth + mf, f1 - mf, 0.1);
                    v0 = Snap(v0, mf, f0, Math.Round(f1 - depth, 6));
                    if (Math.Abs(v0 + depth - f0) < mf || Math.Abs(v0 + depth - f1) < mf) v0 = f0;
                    var floors = Shapes.FloorsToHeight(r, Math.Round(a.Height + (higher ? step : -step), 6));
                    if (floors is null) continue;
                    var u0 = a.Bounds.U1;
                    var b = new Block("bar", new[] { new Rect(u0, v0, Math.Round(u0 + width, 6), Math.Round(v0 + depth, 6)) }, floors);
                    g = new Group(Relation.RoofStep, r.Chance(0.5) ? new[] { a, b } : new[] { b, a }) { Variant = kind };
                    break;
                }
                case "overlap":
                {
                    Block a, b;
                    var cross = r.Chance(0.5);
                    if (cross)
                    {
                        var len = r.Step(30, 70, 0.1);
                        var dep = r.Step(10, 16, 0.1);
                        a = new Block("bar", new[] { new Rect(0, 0, len, dep) }, Shapes.Floors(r, r.IntIn(3, 9)));
                        var bw = r.Step(10, 16, 0.1);
                        var bu0 = r.Step(mf, len - bw - mf, 0.1);
                        var below = r.Chance(0.6) ? r.Step(mf, 20, 0.1) : 0;
                        var above = r.Step(mf, 24, 0.1);
                        var floors = Shapes.FloorsToHeight(r, Math.Round(a.Height + (higher ? step : -step), 6));
                        if (floors is null) continue;
                        b = new Block("bar", new[] { new Rect(bu0, Math.Round(-below, 6), Math.Round(bu0 + bw, 6), Math.Round(dep + above, 6)) }, floors);
                    }
                    else
                    {
                        a = NoSetbacks(Shapes.Bar(r, d));
                        var ab = a.Bounds;
                        var bw = r.Step(12, 30, 0.1);
                        var bd = r.Step(10, 24, 0.1);
                        var ou = r.Step(mf, Math.Min(bw, ab.Width) - mf, 0.1);
                        var ov = r.Step(mf, Math.Min(bd, ab.Depth) - mf, 0.1);
                        var bu0 = Math.Round(ab.U1 - ou, 6);
                        var bv0 = Math.Round(ab.V1 - ov, 6);
                        var floors = Shapes.FloorsToHeight(r, Math.Round(a.Height + (higher ? step : -step), 6));
                        if (floors is null) continue;
                        b = new Block("bar", new[] { new Rect(bu0, bv0, Math.Round(bu0 + bw, 6), Math.Round(bv0 + bd, 6)) }, floors);
                    }
                    g = new Group(Relation.RoofStep, r.Chance(0.5) ? new[] { a, b } : new[] { b, a }) { Variant = cross ? "overlap (crossing bars)" : "overlap (corner)" };
                    break;
                }
                default:
                {
                    var basis = NoSetbacks(Shapes.Make(r.Pick(new[] { "bar", "bar", "L", "tower" }), r, d));
                    var floors = basis.FloorHeights.Append(step).ToList();
                    var bounds = basis.Bounds;
                    Rect clip;
                    string where;
                    if (r.Chance(0.5))
                    {
                        var inset = r.Step(mf, Math.Max(mf, Math.Min(8, Math.Min(bounds.Width, bounds.Depth) / 4)), 0.1);
                        clip = new Rect(Math.Round(bounds.U0 + inset, 6), Math.Round(bounds.V0 + inset, 6),
                            Math.Round(bounds.U1 - inset, 6), Math.Round(bounds.V1 - inset, 6));
                        where = "every side";
                    }
                    else
                    {
                        var side = r.Int(0, 4);
                        var extent = side % 2 == 0 ? bounds.Width : bounds.Depth;
                        var depth = r.Step(mf, Math.Max(mf, Math.Min(8, extent / 4)), 0.1);
                        clip = side switch
                        {
                            0 => Rect.Everything with { U0 = Math.Round(bounds.U0 + depth, 6) },
                            1 => Rect.Everything with { V0 = Math.Round(bounds.V0 + depth, 6) },
                            2 => Rect.Everything with { U1 = Math.Round(bounds.U1 - depth, 6) },
                            _ => Rect.Everything with { V1 = Math.Round(bounds.V1 - depth, 6) },
                        };
                        where = new[] { "west", "south", "east", "north" }[side] + " side";
                    }
                    var b = new Block(basis.Kind, basis.Rects, floors,
                        new[] { new Tier(basis.FloorCount, Rect.Everything), new Tier(basis.FloorCount + 1, clip) });
                    g = new Group(Relation.RoofStep, new[] { b }) { Variant = $"tier ({where})" };
                    break;
                }
            }

            // The group must have exactly two roof heights, touching, a step apart.
            var hf = HeightField.Build(g.Clean);
            var heights = new SortedSet<double>();
            for (var i = 0; i < hf.Nx; i++)
                for (var j = 0; j < hf.Ny; j++)
                    if (hf.Top[i, j] > 0) heights.Add(hf.Top[i, j]);
            if (hf.ComponentCount != 1 || heights.Count != 2) continue;
            if (!hf.TouchingSteps().SetEquals(new[] { Math.Round(step, 6) })) continue;
            g = new Group(Relation.RoofStep, g.Clean) { Variant = g.Variant, Step = step, Roofs = (heights.Min, heights.Max) };
            var quarter = r.Int(0, 4);
            g.Transform(x => x.Rotate90(quarter));
            g.Normalise();
            return g;
        }
        throw new InvalidOperationException("Could not build a roofStep group.");
    }
}
